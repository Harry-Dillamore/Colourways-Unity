using System;
using ColourWays.Level;
using ColourWays.Paint;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ColourWays.Player
{
    /// <summary>
    /// Third-person movement. Port of BP_ThirdPersonCharacter + CharacterMovementComponent
    /// (defaults converted from the UE template: cm -> m). Kinematic, built on CharacterController.
    ///
    /// Running into a wall fast enough "bonks": the dude bounces off, is briefly stunned, and
    /// <see cref="WallHit"/> fires so the splat emitter can paint the (invisible) wall.
    /// Jumping is off by default (the levels are walk-and-explore for now).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InputActionAsset controls;
        [SerializeField] private ThirdPersonCamera cameraRig;

        [Header("Ground")]
        [SerializeField] private float walkSpeed = 2.2f;
        [SerializeField] private float sprintSpeed = 3.8f;
        [Tooltip("UE MaxAcceleration (1500 cm/s²).")]
        [SerializeField] private float maxAcceleration = 10f;
        [Tooltip("UE BrakingDecelerationWalking (2000 cm/s²).")]
        [SerializeField] private float brakingDeceleration = 14f;
        [Tooltip("UE GroundFriction. Higher = snappier direction changes.")]
        [SerializeField] private float groundFriction = 8f;
        [Tooltip("Degrees per second the dude turns to face its movement.")]
        [SerializeField] private float rotationRate = 720f;

        [Header("Wall Bonk")]
        [Tooltip("Running into a wall splats the dude (dies) and respawns him at the last paint blob.")]
        [SerializeField] private bool bonkKills = true;
        [Tooltip("Seconds between splatting and respawning.")]
        [SerializeField] private float splatRespawnDelay = 1.1f;
        [Tooltip("Speed straight into a wall (m/s) that splats. Low, so walking into a wall splats too; only shallow scrapes along a wall are safe.")]
        [SerializeField] private float bonkSpeed = 0.9f;
        [Tooltip("Bounce-back speed as a fraction of the impact speed.")]
        [SerializeField, Range(0f, 1.5f)] private float bounce = 0.55f;
        [Tooltip("Seconds of no control after a bonk.")]
        [SerializeField] private float stunTime = 0.3f;
        [Tooltip("Minimum time between bonks.")]
        [SerializeField] private float bonkCooldown = 0.35f;
        [Tooltip("Surfaces steeper than this (degrees from up) count as walls.")]
        [SerializeField] private float wallAngle = 60f;

        [Header("Air")]
        [SerializeField] private bool allowJump;
        [Tooltip("UE JumpZVelocity (700 cm/s).")]
        [SerializeField] private float jumpVelocity = 7f;
        [Tooltip("UE GravityScale.")]
        [SerializeField] private float gravityScale = 1.75f;
        [Tooltip("UE AirControl.")]
        [SerializeField, Range(0f, 1f)] private float airControl = 0.35f;
        [Tooltip("UE BrakingDecelerationFalling (1500 cm/s²).")]
        [SerializeField] private float airBraking = 15f;
        [SerializeField] private float maxFallSpeed = 40f;
        [SerializeField] private float coyoteTime = 0.1f;
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
        /// <summary>Ran into a wall: (contact point, wall normal, speed into the wall in m/s).</summary>
        public event Action<Vector3, Vector3, float> WallHit;
        /// <summary>The dude splatted against a wall and is gone until he respawns.</summary>
        public event Action Splatted;

        /// <summary>True between splatting into a wall and respawning.</summary>
        public bool IsSplatted { get; private set; }
        private float _splatTimer;

        public Vector3 Velocity => _velocity;
        public float HorizontalSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;
        public float WalkSpeed => walkSpeed;
        public float SprintSpeed => sprintSpeed;
        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        /// <summary>0..1, counts down after a bonk.</summary>
        public float Stun01 => stunTime > 0f ? Mathf.Clamp01(_stunTimer / stunTime) : 0f;
        public ThirdPersonCamera CameraRig => cameraRig;

        /// <summary>When false, movement input is ignored (gravity still applies).</summary>
        public bool InputEnabled { get; set; } = true;

        private CharacterController _cc;
        private InputActionMap _map;
        private InputAction _move, _jump, _sprint, _restart;
        private Vector3 _velocity;
        private Vector3 _moveVelocity; // velocity used for the current Move(), for bonk detection
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private float _stunTimer;
        private float _bonkCooldownTimer;
        private bool _bonkedThisMove;
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
            if (InputEnabled && allowJump)
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

            if (IsSplatted)
            {
                _splatTimer -= dt;
                if (_splatTimer <= 0f) Respawn();
                return;
            }

            _stunTimer = Mathf.Max(0f, _stunTimer - dt);
            _bonkCooldownTimer = Mathf.Max(0f, _bonkCooldownTimer - dt);
            bool controllable = InputEnabled && _stunTimer <= 0f;

            // ---- Input -> world-space wish direction (relative to camera yaw, like UE control rotation)
            Vector2 input = (controllable && _move != null) ? _move.ReadValue<Vector2>() : Vector2.zero;
            input = Vector2.ClampMagnitude(input, 1f);
            float yaw = cameraRig != null ? cameraRig.Yaw : (Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f);
            Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
            float wishAmount = wish.magnitude;
            Vector3 wishDir = wishAmount > 1e-4f ? wish / wishAmount : Vector3.zero;

            IsSprinting = controllable && _sprint != null && _sprint.IsPressed() && wishAmount > 0.1f;
            float maxSpeed = (IsSprinting ? sprintSpeed : walkSpeed) * wishAmount;

            bool grounded = _cc.isGrounded;
            _coyoteTimer = grounded ? coyoteTime : _coyoteTimer - dt;
            _jumpBufferTimer -= dt;

            // ---- Horizontal velocity (approximation of UE CalcVelocity)
            Vector3 h = new Vector3(_velocity.x, 0f, _velocity.z);
            if (grounded && _stunTimer <= 0f)
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
            else if (grounded)
            {
                // Stunned: slide out the bounce with gentle braking.
                h = Vector3.MoveTowards(h, Vector3.zero, brakingDeceleration * 0.35f * dt);
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

            if (allowJump && _jumpBufferTimer > 0f && _coyoteTimer > 0f && controllable)
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
            _moveVelocity = _velocity;
            _bonkedThisMove = false;
            CollisionFlags flags = _cc.Move(_velocity * dt);

            if ((flags & CollisionFlags.Above) != 0 && _velocity.y > 0f)
                _velocity.y = 0f;

            if ((flags & CollisionFlags.Sides) != 0 && !_bonkedThisMove)
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

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (_bonkedThisMove || _bonkCooldownTimer > 0f) return;
            Vector3 n = hit.normal;
            if (Vector3.Angle(n, Vector3.up) < wallAngle) return; // floor / gentle slope

            Vector3 wallN = new Vector3(n.x, 0f, n.z);
            if (wallN.sqrMagnitude < 1e-4f) return;
            wallN.Normalize();
            Vector3 hv = new Vector3(_moveVelocity.x, 0f, _moveVelocity.z);
            float into = -Vector3.Dot(hv, wallN);
            if (into < bonkSpeed) return;

            _bonkCooldownTimer = bonkCooldown;
            _bonkedThisMove = true;

            if (bonkKills)
            {
                // Splat: the dude bursts into paint on the wall and respawns at the last blob.
                _velocity = Vector3.zero;
                IsSplatted = true;
                _splatTimer = splatRespawnDelay;
                WallHit?.Invoke(hit.point, wallN, into);
                Splatted?.Invoke();
                return;
            }

            // Bonk: bounce straight back off the wall, keep a little of the sideways motion.
            Vector3 along = hv + wallN * into;
            Vector3 bounced = wallN * (into * bounce) + along * 0.3f;
            _velocity = new Vector3(bounced.x, _velocity.y, bounced.z);
            _stunTimer = stunTime;
            WallHit?.Invoke(hit.point, wallN, into);
        }

        /// <summary>Respawns (R, or falling out of the world) happen here from now on.</summary>
        public void SetCheckpoint(Vector3 position, float yaw)
        {
            _hasCheckpoint = true;
            _checkpointPosition = position;
            _checkpointRotation = Quaternion.Euler(0f, yaw, 0f);
            CheckpointReached?.Invoke();
        }

        public event Action CheckpointReached;
        private bool _hasCheckpoint;
        private Vector3 _checkpointPosition;
        private Quaternion _checkpointRotation;

        /// <summary>Teleport back to the last checkpoint, or the spawn point if none was reached (Unreal: R -> FindPlayerStart).</summary>
        public void Respawn()
        {
            Vector3 position = _hasCheckpoint ? _checkpointPosition
                : spawnPoint != null ? spawnPoint.transform.position : _fallbackSpawnPosition;
            Quaternion rotation = _hasCheckpoint ? _checkpointRotation
                : spawnPoint != null ? Quaternion.Euler(0f, spawnPoint.transform.eulerAngles.y, 0f)
                : _fallbackSpawnRotation;

            _cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _cc.enabled = true;

            _velocity = Vector3.zero;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            _stunTimer = 0f;
            IsSplatted = false;
            IsGrounded = false;

            if (cameraRig != null)
                cameraRig.SnapBehindTarget();

            if (clearPaintOnRestart && PaintManager.Instance != null)
                PaintManager.Instance.ClearPaint();

            Respawned?.Invoke();
        }
    }
}
