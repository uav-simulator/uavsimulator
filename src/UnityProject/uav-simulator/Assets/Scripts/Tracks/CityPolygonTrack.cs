using System.Collections.Generic;
using UavSimulator.CityDemo;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UavSimulator.Tracks
{
    /// <summary>
    /// City demo track built from POLYGON City Pack prefabs (asset: Unity Asset Store id 107224).
    /// Layout (top-down, XZ plane): a "+" cross of street tiles meeting at the origin,
    /// with four traffic light prefabs at the central intersection.
    ///
    /// Traffic lights are managed by a child <see cref="TrafficLightController"/>
    /// (NS pair vs EW pair). On each <see cref="ResetTrack"/> the controller is restarted
    /// with the supplied seed.
    ///
    /// Street tiles, buildings, lamps, and traffic-light prefabs are looked up at runtime
    /// from the <c>Assets/POLYGON city pack/Prefabs/</c> tree via AssetDatabase. If a prefab
    /// is missing, the affected component is silently skipped (track still spawns, just
    /// without that decoration). Editor-only — for standalone builds the city would need
    /// Addressables or AssetBundles, out of current scope.
    ///
    /// Spawn pose for the controlled vehicle is at (0, 0.2, -10), facing +Z.
    /// </summary>
    public sealed class CityPolygonTrack : TrackBase
    {
        // POLYGON pack asset paths (verified against shipping package layout).
        private const string PolygonRoot = "Assets/POLYGON city pack/Prefabs";
        private const string StreetStraightPrefabPath = PolygonRoot + "/Floor/Street 4 Prefab.prefab";
        private const string StreetIntersectionPrefabPath = PolygonRoot + "/Floor/Street 1 Prefab.prefab";
        private const string SidewayPrefabPath = PolygonRoot + "/Floor/Sideway 1 prefab.prefab";
        private const string TrafficLightPrefabPath = PolygonRoot + "/Props/Traffic light 1 Prefab.prefab";
        private const string LampPrefabPath = PolygonRoot + "/Lamps/Lamp_1_prefab.prefab";

        // Traffic light material paths (matched to the slot indices on the POLYGON
        // Traffic light 1 prefab — see TrafficLightPolygonAdapter for the slot map).
        private const string PolygonMatRoot = "Assets/POLYGON city pack/Materials/traffic light";
        private const string MatRedOff = PolygonMatRoot + "/Red.mat";
        private const string MatRedOn = PolygonMatRoot + "/Red lighting.mat";
        private const string MatYellowAny = PolygonMatRoot + "/Yellow.mat";
        private const string MatGreenOff = PolygonMatRoot + "/Green.mat";
        private const string MatGreenOn = PolygonMatRoot + "/Green lighting.mat";

        // Layout constants — POLYGON Street 4 prefab is roughly 12m long after its
        // built-in 200x scale, so 12m spacing per tile gives a continuous road.
        // These values are conservative; user can tweak via Inspector if cells
        // overlap or leave gaps when the asset is updated.
        [SerializeField] private float tileSpacing = 12f;
        [SerializeField] private int armTileCount = 2; // tiles per arm of the cross (excluding center)
        [SerializeField] private float trafficLightOffset = 3.5f; // distance from intersection center

        [Header("Cycle timing (forwarded to controller)")]
        [SerializeField] private float redSeconds = 8f;
        [SerializeField] private float greenSeconds = 10f;
        [SerializeField] private float yellowSeconds = 2f;

        private TrafficLightController controller;
        private bool built;

        private void Awake()
        {
            BuildIfNeeded();
        }

        public override void ResetTrack(int seed)
        {
            BuildIfNeeded();
            if (controller != null)
            {
                controller.ResetCycle(seed);
                controller.StartCycle();
            }
        }

        private void BuildIfNeeded()
        {
            if (built) return;
            built = true;

            // 1. Streets — cross layout: N-S arm + E-W arm + center intersection.
            PlaceStreetArms();

            // 2. Traffic lights at the four corners of the intersection.
            var lights = PlaceTrafficLights();

            // 3. Lamps along the streets for atmosphere (skipped if prefab missing).
            PlaceLamps();

            // 4. Wire the FSM controller. Lights[0]/[2] = NS pair, Lights[1]/[3] = EW pair.
            if (lights.Count >= 4)
            {
                var controllerGo = new GameObject("TrafficLightController");
                controllerGo.transform.SetParent(transform, false);
                controller = controllerGo.AddComponent<TrafficLightController>();
                controller.redSeconds = redSeconds;
                controller.greenSeconds = greenSeconds;
                controller.yellowSeconds = yellowSeconds;
                controller.SetNorthSouthLights(new[] { lights[0], lights[2] });
                controller.SetEastWestLights(new[] { lights[1], lights[3] });
                controller.StartCycle();
            }
        }

        private void PlaceStreetArms()
        {
            var streetPrefab = LoadPrefab(StreetStraightPrefabPath);
            if (streetPrefab == null) return;

            // Center tile.
            Instantiate(streetPrefab, Vector3.zero, Quaternion.identity, transform).name = "StreetCenter";

            // North-South arm (along Z axis).
            for (var i = 1; i <= armTileCount; i++)
            {
                Instantiate(streetPrefab, new Vector3(0f, 0f, i * tileSpacing), Quaternion.identity, transform).name = $"StreetN{i}";
                Instantiate(streetPrefab, new Vector3(0f, 0f, -i * tileSpacing), Quaternion.identity, transform).name = $"StreetS{i}";
            }

            // East-West arm (along X axis, prefabs rotated 90° around Y).
            var ewRotation = Quaternion.Euler(0f, 90f, 0f);
            for (var i = 1; i <= armTileCount; i++)
            {
                Instantiate(streetPrefab, new Vector3(i * tileSpacing, 0f, 0f), ewRotation, transform).name = $"StreetE{i}";
                Instantiate(streetPrefab, new Vector3(-i * tileSpacing, 0f, 0f), ewRotation, transform).name = $"StreetW{i}";
            }
        }

        private List<TrafficLight> PlaceTrafficLights()
        {
            var result = new List<TrafficLight>();
            var trafficLightPrefab = LoadPrefab(TrafficLightPrefabPath);
            if (trafficLightPrefab == null) return result;

            // Four positions, one per intersection corner. Order: N, E, S, W.
            // Matches the wiring NS = {N, S} = indices 0, 2; EW = {E, W} = indices 1, 3.
            var positions = new[]
            {
                new Vector3(0f, 0f, +trafficLightOffset),  // N
                new Vector3(+trafficLightOffset, 0f, 0f),  // E
                new Vector3(0f, 0f, -trafficLightOffset),  // S
                new Vector3(-trafficLightOffset, 0f, 0f),  // W
            };

            // Each light faces the center of the intersection — orient it 180° from its
            // outward normal so the front (with bulbs visible) looks toward oncoming cars.
            var rotations = new[]
            {
                Quaternion.Euler(0f, 180f, 0f), // N facing south
                Quaternion.Euler(0f, 270f, 0f), // E facing west
                Quaternion.Euler(0f, 0f, 0f),   // S facing north
                Quaternion.Euler(0f, 90f, 0f),  // W facing east
            };

            // Pre-load the materials used by the adapter (one shared set per track).
            var redOff = LoadMaterial(MatRedOff);
            var redOn = LoadMaterial(MatRedOn);
            var yellow = LoadMaterial(MatYellowAny);
            var greenOff = LoadMaterial(MatGreenOff);
            var greenOn = LoadMaterial(MatGreenOn);

            for (var i = 0; i < positions.Length; i++)
            {
                var instance = Instantiate(trafficLightPrefab, positions[i], rotations[i], transform);
                instance.name = $"TrafficLight_{i}";

                // Find the renderer on the model — POLYGON pack puts it on the inner
                // child named like "Traffic_light_N", but checking the children
                // for any MeshRenderer is robust.
                var renderer = instance.GetComponentInChildren<MeshRenderer>();

                var fsm = instance.AddComponent<TrafficLight>();
                var adapter = instance.AddComponent<TrafficLightPolygonAdapter>();
                adapter.SetMaterials(redOff, redOn, yellow, yellow, greenOff, greenOn);
                adapter.Bind(fsm, renderer);

                result.Add(fsm);
            }

            return result;
        }

        private void PlaceLamps()
        {
            var lampPrefab = LoadPrefab(LampPrefabPath);
            if (lampPrefab == null) return;

            // 4 lamps, one near each intersection corner offset along the street.
            var lampPositions = new[]
            {
                new Vector3(+1.5f, 0f, +1.5f),
                new Vector3(+1.5f, 0f, -1.5f),
                new Vector3(-1.5f, 0f, +1.5f),
                new Vector3(-1.5f, 0f, -1.5f),
            };
            for (var i = 0; i < lampPositions.Length; i++)
            {
                Instantiate(lampPrefab, lampPositions[i], Quaternion.identity, transform).name = $"Lamp_{i}";
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
            // POLYGON prefabs are not in Resources/ and are Editor-only via AssetDatabase.
            // For standalone builds, switch to Addressables or move prefabs into Resources/.
            return null;
#endif
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
