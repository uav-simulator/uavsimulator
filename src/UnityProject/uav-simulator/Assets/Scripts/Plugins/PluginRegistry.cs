using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UavSimulator.Plugins
{
    public enum PluginRegistrySource
    {
        RegistryAsset,
        ResourcesDescriptorsFolder,
        BuiltinFallbackFromEmptyRegistryAsset,
        BuiltinFallbackFromEmptyResources,
        BuiltinFactory,
    }

    public static class PluginRegistry
    {
        public const string RegistryAssetPath = "UavSimulator/PluginRegistry";
        public const string DescriptorsFolderPath = "UavSimulator/Plugins";

        public static PluginRegistrySnapshot Load()
        {
            var registryAsset = Resources.Load<PluginRegistryAsset>(RegistryAssetPath);
            if (registryAsset != null)
            {
                var snapshot = PluginRegistrySnapshot.FromAsset(registryAsset, PluginRegistrySource.RegistryAsset);
                return snapshot.IsEmpty
                    ? BuiltinPluginFactory.CreateSnapshot(PluginRegistrySource.BuiltinFallbackFromEmptyRegistryAsset)
                    : snapshot;
            }

            var vehicles = Resources.LoadAll<VehiclePluginDescriptor>(DescriptorsFolderPath) ?? new VehiclePluginDescriptor[0];
            var tracks = Resources.LoadAll<TrackPluginDescriptor>(DescriptorsFolderPath) ?? new TrackPluginDescriptor[0];

            var loadedSnapshot = new PluginRegistrySnapshot(
                vehicles: vehicles.Where(v => v != null).ToArray(),
                tracks: tracks.Where(t => t != null).ToArray(),
                source: PluginRegistrySource.ResourcesDescriptorsFolder
            );
            return loadedSnapshot.IsEmpty
                ? BuiltinPluginFactory.CreateSnapshot(PluginRegistrySource.BuiltinFallbackFromEmptyResources)
                : loadedSnapshot;
        }
    }

    public sealed class PluginRegistrySnapshot
    {
        public readonly VehiclePluginDescriptor[] Vehicles;
        public readonly TrackPluginDescriptor[] Tracks;
        public readonly PluginRegistrySource Source;

        public PluginRegistrySnapshot(
            VehiclePluginDescriptor[] vehicles,
            TrackPluginDescriptor[] tracks,
            PluginRegistrySource source = PluginRegistrySource.BuiltinFactory)
        {
            Vehicles = vehicles ?? new VehiclePluginDescriptor[0];
            Tracks = tracks ?? new TrackPluginDescriptor[0];
            Source = source;
        }

        public static PluginRegistrySnapshot FromAsset(PluginRegistryAsset asset, PluginRegistrySource source = PluginRegistrySource.RegistryAsset)
        {
            if (asset == null)
            {
                return new PluginRegistrySnapshot(
                    Array.Empty<VehiclePluginDescriptor>(),
                    Array.Empty<TrackPluginDescriptor>(),
                    source);
            }

            var vehicles = asset.vehicles ?? new VehiclePluginDescriptor[0];
            var tracks = asset.tracks ?? new TrackPluginDescriptor[0];

            return new PluginRegistrySnapshot(
                vehicles: vehicles.Where(v => v != null).ToArray(),
                tracks: tracks.Where(t => t != null).ToArray(),
                source: source
            );
        }

        public IReadOnlyList<VehiclePluginDescriptor> VehiclesList => Vehicles;
        public IReadOnlyList<TrackPluginDescriptor> TracksList => Tracks;
        public bool IsEmpty => Vehicles.Length == 0 || Tracks.Length == 0;
    }
}
