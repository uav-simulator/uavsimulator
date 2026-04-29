using UavSimulator.Contracts;
using UavSimulator.Core;
using UnityEngine;

namespace UavSimulator.Tracks
{
    /// <summary>
    /// Procedural cardboard maze — L/S/zigzag corridor layouts generated on the fly.
    /// Parameters come from trackParams via ApplyTrackParams.
    /// Geometry is built by MazeGenerator (pure C#) and instantiated into Unity primitives.
    ///
    /// Plan 1 (rev37, 2026-04-29): heavy-DR visuals ported from
    /// CardboardCorridorTrack.cs — wood-plank floor texture, mixed wall styles
    /// (cardboard / white plaster / mixed), per-wall albedo value jitter,
    /// warm point lights + spot above finish marker. This brings maze visual
    /// distribution up to the rev24+ corridor budget so policies can transfer
    /// between the two tracks.
    /// </summary>
    public sealed class CardboardMazeTrack : TrackBase
    {
        [SerializeField] private int seed = 42;
        [SerializeField] private int lengthCells = 8;
        [SerializeField] private float corridorWidth = 0.60f;
        [SerializeField] private int leftTurns = 2;
        [SerializeField] private int rightTurns = 2;
        [SerializeField] private float wallHeight = 0.25f;
        [SerializeField] private float wallThickness = 0.02f;
        // When set via trackParams "maze.path_encoded", overrides seed/turns with explicit path
        private string pathEncoded = "";

        // ── rev37 heavy-DR knobs (port from CardboardCorridorTrack) ──
        [SerializeField] private bool randomizeVisuals = true;
        [SerializeField] private float wallHueJitterDegrees = 25f;
        [SerializeField] private float wallValueJitterRange = 0.20f;
        [SerializeField] private float wallSaturationJitterRange = 0.15f;
        [SerializeField] private float lightIntensityMin = 0.55f;
        [SerializeField] private float lightIntensityMax = 1.15f;
        [SerializeField] private float lightHueJitterDegrees = 30f;
        [SerializeField] private float skyboxNullProbability = 0.0f;
        [SerializeField, Range(0f, 1f)] private float pCardboardWall = 0.50f;
        [SerializeField, Range(0f, 1f)] private float pWhiteWall = 0.30f;
        // Mixed style takes whatever probability is left.

        // Base palette — randomization perturbs around these.
        private static readonly Color CardboardBase = new Color(0.76f, 0.60f, 0.42f);
        private static readonly Color CardboardStripe = new Color(0.68f, 0.52f, 0.36f);
        private static readonly Color FloorColor = new Color(0.72f, 0.58f, 0.40f);
        private static readonly Color SurroundFloorColor = new Color(0.75f, 0.75f, 0.75f);
        private static readonly Color WhitePlasterBase = new Color(0.86f, 0.85f, 0.83f);
        private static readonly Color MarkerWhite = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color MarkerBlack = new Color(0.05f, 0.05f, 0.05f);

        private MazeGeometry? geometry;

        // ── rev37 heavy-DR randomization state ──
        private System.Random rng;
        private Color randomizedCardboardBase;
        private Color randomizedCardboardStripe;
        private Color randomizedFloorColor;
        private Color randomizedSurroundFloorColor;
        private Texture2D woodPlankTexture;
        private int wallSeedCounter;
        private enum WallStyle { Cardboard, White, Mixed }

        public override void ApplyTrackParams(ConfigKeyValue[] trackParams)
        {
            if (trackParams == null) return;
            foreach (var p in trackParams)
            {
                if (p.key == null) continue;
                switch (p.key.Trim().ToLowerInvariant())
                {
                    case "maze.seed": if (int.TryParse(p.value, out var s)) seed = s; break;
                    case "maze.length_cells": if (int.TryParse(p.value, out var lc)) lengthCells = lc; break;
                    case "maze.corridor_width_m": if (float.TryParse(p.value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cw)) corridorWidth = cw; break;
                    case "maze.left_turns": if (int.TryParse(p.value, out var lt)) leftTurns = lt; break;
                    case "maze.right_turns": if (int.TryParse(p.value, out var rt)) rightTurns = rt; break;
                    case "maze.wall_height_m": if (float.TryParse(p.value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var wh)) wallHeight = wh; break;
                    case "maze.path_encoded": pathEncoded = p.value ?? ""; break;
                }
            }
        }

