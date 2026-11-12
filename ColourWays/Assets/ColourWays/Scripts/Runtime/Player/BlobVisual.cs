using ColourWays.Paint;
using UnityEngine;

namespace ColourWays.Player
{
    /// <summary>
    /// Procedural animation for the little white blob: body bob, lean, squash & stretch, and
    /// two stepping feet synced with <see cref="FootstepPainter"/>, so prints land where the feet
    /// do. Feet are tinted with the current paint colour. Replace with an Animator-driven model
    /// whenever proper animations exist; painting doesn't depend on this.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlobVisual : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private FootstepPainter footsteps;

        [Header("Parts")]
        [SerializeField] private Transform body;
        [SerializeField] private Transform footLeft;
        [SerializeField] private Transform footRight;
        [Tooltip("Renderers tinted with the current paint colour.")]
        [SerializeField] private Renderer[] paintTinted;

        [Header("Motion")]
        [SerializeField] private float bobHeight = 0.05f;
        [SerializeField] private float footLift = 0.12f;
        [SerializeField] private float leanAngle = 10f;
        [SerializeField] private float airStretch = 0.025f;
        [SerializeField] private float landSquash = 0.05f;
        [SerializeField] private float springStiffness = 220f;
        [SerializeField] private float springDamping = 14f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Vector3 _bodyPos, _bodyScale, _footLPos, _footRPos;
        private Quaternion _bodyRot;
        private float _squash, _squashVelocity;
        private float _lean;
        private MaterialPropertyBlock _mpb;
        private PaintManager _subscribed;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (footsteps == null) footsteps = GetComponentInParent<FootstepPainter>();
            if (body != null) { _bodyPos = body.localPosition; _bodyScale = body.localScale; _bodyRot = body.localRotation; }
            if (footLeft != null) _footLPos = footLeft.localPosition;
            if (footRight != null) _footRPos = footRight.localPosition;
            _mpb = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (player != null) player.Landed += OnLanded;
        }

        private void Start()
        {
            TrySubscribePaint();
        }

        private void OnDisable()
        {
            if (player != null) player.Landed -= OnLanded;
            if (_subscribed != null) _subscribed.ColourChanged -= Tint;
            _subscribed = null;
        }

        private void TrySubscribePaint()
        {
            var paint = PaintManager.Instance;
            if (paint == null || paint == _subscribed) return;
            paint.ColourChanged += Tint;
            _subscribed = paint;
            Tint(paint.CurrentColour);
        }

        private void Tint(Color colour)
        {
            if (paintTinted == null) return;
            _mpb.SetColor(BaseColorId, colour);
            foreach (var r in paintTinted)
                if (r != null) r.SetPropertyBlock(_mpb);
        }

        private void OnLanded(float impactSpeed)
        {
            _squashVelocity -= impactSpeed * landSquash * 10f;
        }

        private void LateUpdate()
        {
            if (player == null) return;
            if (_subscribed == null) TrySubscribePaint();

            float dt = Time.deltaTime;
            bool grounded = player.IsGrounded;
            float speed01 = Mathf.Clamp(player.HorizontalSpeed / Mathf.Max(player.WalkSpeed, 0.01f), 0f, 1.6f);
            float phase = footsteps != null ? footsteps.StridePhase : 0f;
            float stride = footsteps != null ? footsteps.FootForwardOffset + footsteps.StrideLength * 0.25f : 0.2f;

            // Squash & stretch spring (positive = stretched tall).
            float target = grounded ? 0f : Mathf.Clamp(player.Velocity.y * airStretch, -0.15f, 0.2f);
            _squashVelocity += (target - _squash) * springStiffness * dt;
            _squashVelocity *= Mathf.Exp(-springDamping * dt);
            _squash = Mathf.Clamp(_squash + _squashVelocity * dt, -0.4f, 0.4f);

            _lean = Mathf.Lerp(_lean, grounded ? speed01 * leanAngle : 0f, 1f - Mathf.Exp(-10f * dt));

            if (body != null)
            {
                float bob = grounded ? Mathf.Abs(Mathf.Sin(phase * Mathf.PI * 4f)) * bobHeight * Mathf.Min(speed01, 1f) : 0f;
                float stretchY = 1f + _squash;
                float stretchXZ = 1f / Mathf.Sqrt(Mathf.Max(stretchY, 0.2f));
                body.localScale = Vector3.Scale(_bodyScale, new Vector3(stretchXZ, stretchY, stretchXZ));
                body.localPosition = _bodyPos + Vector3.up * (bob + _bodyPos.y * (stretchY - 1f) * 0.5f);
                body.localRotation = Quaternion.Euler(_lean, 0f, 0f) * _bodyRot;
            }

            float amount = grounded ? Mathf.Min(speed01, 1f) : 0f;
            PlaceFoot(footLeft, _footLPos, phase, amount, stride, grounded);
            PlaceFoot(footRight, _footRPos, phase + 0.5f, amount, stride, grounded);
        }

        private void PlaceFoot(Transform foot, Vector3 basePos, float phase, float amount, float stride, bool grounded)
        {
            if (foot == null) return;
            float a = phase * Mathf.PI * 2f;
            // Planted (forward-most, on the ground) at phase 0, swinging through the back half.
            float forward = Mathf.Cos(a) * stride * amount;
            float lift = Mathf.Max(0f, -Mathf.Sin(a)) * footLift * amount;
            if (!grounded) lift = footLift * 0.5f;
            Vector3 targetPos = basePos + new Vector3(0f, lift, forward);
            foot.localPosition = Vector3.Lerp(foot.localPosition, targetPos, 1f - Mathf.Exp(-30f * Time.deltaTime));
        }
    }
}
