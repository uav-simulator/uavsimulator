using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UavSimulator.CityDemo;
using UavSimulator.Core;
using UavSimulator.Tracks;
using UavSimulator.Vehicles;
using UnityEditor;
using UnityEngine;

namespace UavSimulator.Tests.EditMode
{
    public sealed class CityRuntimePresentationTests
    {
        private const string RuntimeCityPrefabPath = "Assets/Resources/UavSimulator/City/city_runtime_compact.prefab";
        private const string RedOffPath = "Assets/POLYGON city pack/Materials/traffic light/Red.mat";
        private const string RedOnPath = "Assets/POLYGON city pack/Materials/traffic light/Red lighting.mat";
        private const string YellowOffPath = "Assets/POLYGON city pack/Materials/traffic light/Yellow.mat";
        private const string YellowOnPath = "Assets/POLYGON city pack/Materials/traffic light/Yellow light.mat";
        private const string GreenOffPath = "Assets/POLYGON city pack/Materials/traffic light/Green.mat";
        private const string GreenOnPath = "Assets/POLYGON city pack/Materials/traffic light/Green lighting.mat";

        [Test]
        public void RuntimeCityPrefab_EgoStopZoneIsBeforeCrosswalkApproach()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeCityPrefabPath);
            Assert.NotNull(prefab, $"Missing runtime city prefab at {RuntimeCityPrefabPath}");

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var zone = root.GetComponentsInChildren<TrafficLightTriggerZone>(includeInactive: true)
                    .FirstOrDefault(candidate => candidate.name == "DemoSliceStopZone_NS_South_EgoLane");
                Assert.NotNull(zone, "Missing ego approach stop-zone");

                Assert.LessOrEqual(
                    zone.transform.position.z,
                    -7.5f,
                    "Ego stop-zone must be before the crosswalk/stop-line approach, not in the intersection.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeCityPrefab_EgoTrafficLightUsesLitYellowMaterial()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeCityPrefabPath);
            Assert.NotNull(prefab, $"Missing runtime city prefab at {RuntimeCityPrefabPath}");

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var light = root.GetComponentsInChildren<TrafficLight>(includeInactive: true)
                    .FirstOrDefault(candidate => candidate.name == "TrafficLight_EgoApproach_NS_South");
                Assert.NotNull(light, "Missing ego approach traffic-light");

                var adapter = light.GetComponent<TrafficLightPolygonAdapter>();
                Assert.NotNull(adapter, "Missing POLYGON traffic-light visual adapter");

                var serialized = new SerializedObject(adapter);
                var yellowOn = serialized.FindProperty("yellowOn").objectReferenceValue as Material;
                Assert.NotNull(yellowOn, "yellowOn material is not wired");
                Assert.AreEqual("Yellow light", yellowOn.name);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeCityPrefab_EgoTrafficLightUsesStopLinePresentationTiming()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeCityPrefabPath);
            Assert.NotNull(prefab, $"Missing runtime city prefab at {RuntimeCityPrefabPath}");

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var controller = root.GetComponentsInChildren<TrafficLightController>(includeInactive: true)
                    .FirstOrDefault(candidate => candidate.name == "TrafficLightController_Center");
                Assert.NotNull(controller, "Missing traffic-light controller");

