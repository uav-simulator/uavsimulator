using UavSimulator.Core;
using UnityEngine;

namespace UavSimulator.Tracks
{
    /// <summary>
    /// Procedural L-shaped cardboard corridor for sim-to-real training.
    ///
    /// Designed to match a real-world cardboard track built in an apartment:
    /// - Narrow corridor with visible walls (cardboard-colored)
    /// - Apartment-like floor (wood/laminate color)
    /// - Simple L-shape: straight → 90° right turn → straight → finish
    /// - ArUco-style finish marker on the end wall
    /// - No trees, no curbs, no decorations — only walls and floor.
    ///
    /// Layout (top-down, XZ plane):
    ///   Segment A: x=0, z=-1.1..z=0   (straight, +Z direction, 1.1m long)
    ///   Turn:      90° right turn area at (0, 0)
    ///   Segment B: z=0, x=0..x=0.9    (straight, +X direction, 0.9m long)
    ///   Finish:    end wall with ArUco marker at x=0.9
    ///
    /// Robot spawns at (0, 0.01, -0.85) facing +Z.
    ///
    /// Real-world scale: corridor width = 0.40m (40cm), wall height = 0.25m (25cm).
    /// This matches cardboard walls cut from 78x100cm sheets for the 150x190cm room test setup.
    /// </summary>
    public sealed class CardboardCorridorTrack : TrackBase
    {
        [SerializeField] private float corridorWidth = 0.40f;
        [SerializeField] private float wallHeight = 0.25f;
        [SerializeField] private float wallThickness = 0.02f;
        [SerializeField] private float segmentALength = 1.10f;
        [SerializeField] private float segmentBLength = 0.90f;
        [SerializeField] private float roomWidth = 1.50f;
        [SerializeField] private float roomLength = 1.90f;

        // Colors matching real cardboard and apartment floor
        private static readonly Color CardboardColor = new Color(0.76f, 0.60f, 0.42f);
        private static readonly Color CardboardDarkColor = new Color(0.65f, 0.50f, 0.35f);   // darker cardboard edge
        private static readonly Color FloorColor = new Color(0.72f, 0.58f, 0.40f);           // wood/laminate
        private static readonly Color FloorDarkColor = new Color(0.55f, 0.45f, 0.32f);       // floor grain
        private static readonly Color ApartmentFloorColor = new Color(0.82f, 0.78f, 0.72f);  // light tile/linoleum
        private static readonly Color MarkerWhite = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color MarkerBlack = new Color(0.05f, 0.05f, 0.05f);

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
                built = true;
                return;
            }

