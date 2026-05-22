using System.Collections.Generic;
using UnityEngine;
using UavSimulator.Vehicles;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// Sits next to a vehicle's TrafficLightAwareController and publishes nearest
    /// traffic-light state into VehicleState.extensions on every telemetry tick.
    /// Consumed by auto_label.py and by article 2 confusion-matrix-vs-distance analysis.
    /// </summary>
    [RequireComponent(typeof(TrafficLightAwareController))]
    public sealed class CityVehicleTelemetryExtender : MonoBehaviour
    {
        private TrafficLightAwareController controller;

        private void Awake()
        {
            controller = GetComponent<TrafficLightAwareController>();
        }

        public Dictionary<string, object> BuildExtensions()
        {
            var dict = new Dictionary<string, object>();
            if (controller == null) return dict;
            var snap = controller.GetNearestLightSnapshot();
            dict["nearestTrafficLight"] = new
            {
                hasLight = snap.hasLight,
                state = snap.state,
                distanceM = snap.distanceM,
            };
            return dict;
        }
    }
}
