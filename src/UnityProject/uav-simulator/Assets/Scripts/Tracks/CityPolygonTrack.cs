using System.Collections.Generic;
using UavSimulator.CityDemo;
using UavSimulator.Core;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UavSimulator.Tracks
{
    /// <summary>
    /// City demo track built procedurally from POLYGON City Pack prefabs
    /// (asset: Unity Asset Store id 107224).
    ///
    /// Layout (top-down, XZ plane): a regular grid of cells where the parity
    /// of (row, col) decides cell type:
    ///
    /// <code>
    ///   B R B R B
    ///   R I R I R    B = building cell
    ///   B R B R B    R = road cell (straight street tile)
    ///   R I R I R    I = intersection (street + 4 traffic lights)
    ///   B R B R B
    /// </code>
    ///
    /// Grid dimensions: <c>(2 * intersectionsPerSide + 1)</c> cells per side.
    /// Default <c>intersectionsPerSide = 2</c> gives a 5×5 grid: 9 buildings,
    /// 4 intersections, 12 road segments.
    ///
    /// Each intersection gets its own <see cref="TrafficLightController"/> with
    /// 4 traffic-light prefabs (N/E/S/W pair) interlocked NS↔EW.
    ///
    /// Street tiles, buildings, lamps and traffic-light prefabs are looked up
    /// at runtime via AssetDatabase. Editor-only — for standalone builds the
    /// POLYGON pack would need Addressables / Resources/ migration.
    /// </summary>
    public sealed class CityPolygonTrack : TrackBase
    {
        // POLYGON pack asset paths.
        private const string PolygonRoot = "Assets/POLYGON city pack/Prefabs";
        private const string StreetStraightPrefabPath = PolygonRoot + "/Floor/Street 4 Prefab.prefab";
        private const string StreetIntersectionPrefabPath = PolygonRoot + "/Floor/Street 4 Prefab.prefab";
        private const string TrafficLightPrefabPath = PolygonRoot + "/Props/Traffic light 1 Prefab.prefab";
        private const string LampPrefabPath = PolygonRoot + "/Lamps/Lamp_1_prefab.prefab";

        // Building variety — instantiated round-robin into building cells.
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

        // Traffic light material paths (slot mapping is in TrafficLightPolygonAdapter).
        private const string PolygonMatRoot = "Assets/POLYGON city pack/Materials/traffic light";
        private const string MatRedOff = PolygonMatRoot + "/Red.mat";
        private const string MatRedOn = PolygonMatRoot + "/Red lighting.mat";
        private const string MatYellowAny = PolygonMatRoot + "/Yellow.mat";
        private const string MatGreenOff = PolygonMatRoot + "/Green.mat";
        private const string MatGreenOn = PolygonMatRoot + "/Green lighting.mat";

        [Header("City layout")]
        [Tooltip("Number of intersections per side. 2 → 5×5 grid (9 buildings, 4 intersections). 3 → 7×7 grid (16 buildings, 9 intersections).")]
        [SerializeField] private int intersectionsPerSide = 2;

        [Tooltip("Cell side length in meters. 0 = auto-detect from Street 4 prefab bounds.")]
        [SerializeField] private float cellSize = 0f;

        [Tooltip("Distance from intersection center to each of the 4 traffic-light corners (in meters).")]
        [SerializeField] private float trafficLightOffset = 3.5f;

        [Tooltip("Y-offset for traffic light prefabs (lift above ground if pivot lands underneath).")]
        [SerializeField] private float trafficLightYOffset = 0f;

        [Tooltip("Y-offset for building prefabs.")]
        [SerializeField] private float buildingYOffset = 0f;

        [Header("Cycle timing (per intersection)")]
        [SerializeField] private float redSeconds = 8f;
        [SerializeField] private float greenSeconds = 10f;
        [SerializeField] private float yellowSeconds = 2f;

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
                    // Stagger seeds across intersections so adjacent lights aren't synced.
                    controllers[i].ResetCycle(seed + i * 7919);
                    controllers[i].StartCycle();
                }
            }
        }

        private void BuildIfNeeded()
        {
            if (built) return;
            built = true;

            var streetPrefab = LoadPrefab(StreetStraightPrefabPath);
            if (streetPrefab == null) return;

            // Auto-detect cell size by spawning one probe tile and measuring its bounds.
            var probe = SpawnSanitized(streetPrefab, new Vector3(-10000f, 0f, -10000f), Quaternion.identity, "_SizeProbe");
            var detectedSize = MeasureWorldSize(probe, Axis.Z, fallback: 12f);
            DestroyImmediate(probe);
            var spacing = cellSize > 0.01f ? cellSize : detectedSize;

            // Pre-load shared resources.
            var trafficLightPrefab = LoadPrefab(TrafficLightPrefabPath);
            var lampPrefab = LoadPrefab(LampPrefabPath);
            var buildingPrefabs = LoadAllPrefabs(BuildingPrefabPaths);

            var redOff = LoadMaterial(MatRedOff);
            var redOn = LoadMaterial(MatRedOn);
            var yellow = LoadMaterial(MatYellowAny);
            var greenOff = LoadMaterial(MatGreenOff);
            var greenOn = LoadMaterial(MatGreenOn);

            // Grid: (2N+1) × (2N+1) cells centred on origin.
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
                        // Intersection cell — straight tile + 4 traffic lights + controller.
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
                        // Road cell — straight tile, rotated to align with traffic flow.
                        var rot = colIsRoad ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                        SpawnSanitized(streetPrefab, pos, rot, $"Road_{row}_{col}");
                    }
                    else
                    {
                        // Building cell — round-robin a building from the pool.
                        if (buildingPrefabs.Count > 0)
                        {
                            var prefab = buildingPrefabs[buildingIndex % buildingPrefabs.Count];
                            buildingIndex++;
                            // Random 90° rotation per cell using a deterministic hash for variety.
                            var rotSteps = ((row * 73 + col * 31) & 3) * 90f;
                            var rot = Quaternion.Euler(0f, rotSteps, 0f);
                            var buildingPos = pos + new Vector3(0f, buildingYOffset, 0f);
                            SpawnSanitized(prefab, buildingPos, rot, $"Building_{row}_{col}");
                        }
                    }
                }
            }

            // Decorative lampposts at the four outer corners of the city.
            if (lampPrefab != null)
            {
                var corner = halfExtent * spacing + spacing * 0.4f;
                SpawnSanitized(lampPrefab, new Vector3(+corner, 0f, +corner), Quaternion.identity, "Lamp_NE");
                SpawnSanitized(lampPrefab, new Vector3(+corner, 0f, -corner), Quaternion.identity, "Lamp_SE");
                SpawnSanitized(lampPrefab, new Vector3(-corner, 0f, +corner), Quaternion.identity, "Lamp_NW");
                SpawnSanitized(lampPrefab, new Vector3(-corner, 0f, -corner), Quaternion.identity, "Lamp_SW");
            }

            // Start all intersection cycles.
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
            // Four positions, one per intersection corner. Order: N, E, S, W.
            // NS = {N, S} indices 0, 2; EW = {E, W} indices 1, 3.
            var offsets = new[]
            {
                new Vector3(0f, trafficLightYOffset, +trafficLightOffset), // N
                new Vector3(+trafficLightOffset, trafficLightYOffset, 0f), // E
                new Vector3(0f, trafficLightYOffset, -trafficLightOffset), // S
                new Vector3(-trafficLightOffset, trafficLightYOffset, 0f), // W
            };
            var rotations = new[]
            {
                Quaternion.Euler(0f, 180f, 0f), // N facing south
                Quaternion.Euler(0f, 270f, 0f), // E facing west
                Quaternion.Euler(0f, 0f, 0f),   // S facing north
                Quaternion.Euler(0f, 90f, 0f),  // W facing east
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
            // POLYGON pack ships with Built-in pipeline materials; under URP they
            // render magenta. Rebuild each material on the URP/Lit shader,
            // preserving textures and base colour.
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
                Debug.LogWarning($"[CityPolygonTrack] Prefab not found at {path}. Place the POLYGON City Pack at 'Assets/POLYGON city pack/' or update CityPolygonTrack constants.");
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
