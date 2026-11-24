using UnityEngine;

namespace ColourWays.Level
{
    /// <summary>Shows a short on-screen hint at the start of a level, then fades it out.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ScreenHint : MonoBehaviour
    {
        [SerializeField] private float holdSeconds = 6f;
        [SerializeField] private float fadeSeconds = 1.5f;

        private CanvasGroup _group;
        private float _time;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }

        private void Update()
        {
            _time += Time.unscaledDeltaTime;
            float fade = Mathf.Clamp01((_time - holdSeconds) / Mathf.Max(fadeSeconds, 0.01f));
            _group.alpha = 1f - fade * fade;
            if (fade >= 1f) gameObject.SetActive(false);
        }
    }
}
