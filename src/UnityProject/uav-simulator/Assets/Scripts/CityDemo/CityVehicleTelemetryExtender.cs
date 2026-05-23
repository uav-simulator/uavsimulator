using System.Collections.Generic;
using UnityEngine;
using UavSimulator.Contracts;
using UavSimulator.Vehicles;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// Publishes nearest-traffic-light ground-truth into VehicleState.extensions
    /// every telemetry tick. Consumed by auto_label.py (Plan B Task 5).
    /// </summary>
    [RequireComponent(typeof(TrafficLightAwareController))]
    public sealed class CityVehicleTelemetryExtender : MonoBehaviour, IVehicleStateExtender
    {
        private TrafficLightAwareController controller;

        private void Awake()
        {
            controller = GetComponent<TrafficLightAwareController>();
        }

        public IEnumerable<ConfigKeyValue> BuildExtensions()
        {
            if (controller == null) yield break;
            var snap = controller.GetNearestLightSnapshot();
            yield return new ConfigKeyValue { key = "nearestTrafficLight.hasLight", value = snap.hasLight ? "true" : "false" };
            yield return new ConfigKeyValue { key = "nearestTrafficLight.state",    value = snap.state };
            yield return new ConfigKeyValue { key = "nearestTrafficLight.distanceM", value = snap.distanceM.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) };
        }
    }
}
