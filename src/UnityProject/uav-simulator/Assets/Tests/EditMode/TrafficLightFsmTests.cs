using NUnit.Framework;
using UavSimulator.CityDemo;
using UavSimulator.Vehicles;
using UnityEngine;

namespace UavSimulator.Tests.EditMode
{
    public sealed class TrafficLightFsmTests
    {
        private static (GameObject root, TrafficLightController ctrl, TrafficLight ns, TrafficLight ew)
            BuildController(float green = 10f, float yellow = 2f, float red = 8f)
        {
            var root = new GameObject("CtrlRoot");
            var nsGo = new GameObject("NS");
            nsGo.transform.SetParent(root.transform);
            var ns = nsGo.AddComponent<TrafficLight>();

            var ewGo = new GameObject("EW");
            ewGo.transform.SetParent(root.transform);
            var ew = ewGo.AddComponent<TrafficLight>();

            var ctrl = root.AddComponent<TrafficLightController>();
            ctrl.greenSeconds = green;
            ctrl.yellowSeconds = yellow;
            ctrl.redSeconds = red;
            ctrl.SetNorthSouthLights(new[] { ns });
            ctrl.SetEastWestLights(new[] { ew });
            return (root, ctrl, ns, ew);
        }

        [Test]
        public void Controller_AfterStartCycle_NorthSouthGreen()
        {
            var (root, ctrl, ns, ew) = BuildController();
            try
            {
                ctrl.StartCycle();
                Assert.AreEqual(TrafficLightState.Green, ns.State, "NS should be Green at cycle start");
                Assert.AreEqual(TrafficLightState.Red, ew.State, "EW should be Red at cycle start");
            }
            finally
            {
                ctrl.StopCycle();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Controller_TransitionsThroughCycle()
        {
            const float green = 10f;
            const float yellow = 2f;
            const float red = 8f;
            var (root, ctrl, ns, ew) = BuildController(green, yellow, red);
            try
            {
                ctrl.StartCycle();
                Assert.AreEqual(TrafficLightState.Green, ns.State);
                Assert.AreEqual(TrafficLightState.Red, ew.State);

                // After greenSeconds → NS Yellow.
                ctrl.AdvanceTime(green + 0.001f);
                Assert.AreEqual(TrafficLightState.Yellow, ns.State, "NS should be Yellow after green elapses");
                Assert.AreEqual(TrafficLightState.Red, ew.State);

                // After yellowSeconds → NS Red, cross-direction Green.
                ctrl.AdvanceTime(yellow + 0.001f);
                Assert.AreEqual(TrafficLightState.Red, ns.State, "NS should be Red for redSeconds");
                Assert.AreEqual(TrafficLightState.Green, ew.State);

                // After redSeconds → back to NS Green.
                ctrl.AdvanceTime(red + 0.001f);
                Assert.AreEqual(TrafficLightState.Green, ns.State);
                Assert.AreEqual(TrafficLightState.Red, ew.State);
            }
            finally
            {
                ctrl.StopCycle();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Controller_AdvanceTime_CrossingMultiplePhases_LandsCorrectly()
        {
            const float green = 5f;
            const float yellow = 1f;
            const float red = 3f;
            var (root, ctrl, ns, ew) = BuildController(green, yellow, red);
            try
            {
                ctrl.StartCycle();
                // Skip past NS Green (5s) and NS Yellow (1s) → should be in NS Red.
                ctrl.AdvanceTime(green + yellow + 0.001f);
                Assert.AreEqual(TrafficLightState.Red, ns.State);
                Assert.AreEqual(TrafficLightState.Green, ew.State);

                // redSeconds is a real phase duration, not an ignored reserve value.
                ctrl.AdvanceTime(red + 0.001f);
                Assert.AreEqual(TrafficLightState.Green, ns.State);
                Assert.AreEqual(TrafficLightState.Red, ew.State);
            }
            finally
            {
                ctrl.StopCycle();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TrafficLightAwareController_HitsRed_BrakeIntensityFull()
        {
            var lightGo = new GameObject("Light");
            var light = lightGo.AddComponent<TrafficLight>();
            light.SetState(TrafficLightState.Red);

            var zoneGo = new GameObject("Zone");
            zoneGo.AddComponent<BoxCollider>();
            var zone = zoneGo.AddComponent<TrafficLightTriggerZone>();
            zone.SetTrafficLight(light);

            try
            {
                var braked = TrafficLightAwareControllerTestAccess.Evaluate(zone, out var intensity);
                Assert.IsTrue(braked, "Should brake on Red");
                Assert.AreEqual(1.0f, intensity, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(zoneGo);
                Object.DestroyImmediate(lightGo);
            }
        }

        [Test]
        public void TrafficLightAwareController_HitsYellow_BrakeIntensityHalf()
        {
            var lightGo = new GameObject("Light");
            var light = lightGo.AddComponent<TrafficLight>();
            light.SetState(TrafficLightState.Yellow);

            var zoneGo = new GameObject("Zone");
            zoneGo.AddComponent<BoxCollider>();
            var zone = zoneGo.AddComponent<TrafficLightTriggerZone>();
            zone.SetTrafficLight(light);

            try
            {
                var braked = TrafficLightAwareControllerTestAccess.Evaluate(zone, out var intensity);
                Assert.IsTrue(braked, "Should brake on Yellow");
                Assert.AreEqual(0.5f, intensity, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(zoneGo);
                Object.DestroyImmediate(lightGo);
            }
        }

        [Test]
        public void TrafficLightAwareController_HitsGreen_NoBrake()
        {
            var lightGo = new GameObject("Light");
            var light = lightGo.AddComponent<TrafficLight>();
            light.SetState(TrafficLightState.Green);

            var zoneGo = new GameObject("Zone");
            zoneGo.AddComponent<BoxCollider>();
            var zone = zoneGo.AddComponent<TrafficLightTriggerZone>();
            zone.SetTrafficLight(light);

            try
            {
                var braked = TrafficLightAwareControllerTestAccess.Evaluate(zone, out var intensity);
                Assert.IsFalse(braked, "Should not brake on Green");
                Assert.AreEqual(0f, intensity, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(zoneGo);
                Object.DestroyImmediate(lightGo);
            }
        }

        [Test]
        public void TrafficLightAwareController_NullZone_NoBrake()
        {
            var braked = TrafficLightAwareControllerTestAccess.Evaluate(null, out var intensity);
            Assert.IsFalse(braked);
            Assert.AreEqual(0f, intensity, 1e-6f);
        }

        [Test]
        public void TrafficLightAwareController_RedZoneAheadButFarFromStopLine_DoesNotBrakeYet()
        {
            var probeGo = new GameObject("Probe");
            var lightGo = new GameObject("Light");
            var zoneGo = new GameObject("StopLineZone");

            try
            {
                probeGo.transform.position = Vector3.zero;
                probeGo.transform.rotation = Quaternion.identity;
                var ctrl = probeGo.AddComponent<TrafficLightAwareController>();
                ctrl.lookAheadDistance = 15f;

                var light = lightGo.AddComponent<TrafficLight>();
                light.SetState(TrafficLightState.Red);

                zoneGo.transform.position = new Vector3(0f, 0f, 10f);
                var box = zoneGo.AddComponent<BoxCollider>();
                box.size = new Vector3(4f, 0.5f, 2f);
                box.isTrigger = true;
                var zone = zoneGo.AddComponent<TrafficLightTriggerZone>();
                zone.SetTrafficLight(light);
                Physics.SyncTransforms();

                var braked = ctrl.ShouldBrake(out var intensity);

                Assert.IsFalse(braked, "A visible red light must not stop the car several meters before the stop line.");
                Assert.AreEqual(0f, intensity, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(zoneGo);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(probeGo);
            }
        }

        [Test]
        public void TrafficLightAwareController_RedZoneNearStopLine_Brakes()
        {
            var probeGo = new GameObject("Probe");
            var lightGo = new GameObject("Light");
            var zoneGo = new GameObject("StopLineZone");

            try
            {
                probeGo.transform.position = new Vector3(0f, 0f, 7.2f);
                probeGo.transform.rotation = Quaternion.identity;
                var ctrl = probeGo.AddComponent<TrafficLightAwareController>();
                ctrl.lookAheadDistance = 15f;

                var light = lightGo.AddComponent<TrafficLight>();
                light.SetState(TrafficLightState.Red);

                zoneGo.transform.position = new Vector3(0f, 0f, 10f);
                var box = zoneGo.AddComponent<BoxCollider>();
                box.size = new Vector3(4f, 0.5f, 2f);
                box.isTrigger = true;
                var zone = zoneGo.AddComponent<TrafficLightTriggerZone>();
                zone.SetTrafficLight(light);
                Physics.SyncTransforms();

                var braked = ctrl.ShouldBrake(out var intensity);

                Assert.IsTrue(braked, "The car should brake only when it is close enough to stop before the line.");
                Assert.AreEqual(1f, intensity, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(zoneGo);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(probeGo);
            }
        }

        [Test]
        public void TrafficLightAwareController_NearestSnapshotReportsStopLineSurfaceDistance()
        {
            var probeGo = new GameObject("Probe");
            var lightGo = new GameObject("Light");
            var zoneGo = new GameObject("StopLineZone");

            try
            {
                probeGo.transform.position = Vector3.zero;
                probeGo.transform.rotation = Quaternion.identity;
                var ctrl = probeGo.AddComponent<TrafficLightAwareController>();
                ctrl.lookAheadDistance = 15f;

                var light = lightGo.AddComponent<TrafficLight>();
                light.SetState(TrafficLightState.Red);

                zoneGo.transform.position = new Vector3(0f, 0f, 10f);
                var box = zoneGo.AddComponent<BoxCollider>();
                box.size = new Vector3(4f, 0.5f, 2f);
                box.isTrigger = true;
                var zone = zoneGo.AddComponent<TrafficLightTriggerZone>();
                zone.SetTrafficLight(light);
                Physics.SyncTransforms();

                var snapshot = ctrl.GetNearestLightSnapshot();

                Assert.IsTrue(snapshot.hasLight);
                Assert.AreEqual(TrafficLightState.Red.ToString(), snapshot.state);
                Assert.AreEqual(9f, snapshot.distanceM, 0.05f, "Telemetry distance should describe the stop-line surface, not the zone center.");
            }
            finally
            {
                Object.DestroyImmediate(zoneGo);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(probeGo);
            }
        }
    }

    /// <summary>
    /// Bridge to the internal static <c>EvaluateZone</c> on
    /// <see cref="TrafficLightAwareController"/>. The test assembly references
    /// <c>UavSimulator.Runtime</c> but does not use <c>InternalsVisibleTo</c>;
    /// we instead expose the zone via a probe override on a real instance.
    /// </summary>
    internal static class TrafficLightAwareControllerTestAccess
    {
        public static bool Evaluate(TrafficLightTriggerZone zone, out float brakeIntensity)
        {
            var go = new GameObject("Probe");
            try
            {
                var ctrl = go.AddComponent<TrafficLightAwareController>();
                ctrl.SetProbeOverride(zone);
                return ctrl.ShouldBrake(out brakeIntensity);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
