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
            };

            var json = JsonUtility.ToJson(config);
            var parsed = JsonUtility.FromJson<SimulationConfig>(json);

            Assert.That(parsed.seed, Is.EqualTo(123));
            Assert.That(parsed.selectedTrackId, Is.EqualTo("track-01"));
            Assert.That(parsed.trackParams, Is.Not.Null);
            Assert.That(parsed.trackParams.Length, Is.EqualTo(1));
        }

        [Test]
        public void ControlCommand_RoundTrip_JsonUtility()
        {
            var command = new ControlCommand
            {
                throttle = 0.5f,
                steer = -0.1f,
                brake = 0.0f,
                timestamp = 42,
                timeBase = "unix_ms",
            };

            var json = JsonUtility.ToJson(command);
            var parsed = JsonUtility.FromJson<ControlCommand>(json);

            Assert.That(parsed.timeBase, Is.EqualTo("unix_ms"));
            Assert.That(parsed.throttle, Is.EqualTo(0.5f));
        }
    }
}

