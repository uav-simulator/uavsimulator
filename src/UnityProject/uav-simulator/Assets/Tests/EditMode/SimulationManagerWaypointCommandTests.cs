using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UavSimulator.Contracts;
using UavSimulator.Core;

namespace UavSimulator.Tests.EditMode
{
    public sealed class SimulationManagerWaypointCommandTests
    {
        [Test]
        public void WaypointFollowerCommand_DropsRequestedDrivePwmExtensions()
        {
            var requested = new ControlCommand
            {
                extensions = new[]
                {
                    new ConfigKeyValue { key = "drive.left_pwm_norm", value = "0.000000" },
                    new ConfigKeyValue { key = "drive.right_pwm_norm", value = "0.000000" },
                    new ConfigKeyValue { key = "camera.model_capture_mode", value = "driver" },
                },
            };
            var agent = CreateAgentRuntime("ego", "vehicle.prometeo.sport.v1");

            var command = InvokeCloneCommand(requested, agent, throttle: 0.55f, steer: 0.1f, brake: 0f);

            Assert.AreEqual(0.55f, command.throttle, 0.0001f);
            Assert.AreEqual(0.1f, command.steer, 0.0001f);
            Assert.IsFalse(command.extensions.Any(item => item.key == "drive.left_pwm_norm"));
            Assert.IsFalse(command.extensions.Any(item => item.key == "drive.right_pwm_norm"));
            Assert.IsTrue(command.extensions.Any(item => item.key == "camera.model_capture_mode"));
        }

        [Test]
        public void NonLoopRouteCompletion_RemainsStickyInStepAndRouteTelemetry()
        {
            var managerObject = new UnityEngine.GameObject("simulation-manager-test");
            var manager = managerObject.AddComponent<SimulationManager>();
            var state = CreateVehicleState(0f, 0f, 0f);

            try
            {
                SetPrivateField(manager, "activeRouteWaypoints", new[] { new UnityEngine.Vector3(0f, 0f, 0f) });
                SetPrivateField(manager, "activeRouteWaypointIndex", 0);
                SetPrivateField(manager, "activeRouteReachDistance", 1f);
                SetPrivateField(manager, "activeRouteLoop", false);

                Assert.IsTrue(InvokeUpdateRouteProgress(manager, state));
                Assert.IsTrue(InvokeUpdateRouteProgress(manager, state));

                var agent = CreateAgentRuntime("ego", "vehicle.prometeo.sport.v1", isPrimary: true);
                var step = InvokeBuildStepResult(manager, agent, state, routeCompleted: false);
                var routeInfo = InvokeBuildRouteInfo(manager, state, routeCompleted: false)
                    .ToDictionary(item => item.key, item => item.value);

                Assert.IsTrue(step.done);
                Assert.AreEqual("true", routeInfo["route.completed"]);
                Assert.AreEqual("1", routeInfo["route.current_index"]);
                Assert.AreEqual("0", routeInfo["route.remaining_waypoints"]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
        }

        private static object CreateAgentRuntime(string agentId, string vehicleId)
            => CreateAgentRuntime(agentId, vehicleId, isPrimary: false);

        private static object CreateAgentRuntime(string agentId, string vehicleId, bool isPrimary)
        {
            var type = typeof(SimulationManager).GetNestedType("ActiveAgentRuntime", BindingFlags.NonPublic);
            Assert.IsNotNull(type);
            var agent = Activator.CreateInstance(type!);
            type!.GetField("AgentId")!.SetValue(agent, agentId);
            type.GetField("VehicleId")!.SetValue(agent, vehicleId);
            type.GetField("IsPrimary")!.SetValue(agent, isPrimary);
            return agent!;
        }

        private static ControlCommand InvokeCloneCommand(
            ControlCommand requested,
            object agent,
            float throttle,
            float steer,
            float brake)
        {
            var method = typeof(SimulationManager).GetMethod("CloneCommand", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);
            return (ControlCommand)method!.Invoke(null, new[] { requested, agent, throttle, steer, brake })!;
        }

        private static VehicleState CreateVehicleState(float x, float y, float z)
        {
            return new VehicleState
            {
                pose = new Posef
                {
                    position = new Vector3f { x = x, y = y, z = z },
                    rotation = new Quaternionf { w = 1f },
                },
                linearVelocity = new Vector3f(),
                angularVelocity = new Vector3f(),
                telemetry = Array.Empty<ConfigKeyValue>(),
            };
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            var field = typeof(SimulationManager).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field!.SetValue(instance, value);
        }

        private static bool InvokeUpdateRouteProgress(SimulationManager manager, VehicleState state)
        {
            var method = typeof(SimulationManager).GetMethod("UpdateRouteProgress", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            return (bool)method!.Invoke(manager, new object[] { state })!;
        }

        private static ConfigKeyValue[] InvokeBuildRouteInfo(
            SimulationManager manager,
            VehicleState state,
            bool routeCompleted)
        {
            var method = typeof(SimulationManager).GetMethod("BuildRouteInfo", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            return (ConfigKeyValue[])method!.Invoke(manager, new object[] { state, routeCompleted })!;
        }

        private static StepResult InvokeBuildStepResult(
            SimulationManager manager,
            object agent,
            VehicleState state,
            bool routeCompleted)
        {
            var method = typeof(SimulationManager).GetMethod("BuildStepResult", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            return (StepResult)method!.Invoke(manager, new object[] { agent, state, null, null, routeCompleted })!;
        }
    }
}
