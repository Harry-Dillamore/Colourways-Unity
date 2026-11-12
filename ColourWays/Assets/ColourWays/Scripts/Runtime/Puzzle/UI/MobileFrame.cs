using UnityEngine;
using UnityEngine.UI;

namespace ColourWays.Puzzle.UI
{
    /// <summary>
    /// Always presents the game in a portrait "phone" layout. On screens wider than
    /// <see cref="maxAspect"/> (desktop browsers, landscape editor Game view) the world camera
    /// and UI are confined to a centred portrait column with side bars; on phones it fills the
    /// screen. UI is scaled so the 1080x1920 reference layout always fits the column.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class MobileFrame : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private CanvasScaler scaler;
        [Tooltip("RectTransform that holds all gameplay UI; resized to the portrait column.")]
        [SerializeField] private RectTransform frame;
        [SerializeField] private RectTransform leftBar;
        [SerializeField] private RectTransform rightBar;
        [Tooltip("Widest allowed width/height ratio (9:16 = 0.5625).")]
        [SerializeField] private float maxAspect = 0.5625f;
        [SerializeField] private Vector2 referenceResolution = new Vector2(1080f, 1920f);

        private Vector2Int _lastScreen;

        /// <summary>The portrait column in screen pixels.</summary>
        public Rect PixelRect { get; private set; }

        private void Awake() => Apply();

        private void Update()
        {
            if (_lastScreen.x != Screen.width || _lastScreen.y != Screen.height)
                Apply();
        }

        private void Apply()
        {
            float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            _lastScreen = new Vector2Int(Screen.width, Screen.height);

            bool framed = sw / sh > maxAspect;
            float w = framed ? sh * maxAspect : sw;
            float x = (sw - w) * 0.5f;
            PixelRect = new Rect(x, 0f, w, sh);

            if (worldCamera != null)
                worldCamera.rect = new Rect(x / sw, 0f, w / sw, 1f);

            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = Mathf.Min(w / referenceResolution.x, sh / referenceResolution.y);
            }

            if (frame != null)
            {
                frame.anchorMin = new Vector2(x / sw, 0f);
                frame.anchorMax = new Vector2((x + w) / sw, 1f);
                frame.offsetMin = frame.offsetMax = Vector2.zero;
            }

            SetBar(leftBar, framed, 0f, x / sw);
            SetBar(rightBar, framed, (x + w) / sw, 1f);
        }

        private static void SetBar(RectTransform bar, bool visible, float from, float to)
        {
            if (bar == null) return;
            bar.gameObject.SetActive(visible);
            bar.anchorMin = new Vector2(from, 0f);
            bar.anchorMax = new Vector2(to, 1f);
            bar.offsetMin = bar.offsetMax = Vector2.zero;
        }
    }
}
