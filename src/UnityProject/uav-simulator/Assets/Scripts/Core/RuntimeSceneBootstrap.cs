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
    }
}
