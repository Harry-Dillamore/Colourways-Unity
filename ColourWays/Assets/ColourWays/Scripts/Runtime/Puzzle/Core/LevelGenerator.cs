using System;
using System.Collections.Generic;
using UnityEngine;

namespace ColourWays.Puzzle
{
    public enum LevelStyle
    {
        /// <summary>Corridors and rooms with loops: structured, "designed" feeling levels.</summary>
        Maze,
        /// <summary>Open field with scattered wall runs and pit clusters.</summary>
        Scatter,
    }

    [Serializable]
    public sealed class GeneratorSettings
    {
        public LevelStyle style = LevelStyle.Maze;
        [Tooltip("Board width in cells (maze style rounds to odd).")]
        [Min(4)] public int width = 9;
        [Tooltip("Board depth in cells (maze style rounds to odd).")]
        [Min(5)] public int height = 13;

        [Header("Maze")]
        [Tooltip("Chance to knock through each remaining internal wall, creating loops / alternate routes.")]
        [Range(0f, 1f)] public float loopChance = 0.3f;
        [Tooltip("Open rooms carved into the maze (min, max).")]
        public Vector2Int rooms = new Vector2Int(1, 2);
        [Tooltip("Pits dropped into corridors (min, max).")]
        public Vector2Int mazePits = new Vector2Int(2, 4);

        [Header("Scatter")]
        [Tooltip("Number of wall runs (min, max).")]
        public Vector2Int wallSegments = new Vector2Int(5, 8);
        public Vector2Int wallSegmentLength = new Vector2Int(2, 4);
        [Tooltip("Number of pit clusters (min, max).")]
        public Vector2Int pitClusters = new Vector2Int(3, 5);
        public Vector2Int pitClusterSize = new Vector2Int(1, 3);
        [Header("Validation")]
        [Tooltip("Shortest walkable route must be at least this many steps.")]
        public int minPathLength = 14;
        [Tooltip("Reject levels whose par needs fewer instructions than this.")]
        public int minParInstructions = 2;
        public int maxAttempts = 400;
    }

    /// <summary>
    /// Seeded level generator. Every level it returns:
    ///  • is completable (a walkable route from start to exit exists and the solver found par),
    ///  • has at least two routes (the shortest route's interior can be blocked and it's still solvable),
    ///  • can't be solved with zero instructions.
    /// Same seed + settings = same level (used for the daily puzzle).
    /// </summary>
    public static class LevelGenerator
    {
        /// <summary>Seed for today's daily puzzle (UTC date as yyyyMMdd).</summary>
        public static int DailySeed(DateTime utcNow) => utcNow.Year * 10000 + utcNow.Month * 100 + utcNow.Day;

        public static LevelData Generate(int seed, GeneratorSettings g, SimSettings sim, int[] costs, float timePointsPerSecond)
        {
            var rng = new System.Random(seed);
            LevelData fallback = null;

            for (int attempt = 0; attempt < g.maxAttempts; attempt++)
            {
                // Ease off density if we keep failing.
                float density = Mathf.Lerp(1f, 0.4f, attempt / (float)g.maxAttempts);
                var level = g.style == LevelStyle.Maze ? CarveMaze(rng, seed, g, density) : Carve(rng, seed, g, density);

                var path = ShortestPath(level, level.Start, level.Exit, null);
                if (path == null) continue;

                if (!HasAlternateRoute(level, path)) continue;
                if (PuzzleSim.Evaluate(level, new Dictionary<Vector2Int, Placement>(), sim, 40f) >= 0f) continue; // too easy

                var par = ParSolver.Solve(level, sim, costs, timePointsPerSecond);
                if (!par.Solved) continue;

                level.Par = par.Score;
                level.ParSolution = par.Placements;
                fallback ??= level;

                if (path.Count - 1 < g.minPathLength) continue;
                if (par.Placements.Count < g.minParInstructions) continue;
                return level;
            }

            if (fallback != null) return fallback;

            // Last resort: open field with the exit offset, always solvable with two routes.
            var open = new LevelData(g.width, g.height, seed)
            {
                Start = new Vector2Int(0, 0),
                StartDir = Dir.North,
                Exit = new Vector2Int(g.width - 1, g.height - 1),
            };
            var openPar = ParSolver.Solve(open, sim, costs, timePointsPerSecond);
            open.Par = openPar.Solved ? openPar.Score : -1;
            open.ParSolution = openPar.Placements;
            return open;
        }

