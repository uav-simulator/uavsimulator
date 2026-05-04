using System.Collections.Generic;
using UavSimulator.CityDemo;
using UavSimulator.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace UavSimulator.Tracks
{
    /// <summary>
    /// City demo track backed by POLYGON City Pack (Unity Asset Store id 107224).
    ///
    /// <para>Two assembly modes, picked at runtime:</para>
    /// <list type="number">
    /// <item><b>useDemoScene = true (default):</b> the prebuilt
    /// <c>Assets/POLYGON city pack/scene/DemoScene.unity</c> is loaded
    /// additively, its root GameObjects are reparented under this track,
    /// and any built-in Cameras/Lights from the demo scene are destroyed so
    /// they don't fight the host scene's rig. ~999 GameObjects, full block
    /// city — the same showcase scene the asset ships with.</item>
    /// <item><b>useDemoScene = false:</b> a procedural (2N+1)×(2N+1) grid
    /// of Building / Road / Intersection cells assembled from individual
    /// POLYGON prefabs. Smaller and more configurable, but visually thin.</item>
    /// </list>
    ///
    /// All loaded content runs through <see cref="RuntimeMaterialCompatibility"/>
    /// to convert Built-in pipeline materials onto URP/Lit at runtime.
    ///
    /// Editor-only — for standalone builds, the POLYGON pack would need
    /// Addressables / Resources/ migration.
    /// </summary>
    public sealed class CityPolygonTrack : TrackBase
    {
        // ───────────── Asset paths ─────────────

        private const string DemoScenePath = "Assets/POLYGON city pack/scene/DemoScene.unity";

        private const string PolygonRoot = "Assets/POLYGON city pack/Prefabs";
        private const string StreetStraightPrefabPath = PolygonRoot + "/Floor/Street 4 Prefab.prefab";
        private const string TrafficLightPrefabPath = PolygonRoot + "/Props/Traffic light 1 Prefab.prefab";
        private const string LampPrefabPath = PolygonRoot + "/Lamps/Lamp_1_prefab.prefab";

        private static readonly string[] BuildingPrefabPaths =
        {
            PolygonRoot + "/Buildings/Building_A_prefab.prefab",
            PolygonRoot + "/Buildings/Building_B_prefab.prefab",
            PolygonRoot + "/Buildings/Building_D_prefab.prefab",
            PolygonRoot + "/Buildings/Building_E_prefab.prefab",
            PolygonRoot + "/Buildings/Building_F_prefab.prefab",
            PolygonRoot + "/Buildings/Building_H_prefab.prefab",
            PolygonRoot + "/Buildings/Building_J_prefab.prefab",
            PolygonRoot + "/Buildings/Building_K_prefab.prefab",
            PolygonRoot + "/Buildings/Building_M_prefab.prefab",
            PolygonRoot + "/Buildings/Building_N_Prefab.prefab",
        };

        private const string PolygonMatRoot = "Assets/POLYGON city pack/Materials/traffic light";
        private const string MatRedOff = PolygonMatRoot + "/Red.mat";
        private const string MatRedOn = PolygonMatRoot + "/Red lighting.mat";
        private const string MatYellowAny = PolygonMatRoot + "/Yellow.mat";
        private const string MatGreenOff = PolygonMatRoot + "/Green.mat";
        private const string MatGreenOn = PolygonMatRoot + "/Green lighting.mat";

        // ───────────── Serialised parameters ─────────────

        [Header("Assembly mode")]
        [Tooltip("If true: load the POLYGON DemoScene additively (full ~999-object city). " +
                 "If false: build a procedural (2N+1)×(2N+1) grid of POLYGON prefabs.")]
        [SerializeField] private bool useDemoScene = true;

        [Tooltip("Run RuntimeMaterialCompatibility URP fix on every loaded renderer. " +
                 "POLYGON pack ships with Built-in materials so this is required under URP.")]
        [SerializeField] private bool sanitizeMaterials = true;

        [Tooltip("Destroy Camera and Light components in the loaded DemoScene so they don't fight the host scene rig.")]
        [SerializeField] private bool stripDemoSceneRig = true;

        [Header("Procedural fallback layout")]
        [SerializeField] private int intersectionsPerSide = 2;
        [SerializeField] private float cellSize = 0f;
        [SerializeField] private float trafficLightOffset = 3.5f;
        [SerializeField] private float trafficLightYOffset = 0f;
        [SerializeField] private float buildingYOffset = 0f;

        [Header("Cycle timing (procedural mode)")]
        [SerializeField] private float redSeconds = 8f;
        [SerializeField] private float greenSeconds = 10f;
        [SerializeField] private float yellowSeconds = 2f;

        // ───────────── State ─────────────

        private readonly List<TrafficLightController> controllers = new();
        private bool built;

        private void Awake()
        {
            BuildIfNeeded();
        }

        public override void ResetTrack(int seed)
        {
            BuildIfNeeded();
            for (var i = 0; i < controllers.Count; i++)
            {
                if (controllers[i] != null)
                {
                    controllers[i].ResetCycle(seed + i * 7919);
                    controllers[i].StartCycle();
                }
            }
        }

        private void BuildIfNeeded()
        {
            if (built) return;
            built = true;

#if UNITY_EDITOR
            if (useDemoScene && TryLoadDemoScene())
            {
                return;
            }
#endif

            BuildProcedural();
        }

        // ───────────── DemoScene mode ─────────────

#if UNITY_EDITOR
        private bool TryLoadDemoScene()
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath);
            if (sceneAsset == null)
            {
                Debug.LogWarning($"[CityPolygonTrack] DemoScene not found at {DemoScenePath}, falling back to procedural city.");
                return false;
            }

            var op = EditorSceneManager.LoadSceneAsyncInPlayMode(
                DemoScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            if (op == null)
            {
                Debug.LogError("[CityPolygonTrack] LoadSceneAsyncInPlayMode returned null, falling back to procedural city.");
                return false;
            }

            op.completed += _ => AdoptLoadedScene();
            return true;
        }

        private void AdoptLoadedScene()
        {
            var loaded = SceneManager.GetSceneByPath(DemoScenePath);
            if (!loaded.IsValid())
            {
                Debug.LogError("[CityPolygonTrack] DemoScene loaded but Scene handle invalid; aborting reparent.");
                return;
            }

            var roots = loaded.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var root = roots[i];

                if (stripDemoSceneRig)
                {
                    foreach (var cam in root.GetComponentsInChildren<Camera>(includeInactive: true))
                    {
                        Destroy(cam.gameObject);
                    }
                    foreach (var light in root.GetComponentsInChildren<Light>(includeInactive: true))
                    {
                        Destroy(light.gameObject);
                    }
                    foreach (var listener in root.GetComponentsInChildren<AudioListener>(includeInactive: true))
                    {
                        Destroy(listener);
                    }
                }

                root.transform.SetParent(transform, worldPositionStays: true);

                if (sanitizeMaterials)
                {
                    ReplaceIncompatibleMaterials(root);
                }
            }

            SceneManager.UnloadSceneAsync(loaded);
        }
