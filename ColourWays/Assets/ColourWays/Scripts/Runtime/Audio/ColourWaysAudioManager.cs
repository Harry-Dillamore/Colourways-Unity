using System;
using System.Collections.Generic;
using ColourWays.Level;
using ColourWays.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
#if COLOURWAYS_FMOD
using FMOD;
using FMOD.Studio;
using FMODUnity;
#endif

namespace ColourWays.Audio
{
    /// <summary>
    /// Persistent audio manager that bridges ColourWays gameplay events to FMOD Studio.
    ///
    /// Automatically bootstraps on game start from <c>Resources/ColourWays/AudioConfig</c>
    /// (or falls back to default paths if no asset exists yet) and survives scene loads across
    /// MainMenu, Level_1, Level_2, and Level_3.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ColourWaysAudioManager : MonoBehaviour
    {
        public static ColourWaysAudioManager Instance { get; private set; }

        [SerializeField] private AudioConfig config;

        public AudioConfig Config => config;
        public AudioTelemetry Telemetry => _telemetry;
        public float CurrentIsInLevel { get; private set; }
        public float CurrentPlayerSpeed { get; private set; }
        public float CurrentPaused { get; private set; }
        public bool IsMusicRunning { get; private set; }

        private AudioTelemetry _telemetry;
        private PlayerController _player;
        private FootstepPainter _footstepPainter;
        private readonly HashSet<string> _warnedMissingEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

#if COLOURWAYS_FMOD
        private EventInstance _musicInstance;
        private EventInstance _pauseSnapshotInstance;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            if (Instance != null) return;
            var existing = FindAnyObjectByType<ColourWaysAudioManager>();
            if (existing != null) return;

            var go = new GameObject("ColourWaysAudioManager");
            DontDestroyOnLoad(go);
            go.AddComponent<ColourWaysAudioManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (config == null)
                config = Resources.Load<AudioConfig>(AudioConfig.ResourcesPath);

            if (config == null)
            {
                config = ScriptableObject.CreateInstance<AudioConfig>();
                config.name = "RuntimeDefaultAudioConfig";
            }

            _telemetry = new AudioTelemetry(config.logEventsToConsole, config.enableCsvTelemetry);

            var overlay = GetComponent<AudioDebugOverlay>();
            if (overlay == null)
                overlay = gameObject.AddComponent<AudioDebugOverlay>();
            overlay.SetVisible(config.showDebugOverlayOnStart);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            PaintBlob.AnyCollected += OnPaintBlobCollected;
            LevelFinish.AnyLevelFinished += OnLevelFinished;
            GameState.PauseChanged += OnPauseChanged;
            MenuItemFx.AnyHovered += OnUiHover;
            MenuItemFx.AnyPressed += OnUiClick;
            ScreenTransition.TransitionStarted += OnTransitionStarted;
        }

        private void Start()
        {
            BindSceneActors(SceneManager.GetActiveScene());
            StartMusic();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            PaintBlob.AnyCollected -= OnPaintBlobCollected;
            LevelFinish.AnyLevelFinished -= OnLevelFinished;
            GameState.PauseChanged -= OnPauseChanged;
            MenuItemFx.AnyHovered -= OnUiHover;
            MenuItemFx.AnyPressed -= OnUiClick;
            ScreenTransition.TransitionStarted -= OnTransitionStarted;
            UnbindSceneActors();
        }

        private void OnDestroy()
        {
            StopMusic();
            StopPauseSnapshot();
            if (_telemetry != null)
            {
                _telemetry.LogSummary();
                _telemetry.Dispose();
                _telemetry = null;
            }
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (_player != null && !_player.IsSplatted)
            {
                float maxSpeed = Mathf.Max(_player.SprintSpeed, 0.01f);
                CurrentPlayerSpeed = Mathf.Clamp01(_player.HorizontalSpeed / maxSpeed);
            }
            else
            {
                CurrentPlayerSpeed = 0f;
            }

            SetAdaptiveParameter(config.paramPlayerSpeed, CurrentPlayerSpeed);
        }

        // ------------------------------------------------------------------ Scene & Gameplay Binding

        private void OnSceneLoaded(Scene scene, LoadSceneMode _)
        {
            BindSceneActors(scene);
        }