        private static LevelData Carve(System.Random rng, int seed, GeneratorSettings g, float density)
        {
            int w = g.width, h = g.height;
            var level = new LevelData(w, h, seed);

            level.Start = new Vector2Int(rng.Next(w), 0);
            level.StartDir = Dir.North;
            int exitX;
            do exitX = rng.Next(w);
            while (w > 2 && Mathf.Abs(exitX - level.Start.x) < 2);
            level.Exit = new Vector2Int(exitX, h - 1);

            var keep = new HashSet<Vector2Int>
            {
                level.Start,
                level.Start + Vector2Int.up,
                level.Exit,
                level.Exit + Vector2Int.down,
            };

            int segments = Mathf.RoundToInt(Range(rng, g.wallSegments) * density);
            for (int s = 0; s < segments; s++)
            {
                var c = new Vector2Int(rng.Next(w), 1 + rng.Next(Mathf.Max(1, h - 2)));
                Dir d = (Dir)rng.Next(4);
                int len = Range(rng, g.wallSegmentLength);
                for (int i = 0; i < len; i++, c += d.Offset())
                    if (level.InBounds(c) && !keep.Contains(c))
                        level.Set(c, CellType.Wall);
            }

            int clusters = Mathf.RoundToInt(Range(rng, g.pitClusters) * density);
            for (int s = 0; s < clusters; s++)
            {
                var c = new Vector2Int(rng.Next(w), 1 + rng.Next(Mathf.Max(1, h - 2)));
                int size = Range(rng, g.pitClusterSize);
                for (int i = 0; i < size; i++)
                {
                    if (level.InBounds(c) && !keep.Contains(c))
                        level.Set(c, CellType.Pit);
                    c += ((Dir)rng.Next(4)).Offset();
                }
            }

            return level;
        }

        /// <summary>
        /// Randomised-DFS maze on the even cells, extra walls knocked through for loops, a few
        /// open rooms, then pits in corridors. Start on the bottom row, exit on the top row.
        /// </summary>
        private static LevelData CarveMaze(System.Random rng, int seed, GeneratorSettings g, float density)
        {
            int w = g.width | 1, h = g.height | 1; // odd so the maze lattice fits exactly
            var level = new LevelData(w, h, seed);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    level.Set(new Vector2Int(x, y), CellType.Wall);

            int nx = (w + 1) / 2, ny = (h + 1) / 2;
            var visited = new bool[nx, ny];
            var stack = new Stack<Vector2Int>();
            var startNode = new Vector2Int(rng.Next(nx), rng.Next(ny));
            stack.Push(startNode);
            visited[startNode.x, startNode.y] = true;
            level.Set(startNode * 2, CellType.Floor);

            var options = new List<Vector2Int>(4);
            while (stack.Count > 0)
            {
                var node = stack.Peek();
                options.Clear();
                for (int d = 0; d < 4; d++)
                {
                    var n = node + ((Dir)d).Offset();
                    if (n.x >= 0 && n.y >= 0 && n.x < nx && n.y < ny && !visited[n.x, n.y]) options.Add(n);
                }
                if (options.Count == 0) { stack.Pop(); continue; }

                var next = options[rng.Next(options.Count)];
                visited[next.x, next.y] = true;
                level.Set(next * 2, CellType.Floor);
                level.Set(node + next, CellType.Floor); // (node*2 + next*2) / 2: the wall between
                stack.Push(next);
            }

            // Loops: knock through remaining walls that sit between two corridor cells.
            float loopChance = g.loopChance * Mathf.Lerp(1f, 1.6f, 1f - density);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool between = (x & 1) != (y & 1);
                    var c = new Vector2Int(x, y);
                    if (!between || level.Get(c) != CellType.Wall) continue;
                    if (rng.NextDouble() < loopChance) level.Set(c, CellType.Floor);
                }
            }

