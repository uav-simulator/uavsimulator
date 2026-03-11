using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace UavSimulator.EditorTools
{
    public static class RuntimeServerLauncher
    {
        private const string DefaultScenePath = "Assets/Scenes/TrackScence.unity";
        private static bool startRequested;

        public static void StartRuntimeServer()
        {
            if (startRequested)
            {
                return;
            }

            startRequested = true;
            var scenePath = Environment.GetEnvironmentVariable("RUSIM_START_SCENE");
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                scenePath = DefaultScenePath;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            EditorApplication.delayCall += EnterPlayMode;
            UnityEngine.Debug.Log($"[RuntimeServerLauncher] Prepared scene '{scenePath}' and requested Play Mode.");
        }

        private static void EnterPlayMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EditorApplication.EnterPlaymode();
        }
    }
}
