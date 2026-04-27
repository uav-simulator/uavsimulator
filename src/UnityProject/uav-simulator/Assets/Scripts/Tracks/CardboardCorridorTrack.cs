using UavSimulator.Core;
using UnityEngine;

namespace UavSimulator.Tracks
{
    /// <summary>
    /// Procedural L-shaped cardboard corridor for sim-to-real training.
    ///
    /// Layout (top-down, XZ plane, origin at turn junction):
    ///   Segment A: x=0, z=−segA..z=0  (straight, +Z, 1.10m)
    ///   Turn:      90° right at origin
    ///   Segment B: z=0, x=0..x=segB   (straight, +X, 0.90m)
    ///
    /// Corridor width = 0.60m, wall height = 0.25m.
    /// Robot spawns at (0, 0.01, −0.85) facing +Z.
    ///
    /// 6 walls form a closed L-shape. All walls have active colliders.
    ///
    /// rev20: domain randomization — wall/floor color jitter (HSV), light
    /// intensity/hue jitter, optional skybox-null with random ambient.
    /// Each ResetTrack(seed) destroys existing children and rebuilds with new
    /// visuals so each episode trains the policy on a different scene.
    /// </summary>
    public sealed class CardboardCorridorTrack : TrackBase
    {
        [SerializeField] private float corridorWidth = 0.60f;
        [SerializeField] private float wallHeight = 0.25f;
        [SerializeField] private float wallThickness = 0.02f;
        [SerializeField] private float segmentALength = 1.10f;
        [SerializeField] private float segmentBLength = 0.90f;

        // rev20 domain randomization knobs
        [SerializeField] private bool randomizeVisuals = true;
        // rev21 softer DR: halved ranges from rev20 since aggressive DR
        // produced 0% SR after 1.2M steps (mode collapse). Goal: keep mostly
        // the rev16-like bright-tan look with gentle perturbation so transfer
        // learning from rev16 baseline can preserve navigation while gaining
        // robustness to slight scene variance.
        [SerializeField] private float wallHueJitterDegrees = 12f;
        [SerializeField] private float wallValueJitterRange = 0.07f;
        [SerializeField] private float wallSaturationJitterRange = 0.07f;
        [SerializeField] private float lightIntensityMin = 0.75f;
        [SerializeField] private float lightIntensityMax = 1.10f;
        [SerializeField] private float lightHueJitterDegrees = 12f;
        [SerializeField] private float skyboxNullProbability = 0.0f;
        [SerializeField] private float cameraPitchJitterDegrees = 4f;

        // Base palette — randomization perturbs around these.
        private static readonly Color CardboardBase = new Color(0.76f, 0.60f, 0.42f);
        private static readonly Color CardboardStripe = new Color(0.68f, 0.52f, 0.36f);
        private static readonly Color FloorColor = new Color(0.72f, 0.58f, 0.40f);
        private static readonly Color SurroundFloorColor = new Color(0.75f, 0.75f, 0.75f);
        private static readonly Color MarkerWhite = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color MarkerBlack = new Color(0.05f, 0.05f, 0.05f);

        private bool built;

        // rev20 randomization state
        private System.Random rng;
        private Color randomizedCardboardBase;
        private Color randomizedCardboardStripe;
        private Color randomizedFloorColor;
        private Color randomizedSurroundFloorColor;

        private void Awake()
        {
            // Awake fires before any seeded reset; use a deterministic default
            // seed so first frame is valid even if ResetTrack hasn't run yet.
            if (rng == null) rng = new System.Random(0);
            if (randomizeVisuals) JitterPalette();
            BuildIfNeeded();
        }

        public override void ResetTrack(int seed)
        {
            rng = new System.Random(seed);
            if (randomizeVisuals)
            {
                // DestroyImmediate so the children are gone *this* frame; otherwise
                // BuildIfNeeded sees the still-alive Destroy()-pending children, marks
                // built=true and returns without rebuilding (silent DR failure).
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    var child = transform.GetChild(i).gameObject;
                    UnityEngine.Object.DestroyImmediate(child);
                }
                JitterPalette();
                built = false;
                BuildTrack();
                built = true;
                return;
            }
            BuildIfNeeded();
        }

