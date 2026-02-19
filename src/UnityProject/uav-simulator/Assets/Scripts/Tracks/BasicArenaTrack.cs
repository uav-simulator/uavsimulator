using UnityEngine;

namespace UavSimulator.Tracks
{
    public sealed class BasicArenaTrack : TrackBase
    {
        [SerializeField] private float groundSize = 34f;
        [SerializeField] private float roadWidth = 2.4f;
        [SerializeField] private float roadThickness = 0.08f;
        [SerializeField] private float shoulderWidth = 1.1f;
        [SerializeField] private float edgeLineWidth = 0.08f;
        [SerializeField] private float barrierHeight = 0.35f;
        [SerializeField] private float barrierThickness = 0.16f;

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

            var ground = CreateBlock(
                "Ground",
                transform,
                new Vector3(0f, -0.1f, 0f),
                new Vector3(groundSize, 0.2f, groundSize));
            var landscape = CreateBlock(
                "Landscape",
                transform,
                new Vector3(0f, -0.25f, 0f),
                new Vector3(groundSize + 12f, 0.08f, groundSize + 12f));

            // S-like path with two turns:
            // segment A: straight forward, segment B: right turn corridor, segment C: left turn corridor.
            var segA = CreateRoadSegment("RoadA", new Vector3(0f, 0f, -4.5f), new Vector3(roadWidth, roadThickness, 7f));
            var segB = CreateRoadSegment("RoadB", new Vector3(3f, 0f, -1f), new Vector3(6f, roadThickness, roadWidth));
            var segC = CreateRoadSegment("RoadC", new Vector3(6f, 0f, 2f), new Vector3(roadWidth, roadThickness, 6f));

            // Corner fillers for smooth transitions.
            var turnA = CreateRoadSegment("RoadTurnA", new Vector3(0f, 0f, -1f), new Vector3(roadWidth, roadThickness, roadWidth));
            var turnB = CreateRoadSegment("RoadTurnB", new Vector3(6f, 0f, -1f), new Vector3(roadWidth, roadThickness, roadWidth));

            CreateShoulders();
            CreateEdgeLines();
            CreateDashedMarkings();
            CreateInvisibleBoundaries();
            CreateScenery();

            MarkStatic(ground, landscape, segA, segB, segC, turnA, turnB);

            built = true;
        }

        private void ClearChildren()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private GameObject CreateRoadSegment(string name, Vector3 localPosition, Vector3 localScale)
        {
            var road = CreateBlock(name, transform, localPosition, localScale);
            return road;
        }

        private void CreateShoulders()
        {
            var shoulderThickness = roadThickness * 0.5f;
            var shoulderY = -0.03f;
            var totalWidth = roadWidth + shoulderWidth * 2f;

            CreateBlock("ShoulderA", transform, new Vector3(0f, shoulderY, -4.5f), new Vector3(totalWidth, shoulderThickness, 7f));
            CreateBlock("ShoulderB", transform, new Vector3(3f, shoulderY, -1f), new Vector3(6f, shoulderThickness, totalWidth));
            CreateBlock("ShoulderC", transform, new Vector3(6f, shoulderY, 2f), new Vector3(totalWidth, shoulderThickness, 6f));
            CreateBlock("ShoulderTurnA", transform, new Vector3(0f, shoulderY, -1f), new Vector3(totalWidth, shoulderThickness, totalWidth));
            CreateBlock("ShoulderTurnB", transform, new Vector3(6f, shoulderY, -1f), new Vector3(totalWidth, shoulderThickness, totalWidth));
        }

        private void CreateEdgeLines()
        {
            var y = 0.05f;
            var hz = edgeLineWidth;
            var halfRoad = roadWidth * 0.5f;

            // Segment A (vertical).
            CreateBlock("EdgeA_Left", transform, new Vector3(-halfRoad + hz, y, -4.5f), new Vector3(hz, 0.01f, 7f));
            CreateBlock("EdgeA_Right", transform, new Vector3(halfRoad - hz, y, -4.5f), new Vector3(hz, 0.01f, 7f));

            // Segment B (horizontal).
            CreateBlock("EdgeB_Down", transform, new Vector3(3f, y, -1f - halfRoad + hz), new Vector3(6f, 0.01f, hz));
            CreateBlock("EdgeB_Up", transform, new Vector3(3f, y, -1f + halfRoad - hz), new Vector3(6f, 0.01f, hz));

            // Segment C (vertical).
            CreateBlock("EdgeC_Left", transform, new Vector3(6f - halfRoad + hz, y, 2f), new Vector3(hz, 0.01f, 6f));
            CreateBlock("EdgeC_Right", transform, new Vector3(6f + halfRoad - hz, y, 2f), new Vector3(hz, 0.01f, 6f));
        }

        private void CreateDashedMarkings()
        {
            var markingsRoot = new GameObject("Markings");
            markingsRoot.transform.SetParent(transform, false);

            // Segment A markings.
            for (var z = -7.5f; z <= -1.5f; z += 1.0f)
            {
                CreateBlock(
                    "DashA",
                    markingsRoot.transform,
                    new Vector3(0f, 0.05f, z),
                    new Vector3(0.2f, 0.01f, 0.55f));
            }

            // Segment B markings.
            for (var x = 0.8f; x <= 5.2f; x += 1.0f)
            {
                CreateBlock(
                    "DashB",
                    markingsRoot.transform,
                    new Vector3(x, 0.05f, -1f),
                    new Vector3(0.55f, 0.01f, 0.2f));
            }

            // Segment C markings.
            for (var z = -0.2f; z <= 4.2f; z += 1.0f)
            {
                CreateBlock(
                    "DashC",
                    markingsRoot.transform,
                    new Vector3(6f, 0.05f, z),
                    new Vector3(0.2f, 0.01f, 0.55f));
            }
        }

