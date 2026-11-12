using UnityEngine;
using UnityEngine.InputSystem;

namespace ColourWays.Puzzle
{
    /// <summary>
    /// Interactive orthographic isometric camera.
    ///  • one-finger / left-drag on empty space pans (driven by BoardInput),
    ///  • two-finger pinch, mouse wheel or trackpad zooms around the pointer,
    ///  • hard 45° rotation steps (buttons, Q / E keys) with a smooth swing,
    ///  • right/middle mouse drag pans, double-tap recentres.
    /// All layout maths use the camera's pixel rect, so it works inside a portrait "phone"
    /// frame on wide screens (see <see cref="UI.MobileFrame"/>).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [Header("Angle")]
        [SerializeField, Range(15f, 89f)] private float pitch = 32f;
        [Tooltip("Starting yaw. 45 = classic isometric diamond.")]
        [SerializeField] private float yaw = 45f;
        [SerializeField] private float rotateStep = 45f;
        [SerializeField] private float distance = 120f;

        [Header("Zoom")]
        [SerializeField] private float minSize = 3f;
        [Tooltip("Largest zoom-out, as a multiple of the size that fits the whole board.")]
        [SerializeField] private float maxSizeOfFit = 1.8f;
        [SerializeField] private float wheelSpeed = 0.0015f;

        [Header("Feel")]
        [SerializeField] private float damping = 14f;
        [SerializeField] private float rotateDamping = 10f;
        [SerializeField] private float panMargin = 8f;
        [SerializeField] private float fitPadding = 0.8f;

        [Header("UI insets")]
        [SerializeField] private RectTransform topBar;
        [SerializeField] private RectTransform bottomBar;

        public Camera Camera => _camera != null ? _camera : (_camera = GetComponent<Camera>());
        public bool IsPinching { get; private set; }
        /// <summary>Current (animated) yaw in degrees.</summary>
        public float Yaw => _yaw;

        private Camera _camera;
        private Bounds _board;
        private bool _hasBoard;
        private Vector3 _focus, _focusTarget;
        private float _size, _sizeTarget, _fitSize = 8f;
        private float _yaw, _yawTarget;
        private float _lastTapTime;
        private float _pinchDistance;
        private Vector2 _pinchMid;
        private Rect _lastPixelRect;
        private readonly Vector3[] _corners = new Vector3[4];

        private Quaternion RotationFor(float y) => Quaternion.Euler(pitch, y, 0f);

        private void Awake()
        {
            _yaw = _yawTarget = yaw;
        }

        /// <summary>Frame the board (zoomed to fit) and allow panning around it.</summary>
        public void Fit(Bounds board, bool snap = true)
        {
            _board = board;
            _hasBoard = true;
            _fitSize = ComputeFitSize(board);
            _sizeTarget = _fitSize;
            _focusTarget = new Vector3(board.center.x, 0f, board.center.z);
            if (snap)
            {
                _size = _sizeTarget;
                _focus = _focusTarget;
            }
            _lastPixelRect = Camera.pixelRect;
            Apply();
        }

        public void Recenter()
        {
            if (!_hasBoard) return;
            _fitSize = ComputeFitSize(_board);
            _sizeTarget = _fitSize;
            _focusTarget = new Vector3(_board.center.x, 0f, _board.center.z);
        }

        /// <summary>Rotate by whole 45° steps (positive = clockwise seen from above).</summary>
        public void Rotate(int steps)
        {
            _yawTarget += rotateStep * steps;
            Haptics.Light();
        }

        /// <summary>Pan by a screen-space pointer delta (pixels): the world follows the finger.</summary>
        public void Pan(Vector2 screenDelta)
        {
            if (IsPinching) return;
            float unitsPerPixel = 2f * _size / Mathf.Max(1f, Camera.pixelHeight);
            Vector3 right = transform.right; right.y = 0f; right.Normalize();
            Vector3 up = transform.up; up.y = 0f; up.Normalize();
            float upScale = 1f / Mathf.Max(0.2f, Mathf.Sin(pitch * Mathf.Deg2Rad));
            Vector3 move = -(right * screenDelta.x + up * screenDelta.y * upScale) * unitsPerPixel;
            _focusTarget = ClampFocus(_focusTarget + move);
            _focus = ClampFocus(_focus + move); // direct manipulation: no lag while dragging
        }

        /// <summary>Multiply the zoom (size) keeping the world point under <paramref name="screenPoint"/> fixed.</summary>
        public void ZoomAt(float factor, Vector2 screenPoint)
        {
            if (!_hasBoard) return;
            Vector3 before = GroundPoint(screenPoint);
            _size = _sizeTarget = Mathf.Clamp(_sizeTarget * factor, minSize, _fitSize * maxSizeOfFit);
            Apply();
            Vector3 after = GroundPoint(screenPoint);
            _focus = _focusTarget = ClampFocus(_focus + (before - after));
            Apply();
        }

        /// <summary>Call on a tap on empty space; two quick taps recentre.</summary>
        public void RegisterTap()
        {
            if (Time.unscaledTime - _lastTapTime < 0.3f)
            {
                Recenter();
                _lastTapTime = 0f;
            }
            else _lastTapTime = Time.unscaledTime;
        }

        private void Update()
        {
            if (!_hasBoard) return;
            HandlePinch();
            HandleMouseAndKeys();
        }

