using System.IO;
using UavSimulator.Core;
using UavSimulator.Tracks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UavSimulator.EditorTools
{
    public static class RoadSystemSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/RoadSystemTrack.unity";

        [MenuItem("UavSimulator/Scene/Build RoadSystem Track Scene")]
        public static void BuildRoadSystemScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("PresentationRoot");
            root.AddComponent<PresentationDemoDriver>();
            root.AddComponent<PresentationTelemetryHud>();

            var trackRoot = new GameObject("TrackRoot");
            trackRoot.transform.SetParent(root.transform, false);
            var trackGo = new GameObject("RoadSystemArenaTrack");
            trackGo.transform.SetParent(trackRoot.transform, false);
            var track = trackGo.AddComponent<RoadSystemArenaTrack>();
            track.ResetTrack(seed: 1);

            var vehicleRoot = new GameObject("VehicleRoot");
            vehicleRoot.transform.SetParent(root.transform, false);

            CreateCamera(root.transform);
            CreateLight(root.transform);

            EnsureSceneFolder();
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log($"[RoadSystemSceneBuilder] Scene saved: {Path.GetFullPath(ScenePath)}");
        }

        private static void EnsureSceneFolder()
        {
            var folder = Path.GetDirectoryName(ScenePath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }
        }

        private static void CreateCamera(Transform parent)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(parent, false);
            camGo.transform.position = new Vector3(0f, 3.1f, -12f);
            camGo.transform.LookAt(new Vector3(0f, 0.4f, -2.0f));

            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.74f, 0.84f, 0.93f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 220f;

            camGo.AddComponent<PresentationFollowCamera>();
        }

        private static void CreateLight(Transform parent)
        {
            var lightGo = new GameObject("Directional Light");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.rotation = Quaternion.Euler(50f, -32f, 0f);

            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.98f, 0.95f);
            light.intensity = 1.1f;
        }
    }
}