                Assert.AreEqual(
                    30f,
                    controller.greenSeconds,
                    0.01f,
                    "Showcase traffic light should hold Green long enough to avoid flickering in presentation video.");
                Assert.AreEqual(
                    5f,
                    controller.yellowSeconds,
                    0.01f,
                    "Showcase traffic light should use a readable Yellow phase.");
                Assert.AreEqual(
                    10f,
                    controller.redSeconds,
                    0.01f,
                    "Showcase traffic light should use the requested Red duration.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeCityPrefab_WestSecondSignalCandidateRouteHasRoadPhysicsCoverage()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeCityPrefabPath);
            Assert.NotNull(prefab, $"Missing runtime city prefab at {RuntimeCityPrefabPath}");

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                root.transform.position = Vector3.zero;
                Physics.SyncTransforms();

                var waypoints = new[]
                {
                    new Vector3(-4f, 3f, -20f),
                    new Vector3(-4f, 3f, -8f),
                    new Vector3(-4.7f, 3f, -2.3f),
                    new Vector3(-7f, 3f, 1.2f),
                    new Vector3(-18f, 3f, 1.2f),
                    new Vector3(-38f, 3f, 1.2f),
                    new Vector3(-58f, 3f, 1.2f),
                    new Vector3(-77f, 3f, 1.2f),
                };

                var misses = new List<Vector3>();
                var westDeckHits = 0;
                for (var segment = 0; segment < waypoints.Length - 1; segment++)
                {
                    var from = waypoints[segment];
                    var to = waypoints[segment + 1];
                    var distance = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
                    var steps = Mathf.Max(1, Mathf.CeilToInt(distance / 2f));
                    for (var i = 0; i <= steps; i++)
                    {
                        var sample = Vector3.Lerp(from, to, i / (float)steps);
                        if (!Physics.Raycast(sample, Vector3.down, out var hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        {
                            misses.Add(sample);
                            continue;
                        }

                        if (hit.collider.name.Contains("RoadPhysicsDeck_WestSecondSignal"))
                        {
                            westDeckHits++;
                        }
                    }
                }

                Assert.That(misses, Is.Empty, "Candidate second-signal route must not leave audited road physics.");
                Assert.Greater(westDeckHits, 8, "Route should be supported by the dedicated west second-signal road deck, not only by the central cross deck.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeCityPrefab_WestSecondSignalCityContinuationHasRoadPhysicsCoverage()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeCityPrefabPath);
            Assert.NotNull(prefab, $"Missing runtime city prefab at {RuntimeCityPrefabPath}");

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                root.transform.position = Vector3.zero;
                Physics.SyncTransforms();

                var waypoints = new[]
                {
                    new Vector3(-58f, 3f, 1.2f),
                    new Vector3(-70.4f, 3f, 1.2f),
                    new Vector3(-74.8f, 3f, 0.7f),
                    new Vector3(-77.6f, 3f, -1.0f),
                    new Vector3(-78f, 3f, -8f),
                    new Vector3(-78f, 3f, -16f),
                    new Vector3(-78f, 3f, -22f),
                };

                var misses = new List<Vector3>();
                var continuationDeckHits = 0;
                for (var segment = 0; segment < waypoints.Length - 1; segment++)
                {
                    var from = waypoints[segment];
                    var to = waypoints[segment + 1];
                    var distance = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
                    var steps = Mathf.Max(1, Mathf.CeilToInt(distance / 2f));
                    for (var i = 0; i <= steps; i++)
                    {
                        var sample = Vector3.Lerp(from, to, i / (float)steps);
                        if (!Physics.Raycast(sample, Vector3.down, out var hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        {
                            misses.Add(sample);
                            continue;
                        }

                        if (hit.collider.name.Contains("RoadPhysicsDeck_WestSecondSignal_SouthContinuation"))
                        {
                            continuationDeckHits++;
                        }
                    }
                }

                Assert.That(misses, Is.Empty, "City continuation after the west signal must stay on audited road physics.");
                Assert.Greater(continuationDeckHits, 6, "The west-signal continuation should use the southbound city road instead of ending at the map edge.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeCityPrefab_IncludesWestSecondSignalStopZone()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeCityPrefabPath);
            Assert.NotNull(prefab, $"Missing runtime city prefab at {RuntimeCityPrefabPath}");

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var zone = root.GetComponentsInChildren<TrafficLightTriggerZone>(includeInactive: true)
                    .FirstOrDefault(candidate => candidate.name == "DemoSliceStopZone_WestSecondSignal_EW");
                Assert.NotNull(zone, "Missing west second-signal stop-zone");

                Assert.Less(
                    zone.transform.position.x,
                    -60f,
                    "West second-signal stop-zone must be placed near the far DemoScene signal, not at the central intersection.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeCityPrefab_WestSecondSignalStopZoneIsCalibratedNearVisualStopLine()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeCityPrefabPath);
            Assert.NotNull(prefab, $"Missing runtime city prefab at {RuntimeCityPrefabPath}");

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var zone = root.GetComponentsInChildren<TrafficLightTriggerZone>(includeInactive: true)
                    .FirstOrDefault(candidate => candidate.name == "DemoSliceStopZone_WestSecondSignal_EW");
                Assert.NotNull(zone, "Missing west second-signal stop-zone");

                Assert.That(
                    zone.transform.position.x,
                    Is.InRange(-73.8f, -73.0f),
                    "West second-signal stop-zone must be close to the visible stop-line/crosswalk, not several meters before it.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeCityPrefab_WestSignalAreaUsesDemoSceneCityAssets()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeCityPrefabPath);
            Assert.NotNull(prefab, $"Missing runtime city prefab at {RuntimeCityPrefabPath}");

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var manualVisualContext = root.GetComponentsInChildren<Transform>(includeInactive: true)
                    .FirstOrDefault(candidate => candidate.name == "CityVisualContext_WestSignal");
                Assert.Null(
                    manualVisualContext,
                    "The city showcase must not place a manual west-signal backdrop; use the POLYGON DemoScene city assets instead.");

                var cityContextRenderers = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true)
                    .Where(renderer => renderer.bounds.center.x < -66f)
                    .Where(renderer => Mathf.Abs(renderer.bounds.center.z - 1.2f) < 35f)
                    .Where(renderer =>
                    {
                        var path = GetTransformPath(renderer.transform).ToLowerInvariant();
                        return !path.Contains("traffic")
                               && !path.Contains("street")
                               && !path.Contains("road")
                               && !path.Contains("sideway")
                               && !path.Contains("sidewalk")
                               && !path.Contains("walkway")
                               && !path.Contains("crosswalk")
                               && !path.Contains("cityvisualcontext_westsignal");
                    })
                    .ToArray();

                Assert.GreaterOrEqual(
                    cityContextRenderers.Length,
                    12,
                    "The far west signal should be surrounded by original DemoScene city geometry, not an empty compact edge.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeCityPrefab_WestSecondSignalStopZoneIsDetectedFromApproachLane()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeCityPrefabPath);
            Assert.NotNull(prefab, $"Missing runtime city prefab at {RuntimeCityPrefabPath}");

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var probe = new GameObject("WestSecondSignalProbe");
            try
            {
                root.transform.position = Vector3.zero;
                probe.transform.position = new Vector3(-65f, 0.2f, 1.2f);
                probe.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
                var controller = probe.AddComponent<TrafficLightAwareController>();
                controller.lookAheadDistance = 8f;
                Physics.SyncTransforms();

                var snapshot = controller.GetNearestLightSnapshot();

                Assert.That(snapshot.hasLight, Is.True, "Westbound approach ray must detect the second-signal stop-zone before the car reaches the line.");
                Assert.That(snapshot.distanceM, Is.InRange(6f, 8f));
            }
            finally
            {
                Object.DestroyImmediate(probe);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeCityPrefab_EgoTrafficLightFacesApproachingCar()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeCityPrefabPath);
            Assert.NotNull(prefab, $"Missing runtime city prefab at {RuntimeCityPrefabPath}");

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var light = root.GetComponentsInChildren<TrafficLight>(includeInactive: true)
                    .FirstOrDefault(candidate => candidate.name == "TrafficLight_EgoApproach_NS_South");
                Assert.NotNull(light, "Missing ego approach traffic-light");

                Assert.That(
                    Mathf.Abs(Mathf.DeltaAngle(light.transform.eulerAngles.y, 180f)),
                    Is.LessThan(1f),
                    "The ego-facing traffic light must face the south approach camera/car, not show its back side.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeMaterialCompatibility_PreservesEmissionColor()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(RedOnPath);
            Assert.NotNull(source, $"Missing material at {RedOnPath}");
            Assume.That(source.HasProperty("_EmissionColor"), Is.True);

            var replacement = RuntimeMaterialCompatibility.CreateReplacementMaterial(source);
            try
            {
                Assert.That(replacement.HasProperty("_EmissionColor"), Is.True);
                var emission = replacement.GetColor("_EmissionColor");
                Assert.Greater(emission.r, 0.5f);
                Assert.Greater(emission.g, 0.05f);
                Assert.That(replacement.IsKeywordEnabled("_EMISSION"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(replacement);
            }
        }

        [Test]
        public void CityPolygonTrack_MaterialSanitizerDampsGrassPresentationTint()
        {
            var root = new GameObject("CityRuntimeCompact");
            var grass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var sourceMaterial = new Material(RuntimeMaterialCompatibility.ResolveCompatibleLitShader())
            {
                name = "Grass Debug Tint",
            };
            try
            {
                grass.name = "Grass";
                grass.transform.SetParent(root.transform, worldPositionStays: false);
                SetDisplayColor(sourceMaterial, Color.magenta);
                grass.GetComponent<MeshRenderer>().sharedMaterial = sourceMaterial;

                var sanitizer = typeof(CityPolygonTrack).GetMethod(
                    "ReplaceIncompatibleMaterials",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(sanitizer, "City material sanitizer method is missing.");

                sanitizer.Invoke(null, new object[] { root });

                var sanitized = grass.GetComponent<MeshRenderer>().sharedMaterial;
                var color = ReadDisplayColor(sanitized);
                Assert.Less(color.r, 0.35f);
                Assert.Greater(color.g, 0.35f);
                Assert.Less(color.b, 0.30f);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(sourceMaterial);
            }
        }

        [Test]
        public void CityPolygonTrack_MaterialSanitizerForcesStreetMaterialToAsphalt()
        {
            var root = new GameObject("CityRuntimeCompact");
            var street = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var sourceMaterial = new Material(RuntimeMaterialCompatibility.ResolveCompatibleLitShader())
            {
                name = "street 4",
            };
            try
            {
                street.name = "Street 4 Prefab";
                street.transform.SetParent(root.transform, worldPositionStays: false);
                SetDisplayColor(sourceMaterial, Color.magenta);
                street.GetComponent<MeshRenderer>().sharedMaterial = sourceMaterial;

                var sanitizer = typeof(CityPolygonTrack).GetMethod(
                    "ReplaceIncompatibleMaterials",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(sanitizer, "City material sanitizer method is missing.");

                sanitizer.Invoke(null, new object[] { root });

                var sanitized = street.GetComponent<MeshRenderer>().sharedMaterial;
                var color = ReadDisplayColor(sanitized);
                Assert.Less(color.maxColorComponent, 0.18f);
                Assert.Greater(color.r, 0.08f);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(sourceMaterial);
            }
        }

        [Test]
        public void TrafficLightPolygonAdapter_UsesCompatibleLitMaterialCopies()
        {
            Assume.That(RuntimeMaterialCompatibility.IsUrpActive(), Is.True);

            var rendererGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var lightGo = new GameObject("Light");
            try
            {
                var renderer = rendererGo.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = Enumerable.Repeat(AssetDatabase.LoadAssetAtPath<Material>(YellowOffPath), 7).ToArray();

                var light = lightGo.AddComponent<TrafficLight>();
                var adapter = lightGo.AddComponent<TrafficLightPolygonAdapter>();
                adapter.SetMaterials(
                    AssetDatabase.LoadAssetAtPath<Material>(RedOffPath),
                    AssetDatabase.LoadAssetAtPath<Material>(RedOnPath),
                    AssetDatabase.LoadAssetAtPath<Material>(YellowOffPath),
                    AssetDatabase.LoadAssetAtPath<Material>(YellowOnPath),
                    AssetDatabase.LoadAssetAtPath<Material>(GreenOffPath),
                    AssetDatabase.LoadAssetAtPath<Material>(GreenOnPath));
                adapter.Bind(light, renderer);

                light.SetState(TrafficLightState.Red);

                var redOnSlot = renderer.sharedMaterials[4];
                Assert.That(RuntimeMaterialCompatibility.IsShaderCompatibleForCurrentPipeline(redOnSlot.shader), Is.True);
                Assert.That(redOnSlot.HasProperty("_EmissionColor"), Is.True);
                Assert.Greater(redOnSlot.GetColor("_EmissionColor").r, 0.5f);
            }
            finally
            {
                Object.DestroyImmediate(rendererGo);
                Object.DestroyImmediate(lightGo);
            }
        }

        [Test]
        public void TrafficLightPolygonAdapter_DimsInactiveBulbsAtRuntime()
        {
            Assume.That(RuntimeMaterialCompatibility.IsUrpActive(), Is.True);

            var rendererGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var lightGo = new GameObject("Light");
            try
            {
                var renderer = rendererGo.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = Enumerable.Repeat(AssetDatabase.LoadAssetAtPath<Material>(YellowOffPath), 7).ToArray();

                var light = lightGo.AddComponent<TrafficLight>();
                var adapter = lightGo.AddComponent<TrafficLightPolygonAdapter>();
                adapter.SetMaterials(
                    AssetDatabase.LoadAssetAtPath<Material>(RedOffPath),
                    AssetDatabase.LoadAssetAtPath<Material>(RedOnPath),
                    AssetDatabase.LoadAssetAtPath<Material>(YellowOffPath),
                    AssetDatabase.LoadAssetAtPath<Material>(YellowOnPath),
                    AssetDatabase.LoadAssetAtPath<Material>(GreenOffPath),
                    AssetDatabase.LoadAssetAtPath<Material>(GreenOnPath));
                adapter.Bind(light, renderer);

                light.SetState(TrafficLightState.Red);

                Assert.Greater(ReadEmissionColor(renderer.sharedMaterials[4]).r, 0.5f);
                Assert.Less(ReadDisplayColor(renderer.sharedMaterials[5]).maxColorComponent, 0.08f);
                Assert.Less(ReadDisplayColor(renderer.sharedMaterials[6]).maxColorComponent, 0.08f);
                Assert.Less(ReadEmissionColor(renderer.sharedMaterials[5]).maxColorComponent, 0.01f);
                Assert.Less(ReadEmissionColor(renderer.sharedMaterials[6]).maxColorComponent, 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(rendererGo);
                Object.DestroyImmediate(lightGo);
            }
        }

        private static Color ReadDisplayColor(Material material)
        {
            if (material.HasProperty("_BaseColor"))
            {
                return material.GetColor("_BaseColor");
            }

            if (material.HasProperty("_Color"))
            {
                return material.GetColor("_Color");
            }

            return material.color;
        }

        private static void SetDisplayColor(Material material, Color color)
        {
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static Color ReadEmissionColor(Material material)
        {
            return material.HasProperty("_EmissionColor")
                ? material.GetColor("_EmissionColor")
                : Color.black;
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            return transform.parent == null
                ? transform.name
                : $"{GetTransformPath(transform.parent)}/{transform.name}";
        }
    }
}
