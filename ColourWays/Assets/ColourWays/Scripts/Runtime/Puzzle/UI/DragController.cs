using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ColourWays.Puzzle.UI
{
    /// <summary>
    /// Drag-and-drop of instructions from the tray (or the board) onto grid cells.
    ///  • Pick-up: the ghost springs out of the key and trails the finger with a little weight,
    ///    tilting with drag speed.
    ///  • Over the board: the ghost shrinks away and the real tile glides between cells instead.
    ///  • Drop: the tile drops onto the board; cancel flies the ghost back to the tray; dragging a
    ///    placed tile off the board spins it away (deleted).
    /// Haptic ticks on pick-up, each new cell and on drop.
    /// </summary>
    public sealed class DragController : MonoBehaviour
    {
        [SerializeField] private PuzzleGame game;
        [SerializeField] private GridOverlay grid;
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform ghost;
        [SerializeField] private Image ghostBackground;
        [SerializeField] private Image ghostIcon;
        [Tooltip("Dropping here cancels (or deletes, when dragged from the board).")]
        [SerializeField] private RectTransform trayArea;
        [Tooltip("Target offset above the finger on touch screens (canvas units).")]
        [SerializeField] private float touchLift = 70f;

        [Header("Feel")]
        [SerializeField] private float follow = 28f;
        [SerializeField] private float springStiffness = 380f;
        [SerializeField] private float springDamping = 22f;
        [SerializeField] private float maxTilt = 16f;

        public bool IsDragging { get; private set; }

        private enum GhostMode { Hidden, Dragging, Returning, Deleting }

        private Placement _placement;
        private InstructionSet.Entry _entry;
        private bool _fromBoard;
        private bool _hasHover;
        private Vector2Int _hover;
        private bool _hoverValid;
        private bool _touch;

        private GhostMode _mode = GhostMode.Hidden;
        private Vector2 _ghostPos, _ghostTarget, _origin, _lastPointer, _velocity;
        private float _scale, _scaleTarget, _scaleVelocity, _tilt, _spin;

        private void Awake()
        {
            if (ghost != null) ghost.gameObject.SetActive(false);
        }

        public void Begin(Placement placement, bool fromBoard, PointerEventData e)
        {
            _entry = game.Instructions.Get(placement.Type);
            if (_entry == null) return;

            IsDragging = true;
            _placement = placement;
            _fromBoard = fromBoard;
            _hasHover = false;
            _touch = e is ExtendedPointerEventData ext ? ext.pointerType == UIPointerType.Touch : false;

            // Spring out of wherever the drag started (the tray key, or the finger on the board).
            _origin = e.pointerDrag != null && !fromBoard ? (Vector2)e.pointerDrag.transform.position : e.pressPosition;
            _ghostPos = _origin;
            _lastPointer = e.position;
            _velocity = Vector2.zero;
            _scale = fromBoard ? 0.8f : 0.55f;
            _scaleVelocity = 6f; // a little pop
            _scaleTarget = 1f;
            _tilt = _spin = 0f;
            _mode = GhostMode.Dragging;

            if (ghost != null)
            {
                ghost.gameObject.SetActive(true);
                ghost.SetAsLastSibling();
                if (ghostBackground != null) ghostBackground.color = _entry.colour; // same look as the board tile
                if (ghostIcon != null)
                {
                    ghostIcon.sprite = _entry.icon;
                    ghostIcon.color = Color.white;
                    ghostIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, ScreenAngle(placement));
                }
            }

            grid.SetVisible(true);
            Haptics.Light();
            Move(e);
        }

        public void Move(PointerEventData e)
        {
            if (!IsDragging) return;
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            Vector2 target = e.position + (_touch ? Vector2.up * touchLift * scale : Vector2.zero);

            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-3f);
            _velocity = Vector2.Lerp(_velocity, (e.position - _lastPointer) / dt, 0.35f);
            _lastPointer = e.position;

            bool overTray = trayArea != null && RectTransformUtility.RectangleContainsScreenPoint(trayArea, e.position, null);
            Vector2Int cell = default;
            bool onGrid = !overTray && game.TryCellFromScreen(target, out cell);

            if (onGrid)
            {
                bool valid = game.CanPlace(cell);
                if (!_hasHover || cell != _hover || valid != _hoverValid)
                {
                    if (!_hasHover || cell != _hover) Haptics.Tick();
                    game.Builder.ShowPreview(cell, _placement, _entry, valid);
                    grid.SetHover(cell, valid);
                }
                _hasHover = true;
                _hover = cell;
                _hoverValid = valid;
            }
            else if (_hasHover)
            {
                _hasHover = false;
                game.Builder.HidePreview();
                grid.ClearHover();
            }

            // Over the board the world tile takes over; the UI ghost shrinks away.
            _scaleTarget = onGrid ? 0f : 1f;
            _ghostTarget = e.position + Vector2.up * 40f * scale;
        }

        public void End(PointerEventData e)
        {
            if (!IsDragging) return;
            Move(e);
            IsDragging = false;
            grid.ClearHover();
            grid.SetVisible(false);

            if (_hasHover && _hoverValid && game.Place(_hover, _placement))
            {
                game.Builder.HidePreviewImmediate(); // the placed tile drops in where the preview was
                Haptics.Medium();
                _mode = GhostMode.Hidden;
                _scaleTarget = 0f;
            }
            else
            {
                game.Builder.HidePreview();
                if (_fromBoard)
                {
                    Haptics.Remove(); // dragged off the board: deleted
                    _mode = GhostMode.Deleting;
                    _scaleTarget = 0f;
                    _scale = Mathf.Max(_scale, 0.6f);
                }
                else
                {
                    _mode = GhostMode.Returning; // cancelled: fly back to the tray
                    _scaleTarget = 0.5f;
                }
            }
            _hasHover = false;
        }

        private void Update()
        {
            if (ghost == null || !ghost.gameObject.activeSelf) return;
            float dt = Time.unscaledDeltaTime;

            // Spring the scale (overshoots a touch for a lively pop).
            _scaleVelocity += (_scaleTarget - _scale) * springStiffness * dt;
            _scaleVelocity *= Mathf.Exp(-springDamping * dt);
            _scale = Mathf.Max(0f, _scale + _scaleVelocity * dt);

            switch (_mode)
            {
                case GhostMode.Dragging:
                    _ghostPos = Vector2.Lerp(_ghostPos, _ghostTarget, 1f - Mathf.Exp(-follow * dt));
                    _tilt = Mathf.Lerp(_tilt, Mathf.Clamp(-_velocity.x * 0.012f, -maxTilt, maxTilt), 1f - Mathf.Exp(-14f * dt));
                    break;
                case GhostMode.Returning:
                    _ghostPos = Vector2.Lerp(_ghostPos, _origin, 1f - Mathf.Exp(-16f * dt));
                    _tilt = Mathf.Lerp(_tilt, 0f, 1f - Mathf.Exp(-14f * dt));
                    if ((_ghostPos - _origin).sqrMagnitude < 64f) _scaleTarget = 0f;
                    break;
                case GhostMode.Deleting:
                    _spin += dt * 900f;
                    _ghostPos += Vector2.down * dt * 120f;
                    break;
            }
            _velocity = Vector2.Lerp(_velocity, Vector2.zero, 1f - Mathf.Exp(-6f * dt));

            ghost.position = _ghostPos;
            ghost.localScale = Vector3.one * _scale;
            ghost.localRotation = Quaternion.Euler(0f, 0f, _tilt + _spin);

            if (_mode != GhostMode.Dragging && _scale < 0.02f && Mathf.Abs(_scaleVelocity) < 0.5f)
            {
                ghost.gameObject.SetActive(false);
                _mode = GhostMode.Hidden;
            }
        }

        /// <summary>Screen-space angle of an arrow so the ghost matches how it'll look on the board.</summary>
        private float ScreenAngle(Placement p)
        {
            if (p.Type != InstructionType.Arrow || game.Camera == null) return 0f;
            var cam = game.Camera;
            Vector3 origin = game.Builder.transform.position;
            Vector3 a = cam.WorldToScreenPoint(origin);
            Vector3 b = cam.WorldToScreenPoint(origin + p.Dir.World());
            Vector2 d = b - a;
            return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;
        }
    }
}
