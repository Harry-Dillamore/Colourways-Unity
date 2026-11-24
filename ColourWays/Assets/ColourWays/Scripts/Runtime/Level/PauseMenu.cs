using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ColourWays.Level
{
    /// <summary>
    /// Esc (or Start on a gamepad) pauses: resume, restart the level, toggle dark mode, or go back
    /// to the main menu. Freezes time and frees the cursor while open.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField] private CanvasGroup sheet;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button themeButton;
        [SerializeField] private Text themeLabel;
        [SerializeField] private Button menuButton;
        [SerializeField] private string menuScene = "MainMenu";

        private Coroutine _fade;

        private void Awake()
        {
            if (sheet != null) { sheet.alpha = 0f; sheet.gameObject.SetActive(false); }
            Bind(resumeButton, () => SetOpen(false));
            Bind(restartButton, () => { SetOpen(false); ScreenTransition.LoadScene(SceneManager.GetActiveScene().name); });
            Bind(themeButton, () => { Theme.Toggle(); RefreshTheme(); });
            Bind(menuButton, () => { SetOpen(false); ScreenTransition.LoadScene(menuScene); });
            RefreshTheme();
        }

        private void Start()
        {
            GameState.SetPaused(false);
        }

        private void OnDestroy()
        {
            if (GameState.Paused) GameState.SetPaused(false);
        }

        private static void Bind(Button b, UnityEngine.Events.UnityAction a)
        {
            if (b != null) b.onClick.AddListener(a);
        }

        private void RefreshTheme()
        {
            if (themeLabel != null) themeLabel.text = Theme.IsDark ? "Light mode" : "Dark mode";
        }

        private void Update()
        {
            if (ScreenTransition.Busy) return;
            bool pressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                           (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
            if (pressed) SetOpen(!GameState.Paused);
        }

        public void SetOpen(bool open)
        {
            if (open == GameState.Paused && (sheet == null || sheet.gameObject.activeSelf == open)) return;
            GameState.SetPaused(open);
            if (sheet == null) return;
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(Fade(open));
        }

        private IEnumerator Fade(bool open)
        {
            sheet.gameObject.SetActive(true);
            sheet.blocksRaycasts = sheet.interactable = open;
            var panel = sheet.transform.childCount > 1 ? sheet.transform.GetChild(1) as RectTransform : null;
            float from = sheet.alpha, to = open ? 1f : 0f;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.2f)
            {
                float e = 1f - (1f - t) * (1f - t) * (1f - t);
                sheet.alpha = Mathf.Lerp(from, to, e);
                if (panel != null) panel.localScale = Vector3.one * (open ? Mathf.Lerp(0.92f, 1f, e) : Mathf.Lerp(1f, 0.95f, e));
                yield return null;
            }
            sheet.alpha = to;
            if (panel != null) panel.localScale = Vector3.one;
            if (!open) sheet.gameObject.SetActive(false);
            _fade = null;
        }
    }
}
