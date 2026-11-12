using UnityEngine;

namespace ColourWays.Puzzle
{
    /// <summary>
    /// World-space tile for a placed instruction (drawn over the hidden level): a vibrant rounded
    /// tile in the instruction's colour with a white glyph, like a metro station marker. Animated: drops in on placement, glides as a drag
    /// preview, turns smoothly when an arrow is rotated, and shrinks away when removed.
    /// </summary>
    public sealed class InstructionMarker : MonoBehaviour
    {
        [SerializeField] private Renderer background;
        [SerializeField] private Renderer icon;
        [SerializeField] private Transform iconPivot;
        [SerializeField] private Color glyphColour = Color.white;
        [SerializeField] private Color invalidColour = new Color(0.62f, 0.6f, 0.57f);
        [SerializeField, Range(0f, 1f)] private float previewAlpha = 0.75f;

        [Header("Animation")]
        [SerializeField] private float dropDuration = 0.32f;
        [SerializeField] private float dropHeight = 0.55f;
        [SerializeField] private float hideDuration = 0.16f;
        [SerializeField] private float glideSpeed = 22f;
        [SerializeField] private float turnSpeed = 18f;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _mpb;
        private Vector3 _baseScale = Vector3.one;
        private Vector3 _rest;
        private float _drop = 1f;
        private float _hide = -1f;
        private bool _preview;
        private bool _hasShown;
        private InstructionType _type;
        private Quaternion _pivotTarget = Quaternion.identity;

        public bool IsHiding => _hide >= 0f;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _baseScale = transform.localScale;
        }

        /// <summary>Show as a placed instruction (drops in, or turns if it's the same tile re-shown).</summary>
        public void Show(Placement placement, InstructionSet.Entry entry, Vector3 position)
        {
            bool sameTile = _hasShown && !_preview && !IsHiding && gameObject.activeSelf && _type == placement.Type;
            _preview = false;
            _hide = -1f;
            _rest = position;
            _type = placement.Type;
            Apply(placement, entry, entry != null ? entry.colour : Color.white, glyphColour, 1f, !sameTile);
            if (!sameTile)
            {
                _drop = 0f;
                transform.position = _rest + Vector3.up * dropHeight;
            }
            _hasShown = true;
        }

        /// <summary>Show as the translucent drag preview; glides to <paramref name="position"/>.</summary>
        public void ShowPreview(Placement placement, InstructionSet.Entry entry, Vector3 position, bool valid)
        {
            bool wasVisible = _preview && gameObject.activeSelf && _hasShown;
            _preview = true;
            _hide = -1f;
            _rest = position;
            _type = placement.Type;
            if (!wasVisible) transform.position = position;
            Color tile = valid ? (entry != null ? entry.colour : Color.white) : invalidColour;
            Apply(placement, entry, tile, glyphColour, previewAlpha, !wasVisible);
            _hasShown = true;
        }

        /// <summary>Shrink away, then deactivate.</summary>
        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            _hide = 0f;
        }

        private void Apply(Placement placement, InstructionSet.Entry entry, Color tile, Color glyph, float alpha, bool snapRotation)
        {
            _mpb ??= new MaterialPropertyBlock();
            tile.a = alpha;
            glyph.a = alpha;

            if (background != null)
            {
                background.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorId, tile);
                background.SetPropertyBlock(_mpb);
            }

            if (icon != null)
            {
                icon.GetPropertyBlock(_mpb);
                if (entry != null && entry.icon != null)
                    _mpb.SetTexture(MainTexId, entry.icon.texture);
                _mpb.SetColor(ColorId, glyph);
                icon.SetPropertyBlock(_mpb);
            }

            // Arrows point in world space; relative instructions are drawn "forward = up the grid".
            float yaw = placement.Type == InstructionType.Arrow ? placement.Dir.Yaw() : 0f;
            _pivotTarget = Quaternion.Euler(0f, yaw, 0f);
            if (iconPivot != null && snapRotation)
                iconPivot.localRotation = _pivotTarget;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (iconPivot != null)
                iconPivot.localRotation = Quaternion.Slerp(iconPivot.localRotation, _pivotTarget, 1f - Mathf.Exp(-turnSpeed * dt));

            if (_hide >= 0f)
            {
                _hide += dt / hideDuration;
                float s = 1f - Mathf.Clamp01(_hide);
                transform.localScale = _baseScale * (s * s);
                transform.position += Vector3.up * dt * 0.6f;
                if (_hide >= 1f)
                {
                    _hide = -1f;
                    gameObject.SetActive(false);
                }
                return;
            }

            if (_preview)
            {
                float t = Time.unscaledTime;
                Vector3 hover = _rest + Vector3.up * (0.08f + Mathf.Sin(t * 7f) * 0.03f);
                transform.position = Vector3.Lerp(transform.position, hover, 1f - Mathf.Exp(-glideSpeed * dt));
                transform.localScale = _baseScale * (1.06f + Mathf.Sin(t * 7f) * 0.03f);
                return;
            }

            if (_drop < 1f)
            {
                _drop = Mathf.Min(1f, _drop + dt / dropDuration);
                // Fall with gravity-like easing, then a squash-and-settle on impact.
                float fall = Mathf.Clamp01(_drop / 0.6f);
                float height = (1f - fall * fall) * dropHeight;
                float impact = Mathf.Clamp01((_drop - 0.6f) / 0.4f);
                float squash = _drop < 0.6f ? 0f : Mathf.Sin(impact * Mathf.PI) * (1f - impact * 0.5f);
                transform.position = _rest + Vector3.up * height;
                transform.localScale = new Vector3(_baseScale.x * (1f + squash * 0.25f), _baseScale.y, _baseScale.z * (1f + squash * 0.25f));
                return;
            }

            transform.position = _rest;
            transform.localScale = _baseScale;
        }
    }
}
