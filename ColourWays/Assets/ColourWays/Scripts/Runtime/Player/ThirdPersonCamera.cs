using UnityEngine;
using UnityEngine.InputSystem;

namespace ColourWays.Player
{
    /// <summary>
    /// Orbiting third-person camera. Port of the UE SpringArm (CameraBoom) + FollowCamera:
    /// arm length, camera lag and collision probe. Lives as a child of the player prefab for
    /// convenience and detaches itself on Awake so lag works.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [Tooltip("Pivot offset from the target's origin (UE spring arm socket).")]
        [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 0.9f, 0f);

        [Header("Spring Arm")]
        [SerializeField] private float armLength = 4.5f;
        [Tooltip("UE CameraLagSpeed. 0 disables lag.")]
        [SerializeField] private float lagSpeed = 10f;
        [Tooltip("UE ProbeSize.")]
        [SerializeField] private float probeRadius = 0.15f;
        [SerializeField] private LayerMask collisionMask = ~0;

        [Header("Look")]
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

        /// <summary>When false, look input is ignored.</summary>
        public bool InputEnabled { get; set; } = true;

        private InputAction _look, _lookStick;
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
            if (lockCursor)
                SetCursorLocked(true);
        }

        private void OnDisable()
        {
            if (lockCursor)
                SetCursorLocked(false);
        }

        /// <summary>Reset the camera directly behind the target with no lag.</summary>
        public void SnapBehindTarget()
        {
            if (target == null) return;
            Yaw = target.eulerAngles.y;
            Pitch = startPitch;
            _pivot = target.position + pivotOffset;
            _currentArm = armLength;
            Apply(0f, true);
        }

        private void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.unscaledDeltaTime;

            HandleCursor();

            if (InputEnabled)
            {
                Vector2 look = Vector2.zero;
                if (_look != null && Cursor.lockState == CursorLockMode.Locked)
                    look += _look.ReadValue<Vector2>() * mouseSensitivity;
                if (_lookStick != null)
                    look += _lookStick.ReadValue<Vector2>() * (stickSensitivity * dt);

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

            float arm = armLength;
            if (Physics.SphereCast(_pivot, probeRadius, back, out RaycastHit hit, armLength, collisionMask, QueryTriggerInteraction.Ignore))
                arm = hit.distance;

            // Pull in instantly, ease back out (avoids popping when leaving a wall).
            _currentArm = (snap || arm < _currentArm) ? arm : Mathf.Lerp(_currentArm, arm, 1f - Mathf.Exp(-8f * dt));

            transform.SetPositionAndRotation(_pivot + back * _currentArm, rotation);
        }

        private void HandleCursor()
        {
            if (!lockCursor) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                SetCursorLocked(false);
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
                SetCursorLocked(true);
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
