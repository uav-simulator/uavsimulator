using System.Collections.Generic;
using UavSimulator.Plugins;
using UnityEditor;
using UnityEngine;

namespace UavSimulator.PluginSDK.Editor
{
    public static class PluginValidator
    {
        [MenuItem("Tools/UavSimulator/Validate Plugins")]
        public static void ValidateAllPlugins()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var validCount = 0;

            var vehicleGuids = AssetDatabase.FindAssets("t:VehiclePluginDescriptor");
            foreach (var guid in vehicleGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var descriptor = AssetDatabase.LoadAssetAtPath<VehiclePluginDescriptor>(path);
                if (descriptor == null) continue;

                var issues = ValidateVehicle(descriptor, path);
                if (issues.Count == 0)
                {
                    validCount++;
                }
                else
                {
                    errors.AddRange(issues);
                }
            }

            var trackGuids = AssetDatabase.FindAssets("t:TrackPluginDescriptor");
            foreach (var guid in trackGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var descriptor = AssetDatabase.LoadAssetAtPath<TrackPluginDescriptor>(path);
                if (descriptor == null) continue;

                var issues = ValidateTrack(descriptor, path);
                if (issues.Count == 0)
                {
                    validCount++;
                }
                else
                {
                    errors.AddRange(issues);
                }
            }

            var total = vehicleGuids.Length + trackGuids.Length;

            foreach (var error in errors)
            {
                Debug.LogWarning($"[PluginValidator] {error}");
            }

            if (errors.Count == 0)
            {
                Debug.Log($"[PluginValidator] All {total} plugins are valid ({vehicleGuids.Length} vehicles, {trackGuids.Length} tracks).");
                EditorUtility.DisplayDialog(
                    "Plugin Validation",
                    $"All {total} plugins are valid.\n\nVehicles: {vehicleGuids.Length}\nTracks: {trackGuids.Length}",
                    "OK");
            }
            else
            {
                Debug.LogWarning($"[PluginValidator] Found {errors.Count} issue(s) across {total} plugins.");
                EditorUtility.DisplayDialog(
                    "Plugin Validation",
                    $"Found {errors.Count} issue(s) across {total} plugins.\n\nValid: {validCount}\nWith issues: {total - validCount}\n\nSee Console for details.",
                    "OK");
            }
        }

        private static List<string> ValidateVehicle(VehiclePluginDescriptor descriptor, string assetPath)
        {
            var issues = new List<string>();
            var label = $"Vehicle '{assetPath}'";

            if (string.IsNullOrWhiteSpace(descriptor.id))
                issues.Add($"{label}: id is empty");

            if (string.IsNullOrWhiteSpace(descriptor.displayName))
                issues.Add($"{label}: displayName is empty");

            if (!descriptor.version.IsValid)
                issues.Add($"{label}: version is invalid ({descriptor.version})");

            if (descriptor.prefab == null)
                issues.Add($"{label}: prefab is not assigned");

            if (descriptor.deviceContract == null)
            {
                issues.Add($"{label}: deviceContract is not assigned");
            }
            else if (descriptor.deviceContract.descriptor == null)
            {
                issues.Add($"{label}: deviceContract.descriptor is null");
            }
            else
            {
                var contract = descriptor.deviceContract.descriptor;
                if (contract.sensors == null || contract.sensors.Length == 0)
                    issues.Add($"{label}: deviceContract has no sensors");
                if (contract.actuators == null || contract.actuators.Length == 0)
                    issues.Add($"{label}: deviceContract has no actuators");
            }

            return issues;
        }

        private static List<string> ValidateTrack(TrackPluginDescriptor descriptor, string assetPath)
        {
            var issues = new List<string>();
            var label = $"Track '{assetPath}'";

            if (string.IsNullOrWhiteSpace(descriptor.id))
                issues.Add($"{label}: id is empty");

            if (string.IsNullOrWhiteSpace(descriptor.displayName))
                issues.Add($"{label}: displayName is empty");

            if (!descriptor.version.IsValid)
                issues.Add($"{label}: version is invalid ({descriptor.version})");

            if (descriptor.prefab == null)
                issues.Add($"{label}: prefab is not assigned");

            if (!string.IsNullOrWhiteSpace(descriptor.parametersSchemaJson))
            {
                try
                {
                    JsonUtility.FromJson<object>(descriptor.parametersSchemaJson);
                }
                catch
                {
                    issues.Add($"{label}: parametersSchemaJson is not valid JSON");
                }
            }

            return issues;
        }
    }
}
