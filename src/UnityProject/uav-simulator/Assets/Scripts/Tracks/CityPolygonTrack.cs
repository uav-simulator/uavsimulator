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
    /// <para>Assembly modes, picked at runtime:</para>
    /// <list type="number">
    /// <item><b>Runtime city prefab:</b> an audited compact city slice loaded
    /// from Resources. This is the preferred presentation path because it
    /// carries explicit road physics decks and traffic-light stop zones.</item>
    /// <item><b>useDemoScene = true:</b> the prebuilt
    /// <c>Assets/POLYGON city pack/scene/DemoScene.unity</c> is loaded
    /// additively, its root GameObjects are reparented under this track,
    /// and any built-in Cameras/Lights from the demo scene are destroyed so
    /// they don't fight the host scene's rig. This remains a fallback when
    /// the runtime prefab is unavailable.</item>
    /// <item><b>Procedural fallback:</b> a procedural (2N+1)×(2N+1) grid
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
        private const string RuntimeCityResourcePath = "UavSimulator/City/city_runtime_compact";

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

            if (TryBuildRuntimePrefab())
            {
                return;
            }

#if UNITY_EDITOR
            if (useDemoScene && TryLoadDemoScene())
            {
                return;
            }
#endif

            BuildProcedural();
        }

        private bool TryBuildRuntimePrefab()
        {
            var prefab = Resources.Load<GameObject>(RuntimeCityResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"[CityPolygonTrack] Runtime city prefab not found at Resources/{RuntimeCityResourcePath}; " +
                    "falling back to editor/procedural assembly.");
                return false;
            }

            var instance = Instantiate(prefab, transform, worldPositionStays: false);
            instance.name = "CityRuntimeCompact";
            if (sanitizeMaterials)
            {
                ReplaceIncompatibleMaterials(instance);
            }

            // Runtime prefab already carries explicit RoadPhysicsDeck_* colliders
            // and traffic-light trigger zones. Re-adding MeshCollider to the
            // visual city turns crosswalks, poles and traffic-light meshes into
            // blockers for the demo vehicle.
            RegisterTrafficLightControllers(instance);
            return true;
        }

        private static void EnsurePresentationRoadDecks(GameObject root)
        {
            var roadsRoot = FindChildByName(root.transform, "Roads") ?? root.transform;
            if (FindChildByName(roadsRoot, "RoadPresentationDeck") != null)
            {
                return;
            }

            var northSouthDeck = FindChildByName(root.transform, "RoadPhysicsDeck_NS")?.GetComponent<BoxCollider>();
            var eastWestDeck = FindChildByName(root.transform, "RoadPhysicsDeck_EW")?.GetComponent<BoxCollider>();
            if (northSouthDeck == null || eastWestDeck == null)
            {
                return;
            }

            var measuredRoadLength = Mathf.Max(northSouthDeck.size.z, eastWestDeck.size.x);
            var roadLength = Mathf.Clamp(Mathf.Max(measuredRoadLength, 34f), 34f, 40f);
            var roadWidth = Mathf.Clamp(Mathf.Min(northSouthDeck.size.x, eastWestDeck.size.y) * 0.82f, 4.2f, 5.6f);
            var asphaltMaterial = CreateRuntimeColorMaterial("CityPresentationAsphalt", new Color(0.105f, 0.125f, 0.115f, 1f), 0.16f);
            var lineMaterial = CreateRuntimeColorMaterial("CityPresentationLaneMarking", new Color(0.92f, 0.78f, 0.22f, 1f), 0.08f);

            var deck = new GameObject("RoadPresentationDeck");
            deck.transform.SetParent(roadsRoot, worldPositionStays: false);
            deck.transform.localPosition = new Vector3(0f, 0.016f, 0f);
            deck.transform.localRotation = Quaternion.identity;
            deck.transform.localScale = Vector3.one;
            deck.AddComponent<MeshFilter>().sharedMesh = BuildRoadCrossMesh(roadLength, roadWidth);
            deck.AddComponent<MeshRenderer>().sharedMaterial = asphaltMaterial;

            AddLaneMarkings(roadsRoot, roadLength, roadWidth, lineMaterial);
        }

        private static Mesh BuildRoadCrossMesh(float roadLength, float roadWidth)
        {
            var halfLength = roadLength * 0.5f;
            var halfWidth = roadWidth * 0.5f;
            var vertices = new[]
            {
                new Vector3(-halfWidth, 0f, -halfLength),
                new Vector3(+halfWidth, 0f, -halfLength),
                new Vector3(+halfWidth, 0f, +halfLength),
                new Vector3(-halfWidth, 0f, +halfLength),

                new Vector3(-halfLength, 0f, -halfWidth),
                new Vector3(-halfWidth, 0f, -halfWidth),
                new Vector3(-halfWidth, 0f, +halfWidth),
                new Vector3(-halfLength, 0f, +halfWidth),

                new Vector3(+halfWidth, 0f, -halfWidth),
                new Vector3(+halfLength, 0f, -halfWidth),
                new Vector3(+halfLength, 0f, +halfWidth),
                new Vector3(+halfWidth, 0f, +halfWidth),
            };
            var triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 6, 5, 4, 7, 6,
                8, 10, 9, 8, 11, 10,
            };
            var uvs = new Vector2[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                uvs[i] = new Vector2(vertices[i].x / roadLength + 0.5f, vertices[i].z / roadLength + 0.5f);
            }

            var mesh = new Mesh
            {
                name = "CityPresentationRoadCrossMesh",
                vertices = vertices,
                triangles = triangles,
                uv = uvs,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddLaneMarkings(Transform parent, float roadLength, float roadWidth, Material lineMaterial)
        {
            var halfLength = roadLength * 0.5f;
            var halfIntersection = roadWidth * 0.55f;
            const float segmentLength = 1.25f;
            const float segmentStep = 2.45f;
            const float lineWidth = 0.08f;

            for (var z = -halfLength + 1.6f; z <= halfLength - 1.6f; z += segmentStep)
            {
                if (Mathf.Abs(z) < halfIntersection)
                {
                    continue;
                }

                AddPresentationQuad(parent, $"RoadLane_NS_{z:0.0}", new Vector3(0f, 0.024f, z), lineWidth, segmentLength, lineMaterial);
            }

            for (var x = -halfLength + 1.6f; x <= halfLength - 1.6f; x += segmentStep)
            {
                if (Mathf.Abs(x) < halfIntersection)
                {
                    continue;
                }

                AddPresentationQuad(parent, $"RoadLane_EW_{x:0.0}", new Vector3(x, 0.025f, 0f), segmentLength, lineWidth, lineMaterial);
            }
        }

        private static void AddPresentationQuad(Transform parent, string name, Vector3 position, float widthX, float lengthZ, Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, worldPositionStays: false);
            quad.transform.localPosition = position;
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(widthX, lengthZ, 1f);

            var collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = quad.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static Material CreateRuntimeColorMaterial(string name, Color color, float smoothness)
        {
            var material = new Material(RuntimeMaterialCompatibility.ResolveCompatibleLitShader())
            {
                name = name,
                color = color,
            };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            return material;
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == childName)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChildByName(root.GetChild(i), childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void RegisterTrafficLightControllers(GameObject root)
        {
            var loadedControllers = root.GetComponentsInChildren<TrafficLightController>(includeInactive: true);
            for (var i = 0; i < loadedControllers.Length; i++)
            {
                if (loadedControllers[i] != null && !controllers.Contains(loadedControllers[i]))
                {
                    controllers.Add(loadedControllers[i]);
                }
            }
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

                // Stop-zone trigger: ~4m wide, ~0.5m tall, ~2m deep slab in front
                // of the light, so TrafficLightAwareController raycasts intercept
                // it and CityVehicleTelemetryExtender publishes ground-truth state.
                var zoneGo = new GameObject($"TLZone_{nameSuffix}_{i}");
                zoneGo.transform.SetParent(instance.transform, worldPositionStays: false);
                zoneGo.transform.localPosition = new Vector3(0f, -trafficLightYOffset + 0.25f, -trafficLightOffset * 0.5f);
                zoneGo.transform.localRotation = Quaternion.identity;
                var box = zoneGo.AddComponent<BoxCollider>();
                box.size = new Vector3(4f, 0.5f, 2f);
                box.isTrigger = true;
                var zone = zoneGo.AddComponent<UavSimulator.CityDemo.TrafficLightTriggerZone>();
                zone.SetTrafficLight(fsm);
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
            EnsureMeshColliders(instance);
            return instance;
        }

        /// <summary>
        /// Add a MeshCollider to every child MeshFilter that lacks any Collider.
        /// POLYGON City prefabs ship visual-only — without this the Prometeo physics
        /// vehicle falls through the road on spawn.
        /// </summary>
        private static void EnsureMeshColliders(GameObject root)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(includeInactive: true);
            foreach (var mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (mf.GetComponent<Collider>() != null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
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
