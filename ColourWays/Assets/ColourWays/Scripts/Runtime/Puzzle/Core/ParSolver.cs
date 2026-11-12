using System.Collections.Generic;
using UnityEngine;

namespace ColourWays.Puzzle
{
    /// <summary>
    /// Finds a cheap solution for a level to use as "par" (golf scoring: instructions + run time).
    /// Dijkstra over (cell, heading) where instructions cost their points and every cell walked
    /// costs time points; every candidate is verified in the real simulation.
    /// </summary>
    public static class ParSolver
    {
        public struct Result
        {
            public bool Solved;
            public int Score;
            public int InstructionCost;
            public float Time;
            public Dictionary<Vector2Int, Placement> Placements;
        }

        /// <param name="costs">Cost per <see cref="InstructionType"/> (index); negative = not allowed.</param>
        public static Result Solve(LevelData level, SimSettings sim, int[] costs, float timePointsPerSecond)
        {
            var best = new Result { Solved = false, Score = int.MaxValue };

            // Try a few time/instruction trade-offs; keep whichever really scores best.
            foreach (float weight in new[] { 1f, 0.35f, 3f })
            {
                var placements = Dijkstra(level, sim, costs, timePointsPerSecond * weight);
                if (placements != null)
                    Consider(level, sim, costs, timePointsPerSecond, placements, ref best);
            }

            var greedy = GreedyAlongShortestPath(level, costs);
            if (greedy != null)
                Consider(level, sim, costs, timePointsPerSecond, greedy, ref best);

            return best;
        }

        public static int InstructionCost(IReadOnlyDictionary<Vector2Int, Placement> placements, int[] costs)
        {
            int total = 0;
            foreach (var p in placements.Values)
                total += Mathf.Max(0, costs[(int)p.Type]);
            return total;
        }

        public static int Score(int instructionCost, float runSeconds, float timePointsPerSecond)
            => instructionCost + Mathf.RoundToInt(runSeconds * timePointsPerSecond);

        private static void Consider(LevelData level, SimSettings sim, int[] costs, float timePts, Dictionary<Vector2Int, Placement> placements, ref Result best)
        {
            float t = PuzzleSim.Evaluate(level, placements, sim);
            if (t < 0f) return;
            int cost = InstructionCost(placements, costs);
            int score = Score(cost, t, timePts);
            if (score < best.Score)
                best = new Result { Solved = true, Score = score, InstructionCost = cost, Time = t, Placements = placements };
        }

        // ------------------------------------------------------------------ Dijkstra

        private struct Option
        {
            public bool Has;
            public Placement P;
        }

        private static Dictionary<Vector2Int, Placement> Dijkstra(LevelData level, SimSettings sim, int[] costs, float timePts)
        {
            int w = level.Width, h = level.Height;
            int n = w * h * 4;
            var dist = new float[n];
            var prev = new int[n];
            var used = new Option[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }

            float cellCost = timePts / Mathf.Max(0.01f, sim.speed);
            var options = BuildOptions(costs);

            int startIndex = Index(level, level.Start, level.StartDir);
            dist[startIndex] = 0f;
            int goal = -1;

            for (int iter = 0; iter < n; iter++)
            {
                int u = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!done[i] && dist[i] < bestDist) { bestDist = dist[i]; u = i; }
                if (u < 0) break;
                done[u] = true;

                Decode(level, u, out var cell, out var heading);
                if (cell == level.Exit) { goal = u; break; }
                if (level.Get(cell) == CellType.Pit) continue;

                // Option: no instruction.
                Relax(level, u, cell, heading, new Option(), 0f, cellCost, dist, prev, used, done);
                foreach (var (p, c) in options)
                    Relax(level, u, cell, heading, new Option { Has = true, P = p }, c, cellCost, dist, prev, used, done);
            }

            if (goal < 0) return null;

