using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UavSimulator.Plugins
{
    public static class PluginRegistry
    {
        public const string RegistryAssetPath = "UavSimulator/PluginRegistry";
        public const string DescriptorsFolderPath = "UavSimulator/Plugins";

        public static PluginRegistrySnapshot Load()
        {
            var registryAsset = Resources.Load<PluginRegistryAsset>(RegistryAssetPath);
            if (registryAsset != null)
            {
                var snapshot = PluginRegistrySnapshot.FromAsset(registryAsset);
                return snapshot.IsEmpty ? BuiltinPluginFactory.CreateSnapshot() : snapshot;
            }

            var vehicles = Resources.LoadAll<VehiclePluginDescriptor>(DescriptorsFolderPath) ?? new VehiclePluginDescriptor[0];
            var tracks = Resources.LoadAll<TrackPluginDescriptor>(DescriptorsFolderPath) ?? new TrackPluginDescriptor[0];

            var loadedSnapshot = new PluginRegistrySnapshot(
                vehicles: vehicles.Where(v => v != null).ToArray(),
                tracks: tracks.Where(t => t != null).ToArray()
            );
            return loadedSnapshot.IsEmpty ? BuiltinPluginFactory.CreateSnapshot() : loadedSnapshot;
        }
    }

    public sealed class PluginRegistrySnapshot
    {
        public readonly VehiclePluginDescriptor[] Vehicles;
        public readonly TrackPluginDescriptor[] Tracks;

        public PluginRegistrySnapshot(VehiclePluginDescriptor[] vehicles, TrackPluginDescriptor[] tracks)
        {
            Vehicles = vehicles ?? new VehiclePluginDescriptor[0];
            Tracks = tracks ?? new TrackPluginDescriptor[0];
        }

        public static PluginRegistrySnapshot FromAsset(PluginRegistryAsset asset)
        {
            if (asset == null) return new PluginRegistrySnapshot(Array.Empty<VehiclePluginDescriptor>(), Array.Empty<TrackPluginDescriptor>());

            var vehicles = asset.vehicles ?? new VehiclePluginDescriptor[0];
            var tracks = asset.tracks ?? new TrackPluginDescriptor[0];

            return new PluginRegistrySnapshot(
                vehicles: vehicles.Where(v => v != null).ToArray(),
                tracks: tracks.Where(t => t != null).ToArray()
            );
        }

        public IReadOnlyList<VehiclePluginDescriptor> VehiclesList => Vehicles;
        public IReadOnlyList<TrackPluginDescriptor> TracksList => Tracks;
        public bool IsEmpty => Vehicles.Length == 0 || Tracks.Length == 0;
    }
}
