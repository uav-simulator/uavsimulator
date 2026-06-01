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

        [Test]
        public void NearestLightSnapshot_FindsZoneBehindRoadCollider()
        {
            var go = new GameObject("ctrl");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            var ctrl = go.AddComponent<TrafficLightAwareController>();

            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "road-mesh-blocker";
            blocker.transform.position = new Vector3(0f, 0f, 2f);
            blocker.transform.localScale = new Vector3(3f, 1f, 0.2f);

            var zoneGo = new GameObject("zone");
            zoneGo.transform.position = new Vector3(0f, 0f, 4f);
            var box = zoneGo.AddComponent<BoxCollider>();
            box.size = new Vector3(3f, 1f, 1f);
            box.isTrigger = true;
            var zone = zoneGo.AddComponent<TrafficLightTriggerZone>();
            zone.SetStateForTesting(TrafficLightState.Red);

            try
            {
                Physics.SyncTransforms();
                var snapshot = ctrl.GetNearestLightSnapshot();

                Assert.IsTrue(snapshot.hasLight);
                Assert.AreEqual("Red", snapshot.state);
            }
            finally
            {
                Object.DestroyImmediate(zoneGo);
                Object.DestroyImmediate(blocker);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NearestLightSnapshot_ReportsZoneWhenVehicleIsInsideTrigger()
        {
            var go = new GameObject("ctrl");
            go.transform.position = Vector3.zero;
            var ctrl = go.AddComponent<TrafficLightAwareController>();

            var zoneGo = new GameObject("zone");
            zoneGo.transform.position = Vector3.zero;
            var box = zoneGo.AddComponent<BoxCollider>();
            box.size = new Vector3(3f, 1f, 3f);
            box.isTrigger = true;
            var zone = zoneGo.AddComponent<TrafficLightTriggerZone>();
            zone.SetStateForTesting(TrafficLightState.Red);

            try
            {
                Physics.SyncTransforms();
                var snapshot = ctrl.GetNearestLightSnapshot();

                Assert.IsTrue(snapshot.hasLight);
                Assert.AreEqual("Red", snapshot.state);
            }
            finally
            {
                Object.DestroyImmediate(zoneGo);
                Object.DestroyImmediate(go);
            }
        }
    }
}
