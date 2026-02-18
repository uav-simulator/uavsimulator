using UavSimulator.Api;
using UnityEngine;

namespace UavSimulator.Core
{
    public static class RuntimeSceneBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureRuntimeObjects()
        {
            EnsureSimulationManager();
            EnsureApiHost();
            EnsureRos2BridgeHostIfEnabled();
        }

        private static void EnsureSimulationManager()
        {
            if (Object.FindFirstObjectByType<SimulationManager>() != null)
            {
                return;
            }

            var go = new GameObject(nameof(SimulationManager));
            Object.DontDestroyOnLoad(go);
            go.AddComponent<SimulationManager>();
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