        private void BindSceneActors(Scene scene)
        {
            UnbindSceneActors();

            _player = FindAnyObjectByType<PlayerController>();
            if (_player != null)
            {
                _player.WallHit += OnWallHit;
                _player.Landed += OnLanded;
                _player.Jumped += OnJumped;
                _player.Respawned += OnRespawned;

                _footstepPainter = _player.GetComponent<FootstepPainter>();
                if (_footstepPainter != null)
                    _footstepPainter.Footstepped += OnFootstepped;
            }

            bool inLevel = !string.Equals(scene.name, "MainMenu", StringComparison.OrdinalIgnoreCase);
            CurrentIsInLevel = inLevel ? 1f : 0f;
            SetAdaptiveParameter(config.paramIsInLevel, CurrentIsInLevel);
            OnPauseChanged(GameState.Paused);
        }

        private void UnbindSceneActors()
        {
            if (_player != null)
            {
                _player.WallHit -= OnWallHit;
                _player.Landed -= OnLanded;
                _player.Jumped -= OnJumped;
                _player.Respawned -= OnRespawned;
                _player = null;
            }

            if (_footstepPainter != null)
            {
                _footstepPainter.Footstepped -= OnFootstepped;
                _footstepPainter = null;
            }
        }

        // ------------------------------------------------------------------ Gameplay Callbacks

        private void OnFootstepped(int foot, Vector3 position, float sizeScale)
        {
            PlayConfiguredOneShot(
                "Footstep",
#if COLOURWAYS_FMOD
                config.footstep,
#endif
                config.footstepPath,
                position,
                null,
                0f);
        }

        private void OnWallHit(Vector3 point, Vector3 wallNormal, float speed)
        {
            float impact01 = Mathf.Clamp01(speed / 6f);
            PlayConfiguredOneShot(
                "WallSplat",
#if COLOURWAYS_FMOD
                config.wallSplat,
#endif
                config.wallSplatPath,
                point,
                config.paramImpactSpeed,
                impact01);
        }

        private void OnLanded(float impactSpeed)
        {
            if (impactSpeed < 1f) return;
            float impact01 = Mathf.Clamp01(impactSpeed / 12f);
            Vector3 pos = _player != null ? _player.transform.position : GetFallbackListenerPosition();
            PlayConfiguredOneShot(
                "Land",
#if COLOURWAYS_FMOD
                config.land,
#endif
                config.landPath,
                pos,
                config.paramImpactSpeed,
                impact01);
        }

        private void OnJumped()
        {
            Vector3 pos = _player != null ? _player.transform.position : GetFallbackListenerPosition();
            PlayConfiguredOneShot(
                "Jump",
#if COLOURWAYS_FMOD
                config.jump,
#endif
                config.jumpPath,
                pos,
                null,
                0f);
        }

        private void OnRespawned()
        {
            Vector3 pos = _player != null ? _player.transform.position : GetFallbackListenerPosition();
            PlayConfiguredOneShot(
                "Respawn",
#if COLOURWAYS_FMOD
                config.respawn,
#endif
                config.respawnPath,
                pos,
                null,
                0f);
        }

        private void OnPaintBlobCollected(PaintBlob blob)
        {
            Vector3 pos = blob != null ? blob.transform.position : GetFallbackListenerPosition();
            PlayConfiguredOneShot(
                "PaintBlobCollect",
#if COLOURWAYS_FMOD
                config.paintBlobCollect,
#endif
                config.paintBlobCollectPath,
                pos,
                null,
                0f);
        }

        private void OnLevelFinished(LevelFinish finish)
        {
            Vector3 pos = finish != null ? finish.transform.position : GetFallbackListenerPosition();
            PlayConfiguredOneShot(
                "LevelComplete",
#if COLOURWAYS_FMOD
                config.levelComplete,
#endif
                config.levelCompletePath,
                pos,
                null,
                0f);
        }

        private void OnUiHover()
        {
            PlayConfiguredOneShot(
                "UIHover",
#if COLOURWAYS_FMOD
                config.uiHover,
#endif
                config.uiHoverPath,
                GetFallbackListenerPosition(),
                null,
                0f);
        }

