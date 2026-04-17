using System;
using System.Collections.Generic;
using UnityEngine;

namespace UavSimulator.Tracks
{
    /// <summary>
    /// Grid-based drunk-walk generator for L/S/zigzag corridor layouts.
    /// Pure C# — no Unity MonoBehaviour. Deterministic by seed.
    ///
    /// Algorithm:
    /// 1. Start at (20, 20) facing +Z (north).
    /// 2. Walk forward / turn left / turn right within turn budgets.
    /// 3. Never enter occupied cells. Backtrack on dead ends.
    /// 4. Stop after length_cells cells or throw on total failure.
    /// </summary>
    public static class MazeGenerator
    {
        private const int GridSize = 40;
        private const int StartX = 20;
        private const int StartZ = 20;
        private const int MaxBacktracks = 20;

        public enum Dir { North, East, South, West } // +Z, +X, -Z, -X

        private struct Cell { public int X; public int Z; }

        public static MazeGeometry Generate(MazeParams parameters)
        {
            var rng = new System.Random(parameters.Seed);
            var path = new List<Cell> { new Cell { X = StartX, Z = StartZ } };
            var occupied = new HashSet<long> { PackCell(StartX, StartZ) };
            var direction = Dir.North;

            // Turn budgets and per-step turn tracking (for backtrack release)
            int leftBudget = Mathf.Max(0, parameters.LeftTurns);
            int rightBudget = Mathf.Max(0, parameters.RightTurns);
            var turnHistory = new List<int>(); // 0=forward, -1=left, +1=right per step (length = path.Count - 1)

            int failStreak = 0;
            int targetLength = Mathf.Clamp(parameters.LengthCells, 2, 200);

            while (path.Count < targetLength)
            {
                var last = path[path.Count - 1];
                var candidates = new List<(Dir dir, int turn)>();

                // Forward always available (if cell is valid)
                candidates.Add((direction, 0));
                if (leftBudget > 0) candidates.Add((TurnLeft(direction), -1));
                if (rightBudget > 0) candidates.Add((TurnRight(direction), +1));

                // Filter by validity
                var valid = new List<(Dir dir, int turn)>();
                foreach (var c in candidates)
                {
                    var next = Step(last, c.dir);
                    if (InBounds(next) && !occupied.Contains(PackCell(next.X, next.Z)))
                    {
                        valid.Add(c);
                    }
                }

                if (valid.Count == 0)
                {
                    // Dead end — backtrack
                    if (path.Count <= 1)
                    {
                        throw new InvalidOperationException(
                            "Maze generation failed: seed creates immediate dead end. Change seed or increase turn budget.");
                    }
                    path.RemoveAt(path.Count - 1);
                    var droppedTurn = turnHistory[turnHistory.Count - 1];
                    turnHistory.RemoveAt(turnHistory.Count - 1);
                    occupied.Remove(PackCell(last.X, last.Z));
                    // Release the turn budget that was consumed for this step
                    if (droppedTurn == -1) leftBudget++;
                    else if (droppedTurn == +1) rightBudget++;
                    // Rewind direction: reverse the sequence of turns from start
                    direction = RewindDirection(turnHistory);
                    failStreak++;
                    if (failStreak > MaxBacktracks)
                    {
                        throw new InvalidOperationException(
                            "Maze generation failed after too many backtracks. Change seed or reduce path length.");
                    }
                    continue;
                }

                // Weighted pick: forward weight 2, turn weight 1
                int totalWeight = 0;
                foreach (var c in valid) totalWeight += (c.turn == 0 ? 2 : 1);
                int roll = rng.Next(totalWeight);
                int cursor = 0;
                var chosen = valid[0];
                foreach (var c in valid)
                {
                    cursor += (c.turn == 0 ? 2 : 1);
                    if (roll < cursor) { chosen = c; break; }
                }

                // Apply step
                var nextCell = Step(last, chosen.dir);
                path.Add(nextCell);
                occupied.Add(PackCell(nextCell.X, nextCell.Z));
                turnHistory.Add(chosen.turn);
                direction = chosen.dir;
                if (chosen.turn == -1) leftBudget--;
                else if (chosen.turn == +1) rightBudget--;
                failStreak = 0;
            }

            return BuildGeometry(path, parameters);
        }

        // ── Geometry construction ───────────────────────────────────

