using System;
using System.Collections.Generic;
using System.Linq;
using UavSimulator.Contracts;
using UavSimulator.Plugins;
using UnityEditor;
using UnityEngine;

namespace UavSimulator.EditorTools
{
    public static class PluginCatalogSeeder
    {
        private const string ResourcesRoot = "Assets/Resources/UavSimulator";
        private const string RegistryAssetPath = ResourcesRoot + "/PluginRegistry.asset";
        private const string PluginsRoot = ResourcesRoot + "/Plugins";
        private const string VehiclesRoot = PluginsRoot + "/Vehicles";
        private const string TracksRoot = PluginsRoot + "/Tracks";
        private const string ContractsRoot = ResourcesRoot + "/Contracts";

        [MenuItem("UavSimulator/Plugins/Sync Builtin Plugin Catalog")]
        public static void SyncBuiltinPluginCatalog()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "UavSimulator");
            EnsureFolder(ResourcesRoot, "Plugins");
            EnsureFolder(ResourcesRoot, "Contracts");
            EnsureFolder(PluginsRoot, "Vehicles");
            EnsureFolder(PluginsRoot, "Tracks");

            var snapshot = BuiltinPluginFactory.CreateSnapshot(PluginRegistrySource.RegistryAsset);
            var vehicleAssets = new List<VehiclePluginDescriptor>();
            var trackAssets = new List<TrackPluginDescriptor>();

            foreach (var vehicle in snapshot.Vehicles.Where(item => item != null))
            {
                var contractPath = $"{ContractsRoot}/{ToAssetFileName(vehicle.id)}_contract.asset";
                var contractAsset = LoadOrCreateAsset<DeviceContractDescriptorAsset>(contractPath);
                CopyDeviceContract(vehicle.deviceContract, contractAsset);
                EditorUtility.SetDirty(contractAsset);

                var vehiclePath = $"{VehiclesRoot}/{ToAssetFileName(vehicle.id)}.asset";
                var vehicleAsset = LoadOrCreateAsset<VehiclePluginDescriptor>(vehiclePath);
                CopyPluginDescriptor(vehicle, vehicleAsset);
                vehicleAsset.prefab = vehicle.prefab;
                vehicleAsset.deviceContract = contractAsset;
                EditorUtility.SetDirty(vehicleAsset);
                vehicleAssets.Add(vehicleAsset);
            }

            foreach (var track in snapshot.Tracks.Where(item => item != null))
            {
                var trackPath = $"{TracksRoot}/{ToAssetFileName(track.id)}.asset";
                var trackAsset = LoadOrCreateAsset<TrackPluginDescriptor>(trackPath);
                CopyPluginDescriptor(track, trackAsset);
                trackAsset.prefab = track.prefab;
                trackAsset.parametersSchemaJson = track.parametersSchemaJson;
                EditorUtility.SetDirty(trackAsset);
                trackAssets.Add(trackAsset);
            }

            var registry = LoadOrCreateAsset<PluginRegistryAsset>(RegistryAssetPath);
            registry.vehicles = vehicleAssets.OrderBy(item => item.id, StringComparer.Ordinal).ToArray();
            registry.tracks = trackAssets.OrderBy(item => item.id, StringComparer.Ordinal).ToArray();
            EditorUtility.SetDirty(registry);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[PluginCatalogSeeder] Synced catalog: vehicles={registry.vehicles.Length}, tracks={registry.tracks.Length}, registry={RegistryAssetPath}");
        }

        private static void CopyPluginDescriptor(PluginDescriptorBase source, PluginDescriptorBase target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.id = source.id;
            target.displayName = source.displayName;
            target.version = source.version;
            target.description = source.description;
        }

        private static void CopyDeviceContract(DeviceContractDescriptorAsset source, DeviceContractDescriptorAsset target)
        {
            if (target == null)
            {
                return;
            }

            if (source == null)
            {
                target.contractVersion = default;
                target.descriptor = null;
                return;
            }

            target.contractVersion = source.contractVersion;
            target.descriptor = CloneJson(source.descriptor);
        }

        private static T LoadOrCreateAsset<T>(string assetPath) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static void EnsureFolder(string parentFolder, string childFolderName)
        {
            var childPath = $"{parentFolder}/{childFolderName}";
            if (AssetDatabase.IsValidFolder(childPath))
            {
                return;
            }

            AssetDatabase.CreateFolder(parentFolder, childFolderName);
        }

        private static string ToAssetFileName(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "unnamed_plugin";
            }

            var buffer = id.Trim().ToCharArray();
            for (var i = 0; i < buffer.Length; i++)
            {
                if (char.IsLetterOrDigit(buffer[i]) || buffer[i] == '_' || buffer[i] == '-')
                {
                    continue;
                }

                buffer[i] = '_';
            }

            return new string(buffer);
        }

        private static T CloneJson<T>(T value)
        {
            if (value == null)
            {
                return default;
            }

            return JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        }
    }
}
