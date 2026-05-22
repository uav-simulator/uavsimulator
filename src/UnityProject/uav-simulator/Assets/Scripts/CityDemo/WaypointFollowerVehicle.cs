using UnityEngine;
using UavSimulator.Contracts;
using UavSimulator.Vehicles;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// AI vehicle controller that follows a CityWaypointGraph.
    /// On each FixedUpdate: steers toward current target waypoint via PD,
    /// throttles to maintain target speed, brakes if TrafficLightAwareController says so.
    /// Switches to next waypoint when reach distance achieved; on intersection chooses random outgoing edge.
    /// </summary>
    public sealed class WaypointFollowerVehicle : VehicleBase
    {
        [Header("Graph reference")]
        [SerializeField] private CityWaypointGraph graph;
        [SerializeField] private string startNodeId; // optional; if empty, picked at runtime

        [Header("Path-following parameters")]
        [SerializeField] private float reachDistanceM = 1.5f;
        [SerializeField] private float defaultSpeedMps = 4.0f;
        [SerializeField] private float maxSteer = 1.0f;
        [SerializeField] private float steerKp = 1.5f;
        [SerializeField] private float steerKd = 0.4f;
        [SerializeField] private float intersectionSlowdownFactor = 0.6f;

        [Header("Wiring")]
        // Generic gate slot: drag any IMovementGate implementer (ground-truth
        // TrafficLightAwareController or ONNX-based OnnxTrafficLightAwareController).
        // The MonoBehaviour-typed field keeps inspector drag-and-drop working
        // without binding to a concrete type.
        [SerializeField] private MonoBehaviour gateComponent;
        // Deprecated, kept only so existing prefabs / scenes that already wired
        // a TrafficLightAwareController continue to deserialize and behave.
        // New wiring should use <see cref="gateComponent"/>.
        [SerializeField] private TrafficLightAwareController trafficAware;
        [SerializeField] private Rigidbody body;

        private IMovementGate gate;

        // Public API for inspection / debugging
        public string CurrentNodeId { get; private set; }
        public string TargetNodeId { get; private set; }
        public float TargetSpeedMps { get; private set; }

        private System.Random rng;
        private CityWaypointNode current;
        private CityWaypointNode target;
        private float prevHeadingError;

        private void Awake()
        {
            // Init RNG with deterministic seed if ResetVehicle called.
            rng = new System.Random();
            ResolveBody();
            ResolveGate();
        }

        public override void ResetVehicle(int seed)
        {
            rng = new System.Random(seed);
            current = ResolveStartNode();
            target = PickNextTarget(current);
            prevHeadingError = 0f;
            if (current != null && body != null)
            {
                body.position = current.position;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        public override void ApplyControl(ControlCommand command)
        {
            // External control commands ignored — this vehicle is autonomous.
            // (TrafficLightAwareController and waypoint logic produce all motion.)
        }

        private void FixedUpdate()
        {
            if (graph == null || target == null || body == null) return;

            // Advance to next waypoint if close enough.
            var distance = Vector3.Distance(body.position, target.position);
            if (distance <= reachDistanceM)
            {
                current = target;
                target = PickNextTarget(current);
                if (target == null) return;
            }

            // Compute desired speed (slow down before intersection).
            var edgeSpeed = FindEdgeSpeed(current, target);
            var baseSpeed = edgeSpeed > 0.01f ? edgeSpeed : defaultSpeedMps;
            var desired = target != null && target.isIntersection ? baseSpeed * intersectionSlowdownFactor : baseSpeed;
            TargetSpeedMps = desired;

            // Traffic light override via the IMovementGate abstraction (either
            // ground-truth raycast or ONNX-classifier implementation).
            float brake = 0f;
            if (gate != null && gate.ShouldBrake(out var ti)) brake = ti;

            // Steering: PD on heading error.
            var toTarget = target.position - body.position;
            toTarget.y = 0f;
            var desiredYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            var currentYaw = body.rotation.eulerAngles.y;
            var headingError = Mathf.DeltaAngle(currentYaw, desiredYaw);
            var steerSignal = Mathf.Clamp(steerKp * headingError + steerKd * (headingError - prevHeadingError), -maxSteer, maxSteer);
            prevHeadingError = headingError;

            // Throttle: simple cruise.
            var currentSpeed = Vector3.Dot(body.linearVelocity, body.transform.forward);
            var throttle = brake > 0.01f ? 0f : Mathf.Clamp01(desired - currentSpeed);

            // Apply to physics (simple direct force/torque application).
            body.AddForce(body.transform.forward * throttle * 5f, ForceMode.Acceleration);
            body.AddTorque(Vector3.up * (steerSignal / 57.3f) * 8f, ForceMode.Acceleration);
            if (brake > 0.01f)
            {
                body.linearVelocity *= Mathf.Max(0f, 1f - brake * Time.fixedDeltaTime * 4f);
            }

            CurrentNodeId = current?.id;
            TargetNodeId = target?.id;
        }

        // ── Internals ──

        private CityWaypointNode ResolveStartNode()
        {
            if (graph == null) return null;
            if (!string.IsNullOrEmpty(startNodeId))
            {
                var n = graph.FindNode(startNodeId);
                if (n != null) return n;
            }
            // Fallback: random non-intersection node.
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                var idx = rng.Next(0, graph.nodes.Count);
                var n = graph.nodes[idx];
                if (!n.isIntersection && n.outgoingEdges.Count > 0) return n;
            }
            return graph.nodes.Count > 0 ? graph.nodes[0] : null;
        }

        private CityWaypointNode PickNextTarget(CityWaypointNode from)
        {
            if (from == null || from.outgoingEdges.Count == 0) return null;
            var idx = rng.Next(0, from.outgoingEdges.Count);
            var edge = from.outgoingEdges[idx];
            return graph.FindNode(edge.toNodeId);
        }

        private float FindEdgeSpeed(CityWaypointNode from, CityWaypointNode to)
        {
            if (from == null || to == null) return 0f;
            foreach (var e in from.outgoingEdges)
            {
                if (e.toNodeId == to.id) return e.speedLimitMps;
            }
            return 0f;
        }

        private void ResolveBody() { if (body == null) body = GetComponent<Rigidbody>(); }

        /// <summary>
        /// Resolve <see cref="gate"/> in priority order:
        ///   1. Inspector-assigned <see cref="gateComponent"/> if it implements <see cref="IMovementGate"/>.
        ///   2. Deprecated <see cref="trafficAware"/> field (back-compat with existing prefabs).
        ///   3. Any <see cref="TrafficLightAwareController"/> on this GameObject.
        ///   4. Any <see cref="OnnxTrafficLightAwareController"/> on this GameObject.
        /// </summary>
        private void ResolveGate()
        {
            if (gateComponent != null) gate = gateComponent as IMovementGate;
            if (gate == null && trafficAware != null) gate = trafficAware;
            if (gate == null) gate = GetComponent<TrafficLightAwareController>();
            if (gate == null) gate = GetComponent<OnnxTrafficLightAwareController>();
        }

        // Test-only seam (assemblies have InternalsVisibleTo via reflection workaround in EditMode tests).
        public void __TestSetGraphAndStart(CityWaypointGraph g, string startId)
        {
            graph = g;
            startNodeId = startId;
            ResetVehicle(0);
        }
        public CityWaypointNode __TestPickNext(CityWaypointNode from) => PickNextTarget(from);
    }
}
