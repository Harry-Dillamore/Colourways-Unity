using System.Collections;
using System.Collections.Generic;
using ColourWays.Paint;
using UnityEngine;

namespace ColourWays.Player
{
    /// <summary>
    /// Paint bursts. Port of the ExplodeCheck -> NE_Splat_System -> BP_NiagaraHandler chain:
    ///  • Running into a wall: a big splat on the wall where the dude hit, a drip splash at its
    ///    foot, and droplets sprayed back off the wall that splat wherever they land. This is how
    ///    the invisible walls get discovered.
    ///  • Hard landings (when falling off something): a splat underfoot and a ring of droplets.
    /// Every burst switches to a new palette colour, so later footprints use it too.
    /// Put this on a ParticleSystem with World collision and "Send Collision Messages" enabled.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class PaintSplatEmitter : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [Tooltip("Pick a new palette colour on every burst.")]
        [SerializeField] private bool newColourOnBurst = true;
        [SerializeField] private LayerMask paintMask = ~0;

        [Header("Wall Bonk")]
        [Tooltip("Speed into the wall at which the wall splat is at its largest.")]
        [SerializeField] private float maxWallSpeed = 8f;
        [SerializeField] private Vector2 wallSplatRadius = new Vector2(0.55f, 1.1f);
        [Tooltip("Height of the wall splat centre above the feet.")]
        [SerializeField] private float wallSplatHeight = 0.7f;
        [SerializeField] private Vector2Int wallDroplets = new Vector2Int(12, 30);
        [SerializeField, Range(0f, 89f)] private float wallCone = 70f;

        [Header("Drips")]
        [Tooltip("Streaks of paint that run down the wall after a splat.")]
        [SerializeField] private Vector2Int dripCount = new Vector2Int(3, 6);
        [SerializeField] private Vector2 dripLength = new Vector2(0.3f, 0.9f);
        [SerializeField] private float dripRadius = 0.045f;

        [Header("Landing")]
        [Tooltip("Minimum downward speed on landing to splat.")]
        [SerializeField] private float minImpactSpeed = 6f;
        [SerializeField] private float maxImpactSpeed = 16f;
        [SerializeField] private float impactSplatRadius = 0.7f;
        [SerializeField] private Vector2Int landingDroplets = new Vector2Int(10, 28);

        [Header("Droplets")]
        [SerializeField] private Vector2 dropletSpeed = new Vector2(2.5f, 6.5f);
        [Tooltip("Fraction of the player's horizontal velocity the droplets inherit (UE CharacterVelocity).")]
        [SerializeField] private float inheritVelocity = 0.4f;
        [SerializeField] private Vector2 dropletSize = new Vector2(0.06f, 0.14f);
        [SerializeField] private Vector2 dropletSplatRadius = new Vector2(0.14f, 0.38f);

        private ParticleSystem _particles;
        private readonly List<ParticleCollisionEvent> _events = new List<ParticleCollisionEvent>(16);
        private Color _burstColour = Color.white;

        private void Awake()
        {
            _particles = GetComponent<ParticleSystem>();
            if (player == null)
                player = GetComponentInParent<PlayerController>();
            if (player != null) _controller = player.GetComponent<CharacterController>();
        }

        private CharacterController _controller;

        /// <summary>
        /// A massive splash (paint blobs): huge splats on the floor around <paramref name="centre"/>
        /// and a fountain of droplets in every direction that paint the walls nearby.
        /// </summary>
        public void BigSplash(Vector3 centre, Color colour, int droplets, float radius)
        {
            var paint = PaintManager.Instance;
            if (paint == null) return;
            _burstColour = colour;

            void Floor(Vector3 at, float r)
            {
                if (Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, paintMask, QueryTriggerInteraction.Ignore))
                    paint.Paint(hit.point + hit.normal * 0.01f, hit.normal, r, colour, BrushShape.Splat);
            }

            Floor(centre, radius);
            int blots = 9;
            for (int i = 0; i < blots; i++)
            {
                float angle = (i + Random.Range(-0.3f, 0.3f)) / blots * Mathf.PI * 2f;
                float distance = Random.Range(radius * 0.8f, radius * 2.2f);
                Floor(centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance, radius * Random.Range(0.3f, 0.6f));
            }

