using System.Globalization;
using System.IO;
using System.Text;
using UavSimulator.Core;
using UavSimulator.Plugins;
using UavSimulator.Tracks;
using UavSimulator.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UavSimulator.EditorTools
{
    public static class TrackVisualAudit
    {
        private const int Width = 1920;
        private const int Height = 1080;

        [MenuItem("UavSimulator/Debug/Audit Realistic Track Render")]
        public static void AuditFromMenu()
        {
            var report = RunAuditInternal();
            Debug.Log(report);
        }

        public static void RunBatchAudit()
        {
            var report = RunAuditInternal();
            Debug.Log(report);
            EditorApplication.Exit(0);
        }

        private static string RunAuditInternal()
        {
            var outputDir = Path.Combine(Path.GetTempPath(), "uavsim-track-audit");
            Directory.CreateDirectory(outputDir);

            Transform auditRoot = null;
            var incompatibleCount = 0;
            if (Application.isPlaying)
            {
                incompatibleCount = CountIncompatibleMaterials(SceneManager.GetActiveScene());
            }
            else
            {
                auditRoot = PrepareAuditRoot();
                incompatibleCount = CountIncompatibleMaterials(auditRoot);
            }

            var capturePath = Path.Combine(outputDir, "realistic-track-overview.png");
            CaptureShot(capturePath, new Vector3(0f, 8.8f, -18f), new Vector3(0f, 0.7f, -2f), 48f);

            var report = new StringBuilder();
            report.AppendLine("[TrackVisualAudit] realistic track audit");
            report.AppendLine($"renderPipeline={(RuntimeMaterialCompatibility.IsUrpActive() ? "urp" : "builtin")}");
            report.AppendLine($"incompatibleMaterialCount={incompatibleCount.ToString(CultureInfo.InvariantCulture)}");
            report.AppendLine($"capture={capturePath}");

            var reportPath = Path.Combine(outputDir, "realistic-track-audit.txt");
            File.WriteAllText(reportPath, report.ToString());
            report.AppendLine($"report={reportPath}");

            if (!Application.isPlaying && auditRoot != null)
            {
                Object.DestroyImmediate(auditRoot.gameObject);
            }

            return report.ToString();
        }

        private static Transform PrepareAuditRoot()
        {
            _ = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("TrackAuditRoot").transform;
            var trackRoot = new GameObject("TrackRoot").transform;
            trackRoot.SetParent(root, false);
            var vehicleRoot = new GameObject("VehicleRoot").transform;
            vehicleRoot.SetParent(root, false);

            BuiltinPluginFactory.TryCreateTrackInstance(BuiltinPluginFactory.RoadSystemRealisticTrackId, trackRoot, out TrackBase track);
            BuiltinPluginFactory.TryCreateVehicleInstance(BuiltinPluginFactory.Ks0223ArcadeBlueVehicleId, vehicleRoot, out VehicleBase vehicle);

            track?.ResetTrack(seed: 1);
            vehicle?.ResetVehicle(seed: 1);

            return root;
        }

        private static int CountIncompatibleMaterials(Transform root)
        {
            var count = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (renderer == null)
                {
                    continue;
                }

                foreach (var material in renderer.sharedMaterials)
                {
                    if (RuntimeMaterialCompatibility.NeedsReplacement(material))
                    {
                        count += 1;
                    }
                }
            }

            return count;
        }

        private static int CountIncompatibleMaterials(Scene scene)
        {
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null)
                {
                    continue;
                }

                count += CountIncompatibleMaterials(root.transform);
            }

            return count;
        }

        private static void CaptureShot(string outputPath, Vector3 cameraPosition, Vector3 lookAt, float fieldOfView)
        {
            var cameraGo = new GameObject("TrackAuditCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.71f, 0.82f, 0.91f);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 400f;
            camera.transform.position = cameraPosition;
            camera.transform.LookAt(lookAt);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            camera.targetTexture = rt;
            camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(outputPath, texture.EncodeToPNG());

            camera.targetTexture = null;
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(cameraGo);
        }
    }
}