        // rev37: deterministic-default RNG — Awake fires before any ResetTrack,
        // so children rendering before first sim-host reset still has a valid
        // RNG. Same pattern as CardboardCorridorTrack.
        private void Awake()
        {
            if (rng == null) rng = new System.Random(0);
        }

        public override void ResetTrack(int fallbackSeed)
        {
            // rev37: DestroyImmediate so children are gone *this* frame; Destroy()
            // pending objects make Unity report transform.childCount > 0 and the
            // build can be skipped silently. Same trick as CardboardCorridorTrack.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(transform.GetChild(i).gameObject);
            }

            // rev37: per-reset RNG. `seed` comes from trackParams ("maze.seed")
            // when Python sets it; otherwise SerializeField default applies.
            // fallbackSeed (Unity-side reset seed) is used only if maze.seed
            // was never set in this run.
            rng = new System.Random(seed);
            if (randomizeVisuals)
            {
                JitterPalette();
            }

            var parameters = new MazeParams
            {
                Seed = seed,
                LengthCells = lengthCells,
                CorridorWidthM = corridorWidth,
                LeftTurns = leftTurns,
                RightTurns = rightTurns,
                WallHeightM = wallHeight,
                WallThicknessM = wallThickness,
            };

            MazeGeometry g;
            // If path_encoded is provided (from Python Truth), build from it directly.
            // This avoids C#/Python PRNG mismatch when generating from seed.
            if (!string.IsNullOrWhiteSpace(pathEncoded))
            {
                try
                {
                    g = MazeGenerator.BuildFromEncodedPath(pathEncoded, parameters);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[CardboardMazeTrack] path_encoded parse failed: {ex.Message}. Falling back to seed.");
                    g = MazeGenerator.Generate(parameters);
                }
            }
            else
            {
                try
                {
                    g = MazeGenerator.Generate(parameters);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[CardboardMazeTrack] Generation failed: {ex.Message}. Falling back to defaults.");
                    parameters = MazeParams.Defaults();
                    g = MazeGenerator.Generate(parameters);
                }
            }
            geometry = g;

            BuildFromGeometry(g);
        }

        public override Vector3? GetDefaultSpawnPosition() => geometry?.SpawnPosition;
        public override float? GetDefaultSpawnYawDeg() => geometry?.SpawnYawDeg;
        public override Vector2[] GetDefaultWaypoints() => geometry?.Waypoints;

        // ── Geometry ───────────────────────────────────────────────

        private void BuildFromGeometry(MazeGeometry g)
        {
            CreateSurroundingFloor(g);
            CreateCorridorFloor(g);
            CreateWalls(g);
            CreateFinishMarker(g);
            CreateLighting(g);
        }

        private void CreateSurroundingFloor(MazeGeometry g)
        {
            var floor = CreateBox("SurroundFloor",
                new Vector3(10f, 0.01f, 10f),
                new Vector3(0f, -0.005f, 0f));
            SetMaterial(floor, SurroundFloorColorActive(), 0.3f);
        }

        private void CreateCorridorFloor(MazeGeometry g)
        {
            // rev37: wood-plank textured floor cells with per-cell value tint,
            // so adjacent cells look subtly different (matches real-world wood
            // floor where each plank is slightly different shade).
            int i = 0;
            foreach (var cellPos in g.FloorCells)
            {
                Color tint = Color.white;
                if (randomizeVisuals)
                {
                    float shade = 0.85f + (float)rng.NextDouble() * 0.30f;
                    tint = Color.white * shade;
                    tint.a = 1f;
                }
                var floor = CreateBox($"FloorCell_{i++}",
                    new Vector3(g.CorridorWidthM, 0.01f, g.CorridorWidthM),
                    cellPos);
                if (randomizeVisuals && woodPlankTexture != null)
                {
                    // Tile so planks are at realistic ~0.30m scale
                    Vector2 tile = new Vector2(2f, 2f);
                    SetTexturedMaterial(floor, tint, woodPlankTexture, tile, 0.2f);
                }
                else
                {
                    SetMaterial(floor, FloorColorActive(), 0.2f);
                }
            }
        }

