using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColourWays.Paint
{
    public enum BrushShape
    {
        /// <summary>Clean round print (footsteps).</summary>
        Round = 0,
        /// <summary>Irregular, noisy-edged blob (landing splats / droplets).</summary>
        Splat = 1,
    }

    /// <summary>
    /// Owns the shared paint atlas and stamps brushes into it.
    ///
    /// Port of the Unreal flow (FindCollisionUV + DrawMaterialToRenderTarget per surface) with
    /// two optimisations: one atlas + one material for every surface, and brushes are stamped
    /// in UV space using world-space distance, so no UV raycasts or MeshColliders are needed and
    /// splats wrap naturally around edges.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PaintManager : MonoBehaviour
    {
        public static PaintManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [Header("Atlas")]
        [Tooltip("Resolution of the shared paint atlas. Clamped to the GPU's max texture size.")]
        [SerializeField] private int atlasSize = 4096;
        [Tooltip("Paint resolution in texels per metre. Automatically reduced if the level doesn't fit in the atlas.")]
        [SerializeField, Min(1f)] private float texelsPerMetre = 40f;
        [Tooltip("Empty texels around each chart.")]
        [SerializeField, Range(1, 8)] private int chartPadding = 2;
        [Tooltip("Generate mipmaps after painting. Reduces shimmer on distant paint at a small GPU cost per painted frame.")]
        [SerializeField] private bool generateMipmaps;

        [Header("Paint")]
        [SerializeField] private PaintPalette palette;
        [Tooltip("Hidden/ColourWays/PaintStamp material.")]
        [SerializeField] private Material stampMaterial;
        [Tooltip("Seed for colour selection. 0 = random every run (set this for a deterministic daily level).")]
        [SerializeField] private int seed;

        [Header("Brush Look")]
        [SerializeField, Range(0f, 1f)] private float roundEdgeSoftness = 0.12f;
        [SerializeField, Range(0f, 1f)] private float roundEdgeNoise = 0.12f;
        [SerializeField, Range(0f, 1f)] private float splatEdgeSoftness = 0.06f;
        [SerializeField, Range(0f, 1.5f)] private float splatEdgeNoise = 0.7f;

        [Header("Trail Fade")]
        [Tooltip("Hidden/ColourWays/PaintFade material. Leave empty to keep paint forever.")]
        [SerializeField] private Material fadeMaterial;
        [Tooltip("Seconds for fully painted texels to fade back to the floor (0 = never fade).")]
        [SerializeField, Min(0f)] private float trailFadeSeconds;

        /// <summary>Multiplier on fade time (0 while the game is paused, higher on fast-forward).</summary>
        public float FadeTimeScale { get; set; } = 1f;

        public event Action<Color> ColourChanged;

        public Color CurrentColour { get; private set; } = Color.white;
        public bool IsBuilt { get; private set; }
        public RenderTexture Atlas => _atlas;
        public float TexelsPerMetre => _density;

        private static readonly int AtlasId = Shader.PropertyToID("_CW_PaintAtlas");
        private static readonly int AtlasTexelId = Shader.PropertyToID("_CW_PaintAtlasTexel");
        private static readonly int EnabledId = Shader.PropertyToID("_CW_PaintEnabled");
        private static readonly int BrushPosRadiusId = Shader.PropertyToID("_CW_BrushPosRadius");
        private static readonly int BrushNormalId = Shader.PropertyToID("_CW_BrushNormal");
        private static readonly int BrushColourId = Shader.PropertyToID("_CW_BrushColour");
        private static readonly int BrushParamsId = Shader.PropertyToID("_CW_BrushParams");
        private static readonly int FadeAmountId = Shader.PropertyToID("_CW_FadeAmount");
        private float _fadeAccumulator;

        private struct Stamp
        {
            public Vector3 Position;
            public Vector3 Normal;
            public float Radius;
            public Color Colour;
            public BrushShape Shape;
            public float Seed;
        }

        private readonly List<Stamp> _pending = new List<Stamp>(64);
        private readonly List<PaintableSurface> _surfaces = new List<PaintableSurface>(128);
        private RenderTexture _atlas;
        private CommandBuffer _cmd;
        private ShelfPacker _packer;
        private RectInt _blankRect;
        private float _density;
        private int _size;
        private System.Random _rng;
        private int _colourIndex = -1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[ColourWays] More than one PaintManager in the scene; disabling the extra one.", this);
                enabled = false;
                return;
            }

            Instance = this;
            _rng = seed != 0 ? new System.Random(seed) : new System.Random();
            _cmd = new CommandBuffer { name = "ColourWays Paint" };
            CreateAtlas();
            NextColour();
        }

        private void Start()
        {
            if (Instance == this && !IsBuilt)
                BuildAtlas();
        }

        /// <summary>
        /// While true, surfaces enabled at runtime don't allocate atlas space individually.
        /// Set it while spawning a whole level, then call <see cref="RebuildAtlas"/>.
        /// </summary>
        public bool DeferRegistration { get; set; }

        /// <summary>Wipe the atlas and re-pack every enabled surface (e.g. after generating a new level).</summary>
        public void RebuildAtlas()
        {
            _pending.Clear();
            ClearPaint();
            _surfaces.Clear();
            IsBuilt = false;
            BuildAtlas();
        }

        /// <summary>Atlas size used for this platform.</summary>
        public void SetAtlasSize(int size)
        {
            if (size == atlasSize && _atlas != null) return;
            atlasSize = size;
            if (_atlas != null)
            {
                _atlas.Release();
                Destroy(_atlas);
            }
            CreateAtlas();
            if (IsBuilt) RebuildAtlas();
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            Shader.SetGlobalFloat(EnabledId, 0f);
            _cmd?.Release();
            if (_quadMesh != null) Destroy(_quadMesh);
            if (_atlas != null)
            {
                _atlas.Release();
                Destroy(_atlas);
            }
            Instance = null;
        }

        // ------------------------------------------------------------------ Public API

        /// <summary>Queue a brush stamp at a world position. Applied at the end of the frame.</summary>
        public void Paint(Vector3 position, Vector3 normal, float radius, Color colour, BrushShape shape = BrushShape.Round)
        {
            if (radius <= 0f)
                return;

            _pending.Add(new Stamp
            {
                Position = position,
                Normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up,
                Radius = radius,
                Colour = colour,
                Shape = shape,
                Seed = (float)(_rng.NextDouble() * 100.0),
            });
        }

        /// <summary>Paint with the current colour.</summary>
        public void Paint(Vector3 position, Vector3 normal, float radius, BrushShape shape = BrushShape.Round)
            => Paint(position, normal, radius, CurrentColour, shape);

        /// <summary>Pick a new random colour from the palette (never the same twice in a row).</summary>
        public Color NextColour()
        {
            var colours = palette != null ? palette.Colours : null;
            if (colours == null || colours.Count == 0)
            {
                CurrentColour = new Color(1f, 0.31f, 0.6f);
            }
            else
            {
                int index = 0;
                if (colours.Count > 1)
                {
                    do index = _rng.Next(colours.Count);
                    while (index == _colourIndex);
                }
                _colourIndex = index;
                CurrentColour = colours[index];
            }

            ColourChanged?.Invoke(CurrentColour);
            return CurrentColour;
        }

        /// <summary>Make a specific colour the current one (e.g. a paint blob's colour).</summary>
        public void SetColour(Color colour)
        {
            CurrentColour = colour;
            ColourChanged?.Invoke(CurrentColour);
        }

        /// <summary>Re-seed colour selection (for a deterministic daily level).</summary>
        public void SetSeed(int newSeed)
        {
            seed = newSeed;
            _rng = new System.Random(newSeed);
            _colourIndex = -1;
        }

        /// <summary>Wipe all paint from every surface.</summary>
        public void ClearPaint()
        {
            _pending.Clear();
            if (_atlas == null)
                return;

            _cmd.Clear();
            _cmd.SetRenderTarget(_atlas);
            _cmd.ClearRenderTarget(false, true, Color.clear);
            Graphics.ExecuteCommandBuffer(_cmd);
            _cmd.Clear();
            if (generateMipmaps)
                _atlas.GenerateMips();
        }

        // ------------------------------------------------------------------ Atlas

        private void CreateAtlas()
        {
            _size = Mathf.ClosestPowerOfTwo(Mathf.Clamp(atlasSize, 256, SystemInfo.maxTextureSize));

            var desc = new RenderTextureDescriptor(_size, _size, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
                useMipMap = generateMipmaps,
                autoGenerateMips = false,
                msaaSamples = 1,
            };

            _atlas = new RenderTexture(desc)
            {
                name = "CW_PaintAtlas",
                filterMode = generateMipmaps ? FilterMode.Trilinear : FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = generateMipmaps ? 4 : 0,
            };
            _atlas.Create();

            var previous = RenderTexture.active;
            RenderTexture.active = _atlas;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;

            Shader.SetGlobalTexture(AtlasId, _atlas);
            Shader.SetGlobalVector(AtlasTexelId, new Vector4(1f / _size, 1f / _size, _size, _size));
            Shader.SetGlobalFloat(EnabledId, 1f);
        }

        private void BuildAtlas()
        {
            var candidates = new List<PaintableSurface>();
            foreach (var surface in PaintableSurface.All)
            {
                if (surface != null && surface.BuildCharts())
                    candidates.Add(surface);
            }

            _density = texelsPerMetre;
            int pad = chartPadding;

            // Shrink density up-front if the total area clearly won't fit.
            double area = 0;
            foreach (var s in candidates)
            {
                for (int c = 0; c < 6; c++)
                {
                    var ch = s.Charts[c];
                    if (!ch.HasTriangles || !ch.Paintable) continue;
                    Vector2 m = s.ChartSize(c) * s.TexelDensityScale;
                    area += (m.x * _density + pad * 2) * (m.y * _density + pad * 2);
                }
            }
            double budget = (double)_size * _size * 0.85;
            if (area > budget)
                _density *= (float)Math.Sqrt(budget / area);

            bool packed = false;
            for (int attempt = 0; attempt < 12 && !packed; attempt++)
            {
                packed = TryPackAll(candidates, pad);
                if (!packed)
                    _density *= 0.9f;
            }

            if (!packed)
            {
                Debug.LogError("[ColourWays] Could not fit paintable surfaces into the paint atlas. Increase the atlas size or reduce paintable faces.", this);
                return;
            }

            if (_density < texelsPerMetre - 0.01f)
                Debug.Log($"[ColourWays] Paint density reduced to {_density:0.0} texels/m to fit the atlas ({_size}x{_size}).", this);

            _surfaces.Clear();
            foreach (var s in candidates)
            {
                s.ApplyAtlas(_size, _blankRect);
                _surfaces.Add(s);
            }

            IsBuilt = true;
        }

        private struct PackItem
        {
            public PaintableSurface Surface;
            public int Chart;
            public int Width;
            public int Height;
        }

        private bool TryPackAll(List<PaintableSurface> surfaces, int pad)
        {
            _packer = new ShelfPacker(_size);
            // Reserve a small permanently-blank block for unpaintable faces.
            _packer.TryPack(4, 4, out _blankRect);
            _blankRect = new RectInt(1, 1, 2, 2);

            var items = new List<PackItem>();
            foreach (var s in surfaces)
            {
                for (int c = 0; c < 6; c++)
                {
                    var ch = s.Charts[c];
                    if (!ch.HasTriangles || !ch.Paintable) continue;
                    Vector2 px = PixelSize(s, c);
                    items.Add(new PackItem { Surface = s, Chart = c, Width = (int)px.x, Height = (int)px.y });
                }
            }

            items.Sort((a, b) => b.Height != a.Height ? b.Height.CompareTo(a.Height) : b.Width.CompareTo(a.Width));

            foreach (var item in items)
            {
                if (!_packer.TryPack(item.Width + pad * 2, item.Height + pad * 2, out RectInt outer))
                    return false;
                item.Surface.SetChartRect(item.Chart, new RectInt(outer.x + pad, outer.y + pad, item.Width, item.Height));
            }
            return true;
        }

        private Vector2 PixelSize(PaintableSurface s, int chart)
        {
            Vector2 m = s.ChartSize(chart) * (_density * s.TexelDensityScale);
            return new Vector2(Mathf.Max(1, Mathf.CeilToInt(m.x)), Mathf.Max(1, Mathf.CeilToInt(m.y)));
        }

        /// <summary>Allocate atlas space for a surface enabled after the level started.</summary>
        internal void RegisterLate(PaintableSurface s)
        {
            if (!IsBuilt || DeferRegistration || _surfaces.Contains(s) || !s.BuildCharts())
                return;

            int pad = chartPadding;
            for (int c = 0; c < 6; c++)
            {
                var ch = s.Charts[c];
                if (!ch.HasTriangles || !ch.Paintable) continue;
                Vector2 px = PixelSize(s, c);
                if (_packer.TryPack((int)px.x + pad * 2, (int)px.y + pad * 2, out RectInt outer))
                    s.SetChartRect(c, new RectInt(outer.x + pad, outer.y + pad, (int)px.x, (int)px.y));
                else
                    Debug.LogWarning($"[ColourWays] Paint atlas full; '{s.name}' face {c} won't receive paint.", s);
            }

            s.ApplyAtlas(_size, _blankRect);
            _surfaces.Add(s);
        }

        /// <summary>
        /// Linear fade of the whole atlas so trails read like live flows. Applied in chunks of at
        /// least ~2/255 alpha so 8-bit precision never stalls the fade.
        /// </summary>
        private void FadeTrails()
        {
            if (fadeMaterial == null || trailFadeSeconds <= 0f || !IsBuilt || _atlas == null) return;
            _fadeAccumulator += Time.deltaTime * Mathf.Max(0f, FadeTimeScale);
            float minChunk = trailFadeSeconds * (2f / 255f);
            if (_fadeAccumulator < minChunk) return;

            float amount = _fadeAccumulator / trailFadeSeconds;
            _fadeAccumulator = 0f;
            _cmd.Clear();
            _cmd.SetRenderTarget(_atlas);
            _cmd.SetGlobalFloat(FadeAmountId, amount);
            _cmd.DrawProcedural(Matrix4x4.identity, fadeMaterial, 0, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(_cmd);
            _cmd.Clear();
        }

        // ------------------------------------------------------------------ Stamping

        private void LateUpdate()
        {
            FadeTrails();
            if (_pending.Count == 0 || !IsBuilt || stampMaterial == null)
                return;

            if (!_atlas.IsCreated())
                _atlas.Create();

            _cmd.Clear();
            _cmd.SetRenderTarget(_atlas);

            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            bool drewAnything = false;
            ClearQuads();

            for (int i = 0; i < _pending.Count; i++)
            {
                Stamp st = _pending[i];
                // Brush is a sphere; pad slightly for noisy splat edges.
                float reach = st.Radius * (st.Shape == BrushShape.Splat ? 1f + splatEdgeNoise * 0.6f : 1.1f);
                var brushBounds = new Bounds(st.Position, Vector3.one * (reach * 2f));
                Color c = linear ? st.Colour.linear : st.Colour;
                bool splat = st.Shape == BrushShape.Splat;
                var posRadius = new Vector4(st.Position.x, st.Position.y, st.Position.z, st.Radius);
                var brushParams = new Vector4((float)st.Shape, splat ? splatEdgeSoftness : roundEdgeSoftness, splat ? splatEdgeNoise : roundEdgeNoise, st.Seed);
                var colour = new Vector4(c.r, c.g, c.b, c.a);

                bool globalsSet = false;
                for (int s = _surfaces.Count - 1; s >= 0; s--)
                {
                    var surface = _surfaces[s];
                    if (surface == null)
                    {
                        _surfaces.RemoveAt(s);
                        continue;
                    }
                    if (!surface.WorldBounds.Intersects(brushBounds) || !surface.isActiveAndEnabled)
                        continue;

                    if (surface.QuadStampable)
                    {
                        // Fast path: only the few texels the brush can reach on each flat face.
                        for (int chart = 0; chart < 6; chart++)
                            if (surface.TryBrushQuad(chart, st.Position, reach, _corners, _cornerUvs, out Vector3 n))
                                AddQuad(n, posRadius, st.Normal, brushParams, colour);
                        continue;
                    }

                    // Fallback: rasterise the whole surface in its paint-UV layout.
                    if (!globalsSet)
                    {
                        _cmd.SetGlobalVector(BrushPosRadiusId, posRadius);
                        _cmd.SetGlobalVector(BrushNormalId, st.Normal);
                        _cmd.SetGlobalVector(BrushColourId, colour);
                        _cmd.SetGlobalVector(BrushParamsId, brushParams);
                        globalsSet = true;
                    }
                    Mesh mesh = surface.PaintMesh;
                    Matrix4x4 matrix = surface.transform.localToWorldMatrix;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        _cmd.DrawMesh(mesh, matrix, stampMaterial, sub, 0);
                    drewAnything = true;
                }
            }

            _pending.Clear();

            if (_qPos.Count > 0)
            {
                // Every quad of every stamp this frame in one draw (pass 1 reads the brush per vertex).
                if (_quadMesh == null)
                {
                    _quadMesh = new Mesh { name = "CW_PaintStampQuads", indexFormat = IndexFormat.UInt32 };
                    _quadMesh.MarkDynamic();
                }
                _quadMesh.Clear();
                _quadMesh.SetVertices(_qPos);
                _quadMesh.SetNormals(_qNormal);
                _quadMesh.SetUVs(0, _qUv);
                _quadMesh.SetUVs(1, _qBrush);
                _quadMesh.SetUVs(2, _qBrushNormal);
                _quadMesh.SetUVs(3, _qParams);
                _quadMesh.SetUVs(4, _qColour);
                _quadMesh.SetIndices(_qIndex, MeshTopology.Triangles, 0, false);
                _quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
                _cmd.DrawMesh(_quadMesh, Matrix4x4.identity, stampMaterial, 0, 1);
                drewAnything = true;
            }

            if (drewAnything)
            {
                Graphics.ExecuteCommandBuffer(_cmd);
                if (generateMipmaps)
                    _atlas.GenerateMips();
            }
            _cmd.Clear();
        }

        // ------------------------------------------------------------------ Batched brush quads

        private Mesh _quadMesh;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector2[] _cornerUvs = new Vector2[4];
        private readonly List<Vector3> _qPos = new List<Vector3>(256);
        private readonly List<Vector3> _qNormal = new List<Vector3>(256);
        private readonly List<Vector2> _qUv = new List<Vector2>(256);
        private readonly List<Vector4> _qBrush = new List<Vector4>(256);
        private readonly List<Vector4> _qBrushNormal = new List<Vector4>(256);
        private readonly List<Vector4> _qParams = new List<Vector4>(256);
        private readonly List<Vector4> _qColour = new List<Vector4>(256);
        private readonly List<int> _qIndex = new List<int>(384);

        private void ClearQuads()
        {
            _qPos.Clear(); _qNormal.Clear(); _qUv.Clear(); _qBrush.Clear();
            _qBrushNormal.Clear(); _qParams.Clear(); _qColour.Clear(); _qIndex.Clear();
        }

        private void AddQuad(Vector3 faceNormal, Vector4 posRadius, Vector3 brushNormal, Vector4 brushParams, Vector4 colour)
        {
            int b = _qPos.Count;
            for (int k = 0; k < 4; k++)
            {
                _qPos.Add(_corners[k]);
                _qNormal.Add(faceNormal);
                _qUv.Add(_cornerUvs[k]);
                _qBrush.Add(posRadius);
                _qBrushNormal.Add(brushNormal);
                _qParams.Add(brushParams);
                _qColour.Add(colour);
            }
            _qIndex.Add(b); _qIndex.Add(b + 1); _qIndex.Add(b + 2);
            _qIndex.Add(b); _qIndex.Add(b + 2); _qIndex.Add(b + 3);
        }
    }
}
