using System;
using UnityEngine;

namespace ColourWays
{
    /// <summary>Tiny shared game state: pause and cursor locking.</summary>
    public static class GameState
    {
        /// <summary>True while the pause menu (or another modal screen) is open.</summary>
        public static bool Paused { get; private set; }

        /// <summary>Raised whenever <see cref="Paused"/> changes.</summary>
        public static event Action<bool> PauseChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Paused = false;
            PauseChanged = null;
        }

        public static void SetPaused(bool paused)
        {
            bool changed = Paused != paused;
            Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            LockCursor(!paused);
            if (changed) PauseChanged?.Invoke(paused);
        }

        /// <summary>Locked + hidden while playing (mouse turns the camera); free in menus.</summary>
        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
