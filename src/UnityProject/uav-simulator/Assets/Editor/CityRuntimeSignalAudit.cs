using System.Globalization;
using System.IO;
using System.Text;
using UavSimulator.CityDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UavSimulator.EditorTools
{
    public static class CityRuntimeSignalAudit
    {
        private const string RuntimePrefabPath = "Assets/Resources/UavSimulator/City/city_runtime_compact.prefab";
        private const string DemoScenePath = "Assets/POLYGON city pack/scene/DemoScene.unity";

        public static void RunBatch()
        {
            var report = RunAudit();
            Debug.Log(report);
            EditorApplication.Exit(0);
        }

        public static void RunDemoSceneBatch()
        {
            var report = RunDemoSceneAudit();
            Debug.Log(report);
            EditorApplication.Exit(0);
        }

        private static string RunAudit()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
            if (prefab == null)
            {
                var missing = $"[CityRuntimeSignalAudit] Missing runtime prefab: {RuntimePrefabPath}";
                Debug.LogError(missing);
                return missing;
            }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            root.transform.position = Vector3.zero;
            Physics.SyncTransforms();

            var outputDir = ResolveReportAssetsDir();
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, "city-runtime-signal-audit.csv");

            var report = new StringBuilder();
            report.AppendLine("[CityRuntimeSignalAudit] Runtime city traffic-light wiring");
            report.AppendLine($"prefab={RuntimePrefabPath}");
            report.AppendLine($"output={outputPath}");
            report.AppendLine("kind,path,x,y,z,yaw,sizeX,sizeY,sizeZ,lightPath,state,rendererPath");

            foreach (var light in root.GetComponentsInChildren<TrafficLight>(includeInactive: true))
            {
                var rendererPath = ResolveAdapterRendererPath(light);
                AppendRow(
                    report,
                    "light",
                    GetTransformPath(light.transform),
                    light.transform.position,
                    light.transform.rotation.eulerAngles.y,
                    Vector3.zero,
                    "",
                    light.State.ToString(),
                    rendererPath);
            }

            foreach (var zone in root.GetComponentsInChildren<TrafficLightTriggerZone>(includeInactive: true))
            {
                var box = zone.GetComponent<BoxCollider>();
                var size = box != null ? Vector3.Scale(box.size, zone.transform.lossyScale) : Vector3.zero;
                var center = box != null ? zone.transform.TransformPoint(box.center) : zone.transform.position;
                var light = ResolveZoneLight(zone);
                AppendRow(
                    report,
                    "zone",
                    GetTransformPath(zone.transform),
                    center,
                    zone.transform.rotation.eulerAngles.y,
                    size,
                    light != null ? GetTransformPath(light.transform) : "",
                    zone.CurrentState.ToString(),
                    "");
            }

            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                var path = GetTransformPath(renderer.transform);
                if (!ContainsTrafficName(path))
                {
                    continue;
                }

                AppendRow(
                    report,
                    "named-renderer",
                    path,
                    renderer.bounds.center,
                    renderer.transform.rotation.eulerAngles.y,
                    renderer.bounds.size,
                    "",
                    "",
                    "");
            }

            File.WriteAllText(outputPath, report.ToString());
            Object.DestroyImmediate(root);
            return report.ToString();
        }

        private static string RunDemoSceneAudit()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) == null)
            {
                var missing = $"[CityRuntimeSignalAudit] Missing DemoScene: {DemoScenePath}";
                Debug.LogError(missing);
                return missing;
            }

            var scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
            var outputDir = ResolveReportAssetsDir();
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, "city-demoscene-signal-audit.csv");

            var report = new StringBuilder();
            report.AppendLine("[CityRuntimeSignalAudit] POLYGON DemoScene traffic-light renderers");
            report.AppendLine($"scene={DemoScenePath}");
            report.AppendLine($"output={outputPath}");
            report.AppendLine("kind,path,x,y,z,yaw,sizeX,sizeY,sizeZ,lightPath,state,rendererPath");

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
                {
                    var path = GetTransformPath(renderer.transform);
                    if (!ContainsTrafficName(path))
                    {
                        continue;
                    }

                    AppendRow(
                        report,
                        "scene-named-renderer",
                        path,
                        renderer.bounds.center,
                        renderer.transform.rotation.eulerAngles.y,
                        renderer.bounds.size,
                        "",
                        "",
                        "");
                }
            }

            File.WriteAllText(outputPath, report.ToString());
            return report.ToString();
        }

        private static void AppendRow(
            StringBuilder report,
            string kind,
            string path,
            Vector3 position,
            float yaw,
            Vector3 size,
            string lightPath,
            string state,
            string rendererPath)
        {
            report.Append(kind);
            report.Append(',');
            report.Append(Escape(path));
            report.Append(',');
            report.Append(position.x.ToString("0.###", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(position.y.ToString("0.###", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(position.z.ToString("0.###", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(yaw.ToString("0.#", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(size.x.ToString("0.###", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(size.y.ToString("0.###", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(size.z.ToString("0.###", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(Escape(lightPath));
            report.Append(',');
            report.Append(Escape(state));
            report.Append(',');
            report.Append(Escape(rendererPath));
            report.AppendLine();
        }

        private static string ResolveAdapterRendererPath(TrafficLight light)
        {
            foreach (var adapter in light.GetComponents<TrafficLightPolygonAdapter>())
            {
                var serialized = new SerializedObject(adapter);
                var renderer = serialized.FindProperty("trafficLightRenderer")?.objectReferenceValue as MeshRenderer;
                if (renderer != null)
                {
                    return GetTransformPath(renderer.transform);
                }
            }

            return "";
        }

        private static TrafficLight ResolveZoneLight(TrafficLightTriggerZone zone)
        {
            var serialized = new SerializedObject(zone);
            return serialized.FindProperty("trafficLight")?.objectReferenceValue as TrafficLight;
        }

        private static bool ContainsTrafficName(string value)
        {
            var lower = value.ToLowerInvariant();
            return lower.Contains("traffic_light")
                || lower.Contains("traffic light")
                || lower.Contains("trafficlight")
                || lower.Contains("svetofor")
                || lower.Contains("signal");
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string GetTransformPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }

        private static string ResolveReportAssetsDir()
        {
            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
            return Path.Combine(
                repoRoot,
                "docs/report/master-thesis/city-showcase-2026-05-29/assets");
        }
    }
}
