using UnityEngine;

namespace ColourWays
{
    /// <summary>Tiny shared game state: pause and cursor locking.</summary>
    public static class GameState
    {
        /// <summary>True while the pause menu (or another modal screen) is open.</summary>
        public static bool Paused { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Paused = false;

        public static void SetPaused(bool paused)
        {
            Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            LockCursor(!paused);
        }

        /// <summary>Locked + hidden while playing (mouse turns the camera); free in menus.</summary>
        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
