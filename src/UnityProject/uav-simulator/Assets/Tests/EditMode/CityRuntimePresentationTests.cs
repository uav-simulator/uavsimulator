using System.Linq;
using NUnit.Framework;
using UavSimulator.CityDemo;
using UavSimulator.Core;
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

        private static Color ReadEmissionColor(Material material)
        {
            return material.HasProperty("_EmissionColor")
                ? material.GetColor("_EmissionColor")
                : Color.black;
        }
    }
}