        private void OnUiClick()
        {
            PlayConfiguredOneShot(
                "UIClick",
#if COLOURWAYS_FMOD
                config.uiClick,
#endif
                config.uiClickPath,
                GetFallbackListenerPosition(),
                null,
                0f);
        }

        private void OnTransitionStarted(string targetScene)
        {
            PlayConfiguredOneShot(
                "SceneTransition",
#if COLOURWAYS_FMOD
                config.sceneTransition,
#endif
                config.sceneTransitionPath,
                GetFallbackListenerPosition(),
                null,
                0f);
        }

        private void OnPauseChanged(bool paused)
        {
            CurrentPaused = paused ? 1f : 0f;
            SetAdaptiveParameter(config.paramPaused, CurrentPaused);

            if (paused) StartPauseSnapshot();
            else StopPauseSnapshot();
        }

        // ------------------------------------------------------------------ Public Controls (Debug / UI)

        public void TriggerTestEvent()
        {
            Vector3 pos = _player != null ? _player.transform.position : GetFallbackListenerPosition();
            PlayConfiguredOneShot(
                "WallSplat (Test)",
#if COLOURWAYS_FMOD
                config.wallSplat,
#endif
                config.wallSplatPath,
                pos,
                config.paramImpactSpeed,
                1f);
        }

        public void RestartMusic()
        {
            StopMusic();
            StartMusic();
        }

        // ------------------------------------------------------------------ FMOD Core Execution

        public void StartMusic()
        {
            Vector3 listenerPos = GetFallbackListenerPosition();
            var perf = AudioTelemetry.SamplePerformance(listenerPos);

#if COLOURWAYS_FMOD
            if (_musicInstance.isValid())
                return;

            if (!TryCreateInstance(config.mainMusic, config.mainMusicPath, "MainMusic", out _musicInstance, out string resolvedName))
            {
                _telemetry.RecordEvent(resolvedName, "FMOD", "EVENT_NOT_FOUND", listenerPos, perf.ListenerPosition, 0f, 0f, "", in perf);
                return;
            }

            RESULT startResult = _musicInstance.start();
            IsMusicRunning = startResult == RESULT.OK;
            SetAdaptiveParameter(config.paramIsInLevel, CurrentIsInLevel);
            SetAdaptiveParameter(config.paramPaused, CurrentPaused);

            _telemetry.RecordEvent(
                resolvedName,
                "FMOD",
                startResult.ToString(),
                listenerPos,
                perf.ListenerPosition,
                0f,
                0f,
                $"{config.paramIsInLevel}={CurrentIsInLevel:F0}",
                in perf);
#else
            IsMusicRunning = false;
            _telemetry.RecordEvent(
                config.mainMusicPath,
                "STUB",
                "FMOD_NOT_IMPORTED",
                listenerPos,
                perf.ListenerPosition,
                0f,
                0f,
                $"{config.paramIsInLevel}={CurrentIsInLevel:F0}",
                in perf);
#endif
        }

        public void StopMusic()
        {
#if COLOURWAYS_FMOD
            if (_musicInstance.isValid())
            {
                _musicInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                _musicInstance.release();
                _musicInstance.clearHandle();
            }
#endif
            IsMusicRunning = false;
        }

        private void StartPauseSnapshot()
        {
#if COLOURWAYS_FMOD
            if (_pauseSnapshotInstance.isValid()) return;
            if (!TryCreateInstance(config.pauseSnapshot, config.pauseSnapshotPath, "PauseSnapshot", out _pauseSnapshotInstance, out _))
                return;
            _pauseSnapshotInstance.start();
#endif
        }

        private void StopPauseSnapshot()
        {
#if COLOURWAYS_FMOD
            if (_pauseSnapshotInstance.isValid())
            {
                _pauseSnapshotInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                _pauseSnapshotInstance.release();
                _pauseSnapshotInstance.clearHandle();
            }
#endif
        }

        private void SetAdaptiveParameter(string paramName, float value)
        {
            if (string.IsNullOrEmpty(paramName)) return;
#if COLOURWAYS_FMOD
            if (!RuntimeManager.IsInitialized) return;
            // Apply to both global Studio parameter table and the active music instance so either
            // Global or Local FMOD parameters work out of the box.
            RuntimeManager.StudioSystem.setParameterByName(paramName, value);
            if (_musicInstance.isValid())
                _musicInstance.setParameterByName(paramName, value);
#endif
        }

