using System.Globalization;
using System.IO;
using System.Text;
using UavSimulator.CityDemo;
using UnityEditor;
using UnityEngine;

namespace UavSimulator.EditorTools
{
    public static class CityRuntimePrefabAudit
    {
        private const string RuntimePrefabPath = "Assets/Resources/UavSimulator/City/city_runtime_compact.prefab";

        public static void AuditRoadCoverageBatch()
        {
            var report = AuditRoadCoverage();
            Debug.Log(report);
            EditorApplication.Exit(0);
        }

        public static void AuditRouteExpansionBatch()
        {
            var report = AuditRouteExpansion();
            Debug.Log(report);
            EditorApplication.Exit(0);
        }

        private static string AuditRoadCoverage()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
            if (prefab == null)
            {
                var missing = $"[CityRuntimePrefabAudit] Missing runtime prefab: {RuntimePrefabPath}";
                Debug.LogError(missing);
                return missing;
            }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            root.transform.position = Vector3.zero;
            Physics.SyncTransforms();

            var report = new StringBuilder();
            report.AppendLine("[CityRuntimePrefabAudit] Road coverage");
            report.AppendLine($"prefab={RuntimePrefabPath}");
            report.AppendLine("sample,x,z,hit,hitY,collider");

            var index = 0;
            for (var z = -13f; z <= 13.001f; z += 0.5f)
            {
                AppendSample(report, index++, $"center_ns_{z.ToString("0.0", CultureInfo.InvariantCulture)}", new Vector3(0f, 3f, z));
            }

            for (var x = -13f; x <= 13.001f; x += 0.5f)
            {
                AppendSample(report, index++, $"center_ew_{x.ToString("0.0", CultureInfo.InvariantCulture)}", new Vector3(x, 3f, 0f));
            }

            var routeSamples = new[]
            {
                new Vector3(0f, 3f, -12f),
                new Vector3(0f, 3f, -2f),
                new Vector3(10f, 3f, 0f),
                new Vector3(2f, 3f, 0f),
                new Vector3(0f, 3f, 10f),
                new Vector3(0f, 3f, 2f),
                new Vector3(-10f, 3f, 0f),
                new Vector3(-2f, 3f, 0f),
            };

            for (var i = 0; i < routeSamples.Length; i++)
            {
                AppendSample(report, index++, $"route_{i}", routeSamples[i]);
            }

            var outputDir = Path.Combine(Path.GetTempPath(), "uavsim-city-audit");
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, "city-runtime-road-coverage.csv");
            File.WriteAllText(outputPath, report.ToString());
            report.AppendLine($"report={outputPath}");

            Object.DestroyImmediate(root);
            return report.ToString();
        }

        private static string AuditRouteExpansion()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
            if (prefab == null)
            {
                var missing = $"[CityRuntimePrefabAudit] Missing runtime prefab: {RuntimePrefabPath}";
                Debug.LogError(missing);
                return missing;
            }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            root.transform.position = Vector3.zero;
            Physics.SyncTransforms();

            var outputDir = ResolveReportAssetsDir();
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, "city-route-expansion-road-audit.csv");

            var report = new StringBuilder();
            report.AppendLine("[CityRuntimePrefabAudit] Route expansion road audit");
            report.AppendLine($"prefab={RuntimePrefabPath}");
            report.AppendLine($"output={outputPath}");
            report.AppendLine("route,segment,sample,x,z,hit,hitY,collider,nearestStopZone,nearestStopZoneDistanceM");

            var index = 0;
            AppendRouteSamples(
                report,
                "current_showcase_route",
                new[]
                {
                    new Vector3(-4f, 3f, -20f),
                    new Vector3(-4f, 3f, -12f),
                    new Vector3(-4f, 3f, -4f),
                    new Vector3(-4f, 3f, 6f),
                    new Vector3(-4f, 3f, 18f),
                },
                root,
                ref index);

            AppendRouteSamples(
                report,
                "candidate_west_second_signal_route",
                new[]
                {
                    new Vector3(-4f, 3f, 16f),
                    new Vector3(-18f, 3f, 16f),
                    new Vector3(-38f, 3f, 16f),
                    new Vector3(-58f, 3f, 16f),
                    new Vector3(-77f, 3f, 3f),
                },
                root,
                ref index);

            File.WriteAllText(outputPath, report.ToString());
            Object.DestroyImmediate(root);
            return report.ToString();
        }

        private static void AppendRouteSamples(
            StringBuilder report,
            string routeName,
            Vector3[] waypoints,
            GameObject root,
            ref int globalIndex)
        {
            for (var segment = 0; segment < waypoints.Length - 1; segment++)
            {
                var from = waypoints[segment];
                var to = waypoints[segment + 1];
                var distance = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
                var steps = Mathf.Max(1, Mathf.CeilToInt(distance / 2f));
                for (var i = 0; i <= steps; i++)
                {
                    var t = i / (float)steps;
                    var sample = Vector3.Lerp(from, to, t);
                    AppendRouteSample(report, routeName, segment, globalIndex++, sample, root);
                }
            }
        }

        private static void AppendRouteSample(
            StringBuilder report,
            string routeName,
            int segment,
            int sampleIndex,
            Vector3 origin,
            GameObject root)
        {
            var hit = Physics.Raycast(
                origin,
                Vector3.down,
                out var hitInfo,
                8f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var hitY = hit ? hitInfo.point.y.ToString("0.000", CultureInfo.InvariantCulture) : "";
            var colliderName = hit ? Escape(GetTransformPath(hitInfo.collider.transform)) : "";
            var nearestZone = ResolveNearestStopZone(root, origin, out var nearestDistance);
            report.AppendLine(string.Join(
                ",",
                Escape(routeName),
                segment.ToString(CultureInfo.InvariantCulture),
                sampleIndex.ToString(CultureInfo.InvariantCulture),
                origin.x.ToString("0.000", CultureInfo.InvariantCulture),
                origin.z.ToString("0.000", CultureInfo.InvariantCulture),
                hit ? "true" : "false",
                hitY,
                colliderName,
                Escape(nearestZone),
                nearestDistance.ToString("0.000", CultureInfo.InvariantCulture)));
        }

        private static string ResolveNearestStopZone(GameObject root, Vector3 origin, out float nearestDistance)
        {
            nearestDistance = float.PositiveInfinity;
            var nearestPath = "";
            foreach (var zone in root.GetComponentsInChildren<TrafficLightTriggerZone>(includeInactive: true))
            {
                var delta = zone.transform.position - origin;
                delta.y = 0f;
                var distance = delta.magnitude;
                if (distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = distance;
                nearestPath = GetTransformPath(zone.transform);
            }

            return nearestPath;
        }

        private static void AppendSample(StringBuilder report, int index, string label, Vector3 origin)
        {
            var hit = Physics.Raycast(
                origin,
                Vector3.down,
                out var hitInfo,
                8f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var hitY = hit ? hitInfo.point.y.ToString("0.000", CultureInfo.InvariantCulture) : "";
            var colliderName = hit ? GetTransformPath(hitInfo.collider.transform) : "";
            report.AppendLine(string.Join(
                ",",
                index.ToString(CultureInfo.InvariantCulture),
                label,
                origin.x.ToString("0.000", CultureInfo.InvariantCulture),
                origin.z.ToString("0.000", CultureInfo.InvariantCulture),
                hit ? "true" : "false",
                hitY,
                colliderName));
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
    }
}
