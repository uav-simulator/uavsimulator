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

                        if (!RuntimeMaterialCompatibility.NeedsReplacement(source))
                        {
                            continue;
                        }

                        if (!replacements.TryGetValue(source, out var replacement))
                        {
                            replacement = RuntimeMaterialCompatibility.CreateReplacementMaterial(source);
                            replacement.name = $"{source.name}_BuiltinFallback";

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
