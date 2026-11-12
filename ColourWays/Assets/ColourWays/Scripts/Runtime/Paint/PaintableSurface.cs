using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColourWays.Paint
{
    /// <summary>
    /// Marks a mesh as paintable (port of Unreal's BP_PaintableSurface).
    ///
    /// Instead of a render target per surface, every surface owns a region of one shared
    /// paint atlas managed by <see cref="PaintManager"/>. At runtime the mesh is given a
    /// box-projected "paint UV" layout (TEXCOORD2) in world-scale units so brushes keep the
    /// same size on any scaled object, plus the chart rectangle (TEXCOORD3) used by the
    /// surface shader to stop bilinear bleeding between charts.
    ///
    /// Works with any collider (BoxCollider included) because painting never needs a UV lookup.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PaintableSurface : MonoBehaviour
    {
        [Flags]
        public enum Faces
        {
            None = 0,
            PosX = 1 << 0,
            NegX = 1 << 1,
            PosY = 1 << 2,
            NegY = 1 << 3,
            PosZ = 1 << 4,
            NegZ = 1 << 5,
            All = PosX | NegX | PosY | NegY | PosZ | NegZ,
        }

        [Tooltip("Which object-space faces can receive paint. Untick faces nobody will see (e.g. the underside of floors) to save atlas space.")]
        [SerializeField] private Faces paintableFaces = Faces.All;

        [Tooltip("Multiplier on the PaintManager texel density for this surface only.")]
        [SerializeField, Min(0.05f)] private float texelDensityScale = 1f;

        private static readonly List<PaintableSurface> s_All = new List<PaintableSurface>();

        /// <summary>All enabled paintable surfaces.</summary>
        public static IReadOnlyList<PaintableSurface> All => s_All;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_All.Clear();

        internal struct Chart
        {
            public Vector2 Min;
            public Vector2 Max;
            public bool HasTriangles;
            public bool Paintable;
            public RectInt Rect; // inner rect in atlas pixels (padding excluded)
            public bool Allocated;
        }

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _sourceMesh;
        private Mesh _paintMesh;

        // Intermediate unwrap data, kept only between BuildCharts and ApplyAtlas.
        private readonly Chart[] _charts = new Chart[6];
        private List<Vector2> _projected;
        private List<int> _vertexChart;
        private List<Vector3> _positions;
        private List<Vector3> _normals;
        private List<Vector4> _tangents;
        private List<Color> _colors;
        private List<Vector2> _uv0;
        private List<Vector2> _uv1;
        private List<int>[] _submeshIndices;

        public Faces PaintableFaces => paintableFaces;
        public float TexelDensityScale => texelDensityScale;
        public Renderer Renderer => _renderer;
        public Mesh PaintMesh => _paintMesh;
        public bool IsReady => _paintMesh != null;
        internal Chart[] Charts => _charts;

        private void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
        }

        private void OnEnable()
        {
            s_All.Add(this);
            var manager = PaintManager.Instance;
            if (manager != null && manager.IsBuilt && !IsReady)
                manager.RegisterLate(this);
        }

        private void OnDisable()
        {
            s_All.Remove(this);
        }

        private void OnDestroy()
        {
            if (_paintMesh != null)
            {
                if (_filter != null && _filter.sharedMesh == _paintMesh)
                    _filter.sharedMesh = _sourceMesh;
                Destroy(_paintMesh);
                _paintMesh = null;
            }
        }

        /// <summary>
        /// Splits the mesh into up to six box-projected charts measured in metres.
        /// Returns false if the mesh can't be unwrapped.
        /// </summary>
        internal bool BuildCharts()
        {
            if (_filter == null)
                Awake();

            // Keep the original mesh: when re-packing, the filter already holds our paint mesh.
            var current = _filter.sharedMesh;
            if (_paintMesh == null || current != _paintMesh)
                _sourceMesh = current;
            if (_sourceMesh == null)
                return false;

            if (!_sourceMesh.isReadable)
            {
                Debug.LogError($"[ColourWays] '{name}': mesh '{_sourceMesh.name}' needs Read/Write enabled in its import settings to be paintable.", this);
                return false;
            }

            Vector3 scale = transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            var srcPositions = _sourceMesh.vertices;
            var srcNormals = _sourceMesh.normals;
            var srcTangents = _sourceMesh.tangents;
            var srcColors = _sourceMesh.colors;
            var srcUv0 = new List<Vector2>();
            var srcUv1 = new List<Vector2>();
            _sourceMesh.GetUVs(0, srcUv0);
            _sourceMesh.GetUVs(1, srcUv1);

            bool hasNormals = srcNormals != null && srcNormals.Length == srcPositions.Length;
            bool hasTangents = srcTangents != null && srcTangents.Length == srcPositions.Length;
            bool hasColors = srcColors != null && srcColors.Length == srcPositions.Length;
            bool hasUv0 = srcUv0.Count == srcPositions.Length;
            bool hasUv1 = srcUv1.Count == srcPositions.Length;

            int capacity = srcPositions.Length + 8;
            _positions = new List<Vector3>(capacity);
            _normals = hasNormals ? new List<Vector3>(capacity) : null;
            _tangents = hasTangents ? new List<Vector4>(capacity) : null;
            _colors = hasColors ? new List<Color>(capacity) : null;
            _uv0 = hasUv0 ? new List<Vector2>(capacity) : null;
            _uv1 = hasUv1 ? new List<Vector2>(capacity) : null;
            _projected = new List<Vector2>(capacity);
            _vertexChart = new List<int>(capacity);

            for (int c = 0; c < 6; c++)
            {
                _charts[c] = new Chart
                {
                    Min = new Vector2(float.MaxValue, float.MaxValue),
                    Max = new Vector2(float.MinValue, float.MinValue),
                    Paintable = (paintableFaces & (Faces)(1 << c)) != 0,
                };
            }

            // (source vertex, chart) -> new vertex. Vertices shared between charts are split.
            var remap = new Dictionary<long, int>(capacity);
            int subMeshCount = _sourceMesh.subMeshCount;
            _submeshIndices = new List<int>[subMeshCount];

            for (int s = 0; s < subMeshCount; s++)
            {
                if (_sourceMesh.GetTopology(s) != MeshTopology.Triangles)
                {
                    _submeshIndices[s] = new List<int>();
                    continue;
                }

                var tris = _sourceMesh.GetTriangles(s);
                var outTris = new List<int>(tris.Length);
                _submeshIndices[s] = outTris;

                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
                    Vector3 a = Vector3.Scale(srcPositions[ia], scale);
                    Vector3 b = Vector3.Scale(srcPositions[ib], scale);
                    Vector3 c = Vector3.Scale(srcPositions[ic], scale);
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    if (hasNormals)
                    {
                        // Normals transform by inverse scale; use them to fix the sign (and degenerate tris).
                        Vector3 vn = srcNormals[ia] + srcNormals[ib] + srcNormals[ic];
                        vn = new Vector3(vn.x / Mathf.Max(scale.x, 1e-6f), vn.y / Mathf.Max(scale.y, 1e-6f), vn.z / Mathf.Max(scale.z, 1e-6f));
                        if (n.sqrMagnitude < 1e-12f)
                            n = vn;
                        else if (Vector3.Dot(n, vn) < 0f)
                            n = -n;
                    }

                    int chart = DominantChart(n);

                    outTris.Add(GetOrAddVertex(ia, chart));
                    outTris.Add(GetOrAddVertex(ib, chart));
                    outTris.Add(GetOrAddVertex(ic, chart));
                }
            }

            return _positions.Count > 0;

            int GetOrAddVertex(int src, int chart)
            {
                long key = ((long)src << 3) | (long)chart;
                if (remap.TryGetValue(key, out int existing))
                    return existing;

                int index = _positions.Count;
                Vector3 p = srcPositions[src];
                _positions.Add(p);
                if (hasNormals) _normals.Add(srcNormals[src]);
                if (hasTangents) _tangents.Add(srcTangents[src]);
                if (hasColors) _colors.Add(srcColors[src]);
                if (hasUv0) _uv0.Add(srcUv0[src]);
                if (hasUv1) _uv1.Add(srcUv1[src]);

                Vector2 proj = Project(Vector3.Scale(p, scale), chart);
                _projected.Add(proj);
                _vertexChart.Add(chart);

                ref Chart ch = ref _charts[chart];
                ch.HasTriangles = true;
                ch.Min = Vector2.Min(ch.Min, proj);
                ch.Max = Vector2.Max(ch.Max, proj);

                remap.Add(key, index);
                return index;
            }
        }

        /// <summary>Size of a chart in metres.</summary>
        internal Vector2 ChartSize(int chart) => _charts[chart].Max - _charts[chart].Min;

        internal void SetChartRect(int chart, RectInt rect)
        {
            _charts[chart].Rect = rect;
            _charts[chart].Allocated = true;
        }

        /// <summary>
        /// Writes paint UVs for the allocated chart rectangles and swaps the mesh in.
        /// Non-allocated / non-paintable charts point at the atlas's reserved blank block.
        /// </summary>
        internal void ApplyAtlas(int atlasSize, RectInt blankRect)
        {
            float inv = 1f / atlasSize;
            int count = _positions.Count;
            var paintUv = new List<Vector2>(count);
            var chartRects = new List<Vector4>(count);

            Vector2 blankUv = blankRect.center * inv;
            Vector4 blankUvRect = new Vector4(blankRect.xMin * inv, blankRect.yMin * inv, blankRect.xMax * inv, blankRect.yMax * inv);

            for (int i = 0; i < count; i++)
            {
                ref Chart ch = ref _charts[_vertexChart[i]];
                if (!ch.Allocated)
                {
                    paintUv.Add(blankUv);
                    chartRects.Add(blankUvRect);
                    continue;
                }

                Vector2 size = ch.Max - ch.Min;
                Vector2 local = _projected[i] - ch.Min;
                float u = size.x > 1e-5f ? local.x / size.x : 0.5f;
                float v = size.y > 1e-5f ? local.y / size.y : 0.5f;

                RectInt r = ch.Rect;
                paintUv.Add(new Vector2((r.xMin + u * r.width) * inv, (r.yMin + v * r.height) * inv));
                chartRects.Add(new Vector4(r.xMin * inv, r.yMin * inv, r.xMax * inv, r.yMax * inv));
            }

            if (_paintMesh != null)
                Destroy(_paintMesh);

            var mesh = new Mesh { name = _sourceMesh.name + " (Paint)" };
            if (count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(_positions);
            if (_normals != null) mesh.SetNormals(_normals);
            if (_tangents != null) mesh.SetTangents(_tangents);
            if (_colors != null) mesh.SetColors(_colors);
            if (_uv0 != null) mesh.SetUVs(0, _uv0);
            if (_uv1 != null) mesh.SetUVs(1, _uv1);
            mesh.SetUVs(2, paintUv);
            mesh.SetUVs(3, chartRects);

            mesh.subMeshCount = _submeshIndices.Length;
            for (int s = 0; s < _submeshIndices.Length; s++)
                mesh.SetTriangles(_submeshIndices[s], s, false);

            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            _paintMesh = mesh;
            _filter.sharedMesh = mesh;

            // Release the intermediate data.
            _projected = null;
            _vertexChart = null;
            _positions = null;
            _normals = null;
            _tangents = null;
            _colors = null;
            _uv0 = null;
            _uv1 = null;
            _submeshIndices = null;
        }

        private static int DominantChart(Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) return n.x >= 0f ? 0 : 1;
            if (ay >= az) return n.y >= 0f ? 2 : 3;
            return n.z >= 0f ? 4 : 5;
        }

        private static Vector2 Project(Vector3 p, int chart)
        {
            switch (chart >> 1)
            {
                case 0: return new Vector2(p.z, p.y); // X faces
                case 1: return new Vector2(p.x, p.z); // Y faces
                default: return new Vector2(p.x, p.y); // Z faces
            }
        }
    }
}
