using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ColourWays.Puzzle.UI
{
    /// <summary>
    /// NYT-Games-style HUD: header (title, date, help), stat strip, instruction tray, action row,
    /// a toast line, a help sheet and a results sheet with share.
    /// </summary>
    public sealed class PuzzleHUD : MonoBehaviour
    {
        [SerializeField] private PuzzleGame game;
        [SerializeField] private DragController drag;

        [Header("Header")]
        [SerializeField] private Text subtitle;
        [SerializeField] private Button helpButton;
        [SerializeField] private Button recenterButton;
        [SerializeField] private Button rotateLeftButton;
        [SerializeField] private Button rotateRightButton;

        [Header("Stats")]
        [SerializeField] private Text scoreValue;
        [SerializeField] private Text parValue;
        [SerializeField] private Text timeValue;
        [SerializeField] private Text homeValue;

        [Header("Tray")]
        [SerializeField] private RectTransform tray;
        [SerializeField] private InstructionCard cardPrefab;

        [Header("Actions")]
        [SerializeField] private Button runButton;
        [SerializeField] private Text runLabel;
        [SerializeField] private Image runBackground;
        [Tooltip("Accent strip on the Run button; its colour shows the run state.")]
        [SerializeField] private Image runAccent;
        [SerializeField] private Color runColour = Color.black;
        [SerializeField] private Color stopColour = Color.black;
        [SerializeField] private Color runAccentColour = new Color(0f, 0.8f, 0.36f);
        [SerializeField] private Color stopAccentColour = new Color(1f, 0.16f, 0.5f);
        [SerializeField] private Button clearButton;
        [SerializeField] private Button newButton;

        [Header("Toast")]
        [SerializeField] private Text toast;
        [SerializeField] private CanvasGroup toastGroup;

        [Header("Results")]
        [SerializeField] private CanvasGroup resultsSheet;
        [SerializeField] private Text resultsHeadline;
        [SerializeField] private Text resultsScore;
        [SerializeField] private Text resultsPar;
        [SerializeField] private Text resultsInstructions;
        [SerializeField] private Text resultsTime;
        [SerializeField] private Text resultsBest;
        [SerializeField] private Button shareButton;
        [SerializeField] private Text shareLabel;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button replayButton;
        [SerializeField] private Button dailyButton;

        [Header("Help")]
        [SerializeField] private CanvasGroup helpSheet;
        [SerializeField] private Button helpCloseButton;
        [SerializeField] private Button helpDailyButton;
        [SerializeField] private Button hapticsButton;
        [SerializeField] private Text hapticsLabel;

        private const string SeenHelpKey = "cw_seen_help";
        private Coroutine _toastRoutine;

        private void Awake()
        {
            Show(resultsSheet, false);
            Show(helpSheet, false);
            if (toastGroup != null) toastGroup.alpha = 0f;

            Bind(runButton, () => game.ToggleRun());
            Bind(clearButton, () =>
            {
                if (game.Placements.Count == 0) return;
                game.ClearInstructions();
                Haptics.Remove();
                Toast("Board cleared");
            });
            Bind(newButton, () => { game.NewRandomLevel(); Haptics.Light(); });
            Bind(recenterButton, () => { if (game.CameraRig != null) game.CameraRig.Recenter(); Haptics.Tick(); });
            Bind(rotateLeftButton, () => { if (game.CameraRig != null) game.CameraRig.Rotate(-1); });
            Bind(rotateRightButton, () => { if (game.CameraRig != null) game.CameraRig.Rotate(1); });
            Bind(helpButton, () => ShowSheet(helpSheet, true));
            Bind(helpCloseButton, () => ShowSheet(helpSheet, false));
            Bind(helpDailyButton, () => { ShowSheet(helpSheet, false); game.LoadDaily(); });
            Bind(hapticsButton, () => { Haptics.Enabled = !Haptics.Enabled; RefreshHaptics(); Haptics.Medium(); });
            Bind(shareButton, Share);
            Bind(nextButton, () => { ShowSheet(resultsSheet, false); game.NewRandomLevel(); });
            Bind(replayButton, () => { ShowSheet(resultsSheet, false); game.Replay(); });
            Bind(dailyButton, () => { ShowSheet(resultsSheet, false); game.LoadDaily(); });

            game.LevelLoaded += OnLevelLoaded;
            game.Completed += OnCompleted;
            game.RunStateChanged += RefreshRunButton;
        }

        private void OnDestroy()
        {
            if (game == null) return;
            game.LevelLoaded -= OnLevelLoaded;
            game.Completed -= OnCompleted;
            game.RunStateChanged -= RefreshRunButton;
        }

        private void Start()
        {
            BuildTray();
            RefreshRunButton();
            RefreshHaptics();
            if (PlayerPrefs.GetInt(SeenHelpKey, 0) == 0)
            {
                PlayerPrefs.SetInt(SeenHelpKey, 1);
                ShowSheet(helpSheet, true);
            }
        }

        private static void Bind(Button b, UnityEngine.Events.UnityAction a)
        {
            if (b != null) b.onClick.AddListener(a);
        }

        private void BuildTray()
        {
            if (tray == null || cardPrefab == null || game.Instructions == null) return;
            for (int i = tray.childCount - 1; i >= 0; i--)
                Destroy(tray.GetChild(i).gameObject);
            foreach (var entry in game.Instructions.Entries)
            {
                if (!entry.enabled) continue;
                Instantiate(cardPrefab, tray).Setup(entry, drag);
            }
        }

        private void OnLevelLoaded()
        {
            Show(resultsSheet, false);
            if (subtitle != null) subtitle.text = game.IsDaily ? game.Title.Replace("Daily · ", "") : $"Practice  #{game.Level.Seed % 100000:00000}";
            RefreshRunButton();
            Toast(game.IsDaily ? "Today's puzzle. Drag an instruction onto the board." : "New practice puzzle");
        }

        private void RefreshRunButton()
        {
            if (runLabel != null) runLabel.text = game.IsRunning ? "Stop" : game.Runs > 0 ? "Run again" : "Run";
            if (runBackground != null) runBackground.color = game.IsRunning ? stopColour : runColour;
            if (runAccent != null) runAccent.color = game.IsRunning ? stopAccentColour : runAccentColour;
            if (runButton != null) runButton.interactable = !game.IsComplete;
        }

        private void RefreshHaptics()
        {
            if (hapticsLabel != null) hapticsLabel.text = Haptics.Enabled ? "Haptics: On" : "Haptics: Off";
        }

        private void Update()
        {
            if (game.Level == null) return;
            if (scoreValue != null) scoreValue.text = game.Score.ToString();
            if (parValue != null) parValue.text = game.Par >= 0 ? game.Par.ToString() : "–";
            if (timeValue != null) timeValue.text = game.RunTime.ToString("0.0") + "s";
            if (homeValue != null) homeValue.text = $"{game.Saved}/{game.Required}";
        }

        private void OnCompleted()
        {
            int diff = game.Par >= 0 ? game.Score - game.Par : 0;
            if (resultsHeadline != null)
                resultsHeadline.text = game.Par < 0 ? "Solved!" : diff < 0 ? "Under par!" : diff == 0 ? "Right on par" : diff <= 3 ? "Splendid!" : "Nicely done";
            if (resultsScore != null) resultsScore.text = game.Score.ToString();
            if (resultsPar != null) resultsPar.text = game.Par >= 0 ? game.Par.ToString() : "–";
            if (resultsInstructions != null) resultsInstructions.text = game.Placements.Count.ToString();
            if (resultsTime != null) resultsTime.text = game.RunTime.ToString("0.0") + "s";
            if (resultsBest != null)
            {
                int best = game.BestScore;
                resultsBest.text = best >= 0 ? $"Your best on this puzzle: {best}" : "";
            }
            if (shareLabel != null) shareLabel.text = "Share";
            if (dailyButton != null) dailyButton.gameObject.SetActive(!game.IsDaily);
            StartCoroutine(ShowResultsAfter(0.8f));
        }

        private IEnumerator ShowResultsAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            ShowSheet(resultsSheet, true);
        }

        private void Share()
        {
            Clipboard.Copy(game.ShareText());
            if (shareLabel != null) shareLabel.text = "Copied!";
            Haptics.Light();
        }

        // ------------------------------------------------------------------ Sheets & toast

        private void ShowSheet(CanvasGroup sheet, bool visible)
        {
            if (sheet == null) return;
            StartCoroutine(Fade(sheet, visible));
        }

        private static void Show(CanvasGroup g, bool visible)
        {
            if (g == null) return;
            g.alpha = visible ? 1f : 0f;
            g.blocksRaycasts = visible;
            g.interactable = visible;
            g.gameObject.SetActive(visible);
        }

        private static IEnumerator Fade(CanvasGroup g, bool visible)
        {
            g.gameObject.SetActive(true);
            g.blocksRaycasts = visible;
            g.interactable = visible;
            var panel = g.transform.childCount > 1 ? g.transform.GetChild(1) as RectTransform : null;
            float from = g.alpha, to = visible ? 1f : 0f;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.2f)
            {
                float e = 1f - (1f - t) * (1f - t);
                g.alpha = Mathf.Lerp(from, to, e);
                if (panel != null) panel.localScale = Vector3.one * Mathf.Lerp(visible ? 0.96f : 1f, visible ? 1f : 0.96f, e);
                yield return null;
            }
            g.alpha = to;
            if (panel != null) panel.localScale = Vector3.one;
            if (!visible) g.gameObject.SetActive(false);
        }

        public void Toast(string message)
        {
            if (toast == null || toastGroup == null) return;
            toast.text = message;
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastRoutine = StartCoroutine(ToastRoutine());
        }

        private IEnumerator ToastRoutine()
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.15f) { toastGroup.alpha = t; yield return null; }
            toastGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(2.4f);
            for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.4f) { toastGroup.alpha = t; yield return null; }
            toastGroup.alpha = 0f;
        }
    }
}