            var placements = new Dictionary<Vector2Int, Placement>();
            for (int s = goal; prev[s] >= 0; s = prev[s])
            {
                var opt = used[s];
                if (!opt.Has) continue;
                Decode(level, prev[s], out var cell, out _);
                if (placements.TryGetValue(cell, out var existing) && !existing.Equals(opt.P))
                    return null; // conflicting instructions on a revisited cell
                placements[cell] = opt.P;
            }
            return placements;
        }

        private static void Relax(LevelData level, int u, Vector2Int cell, Dir heading, Option opt, float instrCost, float cellCost,
            float[] dist, int[] prev, Option[] used, bool[] done)
        {
            Dir dir = opt.Has ? opt.P.Apply(heading) : heading;
            Vector2Int next;
            float travel;

            if (opt.Has && opt.P.Type == InstructionType.Jump)
            {
                next = cell + dir.Offset() * 2;
                if (!level.InBounds(next) || level.IsBlocked(next)) return; // a failed jump == no instruction
                travel = cellCost * 2f;
            }
            else
            {
                next = cell + dir.Offset();
                // Walls splat and the edge is a drop: both are dead ends.
                if (!level.InBounds(next) || level.IsBlocked(next)) return;
                travel = cellCost;
            }

            int v = Index(level, next, dir);
            if (done[v]) return;
            float nd = dist[u] + instrCost + travel;
            if (nd < dist[v])
            {
                dist[v] = nd;
                prev[v] = u;
                used[v] = opt;
            }
        }

        private static List<(Placement, float)> BuildOptions(int[] costs)
        {
            var list = new List<(Placement, float)>();
            void Add(InstructionType t, Dir d = Dir.North)
            {
                int c = costs[(int)t];
                if (c >= 0) list.Add((new Placement(t, d), c));
            }
            Add(InstructionType.TurnLeft);
            Add(InstructionType.TurnRight);
            Add(InstructionType.Reverse);
            Add(InstructionType.Jump);
            for (int d = 0; d < 4; d++) Add(InstructionType.Arrow, (Dir)d);
            return list;
        }

        private static int Index(LevelData level, Vector2Int c, Dir d) => ((c.y * level.Width) + c.x) * 4 + (int)d;

        private static void Decode(LevelData level, int index, out Vector2Int cell, out Dir dir)
        {
            dir = (Dir)(index & 3);
            int i = index >> 2;
            cell = new Vector2Int(i % level.Width, i / level.Width);
        }

        // ------------------------------------------------------------------ Fallback

        /// <summary>Walk the BFS shortest path, adding the cheapest instruction wherever the wall rule goes the wrong way.</summary>
        private static Dictionary<Vector2Int, Placement> GreedyAlongShortestPath(LevelData level, int[] costs)
        {
            var path = LevelGenerator.ShortestPath(level, level.Start, level.Exit, null);
            if (path == null) return null;

            var placements = new Dictionary<Vector2Int, Placement>();
            Dir heading = level.StartDir;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var cell = path[i];
                var delta = path[i + 1] - cell;
                Dir want = DirFromOffset(delta);

                if (heading == want)
                    continue;

                Placement? bestP = null;
                int bestCost = int.MaxValue;
                foreach (var candidate in new[]
                         {
                             new Placement(InstructionType.TurnLeft), new Placement(InstructionType.TurnRight),
                             new Placement(InstructionType.Reverse), new Placement(InstructionType.Arrow, want),
                         })
                {
                    int c = costs[(int)candidate.Type];
                    if (c < 0 || c >= bestCost) continue;
                    if (candidate.Apply(heading) != want) continue;
                    bestP = candidate;
                    bestCost = c;
                }

                if (bestP == null) return null;
                placements[cell] = bestP.Value;
                heading = want;
            }
            return placements;
        }

        public static Dir DirFromOffset(Vector2Int d)
        {
            if (d.y > 0) return Dir.North;
            if (d.x > 0) return Dir.East;
            if (d.y < 0) return Dir.South;
            return Dir.West;
        }
    }
}
