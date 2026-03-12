using UavSimulator.Api;
using UavSimulator.Tracks;
using UnityEngine.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UavSimulator.Core
{
    public static class RuntimeSceneBootstrap
    {
        private const string RoadSystemSceneName = "RoadSystemTrack";
        private static bool sceneHookInstalled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureRuntimeObjects()
        {
            ConfigureRuntimeExecution();
            var manager = EnsureSimulationManager();
            InstallSceneHooks();
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            EnsureApiHost();
            EnsureRos2BridgeHostIfEnabled();
        }

        private static void ConfigureRuntimeExecution()
        {
            // Keep simulation/API responsive even when Unity window is not focused.
            Application.runInBackground = true;
        }

        private static SimulationManager EnsureSimulationManager()
        {
            var manager = Object.FindFirstObjectByType<SimulationManager>();
            if (manager == null)
            {
                var go = new GameObject(nameof(SimulationManager));
                Object.DontDestroyOnLoad(go);
                manager = go.AddComponent<SimulationManager>();
            }

            BindSceneRoots(manager);
            return manager;
        }

        private static void BindSceneRoots(SimulationManager manager)
        {
            if (manager == null)
            {
                return;
            }

            var trackRoot = GameObject.Find("TrackRoot")?.transform;
            var vehicleRoot = GameObject.Find("VehicleRoot")?.transform;
            if (trackRoot == null && vehicleRoot == null)
            {
                return;
            }

            manager.ConfigureRoots(trackRoot, vehicleRoot);
            manager.RemoveSceneVehiclePlaceholders();
        }

        private static void InstallSceneHooks()
        {
            if (sceneHookInstalled)
            {
                return;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            sceneHookInstalled = true;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _ = mode;

            var manager = EnsureSimulationManager();
            EnsureSceneMaterialCompatibility(scene);
            DisableLegacySceneGround(scene);
            EnsureSceneSpecificTrack(scene);
            BindSceneRoots(manager);
        }

        private static void EnsureSceneMaterialCompatibility(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            var fallbackShader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            if (fallbackShader == null)
            {
                return;
            }

            var replacements = new System.Collections.Generic.Dictionary<Material, Material>();
            var roots = scene.GetRootGameObjects();
            for (var r = 0; r < roots.Length; r++)
            {
                var renderers = roots[r].GetComponentsInChildren<Renderer>(includeInactive: true);
                for (var i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    var sharedMaterials = renderer.sharedMaterials;
                    var changed = false;
                    for (var m = 0; m < sharedMaterials.Length; m++)
                    {
                        var source = sharedMaterials[m];
                        if (source == null)
                        {
                            continue;
                        }

                        var shader = source.shader;
                        var unsupported = shader == null || !shader.isSupported;
                        var builtinIncompatible = !IsUrpActive() && !IsBuiltinCompatibleShader(shader);
                        if (!unsupported && !builtinIncompatible)
                        {
                            continue;
                        }

                        if (!replacements.TryGetValue(source, out var replacement))
                        {
                            replacement = new Material(fallbackShader)
                            {
                                name = $"{source.name}_BuiltinFallback",
                                color = ReadSourceColor(source),
                            };

                            var sourceTexture = ReadSourceTexture(source);
                            if (sourceTexture != null)
                            {
                                if (replacement.HasProperty("_MainTex"))
                                {
                                    replacement.SetTexture("_MainTex", sourceTexture);
                                }

                                if (replacement.HasProperty("_BaseMap"))
                                {
                                    replacement.SetTexture("_BaseMap", sourceTexture);
                                }
                            }

                            if (replacement.HasProperty("_Smoothness"))
                            {
                                var smoothness = source.HasProperty("_Smoothness") ? source.GetFloat("_Smoothness") : 0.2f;
                                replacement.SetFloat("_Smoothness", smoothness);
                            }

                            replacements[source] = replacement;
                        }

                        sharedMaterials[m] = replacement;
                        changed = true;
                    }

                    if (changed)
                    {
                        renderer.sharedMaterials = sharedMaterials;
                    }
                }
            }
        }

        private static Texture ReadSourceTexture(Material source)
        {
            if (source == null)
            {
                return null;
            }

            if (source.mainTexture != null)
            {
                return source.mainTexture;
            }

            if (source.HasProperty("_BaseMap"))
            {
                return source.GetTexture("_BaseMap");
            }

            if (source.HasProperty("_MainTex"))
            {
                return source.GetTexture("_MainTex");
            }

            return null;
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

        private static bool IsUrpActive()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
            {
                return false;
            }

            var name = pipeline.GetType().Name;
            return name.Contains("UniversalRenderPipeline", System.StringComparison.Ordinal) ||
                   name.Contains("URP", System.StringComparison.Ordinal);
        }

        private static bool IsUrpShader(Shader shader)
        {
            var name = shader != null ? shader.name : string.Empty;
            return name.StartsWith("Universal Render Pipeline/", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBuiltinCompatibleShader(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            var name = shader.name ?? string.Empty;
            if (name.StartsWith("Standard", System.StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Legacy Shaders/", System.StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Unlit/", System.StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Mobile/", System.StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Particles/", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static void DisableLegacySceneGround(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null || !string.Equals(root.name, "TrackScence", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var ground = root.transform.Find("Ground");
                if (ground == null)
                {
                    continue;
                }

                var renderer = ground.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.enabled = false;
                }

                var collider = ground.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                }
            }
        }

        private static void EnsureSceneSpecificTrack(Scene scene)
        {
            if (!string.Equals(scene.name, RoadSystemSceneName, System.StringComparison.Ordinal))
            {
                return;
            }

            var trackRoot = GameObject.Find("TrackRoot");
            if (trackRoot == null)
            {
                trackRoot = new GameObject("TrackRoot");
            }

            var existingRoadSystemTrack = trackRoot.GetComponentInChildren<RoadSystemArenaTrack>(includeInactive: true);
            if (existingRoadSystemTrack != null)
            {
                existingRoadSystemTrack.ResetTrack(seed: 1);
                return;
            }

            for (var i = trackRoot.transform.childCount - 1; i >= 0; i--)
            {
                var child = trackRoot.transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Object.Destroy(child);
                }
                else
                {
                    Object.DestroyImmediate(child);
                }
            }

            var roadTrack = new GameObject("RoadSystemArenaTrack");
            roadTrack.transform.SetParent(trackRoot.transform, false);
            roadTrack.transform.localPosition = Vector3.zero;
            roadTrack.transform.localRotation = Quaternion.identity;
            roadTrack.AddComponent<RoadSystemArenaTrack>();
        }

        private static void EnsureApiHost()
        {
            if (Object.FindFirstObjectByType<HttpJsonApiHost>() != null)
            {
                return;
            }

            var go = new GameObject(nameof(HttpJsonApiHost));
            Object.DontDestroyOnLoad(go);
            go.AddComponent<HttpJsonApiHost>();
        }

        private static void EnsureRos2BridgeHostIfEnabled()
        {
            var enabledValue = System.Environment.GetEnvironmentVariable("UAVSIM_ENABLE_ROS2_BRIDGE");
            var enabled = string.Equals(enabledValue, "1", System.StringComparison.Ordinal) ||
                          string.Equals(enabledValue, "true", System.StringComparison.OrdinalIgnoreCase);
            if (!enabled)
            {
                return;
            }

            var bridgeType = ResolveRos2BridgeType();
            if (bridgeType == null)
            {
                return;
            }

            var existing = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mono in existing)
            {
                if (mono != null && mono.GetType() == bridgeType)
                {
                    return;
                }
            }

            var go = new GameObject(bridgeType.Name);
            Object.DontDestroyOnLoad(go);
            var host = go.AddComponent(bridgeType);
            var method = bridgeType.GetMethod("StartBridgeNow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            method?.Invoke(host, null);
        }

        private static System.Type ResolveRos2BridgeType()
        {
            var type = System.Type.GetType("UavSimulator.Api.Ros2BridgeProcessHost, UavSimulator.Runtime");
            if (type != null)
            {
                return type;
            }

            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                type = asm.GetType("UavSimulator.Api.Ros2BridgeProcessHost", throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
