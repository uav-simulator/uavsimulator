using UnityEngine;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// A trigger collider that exposes the state of an attached <see cref="TrafficLight"/>.
    /// Vehicles raycast forward, look for this component on the hit object, and
    /// query <see cref="CurrentState"/> to decide whether to brake.
    ///
    /// If no light is wired, defaults to Green (fail-open: don't brake unnecessarily).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class TrafficLightTriggerZone : MonoBehaviour
    {
        [SerializeField] private TrafficLight trafficLight;

        public TrafficLightState CurrentState
            => trafficLight != null ? trafficLight.State : TrafficLightState.Green;

        /// <summary>Test-only seam to wire the light without going through the inspector.</summary>
        public void SetTrafficLight(TrafficLight light) => trafficLight = light;
    }
}
