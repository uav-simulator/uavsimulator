using System.IO;
using UavSimulator.Core;
using UavSimulator.Plugins;
using UavSimulator.Tracks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UavSimulator.EditorTools
{
    public static class PresentationSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/PresentationTrack.unity";

        [MenuItem("UavSimulator/Scene/Build Presentation Track Scene")]
        public static void BuildPresentationScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("PresentationRoot");
            root.AddComponent<PresentationDemoDriver>();
            root.AddComponent<PresentationTelemetryHud>();

            var trackRoot = new GameObject("TrackRoot");
            trackRoot.transform.SetParent(root.transform, false);
            BuiltinPluginFactory.TryCreateTrackInstance(BuiltinPluginFactory.BasicArenaTrackId, trackRoot.transform, out TrackBase track);
            if (track != null)
            {
                track.ResetTrack(seed: 1);
            }

            var vehicleRoot = new GameObject("VehicleRoot");
            vehicleRoot.transform.SetParent(root.transform, false);

            CreateCamera(root.transform);
            CreateLight(root.transform);
            ApplyPresentationColors(root.transform);

            EnsureSceneFolder();
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log($"[PresentationSceneBuilder] Scene saved: {Path.GetFullPath(ScenePath)}");
        }

        private static void EnsureSceneFolder()
        {
            var folder = Path.GetDirectoryName(ScenePath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }
        }

        private static void CreateCamera(Transform parent)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(parent, false);
            camGo.transform.position = new Vector3(0f, 2.6f, -10f);
            camGo.transform.LookAt(new Vector3(0f, 0.3f, -4f));

            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.74f, 0.84f, 0.93f);
            cam.fieldOfView = 52f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 200f;

            camGo.AddComponent<PresentationFollowCamera>();
        }

        private static void CreateLight(Transform parent)
        {
            var lightGo = new GameObject("Directional Light");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.rotation = Quaternion.Euler(48f, -36f, 0f);

            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.98f, 0.95f);
            light.intensity = 1.1f;
        }

        private static void ApplyPresentationColors(Transform root)
        {
            SetColor(root, "Ground", new Color(0.23f, 0.24f, 0.27f), 0.35f);
            SetColor(root, "Landscape", new Color(0.19f, 0.36f, 0.20f), 0.08f);
            SetColor(root, "RoadA", new Color(0.15f, 0.15f, 0.16f), 0.45f);
            SetColor(root, "RoadB", new Color(0.15f, 0.15f, 0.16f), 0.45f);
            SetColor(root, "RoadC", new Color(0.15f, 0.15f, 0.16f), 0.45f);
            SetColor(root, "RoadTurnA", new Color(0.15f, 0.15f, 0.16f), 0.45f);
            SetColor(root, "RoadTurnB", new Color(0.15f, 0.15f, 0.16f), 0.45f);
            SetColor(root, "ShoulderA", new Color(0.25f, 0.24f, 0.20f), 0.12f);
            SetColor(root, "ShoulderB", new Color(0.25f, 0.24f, 0.20f), 0.12f);
            SetColor(root, "ShoulderC", new Color(0.25f, 0.24f, 0.20f), 0.12f);
            SetColor(root, "ShoulderTurnA", new Color(0.25f, 0.24f, 0.20f), 0.12f);
            SetColor(root, "ShoulderTurnB", new Color(0.25f, 0.24f, 0.20f), 0.12f);
            SetColor(root, "DashA", new Color(0.95f, 0.95f, 0.95f));
            SetColor(root, "DashB", new Color(0.95f, 0.95f, 0.95f));
            SetColor(root, "DashC", new Color(0.95f, 0.95f, 0.95f));
            SetColor(root, "EdgeA_Left", new Color(0.95f, 0.95f, 0.95f));
            SetColor(root, "EdgeA_Right", new Color(0.95f, 0.95f, 0.95f));
            SetColor(root, "EdgeB_Down", new Color(0.95f, 0.95f, 0.95f));
            SetColor(root, "EdgeB_Up", new Color(0.95f, 0.95f, 0.95f));
            SetColor(root, "EdgeC_Left", new Color(0.95f, 0.95f, 0.95f));
            SetColor(root, "EdgeC_Right", new Color(0.95f, 0.95f, 0.95f));
            SetColor(root, "Body", new Color(0.84f, 0.16f, 0.14f), 0.32f);
            SetColor(root, "Roof", new Color(0.10f, 0.10f, 0.11f), 0.28f);
            SetColor(root, "CameraPod", new Color(0.80f, 0.80f, 0.82f), 0.18f);
            SetColor(root, "WheelFL", new Color(0.08f, 0.08f, 0.08f), 0.52f);
            SetColor(root, "WheelFR", new Color(0.08f, 0.08f, 0.08f), 0.52f);
            SetColor(root, "WheelRL", new Color(0.08f, 0.08f, 0.08f), 0.52f);
            SetColor(root, "WheelRR", new Color(0.08f, 0.08f, 0.08f), 0.52f);
            SetColor(root, "Trunk", new Color(0.38f, 0.24f, 0.12f), 0.10f);
            SetColor(root, "Crown", new Color(0.18f, 0.45f, 0.18f), 0.12f);
            SetColor(root, "ConeStartL", new Color(0.95f, 0.45f, 0.10f));
            SetColor(root, "ConeStartR", new Color(0.95f, 0.45f, 0.10f));
            SetColor(root, "ConeMidL", new Color(0.95f, 0.45f, 0.10f));
            SetColor(root, "ConeMidR", new Color(0.95f, 0.45f, 0.10f));
            SetColor(root, "ConeFinishL", new Color(0.95f, 0.45f, 0.10f));
            SetColor(root, "ConeFinishR", new Color(0.95f, 0.45f, 0.10f));
        }

        private static void SetColor(Transform root, string objectName, Color color, float smoothness = 0.2f)
        {
            ApplyColorRecursive(root, objectName, color, smoothness);
        }

        private static void ApplyColorRecursive(Transform root, string objectName, Color color, float smoothness)
        {
            if (root.name == objectName)
            {
                var renderer = root.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    material.color = color;
                    material.SetFloat("_Smoothness", smoothness);
                    renderer.sharedMaterial = material;
                }
            }

            for (var i = 0; i < root.childCount; i++)
            {
                ApplyColorRecursive(root.GetChild(i), objectName, color, smoothness);
            }
        }
    }
}
