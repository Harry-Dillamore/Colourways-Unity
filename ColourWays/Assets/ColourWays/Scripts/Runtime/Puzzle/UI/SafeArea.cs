using UnityEngine;

namespace ColourWays.Puzzle.UI
{
    /// <summary>
    /// Keeps this RectTransform inside the device safe area (notches, home bar), measured
    /// relative to its parent so it also works inside the portrait <see cref="MobileFrame"/>.
    /// Expects a Screen Space Overlay canvas (world corners are screen pixels).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeArea : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _lastSafe, _lastParent;
        private readonly Vector3[] _corners = new Vector3[4];

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
        }

        private void LateUpdate()
        {
            var parent = _rect.parent as RectTransform;
            if (parent == null) return;
            parent.GetWorldCorners(_corners);
            var parentRect = new Rect(_corners[0].x, _corners[0].y, _corners[2].x - _corners[0].x, _corners[2].y - _corners[0].y);
            Rect safe = Screen.safeArea;
            if (safe == _lastSafe && parentRect == _lastParent) return;
            _lastSafe = safe;
            _lastParent = parentRect;
            if (parentRect.width <= 0f || parentRect.height <= 0f) return;

            float xMin = Mathf.Max(safe.xMin, parentRect.xMin), xMax = Mathf.Min(safe.xMax, parentRect.xMax);
            float yMin = Mathf.Max(safe.yMin, parentRect.yMin), yMax = Mathf.Min(safe.yMax, parentRect.yMax);

            _rect.anchorMin = new Vector2((xMin - parentRect.xMin) / parentRect.width, (yMin - parentRect.yMin) / parentRect.height);
            _rect.anchorMax = new Vector2((xMax - parentRect.xMin) / parentRect.width, (yMax - parentRect.yMin) / parentRect.height);
            _rect.offsetMin = _rect.offsetMax = Vector2.zero;
        }
    }
}
