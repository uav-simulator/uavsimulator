using UavSimulator.CityDemo;
using UnityEngine;

namespace UavSimulator.Vehicles
{
    /// <summary>
    /// Pure filter component: raycasts forward, looks for a
    /// <see cref="TrafficLightTriggerZone"/> within <see cref="lookAheadDistance"/>,
    /// and reports a recommended brake intensity. Does NOT apply control to the
    /// vehicle — that's an upstream controller's job (e.g. an Arcade or
    /// PROMETEO controller).
    /// </summary>
    public sealed class TrafficLightAwareController : MonoBehaviour, IMovementGate
    {
        public float lookAheadDistance = 15f;
        public LayerMask trafficLightLayerMask = ~0;

        /// <summary>
        /// Optional override for tests / non-raycast probing. If non-null, the
        /// component returns the brake decision for this zone instead of
        /// performing a physics raycast. Set via <see cref="SetProbeOverride"/>.
        /// </summary>
        private TrafficLightTriggerZone probeOverride;

        /// <summary>
        /// Returns whether the vehicle should brake and the recommended intensity:
        ///   Red    → 1.0
        ///   Yellow → 0.5
        ///   Green or no zone → 0.0 (and returns false)
        /// </summary>
        public bool ShouldBrake(out float brakeIntensity)
        {
            var zone = ResolveZoneAhead();
            return EvaluateZone(zone, out brakeIntensity);
        }

        /// <summary>
        /// Test seam: provide a zone to evaluate directly, bypassing the raycast.
        /// Pass <c>null</c> to clear the override and use the raycast path.
        /// </summary>
        public void SetProbeOverride(TrafficLightTriggerZone zone)
        {
            probeOverride = zone;
        }

        private TrafficLightTriggerZone ResolveZoneAhead()
        {
            if (probeOverride != null) return probeOverride;

            var origin = transform.position;
            var direction = transform.forward;
            if (Physics.Raycast(origin, direction, out var hit, lookAheadDistance,
                    trafficLightLayerMask, QueryTriggerInteraction.Collide))
            {
                return hit.collider.GetComponent<TrafficLightTriggerZone>()
                    ?? hit.collider.GetComponentInParent<TrafficLightTriggerZone>();
            }
            return null;
        }

        /// <summary>
        /// Snapshot of the nearest detectable traffic light's state and distance.
        /// Used by telemetry pipeline to publish ground-truth label for auto-labeling.
        /// </summary>
        public readonly struct NearestLightSnapshot
        {
            public readonly bool hasLight;
            public readonly string state;
            public readonly float distanceM;

            public NearestLightSnapshot(bool hasLight, string state, float distance)
            {
                this.hasLight = hasLight;
                this.state = state;
                this.distanceM = distance;
            }
        }

        public NearestLightSnapshot GetNearestLightSnapshot()
        {
            var zone = ResolveZoneAhead();
            if (zone == null)
            {
                return new NearestLightSnapshot(false, "None", -1f);
            }
            var dist = Vector3.Distance(transform.position, zone.transform.position);
            return new NearestLightSnapshot(true, zone.CurrentState.ToString(), dist);
        }

        /// <summary>
        /// Pure decision function. <c>internal</c> so the test assembly can call it
        /// directly without staging a Collider.
        /// </summary>
        internal static bool EvaluateZone(TrafficLightTriggerZone zone, out float brakeIntensity)
        {
            if (zone == null)
            {
                brakeIntensity = 0f;
                return false;
            }

            switch (zone.CurrentState)
            {
                case TrafficLightState.Red:
                    brakeIntensity = 1.0f;
                    return true;
                case TrafficLightState.Yellow:
                    brakeIntensity = 0.5f;
                    return true;
                case TrafficLightState.Green:
                default:
                    brakeIntensity = 0f;
                    return false;
            }
        }
    }
}
