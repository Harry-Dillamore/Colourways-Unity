using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ColourWays.Level
{
    /// <summary>
    /// Menu entry feel: plain outlined text that, when hovered or selected, slides right a little
    /// and fills with a paint colour (a new one each time). Pressing squashes it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuItemFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private Text label;
        [SerializeField] private Color idleColour = Color.white;
        [SerializeField] private float nudge = 22f;

        private static readonly Color[] Paint =
        {
            new Color(1f, 0.25f, 0.42f), new Color(1f, 0.55f, 0.1f), new Color(1f, 0.82f, 0.1f),
            new Color(0.2f, 0.8f, 0.35f), new Color(0.1f, 0.62f, 1f), new Color(0.55f, 0.3f, 0.95f),
        };
        private static int _next;

        /// <summary>Raised when any menu button is hovered or selected.</summary>
        public static event Action AnyHovered;

        /// <summary>Raised when any menu button is pressed.</summary>
        public static event Action AnyPressed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AnyHovered = null;
            AnyPressed = null;
            _next = 0;
        }

        private RectTransform _rect;
        private Vector2 _home;
        private Color _hoverColour;
        private float _hover, _target, _press;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _home = _rect.anchoredPosition;
            if (label == null) label = GetComponentInChildren<Text>();
            _hoverColour = Paint[0];
        }

        private void OnDisable()
        {
            _hover = _target = _press = 0f;
            if (_rect != null) { _rect.anchoredPosition = _home; _rect.localScale = Vector3.one; }
            if (label != null) label.color = idleColour;
        }

        private void Enter()
        {
            if (_target > 0f) return;
            _hoverColour = Paint[_next++ % Paint.Length];
            _target = 1f;
            AnyHovered?.Invoke();
        }

        private void Exit() => _target = 0f;

        public void OnPointerEnter(PointerEventData e) => Enter();
        public void OnPointerExit(PointerEventData e) => Exit();
        public void OnSelect(BaseEventData e) => Enter();
        public void OnDeselect(BaseEventData e) => Exit();
        public void OnPointerDown(PointerEventData e)
        {
            _press = 1f;
            AnyPressed?.Invoke();
        }
        public void OnPointerUp(PointerEventData e) { }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (Mathf.Approximately(_hover, _target) && _press <= 0f) return;
            _hover = Mathf.MoveTowards(_hover, _target, dt * 7f);
            _press = Mathf.MoveTowards(_press, 0f, dt * 5f);
            float e = 1f - (1f - _hover) * (1f - _hover);
            _rect.anchoredPosition = _home + Vector2.right * (nudge * e);
            _rect.localScale = Vector3.one * (1f - Mathf.Sin(_press * Mathf.PI) * 0.06f);
            if (label != null) label.color = Color.Lerp(idleColour, _hoverColour, e);
        }
    }
}
