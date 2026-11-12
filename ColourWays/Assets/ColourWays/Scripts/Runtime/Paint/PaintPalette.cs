using System.Collections.Generic;
using UnityEngine;

namespace ColourWays.Paint
{
    /// <summary>
    /// The set of colours the player can paint with (port of the Unreal "ColourArray").
    /// </summary>
    [CreateAssetMenu(menuName = "ColourWays/Paint Palette", fileName = "PaintPalette")]
    public sealed class PaintPalette : ScriptableObject
    {
        [SerializeField]
        private List<Color> colours = new List<Color>
        {
            new Color(1.00f, 0.31f, 0.60f), // pink
            new Color(1.00f, 0.54f, 0.12f), // orange
            new Color(1.00f, 0.82f, 0.12f), // yellow
            new Color(0.24f, 0.86f, 0.42f), // green
            new Color(0.12f, 0.78f, 1.00f), // cyan
            new Color(0.24f, 0.42f, 1.00f), // blue
            new Color(0.61f, 0.30f, 1.00f), // purple
        };

        public IReadOnlyList<Color> Colours => colours;
    }
}
