using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public enum ControlMode
    {
        FirstPerson,
        ThirdPerson
    }

    [Header("Mode & Camera")]
    [Tooltip("FirstPerson rotates the character with horizontal mouse look. ThirdPerson rotates the camera around the player and character faces movement direction.")]
    public ControlMode mode = ControlMode.FirstPerson;
    [Tooltip("Reference to the camera transform. If left empty, it will try to find Camera.main or a child Camera.")]
    public Transform playerCamera;
    [Tooltip("Pivot transform used in ThirdPerson mode for camera positioning.")]
    public Transform cameraPivot;

    [Header("Movement Settings")]
    [Tooltip("Normal walking speed in m/s.")]
    public float walkSpeed = 5.0f;
    [Tooltip("Sprint speed in m/s (holding Left Shift / Gamepad Left Stick).")]
    public float sprintSpeed = 9.0f;
    [Tooltip("How quickly the character accelerates/decelerates.")]
    public float speedSmoothTime = 0.1f;
    [Tooltip("Rotation smoothing time for ThirdPerson mode.")]
    public float rotationSmoothTime = 0.12f;

    [Header("Jump & Gravity")]
    [Tooltip("Height of jump in meters.")]
    public float jumpHeight = 1.5f;
    [Tooltip("Gravity acceleration (standard Earth gravity is ~9.81 - 15f for snappier feel).")]
    public float gravity = 20.0f;

    [Header("Look & Mouse Settings")]
    [Tooltip("Mouse look sensitivity.")]
    public float mouseSensitivity = 0.15f;
    [Tooltip("Gamepad look sensitivity.")]
    public float gamepadSensitivity = 120.0f;
    [Tooltip("Minimum vertical camera angle.")]
    public float minPitch = -85.0f;
    [Tooltip("Maximum vertical camera angle.")]
    public float maxPitch = 85.0f;

    [Header("Cursor Settings")]
    [Tooltip("Automatically locks cursor to game window on start.")]
    public bool lockCursorOnStart = true;

    [Header("Paint Explosion on Wall Impact")]
    [Tooltip("Enables exploding into paint when colliding with a wall or obstacle horizontally.")]
    public bool enablePaintExplosion = true;
    [Tooltip("Minimum horizontal speed required to trigger an explosion into a wall.")]
    public float minCollisionSpeed = 1.0f;

    // Component references & internal state
    private CharacterController characterController;
    private PaintExplosionSystem paintExplosionSystem;
    private float currentSpeed;
    private float speedSmoothVelocity;
    private float verticalVelocity;
    private float cameraPitch;
    private float cameraYaw;
    private float rotationSmoothVelocity;
    private Vector3 lastMoveDirection = Vector3.forward;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        paintExplosionSystem = GetComponent<PaintExplosionSystem>();

        // Auto-assign camera if not set
        if (playerCamera == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam == null && Camera.main != null)
            {
                cam = Camera.main;
            }
            if (cam != null)
            {
                playerCamera = cam.transform;
            }
        }

        // Initialize camera yaw and pitch
        if (playerCamera != null)
        {
            Vector3 camAngles = playerCamera.eulerAngles;
            cameraPitch = camAngles.x > 180f ? camAngles.x - 360f : camAngles.x;
            cameraYaw = transform.eulerAngles.y;
        }
    }

    private void Start()
    {
        if (lockCursorOnStart)
        {
            SetCursorLock(true);
        }
    }

    private void Update()
    {
        // Don't process input if currently exploding
        if (paintExplosionSystem != null && paintExplosionSystem.IsExploding)
        {
            currentSpeed = 0f;
            return;
        }

        HandleCursorState();
        HandleRotation();
        HandleMovement();
    }

    private void HandleCursorState()
    {
        // Unlock cursor with Escape
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            SetCursorLock(false);
        }

        // Lock cursor again on mouse click inside window
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            SetCursorLock(true);
        }
    }

    private void SetCursorLock(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void HandleRotation()
    {
        Vector2 lookInput = Vector2.zero;

        // Read mouse delta
        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
        {
            lookInput += Mouse.current.delta.ReadValue() * mouseSensitivity;
        }

        // Read gamepad right stick
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.rightStick.ReadValue();
            if (stick.sqrMagnitude > 0.01f)
            {
                lookInput += stick * (gamepadSensitivity * Time.deltaTime);
            }
        }

        if (mode == ControlMode.FirstPerson)
        {
            // Horizontal rotation rotates the character
            transform.Rotate(Vector3.up * lookInput.x);

            // Vertical rotation tilts the camera
            if (playerCamera != null)
            {
                cameraPitch -= lookInput.y;
                cameraPitch = Mathf.Clamp(cameraPitch, minPitch, maxPitch);
                playerCamera.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
            }
        }
        else // ThirdPerson mode
        {
            cameraYaw += lookInput.x;
            cameraPitch -= lookInput.y;
            cameraPitch = Mathf.Clamp(cameraPitch, minPitch, maxPitch);

            Transform pivot = cameraPivot != null ? cameraPivot : (playerCamera != null ? playerCamera.parent : null);
            if (pivot != null && pivot != transform)
            {
                pivot.rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
            }
            else if (playerCamera != null)
            {
                playerCamera.rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
            }
        }
    }

    private void HandleMovement()
    {
        Vector2 inputDir = Vector2.zero;

        // Read Keyboard Input
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) inputDir.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) inputDir.y -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) inputDir.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) inputDir.x += 1f;
        }

        // Read Gamepad Left Stick
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.01f)
            {
                inputDir += stick;
            }
        }

        inputDir = Vector2.ClampMagnitude(inputDir, 1f);

        // Sprint detection
        bool isSprinting = false;
        if (Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
        {
            isSprinting = true;
        }
        if (Gamepad.current != null && Gamepad.current.leftStickButton.isPressed)
        {
            isSprinting = true;
        }

        float targetSpeed = (isSprinting ? sprintSpeed : walkSpeed) * inputDir.magnitude;
        currentSpeed = Mathf.SmoothDamp(currentSpeed, targetSpeed, ref speedSmoothVelocity, speedSmoothTime);

        // Ground check & gravity
        if (characterController.isGrounded)
        {
            if (verticalVelocity < 0f)
            {
                verticalVelocity = -2.0f;
            }

            // Jump check
            bool jumpPressed = false;
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                jumpPressed = true;
            }
            if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
            {
                jumpPressed = true;
            }

            if (jumpPressed)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * 2.0f * gravity);
            }
        }
        else
        {
            verticalVelocity -= gravity * Time.deltaTime;
        }

        // Calculate world movement direction
        Vector3 moveDir = Vector3.zero;

        if (mode == ControlMode.FirstPerson)
        {
            moveDir = transform.right * inputDir.x + transform.forward * inputDir.y;
        }
        else // ThirdPerson mode
        {
            if (inputDir.magnitude > 0.01f)
            {
                float targetRotation = Mathf.Atan2(inputDir.x, inputDir.y) * Mathf.Rad2Deg;
                if (playerCamera != null)
                {
                    targetRotation += playerCamera.eulerAngles.y;
                }

                float smoothedAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetRotation, ref rotationSmoothVelocity, rotationSmoothTime);
                transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);

                moveDir = Quaternion.Euler(0f, targetRotation, 0f) * Vector3.forward;
            }
        }

        if (moveDir.sqrMagnitude > 0.001f)
        {
            lastMoveDirection = moveDir.normalized;
        }

        Vector3 velocity = (moveDir * currentSpeed) + (Vector3.up * verticalVelocity);
        characterController.Move(velocity * Time.deltaTime);
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!enablePaintExplosion) return;

        // Ignore horizontal floor and ceiling hits (floor normal.y ~ 1, ceiling ~ -1)
        // Walls and steep obstacles have normal.y near 0
        if (Mathf.Abs(hit.normal.y) >= 0.5f) return;

        // Check if player had sufficient movement speed into the obstacle
        if (currentSpeed < minCollisionSpeed) return;

        // Check that player was moving towards the wall normal
        Vector3 heading = lastMoveDirection;
        heading.y = 0f;
        if (heading.sqrMagnitude < 0.01f) heading = transform.forward;
        heading.Normalize();

        float dot = Vector3.Dot(heading, -hit.normal);
        if (dot > 0.15f)
        {
            if (paintExplosionSystem == null)
            {
                paintExplosionSystem = GetComponent<PaintExplosionSystem>();
            }

            if (paintExplosionSystem != null && !paintExplosionSystem.IsExploding)
            {
                paintExplosionSystem.Explode(hit.point, hit.normal, lastMoveDirection);
            }
        }
    }
}
