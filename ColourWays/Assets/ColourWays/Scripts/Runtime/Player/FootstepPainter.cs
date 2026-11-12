using ColourWays.Paint;
using UnityEngine;

namespace ColourWays.Player
{
    /// <summary>
    /// Leaves footprints in the current paint colour. Port of AN_Footstep, but driven by
    /// distance travelled instead of animation notifies, so it's deterministic and works with
    /// any (or no) animation. <see cref="StridePhase"/> lets visuals sync their feet to it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class FootstepPainter : MonoBehaviour
    {
        [Tooltip("Distance travelled between footprints (alternating feet).")]
        [SerializeField] private float strideLength = 0.5f;
        [Tooltip("Sideways offset of each foot from the centre.")]
        [SerializeField] private float footSpacing = 0.14f;
        [Tooltip("Forward offset of the planted foot.")]
        [SerializeField] private float footForwardOffset = 0.08f;
        [Tooltip("Unreal FootStepSize.")]
        [SerializeField] private float footprintRadius = 0.13f;
        [SerializeField] private float landingFootprintScale = 1.3f;
        [SerializeField] private float probeHeight = 0.3f;
        [SerializeField] private float probeDistance = 0.6f;
        [SerializeField] private LayerMask paintMask = ~0;

        /// <summary>0..1 over a full two-step cycle. Left foot plants at 0, right at 0.5.</summary>
        public float StridePhase => _cycle / (strideLength * 2f);
        public float StrideLength => strideLength;
        public float FootSpacing => footSpacing;
        public float FootForwardOffset => footForwardOffset;

        private PlayerController _player;
        private Vector3 _lastPosition;
        private float _cycle;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            _player.Landed += OnLanded;
            _player.Respawned += OnRespawned;
            _lastPosition = transform.position;
        }

        private void OnDisable()
        {
            _player.Landed -= OnLanded;
            _player.Respawned -= OnRespawned;
        }

        private void OnRespawned()
        {
            _lastPosition = transform.position;
            _cycle = 0f;
        }

        private void OnLanded(float impactSpeed)
        {
            _lastPosition = transform.position;
            if (impactSpeed < 1f) return;
            Step(0, landingFootprintScale);
            Step(1, landingFootprintScale);
        }

        private void Update()
        {
            Vector3 position = transform.position;
            Vector3 delta = position - _lastPosition;
            _lastPosition = position;

            if (!_player.IsGrounded)
                return;

            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= 0f || distance > 2f) // ignore teleports
                return;

            float full = strideLength * 2f;
            float before = _cycle;
            _cycle += distance;

            // Right foot plants when crossing half a cycle, left when wrapping.
            if (before < strideLength && _cycle >= strideLength)
                Step(1, 1f);
            if (_cycle >= full)
            {
                _cycle -= full;
                Step(0, 1f);
            }
        }

        /// <summary>Stamp a footprint under a foot (0 = left, 1 = right).</summary>
        public void Step(int foot, float sizeScale)
        {
            var paint = PaintManager.Instance;
            if (paint == null) return;

            Vector3 side = transform.right * (foot == 0 ? -footSpacing : footSpacing);
            Vector3 origin = transform.position + side + transform.forward * footForwardOffset + Vector3.up * probeHeight;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeHeight + probeDistance, paintMask, QueryTriggerInteraction.Ignore))
                paint.Paint(hit.point + hit.normal * 0.01f, hit.normal, footprintRadius * sizeScale, BrushShape.Round);
        }
    }
}
