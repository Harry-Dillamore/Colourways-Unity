using ColourWays.Paint;
using ColourWays.Player;
using UnityEngine;

namespace ColourWays.Level
{
    /// <summary>
    /// A floating paint blob placed along the route. In the all-white maze it's the only thing you
    /// can see from a distance, so blobs loosely lead the way. Touching one:
    ///  • bursts it: huge splats on the floor and a fountain of droplets that paint the walls
    ///    around it (revealing that part of the maze),
    ///  • switches your paint to its colour,
    ///  • makes it the checkpoint you respawn at (R).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class PaintBlob : MonoBehaviour
    {
        [SerializeField] private Color colour = new Color(1f, 0.25f, 0.42f);
        [Tooltip("Floats and wobbles; hidden once collected.")]
        [SerializeField] private Transform visual;
        [Tooltip("Small drops orbiting the blob (optional).")]
        [SerializeField] private Transform orbit;
        [SerializeField] private Renderer[] tinted;

        [Header("Splash")]
        [SerializeField] private float splashRadius = 2f;
        [SerializeField] private int droplets = 150;
        [Tooltip("A soft puddle of the blob's colour under it from the start.")]
        [SerializeField, Range(0f, 1f)] private float puddleAlpha = 0.45f;

        [Header("Motion")]
        [SerializeField] private float hoverHeight = 0.9f;
        [SerializeField] private float bobHeight = 0.15f;
        [SerializeField] private float bobSpeed = 2f;
        [SerializeField] private float wobble = 0.08f;
        [SerializeField] private float orbitSpeed = 70f;

        public Color Colour => colour;
        public bool Collected { get; private set; }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private float _phase, _pop = -1f;
        private bool _puddle;

        private void Reset()
        {
            GetComponent<SphereCollider>().isTrigger = true;
        }

        private void Awake()
        {
            _phase = Random.Range(0f, 10f);
            ApplyColour();
        }

        private void OnValidate() => ApplyColour();

        private void ApplyColour()
        {
            if (tinted == null) return;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, colour);
            foreach (var r in tinted) if (r != null) r.SetPropertyBlock(mpb);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            var paint = PaintManager.Instance;
            if (!_puddle && paint != null && paint.IsBuilt)
            {
                _puddle = true;
                Puddle(paint);
            }

            if (visual == null) return;
            if (Collected)
            {
                if (_pop < 0f) return;
                // Swell, then burst away to nothing.
                _pop += dt / 0.35f;
                float s = _pop < 0.35f ? Mathf.Lerp(1f, 1.45f, _pop / 0.35f) : Mathf.Lerp(1.45f, 0f, (_pop - 0.35f) / 0.65f);
                visual.localScale = Vector3.one * Mathf.Max(0f, s);
                if (_pop >= 1f) { visual.gameObject.SetActive(false); _pop = -1f; }
                return;
            }

            _phase += dt * bobSpeed;
            float y = hoverHeight + Mathf.Sin(_phase) * bobHeight;
            float squash = Mathf.Sin(_phase * 2f) * wobble;
            visual.localPosition = new Vector3(0f, y, 0f);
            visual.localScale = new Vector3(1f + squash, 1f - squash, 1f + squash);
            if (orbit != null) orbit.localRotation = Quaternion.Euler(0f, _phase * orbitSpeed / bobSpeed, 0f);
        }

        private void Puddle(PaintManager paint)
        {
            if (puddleAlpha <= 0f) return;
            if (!Physics.Raycast(transform.position + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore)) return;
            var c = colour;
            c.a = puddleAlpha;
            paint.Paint(hit.point + hit.normal * 0.01f, hit.normal, 0.55f, c, BrushShape.Splat);
            for (int i = 0; i < 4; i++)
            {
                Vector2 o = Random.insideUnitCircle.normalized * Random.Range(0.6f, 1.1f);
                paint.Paint(hit.point + new Vector3(o.x, 0.01f, o.y), hit.normal, Random.Range(0.08f, 0.16f), c, BrushShape.Splat);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Collected) return;
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;

            Collected = true;
            _pop = 0f;

            var paint = PaintManager.Instance;
            if (paint != null) paint.SetColour(colour);
            var emitter = player.GetComponentInChildren<PaintSplatEmitter>();
            if (emitter != null) emitter.BigSplash(transform.position, colour, droplets, splashRadius);
            player.SetCheckpoint(transform.position, player.transform.eulerAngles.y);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(colour.r, colour.g, colour.b, 0.6f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * hoverHeight, 0.45f);
        }
    }
}