        private static MazeGeometry BuildGeometry(List<Cell> path, MazeParams p)
        {
            float cellSize = p.CorridorWidthM;
            var floorCells = new List<Vector3>(path.Count);
            foreach (var c in path)
            {
                floorCells.Add(new Vector3(CellToWorldX(c.X, cellSize), 0.005f, CellToWorldZ(c.Z, cellSize)));
            }

            var pathSet = new HashSet<long>();
            foreach (var c in path) pathSet.Add(PackCell(c.X, c.Z));

            var walls = new List<WallSegment>();
            float halfCell = cellSize * 0.5f;
            float wallY = p.WallHeightM * 0.5f;

            foreach (var c in path)
            {
                float worldX = CellToWorldX(c.X, cellSize);
                float worldZ = CellToWorldZ(c.Z, cellSize);

                // North neighbour (+Z)
                if (!pathSet.Contains(PackCell(c.X, c.Z + 1)))
                {
                    walls.Add(new WallSegment
                    {
                        Position = new Vector3(worldX, wallY, worldZ + halfCell),
                        Scale = new Vector3(cellSize, p.WallHeightM, p.WallThicknessM),
                    });
                }
                // South (-Z)
                if (!pathSet.Contains(PackCell(c.X, c.Z - 1)))
                {
                    walls.Add(new WallSegment
                    {
                        Position = new Vector3(worldX, wallY, worldZ - halfCell),
                        Scale = new Vector3(cellSize, p.WallHeightM, p.WallThicknessM),
                    });
                }
                // East (+X)
                if (!pathSet.Contains(PackCell(c.X + 1, c.Z)))
                {
                    walls.Add(new WallSegment
                    {
                        Position = new Vector3(worldX + halfCell, wallY, worldZ),
                        Scale = new Vector3(p.WallThicknessM, p.WallHeightM, cellSize),
                    });
                }
                // West (-X)
                if (!pathSet.Contains(PackCell(c.X - 1, c.Z)))
                {
                    walls.Add(new WallSegment
                    {
                        Position = new Vector3(worldX - halfCell, wallY, worldZ),
                        Scale = new Vector3(p.WallThicknessM, p.WallHeightM, cellSize),
                    });
                }
            }

            // Spawn
            var first = path[0];
            var second = path.Count > 1 ? path[1] : first;
            var spawnDir = DirectionFromCells(first, second);
            var spawnPos = new Vector3(
                CellToWorldX(first.X, cellSize),
                0.01f,
                CellToWorldZ(first.Z, cellSize));
            var spawnYaw = DirToYawDeg(spawnDir);

            // Waypoints — world XZ centers of every cell
            var waypoints = new Vector2[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                waypoints[i] = new Vector2(
                    CellToWorldX(path[i].X, cellSize),
                    CellToWorldZ(path[i].Z, cellSize));
            }

            // Finish marker — on the wall of the last cell opposite the approach direction
            var last = path[path.Count - 1];
            var approach = path.Count >= 2 ? DirectionFromCells(path[path.Count - 2], last) : Dir.North;
            var markerDir = approach; // the wall we're heading into
            var markerPos = new Vector3(
                CellToWorldX(last.X, cellSize),
                0.15f,
                CellToWorldZ(last.Z, cellSize));
            switch (markerDir)
            {
                case Dir.North: markerPos.z += halfCell; break;
                case Dir.South: markerPos.z -= halfCell; break;
                case Dir.East: markerPos.x += halfCell; break;
                case Dir.West: markerPos.x -= halfCell; break;
            }

            return new MazeGeometry
            {
                FloorCells = floorCells.ToArray(),
                Walls = walls.ToArray(),
                SpawnPosition = spawnPos,
                SpawnYawDeg = spawnYaw,
                Waypoints = waypoints,
                GoalRadiusM = cellSize * 0.4f,
                FinishMarkerPosition = markerPos,
                FinishMarkerFacingDir = markerDir,
                CorridorWidthM = cellSize,
                WallHeightM = p.WallHeightM,
                WallThicknessM = p.WallThicknessM,
            };
        }

        // ── Helpers ─────────────────────────────────────────────────

        private static bool InBounds(Cell c) => c.X >= 0 && c.X < GridSize && c.Z >= 0 && c.Z < GridSize;
        private static long PackCell(int x, int z) => ((long)x << 16) | (uint)z;

        private static Cell Step(Cell c, Dir d)
        {
            switch (d)
            {
                case Dir.North: return new Cell { X = c.X, Z = c.Z + 1 };
                case Dir.East: return new Cell { X = c.X + 1, Z = c.Z };
                case Dir.South: return new Cell { X = c.X, Z = c.Z - 1 };
                case Dir.West: return new Cell { X = c.X - 1, Z = c.Z };
            }
            return c;
        }

        private static Dir TurnLeft(Dir d) => (Dir)(((int)d + 3) % 4);
        private static Dir TurnRight(Dir d) => (Dir)(((int)d + 1) % 4);

        private static Dir RewindDirection(List<int> turnHistory)
        {
            Dir d = Dir.North;
            foreach (var t in turnHistory)
            {
                if (t == -1) d = TurnLeft(d);
                else if (t == +1) d = TurnRight(d);
            }
            return d;
        }

        private static Dir DirectionFromCells(Cell a, Cell b)
        {
            if (b.Z > a.Z) return Dir.North;
            if (b.X > a.X) return Dir.East;
            if (b.Z < a.Z) return Dir.South;
            return Dir.West;
        }

        private static float CellToWorldX(int gridX, float cellSize) => (gridX - StartX) * cellSize;
        private static float CellToWorldZ(int gridZ, float cellSize) => (gridZ - StartZ) * cellSize;

        private static float DirToYawDeg(Dir d)
        {
            switch (d)
            {
                case Dir.North: return 0f;
                case Dir.East: return 90f;
                case Dir.South: return 180f;
                case Dir.West: return 270f;
            }
            return 0f;
        }
    }

    [Serializable]
    public struct MazeParams
    {
        public int Seed;
        public int LengthCells;
        public float CorridorWidthM;
        public int LeftTurns;
        public int RightTurns;
        public float WallHeightM;
        public float WallThicknessM;

        public static MazeParams Defaults() => new MazeParams
        {
            Seed = 42,
            LengthCells = 8,
            CorridorWidthM = 0.60f,
            LeftTurns = 2,
            RightTurns = 2,
            WallHeightM = 0.25f,
            WallThicknessM = 0.02f,
        };
    }

    [Serializable]
    public struct WallSegment
    {
        public Vector3 Position;
        public Vector3 Scale;
    }

    [Serializable]
    public struct MazeGeometry
    {
        public Vector3[] FloorCells;
        public WallSegment[] Walls;
        public Vector3 SpawnPosition;
        public float SpawnYawDeg;
        public Vector2[] Waypoints;
        public float GoalRadiusM;
        public Vector3 FinishMarkerPosition;
        public MazeGenerator.Dir FinishMarkerFacingDir;
        public float CorridorWidthM;
        public float WallHeightM;
        public float WallThicknessM;
    }
}
