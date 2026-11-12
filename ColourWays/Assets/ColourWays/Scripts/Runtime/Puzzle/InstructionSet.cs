using System;
using System.Collections.Generic;
using UnityEngine;

namespace ColourWays.Puzzle
{
    /// <summary>
    /// The instructions available in the tray: display, cost (golf points; simpler = cheaper)
    /// and whether they're enabled. Edit in the Inspector to rebalance.
    /// </summary>
    [CreateAssetMenu(menuName = "ColourWays/Instruction Set", fileName = "InstructionSet")]
    public sealed class InstructionSet : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public InstructionType type;
            public string displayName = "Instruction";
            [Tooltip("Points added to the score while this instruction is on the board.")]
            [Min(0)] public int cost = 1;
            public bool enabled = true;
            public Color colour = Color.white;
            public Sprite icon;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public Entry Get(InstructionType type)
        {
            foreach (var e in entries)
                if (e.type == type) return e;
            return null;
        }

        public int Cost(InstructionType type)
        {
            var e = Get(type);
            return e != null ? e.cost : 0;
        }

        /// <summary>Costs indexed by <see cref="InstructionType"/>; -1 for disabled/missing (used by the solver).</summary>
        public int[] CostTable()
        {
            var table = new int[Enum.GetValues(typeof(InstructionType)).Length];
            for (int i = 0; i < table.Length; i++) table[i] = -1;
            foreach (var e in entries)
                if (e.enabled) table[(int)e.type] = e.cost;
            return table;
        }
    }
}
