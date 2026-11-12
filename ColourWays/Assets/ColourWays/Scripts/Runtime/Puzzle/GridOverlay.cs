using UnityEngine;

namespace ColourWays.Puzzle
{
    /// <summary>
    /// Placement grid drawn on top of everything at floor height. Hidden by default so the world
    /// stays pure white; fades in only while an instruction is being dragged, and highlights the
    /// hovered cell. It never reveals walls or pits.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class GridOverlay : MonoBehaviour
    {
        private static readonly int OriginId = Shader.PropertyToID("_GridOrigin");
        private static readonly int HoverId = Shader.PropertyToID("_Hover");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        [SerializeField] private float fadeSpeed = 8f;
        [Tooltip("Keep the grid visible all the time (accessibility / debugging).")]
        [SerializeField] private bool alwaysVisible;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private Vector4 _hover = new Vector4(-1, -1, 0, 0);
        private float _fade, _fadeTarget;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
        }

        /// <param name="origin">World position of the grid's min corner.</param>
        public void Configure(Vector3 origin, int width, int height)
        {
            if (_renderer == null) Awake();
            transform.SetPositionAndRotation(origin + new Vector3(width * 0.5f, 0.01f, height * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            transform.localScale = new Vector3(width + 0.2f, height + 0.2f, 1f);
            _mpb.SetVector(OriginId, new Vector4(origin.x, origin.z, width, height));
            Apply();
        }

        public void SetVisible(bool visible) => _fadeTarget = visible ? 1f : 0f;

        /// <summary>Highlight a cell. state: 1 = valid drop, 2 = invalid.</summary>
        public void SetHover(Vector2Int cell, bool valid)
        {
            _hover = new Vector4(cell.x, cell.y, valid ? 1f : 2f, 0f);
            Apply();
        }

        public void ClearHover()
        {
            _hover = new Vector4(-1, -1, 0, 0);
            Apply();
        }

        private void Update()
        {
            float target = alwaysVisible ? 1f : _fadeTarget;
            if (Mathf.Approximately(_fade, target)) return;
            _fade = Mathf.MoveTowards(_fade, target, fadeSpeed * Time.unscaledDeltaTime);
            Apply();
        }

        private void Apply()
        {
            _mpb.SetVector(HoverId, _hover);
            _mpb.SetFloat(FadeId, _fade);
            _renderer.SetPropertyBlock(_mpb);
            _renderer.enabled = _fade > 0.001f;
        }
    }
}