        private void JitterPalette()
        {
            randomizedCardboardBase = JitterColor(CardboardBase, wallHueJitterDegrees, wallSaturationJitterRange, wallValueJitterRange);
            randomizedCardboardStripe = JitterColor(CardboardStripe, wallHueJitterDegrees, wallSaturationJitterRange, wallValueJitterRange);
            randomizedFloorColor = JitterColor(FloorColor, wallHueJitterDegrees, wallSaturationJitterRange, wallValueJitterRange);
            randomizedSurroundFloorColor = JitterColor(SurroundFloorColor, wallHueJitterDegrees, wallSaturationJitterRange, wallValueJitterRange);
        }

        private Color JitterColor(Color baseColor, float hueDeg, float satRange, float valRange)
        {
            Color.RGBToHSV(baseColor, out var h, out var s, out var v);
            h = (h + (float)(rng.NextDouble() - 0.5) * hueDeg / 360f + 1f) % 1f;
            s = Mathf.Clamp01(s + (float)(rng.NextDouble() - 0.5) * 2f * satRange);
            v = Mathf.Clamp01(v + (float)(rng.NextDouble() - 0.5) * 2f * valRange);
            return Color.HSVToRGB(h, s, v);
        }

        private Color WallColor() => randomizeVisuals ? randomizedCardboardBase : CardboardBase;
        private Color StripeColor() => randomizeVisuals ? randomizedCardboardStripe : CardboardStripe;
        private Color FloorColorActive() => randomizeVisuals ? randomizedFloorColor : FloorColor;
        private Color SurroundFloorColorActive() => randomizeVisuals ? randomizedSurroundFloorColor : SurroundFloorColor;

        private void BuildIfNeeded()
        {
            if (built) return;
            if (transform.childCount > 0) { built = true; return; }
            BuildTrack();
            built = true;
        }

        private void BuildTrack()
        {
            CreateSurroundingFloor();
            CreateCorridorFloor();
            CreateWalls();
            CreateFinishMarker();
            CreateLighting();
        }

        // ── Surrounding floor ───────────────────────────────────────

        private void CreateSurroundingFloor()
        {
            var floor = CreateBox("SurroundFloor",
                new Vector3(2.5f, 0.01f, 2.5f),
                new Vector3(0.30f, -0.005f, -0.40f));
            SetMaterial(floor, SurroundFloorColorActive(), 0.3f);
        }

        // ── Corridor floor ──────────────────────────────────────────

        private void CreateCorridorFloor()
        {
            float hw = corridorWidth * 0.5f;
            float floorY = 0.005f;

            // Segment A floor: from z=−segA to z=+hw (extends into turn)
            float aLen = segmentALength + hw;
            var floorA = CreateBox("CorridorFloorA",
                new Vector3(corridorWidth, 0.01f, aLen),
                new Vector3(0f, floorY, -segmentALength * 0.5f + hw * 0.5f));
            SetMaterial(floorA, FloorColorActive(), 0.2f);

            // Segment B floor: from x=+hw to x=+segB (no overlap with turn)
            float bLen = segmentBLength - hw;
            if (bLen > 0.01f)
            {
                var floorB = CreateBox("CorridorFloorB",
                    new Vector3(bLen, 0.01f, corridorWidth),
                    new Vector3(hw + bLen * 0.5f, floorY, 0f));
                SetMaterial(floorB, FloorColorActive(), 0.2f);
            }
        }

        // ── Walls (6 segments, closed L-shape) ──────────────────────

