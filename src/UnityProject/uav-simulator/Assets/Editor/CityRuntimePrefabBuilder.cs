using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UavSimulator.CityDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UavSimulator.EditorTools
{
    /// <summary>
    /// Builds a compact runtime-friendly POLYGON city prefab under Resources.
    /// The prefab is intentionally smaller than the full DemoScene so standalone
    /// runtime can spawn roads, colliders and traffic lights without AssetDatabase.
    /// </summary>
    public static class CityRuntimePrefabBuilder
    {
        private const string OutputDir = "Assets/Resources/UavSimulator/City";
        private const string OutputPrefabPath = OutputDir + "/city_runtime_compact.prefab";
        private const string DemoScenePath = "Assets/POLYGON city pack/scene/DemoScene.unity";
        private const float DemoSceneSliceRadius = 120f;

        private const string PolygonRoot = "Assets/POLYGON city pack/Prefabs";
        private const string StreetStraightPrefabPath = PolygonRoot + "/Floor/Street 4 Prefab.prefab";
        private const string StreetIntersectionPrefabPath = PolygonRoot + "/Floor/Street 8 Prefab.prefab";
        private const string EgoApproachTrafficLightPrefabPath = PolygonRoot + "/Props/Traffic light 4 Prefab.prefab";
        private const float PresentationGreenSeconds = 30f;
        private const float PresentationYellowSeconds = 5f;
        private const float PresentationRedSeconds = 10f;
        private static readonly Vector3 EgoApproachTrafficLightPosition = new Vector3(-5.75f, 0f, -3.55f);
        private static readonly Quaternion EgoApproachTrafficLightRotation = Quaternion.Euler(0f, 180f, 0f);
        private static readonly Vector3 EgoApproachStopZonePosition = new Vector3(-4.0f, 0.25f, -8.2f);
        private static readonly Vector3 WestSecondSignalStopZonePosition = new Vector3(-73.4f, 0.25f, 1.2f);
        private static readonly Quaternion WestSecondSignalStopZoneRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        private static readonly string[] TrafficLightPrefabPaths =
        {
            PolygonRoot + "/Props/Traffic light 2 Prefab.prefab",
            PolygonRoot + "/Props/Traffic light 3 Prefab.prefab",
            PolygonRoot + "/Props/Traffic light 4 Prefab.prefab",
            PolygonRoot + "/Props/Traffic light 1 Prefab.prefab",
        };
        private const string LampPrefabPath = PolygonRoot + "/Lamps/Lamp_1_prefab.prefab";

        private const string PolygonMatRoot = "Assets/POLYGON city pack/Materials/traffic light";
        private const string MatRedOff = PolygonMatRoot + "/Red.mat";
        private const string MatRedOn = PolygonMatRoot + "/Red lighting.mat";
        private const string MatYellowOff = PolygonMatRoot + "/Yellow.mat";
        private const string MatYellowOn = PolygonMatRoot + "/Yellow light.mat";
        private const string MatGreenOff = PolygonMatRoot + "/Green.mat";
        private const string MatGreenOn = PolygonMatRoot + "/Green lighting.mat";
        private const string GroundMaterialPath = "Assets/POLYGON city pack/Materials/Material_meshs/Grass.mat";

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
        };

        [MenuItem("UavSimulator/City Showcase/Build Runtime City Prefab")]
        public static void BuildFromMenu()
        {
            BuildInternal(exitWhenDone: false);
        }

        public static void BuildBatch()
        {
            BuildInternal(exitWhenDone: true);
        }

        private static void BuildInternal(bool exitWhenDone)
        {
            _ = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = TryBuildDemoSceneSlice();
            if (root == null)
            {
                root = BuildCompactFallback();
            }

            EnsureFolder(OutputDir);
            PrefabUtility.SaveAsPrefabAsset(root, OutputPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[CityRuntimePrefabBuilder] Saved compact city runtime prefab: {OutputPrefabPath}");
            if (exitWhenDone)
            {
                EditorApplication.Exit(0);
            }
        }

        private static GameObject BuildCompactFallback()
        {
            var straight = LoadRequiredPrefab(StreetStraightPrefabPath);
            var intersection = LoadOptionalPrefab(StreetIntersectionPrefabPath) ?? straight;
            var trafficLights = LoadTrafficLights();
            var lamp = LoadOptionalPrefab(LampPrefabPath);
            var buildings = LoadBuildings();
            var groundMaterial = LoadMaterial(GroundMaterialPath);

            var spacing = Mathf.Max(5f, MeasureWorldSize(straight, Axis.Z, fallback: 6f));
            var root = new GameObject("CityRuntimeCompact");
            BuildGroundPlane(root.transform, spacing, groundMaterial);
            var roadsRoot = new GameObject("Roads");
            roadsRoot.transform.SetParent(root.transform, false);
            var buildingsRoot = new GameObject("Buildings");
            buildingsRoot.transform.SetParent(root.transform, false);
            var propsRoot = new GameObject("Props");
            propsRoot.transform.SetParent(root.transform, false);
            var signalsRoot = new GameObject("TrafficLights");
            signalsRoot.transform.SetParent(root.transform, false);

            BuildRoadCross(roadsRoot.transform, straight, intersection, spacing);
            BuildRoadPhysicsDecks(roadsRoot.transform, spacing);
            BuildBuildings(buildingsRoot.transform, buildings, spacing);
            BuildProps(propsRoot.transform, lamp, spacing);
            BuildTrafficLights(signalsRoot.transform, trafficLights, spacing);

            return root;
        }

        private static GameObject TryBuildDemoSceneSlice()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) == null)
            {
                Debug.LogWarning($"[CityRuntimePrefabBuilder] DemoScene not found: {DemoScenePath}. Falling back to compact prefab assembly.");
                return null;
            }

            var scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
            if (!TryFindDemoRoadAnchor(scene, out var roadRenderer))
            {
                Debug.LogWarning("[CityRuntimePrefabBuilder] Could not find a DemoScene road anchor. Falling back to compact prefab assembly.");
                return null;
            }

            var anchor = roadRenderer.bounds.center;
            var surfaceY = roadRenderer.bounds.max.y;
            var worldOffset = new Vector3(anchor.x, surfaceY, anchor.z);
            var root = new GameObject("CityRuntimeCompact");
            root.transform.position = Vector3.zero;

            var selected = CollectDemoSliceRoots(scene, anchor, DemoSceneSliceRadius);
            var copied = 0;
            foreach (var source in selected)
            {
                var sourcePosition = source.transform.position;
                var sourceRotation = source.transform.rotation;
                var sourceScale = source.transform.lossyScale;
                var clone = Object.Instantiate(source);
                clone.name = source.name;
                StripSceneRig(clone);
                clone.transform.SetParent(root.transform, worldPositionStays: false);
                clone.transform.localPosition = sourcePosition - worldOffset;
                clone.transform.localRotation = sourceRotation;
                clone.transform.localScale = sourceScale;
                StripNonTriggerColliders(clone);
                copied++;
            }

            BuildRoadPhysicsDecks(root.transform, spacing: 5.2f);
            WireExistingTrafficLights(root.transform);

            Debug.Log(
                $"[CityRuntimePrefabBuilder] Built DemoScene slice: anchor={anchor}, " +
                $"surfaceY={surfaceY:0.###}, radius={DemoSceneSliceRadius:0.#}, objects={copied}");
            return root;
        }

        private static void BuildRoadCross(Transform parent, GameObject straight, GameObject intersection, float spacing)
        {
            for (var i = -3; i <= 3; i++)
            {
                var z = i * spacing;
                var road = SpawnPrefab(straight, parent, new Vector3(0f, 0f, z), Quaternion.identity, $"Road_NS_{i + 3}");
                EnsureMeshColliders(road);
            }

            for (var i = -3; i <= 3; i++)
            {
                var x = i * spacing;
                var prefab = i == 0 ? intersection : straight;
                var road = SpawnPrefab(prefab, parent, new Vector3(x, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), $"Road_EW_{i + 3}");
                EnsureMeshColliders(road);
            }
        }

        private static void BuildGroundPlane(Transform parent, float spacing, Material material)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "CityGroundPlane";
            ground.transform.SetParent(parent, worldPositionStays: false);
            ground.transform.localPosition = new Vector3(0f, -0.04f, 0f);
            ground.transform.localRotation = Quaternion.identity;
            ground.transform.localScale = new Vector3(spacing * 1.25f, 1f, spacing * 1.25f);

            var collider = ground.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void BuildRoadPhysicsDecks(Transform parent, float spacing)
        {
            var roadLength = spacing * 7.4f;
            var roadWidth = spacing * 1.1f;
            AddRoadPhysicsDeck(parent, "RoadPhysicsDeck_NS", new Vector3(roadWidth, 0.2f, roadLength));
            AddRoadPhysicsDeck(parent, "RoadPhysicsDeck_EW", new Vector3(roadLength, 0.2f, roadWidth));
            AddRoadPhysicsDeck(parent, "RoadPhysicsDeck_EgoLane_NS", new Vector3(4.6f, 0.2f, 42f), new Vector3(-4f, 0f, -1f));
            AddRoadPhysicsDeckBetween(
                parent,
                "RoadPhysicsDeck_WestSecondSignal_EW",
                new Vector3(-7f, 0f, 1.2f),
                new Vector3(-77f, 0f, 1.2f),
                width: 4.6f);
            AddRoadPhysicsDeckBetween(
                parent,
                "RoadPhysicsDeck_WestSecondSignal_SouthContinuation",
                new Vector3(-77f, 0f, 1.2f),
                new Vector3(-78f, 0f, -22f),
                width: 4.6f);
        }

        private static void AddRoadPhysicsDeck(Transform parent, string name, Vector3 size)
            => AddRoadPhysicsDeck(parent, name, size, Vector3.zero);

        private static void AddRoadPhysicsDeck(Transform parent, string name, Vector3 size, Vector3 localPosition)
        {
            var deck = new GameObject(name);
            deck.transform.SetParent(parent, worldPositionStays: false);
            deck.transform.localPosition = localPosition;
            var collider = deck.AddComponent<BoxCollider>();
            collider.size = size;
            collider.center = Vector3.zero;
        }

        private static void AddRoadPhysicsDeckBetween(Transform parent, string name, Vector3 from, Vector3 to, float width)
        {
            var delta = to - from;
            delta.y = 0f;
            var length = delta.magnitude;
            if (length <= 0.01f)
            {
                return;
            }

            var deck = new GameObject(name);
            deck.transform.SetParent(parent, worldPositionStays: false);
            deck.transform.localPosition = (from + to) * 0.5f;
            deck.transform.localRotation = Quaternion.LookRotation(delta / length, Vector3.up);
            var collider = deck.AddComponent<BoxCollider>();
            collider.size = new Vector3(width, 0.2f, length + 1.2f);
            collider.center = Vector3.zero;
        }

        private static void StripNonTriggerColliders(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (var collider in root.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (collider != null && !collider.isTrigger)
                {
                    Object.DestroyImmediate(collider);
                }
            }
        }

        private static void BuildBuildings(Transform parent, GameObject[] buildings, float spacing)
        {
            if (buildings.Length == 0)
            {
                return;
            }

            var positions = new[]
            {
                new Vector3(-spacing * 3.6f, 0f, -spacing * 3.4f),
                new Vector3(+spacing * 3.6f, 0f, -spacing * 3.4f),
                new Vector3(-spacing * 3.6f, 0f, +spacing * 3.4f),
                new Vector3(+spacing * 3.6f, 0f, +spacing * 3.4f),
                new Vector3(-spacing * 4.6f, 0f, -spacing * 0.8f),
                new Vector3(+spacing * 4.6f, 0f, +spacing * 0.8f),
                new Vector3(-spacing * 0.8f, 0f, -spacing * 4.5f),
                new Vector3(+spacing * 0.8f, 0f, +spacing * 4.5f),
            };

            for (var i = 0; i < positions.Length; i++)
            {
                var prefab = buildings[i % buildings.Length];
                var rotation = Quaternion.Euler(0f, (i % 4) * 90f, 0f);
                var building = SpawnPrefab(prefab, parent, positions[i], rotation, $"Building_{i + 1:00}");
                EnsureMeshColliders(building);
            }
        }

        private static void BuildProps(Transform parent, GameObject lamp, float spacing)
        {
            if (lamp == null)
            {
                return;
            }

            var offset = spacing * 0.62f;
            var positions = new[]
            {
                new Vector3(-offset, 0f, -offset),
                new Vector3(+offset, 0f, -offset),
                new Vector3(-offset, 0f, +offset),
                new Vector3(+offset, 0f, +offset),
            };

            for (var i = 0; i < positions.Length; i++)
            {
                SpawnPrefab(lamp, parent, positions[i], Quaternion.Euler(0f, i * 90f, 0f), $"Lamp_{i + 1}");
            }
        }

        private static void BuildTrafficLights(Transform parent, GameObject[] trafficLightPrefabs, float spacing)
        {
            if (trafficLightPrefabs.Length == 0)
            {
                return;
            }

            var redOff = LoadMaterial(MatRedOff);
            var redOn = LoadMaterial(MatRedOn);
            var yellowOff = LoadMaterial(MatYellowOff);
            var yellowOn = LoadMaterial(MatYellowOn);
            var greenOff = LoadMaterial(MatGreenOff);
            var greenOn = LoadMaterial(MatGreenOn);

            var offset = spacing * 0.72f;
            var positions = new[]
            {
                new Vector3(-offset, 0f, +offset),
                new Vector3(+offset, 0f, +offset),
                new Vector3(+offset, 0f, -offset),
                new Vector3(-offset, 0f, -offset),
            };
            var rotations = new[]
            {
                Quaternion.Euler(0f, 0f, 0f),
                Quaternion.Euler(0f, 90f, 0f),
                Quaternion.Euler(0f, 180f, 0f),
                Quaternion.Euler(0f, 270f, 0f),
            };

            var lights = new TrafficLight[4];
            for (var i = 0; i < positions.Length; i++)
            {
                var prefab = trafficLightPrefabs[i % trafficLightPrefabs.Length];
                var instance = SpawnPrefab(prefab, parent, positions[i], rotations[i], $"TrafficLight_{i + 1}");
                var renderer = instance.GetComponentInChildren<MeshRenderer>();
                var fsm = instance.GetComponent<TrafficLight>() ?? instance.AddComponent<TrafficLight>();
                var adapter = instance.GetComponent<TrafficLightPolygonAdapter>() ?? instance.AddComponent<TrafficLightPolygonAdapter>();
                WireTrafficLightAdapter(adapter, fsm, renderer, redOff, redOn, yellowOff, yellowOn, greenOff, greenOn);
                AddStopZone(instance.transform, fsm, spacing, i + 1);
                lights[i] = fsm;
            }

            var controllerGo = new GameObject("TrafficLightController_Center");
            controllerGo.transform.SetParent(parent, false);
            var controller = controllerGo.AddComponent<TrafficLightController>();
            controller.redSeconds = PresentationRedSeconds;
            controller.greenSeconds = PresentationGreenSeconds;
            controller.yellowSeconds = PresentationYellowSeconds;
            controller.SetNorthSouthLights(new[] { lights[0], lights[2] });
            controller.SetEastWestLights(new[] { lights[1], lights[3] });
        }

        private static void WireTrafficLightAdapter(
            TrafficLightPolygonAdapter adapter,
            TrafficLight trafficLight,
            MeshRenderer renderer,
            Material redOff,
            Material redOn,
            Material yellowOff,
            Material yellowOn,
            Material greenOff,
            Material greenOn)
        {
            var serialized = new SerializedObject(adapter);
            serialized.FindProperty("trafficLight").objectReferenceValue = trafficLight;
            serialized.FindProperty("trafficLightRenderer").objectReferenceValue = renderer;
            serialized.FindProperty("redOff").objectReferenceValue = redOff;
            serialized.FindProperty("redOn").objectReferenceValue = redOn;
            serialized.FindProperty("yellowOff").objectReferenceValue = yellowOff;
            serialized.FindProperty("yellowOn").objectReferenceValue = yellowOn;
            serialized.FindProperty("greenOff").objectReferenceValue = greenOff;
            serialized.FindProperty("greenOn").objectReferenceValue = greenOn;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireExistingTrafficLights(Transform root)
        {
            var redOff = LoadMaterial(MatRedOff);
            var redOn = LoadMaterial(MatRedOn);
            var yellowOff = LoadMaterial(MatYellowOff);
            var yellowOn = LoadMaterial(MatYellowOn);
            var greenOff = LoadMaterial(MatGreenOff);
            var greenOn = LoadMaterial(MatGreenOn);

            var renderers = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true)
                .Where(renderer => ContainsAny(GetTransformPath(renderer.transform), "traffic_light", "traffic light", "trafficlight"))
                .OrderBy(renderer => HorizontalDistanceSq(renderer.bounds.center, Vector3.zero))
                .Take(4)
                .ToArray();
            if (renderers.Length == 0)
            {
                return;
            }

            var lights = new List<TrafficLight>(renderers.Length);
            for (var i = 0; i < renderers.Length; i++)
            {
                var host = FindTrafficLightHost(root, renderers[i].transform);
                var fsm = host.GetComponent<TrafficLight>() ?? host.gameObject.AddComponent<TrafficLight>();
                var adapter = host.GetComponent<TrafficLightPolygonAdapter>() ?? host.gameObject.AddComponent<TrafficLightPolygonAdapter>();
                WireTrafficLightAdapter(adapter, fsm, renderers[i], redOff, redOn, yellowOff, yellowOn, greenOff, greenOn);
                lights.Add(fsm);
            }

            var egoApproachLight = AddEgoApproachTrafficLight(
                root,
                redOff,
                redOn,
                yellowOff,
                yellowOn,
                greenOff,
                greenOn);

            var controllerGo = new GameObject("TrafficLightController_Center");
            controllerGo.transform.SetParent(root, false);
            var controller = controllerGo.AddComponent<TrafficLightController>();
            controller.redSeconds = PresentationRedSeconds;
            controller.greenSeconds = PresentationGreenSeconds;
            controller.yellowSeconds = PresentationYellowSeconds;
            controller.SetNorthSouthLights(new[] { egoApproachLight ?? lights[0] });
            controller.SetEastWestLights(lights.ToArray());

            AddDetachedStopZone(
                root,
                egoApproachLight ?? lights[0],
                EgoApproachStopZonePosition,
                Quaternion.identity,
                "DemoSliceStopZone_NS_South_EgoLane");

            var westSecondSignalLight = lights
                .Where(light => light != null)
                .OrderByDescending(light => light.transform.position.x < -50f ? 1 : 0)
                .ThenBy(light => Mathf.Abs(light.transform.position.z - 3f))
                .FirstOrDefault();
            if (westSecondSignalLight != null && westSecondSignalLight.transform.position.x < -50f)
            {
                AddDetachedStopZone(
                    root,
                    westSecondSignalLight,
                    WestSecondSignalStopZonePosition,
                    WestSecondSignalStopZoneRotation,
                    "DemoSliceStopZone_WestSecondSignal_EW");
            }
        }

        private static TrafficLight AddEgoApproachTrafficLight(
            Transform root,
            Material redOff,
            Material redOn,
            Material yellowOff,
            Material yellowOn,
            Material greenOff,
            Material greenOn)
        {
            var prefab = LoadOptionalPrefab(EgoApproachTrafficLightPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[CityRuntimePrefabBuilder] Missing ego approach traffic-light prefab: {EgoApproachTrafficLightPrefabPath}");
                return null;
            }

            var instance = SpawnPrefab(
                prefab,
                root,
                EgoApproachTrafficLightPosition,
                EgoApproachTrafficLightRotation,
                "TrafficLight_EgoApproach_NS_South");
            var renderer = instance.GetComponentInChildren<MeshRenderer>();
            var fsm = instance.GetComponent<TrafficLight>() ?? instance.AddComponent<TrafficLight>();
            var adapter = instance.GetComponent<TrafficLightPolygonAdapter>() ?? instance.AddComponent<TrafficLightPolygonAdapter>();
            WireTrafficLightAdapter(adapter, fsm, renderer, redOff, redOn, yellowOff, yellowOn, greenOff, greenOn);
            StripNonTriggerColliders(instance);
            return fsm;
        }

        private static void AddDetachedStopZone(Transform parent, TrafficLight light, Vector3 position, Quaternion rotation, string name)
        {
            var zoneGo = new GameObject(name);
            zoneGo.transform.SetParent(parent, worldPositionStays: false);
            zoneGo.transform.localPosition = position;
            zoneGo.transform.localRotation = rotation;
            var box = zoneGo.AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 0.5f, 2f);
            box.isTrigger = true;
            var zone = zoneGo.AddComponent<TrafficLightTriggerZone>();
            zone.SetTrafficLight(light);
        }

        private static Transform FindTrafficLightHost(Transform sliceRoot, Transform rendererTransform)
        {
            var current = rendererTransform;
            while (current.parent != null && current.parent != sliceRoot)
            {
                if (ContainsAny(current.name, "traffic light", "traffic_light", "trafficlight"))
                {
                    return current;
                }

                current = current.parent;
            }

            return rendererTransform;
        }

        private static void AddStopZone(Transform lightRoot, TrafficLight light, float trafficLightOffset, int index)
        {
            var zoneGo = new GameObject($"TrafficLightStopZone_{index}");
            zoneGo.transform.SetParent(lightRoot, worldPositionStays: false);
            zoneGo.transform.localPosition = new Vector3(0f, 0.25f, -trafficLightOffset * 0.5f);
            zoneGo.transform.localRotation = Quaternion.identity;
            var box = zoneGo.AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 0.5f, 2f);
            box.isTrigger = true;
            var zone = zoneGo.AddComponent<TrafficLightTriggerZone>();
            zone.SetTrafficLight(light);
        }

        private static GameObject SpawnPrefab(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.SetParent(parent, worldPositionStays: false);
            instance.transform.localPosition = position;
            instance.transform.localRotation = rotation;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private static void EnsureMeshColliders(GameObject root)
        {
            foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    continue;
                }

                if (meshFilter.GetComponent<Collider>() != null)
                {
                    continue;
                }

                var collider = meshFilter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = meshFilter.sharedMesh;
            }
        }

        private static bool TryFindDemoRoadAnchor(Scene scene, out Renderer renderer)
        {
            var trafficLight = EnumerateSceneRenderers(scene)
                .Where(candidate => candidate is MeshRenderer)
                .Where(candidate => ContainsAny(GetTransformPath(candidate.transform), "traffic light", "traffic_light", "trafficlight"))
                .OrderBy(candidate => HorizontalDistanceSq(candidate.bounds.center, Vector3.zero))
                .FirstOrDefault();
            var anchor = trafficLight != null ? trafficLight.bounds.center : Vector3.zero;

            renderer = EnumerateSceneRenderers(scene)
                .Where(candidate => candidate is MeshRenderer)
                .Where(candidate => ContainsAny(GetTransformPath(candidate.transform), "street 8", "street", "road"))
                .Where(candidate => Mathf.Max(candidate.bounds.size.x, candidate.bounds.size.z) >= 2f)
                .OrderBy(candidate => HorizontalDistanceSq(candidate.bounds.ClosestPoint(anchor), anchor))
                .ThenByDescending(candidate => candidate.bounds.size.x * candidate.bounds.size.z)
                .FirstOrDefault();
            return renderer != null;
        }

        private static List<GameObject> CollectDemoSliceRoots(Scene scene, Vector3 anchor, float radius)
        {
            var result = new List<GameObject>();
            var seen = new HashSet<GameObject>();
            foreach (var renderer in EnumerateSceneRenderers(scene))
            {
                if (renderer == null || !(renderer is MeshRenderer) || renderer.bounds.size.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                var horizontalDistance = Mathf.Sqrt(HorizontalDistanceSq(renderer.bounds.ClosestPoint(anchor), anchor));
                var path = GetTransformPath(renderer.transform);
                var isTrafficOrRoad = ContainsAny(path, "traffic light", "traffic_light", "trafficlight", "street", "road", "sideway", "walkway");
                if (horizontalDistance > radius && !isTrafficOrRoad)
                {
                    continue;
                }

                if (horizontalDistance > radius * 1.25f)
                {
                    continue;
                }

                var instanceRoot = FindSceneInstanceRoot(renderer.transform);
                if (instanceRoot == null || seen.Contains(instanceRoot.gameObject))
                {
                    continue;
                }

                if (instanceRoot.GetComponentInChildren<Camera>(includeInactive: true) != null)
                {
                    continue;
                }

                seen.Add(instanceRoot.gameObject);
                result.Add(instanceRoot.gameObject);
            }

            return result;
        }

        private static Transform FindSceneInstanceRoot(Transform transform)
        {
            var current = transform;
            while (current.parent != null && current.parent.parent != null)
            {
                current = current.parent;
            }

            return current;
        }

        private static IEnumerable<Renderer> EnumerateSceneRenderers(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
                {
                    if (renderer != null)
                    {
                        yield return renderer;
                    }
                }
            }
        }

        private static void StripSceneRig(GameObject root)
        {
            foreach (var camera in root.GetComponentsInChildren<Camera>(includeInactive: true))
            {
                Object.DestroyImmediate(camera.gameObject);
            }

            foreach (var listener in root.GetComponentsInChildren<AudioListener>(includeInactive: true))
            {
                Object.DestroyImmediate(listener);
            }
        }

        private static GameObject[] LoadBuildings()
        {
            var result = new System.Collections.Generic.List<GameObject>(BuildingPrefabPaths.Length);
            foreach (var path in BuildingPrefabPaths)
            {
                var prefab = LoadOptionalPrefab(path);
                if (prefab != null)
                {
                    result.Add(prefab);
                }
            }

            return result.ToArray();
        }

        private static GameObject[] LoadTrafficLights()
        {
            var result = new System.Collections.Generic.List<GameObject>(TrafficLightPrefabPaths.Length);
            foreach (var path in TrafficLightPrefabPaths)
            {
                var prefab = LoadOptionalPrefab(path);
                if (prefab != null)
                {
                    result.Add(prefab);
                }
            }

            if (result.Count == 0)
            {
                throw new FileNotFoundException("No POLYGON traffic-light prefabs found.");
            }

            return result.ToArray();
        }

        private static GameObject LoadRequiredPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new FileNotFoundException($"Required prefab not found: {path}", path);
            }

            return prefab;
        }

        private static GameObject LoadOptionalPrefab(string path)
            => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        private static Material LoadMaterial(string path)
            => AssetDatabase.LoadAssetAtPath<Material>(path);

        private static bool ContainsAny(string value, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return tokens.Any(token => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static float HorizontalDistanceSq(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static string GetTransformPath(Transform target)
        {
            var names = new List<string>();
            var current = target;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private enum Axis { X, Y, Z }

        private static float MeasureWorldSize(GameObject prefab, Axis axis, float fallback)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            probe.transform.position = new Vector3(10000f, 10000f, 10000f);
            var renderers = probe.GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers.Length == 0)
            {
                Object.DestroyImmediate(probe);
                return fallback;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Object.DestroyImmediate(probe);
            return axis switch
            {
                Axis.X => bounds.size.x > 0.01f ? bounds.size.x : fallback,
                Axis.Y => bounds.size.y > 0.01f ? bounds.size.y : fallback,
                _ => bounds.size.z > 0.01f ? bounds.size.z : fallback,
            };
        }

        private static void EnsureFolder(string folder)
        {
            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
