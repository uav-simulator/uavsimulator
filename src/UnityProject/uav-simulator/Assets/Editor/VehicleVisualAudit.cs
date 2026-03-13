using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UavSimulator.Plugins;
using UavSimulator.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace UavSimulator.EditorTools
{
    public static class VehicleVisualAudit
    {
        private const int CaptureWidth = 1600;
        private const int CaptureHeight = 900;

        [MenuItem("UavSimulator/Debug/Audit Vehicle Visuals")]
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
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _ = scene;

            var outputDir = Path.Combine(Path.GetTempPath(), "uavsim-visual-audit");
            Directory.CreateDirectory(outputDir);

            var root = new GameObject("VehicleAuditRoot");
            CreateLight(root.transform);
            CreateGround(root.transform);

            var vehicleIds = new[]
            {
                BuiltinPluginFactory.Ks0223ArcadeBlueVehicleId,
                BuiltinPluginFactory.Ks0223ArcadeRedVehicleId,
                BuiltinPluginFactory.Ks0223ArcadeGrayVehicleId,
                BuiltinPluginFactory.Ks0223ArcadePurpleVehicleId,
            };

            var report = new StringBuilder();
            report.AppendLine("[VehicleVisualAudit] builtin vehicle visual audit");
            report.AppendLine($"renderPipeline={(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "builtin")}");
            report.AppendLine($"isUrpActive={IsUrpActive()}");
            report.AppendLine();

            for (var i = 0; i < vehicleIds.Length; i++)
            {
                var descriptorId = vehicleIds[i];
                var parent = new GameObject($"AuditVehicle_{i + 1}");
                parent.transform.SetParent(root.transform, false);
                parent.transform.position = new Vector3(-6f + (i * 4f), 0f, 0f);

                if (!BuiltinPluginFactory.TryCreateVehicleInstance(descriptorId, parent.transform, out var vehicle))
                {
                    report.AppendLine($"{descriptorId}: FAILED_TO_CREATE");
                    continue;
                }

                if (vehicle is Ks0223Vehicle ks0223Vehicle)
                {
                    EnsureInternalPresentationVisuals(ks0223Vehicle);
                    ks0223Vehicle.ResetVehicle(seed: 1);
                }

                report.AppendLine($"vehicle={descriptorId}");
                AppendRendererAudit(report, parent.transform);
                report.AppendLine();
            }

            var capturePath = Path.Combine(outputDir, "vehicle-variants.png");
            CaptureShot(capturePath, new Vector3(0f, 3.5f, -11f), new Vector3(0f, 0.8f, 0f), 34f);
            report.AppendLine($"capture={capturePath}");

            var reportPath = Path.Combine(outputDir, "vehicle-visual-audit.txt");
            File.WriteAllText(reportPath, report.ToString());
            report.AppendLine($"report={reportPath}");

            return report.ToString();
        }

        private static void AppendRendererAudit(StringBuilder report, Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (renderer == null)
                {
                    continue;
                }

                var materials = renderer.sharedMaterials ?? Array.Empty<Material>();
                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    var shader = material != null ? material.shader : null;
                    var shaderName = shader != null ? shader.name : "<null>";
                    var supported = shader != null && shader.isSupported;
                    var compatible = IsBuiltinCompatibleShader(shader);
                    var color = material != null ? ReadSourceColor(material) : Color.magenta;
                    report.AppendLine(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "  renderer={0} slot={1} material={2} shader={3} supported={4} builtinCompatible={5} color=({6:0.###},{7:0.###},{8:0.###},{9:0.###})",
                            GetTransformPath(renderer.transform, root),
                            i,
                            material != null ? material.name : "<null>",
                            shaderName,
                            supported,
                            compatible,
                            color.r,
                            color.g,
                            color.b,
                            color.a));
                }
            }
        }

        private static string GetTransformPath(Transform target, Transform root)
        {
            if (target == null)
            {
                return "<null>";
            }

            var names = new System.Collections.Generic.List<string>();
            var current = target;
            while (current != null)
            {
                names.Add(current.name);
                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static void CreateLight(Transform parent)
        {
            var lightRoot = new GameObject("Directional Light");
            lightRoot.transform.SetParent(parent, false);
            lightRoot.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
            var light = lightRoot.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.98f, 0.95f);
        }

        private static void CreateGround(Transform parent)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(4f, 1f, 2f);
            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = new Material(ResolveRuntimeLitShader());
                ApplyColor(material, new Color(0.22f, 0.24f, 0.26f));
                ApplySmoothness(material, 0.18f);
                renderer.sharedMaterial = material;
            }
        }

        private static void CaptureShot(string outputPath, Vector3 cameraPosition, Vector3 lookAt, float fieldOfView)
        {
            var cameraGo = new GameObject("AuditCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.71f, 0.82f, 0.91f);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 250f;
            camera.transform.position = cameraPosition;
            camera.transform.LookAt(lookAt);

            var rt = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);

            camera.targetTexture = rt;
            camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(outputPath, texture.EncodeToPNG());

            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(cameraGo);
        }

        private static bool IsUrpActive() => GraphicsSettings.currentRenderPipeline != null;

        private static bool IsBuiltinCompatibleShader(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            var name = shader.name ?? string.Empty;
            return name.StartsWith("Standard", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Unlit/", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Legacy Shaders/", StringComparison.OrdinalIgnoreCase);
        }

        private static Shader ResolveRuntimeLitShader()
        {
            if (IsUrpActive())
            {
                var urp = Shader.Find("Universal Render Pipeline/Lit");
                if (urp != null && urp.isSupported)
                {
                    return urp;
                }
            }

            var standard = Shader.Find("Standard");
            if (standard != null && standard.isSupported)
            {
                return standard;
            }

            var unlitColor = Shader.Find("Unlit/Color");
            if (unlitColor != null && unlitColor.isSupported)
            {
                return unlitColor;
            }

            var legacy = Shader.Find("Legacy Shaders/Diffuse");
            if (legacy != null)
            {
                return legacy;
            }

            throw new MissingReferenceException("Unable to resolve a supported shader for visual audit.");
        }

        private static void ApplyColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static void ApplySmoothness(Material material, float smoothness)
        {
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }
        }

        private static Color ReadSourceColor(Material source)
        {
            if (source == null)
            {
                return Color.white;
            }

            if (source.HasProperty("_BaseColor"))
            {
                return source.GetColor("_BaseColor");
            }

            if (source.HasProperty("_Color"))
            {
                return source.GetColor("_Color");
            }

            return source.color;
        }

        private static void EnsureInternalPresentationVisuals(Ks0223Vehicle vehicle)
        {
            if (vehicle == null)
            {
                return;
            }

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var ensureMethod = typeof(Ks0223Vehicle).GetMethod("EnsurePresentationVisuals", flags);
            ensureMethod?.Invoke(vehicle, null);

            var paletteMethod = typeof(Ks0223Vehicle).GetMethod("ApplyVisualPalette", flags);
            paletteMethod?.Invoke(vehicle, null);
        }
    }
}
