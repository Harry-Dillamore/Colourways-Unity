using UnityEngine;
#if COLOURWAYS_FMOD
using FMODUnity;
#endif

namespace ColourWays.Audio
{
    /// <summary>
    /// Central mapping between ColourWays gameplay cues and FMOD Studio events/parameters.
    /// Place an instance at <c>Assets/ColourWays/Resources/ColourWays/AudioConfig.asset</c>
    /// (or run <c>ColourWays &gt; Audio (FMOD) &gt; Create Default Audio Config</c>).
    ///
    /// When the FMOD for Unity package is imported, <c>COLOURWAYS_FMOD</c> is enabled automatically
    /// and native FMOD EventReference pickers appear below.
    /// </summary>
    [CreateAssetMenu(menuName = "ColourWays/Audio Config", fileName = "AudioConfig")]
    public sealed class AudioConfig : ScriptableObject
    {
        public const string ResourcesPath = "ColourWays/AudioConfig";

#if COLOURWAYS_FMOD
        [Header("FMOD Studio Events (Music & Snapshots)")]
        [Tooltip("Looping background music event (e.g. event:/Music/MainTheme).")]
        public EventReference mainMusic;

        [Tooltip("Optional mixer snapshot applied while the pause menu is open (e.g. snapshot:/Pause).")]
        public EventReference pauseSnapshot;

        [Header("FMOD Studio Events (Player SFX)")]
        public EventReference footstep;
        public EventReference wallSplat;
        public EventReference land;
        public EventReference jump;
        public EventReference respawn;

        [Header("FMOD Studio Events (World & UI SFX)")]
        public EventReference paintBlobCollect;
        public EventReference levelComplete;
        public EventReference uiHover;
        public EventReference uiClick;
        public EventReference sceneTransition;
#endif

        [Header("Fallback FMOD Event Paths")]
        [Tooltip("If an EventReference above is unassigned, try resolving these standard FMOD Studio paths automatically.")]
        public bool allowPathFallback = true;

        public string mainMusicPath = "event:/Music/MainTheme";
        public string pauseSnapshotPath = "snapshot:/Pause";
        public string footstepPath = "event:/SFX/Player/Footstep";
        public string wallSplatPath = "event:/SFX/Player/WallSplat";
        public string landPath = "event:/SFX/Player/Land";
        public string jumpPath = "event:/SFX/Player/Jump";
        public string respawnPath = "event:/SFX/Player/Respawn";
        public string paintBlobCollectPath = "event:/SFX/World/PaintBlobCollect";
        public string levelCompletePath = "event:/SFX/World/LevelComplete";
        public string uiHoverPath = "event:/SFX/UI/Hover";
        public string uiClickPath = "event:/SFX/UI/Click";
        public string sceneTransitionPath = "event:/SFX/UI/Transition";

        [Header("Adaptive Parameter Names")]
        [Tooltip("0 in MainMenu, 1 in gameplay levels.")]
        public string paramIsInLevel = "IsInLevel";

        [Tooltip("0..1 normalised player horizontal speed.")]
        public string paramPlayerSpeed = "PlayerSpeed";

        [Tooltip("0 when playing, 1 while paused.")]
        public string paramPaused = "Paused";

        [Tooltip("0..1 normalised impact strength passed to WallSplat / Land one-shots.")]
        public string paramImpactSpeed = "ImpactSpeed";

        [Header("Telemetry & Debug Overlay")]
        [Tooltip("Log every triggered audio event to Unity Console (and Player.log in standalone builds).")]
        public bool logEventsToConsole = true;

        [Tooltip("Write a CSV event & performance log to Application.persistentDataPath/fmod_audio_log.csv.")]
        public bool enableCsvTelemetry = true;

        [Tooltip("Show the F9 Audio Debug Overlay automatically on startup (helpful when recording build videos).")]
        public bool showDebugOverlayOnStart;
    }
}