        private void CreateWalls(MazeGeometry g)
        {
            var wallPhysMat = CreateWallPhysicsMaterial();
            int i = 0;
            foreach (var wall in g.Walls)
            {
                var go = CreateBox($"Wall_{i++}", wall.Scale, wall.Position);

                // rev37: per-wall style mix — same pCardboard/pWhite/pMixed logic
                // as CardboardCorridorTrack so policies cannot memorize "all
                // walls are tan cardboard".
                WallStyle style = randomizeVisuals ? PickWallStyle() : WallStyle.Cardboard;

                // Per-wall albedo value jitter — fakes uneven indoor lighting on
                // an Unlit shader. Crucial for matching real-corridor where
                // lighting is highly non-uniform.
                float lightingShade = 1f;
                if (randomizeVisuals)
                {
                    lightingShade = 0.70f + (float)rng.NextDouble() * 0.55f;  // 0.70–1.25
                }

                Color wallColor = ResolveWallStyleColor(style) * lightingShade;
                wallColor.a = 1f;
                SetMaterial(go, wallColor, 0.05f);

                var collider = go.GetComponent<Collider>();
                if (collider != null) collider.material = wallPhysMat;

                // rev37: mixed-style walls get a vertical seam stripe
                // (cardboard-only walls keep no corrugation stripes — too
                // expensive to regenerate every episode for many cells).
                if (style == WallStyle.Mixed)
                {
                    AddMixedWallSeam(go.name, wall.Scale, wall.Position, wallColor);
                }
            }
        }

        private void AddMixedWallSeam(string parentName, Vector3 scale, Vector3 position, Color baseColor)
        {
            bool isZWall = scale.x < scale.z;
            float wallLen = isZWall ? scale.z : scale.x;
            float seamPos = -wallLen * 0.5f + wallLen * (0.35f + (float)rng.NextDouble() * 0.30f);
            float seamW = 0.008f;

            Vector3 seamScale, seamPosV;
            if (isZWall)
            {
                seamScale = new Vector3(wallThickness + 0.001f, wallHeight * 0.95f, seamW);
                seamPosV = position + new Vector3(0f, 0f, seamPos);
            }
            else
            {
                seamScale = new Vector3(seamW, wallHeight * 0.95f, wallThickness + 0.001f);
                seamPosV = position + new Vector3(seamPos, 0f, 0f);
            }
            var seam = CreateBox($"{parentName}_seam", seamScale, seamPosV);
            Color seamColor = baseColor * 0.55f; seamColor.a = 1f;
            SetMaterial(seam, seamColor, 0.05f);
            DisableCollider(seam);
        }

        private static PhysicsMaterial CreateWallPhysicsMaterial()
        {
            return new PhysicsMaterial("CardboardWall")
            {
                dynamicFriction = 0.9f,
                staticFriction = 0.95f,
                bounciness = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
        }

        private void CreateFinishMarker(MazeGeometry g)
        {
            float markerSize = Mathf.Min(0.14f, g.CorridorWidthM * 0.5f);
            Vector3 markerPos = g.FinishMarkerPosition;
            Vector3 bgScale;
            switch (g.FinishMarkerFacingDir)
            {
                case MazeGenerator.Dir.North:
                case MazeGenerator.Dir.South:
                    bgScale = new Vector3(markerSize, markerSize, 0.005f);
                    break;
                default: // East/West
                    bgScale = new Vector3(0.005f, markerSize, markerSize);
                    break;
            }
            var bg = CreateBox("MarkerBG", bgScale, markerPos);
            SetMaterial(bg, MarkerWhite, 0.1f);
            DisableCollider(bg);

            float cellSize = markerSize / 5f;
            float startY = markerPos.y - markerSize * 0.5f + cellSize * 0.5f;
            bool zWall = g.FinishMarkerFacingDir == MazeGenerator.Dir.North || g.FinishMarkerFacingDir == MazeGenerator.Dir.South;
            float startAxial = (zWall ? markerPos.x : markerPos.z) - markerSize * 0.5f + cellSize * 0.5f;

            for (int row = 0; row < 5; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    bool isBorder = row == 0 || row == 4 || col == 0 || col == 4;
                    bool isBlack = isBorder || ((row + col) % 2 == 0);
                    if (!isBlack) continue;

                    Vector3 cellScale, cellPos;
                    if (zWall)
                    {
                        cellScale = new Vector3(cellSize * 0.9f, cellSize * 0.9f, 0.003f);
                        float zOffset = g.FinishMarkerFacingDir == MazeGenerator.Dir.North ? -0.002f : 0.002f;
                        cellPos = new Vector3(startAxial + col * cellSize, startY + row * cellSize, markerPos.z + zOffset);
                    }
                    else
                    {
                        cellScale = new Vector3(0.003f, cellSize * 0.9f, cellSize * 0.9f);
                        float xOffset = g.FinishMarkerFacingDir == MazeGenerator.Dir.East ? -0.002f : 0.002f;
                        cellPos = new Vector3(markerPos.x + xOffset, startY + row * cellSize, startAxial + col * cellSize);
                    }

                    var cell = CreateBox($"MC_{row}{col}", cellScale, cellPos);
                    SetMaterial(cell, MarkerBlack, 0.05f);
                    DisableCollider(cell);
                }
            }
        }