        private void PlayConfiguredOneShot(
            string label,
#if COLOURWAYS_FMOD
            EventReference eventRef,
#endif
            string fallbackPath,
            Vector3 worldPosition,
            string paramName,
            float paramValue)
        {
            Vector3 fallbackListener = GetFallbackListenerPosition();
            var perf = AudioTelemetry.SamplePerformance(fallbackListener);
            string paramSummary = string.IsNullOrEmpty(paramName) ? "" : $"{paramName}={paramValue:F2}";

#if COLOURWAYS_FMOD
            if (!TryCreateInstance(eventRef, fallbackPath, label, out EventInstance instance, out string resolvedName))
            {
                _telemetry.RecordEvent(resolvedName, "FMOD", "EVENT_NOT_FOUND", worldPosition, perf.ListenerPosition, 0f, 0f, paramSummary, in perf);
                return;
            }

            instance.set3DAttributes(RuntimeUtils.To3DAttributes(worldPosition));
            if (!string.IsNullOrEmpty(paramName))
                instance.setParameterByName(paramName, paramValue);

            float minDist = 0f, maxDist = 0f;
            instance.getMinMaxDistance(out minDist, out maxDist);

            RESULT res = instance.start();
            instance.release();

            _telemetry.RecordEvent(
                resolvedName,
                "FMOD",
                res.ToString(),
                worldPosition,
                perf.ListenerPosition,
                minDist,
                maxDist,
                paramSummary,
                in perf);
#else
            string eventLabel = !string.IsNullOrEmpty(fallbackPath) ? fallbackPath : label;
            _telemetry.RecordEvent(
                eventLabel,
                "STUB",
                "FMOD_NOT_IMPORTED",
                worldPosition,
                perf.ListenerPosition,
                0f,
                0f,
                paramSummary,
                in perf);
#endif
        }

#if COLOURWAYS_FMOD
        private bool TryCreateInstance(
            EventReference eventRef,
            string fallbackPath,
            string label,
            out EventInstance instance,
            out string resolvedName)
        {
            instance = default;
            resolvedName = label;

            if (!RuntimeManager.IsInitialized)
                return false;

            try
            {
                if (!eventRef.IsNull)
                {
#if UNITY_EDITOR
                    resolvedName = string.IsNullOrEmpty(eventRef.Path)
                        ? (!string.IsNullOrEmpty(fallbackPath) ? fallbackPath : label)
                        : eventRef.Path;
#else
                    resolvedName = !string.IsNullOrEmpty(fallbackPath) ? fallbackPath : $"{label} ({eventRef.Guid})";
#endif
                    instance = RuntimeManager.CreateInstance(eventRef);
                    if (instance.isValid()
                        && instance.getDescription(out EventDescription desc) == RESULT.OK
                        && desc.getPath(out string runtimePath) == RESULT.OK
                        && !string.IsNullOrEmpty(runtimePath))
                    {
                        resolvedName = runtimePath;
                    }
                    return instance.isValid();
                }

                if (config.allowPathFallback && !string.IsNullOrEmpty(fallbackPath))
                {
                    resolvedName = fallbackPath;
                    instance = RuntimeManager.CreateInstance(fallbackPath);
                    return instance.isValid();
                }
            }
            catch (EventNotFoundException)
            {
                if (_warnedMissingEvents.Add(resolvedName))
                    UnityEngine.Debug.LogWarning($"[ColourWays.Audio] FMOD event not found in loaded banks: '{resolvedName}'. Create it in FMOD Studio and press F7 to build banks.");
            }
            catch (Exception ex)
            {
                if (_warnedMissingEvents.Add(resolvedName))
                    UnityEngine.Debug.LogWarning($"[ColourWays.Audio] Failed to create FMOD event '{resolvedName}': {ex.Message}");
            }

            return false;
        }
#endif

        private Vector3 GetFallbackListenerPosition()
        {
            var cam = Camera.main;
            if (cam != null) return cam.transform.position;
            if (_player != null) return _player.transform.position;
            return Vector3.zero;
        }
    }
}
