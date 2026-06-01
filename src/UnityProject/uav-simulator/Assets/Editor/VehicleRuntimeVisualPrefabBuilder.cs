using System.IO;
using UnityEditor;
using UnityEngine;

namespace UavSimulator.EditorTools
{
    /// <summary>
    /// Copies imported showcase vehicle prefabs into Resources so standalone
    /// builds can instantiate the same PROMETEO/ARCADE visuals as Editor runs.
    /// </summary>
    public static class VehicleRuntimeVisualPrefabBuilder
    {
        private const string OutputDir = "Assets/Resources/UavSimulator/Vehicles/Visuals";

        private static readonly VehicleVisualCopy[] Copies =
        {
            new(
                "Assets/PROMETEO - Car Controller/Prefabs/Prometheus.prefab",
                OutputDir + "/prometeo_sport.prefab",
                "PrometeoSportVisual"),
            new(
                "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car Blue Variant.prefab",
                OutputDir + "/arcade_blue.prefab",
                "ArcadeBlueVisual"),
            new(
                "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car Red Variant.prefab",
                OutputDir + "/arcade_red.prefab",
                "ArcadeRedVisual"),
            new(
                "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car Gray Variant.prefab",
                OutputDir + "/arcade_gray.prefab",
                "ArcadeGrayVisual"),
            new(
                "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car Purple Variant.prefab",
                OutputDir + "/arcade_purple.prefab",
                "ArcadePurpleVisual"),
        };

        [MenuItem("UavSimulator/City Showcase/Build Runtime Vehicle Visual Prefabs")]
        public static void BuildFromMenu()
        {
            BuildInternal(exitWhenDone: false);
        }

        public static void BuildBatch()
        {
            BuildInternal(exitWhenDone: true);
        }

        private static void BuildInternal(bool exitWhenDone)
        {
            EnsureFolder(OutputDir);

            for (var i = 0; i < Copies.Length; i++)
            {
                BuildCopy(Copies[i]);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[VehicleRuntimeVisualPrefabBuilder] Saved {Copies.Length} runtime vehicle visual prefabs to {OutputDir}.");

            if (exitWhenDone)
            {
                EditorApplication.Exit(0);
            }
        }

        private static void BuildCopy(VehicleVisualCopy copy)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(copy.SourcePath);
            if (source == null)
            {
                throw new FileNotFoundException($"Vehicle visual source prefab not found: {copy.SourcePath}", copy.SourcePath);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = copy.RootName;
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            PrefabUtility.SaveAsPrefabAsset(instance, copy.OutputPath);
            Object.DestroyImmediate(instance);
            Debug.Log($"[VehicleRuntimeVisualPrefabBuilder] Saved {copy.OutputPath} from {copy.SourcePath}");
        }

        private static void EnsureFolder(string folder)
        {
            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private readonly struct VehicleVisualCopy
        {
            public readonly string SourcePath;
            public readonly string OutputPath;
            public readonly string RootName;

            public VehicleVisualCopy(string sourcePath, string outputPath, string rootName)
            {
                SourcePath = sourcePath;
                OutputPath = outputPath;
                RootName = rootName;
            }
        }
    }
}
