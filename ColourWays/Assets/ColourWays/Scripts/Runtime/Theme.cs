using System;
using UnityEngine;

namespace ColourWays
{
    /// <summary>
    /// Light / dark mode, remembered between sessions. Light: a pure white world revealed by
    /// paint. Dark: everything white turns black and the paint glows neon (ColourWaysTheme.hlsl
    /// reads the global _CW_Dark). <see cref="ThemeApplier"/> animates the switch in a scene.
    /// </summary>
    public static class Theme
    {
        private const string Key = "cw_dark";
        private static bool _loaded, _dark;

        public static bool IsDark
        {
            get
            {
                if (!_loaded) { _dark = PlayerPrefs.GetInt(Key, 0) == 1; _loaded = true; }
                return _dark;
            }
        }

        /// <summary>Animated 0 (light) .. 1 (dark), driven by <see cref="ThemeApplier"/>. Use it to blend UI colours.</summary>
        public static float Blend { get; internal set; }

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _loaded = false;
            Changed = null;
            Blend = 0f;
        }

        public static void Set(bool dark)
        {
            if (dark == IsDark) return;
            _dark = dark;
            PlayerPrefs.SetInt(Key, dark ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void Toggle() => Set(!IsDark);

        public static Color Pick(Color light, Color dark) => Color.Lerp(light, dark, Blend);
    }
}
