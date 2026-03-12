#if UAVSIM_ROADSYSTEM
using Barmetler.RoadSystem;
using Barmetler.RoadSystem.Util;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UavSimulator.Tracks
{
    public sealed class RoadSystemRealisticTrack : TrackBase
    {
        private const string ArcadeEnvironmentMaterialPath = "Assets/ARCADE - FREE Racing Car/Materials/AFRC_Env_Mat.mat";
        private const string ArcadeRoadMeshPath = "Assets/ARCADE - FREE Racing Car/Meshes/Road.fbx";
        private const string ArcadeDaySkyboxPath = "Assets/ARCADE - FREE Racing Car/Skybox/Day/Day Skybox.mat";

        [SerializeField] private float groundSize = 90f;
        [SerializeField] private float roadWidth = 3.4f;
        [SerializeField] private float roadThickness = 0.11f;
        [SerializeField] private float shoulderWidth = 0.55f;
        [SerializeField] private float shoulderHeight = 0.035f;
        [SerializeField] private float curbWidth = 0.22f;
        [SerializeField] private float curbHeight = 0.09f;
        [SerializeField] private float boundaryHeight = 0.34f;
        [SerializeField] private float boundaryThickness = 0.12f;
        [SerializeField] private float dashSpacing = 1.2f;

        private bool built;
        private Road road;

        private Material roadMaterial;
        private Material groundMaterial;
        private Material laneMaterial;
        private Material shoulderMaterial;
        private Material boundaryMaterial;
        private Material curbRedMaterial;
        private Material curbWhiteMaterial;

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
            BuildMaterials();
            TryApplyDaySkybox();

            CreateLandscape();

            var roadSystemRoot = new GameObject("RoadSystem");
            roadSystemRoot.transform.SetParent(transform, false);
            roadSystemRoot.transform.localPosition = Vector3.zero;
            roadSystemRoot.transform.localRotation = Quaternion.identity;
            _ = roadSystemRoot.AddComponent<RoadSystem>();

            road = BuildMainRoad(roadSystemRoot.transform);
            CreateShoulders(road);
            CreateCenterAndEdgeMarkings(road);
            CreateCurbs(road);
            CreateBoundaries(road);
            CreateStartAndFinish(road);
            CreateLighting();
            CreateDecorativeProps();
            CreateArcadeBackdropMeshes();

            built = true;
        }

        private void BuildMaterials()
        {
            roadMaterial = CreateLitMaterial(new Color(0.10f, 0.10f, 0.11f), 0.30f);
            groundMaterial = TryCloneAssetMaterial(ArcadeEnvironmentMaterialPath) ?? CreateLitMaterial(new Color(0.23f, 0.31f, 0.22f), 0.07f);
            laneMaterial = CreateLitMaterial(new Color(0.95f, 0.95f, 0.95f), 0.08f);
            shoulderMaterial = CreateLitMaterial(new Color(0.22f, 0.20f, 0.18f), 0.10f);
            boundaryMaterial = CreateLitMaterial(new Color(0.72f, 0.74f, 0.76f), 0.20f);
            curbRedMaterial = CreateLitMaterial(new Color(0.78f, 0.16f, 0.14f), 0.14f);
            curbWhiteMaterial = CreateLitMaterial(new Color(0.89f, 0.90f, 0.90f), 0.10f);
        }

        private void CreateLandscape()
        {
            var landscape = CreateBlock("Landscape", transform, new Vector3(0f, -0.30f, 0f), new Vector3(groundSize + 16f, 0.12f, groundSize + 16f));
            var ground = CreateBlock("Ground", transform, new Vector3(0f, -0.14f, 0f), new Vector3(groundSize, 0.28f, groundSize));
            var infield = CreateBlock("Infield", transform, new Vector3(-0.8f, -0.11f, -0.4f), new Vector3(22f, 0.05f, 22f));

            ApplyMaterial(landscape, groundMaterial);
            ApplyMaterial(ground, groundMaterial);
            ApplyColor(infield, new Color(0.28f, 0.35f, 0.25f), 0.08f);
        }

        private Road BuildMainRoad(Transform parent)
        {
            var roadGo = new GameObject("MainRoad");
            roadGo.transform.SetParent(parent, false);
            roadGo.transform.localPosition = Vector3.zero;
            roadGo.transform.localRotation = Quaternion.identity;

            var roadComponent = roadGo.AddComponent<Road>();
            _ = roadGo.AddComponent<MeshFilter>();
            _ = roadGo.AddComponent<MeshCollider>();
            var meshRenderer = roadGo.AddComponent<MeshRenderer>();

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
            meshRenderer.sharedMaterial = roadMaterial;

            roadComponent.RefreshEndPoints(updatemesh: false);
            roadComponent.AutoSetControlPoints = true;

            roadComponent.MovePoint(0, new Vector3(-11f, 0f, -14f));
            roadComponent.MovePoint(3, new Vector3(-11f, 0f, -4f));
            roadComponent.AppendSegment(new Vector3(-6f, 0f, 4f), isStart: false, normal: Vector3.up);
            roadComponent.AppendSegment(new Vector3(2f, 0f, 8f), isStart: false, normal: Vector3.up);
            roadComponent.AppendSegment(new Vector3(10f, 0f, 2f), isStart: false, normal: Vector3.up);
            roadComponent.AppendSegment(new Vector3(11f, 0f, -7f), isStart: false, normal: Vector3.up);
            roadComponent.AppendSegment(new Vector3(4f, 0f, -13f), isStart: false, normal: Vector3.up);

            meshGenerator.GenerateRoadMesh(stepSize: 0.55f);
            return roadComponent;
        }

        private void CreateShoulders(Road roadComponent)
        {
            if (roadComponent == null)
            {
                return;
            }

            var shouldersRoot = new GameObject("RoadShoulders");
            shouldersRoot.transform.SetParent(transform, false);

            var points = roadComponent.GetEvenlySpacedPoints(0.75f, 1f);
            var sideOffset = roadWidth * 0.5f + shoulderWidth * 0.5f;
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
                var center = (a + b) * 0.5f + Vector3.up * (shoulderHeight * 0.5f);
                var rotation = Quaternion.LookRotation(forward, Vector3.up);

                CreateShoulderBox(shouldersRoot.transform, $"Shoulder_L_{i:000}", center - right * sideOffset, rotation, length + 0.08f);
                CreateShoulderBox(shouldersRoot.transform, $"Shoulder_R_{i:000}", center + right * sideOffset, rotation, length + 0.08f);
            }
        }

        private void CreateCenterAndEdgeMarkings(Road roadComponent)
        {
            if (roadComponent == null)
            {
                return;
            }

            var markingsRoot = new GameObject("RoadMarkings");
            markingsRoot.transform.SetParent(transform, false);

            var points = roadComponent.GetEvenlySpacedPoints(dashSpacing, 1f);
            var edgeOffset = roadWidth * 0.5f - 0.16f;
            for (var i = 2; i < points.Length - 2; i++)
            {
                var point = points[i];
                var right = Vector3.Cross(Vector3.up, point.forward).normalized;
                var rotation = Quaternion.LookRotation(point.forward, Vector3.up);

                if ((i % 3) == 0)
                {
                    var dash = CreateBlock($"CenterDash_{i:000}", markingsRoot.transform, point.position + Vector3.up * 0.03f, new Vector3(0.20f, 0.01f, 0.70f));
                    dash.transform.rotation = rotation;
                    DisableCollider(dash);
                    ApplyMaterial(dash, laneMaterial);
                }

                var edgeLeft = CreateBlock($"EdgeL_{i:000}", markingsRoot.transform, point.position - right * edgeOffset + Vector3.up * 0.02f, new Vector3(0.10f, 0.01f, 0.44f));
                edgeLeft.transform.rotation = rotation;
                DisableCollider(edgeLeft);
                ApplyMaterial(edgeLeft, laneMaterial);

                var edgeRight = CreateBlock($"EdgeR_{i:000}", markingsRoot.transform, point.position + right * edgeOffset + Vector3.up * 0.02f, new Vector3(0.10f, 0.01f, 0.44f));
                edgeRight.transform.rotation = rotation;
                DisableCollider(edgeRight);
                ApplyMaterial(edgeRight, laneMaterial);
            }
        }

        private void CreateCurbs(Road roadComponent)
        {
            if (roadComponent == null)
            {
                return;
            }

            var curbsRoot = new GameObject("RoadCurbs");
            curbsRoot.transform.SetParent(transform, false);

            var points = roadComponent.GetEvenlySpacedPoints(0.9f, 1f);
            var sideOffset = roadWidth * 0.5f + shoulderWidth + curbWidth * 0.5f;
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
                var center = (a + b) * 0.5f + Vector3.up * (curbHeight * 0.5f);
                var rotation = Quaternion.LookRotation(forward, Vector3.up);
                var curbMaterial = (i % 2 == 0) ? curbRedMaterial : curbWhiteMaterial;

                var leftCurb = CreateBlock($"Curb_L_{i:000}", curbsRoot.transform, center - right * sideOffset, new Vector3(curbWidth, curbHeight, length + 0.04f));
                leftCurb.transform.rotation = rotation;
                DisableCollider(leftCurb);
                ApplyMaterial(leftCurb, curbMaterial);

                var rightCurb = CreateBlock($"Curb_R_{i:000}", curbsRoot.transform, center + right * sideOffset, new Vector3(curbWidth, curbHeight, length + 0.04f));
                rightCurb.transform.rotation = rotation;
                DisableCollider(rightCurb);
                ApplyMaterial(rightCurb, curbMaterial);
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

            var points = roadComponent.GetEvenlySpacedPoints(0.75f, 1f);
            var sideOffset = roadWidth * 0.5f + shoulderWidth + curbWidth + boundaryThickness * 0.5f;
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
                CreateBoundaryBox(boundariesRoot.transform, $"Boundary_L_{i:000}", center - right * sideOffset, rotation, length + 0.03f);
                CreateBoundaryBox(boundariesRoot.transform, $"Boundary_R_{i:000}", center + right * sideOffset, rotation, length + 0.03f);
            }
        }

        private void CreateStartAndFinish(Road roadComponent)
        {
            if (roadComponent == null)
            {
                return;
            }

            var startFinishRoot = new GameObject("StartFinish");
            startFinishRoot.transform.SetParent(transform, false);

            var points = roadComponent.GetEvenlySpacedPoints(1.0f, 1f);
            if (points.Length < 8)
            {
                return;
            }

            var start = points[2];
            var finish = points[points.Length - 4];

            CreateCheckeredStrip(startFinishRoot.transform, "StartStrip", start.position, start.forward, roadWidth * 0.92f);
            CreateFinishArch(startFinishRoot.transform, finish.position, finish.forward);
        }

        private void CreateCheckeredStrip(Transform parent, string name, Vector3 position, Vector3 forward, float width)
        {
            var strip = new GameObject(name);
            strip.transform.SetParent(parent, false);
            strip.transform.position = position + Vector3.up * 0.03f;
            strip.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            var tiles = 10;
            var tileWidth = width / tiles;
            for (var i = 0; i < tiles; i++)
            {
                var tile = CreateBlock($"Tile_{i:00}", strip.transform, new Vector3((-width * 0.5f) + tileWidth * (i + 0.5f), 0f, 0f), new Vector3(tileWidth, 0.01f, 0.46f));
                DisableCollider(tile);
                ApplyColor(tile, (i % 2 == 0) ? Color.white : Color.black, 0.1f);
            }
        }

        private void CreateFinishArch(Transform parent, Vector3 position, Vector3 forward)
        {
            var arch = new GameObject("FinishArch");
            arch.transform.SetParent(parent, false);
            arch.transform.position = position + Vector3.up * 0.02f;
            arch.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            var pillarOffset = roadWidth * 0.62f;
            var pillarHeight = 2.0f;

            var pillarLeft = CreateBlock("PillarL", arch.transform, new Vector3(-pillarOffset, pillarHeight * 0.5f, 0f), new Vector3(0.22f, pillarHeight, 0.22f));
            var pillarRight = CreateBlock("PillarR", arch.transform, new Vector3(pillarOffset, pillarHeight * 0.5f, 0f), new Vector3(0.22f, pillarHeight, 0.22f));
            var top = CreateBlock("Beam", arch.transform, new Vector3(0f, pillarHeight + 0.12f, 0f), new Vector3(roadWidth + 1.2f, 0.24f, 0.24f));

            ApplyColor(pillarLeft, new Color(0.12f, 0.12f, 0.14f), 0.20f);
            ApplyColor(pillarRight, new Color(0.12f, 0.12f, 0.14f), 0.20f);
            ApplyColor(top, new Color(0.18f, 0.18f, 0.20f), 0.24f);
        }

        private void CreateLighting()
        {
            var lightsRoot = new GameObject("Lighting");
            lightsRoot.transform.SetParent(transform, false);

            CreateLamp(lightsRoot.transform, "LampA", new Vector3(-14f, 0f, -11f));
            CreateLamp(lightsRoot.transform, "LampB", new Vector3(-13f, 0f, 1f));
            CreateLamp(lightsRoot.transform, "LampC", new Vector3(11f, 0f, 8f));
            CreateLamp(lightsRoot.transform, "LampD", new Vector3(14f, 0f, -5f));
        }

        private void CreateDecorativeProps()
        {
            var propsRoot = new GameObject("DecorativeProps");
            propsRoot.transform.SetParent(transform, false);

            CreateTree(propsRoot.transform, "TreeA", new Vector3(-17f, 0f, -15f), 1.1f);
            CreateTree(propsRoot.transform, "TreeB", new Vector3(-18f, 0f, -3f), 0.95f);
            CreateTree(propsRoot.transform, "TreeC", new Vector3(17f, 0f, 7f), 1.15f);
            CreateTree(propsRoot.transform, "TreeD", new Vector3(18f, 0f, -10f), 1.0f);

            CreateCone(propsRoot.transform, "ConeA", new Vector3(-11f, 0f, -12f));
            CreateCone(propsRoot.transform, "ConeB", new Vector3(-9.5f, 0f, -12f));
            CreateCone(propsRoot.transform, "ConeC", new Vector3(9f, 0f, 4f));
            CreateCone(propsRoot.transform, "ConeD", new Vector3(10.5f, 0f, 4f));
        }

        private void CreateArcadeBackdropMeshes()
        {
#if UNITY_EDITOR
            var roadPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArcadeRoadMeshPath);
            if (roadPrefab == null)
            {
                return;
            }

            var backdropRoot = new GameObject("ArcadeBackdrop");
            backdropRoot.transform.SetParent(transform, false);

            CreateArcadeRoadCopy(backdropRoot.transform, roadPrefab, "BackdropRoadA", new Vector3(-23f, 0f, -18f), new Vector3(8f, 1f, 8f), Quaternion.Euler(0f, 0f, 0f));
            CreateArcadeRoadCopy(backdropRoot.transform, roadPrefab, "BackdropRoadB", new Vector3(24f, 0f, 16f), new Vector3(8f, 1f, 8f), Quaternion.Euler(0f, 180f, 0f));
            CreateArcadeRoadCopy(backdropRoot.transform, roadPrefab, "BackdropRoadC", new Vector3(0f, 0f, 25f), new Vector3(10f, 1f, 10f), Quaternion.Euler(0f, 90f, 0f));
