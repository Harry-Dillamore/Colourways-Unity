using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ColourWays.Level
{
    /// <summary>The on-screen line used by <see cref="TutorialPrompt"/>: fades in, holds, fades out.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class PromptUI : MonoBehaviour
    {
        [SerializeField] private Text text;

        private static PromptUI _instance;
        private CanvasGroup _group;
        private Coroutine _routine;

        private void Awake()
        {
            _instance = this;
            _group = GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static void Show(string message, float seconds)
        {
            if (_instance == null) return;
            if (_instance._routine != null) _instance.StopCoroutine(_instance._routine);
            _instance._routine = _instance.StartCoroutine(_instance.Run(message, seconds));
        }

        private IEnumerator Run(string message, float seconds)
        {
            // Fade out whatever is showing, swap the text, fade back in with a little rise.
            var rect = (RectTransform)transform;
            Vector2 home = rect.anchoredPosition;
            for (float a = _group.alpha; a > 0f; a -= Time.unscaledDeltaTime / 0.15f) { _group.alpha = a; yield return null; }
            if (text != null) text.text = message;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.3f)
            {
                float e = 1f - (1f - t) * (1f - t);
                _group.alpha = e;
                rect.anchoredPosition = home + Vector2.down * (1f - e) * 20f;
                yield return null;
            }
            _group.alpha = 1f;
            rect.anchoredPosition = home;
            yield return new WaitForSecondsRealtime(seconds);
            for (float a = 1f; a > 0f; a -= Time.unscaledDeltaTime / 0.5f) { _group.alpha = a; yield return null; }
            _group.alpha = 0f;
            _routine = null;
        }
    }
}
