using UnityEngine;
using UnityEngine.EventSystems;

namespace ColourWays.Puzzle.UI
{
    /// <summary>
    /// Full-screen transparent UI layer behind the HUD that handles touches on the world:
    ///  • drag a placed instruction to move it (drop off the board / on the tray to delete),
    ///  • drag anywhere else to pan the camera (pinch / wheel zoom live in CameraRig),
    ///  • tap an Arrow to rotate it, double-tap empty space to recentre.
    /// </summary>
    public sealed class BoardInput : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private PuzzleGame game;
        [SerializeField] private DragController drag;

        private enum Mode { None, Instruction, Pan }
        private Mode _mode;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging) return;
            if (game.TryCellFromScreen(e.position, out var cell) && game.TryGetPlacement(cell, out var p))
            {
                if (p.Type == InstructionType.Arrow && game.Rotate(cell))
                    Haptics.Light();
                return;
            }
            if (game.CameraRig != null) game.CameraRig.RegisterTap();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _mode = Mode.Pan;
            if (e.button != PointerEventData.InputButton.Left) { _mode = Mode.None; return; } // right/middle pan is handled by CameraRig

            if (game.TryCellFromScreen(e.pressPosition, out var cell) && game.TryGetPlacement(cell, out var p) && game.Remove(cell))
            {
                _mode = Mode.Instruction;
                drag.Begin(p, true, e);
            }
        }

        public void OnDrag(PointerEventData e)
        {
            switch (_mode)
            {
                case Mode.Instruction: drag.Move(e); break;
                case Mode.Pan: if (game.CameraRig != null) game.CameraRig.Pan(e.delta); break;
            }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_mode == Mode.Instruction) drag.End(e);
            _mode = Mode.None;
        }
    }
}