#endif
        }

#if UNITY_EDITOR
        private void CreateArcadeRoadCopy(Transform parent, GameObject prefab, string name, Vector3 localPosition, Vector3 localScale, Quaternion localRotation)
        {
            var instance = Object.Instantiate(prefab, parent, false);
            instance.name = name;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = localScale;

            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            {
                DisableCollider(collider.gameObject);
            }

            var materialOverride = TryCloneAssetMaterial(ArcadeEnvironmentMaterialPath);
            if (materialOverride == null)
            {
                return;
            }

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = materialOverride;
            }
        }
#endif

        private void CreateShoulderBox(Transform parent, string name, Vector3 worldCenter, Quaternion worldRotation, float length)
        {
            var shoulder = CreateBlock(name, parent, parent.InverseTransformPoint(worldCenter), new Vector3(shoulderWidth, shoulderHeight, length));
            shoulder.transform.rotation = worldRotation;
            DisableCollider(shoulder);
            ApplyMaterial(shoulder, shoulderMaterial);
        }

        private void CreateBoundaryBox(Transform parent, string name, Vector3 worldCenter, Quaternion worldRotation, float length)
        {
            var boundary = CreateBlock(name, parent, parent.InverseTransformPoint(worldCenter), new Vector3(boundaryThickness, boundaryHeight, length));
            boundary.transform.rotation = worldRotation;
            ApplyMaterial(boundary, boundaryMaterial);
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
            trunk.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            trunk.transform.localScale = new Vector3(0.14f, 0.55f, 0.14f);
            DisableCollider(trunk);
            ApplyColor(trunk, new Color(0.37f, 0.24f, 0.12f), 0.10f);

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "Crown";
            crown.transform.SetParent(tree.transform, false);
            crown.transform.localPosition = new Vector3(0f, 1.30f, 0f);
            crown.transform.localScale = new Vector3(0.95f, 1.05f, 0.95f);
            DisableCollider(crown);
            ApplyColor(crown, new Color(0.19f, 0.42f, 0.18f), 0.10f);
        }

        private static void CreateCone(Transform parent, string name, Vector3 localPosition)
        {
            var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cone.name = name;
            cone.transform.SetParent(parent, false);
            cone.transform.localScale = new Vector3(0.11f, 0.17f, 0.11f);
            cone.transform.localPosition = new Vector3(localPosition.x, 0.17f, localPosition.z);
            DisableCollider(cone);
            ApplyColor(cone, new Color(0.94f, 0.42f, 0.10f), 0.14f);
        }

        private static void CreateLamp(Transform parent, string name, Vector3 localPosition)
        {
            var lamp = new GameObject(name);
            lamp.transform.SetParent(parent, false);
            lamp.transform.localPosition = localPosition;

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(lamp.transform, false);
            pole.transform.localScale = new Vector3(0.06f, 1.45f, 0.06f);
            pole.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            DisableCollider(pole);
            ApplyColor(pole, new Color(0.66f, 0.69f, 0.73f), 0.18f);

            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb";
            bulb.transform.SetParent(lamp.transform, false);
            bulb.transform.localScale = Vector3.one * 0.22f;
            bulb.transform.localPosition = new Vector3(0f, 2.95f, 0f);
            DisableCollider(bulb);
            ApplyColor(bulb, new Color(0.95f, 0.92f, 0.62f), 0.75f);
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

        private void TryApplyDaySkybox()
        {
#if UNITY_EDITOR
            var skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArcadeDaySkyboxPath);
            if (skyboxMaterial != null)
            {
                RenderSettings.skybox = skyboxMaterial;
            }
#endif
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
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                2, 3, 7, 2, 7, 6,
                1, 2, 6, 1, 6, 5,
                0, 4, 7, 0, 7, 3,
            };

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateLitMaterial(Color color, float smoothness)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            material.SetFloat("_Smoothness", smoothness);
            return material;
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

        private static void ApplyMaterial(GameObject gameObject, Material material)
        {
            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null || material == null)
            {
                return;
            }

            renderer.sharedMaterial = material;
        }

        private static void ApplyColor(GameObject gameObject, Color color, float smoothness)
        {
            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.sharedMaterial = CreateLitMaterial(color, smoothness);
        }

        private static Material TryCloneAssetMaterial(string path)
        {
#if UNITY_EDITOR
            var source = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (source != null)
            {
                return new Material(source);
            }
#endif
            return null;
        }
    }
}
#else
using UnityEngine;

namespace UavSimulator.Tracks
{
    public sealed class RoadSystemRealisticTrack : TrackBase
    {
        private void Awake()
        {
            Debug.LogWarning("RoadSystem package is unavailable in this assembly context. Using empty RoadSystemRealisticTrack fallback.");
        }

        public override void ResetTrack(int seed)
        {
            _ = seed;
        }
    }
}
#endif