            // Rooms: open rectangles that read as distinct areas.
            int rooms = Range(rng, g.rooms);
            for (int r = 0; r < rooms; r++)
            {
                int rw = 3 + 2 * rng.Next(2), rh = 3 + 2 * rng.Next(2);
                int rx = 2 * rng.Next(Mathf.Max(1, (w - rw) / 2 + 1));
                int ry = 2 + 2 * rng.Next(Mathf.Max(1, (h - rh - 4) / 2 + 1));
                for (int y = ry; y < Mathf.Min(h, ry + rh); y++)
                    for (int x = rx; x < Mathf.Min(w, rx + rw); x++)
                        level.Set(new Vector2Int(x, y), CellType.Floor);
            }

            level.Start = new Vector2Int(2 * rng.Next(nx), 0);
            level.StartDir = Dir.North;
            int exitX;
            int guard = 0;
            do exitX = 2 * rng.Next(nx);
            while (Mathf.Abs(exitX - level.Start.x) < 2 && guard++ < 20);
            level.Exit = new Vector2Int(exitX, h - 1);
            level.Set(level.Start, CellType.Floor);
            level.Set(level.Exit, CellType.Floor);

            // Pits on corridor cells away from the start and exit.
            var keep = new HashSet<Vector2Int> { level.Start, level.Exit };
            for (int d = 0; d < 4; d++)
            {
                keep.Add(level.Start + ((Dir)d).Offset());
                keep.Add(level.Exit + ((Dir)d).Offset());
            }
            int pits = Mathf.RoundToInt(Range(rng, g.mazePits) * density);
            for (int i = 0, tries = 0; i < pits && tries < 200; tries++)
            {
                var c = new Vector2Int(rng.Next(w), 1 + rng.Next(h - 2));
                if (keep.Contains(c) || level.Get(c) != CellType.Floor) continue;
                level.Set(c, CellType.Pit);
                i++;
            }

            return level;
        }

        private static int Range(System.Random rng, Vector2Int range) => rng.Next(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y) + 1);

        /// <summary>
        /// True if blocking the shortest route's interior (keeping two cells at each end) still
        /// leaves a walkable route, i.e. there are genuinely different ways through.
        /// </summary>
        public static bool HasAlternateRoute(LevelData level, List<Vector2Int> path)
        {
            var blocked = new HashSet<Vector2Int>();
            for (int i = 2; i < path.Count - 2; i++)
                blocked.Add(path[i]);
            if (blocked.Count == 0) return true;
            return ShortestPath(level, level.Start, level.Exit, blocked) != null;
        }

        /// <summary>BFS over walkable cells (4-neighbour). Returns the cell list including both ends, or null.</summary>
        public static List<Vector2Int> ShortestPath(LevelData level, Vector2Int from, Vector2Int to, HashSet<Vector2Int> blocked)
        {
            var prev = new Dictionary<Vector2Int, Vector2Int>();
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(from);
            prev[from] = from;

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (c == to)
                {
                    var path = new List<Vector2Int>();
                    for (var p = to; ; p = prev[p])
                    {
                        path.Add(p);
                        if (p == from) break;
                    }
                    path.Reverse();
                    return path;
                }

                for (int d = 0; d < 4; d++)
                {
                    var n = c + ((Dir)d).Offset();
                    if (prev.ContainsKey(n) || !level.IsWalkable(n)) continue;
                    if (blocked != null && blocked.Contains(n)) continue;
                    prev[n] = c;
                    queue.Enqueue(n);
                }
            }
            return null;
        }
    }
}