            Burst(centre + Vector3.up * 0.6f, Vector3.up, 85f, droplets, 1.6f, Vector3.zero);
        }

        private void OnEnable()
        {
            if (player == null) return;
            player.Landed += OnLanded;
            player.WallHit += OnWallHit;
        }

        private void OnDisable()
        {
            if (player == null) return;
            player.Landed -= OnLanded;
            player.WallHit -= OnWallHit;
        }

        private Color NextBurstColour(PaintManager paint) => newColourOnBurst ? paint.NextColour() : paint.CurrentColour;

        private void OnWallHit(Vector3 point, Vector3 wallNormal, float speed)
        {
            var paint = PaintManager.Instance;
            if (paint == null) return;
            _burstColour = NextBurstColour(paint);
            // A fatal splat (the dude bursts) is always the biggest.
            float t = player.IsSplatted ? 1f : Mathf.InverseLerp(player.WalkSpeed * 0.4f, maxWallSpeed, speed);

            // The big splat on the wall, at body height where the dude's face met it.
            Vector3 feet = player.transform.position;
            float bodyHeight = _controller != null ? _controller.height * 0.6f : wallSplatHeight;
            Vector3 onWall = new Vector3(point.x, feet.y + bodyHeight, point.z);
            float radius = Mathf.Lerp(wallSplatRadius.x, wallSplatRadius.y, t);
            paint.Paint(onWall + wallNormal * 0.02f, wallNormal, radius, _burstColour, BrushShape.Splat);
            // A second, offset blob makes it read as a splash rather than a stamp.
            Vector3 side = Vector3.Cross(Vector3.up, wallNormal);
            paint.Paint(onWall + side * Random.Range(-0.35f, 0.35f) + Vector3.up * Random.Range(0.1f, 0.45f) + wallNormal * 0.02f,
                wallNormal, radius * Random.Range(0.45f, 0.7f), _burstColour, BrushShape.Splat);

            // Splash on the floor at the foot of the wall.
            if (Physics.Raycast(new Vector3(point.x, feet.y + 0.3f, point.z) + wallNormal * 0.3f, Vector3.down, out RaycastHit floor, 1f, paintMask, QueryTriggerInteraction.Ignore))
                paint.Paint(floor.point + floor.normal * 0.01f, floor.normal, radius * 0.75f, _burstColour, BrushShape.Splat);

            // Paint runs down the wall from the bottom of the splat.
            int drips = Random.Range(dripCount.x, dripCount.y + 1);
            for (int i = 0; i < drips; i++)
            {
                Vector3 from = onWall + side * Random.Range(-radius * 0.75f, radius * 0.75f) + Vector3.down * Random.Range(radius * 0.2f, radius * 0.6f);
                StartCoroutine(Drip(from, wallNormal, _burstColour, Random.Range(dripLength.x, dripLength.y) * Mathf.Lerp(0.7f, 1.2f, t), feet.y));
            }

            // Droplets fly back off the wall (up and outwards).
            Vector3 axis = (wallNormal + Vector3.up * 0.55f).normalized;
            int count = Mathf.RoundToInt(Mathf.Lerp(wallDroplets.x, wallDroplets.y, t));
            Burst(onWall + wallNormal * 0.15f, axis, wallCone, count, Mathf.Lerp(0.8f, 1.2f, t), Vector3.zero);
        }

        private void OnLanded(float impactSpeed)
        {
            if (impactSpeed < minImpactSpeed) return;
            var paint = PaintManager.Instance;
            if (paint == null) return;

            _burstColour = NextBurstColour(paint);
            float t = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);
            Vector3 feet = player.transform.position;

            if (Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 1f, paintMask, QueryTriggerInteraction.Ignore))
                paint.Paint(hit.point + hit.normal * 0.01f, hit.normal, impactSplatRadius * Mathf.Lerp(0.8f, 1.3f, t), _burstColour, BrushShape.Splat);

            Vector3 v = player.Velocity;
            int count = Mathf.RoundToInt(Mathf.Lerp(landingDroplets.x, landingDroplets.y, t));
            Burst(feet + Vector3.up * 0.2f, Vector3.up, 60f, count, Mathf.Lerp(0.85f, 1.25f, t), new Vector3(v.x, 0f, v.z) * inheritVelocity);
        }

        /// <summary>A bead of paint sliding down a wall, slowing as it goes, leaving a tapering streak.</summary>
        private IEnumerator Drip(Vector3 start, Vector3 normal, Color colour, float length, float floorY)
        {
            yield return new WaitForSeconds(Random.Range(0.05f, 0.35f)); // drips start a beat after the hit
            var paint = PaintManager.Instance;
            if (paint == null) yield break;
            float r = dripRadius * Random.Range(0.8f, 1.3f);
            float speed = Random.Range(0.5f, 0.9f);
            float travelled = 0f;
            Vector3 p = start;
            while (travelled < length && p.y > floorY + 0.03f)
            {
                float step = speed * Time.deltaTime;
                speed = Mathf.Max(0.1f, speed - Time.deltaTime * 0.5f);
                int n = Mathf.Max(1, Mathf.CeilToInt(step / (r * 0.4f)));
                for (int i = 0; i < n; i++)
                {
                    p += Vector3.down * (step / n);
                    travelled += step / n;
                    float taper = 1f - 0.4f * Mathf.Clamp01(travelled / length);
                    paint.Paint(p + normal * 0.02f, normal, r * taper, colour, BrushShape.Round);
                }
                yield return null;
            }
            paint.Paint(p + normal * 0.02f, normal, r * 1.3f, colour, BrushShape.Splat); // the bead at the end
        }

        /// <summary>Fire paint droplets in a cone around <paramref name="axis"/>; each one splats where it lands.</summary>
        private void Burst(Vector3 origin, Vector3 axis, float cone, int count, float speedScale, Vector3 inherit)
        {
            var emit = new ParticleSystem.EmitParams { startColor = _burstColour, applyShapeToPosition = false };
            Quaternion toAxis = Quaternion.FromToRotation(Vector3.up, axis);
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = toAxis * (Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Quaternion.Euler(Random.Range(5f, cone), 0f, 0f) * Vector3.up);
                emit.position = origin + dir * 0.1f;
                emit.velocity = dir * (Random.Range(dropletSpeed.x, dropletSpeed.y) * speedScale) + inherit;
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
