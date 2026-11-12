using System.Collections.Generic;
using ColourWays.Paint;
using UnityEngine;

namespace ColourWays.Player
{
    /// <summary>
    /// Landing paint burst. Port of the ExplodeCheck -> NE_Splat_System -> BP_NiagaraHandler
    /// chain: on a hard enough landing, picks a new colour, stamps a big splat under the player
    /// and fires paint droplets that leave splats wherever they hit.
    /// Put this on a ParticleSystem with World collision and "Send Collision Messages" enabled.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class PaintSplatEmitter : MonoBehaviour
    {
        [SerializeField] private PlayerController player;

        [Header("Trigger")]
        [Tooltip("Minimum downward speed on landing to splat. A normal jump lands at ~7 m/s.")]
        [SerializeField] private float minImpactSpeed = 4f;
        [Tooltip("Impact speed at which the burst is at its largest.")]
        [SerializeField] private float maxImpactSpeed = 16f;
        [Tooltip("Pick a new palette colour on every splat landing.")]
        [SerializeField] private bool newColourOnLanding = true;

        [Header("Impact Splat")]
        [SerializeField] private float impactSplatRadius = 0.7f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Droplets")]
        [SerializeField] private Vector2Int dropletCount = new Vector2Int(10, 28);
        [SerializeField] private Vector2 dropletSpeed = new Vector2(2.5f, 6.5f);
        [SerializeField, Range(0f, 89f)] private float coneAngle = 60f;
        [Tooltip("Fraction of the player's horizontal velocity the droplets inherit (UE CharacterVelocity).")]
        [SerializeField] private float inheritVelocity = 0.6f;
        [SerializeField] private Vector2 dropletSize = new Vector2(0.07f, 0.15f);
        [SerializeField] private Vector2 dropletSplatRadius = new Vector2(0.16f, 0.4f);

        private ParticleSystem _particles;
        private readonly List<ParticleCollisionEvent> _events = new List<ParticleCollisionEvent>(16);
        private Color _burstColour = Color.white;

        private void Awake()
        {
            _particles = GetComponent<ParticleSystem>();
            if (player == null)
                player = GetComponentInParent<PlayerController>();
        }

        private void OnEnable()
        {
            if (player != null)
                player.Landed += OnLanded;
        }

        private void OnDisable()
        {
            if (player != null)
                player.Landed -= OnLanded;
        }

        private void OnLanded(float impactSpeed)
        {
            if (impactSpeed < minImpactSpeed) return;
            var paint = PaintManager.Instance;
            if (paint == null) return;

            _burstColour = newColourOnLanding ? paint.NextColour() : paint.CurrentColour;
            float t = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);

            Vector3 feet = player.transform.position;

            if (Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 1f, groundMask, QueryTriggerInteraction.Ignore))
                paint.Paint(hit.point + hit.normal * 0.01f, hit.normal, impactSplatRadius * Mathf.Lerp(0.8f, 1.3f, t), _burstColour, BrushShape.Splat);

            Vector3 v = player.Velocity;
            Vector3 inherit = new Vector3(v.x, 0f, v.z) * inheritVelocity;
            int count = Mathf.RoundToInt(Mathf.Lerp(dropletCount.x, dropletCount.y, t));

            var emit = new ParticleSystem.EmitParams
            {
                startColor = _burstColour,
                applyShapeToPosition = false,
            };

            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                            * Quaternion.Euler(Random.Range(15f, coneAngle), 0f, 0f)
                            * Vector3.up;
                float speed = Random.Range(dropletSpeed.x, dropletSpeed.y) * Mathf.Lerp(0.85f, 1.25f, t);

                emit.position = feet + Vector3.up * 0.2f + new Vector3(dir.x, 0f, dir.z) * 0.25f;
                emit.velocity = dir * speed + inherit;
                emit.startSize = Random.Range(dropletSize.x, dropletSize.y);
                _particles.Emit(emit, 1);
            }
        }

        private void OnParticleCollision(GameObject other)
        {
            var paint = PaintManager.Instance;
            if (paint == null) return;

            int n = _particles.GetCollisionEvents(other, _events);
            for (int i = 0; i < n; i++)
            {
                var e = _events[i];
                Vector3 normal = e.normal.sqrMagnitude > 1e-6f ? e.normal : Vector3.up;
                float radius = Random.Range(dropletSplatRadius.x, dropletSplatRadius.y);
                paint.Paint(e.intersection + normal * 0.01f, normal, radius, _burstColour, BrushShape.Splat);
            }
        }
    }
}
