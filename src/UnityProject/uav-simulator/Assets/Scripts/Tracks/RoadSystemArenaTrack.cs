#if UAVSIM_ROADSYSTEM
using Barmetler.RoadSystem;
using Barmetler.RoadSystem.Util;
using UnityEngine;

namespace UavSimulator.Tracks
{
    public sealed class RoadSystemArenaTrack : TrackBase
    {
        [SerializeField] private float groundSize = 44f;
        [SerializeField] private float roadWidth = 2.6f;
        [SerializeField] private float roadThickness = 0.10f;
        [SerializeField] private float boundaryHeight = 0.35f;
        [SerializeField] private float boundaryThickness = 0.14f;
        [SerializeField] private float dashSpacing = 1.1f;

        private bool built;
        private Road road;

        private void Awake()
        {
            BuildIfNeeded();
        }

        public override void ResetTrack(int seed)
        {
            _ = seed;
            BuildIfNeeded();
        }

        private void BuildIfNeeded()
        {
            if (built)
            {
                return;
            }

            ClearChildren();

            var ground = CreateBlock("Ground", transform, new Vector3(0f, -0.12f, 0f), new Vector3(groundSize, 0.24f, groundSize));
            var landscape = CreateBlock("Landscape", transform, new Vector3(0f, -0.28f, 0f), new Vector3(groundSize + 12f, 0.08f, groundSize + 12f));
            ApplyColor(ground, new Color(0.22f, 0.24f, 0.27f), 0.35f);
            ApplyColor(landscape, new Color(0.19f, 0.36f, 0.20f), 0.08f);

            var roadSystemRoot = new GameObject("RoadSystem");
            roadSystemRoot.transform.SetParent(transform, false);
            roadSystemRoot.transform.localPosition = Vector3.zero;
            roadSystemRoot.transform.localRotation = Quaternion.identity;
            var roadSystem = roadSystemRoot.AddComponent<RoadSystem>();
            _ = roadSystem;

            road = BuildMainRoad(roadSystemRoot.transform);
            CreateCenterMarkings(road);
            CreateBoundaries(road);
            CreateScenery();

            built = true;
        }

        private Road BuildMainRoad(Transform parent)
        {
            var roadGo = new GameObject("MainRoad");
            roadGo.transform.SetParent(parent, false);
            roadGo.transform.localPosition = Vector3.zero;
            roadGo.transform.localRotation = Quaternion.identity;

            var roadComponent = roadGo.AddComponent<Road>();
            var meshFilter = roadGo.AddComponent<MeshFilter>();
            var meshRenderer = roadGo.AddComponent<MeshRenderer>();
            var meshCollider = roadGo.AddComponent<MeshCollider>();
            _ = meshFilter;
            _ = meshCollider;

            var meshGenerator = roadGo.AddComponent<RoadMeshGenerator>();
            meshGenerator.settings = new RoadMeshGenerator.RoadMeshSettings
            {
                SourceOrientation = MeshConversion.MeshOrientation.Presets["UNITY"],
                uvOffset = new Vector2(0f, 1f),
            };
            meshGenerator.AutoGenerate = false;

            var sourceMeshGo = new GameObject("RoadSourceMesh");
            sourceMeshGo.transform.SetParent(roadGo.transform, false);
            sourceMeshGo.transform.localPosition = Vector3.zero;
            sourceMeshGo.transform.localRotation = Quaternion.identity;
            sourceMeshGo.hideFlags = HideFlags.HideInHierarchy;

            var sourceMeshFilter = sourceMeshGo.AddComponent<MeshFilter>();
            sourceMeshFilter.sharedMesh = CreateBoxMesh(roadWidth, roadThickness, 1.4f);
            var sourceRenderer = sourceMeshGo.AddComponent<MeshRenderer>();
            sourceRenderer.enabled = false;

            meshGenerator.SourceMesh = sourceMeshFilter;

            var roadMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            roadMaterial.color = new Color(0.15f, 0.15f, 0.16f);
            roadMaterial.SetFloat("_Smoothness", 0.42f);
            meshRenderer.sharedMaterial = roadMaterial;

            roadComponent.RefreshEndPoints(updatemesh: false);
            roadComponent.AutoSetControlPoints = true;

            roadComponent.MovePoint(0, new Vector3(-6f, 0f, -9f));
            roadComponent.MovePoint(3, new Vector3(-6f, 0f, -2.2f));
            roadComponent.AppendSegment(new Vector3(0.2f, 0f, 2.8f), isStart: false, normal: Vector3.up);
            roadComponent.AppendSegment(new Vector3(6.2f, 0f, -2.0f), isStart: false, normal: Vector3.up);
            roadComponent.AppendSegment(new Vector3(6.0f, 0f, 7.2f), isStart: false, normal: Vector3.up);

            meshGenerator.GenerateRoadMesh(stepSize: 0.65f);

            return roadComponent;
        }

        private void CreateCenterMarkings(Road roadComponent)
        {
            if (roadComponent == null)
            {
                return;
            }

            var markingsRoot = new GameObject("RoadMarkings");
            markingsRoot.transform.SetParent(transform, false);

            var points = roadComponent.GetEvenlySpacedPoints(dashSpacing, 1f);
            for (var i = 2; i < points.Length - 2; i += 2)
            {
                if ((i / 2) % 2 == 0)
                {
                    continue;
                }

                var point = points[i];
                var dash = CreateBlock($"Dash_{i:000}", markingsRoot.transform, point.position + Vector3.up * 0.03f, new Vector3(0.18f, 0.01f, 0.62f));
                dash.transform.rotation = Quaternion.LookRotation(point.forward, Vector3.up);
                DisableCollider(dash);
                ApplyColor(dash, new Color(0.95f, 0.95f, 0.95f), 0.1f);
            }
        }