        private void CreateWalls()
        {
            float hw = corridorWidth * 0.5f;
            float wallY = wallHeight * 0.5f;

            // 1. WallA_Left: x=−hw, z from −segA to +hw (outer left, full L height = 1.40m)
            float wallALeftLen = segmentALength + hw;
            CreateCardboardWall("WallA_Left",
                new Vector3(wallThickness, wallHeight, wallALeftLen),
                new Vector3(-hw, wallY, -segmentALength * 0.5f + hw * 0.5f));

            // 2. WallA_Right: x=+hw, z from −segA to −hw (inner wall = 0.80m)
            // Center z = -segA + len/2 = -1.10 + 0.40 = -0.70
            float wallARightLen = segmentALength - hw;
            CreateCardboardWall("WallA_Right",
                new Vector3(wallThickness, wallHeight, wallARightLen),
                new Vector3(hw, wallY, -segmentALength + wallARightLen * 0.5f));

            // 3. WallB_Top: z=+hw, x from −hw to +segB (outer top = 1.20m)
            float wallBTopLen = hw + segmentBLength;
            CreateCardboardWall("WallB_Top",
                new Vector3(wallBTopLen, wallHeight, wallThickness),
                new Vector3(-hw + wallBTopLen * 0.5f, wallY, hw));

            // 4. WallB_Inner: z=−hw, x from +hw to +segB (inner wall = 0.60m)
            float wallBInnerLen = segmentBLength - hw;
            CreateCardboardWall("WallB_Inner",
                new Vector3(wallBInnerLen, wallHeight, wallThickness),
                new Vector3(hw + wallBInnerLen * 0.5f, wallY, -hw));

            // 5. WallStart: z=−segA, x from −hw to +hw (closes back)
            CreateCardboardWall("WallStart",
                new Vector3(corridorWidth, wallHeight, wallThickness),
                new Vector3(0f, wallY, -segmentALength));

            // 6. WallEnd: x=+segB, z from −hw to +hw (closes end, ArUco marker)
            CreateCardboardWall("WallEnd",
                new Vector3(wallThickness, wallHeight, corridorWidth),
                new Vector3(segmentBLength, wallY, 0f));
        }

        private void CreateCardboardWall(string name, Vector3 scale, Vector3 position)
        {
            var wall = CreateBox(name, scale, position);
            SetMaterial(wall, WallColor(), 0.05f);

            // High-friction physics material — prevents wall-sliding
            var collider = wall.GetComponent<Collider>();
            if (collider != null)
            {
                var wallPhysMat = new PhysicsMaterial("CardboardWall")
                {
                    dynamicFriction = 0.9f,
                    staticFriction = 0.95f,
                    bounciness = 0.05f,
                    frictionCombine = PhysicsMaterialCombine.Maximum,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                };
                collider.material = wallPhysMat;
            }

            // Corrugation stripes (darker vertical bands)
            bool isZWall = scale.x < scale.z;
            float wallLen = isZWall ? scale.z : scale.x;
            float stripeWidth = 0.01f;
            int stripeCount = Mathf.FloorToInt(wallLen / (stripeWidth * 2f));

            for (int i = 0; i < stripeCount; i++)
            {
                float offset = -wallLen * 0.5f + stripeWidth + i * stripeWidth * 2f;
                Vector3 stripeScale, stripePos;
                if (isZWall)
                {
                    stripeScale = new Vector3(wallThickness + 0.001f, wallHeight * 0.95f, stripeWidth);
                    stripePos = position + new Vector3(0f, 0f, offset);
                }
                else
                {
                    stripeScale = new Vector3(stripeWidth, wallHeight * 0.95f, wallThickness + 0.001f);
                    stripePos = position + new Vector3(offset, 0f, 0f);
                }

                var stripe = CreateBox($"{name}_s{i}", stripeScale, stripePos);
                SetMaterial(stripe, StripeColor(), 0.03f);
                DisableCollider(stripe);
            }
        }

        // ── Finish marker (ArUco-style) ─────────────────────────────

