using System;
using UnityEngine;

namespace ColourWays.Puzzle
{
    public enum CellType : byte
    {
        Floor = 0,
        Wall = 1,
        Pit = 2,
    }

    /// <summary>Grid direction. North = +Y (away from the camera).</summary>
    public enum Dir : byte
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    public enum InstructionType : byte
    {
        TurnLeft = 0,
        TurnRight = 1,
        Arrow = 2,
        Reverse = 3,
        Jump = 4,
    }

    [Serializable]
    public struct Placement : IEquatable<Placement>
    {
        public InstructionType Type;
        /// <summary>Only used by <see cref="InstructionType.Arrow"/>.</summary>
        public Dir Dir;

        public Placement(InstructionType type, Dir dir = Dir.North)
        {
            Type = type;
            Dir = dir;
        }

        public bool Equals(Placement other) => Type == other.Type && (Type != InstructionType.Arrow || Dir == other.Dir);
        public override bool Equals(object obj) => obj is Placement p && Equals(p);
        public override int GetHashCode() => ((int)Type << 4) | (int)Dir;
    }

    public static class DirExt
    {
        private static readonly Vector2Int[] Offsets =
        {
            new Vector2Int(0, 1),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0),
        };

        public static Vector2Int Offset(this Dir d) => Offsets[(int)d];
        public static Dir Right(this Dir d) => (Dir)(((int)d + 1) & 3);
        public static Dir Left(this Dir d) => (Dir)(((int)d + 3) & 3);
        public static Dir Opposite(this Dir d) => (Dir)(((int)d + 2) & 3);

        /// <summary>Yaw in degrees for a transform facing this direction (North = +Z).</summary>
        public static float Yaw(this Dir d) => (int)d * 90f;

        public static Vector3 World(this Dir d)
        {
            var o = Offsets[(int)d];
            return new Vector3(o.x, 0f, o.y);
        }

        /// <summary>
        /// Direction after applying an instruction to a walker heading <paramref name="heading"/>.
        /// Jump keeps the heading (the jump itself is handled by the simulation).
        /// </summary>
        public static Dir Apply(this Placement p, Dir heading)
        {
            switch (p.Type)
            {
                case InstructionType.TurnLeft: return heading.Left();
                case InstructionType.TurnRight: return heading.Right();
                case InstructionType.Reverse: return heading.Opposite();
                case InstructionType.Arrow: return p.Dir;
                default: return heading;
            }
        }
    }
}
