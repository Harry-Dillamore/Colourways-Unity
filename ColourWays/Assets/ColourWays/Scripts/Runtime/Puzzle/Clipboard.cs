using System.Runtime.InteropServices;
using UnityEngine;

namespace ColourWays.Puzzle
{
    /// <summary>Copy text for sharing. Web: navigator.clipboard (falls back to the share sheet on mobile).</summary>
    public static class Clipboard
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void CW_CopyText(string text);
#endif

        public static void Copy(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            CW_CopyText(text);
#else
            GUIUtility.systemCopyBuffer = text;
#endif
        }
    }
}
