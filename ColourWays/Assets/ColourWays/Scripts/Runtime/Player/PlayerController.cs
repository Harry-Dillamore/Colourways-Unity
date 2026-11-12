using System;
using ColourWays.Level;
using ColourWays.Paint;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ColourWays.Player
{
    /// <summary>
    /// Third-person blob movement. Port of BP_ThirdPersonCharacter + CharacterMovementComponent
    /// (defaults converted from the UE template: cm -> m). Kinematic, built on CharacterController.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InputActionAsset controls;
        [SerializeField] private ThirdPersonCamera cameraRig;

        [Header("Ground")]
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float sprintSpeed = 8f;
        [Tooltip("UE MaxAcceleration (1500 cm/s²).")]
        [SerializeField] private float maxAcceleration = 15f;
        [Tooltip("UE BrakingDecelerationWalking (2000 cm/s²).")]
        [SerializeField] private float brakingDeceleration = 20f;
        [Tooltip("UE GroundFriction. Higher = snappier direction changes.")]
        [SerializeField] private float groundFriction = 8f;
        [Tooltip("Degrees per second the blob turns to face its movement.")]
        [SerializeField] private float rotationRate = 540f;

        [Header("Air")]
        [Tooltip("UE JumpZVelocity (700 cm/s).")]
        [SerializeField] private float jumpVelocity = 7f;
        [Tooltip("UE GravityScale.")]
        [SerializeField] private float gravityScale = 1.75f;
        [Tooltip("UE AirControl.")]
        [SerializeField, Range(0f, 1f)] private float airControl = 0.35f;
        [Tooltip("UE BrakingDecelerationFalling (1500 cm/s²).")]
        [SerializeField] private float airBraking = 15f;
        [SerializeField] private float maxFallSpeed = 40f;
        [Tooltip("Grace period to still jump after walking off a ledge.")]
        [SerializeField] private float coyoteTime = 0.1f;
        [Tooltip("Jump presses this long before landing still count.")]
        [SerializeField] private float jumpBufferTime = 0.12f;

        [Header("Respawn")]
        [Tooltip("Optional. If empty, the first SpawnPoint in the scene is used, else the starting position.")]
        [SerializeField] private SpawnPoint spawnPoint;
        [SerializeField] private float killHeight = -25f;
        [SerializeField] private bool clearPaintOnRestart;

        /// <summary>Raised on touchdown with the downward impact speed (m/s).</summary>
        public event Action<float> Landed;
        public event Action Jumped;
        public event Action Respawned;

        public Vector3 Velocity => _velocity;
        public float HorizontalSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;
        public float WalkSpeed => walkSpeed;
        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public ThirdPersonCamera CameraRig => cameraRig;

        /// <summary>When false, movement input is ignored (gravity still applies).</summary>
        public bool InputEnabled { get; set; } = true;

        private CharacterController _cc;
        private InputActionMap _map;
        private InputAction _move, _jump, _sprint, _restart;
        private Vector3 _velocity;
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private Vector3 _fallbackSpawnPosition;
        private Quaternion _fallbackSpawnRotation;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _fallbackSpawnPosition = transform.position;
            _fallbackSpawnRotation = transform.rotation;

            if (spawnPoint == null)
                spawnPoint = FindAnyObjectByType<SpawnPoint>();
            if (cameraRig == null)
                cameraRig = FindAnyObjectByType<ThirdPersonCamera>();

            if (controls != null)
            {
                _map = controls.FindActionMap("Player", true);
                _move = _map.FindAction("Move", true);
                _jump = _map.FindAction("Jump", true);
                _sprint = _map.FindAction("Sprint", true);
                _restart = _map.FindAction("Restart", true);
            }
            else
            {
                Debug.LogError("[ColourWays] PlayerController has no Controls asset assigned.", this);
            }
        }

        private void OnEnable()
        {
            if (_map == null) return;
            _map.Enable();
            _jump.performed += OnJumpPerformed;
            _restart.performed += OnRestartPerformed;
        }

        private void OnDisable()
        {
            if (_map == null) return;
            _jump.performed -= OnJumpPerformed;
            _restart.performed -= OnRestartPerformed;
            _map.Disable();
        }

        private void OnJumpPerformed(InputAction.CallbackContext _)
        {
            if (InputEnabled)
                _jumpBufferTimer = jumpBufferTime;
        }

        private void OnRestartPerformed(InputAction.CallbackContext _)
        {
            if (InputEnabled)
                Respawn();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // ---- Input -> world-space wish direction (relative to camera yaw, like UE control rotation)
            Vector2 input = (InputEnabled && _move != null) ? _move.ReadValue<Vector2>() : Vector2.zero;
            input = Vector2.ClampMagnitude(input, 1f);
            float yaw = cameraRig != null ? cameraRig.Yaw : (Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f);
            Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
            float wishAmount = wish.magnitude;
            Vector3 wishDir = wishAmount > 1e-4f ? wish / wishAmount : Vector3.zero;

            IsSprinting = InputEnabled && _sprint != null && _sprint.IsPressed() && wishAmount > 0.1f;
            float maxSpeed = (IsSprinting ? sprintSpeed : walkSpeed) * wishAmount;

            bool grounded = _cc.isGrounded;
            _coyoteTimer = grounded ? coyoteTime : _coyoteTimer - dt;
            _jumpBufferTimer -= dt;

            // ---- Horizontal velocity (approximation of UE CalcVelocity)
            Vector3 h = new Vector3(_velocity.x, 0f, _velocity.z);
            if (grounded)
            {
                if (wishAmount > 1e-4f)
                {
                    float speed = h.magnitude;
                    h -= (h - wishDir * speed) * Mathf.Min(dt * groundFriction, 1f);
                    h = Vector3.MoveTowards(h, wishDir * maxSpeed, maxAcceleration * dt);
                }
                else
                {
                    h = Vector3.MoveTowards(h, Vector3.zero, (brakingDeceleration + groundFriction * h.magnitude) * dt);
                }
            }
            else
            {
                if (wishAmount > 1e-4f)
                    h = Vector3.MoveTowards(h, wishDir * Mathf.Max(maxSpeed, Mathf.Min(h.magnitude, sprintSpeed)), maxAcceleration * airControl * dt);
                else
                    h = Vector3.MoveTowards(h, Vector3.zero, airBraking * dt);
            }

            // ---- Vertical velocity
            float vy = _velocity.y;
            if (grounded && vy < 0f)
                vy = -2f; // keep the controller snapped to the ground

            if (_jumpBufferTimer > 0f && _coyoteTimer > 0f)
            {
                vy = jumpVelocity;
                _jumpBufferTimer = 0f;
                _coyoteTimer = 0f;
                grounded = false;
                Jumped?.Invoke();
            }

            vy += Physics.gravity.y * gravityScale * dt;
            vy = Mathf.Max(vy, -maxFallSpeed);

            _velocity = new Vector3(h.x, vy, h.z);

            // ---- Face movement direction (UE bOrientRotationToMovement)
            if (wishAmount > 0.05f)
            {
                Quaternion target = Quaternion.LookRotation(wishDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationRate * dt);
            }

            // ---- Move
            float impactSpeed = -_velocity.y;
            Vector3 before = transform.position;
            CollisionFlags flags = _cc.Move(_velocity * dt);

            if ((flags & CollisionFlags.Above) != 0 && _velocity.y > 0f)
                _velocity.y = 0f;

            if ((flags & CollisionFlags.Sides) != 0)
            {
                // Slide along walls instead of keeping velocity pushed into them.
                Vector3 actual = (transform.position - before) / dt;
                Vector3 actualH = new Vector3(actual.x, 0f, actual.z);
                Vector3 currentH = new Vector3(_velocity.x, 0f, _velocity.z);
                if (actualH.sqrMagnitude < currentH.sqrMagnitude)
                    _velocity = new Vector3(actualH.x, _velocity.y, actualH.z);
            }

            bool wasGrounded = IsGrounded;
            IsGrounded = _cc.isGrounded;
            if (IsGrounded && !wasGrounded)
                Landed?.Invoke(Mathf.Max(0f, impactSpeed));

            if (transform.position.y < killHeight)
                Respawn();
        }

        /// <summary>Teleport back to the spawn point (Unreal: Hold R -> FindPlayerStart).</summary>
        public void Respawn()
        {
            Vector3 position = spawnPoint != null ? spawnPoint.transform.position : _fallbackSpawnPosition;
            Quaternion rotation = spawnPoint != null
                ? Quaternion.Euler(0f, spawnPoint.transform.eulerAngles.y, 0f)
                : _fallbackSpawnRotation;

            _cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _cc.enabled = true;

            _velocity = Vector3.zero;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            IsGrounded = false;

            if (cameraRig != null)
                cameraRig.SnapBehindTarget();

            if (clearPaintOnRestart && PaintManager.Instance != null)
                PaintManager.Instance.ClearPaint();

            Respawned?.Invoke();
        }
    }
}