        private void CreateBoundaries(Road roadComponent)
        {
            if (roadComponent == null)
            {
                return;
            }

            var boundariesRoot = new GameObject("RoadBoundaries");
            boundariesRoot.transform.SetParent(transform, false);

            var points = roadComponent.GetEvenlySpacedPoints(0.7f, 1f);
            var sideOffset = roadWidth * 0.5f + boundaryThickness * 0.5f;
            for (var i = 0; i < points.Length - 1; i++)
            {
                var a = points[i].position;
                var b = points[i + 1].position;
                var delta = b - a;
                var length = delta.magnitude;
                if (length < 0.05f)
                {
                    continue;
                }

                var forward = delta / length;
                var right = Vector3.Cross(Vector3.up, forward).normalized;
                var center = (a + b) * 0.5f + Vector3.up * (boundaryHeight * 0.5f);
                var rotation = Quaternion.LookRotation(forward, Vector3.up);
                CreateBoundaryBox(boundariesRoot.transform, $"Boundary_L_{i:000}", center - right * sideOffset, rotation, length + 0.05f);
                CreateBoundaryBox(boundariesRoot.transform, $"Boundary_R_{i:000}", center + right * sideOffset, rotation, length + 0.05f);
            }

            if (points.Length >= 2)
            {
                var startForward = points[1].position - points[0].position;
                var endForward = points[points.Length - 1].position - points[points.Length - 2].position;
                CreateCap(boundariesRoot.transform, "StartCap", points[0].position, startForward.normalized);
                CreateCap(boundariesRoot.transform, "FinishCap", points[points.Length - 1].position, endForward.normalized);
            }
        }

        private void CreateCap(Transform parent, string name, Vector3 position, Vector3 forward)
        {
            var cap = new GameObject(name);
            cap.transform.SetParent(parent, false);
            cap.transform.position = position + Vector3.up * (boundaryHeight * 0.5f);
            cap.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            var collider = cap.AddComponent<BoxCollider>();
            collider.size = new Vector3(roadWidth + boundaryThickness * 2f, boundaryHeight, boundaryThickness);
        }

        private void CreateBoundaryBox(Transform parent, string name, Vector3 worldCenter, Quaternion worldRotation, float length)
        {
            var barrier = new GameObject(name);
            barrier.transform.SetParent(parent, false);
            barrier.transform.position = worldCenter;
            barrier.transform.rotation = worldRotation;

            var collider = barrier.AddComponent<BoxCollider>();
            collider.size = new Vector3(boundaryThickness, boundaryHeight, length);
        }

        private void CreateScenery()
        {
            var sceneryRoot = new GameObject("Scenery");
            sceneryRoot.transform.SetParent(transform, false);

            CreateTree(sceneryRoot.transform, "TreeA", new Vector3(-10f, 0f, -11f), 1.0f);
            CreateTree(sceneryRoot.transform, "TreeB", new Vector3(-10f, 0f, 3f), 1.2f);
            CreateTree(sceneryRoot.transform, "TreeC", new Vector3(10f, 0f, -8f), 1.05f);
            CreateTree(sceneryRoot.transform, "TreeD", new Vector3(10f, 0f, 10f), 1.1f);
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
            ApplyColor(trunk, new Color(0.38f, 0.24f, 0.12f), 0.1f);

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "Crown";
            crown.transform.SetParent(tree.transform, false);
            crown.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            crown.transform.localScale = new Vector3(0.85f, 0.9f, 0.85f);
            DisableCollider(crown);
            ApplyColor(crown, new Color(0.18f, 0.45f, 0.18f), 0.12f);
        }

        private static Mesh CreateBoxMesh(float width, float height, float depth)
        {
            var mesh = new Mesh { name = "RoadSourceBoxMesh" };

            var hw = width * 0.5f;
            var hh = height * 0.5f;
            var hd = depth * 0.5f;

            var vertices = new[]
            {
                new Vector3(-hw, -hh, -hd), new Vector3(hw, -hh, -hd), new Vector3(hw, hh, -hd), new Vector3(-hw, hh, -hd),
                new Vector3(-hw, -hh, hd),  new Vector3(hw, -hh, hd),  new Vector3(hw, hh, hd),  new Vector3(-hw, hh, hd),
            };

            var triangles = new[]
            {
                0, 2, 1, 0, 3, 2, // back
                4, 5, 6, 4, 6, 7, // front
                0, 1, 5, 0, 5, 4, // bottom
                2, 3, 7, 2, 7, 6, // top
                1, 2, 6, 1, 6, 5, // right
                0, 4, 7, 0, 7, 3, // left
            };

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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

        private static GameObject CreateBlock(string name, Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localPosition;
            block.transform.localRotation = Quaternion.identity;
            block.transform.localScale = localScale;
            return block;
        }

        private static void DisableCollider(GameObject gameObject)
        {
            var collider = gameObject.GetComponent<Collider>();
            if (collider == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(collider);
            }
            else
            {
                DestroyImmediate(collider);
            }
        }

        private static void ApplyColor(GameObject gameObject, Color color, float smoothness)
        {
            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            material.SetFloat("_Smoothness", smoothness);
            renderer.sharedMaterial = material;
        }
    }
}
#else
using UnityEngine;

namespace UavSimulator.Tracks
{
    // Compile-safe fallback when Road System package assemblies are not wired into this asmdef.
    public sealed class RoadSystemArenaTrack : TrackBase
    {
        private void Awake()
        {
            Debug.LogWarning("RoadSystem package is unavailable in this assembly context. Using empty RoadSystemArenaTrack fallback.");
        }

        public override void ResetTrack(int seed)
        {
            _ = seed;
        }
    }
}
#endif
