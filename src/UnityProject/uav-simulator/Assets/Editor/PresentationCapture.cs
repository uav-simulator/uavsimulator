using System.IO;
using UavSimulator.Plugins;
using UavSimulator.Tracks;
using UavSimulator.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UavSimulator.EditorTools
{
    public static class PresentationCapture
    {
        private const int Width = 1920;
        private const int Height = 1080;

        [MenuItem("UavSimulator/Capture/Presentation Shots")]
        public static void CaptureDefaultShots()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _ = scene;

            var outputDir = GetOutputDirectory();
            Directory.CreateDirectory(outputDir);

            var root = new GameObject("PresentationRoot");
            var trackRoot = new GameObject("TrackRoot");
            trackRoot.transform.SetParent(root.transform, false);
            var vehicleRoot = new GameObject("VehicleRoot");
            vehicleRoot.transform.SetParent(root.transform, false);

            BuiltinPluginFactory.TryCreateTrackInstance(BuiltinPluginFactory.BasicArenaTrackId, trackRoot.transform, out TrackBase track);
            BuiltinPluginFactory.TryCreateVehicleInstance(BuiltinPluginFactory.PrometeoSportVehicleId, vehicleRoot.transform, out VehicleBase vehicle);

            if (track != null)
            {
                track.ResetTrack(seed: 1);
            }

            if (vehicle != null)
            {
                vehicle.ResetVehicle(seed: 1);
            }

            CreateLight();
            ApplyPresentationColors(root.transform);

            var overviewPath = Path.Combine(outputDir, "unity-overview.png");
            var closeupPath = Path.Combine(outputDir, "unity-robot-closeup.png");

            CaptureShot(
                outputPath: overviewPath,
                cameraPosition: new Vector3(-2.0f, 9.2f, -12.5f),
                lookAt: new Vector3(2.8f, 0.2f, -1.4f),
                fieldOfView: 52f);

            CaptureShot(
                outputPath: closeupPath,
                cameraPosition: new Vector3(1.8f, 1.45f, -5.7f),
                lookAt: new Vector3(0.1f, 0.35f, -4.6f),
                fieldOfView: 42f);

            Debug.Log($"[PresentationCapture] Saved overview: {overviewPath}");
            Debug.Log($"[PresentationCapture] Saved closeup: {closeupPath}");
        }

        private static string GetOutputDirectory()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var workspaceRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", "..", ".."));
            return Path.Combine(workspaceRoot, "docs", "reports", "presentation");
        }

        private static void CreateLight()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.98f, 0.95f);
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(48f, -36f, 0f);
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

        private static void CaptureShot(string outputPath, Vector3 cameraPosition, Vector3 lookAt, float fieldOfView)
        {
            var cameraGo = new GameObject("PresentationCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.74f, 0.84f, 0.93f);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 200f;
            camera.transform.position = cameraPosition;
            camera.transform.LookAt(lookAt);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            camera.targetTexture = rt;
            camera.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = prev;

            var png = texture.EncodeToPNG();
            File.WriteAllBytes(outputPath, png);

            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(cameraGo);
        }
    }
}