        private void CreateFinishMarker()
        {
            float markerSize = Mathf.Min(0.14f, corridorWidth * 0.5f);
            float markerY = 0.15f;
            float markerX = segmentBLength - wallThickness * 0.5f - 0.001f;

            var bg = CreateBox("MarkerBG",
                new Vector3(0.005f, markerSize, markerSize),
                new Vector3(markerX, markerY, 0f));
            SetMaterial(bg, MarkerWhite, 0.1f);
            DisableCollider(bg);

            float cellSize = markerSize / 5f;
            float startY = markerY - markerSize * 0.5f + cellSize * 0.5f;
            float startZ = -markerSize * 0.5f + cellSize * 0.5f;

            for (int row = 0; row < 5; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    bool isBorder = row == 0 || row == 4 || col == 0 || col == 4;
                    bool isBlack = isBorder || ((row + col) % 2 == 0);
                    if (isBlack)
                    {
                        var cell = CreateBox($"M_{row}{col}",
                            new Vector3(0.003f, cellSize * 0.9f, cellSize * 0.9f),
                            new Vector3(markerX - 0.002f, startY + row * cellSize, startZ + col * cellSize));
                        SetMaterial(cell, MarkerBlack, 0.05f);
                        DisableCollider(cell);
                    }
                }
            }
        }

        // ── Lighting ────────────────────────────────────────────────

        private void CreateLighting()
        {
            // rev22: layered indoor-style lighting — 1 directional (sun-like), warm
            // ambient fill, 2 point lights at corridor mid-segments (warm tungsten),
            // 1 spot light above the L-corner. Soft shadows on directional + spot
            // give actual depth cues at the corner that the policy can use to
            // estimate distance to wall.

            // 1. Directional (sun) — softer angle, less harsh than before
            var dirGo = new GameObject("TrackDirLight");
            dirGo.transform.SetParent(transform, false);
            dirGo.transform.localPosition = new Vector3(0.30f, 3f, -0.40f);
            dirGo.transform.localRotation = Quaternion.Euler(60f, -25f, 0f);
            var dirLight = dirGo.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            float dirIntensity = 0.85f;
            Color dirColor = new Color(1f, 0.97f, 0.92f);
            if (randomizeVisuals)
            {
                dirColor = JitterColor(new Color(1f, 0.97f, 0.92f), lightHueJitterDegrees, 0.10f, 0.10f);
                dirIntensity = lightIntensityMin + (float)rng.NextDouble() * (lightIntensityMax - lightIntensityMin);
            }
            dirLight.color = dirColor;
            dirLight.intensity = dirIntensity;
            dirLight.shadows = LightShadows.Soft;
            dirLight.shadowStrength = 0.85f;

            // 2. Warm point light over segment A start
            CreatePointLight("PointLightA",
                new Vector3(0f, 0.55f, -0.85f),
                new Color(1f, 0.85f, 0.65f),  // warm tungsten
                intensity: 1.4f,
                range: 1.8f);

            // 3. Warm point light over segment B end
            CreatePointLight("PointLightB",
                new Vector3(0.55f, 0.55f, 0f),
                new Color(1f, 0.82f, 0.60f),  // slightly warmer tungsten
                intensity: 1.3f,
                range: 1.5f);

            // 4. Spot light above the L-corner — emphasizes the turn shadow
            var spotGo = new GameObject("CornerSpot");
            spotGo.transform.SetParent(transform, false);
            spotGo.transform.localPosition = new Vector3(0f, 1.2f, 0.10f);
            spotGo.transform.localRotation = Quaternion.Euler(75f, 15f, 0f);
            var spotLight = spotGo.AddComponent<Light>();
            spotLight.type = LightType.Spot;
            spotLight.color = new Color(1f, 0.93f, 0.78f);
            spotLight.intensity = 2.0f;
            spotLight.range = 2.2f;
            spotLight.spotAngle = 95f;
            spotLight.innerSpotAngle = 60f;
            spotLight.shadows = LightShadows.Soft;
            spotLight.shadowStrength = 0.8f;

            // 5. Ambient: warm grey fill (always — provides global illumination
            //    so corridor isn't pitch-black even where direct lights miss).
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            Color ambient = new Color(0.40f, 0.39f, 0.36f);
            if (randomizeVisuals && (float)rng.NextDouble() < skyboxNullProbability)
            {
                RenderSettings.skybox = null;
                ambient = JitterColor(ambient, 20f, 0.08f, 0.08f);
            }
            RenderSettings.ambientLight = ambient;
        }

        private void CreatePointLight(string name, Vector3 localPos, Color color,
                                       float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.6f;
        }

        // ── Helpers ─────────────────────────────────────────────────

        private GameObject CreateBox(string name, Vector3 scale, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localScale = scale;
            go.transform.localPosition = position;
            return go;
        }

        private void SetMaterial(GameObject go, Color color, float smoothness)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            renderer.sharedMaterial = CreateMaterial(color, smoothness);
        }

        private static Material CreateMaterial(Color color, float smoothness)
        {
            var shader = RuntimeMaterialCompatibility.ResolveCompatibleUnlitShader();
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        private static void DisableCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
        }
    }
}
