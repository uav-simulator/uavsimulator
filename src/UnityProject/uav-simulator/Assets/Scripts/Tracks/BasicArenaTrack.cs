using System;
using UavSimulator.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace UavSimulator.Tracks
{
    /// <summary>
    /// Procedural S-shaped corridor track for A->B navigation training.
    ///
    /// Layout (top-down, XZ plane):
    ///   Segment A: x=0, z=-8..z=-1  (straight, +Z direction)
    ///   Turn 1:    wide plaza around (0, -1) connecting A to B
    ///   Segment B: z=-1, x=0..x=6   (straight, +X direction)
    ///   Turn 2:    wide plaza around (6, -1) connecting B to C
    ///   Segment C: x=6, z=-1..z=5   (straight, +Z direction)
    ///
    /// Robot spawns at (0, 0.2, -6) facing +Z. Goal is at (6, ?, 5).
    ///
    /// Out-of-bounds is handled by the Python Gymnasium environment
    /// (lateral distance check), so no invisible collision barriers are needed.
    /// </summary>
    public sealed class BasicArenaTrack : TrackBase
    {
        [SerializeField] private float groundSize = 34f;
        [SerializeField] private float roadWidth = 3.0f;
        [SerializeField] private float roadThickness = 0.08f;
        [SerializeField] private float shoulderWidth = 1.0f;
        [SerializeField] private float edgeLineWidth = 0.08f;
        [SerializeField] private float turnPlazaSize = 4.0f;

        private bool built;

        private void Awake()
        {
            BuildIfNeeded();
        }

        public override void ResetTrack(int seed)
        {
            if (!built)
            {
                BuildIfNeeded();
            }
        }

        private void BuildIfNeeded()
        {
            if (built) return;
            if (transform.childCount > 0)
            {
                ClearChildren();
            }

            // Ground & landscape.
            var ground = CreateBlock(
                "Ground", transform,
                new Vector3(0f, -0.1f, 0f),
                new Vector3(groundSize, 0.2f, groundSize));
            var landscape = CreateBlock(
                "Landscape", transform,
                new Vector3(0f, -0.25f, 0f),
                new Vector3(groundSize + 12f, 0.08f, groundSize + 12f));

            // Road segments — three straight corridors.
            var segA = CreateRoadSegment("RoadA",
                new Vector3(0f, 0f, -4.5f),
                new Vector3(roadWidth, roadThickness, 7f));

            var segB = CreateRoadSegment("RoadB",
                new Vector3(3f, 0f, -1f),
                new Vector3(6f + roadWidth, roadThickness, roadWidth));

            var segC = CreateRoadSegment("RoadC",
                new Vector3(6f, 0f, 2f),
                new Vector3(roadWidth, roadThickness, 6f));

            // Turn plazas — wide squares at each corner for easy turning.
            // Turn 1: where seg A meets seg B (around x=0, z=-1).
            var turn1 = CreateRoadSegment("RoadTurn1",
                new Vector3(0f, 0f, -1f),
                new Vector3(turnPlazaSize, roadThickness, turnPlazaSize));

            // Turn 2: where seg B meets seg C (around x=6, z=-1).
            var turn2 = CreateRoadSegment("RoadTurn2",
                new Vector3(6f, 0f, -1f),
                new Vector3(turnPlazaSize, roadThickness, turnPlazaSize));

            CreateShoulders();
            CreateEdgeLines();
            CreateDashedMarkings();
            CreateScenery();

            MarkStatic(ground, landscape, segA, segB, segC, turn1, turn2);
            built = true;
        }

        // ── Road construction helpers ──

        private GameObject CreateRoadSegment(string name, Vector3 localPosition, Vector3 localScale)
        {
            return CreateBlock(name, transform, localPosition, localScale);
        }

        private void CreateShoulders()
        {
            var shoulderThickness = roadThickness * 0.5f;
            var y = -0.03f;
            var total = roadWidth + shoulderWidth * 2f;

            DisableCollider(CreateBlock("ShoulderA", transform,
                new Vector3(0f, y, -4.5f), new Vector3(total, shoulderThickness, 7f)));
            DisableCollider(CreateBlock("ShoulderB", transform,
                new Vector3(3f, y, -1f), new Vector3(6f + total, shoulderThickness, total)));
            DisableCollider(CreateBlock("ShoulderC", transform,
                new Vector3(6f, y, 2f), new Vector3(total, shoulderThickness, 6f)));
            DisableCollider(CreateBlock("ShoulderTurn1", transform,
                new Vector3(0f, y, -1f), new Vector3(turnPlazaSize + shoulderWidth, shoulderThickness, turnPlazaSize + shoulderWidth)));
            DisableCollider(CreateBlock("ShoulderTurn2", transform,
                new Vector3(6f, y, -1f), new Vector3(turnPlazaSize + shoulderWidth, shoulderThickness, turnPlazaSize + shoulderWidth)));
        }

        private void CreateEdgeLines()
        {
            var y = 0.05f;
            var w = edgeLineWidth;
            var hr = roadWidth * 0.5f;

            // Segment A edges (vertical road).
            DisableCollider(CreateBlock("EdgeA_Left", transform,
                new Vector3(-hr, y, -4.5f), new Vector3(w, 0.01f, 7f)));
            DisableCollider(CreateBlock("EdgeA_Right", transform,
                new Vector3(hr, y, -4.5f), new Vector3(w, 0.01f, 7f)));

            // Segment B edges (horizontal road).
            DisableCollider(CreateBlock("EdgeB_Down", transform,
                new Vector3(3f, y, -1f - hr), new Vector3(6f + roadWidth, 0.01f, w)));
            DisableCollider(CreateBlock("EdgeB_Up", transform,
                new Vector3(3f, y, -1f + hr), new Vector3(6f + roadWidth, 0.01f, w)));

            // Segment C edges (vertical road).
            DisableCollider(CreateBlock("EdgeC_Left", transform,
                new Vector3(6f - hr, y, 2f), new Vector3(w, 0.01f, 6f)));
            DisableCollider(CreateBlock("EdgeC_Right", transform,
                new Vector3(6f + hr, y, 2f), new Vector3(w, 0.01f, 6f)));
        }

        private void CreateDashedMarkings()
        {
            var root = new GameObject("Markings");
            root.transform.SetParent(transform, false);

            // Segment A center dashes.
            for (var z = -7.5f; z <= -1.5f; z += 1.0f)
            {
                DisableCollider(CreateBlock("DashA", root.transform,
                    new Vector3(0f, 0.05f, z),
                    new Vector3(0.2f, 0.01f, 0.55f)));
            }

            // Segment B center dashes.
            for (var x = 0.8f; x <= 5.2f; x += 1.0f)
            {
                DisableCollider(CreateBlock("DashB", root.transform,
                    new Vector3(x, 0.05f, -1f),
                    new Vector3(0.55f, 0.01f, 0.2f)));
            }

            // Segment C center dashes.
            for (var z = -0.2f; z <= 4.2f; z += 1.0f)
            {
                DisableCollider(CreateBlock("DashC", root.transform,
                    new Vector3(6f, 0.05f, z),
                    new Vector3(0.2f, 0.01f, 0.55f)));
            }
        }

        // ── Scenery ──

        private void CreateScenery()
        {
            var root = new GameObject("Scenery");
            root.transform.SetParent(transform, false);

            // Trees.
            CreateTree(root.transform, "TreeA", new Vector3(-5.4f, 0f, -7.6f), 1.0f);
            CreateTree(root.transform, "TreeB", new Vector3(-4.8f, 0f, 1.6f), 1.15f);
            CreateTree(root.transform, "TreeC", new Vector3(10.2f, 0f, 4.8f), 0.95f);
            CreateTree(root.transform, "TreeD", new Vector3(9.6f, 0f, -7.4f), 1.1f);
            CreateTree(root.transform, "TreeE", new Vector3(-3.6f, 0f, -4.2f), 0.85f);
            CreateTree(root.transform, "TreeF", new Vector3(3.2f, 0f, 3.8f), 1.2f);
            CreateTree(root.transform, "TreeG", new Vector3(8.8f, 0f, -2.0f), 0.9f);
            CreateTree(root.transform, "TreeH", new Vector3(-6.2f, 0f, 4.5f), 1.05f);
            CreateTree(root.transform, "TreeI", new Vector3(11.0f, 0f, 1.2f), 0.75f);
            CreateTree(root.transform, "TreeJ", new Vector3(-3.0f, 0f, 6.0f), 1.3f);
            CreateTree(root.transform, "TreeK", new Vector3(2.0f, 0f, -9.0f), 0.8f);
            CreateTree(root.transform, "TreeL", new Vector3(9.0f, 0f, 6.5f), 1.0f);

            // Bushes.
            CreateBush(root.transform, "BushA", new Vector3(-3.0f, 0f, -6.0f), 0.6f);
            CreateBush(root.transform, "BushB", new Vector3(4.0f, 0f, -4.5f), 0.5f);
            CreateBush(root.transform, "BushC", new Vector3(8.0f, 0f, 3.0f), 0.55f);
            CreateBush(root.transform, "BushD", new Vector3(-5.0f, 0f, -2.0f), 0.65f);
            CreateBush(root.transform, "BushE", new Vector3(10.5f, 0f, -5.5f), 0.45f);
            CreateBush(root.transform, "BushF", new Vector3(-2.5f, 0f, 3.5f), 0.5f);

            // Traffic cones at key points.
            CreateCone(root.transform, "ConeStartL", new Vector3(-1.8f, 0f, -7.5f));
            CreateCone(root.transform, "ConeStartR", new Vector3(1.8f, 0f, -7.5f));
            CreateCone(root.transform, "ConeMidL", new Vector3(2.5f, 0f, -3.0f));
            CreateCone(root.transform, "ConeMidR", new Vector3(3.5f, 0f, 0.8f));
            CreateCone(root.transform, "ConeFinishL", new Vector3(4.2f, 0f, 4.8f));
            CreateCone(root.transform, "ConeFinishR", new Vector3(7.8f, 0f, 4.8f));

            // Curb stones along road edges.
            CreateCurbStones();

            // Street lamp posts.
            CreateLampPost(root.transform, "LampA", new Vector3(-2.8f, 0f, -6.0f));
            CreateLampPost(root.transform, "LampB", new Vector3(-2.8f, 0f, -2.0f));
            CreateLampPost(root.transform, "LampC", new Vector3(3.0f, 0f, -3.4f));
            CreateLampPost(root.transform, "LampD", new Vector3(8.8f, 0f, -3.4f));
            CreateLampPost(root.transform, "LampE", new Vector3(8.8f, 0f, 1.5f));
            CreateLampPost(root.transform, "LampF", new Vector3(8.8f, 0f, 4.5f));

            // Grass patches.
            CreateGrassPatch(root.transform, "GrassA", new Vector3(-8.0f, 0f, -5.0f), 3.0f);
            CreateGrassPatch(root.transform, "GrassB", new Vector3(12.0f, 0f, 2.0f), 2.5f);
            CreateGrassPatch(root.transform, "GrassC", new Vector3(-7.0f, 0f, 5.0f), 2.0f);
            CreateGrassPatch(root.transform, "GrassD", new Vector3(3.0f, 0f, 8.0f), 3.5f);
        }

        private void CreateCurbStones()
        {
            var root = new GameObject("Curbs");
            root.transform.SetParent(transform, false);
            var hr = roadWidth * 0.5f;
            var curbH = 0.06f;
            var curbW = 0.12f;

            // Segment A curbs.
            CreateCurb(root.transform, "CurbA_L",
                new Vector3(-hr - curbW * 0.5f, curbH * 0.5f, -4.5f),
                new Vector3(curbW, curbH, 7f));
            CreateCurb(root.transform, "CurbA_R",
                new Vector3(hr + curbW * 0.5f, curbH * 0.5f, -4.5f),
                new Vector3(curbW, curbH, 7f));

            // Segment B curbs.
            CreateCurb(root.transform, "CurbB_D",
                new Vector3(3f, curbH * 0.5f, -1f - hr - curbW * 0.5f),
                new Vector3(6f + roadWidth, curbH, curbW));
            CreateCurb(root.transform, "CurbB_U",
                new Vector3(3f, curbH * 0.5f, -1f + hr + curbW * 0.5f),
                new Vector3(6f + roadWidth, curbH, curbW));

            // Segment C curbs.
            CreateCurb(root.transform, "CurbC_L",
                new Vector3(6f - hr - curbW * 0.5f, curbH * 0.5f, 2f),
                new Vector3(curbW, curbH, 6f));
            CreateCurb(root.transform, "CurbC_R",
                new Vector3(6f + hr + curbW * 0.5f, curbH * 0.5f, 2f),
                new Vector3(curbW, curbH, 6f));
        }

        // ── Primitive helpers ──

        private static void CreateTree(Transform parent, string name, Vector3 localPosition, float scale)
        {
            var tree = new GameObject(name);
            tree.transform.SetParent(parent, false);
            tree.transform.localPosition = localPosition;
            tree.transform.localScale = Vector3.one * scale;

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(tree.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            trunk.transform.localScale = new Vector3(0.12f, 0.45f, 0.12f);
            DisableCollider(trunk);
            ApplyColorByName(trunk);

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "Crown";
            crown.transform.SetParent(tree.transform, false);
            crown.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            crown.transform.localScale = new Vector3(0.85f, 0.9f, 0.85f);
            DisableCollider(crown);
            ApplyColorByName(crown);
        }

        private static void CreateBush(Transform parent, string name, Vector3 localPosition, float scale)
        {
            var bush = new GameObject(name);
            bush.transform.SetParent(parent, false);
            bush.transform.localPosition = localPosition;

            var foliage = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            foliage.name = "BushFoliage";
            foliage.transform.SetParent(bush.transform, false);
            foliage.transform.localPosition = new Vector3(0f, 0.15f * scale, 0f);
            foliage.transform.localScale = new Vector3(0.6f * scale, 0.35f * scale, 0.6f * scale);
            DisableCollider(foliage);
            ApplyColorByName(foliage);
        }

        private static void CreateCone(Transform parent, string name, Vector3 localPosition)
        {
            var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cone.name = name;
            cone.transform.SetParent(parent, false);
            cone.transform.localPosition = new Vector3(localPosition.x, 0.14f, localPosition.z);
            cone.transform.localScale = new Vector3(0.14f, 0.14f, 0.14f);
            DisableCollider(cone);
            ApplyColorByName(cone);
        }

        private static void CreateCurb(Transform parent, string name, Vector3 localPosition, Vector3 localScale)
        {
            var curb = CreateBlock(name, parent, localPosition, localScale);
            var renderer = curb.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateLitMaterial(new Color(0.55f, 0.55f, 0.50f), 0.15f);
            }
            DisableCollider(curb);
        }

        private static void CreateLampPost(Transform parent, string name, Vector3 localPosition)
        {
            var lamp = new GameObject(name);
            lamp.transform.SetParent(parent, false);
            lamp.transform.localPosition = localPosition;

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "LampPole";
            pole.transform.SetParent(lamp.transform, false);
            pole.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            pole.transform.localScale = new Vector3(0.04f, 1.0f, 0.04f);
            DisableCollider(pole);
            ApplyColorByName(pole);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "LampHead";
            head.transform.SetParent(lamp.transform, false);
            head.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            head.transform.localScale = new Vector3(0.2f, 0.12f, 0.2f);
            DisableCollider(head);
            ApplyColorByName(head);
        }

        private static void CreateGrassPatch(Transform parent, string name, Vector3 localPosition, float radius)
        {
            var patch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            patch.name = name;
            patch.transform.SetParent(parent, false);
            patch.transform.localPosition = new Vector3(localPosition.x, -0.04f, localPosition.z);
            patch.transform.localScale = new Vector3(radius, 0.02f, radius);
            DisableCollider(patch);
            ApplyColorByName(patch);
        }

        // ── Utility ──

        private void ClearChildren()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }

        private static void DisableCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(collider);
            else
                UnityEngine.Object.DestroyImmediate(collider);
        }

        private static GameObject CreateBlock(string name, Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            ApplyColorByName(go);
            return go;
        }

        private static void ApplyColorByName(GameObject go)
        {
            if (go == null) return;
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            var name = go.name;
            var color = new Color(0.70f, 0.70f, 0.70f);
            var smoothness = 0.18f;

            if (name == "Ground")
            {
                color = new Color(0.23f, 0.24f, 0.27f);
                smoothness = 0.35f;
            }
            else if (name == "Landscape")
            {
                color = new Color(0.19f, 0.36f, 0.20f);
                smoothness = 0.08f;
            }
            else if (name.StartsWith("Road", StringComparison.Ordinal))
            {
                color = new Color(0.15f, 0.15f, 0.16f);
                smoothness = 0.45f;
            }
            else if (name.StartsWith("Shoulder", StringComparison.Ordinal))
            {
                color = new Color(0.25f, 0.24f, 0.20f);
                smoothness = 0.12f;
            }
            else if (name.StartsWith("Dash", StringComparison.Ordinal) ||
                     name.StartsWith("Edge", StringComparison.Ordinal))
            {
                color = new Color(0.95f, 0.95f, 0.95f);
                smoothness = 0.1f;
            }
            else if (name.StartsWith("Cone", StringComparison.Ordinal))
            {
                color = new Color(0.95f, 0.45f, 0.10f);
                smoothness = 0.12f;
            }
            else if (name == "Trunk")
            {
                color = new Color(0.38f, 0.24f, 0.12f);
                smoothness = 0.1f;
            }
            else if (name == "Crown")
            {
                color = new Color(0.18f, 0.45f, 0.18f);
                smoothness = 0.12f;
            }
            else if (name == "BushFoliage")
            {
                color = new Color(0.22f, 0.50f, 0.15f);
                smoothness = 0.08f;
            }
            else if (name == "LampPole")
            {
                color = new Color(0.30f, 0.30f, 0.32f);
                smoothness = 0.55f;
            }
            else if (name == "LampHead")
            {
                color = new Color(0.95f, 0.92f, 0.75f);
                smoothness = 0.6f;
            }
            else if (name.StartsWith("Grass", StringComparison.Ordinal))
            {
                color = new Color(0.24f, 0.42f, 0.16f);
                smoothness = 0.05f;
            }

            renderer.sharedMaterial = CreateLitMaterial(color, smoothness);
        }

        private static Material CreateLitMaterial(Color color, float smoothness)
        {
            var shader = ResolveRuntimeLitShader();
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        private static Shader ResolveRuntimeLitShader()
            => RuntimeMaterialCompatibility.ResolveCompatibleLitShader();

        private static bool IsUrpActive()
            => RuntimeMaterialCompatibility.IsUrpActive();

        private static void MarkStatic(params GameObject[] objects)
        {
            foreach (var obj in objects)
            {
                if (obj != null) obj.isStatic = true;
            }
        }
    }
}
