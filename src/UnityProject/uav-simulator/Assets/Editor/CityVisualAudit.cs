using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UavSimulator.Core;
using UavSimulator.Plugins;
using UavSimulator.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UavSimulator.EditorTools
{
    public static class CityVisualAudit
    {
        private const string DemoScenePath = "Assets/POLYGON city pack/scene/DemoScene.unity";
        private const int Width = 1920;
        private const int Height = 1080;

        [MenuItem("UavSimulator/City Showcase/Audit City Visuals")]
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
            var outputDir = Path.Combine(Path.GetTempPath(), "uavsim-city-audit");
            Directory.CreateDirectory(outputDir);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) == null)
            {
                var missing = $"[CityVisualAudit] DemoScene not found: {DemoScenePath}";
                Debug.LogError(missing);
                return missing;
            }

            var scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
            EnsureAuditLight();
            var incompatibleBefore = CountIncompatibleMaterials(scene);
            var replacedMaterials = ReplaceIncompatibleMaterials(scene);
            var cityBounds = CalculateSceneBounds(scene);

            var overviewPath = Path.Combine(outputDir, "city-overview.png");
            CaptureOverview(overviewPath, cityBounds);

            var report = new StringBuilder();
            report.AppendLine("[CityVisualAudit] POLYGON city visual audit");
            report.AppendLine($"scene={DemoScenePath}");
            report.AppendLine($"renderPipeline={(RuntimeMaterialCompatibility.IsUrpActive() ? "urp" : "builtin")}");
            report.AppendLine($"rootGameObjects={scene.GetRootGameObjects().Length.ToString(CultureInfo.InvariantCulture)}");
            report.AppendLine($"rendererCount={CountRenderers(scene).ToString(CultureInfo.InvariantCulture)}");
            report.AppendLine($"incompatibleMaterialsBefore={incompatibleBefore.ToString(CultureInfo.InvariantCulture)}");
            report.AppendLine($"replacedMaterials={replacedMaterials.ToString(CultureInfo.InvariantCulture)}");
            report.AppendLine(FormatBounds("cityBounds", cityBounds));
            report.AppendLine($"capture.overview={overviewPath}");

            var hasTrafficLight = TryFindNamedRenderer(scene, new[] { "traffic light", "traffic_light", "trafficlight" }, out var trafficLight);
            var roadAnchor = hasTrafficLight ? trafficLight.bounds.center : Vector3.zero;
            if (TryFindRoadRenderer(scene, roadAnchor, out var roadRenderer))
            {
                var spawn = CreateSpawnPose(roadRenderer);
                var vehicleRoot = new GameObject("CityAuditVehicleRoot");
                if (BuiltinPluginFactory.TryCreateVehicleInstance(BuiltinPluginFactory.PrometeoSportVehicleId, vehicleRoot.transform, out VehicleBase vehicle))
                {
                    vehicle.transform.position = spawn.position;
                    vehicle.transform.rotation = spawn.rotation;
                    FreezeRigidbodies(vehicle.transform);
                    var vehiclePath = Path.Combine(outputDir, "prometeo-in-city.png");
                    CaptureVehicleInCity(vehiclePath, spawn.position, spawn.rotation, roadAnchor, hasTrafficLight);
                    var vehicleCloseupPath = Path.Combine(outputDir, "prometeo-in-city-closeup.png");
                    CaptureVehicleCloseupInCity(vehicleCloseupPath, spawn.position, spawn.rotation);
                    report.AppendLine($"roadRenderer={GetTransformPath(roadRenderer.transform)}");
                    report.AppendLine(FormatVector("vehicleSpawn", spawn.position));
                    report.AppendLine(FormatVector("vehicleYawEuler", spawn.rotation.eulerAngles));
                    report.AppendLine($"capture.prometeoInCity={vehiclePath}");
                    report.AppendLine($"capture.prometeoInCityCloseup={vehicleCloseupPath}");
                }
                else
                {
                    report.AppendLine("vehicle=FAILED_TO_CREATE_PROMETEO");
                }
            }
            else
            {
                report.AppendLine("roadRenderer=NOT_FOUND");
            }

            if (hasTrafficLight)
            {
                var trafficLightPath = Path.Combine(outputDir, "traffic-light-closeup.png");
                CaptureCloseup(trafficLightPath, trafficLight.bounds.center, 2.4f, 1.3f, 32f);
                report.AppendLine($"trafficLightRenderer={GetTransformPath(trafficLight.transform)}");
                report.AppendLine($"capture.trafficLight={trafficLightPath}");
            }

            var reportPath = Path.Combine(outputDir, "city-visual-audit.txt");
            File.WriteAllText(reportPath, report.ToString());
            report.AppendLine($"report={reportPath}");

            return report.ToString();
        }

        private static void CaptureOverview(string outputPath, Bounds bounds)
        {
            var lookAt = bounds.center;
            lookAt.y = Mathf.Max(bounds.min.y + 2.5f, bounds.center.y);
            var radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            var cameraPosition = lookAt + new Vector3(radius * 0.72f, Mathf.Max(42f, bounds.extents.y * 1.25f), -radius * 0.72f);
            CaptureShot(outputPath, cameraPosition, lookAt, 43f, Mathf.Max(800f, radius * 4f));
        }

        private static void CaptureVehicleInCity(
            string outputPath,
            Vector3 vehiclePosition,
            Quaternion vehicleRotation,
            Vector3 secondaryTarget,
            bool hasSecondaryTarget)
        {
            var forward = vehicleRotation * Vector3.forward;
            var viewDirection = forward;
            if (hasSecondaryTarget)
            {
                var toTarget = secondaryTarget - vehiclePosition;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    viewDirection = toTarget.normalized;
                }
            }

            var right = Vector3.Cross(Vector3.up, viewDirection).normalized;
            var carFocus = vehiclePosition + Vector3.up * 0.55f;
            var targetFocus = hasSecondaryTarget ? secondaryTarget + Vector3.up * 0.7f : carFocus + viewDirection * 1.8f;
            var lookAt = Vector3.Lerp(carFocus, targetFocus, hasSecondaryTarget ? 0.42f : 0.18f);
            var cameraPosition = vehiclePosition - viewDirection * 4.1f + right * 1.6f + Vector3.up * 1.55f;
            CaptureShot(outputPath, cameraPosition, lookAt, 43f, 220f);
        }

        private static void CaptureCloseup(string outputPath, Vector3 target, float distance, float height, float fieldOfView)
        {
            var lookAt = target + Vector3.up * 0.45f;
            var cameraPosition = lookAt + new Vector3(distance, height, -distance);
            CaptureShot(outputPath, cameraPosition, lookAt, fieldOfView, 160f);
        }

        private static void CaptureVehicleCloseupInCity(string outputPath, Vector3 vehiclePosition, Quaternion vehicleRotation)
        {
            var forward = vehicleRotation * Vector3.forward;
            var right = vehicleRotation * Vector3.right;
            var lookAt = vehiclePosition + Vector3.up * 0.35f;
            var cameraPosition = vehiclePosition - forward * 4.0f + right * 1.2f + Vector3.up * 1.2f;
            CaptureShot(outputPath, cameraPosition, lookAt, 36f, 180f);
        }

        private static void CaptureShot(string outputPath, Vector3 cameraPosition, Vector3 lookAt, float fieldOfView, float farClip)
        {
            var cameraGo = new GameObject("CityAuditCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.71f, 0.82f, 0.91f);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = farClip;
            camera.allowHDR = false;
            camera.allowMSAA = false;
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
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(cameraGo);
        }

        private static (Vector3 position, Quaternion rotation) CreateSpawnPose(Renderer roadRenderer)
        {
            var bounds = roadRenderer.bounds;
            var yaw = bounds.size.x > bounds.size.z ? 90f : 0f;
            var position = new Vector3(bounds.center.x, bounds.max.y + 0.22f, bounds.center.z);
            return (position, Quaternion.Euler(0f, yaw, 0f));
        }

        private static bool TryFindRoadRenderer(Scene scene, Vector3 anchor, out Renderer renderer)
        {
            var candidates = EnumerateSceneRenderers(scene)
                .Where(r => IsMeshRenderer(r) && ContainsAny(GetTransformPath(r.transform), new[] { "street", "road" }))
                .Where(r => Mathf.Max(r.bounds.size.x, r.bounds.size.z) >= 2f)
                .OrderBy(r => HorizontalDistanceSq(r.bounds.ClosestPoint(anchor), anchor))
                .ThenByDescending(r => r.bounds.size.x * r.bounds.size.z)
                .ToList();

            renderer = candidates.FirstOrDefault();
            return renderer != null;
        }

        private static bool TryFindNamedRenderer(Scene scene, string[] tokens, out Renderer renderer)
        {
            renderer = EnumerateSceneRenderers(scene)
                .Where(r => IsMeshRenderer(r) && ContainsAny(GetTransformPath(r.transform), tokens))
                .OrderBy(r => new Vector2(r.bounds.center.x, r.bounds.center.z).sqrMagnitude)
                .FirstOrDefault();
            return renderer != null;
        }

        private static Bounds CalculateSceneBounds(Scene scene)
        {
            var renderers = EnumerateSceneRenderers(scene)
                .Where(IsMeshRenderer)
                .Where(r => r.bounds.size.sqrMagnitude > 0.0001f)
                .ToList();
            if (renderers.Count == 0)
            {
                return new Bounds(Vector3.zero, new Vector3(40f, 12f, 40f));
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Count; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static int ReplaceIncompatibleMaterials(Scene scene)
        {
            var count = 0;
            foreach (var renderer in EnumerateSceneRenderers(scene))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (!RuntimeMaterialCompatibility.NeedsReplacement(source))
                    {
                        continue;
                    }

                    var replacement = RuntimeMaterialCompatibility.CreateReplacementMaterial(source, defaultSmoothness: 0.2f, copyTextures: true);
                    replacement.color = RuntimeMaterialCompatibility.ReadSourceColor(source);
                    materials[i] = replacement;
                    changed = true;
                    count++;
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }

            return count;
        }

        private static int CountIncompatibleMaterials(Scene scene)
        {
            var count = 0;
            foreach (var renderer in EnumerateSceneRenderers(scene))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (RuntimeMaterialCompatibility.NeedsReplacement(material))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static int CountRenderers(Scene scene)
            => EnumerateSceneRenderers(scene).Count();

        private static System.Collections.Generic.IEnumerable<Renderer> EnumerateSceneRenderers(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
                {
                    if (renderer != null)
                    {
                        yield return renderer;
                    }
                }
            }
        }

        private static void EnsureAuditLight()
        {
            var lightGo = new GameObject("CityAuditDirectionalLight");
            lightGo.transform.rotation = Quaternion.Euler(48f, -34f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.98f, 0.94f);
        }

        private static void FreezeRigidbodies(Transform root)
        {
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                body.isKinematic = true;
            }
        }

        private static bool IsMeshRenderer(Renderer renderer)
            => renderer is MeshRenderer or SkinnedMeshRenderer;

        private static float HorizontalDistanceSq(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static bool ContainsAny(string value, string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return tokens.Any(token => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetTransformPath(Transform target)
        {
            var names = new System.Collections.Generic.List<string>();
            var current = target;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static string FormatBounds(string name, Bounds bounds)
            => $"{name}.center=({bounds.center.x:0.###},{bounds.center.y:0.###},{bounds.center.z:0.###}) " +
               $"{name}.size=({bounds.size.x:0.###},{bounds.size.y:0.###},{bounds.size.z:0.###})";

        private static string FormatVector(string name, Vector3 vector)
            => $"{name}=({vector.x:0.###},{vector.y:0.###},{vector.z:0.###})";
    }
}