        private void CreateLighting(MazeGeometry g)
        {
            // rev37: layered indoor-style lighting matching CardboardCorridor.
            // 1 directional sun + per-N-cells warm point lights + 1 spot above
            // finish marker + warm ambient fill.

            // 1. Directional (sun) with optional jitter
            var dirGo = new GameObject("TrackDirLight");
            dirGo.transform.SetParent(transform, false);
            dirGo.transform.localPosition = new Vector3(0f, 3f, 0f);
            dirGo.transform.localRotation = Quaternion.Euler(50f, -30f, 0f);
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

            // 2. Per-cell warm point lights (1 every 3 cells), warm tungsten
            int lightCount = 0;
            int floorCellIdx = 0;
            foreach (var cellPos in g.FloorCells)
            {
                if (floorCellIdx % 3 == 0)
                {
                    Color tungsten = new Color(1f, 0.85f, 0.65f);
                    float intensity = 1.2f + (float)(rng?.NextDouble() ?? 0.5) * 0.4f;
                    CreatePointLight($"PointLight_{lightCount++}",
                        new Vector3(cellPos.x, 0.55f, cellPos.z),
                        tungsten,
                        intensity: intensity,
                        range: 1.5f);
                }
                floorCellIdx++;
            }

            // 3. Spot light above finish marker — emphasizes the goal
            if (g.FloorCells != null && g.FloorCells.Count > 0)
            {
                var finishCell = g.FloorCells[g.FloorCells.Count - 1];
                var spotGo = new GameObject("FinishSpot");
                spotGo.transform.SetParent(transform, false);
                spotGo.transform.localPosition = new Vector3(finishCell.x, 1.2f, finishCell.z);
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
            }

            // 4. Ambient: warm grey fill (always — provides global illumination)
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

        // ── rev37 heavy-DR helpers (port from CardboardCorridorTrack) ──

        private void JitterPalette()
        {
            randomizedCardboardBase = JitterColor(CardboardBase, wallHueJitterDegrees, wallSaturationJitterRange, wallValueJitterRange);
            randomizedCardboardStripe = JitterColor(CardboardStripe, wallHueJitterDegrees, wallSaturationJitterRange, wallValueJitterRange);
            randomizedFloorColor = JitterColor(FloorColor, wallHueJitterDegrees, wallSaturationJitterRange, wallValueJitterRange);
            randomizedSurroundFloorColor = JitterColor(SurroundFloorColor, wallHueJitterDegrees, wallSaturationJitterRange, wallValueJitterRange);

            // Regenerate wood-plank texture per-reset using a sub-seed off rng
            int texSeed = rng.Next();
            if (woodPlankTexture != null) UnityEngine.Object.Destroy(woodPlankTexture);
            woodPlankTexture = GenerateWoodPlankTexture(texSeed);

            wallSeedCounter = 0;
        }

        private Color JitterColor(Color baseColor, float hueDeg, float satRange, float valRange)
        {
            Color.RGBToHSV(baseColor, out var h, out var s, out var v);
            h = (h + (float)(rng.NextDouble() - 0.5) * hueDeg / 360f + 1f) % 1f;
            s = Mathf.Clamp01(s + (float)(rng.NextDouble() - 0.5) * 2f * satRange);
            v = Mathf.Clamp01(v + (float)(rng.NextDouble() - 0.5) * 2f * valRange);
            return Color.HSVToRGB(h, s, v);
        }

        private WallStyle PickWallStyle()
        {
            wallSeedCounter++;
            double r = rng.NextDouble();
            if (r < pCardboardWall) return WallStyle.Cardboard;
            if (r < pCardboardWall + pWhiteWall) return WallStyle.White;
            return WallStyle.Mixed;
        }

        private Color ResolveWallStyleColor(WallStyle style)
        {
            switch (style)
            {
                case WallStyle.White:
                    return JitterColor(WhitePlasterBase, 8f, 0.05f, 0.12f);
                case WallStyle.Mixed:
                    Color avg = (randomizedCardboardBase + WhitePlasterBase) * 0.5f;
                    return JitterColor(avg, wallHueJitterDegrees * 0.5f,
                                       wallSaturationJitterRange * 0.5f,
                                       wallValueJitterRange);
                case WallStyle.Cardboard:
                default:
                    return randomizeVisuals ? randomizedCardboardBase : CardboardBase;
            }
        }

        private Color FloorColorActive() => randomizeVisuals ? randomizedFloorColor : FloorColor;
        private Color SurroundFloorColorActive() => randomizeVisuals ? randomizedSurroundFloorColor : SurroundFloorColor;

        // Procedural oak-plank texture (128x128, 3-6 horizontal planks with
        // Perlin grain inside each + dark seams). Direct port from
        // CardboardCorridorTrack so floor visuals match between tracks.
        private Texture2D GenerateWoodPlankTexture(int seed)
        {
            var rand = new System.Random(seed);
            const int W = 128, H = 128;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, mipChain: false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Repeat;

            float hueDeg = 22f + (float)rand.NextDouble() * 24f;
            float baseSat = 0.30f + (float)rand.NextDouble() * 0.25f;
            float baseVal = 0.45f + (float)rand.NextDouble() * 0.20f;

            int plankCount = 3 + rand.Next(4);
            int plankH = H / plankCount;
            int seamOffset = rand.Next(plankH);

            float[] plankShades = new float[plankCount];
            float[] plankHueOffsets = new float[plankCount];
            for (int p = 0; p < plankCount; p++)
            {
                plankShades[p] = 0.78f + 0.40f * (float)rand.NextDouble();
                plankHueOffsets[p] = ((float)rand.NextDouble() - 0.5f) * 8f;
            }

            float perlinScaleX = 0.04f + (float)rand.NextDouble() * 0.03f;
            float perlinScaleY = 0.5f + (float)rand.NextDouble() * 0.5f;
            float perlinOffsetX = (float)rand.NextDouble() * 100f;
            float perlinOffsetY = (float)rand.NextDouble() * 100f;

            var pixels = new Color[W * H];
            for (int y = 0; y < H; y++)
            {
                int yShift = (y + seamOffset) % H;
                int plankIdx = (yShift / plankH) % plankCount;
                int yWithinPlank = yShift % plankH;
                bool isSeam = yWithinPlank == 0 || yWithinPlank == plankH - 1;

                float plankShade = plankShades[plankIdx];
                float plankHue = (hueDeg + plankHueOffsets[plankIdx]) / 360f;

                for (int x = 0; x < W; x++)
                {
                    float grain = Mathf.PerlinNoise(
                        x * perlinScaleX + perlinOffsetX,
                        y * perlinScaleY + perlinOffsetY);
                    float grainModulate = 0.85f + grain * 0.30f;

                    Color c = Color.HSVToRGB(
                        Mathf.Repeat(plankHue, 1f),
                        Mathf.Clamp01(baseSat + (grain - 0.5f) * 0.15f),
                        Mathf.Clamp01(baseVal * plankShade * grainModulate));

                    if (isSeam) c *= 0.55f;
                    c.a = 1f;
                    pixels[y * W + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private void SetTexturedMaterial(GameObject go, Color tint, Texture2D tex,
                                          Vector2 tile, float smoothness)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = RuntimeMaterialCompatibility.ResolveCompatibleUnlitShader();
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (tex != null)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", tex);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", tex);
                Vector4 st = new Vector4(tile.x, tile.y, 0f, 0f);
                if (material.HasProperty("_BaseMap_ST")) material.SetVector("_BaseMap_ST", st);
                if (material.HasProperty("_MainTex_ST")) material.SetVector("_MainTex_ST", st);
            }
            renderer.sharedMaterial = material;
        }

        // ── Primitive helpers ──────────────────────────────────────

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
            var shader = RuntimeMaterialCompatibility.ResolveCompatibleUnlitShader();
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            renderer.sharedMaterial = material;
        }

        private static void DisableCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
        }
    }
}
