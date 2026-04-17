using UavSimulator.Contracts;
using UavSimulator.Core;
using UnityEngine;

namespace UavSimulator.Tracks
{
    /// <summary>
    /// Procedural cardboard maze — L/S/zigzag corridor layouts generated on the fly.
    /// Parameters come from trackParams via ApplyTrackParams.
    /// Geometry is built by MazeGenerator (pure C#) and instantiated into Unity primitives.
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

        private static readonly Color CardboardBase = new Color(0.76f, 0.60f, 0.42f);
        private static readonly Color CardboardStripe = new Color(0.68f, 0.52f, 0.36f);
        private static readonly Color FloorColor = new Color(0.72f, 0.58f, 0.40f);
        private static readonly Color SurroundFloorColor = new Color(0.75f, 0.75f, 0.75f);
        private static readonly Color MarkerWhite = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color MarkerBlack = new Color(0.05f, 0.05f, 0.05f);

        private MazeGeometry? geometry;

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
                }
            }
        }

        public override void ResetTrack(int fallbackSeed)
        {
            // Clear previous geometry
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
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
            SetMaterial(floor, SurroundFloorColor, 0.3f);
        }

        private void CreateCorridorFloor(MazeGeometry g)
        {
            int i = 0;
            foreach (var cellPos in g.FloorCells)
            {
                var floor = CreateBox($"FloorCell_{i++}",
                    new Vector3(g.CorridorWidthM, 0.01f, g.CorridorWidthM),
                    cellPos);
                SetMaterial(floor, FloorColor, 0.2f);
            }
        }

        private void CreateWalls(MazeGeometry g)
        {
            var wallPhysMat = CreateWallPhysicsMaterial();
            int i = 0;
            foreach (var wall in g.Walls)
            {
                var go = CreateBox($"Wall_{i++}", wall.Scale, wall.Position);
                SetMaterial(go, CardboardBase, 0.05f);
                var collider = go.GetComponent<Collider>();
                if (collider != null) collider.material = wallPhysMat;
                AddCorrugationStripes(go, wall);
            }
        }

        private void AddCorrugationStripes(GameObject wall, WallSegment seg)
        {
            bool isZWall = seg.Scale.x < seg.Scale.z;
            float wallLen = isZWall ? seg.Scale.z : seg.Scale.x;
            float stripeWidth = 0.01f;
            int stripeCount = Mathf.FloorToInt(wallLen / (stripeWidth * 2f));
            for (int i = 0; i < stripeCount; i++)
            {
                float offset = -wallLen * 0.5f + stripeWidth + i * stripeWidth * 2f;
                Vector3 scale, pos;
                if (isZWall)
                {
                    scale = new Vector3(wallThickness + 0.001f, wallHeight * 0.95f, stripeWidth);
                    pos = seg.Position + new Vector3(0f, 0f, offset);
                }
                else
                {
                    scale = new Vector3(stripeWidth, wallHeight * 0.95f, wallThickness + 0.001f);
                    pos = seg.Position + new Vector3(offset, 0f, 0f);
                }
                var stripe = CreateBox($"{wall.name}_s{i}", scale, pos);
                SetMaterial(stripe, CardboardStripe, 0.03f);
                DisableCollider(stripe);
            }
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
            var lightGo = new GameObject("TrackLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 3f, 0f);
            lightGo.transform.localRotation = Quaternion.Euler(50f, -30f, 0f);
            var dirLight = lightGo.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.color = new Color(1f, 0.97f, 0.92f);
            dirLight.intensity = 1.0f;
            dirLight.shadows = LightShadows.Soft;
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
