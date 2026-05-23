using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.Sentis;

namespace UavSimulator.EditorTools
{
    /// <summary>
    /// One-shot setup helper for the city showcase pipeline:
    ///  1. Force-imports the traffic-light ONNX so Sentis converts it to a ModelAsset.
    ///  2. Verifies the ModelAsset is loadable via Resources.Load.
    ///
    /// Invoked manually via menu or in batchmode:
    ///   Unity -batchmode -projectPath ... -executeMethod \
    ///     UavSimulator.EditorTools.CityShowcaseSetup.Run -quit
    /// </summary>
    public static class CityShowcaseSetup
    {
        private const string OnnxAssetPath = "Assets/Resources/Models/tl-classifier.onnx";
        private const string ResourcesLoadPath = "Models/tl-classifier";

        [MenuItem("UavSimulator/City Showcase/Run Setup")]
        public static void Run()
        {
            Debug.Log("[CityShowcaseSetup] starting...");

            if (!File.Exists(OnnxAssetPath))
            {
                Debug.LogError($"[CityShowcaseSetup] ONNX not found at {OnnxAssetPath}; aborting.");
                EditorApplication.Exit(2);
                return;
            }

            // Trigger Sentis importer for the .onnx file.
            AssetDatabase.ImportAsset(OnnxAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CityShowcaseSetup] AssetDatabase refreshed.");

            // Verify the ModelAsset is reachable via Resources.Load.
            var modelAsset = Resources.Load<ModelAsset>(ResourcesLoadPath);
            if (modelAsset == null)
            {
                Debug.LogError(
                    $"[CityShowcaseSetup] Failed to Resources.Load<ModelAsset>('{ResourcesLoadPath}'); " +
                    "either Sentis did not import the .onnx (check importer logs) or the path is wrong.");
                EditorApplication.Exit(3);
                return;
            }
            Debug.Log($"[CityShowcaseSetup] OK — ModelAsset '{modelAsset.name}' loaded via Resources.");
            EditorApplication.Exit(0);
        }
    }
}
