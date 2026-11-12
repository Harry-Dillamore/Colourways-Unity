using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ColourWays.Puzzle.UI
{
    /// <summary>
    /// A key in the instruction tray: soft rounded key with a coloured "station" disc holding a
    /// white glyph (Mini Metro style). Squishes when pressed. Drag it onto the board to place it.
    /// </summary>
    public sealed class InstructionCard : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Image background;
        [Tooltip("Coloured disc behind the glyph (the instruction's accent colour).")]
        [SerializeField] private Image accent;
        [SerializeField] private Image icon;
        [SerializeField] private Text nameLabel;
        [SerializeField] private Text costLabel;
        [SerializeField] private Color keyColour = new Color(0.91f, 0.9f, 0.87f);
        [SerializeField] private Color keyPressedColour = new Color(0.84f, 0.83f, 0.8f);
        [SerializeField] private Color glyphColour = Color.white;

        private InstructionSet.Entry _entry;
        private DragController _drag;
        private Vector3 _scale = Vector3.one;
        private float _press, _pressTarget, _pressVelocity;

        public void Setup(InstructionSet.Entry entry, DragController drag)
        {
            _entry = entry;
            _drag = drag;
            _scale = transform.localScale;
            if (icon != null) { icon.sprite = entry.icon; icon.color = glyphColour; }
            if (accent != null) accent.color = entry.colour;
            if (nameLabel != null) nameLabel.text = entry.displayName;
            if (costLabel != null) costLabel.text = entry.cost == 1 ? "1 pt" : $"{entry.cost} pts";
            if (background != null) background.color = keyColour;
        }

        public void OnPointerDown(PointerEventData e)
        {
            _pressTarget = 1f;
            if (background != null) background.color = keyPressedColour;
            Haptics.Tick();
        }

        public void OnPointerUp(PointerEventData e) => Release();

        private void Release()
        {
            _pressTarget = 0f;
            if (background != null) background.color = keyColour;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            Release();
            if (_entry != null) _drag.Begin(new Placement(_entry.type, Dir.North), false, e);
        }

        public void OnDrag(PointerEventData e) => _drag.Move(e);

        public void OnEndDrag(PointerEventData e) => _drag.End(e);

        private void Update()
        {
            // Springy squish so keys feel soft under the finger.
            float dt = Time.unscaledDeltaTime;
            _pressVelocity += (_pressTarget - _press) * 500f * dt;
            _pressVelocity *= Mathf.Exp(-24f * dt);
            _press += _pressVelocity * dt;
            transform.localScale = _scale * (1f - 0.07f * _press);
        }
    }
}
