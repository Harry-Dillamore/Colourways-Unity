using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ColourWays.Audio
{
    /// <summary>
    /// Lightweight runtime HUD (toggle with F9) displaying FMOD Studio health, real-time CPU/memory
    /// telemetry, adaptive parameters, listener position, and the rolling audio event log.
    /// Works identically in the Unity Editor and packaged Windows builds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioDebugOverlay : MonoBehaviour
    {
        private bool _visible;
        private float _nextPerfSampleTime;
        private FmodPerfSnapshot _perf;
        private GUIStyle _boxStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _monoStyle;
        private Texture2D _bgTex;

        public void SetVisible(bool visible) => _visible = visible;

        private void OnDestroy()
        {
            if (_bgTex != null)
            {
                Destroy(_bgTex);
                _bgTex = null;
            }
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame)
                _visible = !_visible;

            if (_visible && Time.unscaledTime >= _nextPerfSampleTime)
            {
                _nextPerfSampleTime = Time.unscaledTime + 0.25f;
                Vector3 fallbackPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                _perf = AudioTelemetry.SamplePerformance(fallbackPos);
            }
        }

        private void EnsureStyles()
        {
            if (_boxStyle != null) return;

            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, new Color(0.06f, 0.07f, 0.1f, 0.88f));
            _bgTex.Apply();

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10),
                normal = { background = _bgTex }
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                richText = true,
                normal = { textColor = Color.white }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = true,
                normal = { textColor = new Color(0.88f, 0.91f, 0.95f) }
            };

            _monoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                richText = true,
                wordWrap = false,
                normal = { textColor = new Color(0.78f, 0.85f, 0.92f) }
            };
        }

        private void OnGUI()
        {
            if (!_visible) return;
            var mgr = ColourWaysAudioManager.Instance;
            if (mgr == null || mgr.Telemetry == null) return;

            EnsureStyles();

            const float width = 520f;
            float height = Mathf.Min(Screen.height - 24f, 460f);
            GUILayout.BeginArea(new Rect(12f, 12f, width, height), _boxStyle);

            string statusBadge = _perf.IsFmodActive
                ? "<color=#4ade80>● FMOD STUDIO ACTIVE</color>"
                : "<color=#fbbf24>● STUB MODE (Import FMOD for Unity)</color>";

            GUILayout.Label($"ColourWays Audio Telemetry  {statusBadge}  <color=#94a3b8>[F9]</color>", _headerStyle);
            GUILayout.Space(4f);

            string buildTag = Application.isEditor ? "Unity Editor" : $"Standalone Build ({Application.platform})";
            GUILayout.Label($"Runtime: <b>{buildTag}</b> | Scene: <b>{SceneManager.GetActiveScene().name}</b>", _labelStyle);

            if (_perf.IsFmodActive)
            {
                string listenerTag = _perf.HasStudioListener
                    ? "<color=#4ade80>StudioListener OK</color>"
                    : "<color=#f87171>No StudioListener in scene!</color>";

                GUILayout.Label(
                    $"Banks Loaded: <b>{_perf.LoadedBankCount}</b> | Music: <b>{(mgr.IsMusicRunning ? "Playing" : "Stopped")}</b> | {listenerTag}",
                    _labelStyle);
                GUILayout.Label(
                    $"FMOD CPU: <b>{_perf.CpuTotal:F2}%</b> (DSP {_perf.CpuDsp:F2}%, Stream {_perf.CpuStream:F2}%, Update {_perf.CpuUpdate:F2}%)",
                    _labelStyle);
                GUILayout.Label(
                    $"FMOD Memory: <b>{_perf.MemoryCurrentBytes / 1048576f:F2} MB</b> (Peak {_perf.MemoryMaxBytes / 1048576f:F2} MB) | " +
                    $"Channels: <b>{_perf.ChannelsPlaying}</b> (Real: {_perf.RealChannelsPlaying})",
                    _labelStyle);
            }
            else
            {
                GUILayout.Label("Import the official FMOD for Unity package; hooks activate automatically.", _labelStyle);
            }

            GUILayout.Label(
                $"Listener: ({_perf.ListenerPosition.x:F1}, {_perf.ListenerPosition.y:F1}, {_perf.ListenerPosition.z:F1}) | " +
                $"Params: IsInLevel={mgr.CurrentIsInLevel:F0}, Speed={mgr.CurrentPlayerSpeed:F2}, Paused={mgr.CurrentPaused:F0}",
                _labelStyle);

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Trigger Test Event", GUILayout.Height(24f)))
                mgr.TriggerTestEvent();
            if (GUILayout.Button("Restart Music", GUILayout.Height(24f)))
                mgr.RestartMusic();
            if (GUILayout.Button("Log Summary", GUILayout.Height(24f)))
                mgr.Telemetry.LogSummary();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label($"<b>Recent Events (Total Triggers: {mgr.Telemetry.TotalTriggers})</b>", _labelStyle);

            var recent = mgr.Telemetry.RecentRecords;
            int start = Mathf.Max(0, recent.Count - 9);
            for (int i = recent.Count - 1; i >= start; i--)
            {
                var r = recent[i];
                string colour = r.Result == "OK" ? "#4ade80" : (r.Mode == "STUB" ? "#fbbf24" : "#f87171");
                string rad = r.MaxDistance > 0f ? $" r={r.MinDistance:F0}-{r.MaxDistance:F0}m" : "";
                GUILayout.Label(
                    $"<color=#94a3b8>{r.TimeSinceStartup,6:F1}s</color> " +
                    $"<b>{r.EventName}</b> (#{r.TriggerCount}) " +
                    $"<color={colour}>{r.Result}</color> d={r.Distance:F1}m{rad}",
                    _monoStyle);
            }

            if (!string.IsNullOrEmpty(mgr.Telemetry.CsvFilePath))
            {
                GUILayout.Space(4f);
                GUILayout.Label($"<color=#94a3b8>CSV: {mgr.Telemetry.CsvFilePath}</color>", _monoStyle);
            }

            GUILayout.EndArea();
        }
    }
}
