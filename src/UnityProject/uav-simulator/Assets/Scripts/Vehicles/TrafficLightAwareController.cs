using UavSimulator.CityDemo;
using System;
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
        private const float DefaultBrakeStartDistanceM = 2f;

        public float lookAheadDistance = 15f;
        public float brakeStartDistance = DefaultBrakeStartDistanceM;
        public LayerMask trafficLightLayerMask = ~0;

        /// <summary>
        /// Optional override for tests / non-raycast probing. If non-null, the
        /// component returns the brake decision for this zone instead of
        /// performing a physics raycast. Set via <see cref="SetProbeOverride"/>.
        /// </summary>
        private TrafficLightTriggerZone probeOverride;
        private float probeOverrideDistanceM;

        /// <summary>
        /// Returns whether the vehicle should brake and the recommended intensity:
        ///   Red    → 1.0
        ///   Yellow → 0.5
        ///   Green or no zone → 0.0 (and returns false)
        /// </summary>
        public bool ShouldBrake(out float brakeIntensity)
        {
            if (!TryResolveZoneAhead(out var zone, out var distanceM))
            {
                brakeIntensity = 0f;
                return false;
            }

            return EvaluateZone(zone, distanceM, brakeStartDistance, out brakeIntensity);
        }

        /// <summary>
        /// Test seam: provide a zone to evaluate directly, bypassing the raycast.
        /// Pass <c>null</c> to clear the override and use the raycast path.
        /// </summary>
        public void SetProbeOverride(TrafficLightTriggerZone zone)
        {
            probeOverride = zone;
            probeOverrideDistanceM = 0f;
        }

        public void SetProbeOverride(TrafficLightTriggerZone zone, float distanceM)
        {
            probeOverride = zone;
            probeOverrideDistanceM = Mathf.Max(0f, distanceM);
        }

        private bool TryResolveZoneAhead(out TrafficLightTriggerZone zone, out float distanceM)
        {
            if (probeOverride != null)
            {
                zone = probeOverride;
                distanceM = probeOverrideDistanceM;
                return true;
            }

            var origin = transform.position;
            var direction = transform.forward;
            var overlapping = Physics.OverlapSphere(
                origin,
                0.25f,
                trafficLightLayerMask,
                QueryTriggerInteraction.Collide);
            var overlappingZone = ResolveNearestZone(overlapping, origin);
            if (overlappingZone != null)
            {
                zone = overlappingZone;
                distanceM = 0f;
                return true;
            }

            var hits = Physics.RaycastAll(
                origin,
                direction,
                lookAheadDistance,
                trafficLightLayerMask,
                QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0)
            {
                zone = null;
                distanceM = -1f;
                return false;
            }

            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                var hitZone = ResolveZone(hit.collider);
                if (hitZone != null)
                {
                    zone = hitZone;
                    distanceM = Mathf.Max(0f, hit.distance);
                    return true;
                }
            }

            zone = null;
            distanceM = -1f;
            return false;
        }

        private static TrafficLightTriggerZone ResolveNearestZone(Collider[] colliders, Vector3 origin)
        {
            TrafficLightTriggerZone nearest = null;
            var nearestDistanceSq = float.PositiveInfinity;
            for (var i = 0; i < colliders.Length; i++)
            {
                var zone = ResolveZone(colliders[i]);
                if (zone == null)
                {
                    continue;
                }

                var distanceSq = (zone.transform.position - origin).sqrMagnitude;
                if (distanceSq < nearestDistanceSq)
                {
                    nearest = zone;
                    nearestDistanceSq = distanceSq;
                }
            }

            return nearest;
        }

        private static TrafficLightTriggerZone ResolveZone(Collider collider)
            => collider.GetComponent<TrafficLightTriggerZone>()
               ?? collider.GetComponentInParent<TrafficLightTriggerZone>();

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
            if (!TryResolveZoneAhead(out var zone, out var distanceM) || zone == null)
            {
                return new NearestLightSnapshot(false, "None", -1f);
            }

            return new NearestLightSnapshot(true, zone.CurrentState.ToString(), distanceM);
        }

        /// <summary>
        /// Pure decision function. <c>internal</c> so the test assembly can call it
        /// directly without staging a Collider.
        /// </summary>
        internal static bool EvaluateZone(TrafficLightTriggerZone zone, out float brakeIntensity)
            => EvaluateZone(zone, 0f, DefaultBrakeStartDistanceM, out brakeIntensity);

        internal static bool EvaluateZone(
            TrafficLightTriggerZone zone,
            float distanceM,
            float brakeStartDistanceM,
            out float brakeIntensity)
        {
            if (zone == null)
            {
                brakeIntensity = 0f;
                return false;
            }

            if (distanceM > Mathf.Max(0f, brakeStartDistanceM))
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