        private void LateUpdate()
        {
            if (!_hasBoard) return;
            if (Camera.pixelRect != _lastPixelRect)
            {
                // Orientation / browser resize / frame change: keep the same relative zoom.
                float relative = _sizeTarget / Mathf.Max(_fitSize, 0.01f);
                _lastPixelRect = Camera.pixelRect;
                _fitSize = ComputeFitSize(_board);
                _sizeTarget = Mathf.Clamp(_fitSize * relative, minSize, _fitSize * maxSizeOfFit);
            }

            float dt = Time.unscaledDeltaTime;
            float t = 1f - Mathf.Exp(-damping * dt);
            _size = Mathf.Lerp(_size, _sizeTarget, t);
            _focus = Vector3.Lerp(_focus, _focusTarget, t);
            _yaw = Mathf.Lerp(_yaw, _yawTarget, 1f - Mathf.Exp(-rotateDamping * dt));
            if (Mathf.Abs(_yaw - _yawTarget) < 0.01f) _yaw = _yawTarget;
            Apply();
        }

        private void HandlePinch()
        {
            var touch = Touchscreen.current;
            if (touch == null) { IsPinching = false; return; }

            int count = 0;
            Vector2 a = default, b = default;
            foreach (var t in touch.touches)
            {
                if (!t.press.isPressed) continue;
                if (count == 0) a = t.position.ReadValue();
                else if (count == 1) b = t.position.ReadValue();
                count++;
            }

            if (count < 2)
            {
                IsPinching = false;
                return;
            }

            float dist = Vector2.Distance(a, b);
            Vector2 mid = (a + b) * 0.5f;
            if (!IsPinching)
            {
                IsPinching = true;
                _pinchDistance = dist;
                _pinchMid = mid;
                return;
            }

            if (dist > 1f && _pinchDistance > 1f)
                ZoomAt(_pinchDistance / dist, mid);
            IsPinching = false; // allow the two-finger pan
            Pan(mid - _pinchMid);
            IsPinching = true;
            _pinchDistance = dist;
            _pinchMid = mid;
        }

        private void HandleMouseAndKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.qKey.wasPressedThisFrame) Rotate(-1);
                if (keyboard.eKey.wasPressedThisFrame) Rotate(1);
            }

            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pos = mouse.position.ReadValue();
            if (!Camera.pixelRect.Contains(pos) || OverBars(pos)) return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                ZoomAt(Mathf.Exp(-scroll * wheelSpeed), pos);

            if (mouse.rightButton.isPressed || mouse.middleButton.isPressed)
                Pan(mouse.delta.ReadValue());
        }

        private bool OverBars(Vector2 screen) =>
            (topBar != null && RectTransformUtility.RectangleContainsScreenPoint(topBar, screen, null)) ||
            (bottomBar != null && RectTransformUtility.RectangleContainsScreenPoint(bottomBar, screen, null));

        private Vector3 ClampFocus(Vector3 f)
        {
            if (!_hasBoard) return f;
            Vector3 min = _board.min - new Vector3(panMargin, 0f, panMargin);
            Vector3 max = _board.max + new Vector3(panMargin, 0f, panMargin);
            return new Vector3(Mathf.Clamp(f.x, min.x, max.x), 0f, Mathf.Clamp(f.z, min.z, max.z));
        }

        private Vector3 GroundPoint(Vector2 screen)
        {
            var ray = Camera.ScreenPointToRay(screen);
            return new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float enter) ? ray.GetPoint(enter) : _focus;
        }

        // ------------------------------------------------------------------ Layout (camera pixel rect)

        private void Insets(out float top, out float bottom)
        {
            Rect r = Camera.pixelRect;
            top = bottom = 0f;
            if (topBar != null && topBar.gameObject.activeInHierarchy)
            {
                topBar.GetWorldCorners(_corners);
                top = Mathf.Clamp(r.yMax - _corners[0].y, 0f, r.height);
            }
            if (bottomBar != null && bottomBar.gameObject.activeInHierarchy)
            {
                bottomBar.GetWorldCorners(_corners);
                bottom = Mathf.Clamp(_corners[1].y - r.y, 0f, r.height);
            }
        }

        private float ComputeFitSize(Bounds b)
        {
            Rect r = Camera.pixelRect;
            float w = Mathf.Max(1f, r.width), h = Mathf.Max(1f, r.height);
            Insets(out float top, out float bottom);
            float availH = Mathf.Max(h - top - bottom, h * 0.3f);

            // Fit for the worst case over all 45° rotations so rotating never clips the board.
            float best = 0f;
            for (int i = 0; i < 8; i++)
            {
                Quaternion inverse = Quaternion.Inverse(RotationFor(_yawTarget + i * 45f));
                Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
                for (int c = 0; c < 8; c++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 local = inverse * corner;
                    min = Vector2.Min(min, local);
                    max = Vector2.Max(max, local);
                }
                float cw = max.x - min.x + fitPadding * 2f;
                float ch = max.y - min.y + fitPadding * 2f;
                best = Mathf.Max(best, Mathf.Max(ch * 0.5f * h / availH, cw * 0.5f * h / w));
            }
            return best;
        }

        private void Apply()
        {
            var cam = Camera;
            float h = Mathf.Max(1f, cam.pixelHeight);
            Insets(out float top, out float bottom);
            float availH = Mathf.Max(h - top - bottom, h * 0.3f);
            float fraction = (bottom + availH * 0.5f) / h; // where the focus sits in the view

            Quaternion rot = RotationFor(_yaw);
            Vector3 forward = rot * Vector3.forward;
            Vector3 up = rot * Vector3.up;

            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(0.1f, _size);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = distance * 2f;
            transform.SetPositionAndRotation(_focus - forward * distance - up * ((fraction - 0.5f) * 2f * _size), rot);
        }
    }
}
