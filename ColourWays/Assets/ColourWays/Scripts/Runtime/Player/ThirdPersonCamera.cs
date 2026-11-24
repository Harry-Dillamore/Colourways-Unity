using UnityEngine;
using UnityEngine.InputSystem;

namespace ColourWays.Player
{
    /// <summary>
    /// Follow camera. Port of the UE SpringArm (CameraBoom) + FollowCamera.
    ///  • Locked (default, like the original game): a fixed high angle above and behind the dude,
    ///    looking down at him (bird's-eye over the shoulder, not straight down). Moving the mouse
    ///    (or right stick) turns it around him; the pitch stays put.
    ///  • Free: mouse / right-stick orbit with spring-arm collision.
    /// Lives as a child of the player prefab for convenience and detaches itself on Awake so lag works.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [Tooltip("Pivot offset from the target's origin (UE spring arm socket).")]
        [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 0.5f, 0f);

        [Header("Locked View")]
        [Tooltip("Fixed angle above the dude (like the original game). Off = free orbit with the mouse.")]
        [SerializeField] private bool locked = true;
        [Tooltip("Degrees down from horizontal. 90 would be straight down.")]
        [SerializeField, Range(20f, 89f)] private float lockedPitch = 55f;
        [Tooltip("World yaw the camera starts looking along (0 = north / +Z).")]
        [SerializeField] private float lockedYaw;
        [SerializeField] private float lockedArmLength = 5.5f;
        [Tooltip("Turn the locked camera around the dude with the mouse / right stick.")]
        [SerializeField] private bool lockedMouseYaw = true;

        [Header("Spring Arm")]
        [SerializeField] private float armLength = 4.5f;
        [Tooltip("UE CameraLagSpeed. 0 disables lag.")]
        [SerializeField] private float lagSpeed = 8f;
        [Tooltip("UE ProbeSize (free mode only; the locked view looks over the walls).")]
        [SerializeField] private float probeRadius = 0.15f;
        [SerializeField] private LayerMask collisionMask = ~0;

        [Header("Free Look")]
        [SerializeField] private InputActionAsset controls;
        [Tooltip("Degrees per pixel of mouse movement.")]
        [SerializeField] private float mouseSensitivity = 0.12f;
        [Tooltip("Degrees per second at full stick deflection.")]
        [SerializeField] private float stickSensitivity = 200f;
        [SerializeField] private bool invertY;
        [SerializeField] private float minPitch = -60f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private float startPitch = 15f;
        [SerializeField] private bool lockCursor = true;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public Transform Target => target;
        public bool Locked => locked;

        /// <summary>When false, look input is ignored.</summary>
        public bool InputEnabled { get; set; } = true;

        private InputAction _look, _lookStick;
        private float _yawOffset;
        private Vector3 _pivot;
        private float _currentArm;

        private void Awake()
        {
            if (transform.parent != null)
                transform.SetParent(null, true);

            if (controls != null)
            {
                var map = controls.FindActionMap("Player", true);
                _look = map.FindAction("Look", true);
                _lookStick = map.FindAction("LookStick", true);
            }
        }

        private void OnEnable()
        {
            _look?.Enable();
            _lookStick?.Enable();
        }

        private void Start()
        {
            SnapBehindTarget();
            if (lockCursor) GameState.LockCursor(true);
        }

        private void OnDisable()
        {
            if (lockCursor) GameState.LockCursor(false);
        }

        /// <summary>Reset the camera onto the target with no lag.</summary>
        public void SnapBehindTarget()
        {
            if (target == null) return;
            Yaw = locked ? lockedYaw + (lockedMouseYaw ? _yawOffset : 0f) : target.eulerAngles.y;
            Pitch = locked ? lockedPitch : startPitch;
            _pivot = target.position + pivotOffset;
            _currentArm = locked ? lockedArmLength : armLength;
            Apply(0f, true);
        }

        private void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.unscaledDeltaTime;

            HandleCursor();
            Vector2 look = Vector2.zero;
            if (InputEnabled && !GameState.Paused)
            {
                if (_look != null && Cursor.lockState == CursorLockMode.Locked)
                    look += _look.ReadValue<Vector2>() * mouseSensitivity;
                if (_lookStick != null)
                    look += _lookStick.ReadValue<Vector2>() * (stickSensitivity * dt);
            }

            if (locked)
            {
                if (lockedMouseYaw) _yawOffset = Mathf.Repeat(_yawOffset + look.x, 360f);
                Yaw = lockedYaw + (lockedMouseYaw ? _yawOffset : 0f);
                Pitch = lockedPitch;
            }
            else
            {
                Yaw = Mathf.Repeat(Yaw + look.x, 360f);
                Pitch = Mathf.Clamp(Pitch + (invertY ? look.y : -look.y), minPitch, maxPitch);
            }

            Apply(Time.deltaTime, false);
        }

        private void Apply(float dt, bool snap)
        {
            Vector3 desiredPivot = target.position + pivotOffset;
            if (snap || lagSpeed <= 0f)
                _pivot = desiredPivot;
            else
                _pivot = Vector3.Lerp(_pivot, desiredPivot, 1f - Mathf.Exp(-lagSpeed * dt));

            Quaternion rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            Vector3 back = rotation * Vector3.back;

            float arm = locked ? lockedArmLength : armLength;
            if (!locked && Physics.SphereCast(_pivot, probeRadius, back, out RaycastHit hit, armLength, collisionMask, QueryTriggerInteraction.Ignore))
                arm = hit.distance;

            // Pull in instantly, ease back out (avoids popping when leaving a wall).
            _currentArm = (snap || arm < _currentArm) ? arm : Mathf.Lerp(_currentArm, arm, 1f - Mathf.Exp(-8f * dt));

            transform.SetPositionAndRotation(_pivot + back * _currentArm, rotation);
        }

        /// <summary>Re-grab the cursor on click if it got released (alt-tab etc.), unless a menu is open.</summary>
        private void HandleCursor()
        {
            if (!lockCursor || GameState.Paused || !InputEnabled) return;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
                GameState.LockCursor(true);
        }
    }
}