#endif

        // ───────────── Procedural fallback ─────────────

        private void BuildProcedural()
        {
            var streetPrefab = LoadPrefab(StreetStraightPrefabPath);
            if (streetPrefab == null) return;

            var probe = SpawnSanitized(streetPrefab, new Vector3(-10000f, 0f, -10000f), Quaternion.identity, "_SizeProbe");
            var detectedSize = MeasureWorldSize(probe, Axis.Z, fallback: 12f);
            DestroyImmediate(probe);
            var spacing = cellSize > 0.01f ? cellSize : detectedSize;

            var trafficLightPrefab = LoadPrefab(TrafficLightPrefabPath);
            var lampPrefab = LoadPrefab(LampPrefabPath);
            var buildingPrefabs = LoadAllPrefabs(BuildingPrefabPaths);

            var redOff = LoadMaterial(MatRedOff);
            var redOn = LoadMaterial(MatRedOn);
            var yellow = LoadMaterial(MatYellowAny);
            var greenOff = LoadMaterial(MatGreenOff);
            var greenOn = LoadMaterial(MatGreenOn);

            var n = Mathf.Max(1, intersectionsPerSide);
            var cellsPerSide = 2 * n + 1;
            var halfExtent = (cellsPerSide - 1) * 0.5f;
            var buildingIndex = 0;

            for (var row = 0; row < cellsPerSide; row++)
            {
                for (var col = 0; col < cellsPerSide; col++)
                {
                    var x = (col - halfExtent) * spacing;
                    var z = (row - halfExtent) * spacing;
                    var pos = new Vector3(x, 0f, z);

                    var rowIsRoad = row % 2 == 1;
                    var colIsRoad = col % 2 == 1;

                    if (rowIsRoad && colIsRoad)
                    {
                        SpawnSanitized(streetPrefab, pos, Quaternion.identity, $"Intersection_{row}_{col}");
                        if (trafficLightPrefab != null)
                        {
                            var intersection = PlaceIntersectionLights(
                                trafficLightPrefab, pos,
                                redOff, redOn, yellow, greenOff, greenOn,
                                $"r{row}_c{col}");
                            if (intersection != null) controllers.Add(intersection);
                        }
                    }
                    else if (rowIsRoad ^ colIsRoad)
                    {
                        var rot = colIsRoad ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                        SpawnSanitized(streetPrefab, pos, rot, $"Road_{row}_{col}");
                    }
                    else
                    {
                        if (buildingPrefabs.Count > 0)
                        {
                            var prefab = buildingPrefabs[buildingIndex % buildingPrefabs.Count];
                            buildingIndex++;
                            var rotSteps = ((row * 73 + col * 31) & 3) * 90f;
                            var rot = Quaternion.Euler(0f, rotSteps, 0f);
                            var buildingPos = pos + new Vector3(0f, buildingYOffset, 0f);
                            SpawnSanitized(prefab, buildingPos, rot, $"Building_{row}_{col}");
                        }
                    }
                }
            }

            if (lampPrefab != null)
            {
                var corner = halfExtent * spacing + spacing * 0.4f;
                SpawnSanitized(lampPrefab, new Vector3(+corner, 0f, +corner), Quaternion.identity, "Lamp_NE");
                SpawnSanitized(lampPrefab, new Vector3(+corner, 0f, -corner), Quaternion.identity, "Lamp_SE");
                SpawnSanitized(lampPrefab, new Vector3(-corner, 0f, +corner), Quaternion.identity, "Lamp_NW");
                SpawnSanitized(lampPrefab, new Vector3(-corner, 0f, -corner), Quaternion.identity, "Lamp_SW");
            }

            foreach (var c in controllers)
            {
                if (c != null) c.StartCycle();
            }
        }

        private TrafficLightController PlaceIntersectionLights(
            GameObject trafficLightPrefab,
            Vector3 center,
            Material redOff, Material redOn,
            Material yellow,
            Material greenOff, Material greenOn,
            string nameSuffix)
        {
            var offsets = new[]
            {
                new Vector3(0f, trafficLightYOffset, +trafficLightOffset),
                new Vector3(+trafficLightOffset, trafficLightYOffset, 0f),
                new Vector3(0f, trafficLightYOffset, -trafficLightOffset),
                new Vector3(-trafficLightOffset, trafficLightYOffset, 0f),
            };
            var rotations = new[]
            {
                Quaternion.Euler(0f, 180f, 0f),
                Quaternion.Euler(0f, 270f, 0f),
                Quaternion.Euler(0f, 0f, 0f),
                Quaternion.Euler(0f, 90f, 0f),
            };

            var lights = new List<TrafficLight>(4);
            for (var i = 0; i < offsets.Length; i++)
            {
                var instance = SpawnSanitized(
                    trafficLightPrefab,
                    center + offsets[i],
                    rotations[i],
                    $"TL_{nameSuffix}_{i}");
                var renderer = instance.GetComponentInChildren<MeshRenderer>();
                var fsm = instance.AddComponent<TrafficLight>();
                var adapter = instance.AddComponent<TrafficLightPolygonAdapter>();
                adapter.SetMaterials(redOff, redOn, yellow, yellow, greenOff, greenOn);
                adapter.Bind(fsm, renderer);
                lights.Add(fsm);
            }

            if (lights.Count < 4) return null;

            var controllerGo = new GameObject($"TLController_{nameSuffix}");
            controllerGo.transform.SetParent(transform, false);
            controllerGo.transform.position = center;
            var controller = controllerGo.AddComponent<TrafficLightController>();
            controller.redSeconds = redSeconds;
            controller.greenSeconds = greenSeconds;
            controller.yellowSeconds = yellowSeconds;
            controller.SetNorthSouthLights(new[] { lights[0], lights[2] });
            controller.SetEastWestLights(new[] { lights[1], lights[3] });
            return controller;
        }

        // ───────────── Helpers ─────────────

        private GameObject SpawnSanitized(GameObject prefab, Vector3 position, Quaternion rotation, string name)
        {
            var instance = Instantiate(prefab, position, rotation, transform);
            instance.name = name;
            ReplaceIncompatibleMaterials(instance);
            return instance;
        }

        private static void ReplaceIncompatibleMaterials(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var mats = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    var source = mats[i];
                    if (source == null) continue;
                    if (!RuntimeMaterialCompatibility.NeedsReplacement(source)) continue;
                    mats[i] = RuntimeMaterialCompatibility.CreateReplacementMaterial(source, defaultSmoothness: 0.2f, copyTextures: true);
                    mats[i].color = RuntimeMaterialCompatibility.ReadSourceColor(source);
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = mats;
            }
        }

        private enum Axis { X, Y, Z }

        private static float MeasureWorldSize(GameObject root, Axis axis, float fallback)
        {
            if (root == null) return fallback;
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers == null || renderers.Length == 0) return fallback;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            switch (axis)
            {
                case Axis.X: return bounds.size.x > 0.01f ? bounds.size.x : fallback;
                case Axis.Y: return bounds.size.y > 0.01f ? bounds.size.y : fallback;
                default: return bounds.size.z > 0.01f ? bounds.size.z : fallback;
            }
        }

        private static GameObject LoadPrefab(string path)
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[CityPolygonTrack] Prefab not found at {path}.");
            }
            return prefab;
#else
            return null;
#endif
        }

        private static List<GameObject> LoadAllPrefabs(string[] paths)
        {
            var result = new List<GameObject>(paths.Length);
            foreach (var p in paths)
            {
                var prefab = LoadPrefab(p);
                if (prefab != null) result.Add(prefab);
            }
            return result;
        }

        private static Material LoadMaterial(string path)
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<Material>(path);
#else
            return null;
#endif
        }
    }
}
