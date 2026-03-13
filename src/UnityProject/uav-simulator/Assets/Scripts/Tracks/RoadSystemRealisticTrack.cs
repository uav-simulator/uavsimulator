using Barmetler.RoadSystem;
using Barmetler.RoadSystem.Util;
using UnityEngine;
using UnityEngine.Rendering;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UavSimulator.Tracks
{
    public sealed class RoadSystemRealisticTrack : TrackBase
    {
        private const string ArcadeRoadMeshPath = "Assets/ARCADE - FREE Racing Car/Meshes/Road.fbx";
        private const string ArcadeDaySkyboxPath = "Assets/ARCADE - FREE Racing Car/Skybox/Day/Day Skybox.mat";
        private const string PrometeoParkingMaterialPath = "Assets/PROMETEO - Car Controller/Materials/PCC_ParkingZone_Mat.mat";
        private const string ArcadeBlueCarPrefabPath = "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car Blue Variant.prefab";
        private const string ArcadeRedCarPrefabPath = "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car Red Variant.prefab";
        private const string ArcadeGrayCarPrefabPath = "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car Gray Variant.prefab";

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
        private Material serviceAreaMaterial;
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
            SanitizeTrackMaterials();

            built = true;
        }

        private void BuildMaterials()
        {
            roadMaterial = CreateLitMaterial(new Color(0.10f, 0.10f, 0.11f), 0.30f);
            groundMaterial = CreateLitMaterial(new Color(0.23f, 0.31f, 0.22f), 0.07f);
            serviceAreaMaterial = TryCloneAssetMaterial(PrometeoParkingMaterialPath) ?? CreateLitMaterial(new Color(0.28f, 0.28f, 0.30f), 0.16f);
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
            var paddock = CreateBlock("Paddock", transform, new Vector3(-18f, -0.12f, -15f), new Vector3(16f, 0.04f, 10f));
            var serviceZone = CreateBlock("ServiceZone", transform, new Vector3(17.5f, -0.12f, 12f), new Vector3(14f, 0.04f, 11f));
            var spectatorApron = CreateBlock("SpectatorApron", transform, new Vector3(0f, -0.12f, 19f), new Vector3(30f, 0.04f, 9f));

            ApplyMaterial(landscape, groundMaterial);
            ApplyMaterial(ground, groundMaterial);
            ApplyColor(infield, new Color(0.28f, 0.35f, 0.25f), 0.08f);
            ApplyMaterial(paddock, serviceAreaMaterial);
            ApplyMaterial(serviceZone, serviceAreaMaterial);
            ApplyMaterial(spectatorApron, serviceAreaMaterial);
            CreatePaintStripe("PaddockLineA", transform, new Vector3(-18f, -0.095f, -12.5f), new Vector3(14.5f, 0.01f, 0.18f), 0f);
            CreatePaintStripe("PaddockLineB", transform, new Vector3(-18f, -0.095f, -17.3f), new Vector3(14.5f, 0.01f, 0.18f), 0f);
            CreatePaintStripe("ServiceLineA", transform, new Vector3(17.5f, -0.095f, 7.4f), new Vector3(12.0f, 0.01f, 0.18f), 0f);
            CreatePaintStripe("ServiceLineB", transform, new Vector3(17.5f, -0.095f, 16.2f), new Vector3(12.0f, 0.01f, 0.18f), 0f);
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

            var sun = new GameObject("SunLight");
            sun.transform.SetParent(lightsRoot.transform, false);
            sun.transform.rotation = Quaternion.Euler(38f, -34f, 0f);
            var sunLight = sun.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.15f;
            sunLight.color = new Color(1.0f, 0.97f, 0.92f);
            sunLight.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.67f, 0.77f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.40f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.25f, 0.24f);

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
            CreateTree(propsRoot.transform, "TreeE", new Vector3(-3f, 0f, 21f), 1.05f);
            CreateTree(propsRoot.transform, "TreeF", new Vector3(7f, 0f, 22f), 0.92f);
            CreateTree(propsRoot.transform, "TreeG", new Vector3(22f, 0f, 14f), 1.08f);
            CreateTree(propsRoot.transform, "TreeH", new Vector3(-23f, 0f, 8f), 0.98f);

            CreateCone(propsRoot.transform, "ConeA", new Vector3(-13.0f, 0f, -12.3f));
            CreateCone(propsRoot.transform, "ConeB", new Vector3(-8.9f, 0f, -12.3f));
            CreateCone(propsRoot.transform, "ConeC", new Vector3(9f, 0f, 4f));
            CreateCone(propsRoot.transform, "ConeD", new Vector3(10.5f, 0f, 4f));
            CreateCone(propsRoot.transform, "ConeE", new Vector3(-14.3f, 0f, -13.2f));
            CreateCone(propsRoot.transform, "ConeF", new Vector3(-14.3f, 0f, -11.2f));
            CreateCone(propsRoot.transform, "ConeG", new Vector3(6.5f, 0f, 7.4f));
            CreateCone(propsRoot.transform, "ConeH", new Vector3(8f, 0f, 6.6f));

            CreateGrandstand(propsRoot.transform, "GrandstandNorth", new Vector3(0f, 0f, 23f), 10, 4, 0f);
            CreateGrandstand(propsRoot.transform, "GrandstandWest", new Vector3(-23f, 0f, -1f), 8, 3, 90f);
            CreateBillboard(propsRoot.transform, "BillboardStart", new Vector3(-18f, 0f, -8f), 30f, "RUSIM DEMO");
            CreateBillboard(propsRoot.transform, "BillboardEast", new Vector3(18f, 0f, 10f), -55f, "SIM TRACK");
            CreateMarshalPost(propsRoot.transform, "MarshalPostA", new Vector3(-21f, 0f, -14f), 12f);
            CreateMarshalPost(propsRoot.transform, "MarshalPostB", new Vector3(16f, 0f, 4f), -90f);
            CreateSponsorPanel(propsRoot.transform, "SponsorPanelStartRight", new Vector3(13.8f, 0f, -0.6f), -92f, new Color(0.80f, 0.18f, 0.16f));
            CreateSponsorPanel(propsRoot.transform, "SponsorPanelStartLeft", new Vector3(-15.8f, 0f, -8.8f), 86f, new Color(0.12f, 0.23f, 0.62f));
            CreateDecorativeCars();
        }

        private void CreateArcadeBackdropMeshes()
        {
#if UNITY_EDITOR
            var backdropRoot = new GameObject("ArcadeBackdrop");
            backdropRoot.transform.SetParent(transform, false);

            var roadPrefab = IsUrpActive() ? AssetDatabase.LoadAssetAtPath<GameObject>(ArcadeRoadMeshPath) : null;
            if (roadPrefab != null)
            {
                CreateArcadeRoadCopy(backdropRoot.transform, roadPrefab, "BackdropRoadA", new Vector3(-23f, 0f, -18f), new Vector3(8f, 1f, 8f), Quaternion.Euler(0f, 0f, 0f));
                CreateArcadeRoadCopy(backdropRoot.transform, roadPrefab, "BackdropRoadB", new Vector3(24f, 0f, 16f), new Vector3(8f, 1f, 8f), Quaternion.Euler(0f, 180f, 0f));
                CreateArcadeRoadCopy(backdropRoot.transform, roadPrefab, "BackdropRoadC", new Vector3(0f, 0f, 25f), new Vector3(10f, 1f, 10f), Quaternion.Euler(0f, 90f, 0f));
            }

            CreateBackdropBlock(backdropRoot.transform, "BackdropMoundWest", new Vector3(-28f, 0.55f, -3f), new Vector3(10f, 1.1f, 18f), new Color(0.35f, 0.37f, 0.40f));
            CreateBackdropBlock(backdropRoot.transform, "BackdropMoundEast", new Vector3(28f, 0.55f, 8f), new Vector3(10f, 1.1f, 16f), new Color(0.35f, 0.37f, 0.40f));
            CreateBackdropBlock(backdropRoot.transform, "BackdropMoundNorth", new Vector3(0f, 0.50f, 29f), new Vector3(28f, 1.0f, 8f), new Color(0.34f, 0.36f, 0.39f));
#endif
        }

