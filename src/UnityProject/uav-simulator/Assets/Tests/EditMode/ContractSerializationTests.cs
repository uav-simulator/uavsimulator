using NUnit.Framework;
using UavSimulator.Contracts;
using UnityEngine;

namespace UavSimulator.Tests.EditMode
{
    public sealed class ContractSerializationTests
    {
        [Test]
        public void SimulationConfig_RoundTrip_JsonUtility()
        {
            var config = new SimulationConfig
            {
                seed = 123,
                timeScale = 1.0f,
                selectedTrackId = "track-01",
                selectedVehicleId = "vehicle-01",
                trackParams = new[] { new ConfigKeyValue { key = "k", value = "v" } },
                vehicleParams = new[] { new ConfigKeyValue { key = "k2", value = "v2" } },
                flags = new[] { new ConfigKeyValue { key = "flag", value = "1" } },
                agents = new[]
                {
                    new SimulationAgentConfig
                    {
                        agentId = "ego",
                        vehicleId = "vehicle-01",
                        isPrimary = true,
                        trackParams = new[] { new ConfigKeyValue { key = "spawn.position", value = "1,0,2" } },
                        vehicleParams = new[] { new ConfigKeyValue { key = "camera.mode", value = "driver" } },
                    },
                },
            };

            var json = JsonUtility.ToJson(config);
            var parsed = JsonUtility.FromJson<SimulationConfig>(json);

            Assert.That(parsed.seed, Is.EqualTo(123));
            Assert.That(parsed.selectedTrackId, Is.EqualTo("track-01"));
            Assert.That(parsed.trackParams, Is.Not.Null);
            Assert.That(parsed.trackParams.Length, Is.EqualTo(1));
            Assert.That(parsed.agents, Is.Not.Null);
            Assert.That(parsed.agents.Length, Is.EqualTo(1));
            Assert.That(parsed.agents[0].agentId, Is.EqualTo("ego"));
        }

        [Test]
        public void ControlCommand_RoundTrip_JsonUtility()
        {
            var command = new ControlCommand
            {
                throttle = 0.5f,
                steer = -0.1f,
                brake = 0.0f,
                targetAgentId = "ego",
                targetVehicleId = "vehicle-01",
                timestamp = 42,
                timeBase = "unix_ms",
                extensions = new[]
                {
                    new ConfigKeyValue { key = "drive.left_pwm_norm", value = "0.35" },
                    new ConfigKeyValue { key = "drive.right_pwm_norm", value = "0.55" },
                },
            };

            var json = JsonUtility.ToJson(command);
            var parsed = JsonUtility.FromJson<ControlCommand>(json);

            Assert.That(parsed.timeBase, Is.EqualTo("unix_ms"));
            Assert.That(parsed.throttle, Is.EqualTo(0.5f));
            Assert.That(parsed.targetAgentId, Is.EqualTo("ego"));
            Assert.That(parsed.extensions, Is.Not.Null);
            Assert.That(parsed.extensions.Length, Is.EqualTo(2));
        }
    }
}
