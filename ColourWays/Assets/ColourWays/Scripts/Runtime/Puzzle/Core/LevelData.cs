using UnityEngine;

namespace ColourWays.Puzzle
{
    /// <summary>A generated puzzle grid. Out-of-bounds cells behave as pits (walk off the edge = fall).</summary>
    public sealed class LevelData
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int Seed;
        public Vector2Int Start;
        public Dir StartDir;
        public Vector2Int Exit;

        /// <summary>Par score computed by the solver (instructions + one clean run). -1 if unknown.</summary>
        public int Par = -1;
        /// <summary>The placements the solver used to reach par (for hints / debugging).</summary>
        public System.Collections.Generic.Dictionary<Vector2Int, Placement> ParSolution;

        private readonly CellType[] _cells;

        public LevelData(int width, int height, int seed)
        {
            Width = width;
            Height = height;
            Seed = seed;
            _cells = new CellType[width * height];
        }

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;

        public CellType Get(Vector2Int c) => InBounds(c) ? _cells[c.y * Width + c.x] : CellType.Pit;

        public void Set(Vector2Int c, CellType type)
        {
            if (InBounds(c)) _cells[c.y * Width + c.x] = type;
        }

        public bool IsBlocked(Vector2Int c) => Get(c) == CellType.Wall;

        public bool IsWalkable(Vector2Int c) => InBounds(c) && Get(c) == CellType.Floor;

        public LevelData Clone()
        {
            var copy = new LevelData(Width, Height, Seed) { Start = Start, StartDir = StartDir, Exit = Exit, Par = Par, ParSolution = ParSolution };
            System.Array.Copy(_cells, copy._cells, _cells.Length);
            return copy;
        }
    }
}
