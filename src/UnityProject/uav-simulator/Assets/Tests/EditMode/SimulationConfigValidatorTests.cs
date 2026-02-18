using NUnit.Framework;
using UavSimulator.Contracts;
using UavSimulator.Core;
using UavSimulator.Plugins;
using UnityEngine;

namespace UavSimulator.Tests.EditMode
{
    public sealed class SimulationConfigValidatorTests
    {
        [Test]
        public void Validate_Throws_WhenNoPluginsRegistered()
        {
            var cfg = new SimulationConfig { selectedVehicleId = "", selectedTrackId = "" };
            var snapshot = new PluginRegistrySnapshot(
                vehicles: new VehiclePluginDescriptor[0],
                tracks: new TrackPluginDescriptor[0]);

            Assert.That(
                () => SimulationConfigValidator.Validate(cfg, snapshot),
                Throws.Exception);
        }

        [Test]
        public void Validate_UsesFirstPlugin_WhenIdsMissing()
        {
            var vehicle = ScriptableObject.CreateInstance<VehiclePluginDescriptor>();
            vehicle.id = "veh-default";
            var track = ScriptableObject.CreateInstance<TrackPluginDescriptor>();
            track.id = "trk-default";

            var cfg = new SimulationConfig
            {
                selectedVehicleId = "",
                selectedTrackId = "",
                timeScale = 1f,
            };

            var snapshot = new PluginRegistrySnapshot(
                vehicles: new[] { vehicle },
                tracks: new[] { track });

            var result = SimulationConfigValidator.Validate(cfg, snapshot);
            Assert.That(result.Vehicle.id, Is.EqualTo("veh-default"));
            Assert.That(result.Track.id, Is.EqualTo("trk-default"));
        }

        [Test]
        public void Validate_Uses_Default_TimeScale()
        {
            var vehicle = ScriptableObject.CreateInstance<VehiclePluginDescriptor>();
            vehicle.id = "veh-1";
            var track = ScriptableObject.CreateInstance<TrackPluginDescriptor>();
            track.id = "trk-1";

            var cfg = new SimulationConfig
            {
                selectedVehicleId = "veh-1",
                selectedTrackId = "trk-1",
                timeScale = 0f,
            };

            var snapshot = new PluginRegistrySnapshot(
                vehicles: new[] { vehicle },
                tracks: new[] { track });

            var result = SimulationConfigValidator.Validate(cfg, snapshot);
            Assert.That(result.TimeScale, Is.EqualTo(1f));
        }
    }
}
