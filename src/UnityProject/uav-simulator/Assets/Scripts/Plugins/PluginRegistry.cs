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
            var vehicles = Resources.LoadAll<VehiclePluginDescriptor>(DescriptorsFolderPath) ?? new VehiclePluginDescriptor[0];
            var tracks = Resources.LoadAll<TrackPluginDescriptor>(DescriptorsFolderPath) ?? new TrackPluginDescriptor[0];
            var builtinSnapshot = BuiltinPluginFactory.CreateSnapshot(PluginRegistrySource.BuiltinFactory);
            var resourceSnapshot = new PluginRegistrySnapshot(
                vehicles: vehicles.Where(v => v != null).ToArray(),
                tracks: tracks.Where(t => t != null).ToArray(),
                source: PluginRegistrySource.ResourcesDescriptorsFolder);
            resourceSnapshot = PluginRegistrySnapshot.MergePreferPrimary(
                resourceSnapshot,
                builtinSnapshot,
                PluginRegistrySource.ResourcesDescriptorsFolder);

            if (registryAsset != null)
            {
                var registrySnapshot = PluginRegistrySnapshot.FromAsset(registryAsset, PluginRegistrySource.RegistryAsset);
                return PluginRegistrySnapshot.MergePreferPrimary(registrySnapshot, resourceSnapshot, PluginRegistrySource.RegistryAsset);
            }

            return resourceSnapshot;
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

        public static PluginRegistrySnapshot MergePreferPrimary(
            PluginRegistrySnapshot primary,
            PluginRegistrySnapshot secondary,
            PluginRegistrySource source)
        {
            var vehicles = MergeById(
                primary != null ? primary.Vehicles : Array.Empty<VehiclePluginDescriptor>(),
                secondary != null ? secondary.Vehicles : Array.Empty<VehiclePluginDescriptor>());
            var tracks = MergeById(
                primary != null ? primary.Tracks : Array.Empty<TrackPluginDescriptor>(),
                secondary != null ? secondary.Tracks : Array.Empty<TrackPluginDescriptor>());

            return new PluginRegistrySnapshot(vehicles, tracks, source);
        }

        private static T[] MergeById<T>(IEnumerable<T> primary, IEnumerable<T> secondary) where T : PluginDescriptorBase
        {
            var merged = new Dictionary<string, T>(StringComparer.Ordinal);

            foreach (var item in primary ?? Array.Empty<T>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                {
                    continue;
                }

                merged[item.id] = item;
            }

            foreach (var item in secondary ?? Array.Empty<T>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || merged.ContainsKey(item.id))
                {
                    continue;
                }

                merged[item.id] = item;
            }

            return merged.Values.OrderBy(item => item.id, StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyList<VehiclePluginDescriptor> VehiclesList => Vehicles;
        public IReadOnlyList<TrackPluginDescriptor> TracksList => Tracks;
        public bool IsEmpty => Vehicles.Length == 0 || Tracks.Length == 0;
    }
}