#if UNITY_EDITOR
        private void CreateDecorativeCarCopy(string assetPath, string name, Vector3 localPosition, Quaternion localRotation, float scale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                return;
            }

            var instance = UnityEngine.Object.Instantiate(prefab, transform, false);
            instance.name = name;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = Vector3.one * scale;

            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            {
                DisableCollider(collider.gameObject);
            }

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                var safeMaterial = CreateLitMaterial(ReadSourceColor(renderer.sharedMaterial), 0.16f);
                var shared = renderer.sharedMaterials;
                for (var i = 0; i < shared.Length; i++)
                {
                    shared[i] = safeMaterial;
                }

                renderer.sharedMaterials = shared;
            }
        }

        private void CreateArcadeRoadCopy(Transform parent, GameObject prefab, string name, Vector3 localPosition, Vector3 localScale, Quaternion localRotation)
        {
            var instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = name;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = localScale;

            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            {
                DisableCollider(collider.gameObject);
            }

            var materialOverride = CreateLitMaterial(new Color(0.30f, 0.31f, 0.34f), 0.14f);

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null)
                {
                    var slots = renderer.sharedMaterials;
                    for (var i = 0; i < slots.Length; i++)
                    {
                        slots[i] = materialOverride;
                    }

                    renderer.sharedMaterials = slots;
                }
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

        private void CreatePaintStripe(string name, Transform parent, Vector3 localPosition, Vector3 localScale, float yawDeg)
        {
            var stripe = CreateBlock(name, parent, localPosition, localScale);
            stripe.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            DisableCollider(stripe);
            ApplyMaterial(stripe, laneMaterial);
        }

        private static void CreateGrandstand(Transform parent, string name, Vector3 localPosition, int seatsPerRow, int rowCount, float yawDeg)
        {
            var stand = new GameObject(name);
            stand.transform.SetParent(parent, false);
            stand.transform.localPosition = localPosition;
            stand.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

            var baseBlock = CreateBlock("Base", stand.transform, new Vector3(0f, 0.25f, 0f), new Vector3(seatsPerRow * 0.55f, 0.5f, 2.2f));
            DisableCollider(baseBlock);
            ApplyColor(baseBlock, new Color(0.25f, 0.27f, 0.30f), 0.18f);

            for (var row = 0; row < rowCount; row++)
            {
                var seatRow = CreateBlock(
                    $"Row_{row:00}",
                    stand.transform,
                    new Vector3(0f, 0.55f + row * 0.22f, -0.55f + row * 0.32f),
                    new Vector3(seatsPerRow * 0.48f, 0.18f, 0.28f));
                DisableCollider(seatRow);
                ApplyColor(seatRow, (row % 2 == 0) ? new Color(0.75f, 0.18f, 0.16f) : new Color(0.87f, 0.87f, 0.90f), 0.14f);
            }
        }

        private static void CreateBillboard(Transform parent, string name, Vector3 localPosition, float yawDeg, string caption)
        {
            var billboard = new GameObject(name);
            billboard.transform.SetParent(parent, false);
            billboard.transform.localPosition = localPosition;
            billboard.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

            var postsOffset = 1.85f;
            var postLeft = CreateBlock("PostL", billboard.transform, new Vector3(-postsOffset, 1.4f, 0f), new Vector3(0.14f, 2.8f, 0.14f));
            var postRight = CreateBlock("PostR", billboard.transform, new Vector3(postsOffset, 1.4f, 0f), new Vector3(0.14f, 2.8f, 0.14f));
            var board = CreateBlock("Board", billboard.transform, new Vector3(0f, 2.55f, 0f), new Vector3(4.4f, 1.5f, 0.14f));
            DisableCollider(postLeft);
            DisableCollider(postRight);
            DisableCollider(board);
            ApplyColor(postLeft, new Color(0.42f, 0.43f, 0.46f), 0.15f);
            ApplyColor(postRight, new Color(0.42f, 0.43f, 0.46f), 0.15f);
            ApplyColor(board, new Color(0.10f, 0.12f, 0.18f), 0.18f);

            var plateCount = Mathf.Max(3, caption.Length / 4);
            for (var i = 0; i < plateCount; i++)
            {
                var plate = CreateBlock(
                    $"CaptionPlate_{i:00}",
                    billboard.transform,
                    new Vector3(-1.45f + i * 0.55f, 2.55f, -0.08f),
                    new Vector3(0.34f, 0.18f, 0.02f));
                DisableCollider(plate);
                ApplyColor(plate, new Color(0.88f, 0.90f, 0.94f), 0.08f);
            }
        }

        private static void CreateMarshalPost(Transform parent, string name, Vector3 localPosition, float yawDeg)
        {
            var post = new GameObject(name);
            post.transform.SetParent(parent, false);
            post.transform.localPosition = localPosition;
            post.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

            var baseBlock = CreateBlock("Base", post.transform, new Vector3(0f, 0.12f, 0f), new Vector3(1.4f, 0.24f, 1.2f));
            var cabin = CreateBlock("Cabin", post.transform, new Vector3(0f, 0.72f, 0f), new Vector3(1.2f, 0.85f, 1.0f));
            var roof = CreateBlock("Roof", post.transform, new Vector3(0f, 1.22f, 0f), new Vector3(1.5f, 0.12f, 1.3f));
            DisableCollider(baseBlock);
            DisableCollider(cabin);
            DisableCollider(roof);
            ApplyColor(baseBlock, new Color(0.18f, 0.19f, 0.20f), 0.16f);
            ApplyColor(cabin, new Color(0.75f, 0.20f, 0.18f), 0.16f);
            ApplyColor(roof, new Color(0.88f, 0.88f, 0.90f), 0.08f);
        }

        private static void CreateSponsorPanel(Transform parent, string name, Vector3 localPosition, float yawDeg, Color accentColor)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = localPosition;
            panel.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

            var back = CreateBlock("Back", panel.transform, new Vector3(0f, 1.05f, 0f), new Vector3(2.8f, 1.2f, 0.12f));
            var accent = CreateBlock("Accent", panel.transform, new Vector3(0f, 1.05f, -0.05f), new Vector3(2.25f, 0.25f, 0.02f));
            DisableCollider(back);
            DisableCollider(accent);
            ApplyColor(back, new Color(0.90f, 0.90f, 0.92f), 0.08f);
            ApplyColor(accent, accentColor, 0.12f);
        }

        private static void CreateBackdropBlock(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color color)
        {
            var block = CreateBlock(name, parent, localPosition, localScale);
            DisableCollider(block);
            ApplyColor(block, color, 0.08f);
        }

        private void CreateDecorativeCars()
        {
#if UNITY_EDITOR
            CreateDecorativeCarCopy(ArcadeBlueCarPrefabPath, "StartSectorCarBlue", new Vector3(-16.8f, 0f, -10.8f), Quaternion.Euler(0f, 22f, 0f), 1.05f);
            CreateDecorativeCarCopy(ArcadeBlueCarPrefabPath, "PaddockCarBlue", new Vector3(-20.5f, 0f, -15.8f), Quaternion.Euler(0f, 18f, 0f), 1.05f);
            CreateDecorativeCarCopy(ArcadeRedCarPrefabPath, "PaddockCarRed", new Vector3(-15.4f, 0f, -15.6f), Quaternion.Euler(0f, -6f, 0f), 1.05f);
            CreateDecorativeCarCopy(ArcadeGrayCarPrefabPath, "ServiceCarGray", new Vector3(16.8f, 0f, 11.6f), Quaternion.Euler(0f, -102f, 0f), 1.05f);
#endif
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
            if (skyboxMaterial != null && skyboxMaterial.shader != null && skyboxMaterial.shader.isSupported)
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
            var uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
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
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateLitMaterial(Color color, float smoothness)
        {
            var shader = ResolveRuntimeLitShader();
            var material = new Material(shader);
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
                var fallback = CreateLitMaterial(ReadSourceColor(source), 0.16f);
                var sourceTexture = ReadSourceTexture(source);
                if (sourceTexture != null)
                {
                    if (fallback.HasProperty("_MainTex"))
                    {
                        fallback.SetTexture("_MainTex", sourceTexture);
                    }

                    if (fallback.HasProperty("_BaseMap"))
                    {
                        fallback.SetTexture("_BaseMap", sourceTexture);
                    }
                }

                return fallback;
            }
#endif
            return null;
        }

        private static Shader ResolveRuntimeLitShader()
        {
            if (IsUrpActive())
            {
                var urpShader = Shader.Find("Universal Render Pipeline/Lit");
                if (urpShader != null && urpShader.isSupported)
                {
                    return urpShader;
                }
            }

            var shader = Shader.Find("Standard");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("Unlit/Texture");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("Unlit/Color");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader != null)
            {
                return shader;
            }

            throw new MissingReferenceException("Unable to resolve a supported lit shader for runtime track materials.");
        }

        private static bool IsUrpActive()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
            {
                return false;
            }

            var name = pipeline.GetType().Name;
            return name.Contains("UniversalRenderPipeline", StringComparison.Ordinal) ||
                   name.Contains("URP", StringComparison.Ordinal);
        }

        private static bool IsUrpShader(Shader shader)
        {
            var name = shader != null ? shader.name : string.Empty;
            return name.StartsWith("Universal Render Pipeline/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBuiltinCompatibleShader(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            var name = shader.name ?? string.Empty;
            if (name.StartsWith("Standard", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Legacy Shaders/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Unlit/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Mobile/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Particles/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private void SanitizeTrackMaterials()
        {
            var fallbackShader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            if (fallbackShader == null)
            {
                return;
            }

            var replacements = new System.Collections.Generic.Dictionary<Material, Material>();
            var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var shared = renderer.sharedMaterials;
                var changed = false;
                for (var m = 0; m < shared.Length; m++)
                {
                    var source = shared[m];
                    if (source == null)
                    {
                        continue;
                    }

                    var shader = source.shader;
                    var unsupported = shader == null || !shader.isSupported;
                    var builtinIncompatible = !IsUrpActive() && !IsBuiltinCompatibleShader(shader);
                    if (!unsupported && !builtinIncompatible)
                    {
                        continue;
                    }

                    if (!replacements.TryGetValue(source, out var replacement))
                    {
                        replacement = new Material(fallbackShader)
                        {
                            color = ReadSourceColor(source),
                        };

                        var sourceTexture = ReadSourceTexture(source);
                        if (sourceTexture != null)
                        {
                            if (replacement.HasProperty("_MainTex"))
                            {
                                replacement.SetTexture("_MainTex", sourceTexture);
                            }

                            if (replacement.HasProperty("_BaseMap"))
                            {
                                replacement.SetTexture("_BaseMap", sourceTexture);
                            }
                        }

                        if (replacement.HasProperty("_Smoothness"))
                        {
                            replacement.SetFloat("_Smoothness", 0.16f);
                        }

                        replacements[source] = replacement;
                    }

                    shared[m] = replacement;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = shared;
                }
            }
        }

        private static Texture ReadSourceTexture(Material source)
        {
            if (source == null)
            {
                return null;
            }

            if (source.mainTexture != null)
            {
                return source.mainTexture;
            }

            if (source.HasProperty("_BaseMap"))
            {
                return source.GetTexture("_BaseMap");
            }

            if (source.HasProperty("_MainTex"))
            {
                return source.GetTexture("_MainTex");
            }

            return null;
        }

        private static Color ReadSourceColor(Material source)
        {
            if (source == null)
            {
                return Color.white;
            }

            if (source.HasProperty("_BaseColor"))
            {
                return source.GetColor("_BaseColor");
            }

            if (source.HasProperty("_Color"))
            {
                return source.GetColor("_Color");
            }

            return source.color;
        }
    }
}
