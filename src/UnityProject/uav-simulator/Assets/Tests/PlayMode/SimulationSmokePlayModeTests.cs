using NUnit.Framework;
using System.Collections;
using System.Linq;
using UavSimulator.Contracts;
using UavSimulator.Core;
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
        public IEnumerator FallbackScene_ContractContainsBuiltinKs0223()
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

                if (device.deviceId == "vehicle.ks0223.v1")
                {
                    foundVehicle = true;
                }

                if (device.sensors != null &&
                    device.sensors.Any(s => s != null && s.id == "camera.front.rgb"))
                {
                    foundCameraSensor = true;
                }
            }

            Assert.That(foundVehicle, Is.True, "Expected builtin KS0223 vehicle contract in availableVehicles.");
            Assert.That(foundCameraSensor, Is.True, "Expected KS0223 contract to expose front camera sensor.");
            Object.Destroy(root);
        }
    }
}
