using System.Runtime.InteropServices;
using UnityEngine;

namespace ColourWays.Puzzle
{
    /// <summary>
    /// Short haptic taps for drag-and-drop. Web: navigator.vibrate (Android browsers; iOS Safari
    /// ignores it). Other platforms: no-op for now. Can be disabled by the player.
    /// </summary>
    public static class Haptics
    {
        private const string PrefKey = "cw_haptics";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 1) == 1;
            set => PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void CW_Vibrate(int milliseconds);
        [DllImport("__Internal")] private static extern void CW_VibratePattern(string pattern);
#endif

        /// <summary>Picking something up / hovering a new cell.</summary>
        public static void Tick() => Vibrate(6);
        public static void Light() => Vibrate(10);
        /// <summary>Dropping an instruction onto the grid.</summary>
        public static void Medium() => Vibrate(18);
        public static void Heavy() => Vibrate(32);
        /// <summary>Removing an instruction.</summary>
        public static void Remove() => Pattern("10,40,10");
        /// <summary>Level complete.</summary>
        public static void Success() => Pattern("20,50,20,50,60");

        public static void Vibrate(int milliseconds)
        {
            if (!Enabled) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            CW_Vibrate(milliseconds);
#endif
        }

        public static void Pattern(string csvMilliseconds)
        {
            if (!Enabled) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            CW_VibratePattern(csvMilliseconds);
#endif
        }
    }
}
