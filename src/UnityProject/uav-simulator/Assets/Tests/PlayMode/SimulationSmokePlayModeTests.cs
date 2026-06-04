using NUnit.Framework;
using System.Collections;
using System.Linq;
using UavSimulator.CityDemo;
using UavSimulator.Contracts;
using UavSimulator.Core;
using UavSimulator.Plugins;
using UnityEngine;
using UnityEngine.TestTools;

namespace UavSimulator.Tests.PlayMode
{
    public sealed class SimulationSmokePlayModeTests
    {
        [UnityTest]
        public IEnumerator FallbackScene_ResetAndStep_MovesVehicleForward()
        {
            var root = new GameObject("PlayModeSmokeRoot");
            var manager = root.AddComponent<SimulationManager>();

            // Wait one frame so SimulationManager.Awake can initialize the plugin registry.
            yield return null;

            var config = new SimulationConfig
            {
                seed = 1,
                timeScale = 1f,
                selectedTrackId = string.Empty,
                selectedVehicleId = string.Empty,
                trackParams = new ConfigKeyValue[0],
                vehicleParams = new ConfigKeyValue[0],
                flags = new ConfigKeyValue[0],
            };

            manager.ResetSimulation(config);
            var start = manager.ReadState();
            var startZ = start.pose.position.z;
            Debug.Log($"[Smoke] start_z={startZ:0.###}");

            for (var i = 0; i < 40; i++)
            {
                var command = new ControlCommand
                {
                    throttle = 0f,
                    steer = 0f,
                    brake = 0f,
                    timestamp = i,
                    timeBase = "unix_ms",
                    extensions = new[]
                    {
                        new ConfigKeyValue { key = "drive.left_pwm_norm", value = "0.8" },
                        new ConfigKeyValue { key = "drive.right_pwm_norm", value = "0.8" },
                    },
                };

                manager.Step(command);
                yield return new WaitForFixedUpdate();
            }

            var end = manager.ReadState();
            Debug.Log($"[Smoke] end_z={end.pose.position.z:0.###}");
            Assert.That(end.pose.position.z, Is.GreaterThan(startZ + 0.2f));
            Assert.That(end.telemetry, Is.Not.Null);
            Assert.That(end.telemetry.Any(v => v != null && v.key == "sensor.speedometer.mps"), Is.True);
            Assert.That(end.telemetry.Any(v => v != null && v.key == "power.motor.estimated_w"), Is.True);

            var hasCamera = manager.TryReadCameraFrame(out var frame);
            if (hasCamera)
            {
                Assert.That(frame, Is.Not.Null);
                Assert.That(frame.width, Is.GreaterThan(0));
                Assert.That(frame.height, Is.GreaterThan(0));
            }

            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator FallbackScene_ContractContainsBuiltinGroundVehicle()
        {
            var root = new GameObject("PlayModeContractRoot");
            var manager = root.AddComponent<SimulationManager>();

            yield return null;

            var contract = manager.GetContract();
            Assert.That(contract, Is.Not.Null);
            Assert.That(contract.availableVehicles, Is.Not.Null);
            Assert.That(contract.availableTracks, Is.Not.Null);
            Debug.Log($"[Smoke] vehicles={contract.availableVehicles.Length}, tracks={contract.availableTracks.Length}");

            var foundVehicle = false;
            var foundCameraSensor = false;
            foreach (var device in contract.availableVehicles)
            {
                if (device == null)
                {
                    continue;
                }

                if (device.deviceId == "vehicle.prometeo.sport.v1")
                {
                    foundVehicle = true;
                }

                if (device.sensors != null &&
                    device.sensors.Any(s => s != null && s.id == "camera.front.rgb"))
                {
                    foundCameraSensor = true;
                }
            }

            Assert.That(foundVehicle, Is.True, "Expected builtin ground vehicle contract in availableVehicles.");
            Assert.That(foundCameraSensor, Is.True, "Expected ground vehicle contract to expose front camera sensor.");
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator FallbackScene_MultiAgentStep_TargetsRequestedAgent()
        {
            var root = new GameObject("PlayModeMultiAgentRoot");
            var manager = root.AddComponent<SimulationManager>();

            yield return null;

            var config = new SimulationConfig
            {
                seed = 7,
                timeScale = 1f,
                selectedTrackId = BuiltinPluginFactory.BasicArenaTrackId,
                selectedVehicleId = BuiltinPluginFactory.ArcadeBlueVehicleId,
                trackParams = new ConfigKeyValue[0],
                vehicleParams = new ConfigKeyValue[0],
                flags = new[]
                {
                    new ConfigKeyValue { key = "agents.see_each_other", value = "true" },
                    new ConfigKeyValue { key = "agents.collisions_enabled", value = "false" },
                },
                agents = new[]
                {
                    new SimulationAgentConfig
                    {
                        agentId = "ego",
                        vehicleId = BuiltinPluginFactory.ArcadeBlueVehicleId,
                        isPrimary = true,
                        trackParams = new[]
                        {
                            new ConfigKeyValue { key = "spawn.position", value = "0.0,0.2,-7.5" },
                        },
                    },
                    new SimulationAgentConfig
                    {
                        agentId = "npc-red",
                        vehicleId = BuiltinPluginFactory.ArcadeRedVehicleId,
                        trackParams = new[]
                        {
                            new ConfigKeyValue { key = "spawn.position", value = "1.1,0.2,-7.5" },
                        },
                    },
                },
            };

            manager.ResetSimulation(config);
            var before = manager.ReadSnapshot(targetAgentId: "npc-red", includeFrame: false);

            for (var i = 0; i < 30; i++)
            {
                manager.Step(new ControlCommand
                {
                    targetAgentId = "npc-red",
                    timestamp = i,
                    timeBase = "unix_ms",
                    extensions = new[]
                    {
                        new ConfigKeyValue { key = "drive.left_pwm_norm", value = "0.75" },
                        new ConfigKeyValue { key = "drive.right_pwm_norm", value = "0.75" },
                    },
                });
                yield return new WaitForFixedUpdate();
            }

            var after = manager.ReadSnapshot(targetAgentId: "npc-red", includeFrame: false);
            Assert.That(after.activeAgentId, Is.EqualTo("npc-red"));
            Assert.That(after.agents, Is.Not.Null);
            Assert.That(after.agents.Length, Is.EqualTo(2));
            Assert.That(after.state.pose.position.z, Is.GreaterThan(before.state.pose.position.z + 0.15f));

            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator CityPolygonTrack_ResetUsesRuntimeCompactCityPrefab()
        {
            var root = new GameObject("PlayModeCityTrackRoot");
            var manager = root.AddComponent<SimulationManager>();

            yield return null;

            var config = new SimulationConfig
            {
                seed = 4202,
                timeScale = 1f,
                selectedTrackId = BuiltinPluginFactory.CityPolygonTrackId,
                selectedVehicleId = BuiltinPluginFactory.ArcadeRedVehicleId,
                trackParams = new ConfigKeyValue[0],
                vehicleParams = new ConfigKeyValue[0],
                flags = new ConfigKeyValue[0],
            };

            manager.ResetSimulation(config);
            yield return null;

            var city = root.GetComponentsInChildren<Transform>(includeInactive: true)
                .FirstOrDefault(candidate => candidate.name == "CityRuntimeCompact");
            Assert.That(city, Is.Not.Null, "City showcase runtime should use the audited compact city prefab, not the editor-only DemoScene path.");

            var westSecondSignalZone = city.GetComponentsInChildren<TrafficLightTriggerZone>(includeInactive: true)
                .FirstOrDefault(candidate => candidate.name == "DemoSliceStopZone_WestSecondSignal_EW");
            Assert.That(westSecondSignalZone, Is.Not.Null, "Runtime compact city must expose the west second-signal stop-zone used by the showcase route.");

            Object.Destroy(root);
        }
    }
}
