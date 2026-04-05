using System.IO;
using System.IO.Compression;
using System.Text;
using UavSimulator.Plugins;
using UnityEditor;
using UnityEngine;

namespace UavSimulator.PluginSDK.Editor
{
    public static class PluginExporter
    {
        [MenuItem("Tools/UavSimulator/Export Plugin (.zip)")]
        public static void ExportPlugin()
        {
            var selected = Selection.activeObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Export Plugin", "No asset selected.\n\nSelect a VehiclePluginDescriptor or TrackPluginDescriptor in the Project window first.", "OK");
                return;
            }

            var vehicleDescriptor = selected as VehiclePluginDescriptor;
            var trackDescriptor = selected as TrackPluginDescriptor;
            PluginDescriptorBase descriptor = vehicleDescriptor != null ? vehicleDescriptor : (PluginDescriptorBase)trackDescriptor;

            if (descriptor == null)
            {
                EditorUtility.DisplayDialog("Export Plugin", "Selected asset is not a plugin descriptor.\n\nSelect a VehiclePluginDescriptor or TrackPluginDescriptor.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(descriptor.id))
            {
                EditorUtility.DisplayDialog("Export Plugin", "Plugin descriptor has no id. Please fill in the id field first.", "OK");
                return;
            }

            var suggestedName = $"{descriptor.id}.rusim-plugin.zip";
            var savePath = EditorUtility.SaveFilePanel("Export Plugin Archive", "", suggestedName, "zip");
            if (string.IsNullOrEmpty(savePath))
                return;

            var pluginType = vehicleDescriptor != null ? "vehicle" : "track";

            var manifest = new PluginManifest
            {
                pluginId = descriptor.id,
                type = pluginType,
                displayName = descriptor.displayName,
                version = descriptor.version.ToString(),
                compatibleRuntime = ">=0.2.0",
                author = "",
            };

            var manifestJson = JsonUtility.ToJson(manifest, true);
            var descriptorJson = JsonUtility.ToJson(descriptor, true);

            string deviceContractJson = null;
            if (vehicleDescriptor != null && vehicleDescriptor.deviceContract != null)
            {
                deviceContractJson = JsonUtility.ToJson(vehicleDescriptor.deviceContract, true);
            }

            if (File.Exists(savePath))
                File.Delete(savePath);

            using (var archive = ZipFile.Open(savePath, ZipArchiveMode.Create))
            {
                AddTextEntry(archive, "manifest.json", manifestJson);
                AddTextEntry(archive, "descriptor.json", descriptorJson);

                if (deviceContractJson != null)
                {
                    AddTextEntry(archive, "device-contract.json", deviceContractJson);
                }

                var readme = GenerateReadme(descriptor, pluginType);
                AddTextEntry(archive, "README.md", readme);
            }

            Debug.Log($"[PluginExporter] Exported plugin '{descriptor.id}' to {savePath}");
            EditorUtility.DisplayDialog("Export Plugin", $"Plugin exported successfully.\n\n{savePath}", "OK");
        }

        private static void AddTextEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
            using (var stream = entry.Open())
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        private static string GenerateReadme(PluginDescriptorBase descriptor, string pluginType)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {descriptor.displayName}");
            sb.AppendLine();
            sb.AppendLine($"- **Plugin ID:** `{descriptor.id}`");
            sb.AppendLine($"- **Type:** {pluginType}");
            sb.AppendLine($"- **Version:** {descriptor.version}");
            if (!string.IsNullOrWhiteSpace(descriptor.description))
            {
                sb.AppendLine();
                sb.AppendLine(descriptor.description);
            }
            sb.AppendLine();
            sb.AppendLine("## Installation");
            sb.AppendLine();
            sb.AppendLine("```bash");
            sb.AppendLine($"rusim plugin install {descriptor.id}.rusim-plugin.zip");
            sb.AppendLine("```");
            return sb.ToString();
        }

        [System.Serializable]
        private class PluginManifest
        {
            public string pluginId;
            public string type;
            public string displayName;
            public string version;
            public string compatibleRuntime;
            public string author;
        }
    }
}