            BuildTrack();
            built = true;
        }

        private void BuildTrack()
        {
            // 1. Large apartment floor (light tile/linoleum)
            CreateApartmentFloor();

            // 2. Corridor floor (slightly different color to stand out)
            CreateCorridorFloor();

            // 3. Walls — the main visual feature the CNN will learn
            CreateWalls();

            // 4. Finish marker (ArUco-style pattern on end wall)
            CreateFinishMarker();

            // 5. Ambient lighting objects (simple room context)
            CreateRoomContext();

        }

        // ── Apartment Floor ──────────────────────────────────────────

        private void CreateApartmentFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ApartmentFloor";
            floor.transform.SetParent(transform, false);
            floor.transform.localScale = new Vector3(roomWidth, 0.02f, roomLength);
            floor.transform.localPosition = new Vector3(0.35f, -0.01f, -0.45f);
            SetMaterial(floor, ApartmentFloorColor, 0.3f);
            // Keep collider — this is the ground
        }

        // ── Corridor Floor ───────────────────────────────────────────

        private void CreateCorridorFloor()
        {
            float floorY = 0.005f; // Slightly above apartment floor

            // Segment A floor (along Z)
            float aLen = segmentALength + corridorWidth; // extend into turn area
            var floorA = CreateBox("CorridorFloorA",
                new Vector3(corridorWidth, 0.01f, aLen),
                new Vector3(0f, floorY, -segmentALength * 0.5f + corridorWidth * 0.5f));
            SetMaterial(floorA, FloorColor, 0.2f);

            // Segment B floor (along X)
            float bLen = segmentBLength + corridorWidth;
            var floorB = CreateBox("CorridorFloorB",
                new Vector3(bLen, 0.01f, corridorWidth),
                new Vector3(segmentBLength * 0.5f - corridorWidth * 0.5f, floorY, 0f));
            SetMaterial(floorB, FloorColor, 0.2f);
        }

        // ── Walls ────────────────────────────────────────────────────

        private void CreateWalls()
        {
            float hw = corridorWidth * 0.5f;
            float halfWall = wallHeight * 0.5f;
            float wallY = halfWall;

            // ┌─────────────────────────────────────────────────┐
            // │  Segment A: z = -segmentALength .. 0            │
            // │  Left wall (x = -hw), Right wall (x = +hw)      │
            // └─────────────────────────────────────────────────┘

            // Segment A — left wall (west side, extends from start to turn)
            CreateWall("WallA_Left",
                new Vector3(wallThickness, wallHeight, segmentALength),
                new Vector3(-hw, wallY, -segmentALength * 0.5f));

            // Segment A — right wall (east side, extends from start to turn inner corner)
            // Stops at z = 0 minus corridor width (inner corner of the turn)
            float aRightLen = segmentALength - corridorWidth;
            CreateWall("WallA_Right",
                new Vector3(wallThickness, wallHeight, aRightLen),
                new Vector3(hw, wallY, -segmentALength * 0.5f - corridorWidth * 0.5f + aRightLen * 0.5f));

            // ┌─────────────────────────────────────────────────┐
            // │  Turn: inner corner at (+hw, -hw)               │
            // │  Outer corner follows the L-shape               │
            // └─────────────────────────────────────────────────┘

            // Outer wall of the turn — extends from segment A left wall to segment B
            // This is the wall at x = -hw going from z=0 along z direction, then turns to z = +hw going along x
            CreateWall("WallTurn_Outer_Z",
                new Vector3(wallThickness, wallHeight, corridorWidth),
                new Vector3(-hw, wallY, corridorWidth * 0.5f));

            // Outer wall segment B bottom (z = -hw, from x = -hw to x = segmentBLength)
            // This wall is at z = -hw extending along X
            CreateWall("WallB_Bottom",
                new Vector3(segmentBLength + hw, wallHeight, wallThickness),
                new Vector3(segmentBLength * 0.5f - hw * 0.5f, wallY, -hw));

            // ┌─────────────────────────────────────────────────┐
            // │  Segment B: x = 0 .. segmentBLength, z = 0     │
            // │  Top wall (z = +hw), Bottom wall (z = -hw)      │
            // └─────────────────────────────────────────────────┘

            // Segment B — top wall (north side)
            CreateWall("WallB_Top",
                new Vector3(segmentBLength, wallHeight, wallThickness),
                new Vector3(segmentBLength * 0.5f, wallY, hw));

            // ┌─────────────────────────────────────────────────┐
            // │  Start wall (closes the start end of segment A) │
            // │  End wall (closes the end of segment B)         │
            // └─────────────────────────────────────────────────┘

            // Start wall (back of segment A)
            CreateWall("WallStart",
                new Vector3(corridorWidth, wallHeight, wallThickness),
                new Vector3(0f, wallY, -segmentALength));

            // End wall (end of segment B) — will have ArUco marker
            CreateWall("WallEnd",
                new Vector3(wallThickness, wallHeight, corridorWidth),
                new Vector3(segmentBLength, wallY, 0f));
        }

        private GameObject CreateWall(string name, Vector3 scale, Vector3 position)
        {
            var wall = CreateBox(name, scale, position);
            SetMaterial(wall, CardboardColor, 0.05f);
            // Keep collider — walls are physical barriers
            return wall;
        }

        // ── Finish Marker (ArUco-style) ──────────────────────────────

        private void CreateFinishMarker()
        {
            // White background square on the end wall
            float markerSize = Mathf.Min(0.14f, corridorWidth * 0.5f);
            float markerY = 0.15f;
            float markerX = segmentBLength - wallThickness * 0.5f - 0.001f;

            var bg = CreateBox("MarkerBG",
                new Vector3(0.005f, markerSize, markerSize),
                new Vector3(markerX, markerY, 0f));
            SetMaterial(bg, MarkerWhite, 0.1f);
            DisableCollider(bg);

            // Black inner pattern (simplified ArUco — just a checkerboard-ish pattern)
            float cellSize = markerSize / 5f;
            float startY = markerY - markerSize * 0.5f + cellSize * 0.5f;
            float startZ = -markerSize * 0.5f + cellSize * 0.5f;

            // Border cells (all black) — top and bottom rows, left and right columns
            for (int row = 0; row < 5; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    bool isBorder = row == 0 || row == 4 || col == 0 || col == 4;
                    // Inner pattern: checkerboard
                    bool isBlack = isBorder || ((row + col) % 2 == 0);

                    if (isBlack)
                    {
                        var cell = CreateBox($"MarkerCell_{row}_{col}",
                            new Vector3(0.003f, cellSize * 0.9f, cellSize * 0.9f),
                            new Vector3(markerX - 0.002f, startY + row * cellSize, startZ + col * cellSize));
                        SetMaterial(cell, MarkerBlack, 0.05f);
                        DisableCollider(cell);
                    }
                }
            }
        }

        // ── Room Context (simple furniture-like objects) ─────────────

        private void CreateRoomContext()
        {
            // A few simple boxes outside the corridor to simulate apartment context
            // This helps with domain randomization — the model learns to ignore background

            // "Table" outside the corridor
            var table = CreateBox("Table",
                new Vector3(0.8f, 0.4f, 0.5f),
                new Vector3(-0.72f, 0.2f, -0.90f));
            SetMaterial(table, new Color(0.45f, 0.30f, 0.18f), 0.15f);
            DisableCollider(table);

            // "Chair"
            var chair = CreateBox("Chair",
                new Vector3(0.3f, 0.5f, 0.3f),
                new Vector3(-0.55f, 0.25f, 0.35f));
            SetMaterial(chair, new Color(0.35f, 0.25f, 0.15f), 0.1f);
            DisableCollider(chair);

            // "Box/package" (looks like another cardboard box)
            var box = CreateBox("CardboardBox",
                new Vector3(0.25f, 0.20f, 0.30f),
                new Vector3(0.95f, 0.10f, 0.35f));
            SetMaterial(box, CardboardDarkColor, 0.05f);
            DisableCollider(box);

            // Ceiling light (just a flat white square above)
            var light = CreateBox("CeilingLight",
                new Vector3(0.4f, 0.02f, 0.4f),
                new Vector3(0.35f, 2.5f, -0.45f));
            SetMaterial(light, new Color(1f, 0.98f, 0.92f), 0.8f);
            DisableCollider(light);

            // Directional light for the scene
            var lightGo = new GameObject("RoomLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0.35f, 3f, -0.45f);
            lightGo.transform.localRotation = Quaternion.Euler(55f, -30f, 0f);
            var dirLight = lightGo.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.color = new Color(1f, 0.96f, 0.90f);
            dirLight.intensity = 1.2f;
            dirLight.shadows = LightShadows.Soft;
        }

        // ── Helpers ──────────────────────────────────────────────────

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

            renderer.sharedMaterial = CreateLitMaterial(color, smoothness);
        }

        private static Material CreateLitMaterial(Color color, float smoothness)
        {
            var shader = ResolveTrackShader();
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

        private static Shader ResolveTrackShader()
        {
            return RuntimeMaterialCompatibility.ResolveCompatibleUnlitShader();
        }

        private static void DisableCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);
        }
    }
}
