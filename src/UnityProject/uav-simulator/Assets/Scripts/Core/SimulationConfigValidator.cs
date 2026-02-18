using System;
using System.Linq;
using UavSimulator.Contracts;
using UavSimulator.Plugins;

namespace UavSimulator.Core
{
    public static class SimulationConfigValidator
    {
        public sealed class Result
        {
            public VehiclePluginDescriptor Vehicle;
            public TrackPluginDescriptor Track;
            public float TimeScale;
        }

        public static Result Validate(SimulationConfig config, PluginRegistrySnapshot registry)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var vehicles = (registry.Vehicles ?? Array.Empty<VehiclePluginDescriptor>())
                .Where(v => v != null)
                .ToArray();
            var tracks = (registry.Tracks ?? Array.Empty<TrackPluginDescriptor>())
                .Where(t => t != null)
                .ToArray();

            if (vehicles.Length == 0)
            {
                throw new ArgumentException("No vehicle plugins are registered.");
            }

            if (tracks.Length == 0)
            {
                throw new ArgumentException("No track plugins are registered.");
            }

            var resolvedVehicleId = string.IsNullOrWhiteSpace(config.selectedVehicleId)
                ? vehicles[0].id
                : config.selectedVehicleId;
            var resolvedTrackId = string.IsNullOrWhiteSpace(config.selectedTrackId)
                ? tracks[0].id
                : config.selectedTrackId;

            var vehicle = vehicles.FirstOrDefault(v => v.id == resolvedVehicleId);
            if (vehicle == null)
            {
                throw new ArgumentException($"Unknown vehicle id: '{resolvedVehicleId}'.");
            }

            var track = tracks.FirstOrDefault(t => t.id == resolvedTrackId);
            if (track == null)
            {
                throw new ArgumentException($"Unknown track id: '{resolvedTrackId}'.");
            }

            var timeScale = config.timeScale > 0f ? config.timeScale : 1f;

            return new Result
            {
                Vehicle = vehicle,
                Track = track,
                TimeScale = timeScale,
            };
        }
    }
}