        private void CreateScenery()
        {
            var sceneryRoot = new GameObject("Scenery");
            sceneryRoot.transform.SetParent(transform, false);

            CreateTree(sceneryRoot.transform, "TreeA", new Vector3(-5.4f, 0f, -7.6f), 1.0f);
            CreateTree(sceneryRoot.transform, "TreeB", new Vector3(-4.8f, 0f, 1.6f), 1.15f);
            CreateTree(sceneryRoot.transform, "TreeC", new Vector3(10.2f, 0f, 4.8f), 0.95f);
            CreateTree(sceneryRoot.transform, "TreeD", new Vector3(9.6f, 0f, -7.4f), 1.1f);

            CreateCone(sceneryRoot.transform, "ConeStartL", new Vector3(-1.5f, 0f, -7.5f));
            CreateCone(sceneryRoot.transform, "ConeStartR", new Vector3(1.5f, 0f, -7.5f));
            CreateCone(sceneryRoot.transform, "ConeMidL", new Vector3(2.5f, 0f, -2.6f));
            CreateCone(sceneryRoot.transform, "ConeMidR", new Vector3(3.5f, 0f, 0.6f));
            CreateCone(sceneryRoot.transform, "ConeFinishL", new Vector3(4.5f, 0f, 4.8f));
            CreateCone(sceneryRoot.transform, "ConeFinishR", new Vector3(7.5f, 0f, 4.8f));
        }

        private void CreateInvisibleBoundaries()
        {
            var boundariesRoot = new GameObject("RoadBoundaries");
            boundariesRoot.transform.SetParent(transform, false);
            var halfRoad = roadWidth * 0.5f;
            var sideOffset = halfRoad + barrierThickness * 0.5f;
            var capOffset = barrierThickness * 0.5f;
            var y = barrierHeight * 0.5f;

            // Segment A boundaries.
            CreateInvisibleBarrier(boundariesRoot.transform, "A_Left", new Vector3(-sideOffset, y, -4.5f), new Vector3(barrierThickness, barrierHeight, 7f));
            CreateInvisibleBarrier(boundariesRoot.transform, "A_Right", new Vector3(sideOffset, y, -4.5f), new Vector3(barrierThickness, barrierHeight, 7f));

            // Segment B boundaries.
            CreateInvisibleBarrier(boundariesRoot.transform, "B_Bottom", new Vector3(3f, y, -1f - sideOffset), new Vector3(6f, barrierHeight, barrierThickness));
            CreateInvisibleBarrier(boundariesRoot.transform, "B_Top", new Vector3(3f, y, -1f + sideOffset), new Vector3(6f, barrierHeight, barrierThickness));

            // Segment C boundaries.
            CreateInvisibleBarrier(boundariesRoot.transform, "C_Left", new Vector3(6f - sideOffset, y, 2f), new Vector3(barrierThickness, barrierHeight, 6f));
            CreateInvisibleBarrier(boundariesRoot.transform, "C_Right", new Vector3(6f + sideOffset, y, 2f), new Vector3(barrierThickness, barrierHeight, 6f));

            // Extensions around turns to remove boundary gaps.
            CreateInvisibleBarrier(boundariesRoot.transform, "TurnA_LeftExtension", new Vector3(-sideOffset, y, -0.4f), new Vector3(barrierThickness, barrierHeight, 1.2f));
            CreateInvisibleBarrier(boundariesRoot.transform, "TurnB_RightExtension", new Vector3(6f + sideOffset, y, -1.6f), new Vector3(barrierThickness, barrierHeight, 1.2f));

            // End caps to avoid exiting road from start/end.
            CreateInvisibleBarrier(boundariesRoot.transform, "StartCap", new Vector3(0f, y, -8f - capOffset), new Vector3(roadWidth, barrierHeight, barrierThickness));
            CreateInvisibleBarrier(boundariesRoot.transform, "FinishCap", new Vector3(6f, y, 5f + capOffset), new Vector3(roadWidth, barrierHeight, barrierThickness));
        }

        private static void CreateInvisibleBarrier(Transform parent, string name, Vector3 localPosition, Vector3 localScale)
        {
            var barrier = new GameObject(name);
            barrier.transform.SetParent(parent, false);
            barrier.transform.localPosition = localPosition;
            barrier.transform.localRotation = Quaternion.identity;

            var collider = barrier.AddComponent<BoxCollider>();
            collider.size = localScale;
        }

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

        private static void DisableCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(collider);
            }
            else
            {
                Object.DestroyImmediate(collider);
            }
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
            if (go == null)
            {
                return;
            }

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

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
            else if (name.StartsWith("Road", System.StringComparison.Ordinal))
            {
                color = new Color(0.15f, 0.15f, 0.16f);
                smoothness = 0.45f;
            }
            else if (name.StartsWith("Shoulder", System.StringComparison.Ordinal))
            {
                color = new Color(0.25f, 0.24f, 0.20f);
                smoothness = 0.12f;
            }
            else if (name.StartsWith("Dash", System.StringComparison.Ordinal) ||
                     name.StartsWith("Edge", System.StringComparison.Ordinal))
            {
                color = new Color(0.95f, 0.95f, 0.95f);
                smoothness = 0.1f;
            }
            else if (name.StartsWith("Cone", System.StringComparison.Ordinal))
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

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            material.SetFloat("_Smoothness", smoothness);
            renderer.sharedMaterial = material;
        }

        private static void MarkStatic(params GameObject[] objects)
        {
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                obj.isStatic = true;
            }
        }
    }
}
