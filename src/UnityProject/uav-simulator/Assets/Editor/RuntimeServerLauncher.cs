using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace UavSimulator.EditorTools
{
    public static class RuntimeServerLauncher
    {
        private const string DefaultScenePath = "Assets/Scenes/TrackScence.unity";
        private static bool startRequested;
        private static int pollAttempts;

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
            // Poll on every Editor tick instead of delayCall — when launched
            // via `-executeMethod` (no GUI), delayCall fires once and may run
            // before compile/import finishes, so EnterPlaymode silently no-ops.
            // EditorApplication.update keeps firing each tick, letting us wait
            // for isCompiling/isUpdating to settle and retry.
            EditorApplication.update += PollEnterPlayMode;
            UnityEngine.Debug.Log($"[RuntimeServerLauncher] Prepared scene '{scenePath}' and requested Play Mode.");
        }

        private static void PollEnterPlayMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.update -= PollEnterPlayMode;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                pollAttempts++;
                if (pollAttempts > 6000)
                {
                    EditorApplication.update -= PollEnterPlayMode;
                    UnityEngine.Debug.LogError("[RuntimeServerLauncher] Timed out waiting for compile/import to finish; not entering Play Mode.");
                }
                return;
            }

            EditorApplication.update -= PollEnterPlayMode;
            EditorApplication.EnterPlaymode();
            UnityEngine.Debug.Log("[RuntimeServerLauncher] Entered Play Mode.");
        }
    }
}
