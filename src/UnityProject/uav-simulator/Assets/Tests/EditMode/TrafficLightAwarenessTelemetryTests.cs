using NUnit.Framework;
using UnityEngine;
using UavSimulator.CityDemo;
using UavSimulator.Vehicles;

namespace UavSimulator.Tests
{
    public class TrafficLightAwarenessTelemetryTests
    {
        [Test]
        public void NearestLightSnapshot_ReportsRedStateWhenZoneIsRed()
        {
            var go = new GameObject("ctrl");
            var ctrl = go.AddComponent<TrafficLightAwareController>();
            var zoneGo = new GameObject("zone");
            zoneGo.AddComponent<BoxCollider>();
            var zone = zoneGo.AddComponent<TrafficLightTriggerZone>();
            zone.SetStateForTesting(TrafficLightState.Red);
            ctrl.SetProbeOverride(zone);

            var snapshot = ctrl.GetNearestLightSnapshot();

            Assert.AreEqual("Red", snapshot.state);
            Assert.IsTrue(snapshot.hasLight);

            Object.DestroyImmediate(zoneGo);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void NearestLightSnapshot_NoLightReportsEmpty()
        {
            var go = new GameObject("ctrl");
            var ctrl = go.AddComponent<TrafficLightAwareController>();
            ctrl.SetProbeOverride(null);

            var snapshot = ctrl.GetNearestLightSnapshot();

            Assert.IsFalse(snapshot.hasLight);
            Assert.AreEqual("None", snapshot.state);
            Object.DestroyImmediate(go);
        }
    }
}
