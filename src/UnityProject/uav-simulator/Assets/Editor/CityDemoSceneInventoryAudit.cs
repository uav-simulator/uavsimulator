using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UavSimulator.EditorTools
{
    public static class CityDemoSceneInventoryAudit
    {
        private const string DemoScenePath = "Assets/POLYGON city pack/scene/DemoScene.unity";

        public static void RunBatch()
        {
            var report = RunAudit();
            Debug.Log(report);
            EditorApplication.Exit(0);
        }

        private static string RunAudit()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) == null)
            {
                var missing = $"[CityDemoSceneInventoryAudit] Missing DemoScene: {DemoScenePath}";
                Debug.LogError(missing);
                return missing;
            }

            var scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
            var outputDir = ResolveReportAssetsDir();
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, "city-demoscene-inventory-audit.csv");

            var report = new StringBuilder();
            report.AppendLine("[CityDemoSceneInventoryAudit] POLYGON DemoScene inventory");
            report.AppendLine($"scene={DemoScenePath}");
            report.AppendLine($"output={outputPath}");
            report.AppendLine("kind,category,path,x,y,z,sizeX,sizeY,sizeZ");

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
                {
                    var path = GetTransformPath(renderer.transform);
                    var category = Categorize(path);
                    var bounds = renderer.bounds;
                    AppendRow(report, "renderer", category, path, bounds.center, bounds.size);
                }
            }

            File.WriteAllText(outputPath, report.ToString());
            return report.ToString();
        }

        private static string Categorize(string path)
        {
            var lower = path.ToLowerInvariant();
            if (lower.Contains("traffic_light")
                || lower.Contains("traffic light")
                || lower.Contains("trafficlight")
                || lower.Contains("svetofor")
                || lower.Contains("signal"))
            {
                return "traffic-light";
            }

            if (lower.Contains("lamp"))
            {
                return "lamp";
            }

            if (lower.Contains("street")
                || lower.Contains("road")
                || lower.Contains("sideway")
                || lower.Contains("walkway")
                || lower.Contains("stoneway")
                || lower.Contains("crosswalk"))
            {
                return "road";
            }

            if (lower.Contains("building")
                || lower.Contains("bulding")
                || lower.Contains("shop")
                || lower.Contains("bank")
                || lower.Contains("hospital")
                || lower.Contains("police")
                || lower.Contains("gas_station")
                || lower.Contains("motel")
                || lower.Contains("supermaket")
                || lower.Contains("car_repair")
                || lower.Contains("fire_department")
                || lower.Contains("parking_checkout"))
            {
                return "building";
            }

            if (lower.Contains("tree")
                || lower.Contains("bush")
                || lower.Contains("flower")
                || lower.Contains("grass")
                || lower.Contains("hedge"))
            {
                return "vegetation";
            }

            return "prop";
        }

        private static void AppendRow(
            StringBuilder report,
            string kind,
            string category,
            string path,
            Vector3 position,
            Vector3 size)
        {
            report.Append(Escape(kind));
            report.Append(',');
            report.Append(Escape(category));
            report.Append(',');
            report.Append(Escape(path));
            report.Append(',');
            report.Append(position.x.ToString("0.###", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(position.y.ToString("0.###", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(position.z.ToString("0.###", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(size.x.ToString("0.###", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(size.y.ToString("0.###", CultureInfo.InvariantCulture));
            report.Append(',');
            report.Append(size.z.ToString("0.###", CultureInfo.InvariantCulture));
            report.AppendLine();
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
            return Path.Combine(repoRoot, "docs/report/master-thesis/city-showcase-2026-05-29/assets");
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
