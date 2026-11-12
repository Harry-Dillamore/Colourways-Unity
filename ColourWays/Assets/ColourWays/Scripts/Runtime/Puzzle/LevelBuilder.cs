using System.Collections.Generic;
using ColourWays.Paint;
using UnityEngine;

namespace ColourWays.Puzzle
{
    /// <summary>
    /// Turns a <see cref="LevelData"/> into scene objects from prefabs (all editable in Prefabs/Puzzle).
    ///  • Board: a raised, paintable paper plinth with a visible rim — this is what dudes paint.
    ///  • Walls: paintable but invisible until painted (shader discards unpainted pixels).
    ///  • Pits: look like floor until a dude falls in and leaves an ink hole.
    ///  • World: a big tiled ground plane and seeded scenery so the board sits in a larger place.
    /// Cell size is 1 unit; the board is centred on this transform with its top at y = 0.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelBuilder : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject boardPrefab;
        [SerializeField] private GameObject wallPrefab;
        [SerializeField] private GameObject rimPrefab;
        [SerializeField] private GameObject groundPrefab;
        [SerializeField] private GameObject startPrefab;
        [SerializeField] private GameObject exitPrefab;
        [SerializeField] private InstructionMarker markerPrefab;
        [SerializeField] private GridOverlay grid;

        [Header("Scenery")]
        [SerializeField] private GameObject sceneryPrefab;
        [Tooltip("First entries are neutrals (used most), later entries are accents.")]
        [SerializeField] private Material[] sceneryMaterials;
        [SerializeField, Range(0, 1)] private float accentChance = 0.06f;
        [SerializeField] private int sceneryCount = 22;
        [SerializeField] private float sceneryRadius = 36f;
        [Tooltip("Keep decor well away so it never hints where the board edge is.")]
        [SerializeField] private float sceneryClearance = 7f;

        [Header("Look")]
        [Tooltip("Wall material used when the layout is shown (debug / easy mode).")]
        [SerializeField] private Material revealedWallMaterial;

        [Header("Dimensions")]
        [SerializeField] private float boardThickness = 0.6f;
        [SerializeField] private float wallHeight = 0.8f;
        [Tooltip("Invisible board extension past the edge where fallen dudes leave ink holes.")]
        [SerializeField, Range(0, 3)] private int apronCells = 1;
        [SerializeField] private float rimHeight = 0.22f;
        [SerializeField] private float rimWidth = 0.3f;
        [SerializeField] private float groundSize = 240f;

        public float WallHeight => wallHeight;
        public LevelData Level { get; private set; }

        private Transform _root;
        private readonly Dictionary<Vector2Int, InstructionMarker> _markers = new Dictionary<Vector2Int, InstructionMarker>();
        private readonly Stack<InstructionMarker> _markerPool = new Stack<InstructionMarker>();
        private InstructionMarker _preview;

        public Vector3 CellToWorld(Vector2Int c)
        {
            if (Level == null) return transform.position;
            return transform.position + new Vector3(c.x - (Level.Width - 1) * 0.5f, 0f, c.y - (Level.Height - 1) * 0.5f);
        }

        public bool WorldToCell(Vector3 world, out Vector2Int cell)
        {
            cell = default;
            if (Level == null) return false;
            Vector3 local = world - transform.position;
            cell = new Vector2Int(Mathf.FloorToInt(local.x + Level.Width * 0.5f), Mathf.FloorToInt(local.z + Level.Height * 0.5f));
            return Level.InBounds(cell);
        }

        /// <summary>Board area including the rim (used to frame the camera).</summary>
        public Bounds BoardBounds
        {
            get
            {
                if (Level == null) return new Bounds(transform.position, Vector3.one);
                var size = new Vector3(Level.Width + 1f, wallHeight + boardThickness, Level.Height + 1f);
                return new Bounds(transform.position + Vector3.up * (wallHeight - boardThickness) * 0.5f, size);
            }
        }

        public void Build(LevelData level, bool hideLayout)
        {
            Level = level;
            var paint = PaintManager.Instance;
            if (paint != null) paint.DeferRegistration = true;

            if (_root != null)
            {
                _root.gameObject.SetActive(false); // unregisters paintable surfaces immediately
                Destroy(_root.gameObject);
            }
            _markers.Clear();
            _markerPool.Clear();
            _preview = null;

            _root = new GameObject("Level (generated)").transform;
            _root.SetParent(transform, false);
            var board = Group("Board");
            var walls = Group("Walls (hidden)");
            var world = Group("World");

            float w = level.Width, h = level.Height, t = boardThickness;
            Vector3 o = transform.position;

            // Board plinth (top at y = 0) plus an apron past the edge: dudes that walk off the
            // board fall there and leave an ink hole, so edges are discovered, never drawn.
            float span = apronCells * 2f;
            Box(boardPrefab, board, "Board", o + new Vector3(0f, -t * 0.5f, 0f), new Vector3(w + span, t, h + span));

            // Optional visible rim (off by default: the board edge stays invisible).
            if (rimPrefab != null)
            {
                float rimH = rimHeight + t, rimY = o.y + (rimHeight - t) * 0.5f;
                Box(rimPrefab, board, "Rim S", new Vector3(o.x, rimY, o.z - h * 0.5f - rimWidth * 0.5f), new Vector3(w + rimWidth * 2f, rimH, rimWidth));
                Box(rimPrefab, board, "Rim N", new Vector3(o.x, rimY, o.z + h * 0.5f + rimWidth * 0.5f), new Vector3(w + rimWidth * 2f, rimH, rimWidth));
                Box(rimPrefab, board, "Rim W", new Vector3(o.x - w * 0.5f - rimWidth * 0.5f, rimY, o.z), new Vector3(rimWidth, rimH, h));
                Box(rimPrefab, board, "Rim E", new Vector3(o.x + w * 0.5f + rimWidth * 0.5f, rimY, o.z), new Vector3(rimWidth, rimH, h));
            }

            // Walls: merge horizontal runs into single boxes (fewer objects / draw calls).
            for (int y = 0; y < level.Height; y++)
            {
                int x = 0;
                while (x < level.Width)
                {
                    if (level.Get(new Vector2Int(x, y)) != CellType.Wall) { x++; continue; }
                    int start = x;
                    while (x < level.Width && level.Get(new Vector2Int(x, y)) == CellType.Wall) x++;
                    int len = x - start;
                    Vector3 a = CellToWorld(new Vector2Int(start, y)), b = CellToWorld(new Vector2Int(x - 1, y));
                    var wall = Box(wallPrefab, walls, $"Wall {start}-{x - 1},{y}", (a + b) * 0.5f + Vector3.up * wallHeight * 0.5f, new Vector3(len, wallHeight, 1f));
                    if (!hideLayout && revealedWallMaterial != null && wall != null)
                        wall.GetComponent<Renderer>().sharedMaterial = revealedWallMaterial;
                }
            }

            // World around the board.
            if (groundPrefab != null)
                Box(groundPrefab, world, "Ground", o + new Vector3(0f, -t - 0.25f, 0f), new Vector3(groundSize, 0.5f, groundSize));
            BuildScenery(level, world, o.y - t);

            if (startPrefab != null)
                Instantiate(startPrefab, CellToWorld(level.Start), Quaternion.Euler(0f, level.StartDir.Yaw(), 0f), _root).name = "Start";
            if (exitPrefab != null)
                Instantiate(exitPrefab, CellToWorld(level.Exit), Quaternion.identity, _root).name = "Exit";

            if (grid != null)
                grid.Configure(CellToWorld(Vector2Int.zero) - new Vector3(0.5f, 0f, 0.5f), level.Width, level.Height);

            if (paint != null)
            {
                paint.DeferRegistration = false;
                paint.RebuildAtlas();
            }
        }

        private void BuildScenery(LevelData level, Transform parent, float groundY)
        {
            if (sceneryPrefab == null || sceneryMaterials == null || sceneryMaterials.Length == 0) return;

            var rng = new System.Random(level.Seed * 31 + 7);
            float halfW = level.Width * 0.5f + rimWidth + sceneryClearance;
            float halfH = level.Height * 0.5f + rimWidth + sceneryClearance;
            var exclusion = new Rect(-halfW, -halfH, halfW * 2f, halfH * 2f);
            var placed = new List<Rect>();
            float[] heights = { 0.5f, 0.5f, 1f, 1f, 1f, 2f, 2f, 3f, 4f };
            int neutrals = Mathf.Max(1, Mathf.Min(2, sceneryMaterials.Length));

            for (int attempt = 0; attempt < sceneryCount * 6 && placed.Count < sceneryCount; attempt++)
            {
                bool accentCube = rng.NextDouble() < accentChance && sceneryMaterials.Length > neutrals;
                int sx = accentCube ? 1 : 1 + rng.Next(6);
                int sz = accentCube ? 1 : 1 + rng.Next(6);
                float sy = accentCube ? 1f : heights[rng.Next(heights.Length)];
                int r = Mathf.RoundToInt(sceneryRadius);
                int cx = rng.Next(-r, r + 1), cz = rng.Next(-r, r + 1);
                var rect = new Rect(cx - sx * 0.5f, cz - sz * 0.5f, sx, sz);

                if (rect.Overlaps(exclusion)) continue;
                var padded = new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f);
                bool clash = false;
                foreach (var p in placed)
                    if (p.Overlaps(padded)) { clash = true; break; }
                if (clash) continue;

                placed.Add(rect);
                var pos = transform.position + new Vector3(rect.center.x, groundY + sy * 0.5f, rect.center.y);
                var block = Box(sceneryPrefab, parent, accentCube ? "Accent" : "Block", pos, new Vector3(sx, sy, sz));
                if (block == null) continue;
                var mat = accentCube
                    ? sceneryMaterials[neutrals + rng.Next(sceneryMaterials.Length - neutrals)]
                    : sceneryMaterials[rng.Next(neutrals)];
                block.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        private Transform Group(string name)
        {
            var g = new GameObject(name).transform;
            g.SetParent(_root, false);
            return g;
        }

        private static GameObject Box(GameObject prefab, Transform parent, string name, Vector3 position, Vector3 scale)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, position, Quaternion.identity, parent);
            go.name = name;
            go.transform.localScale = scale;
            return go;
        }

        // ------------------------------------------------------------------ Markers

        public void SetMarker(Vector2Int cell, Placement placement, InstructionSet.Entry entry)
        {
            if (markerPrefab == null || _root == null) return;
            if (!_markers.TryGetValue(cell, out var marker))
            {
                marker = _markerPool.Count > 0 ? _markerPool.Pop() : Instantiate(markerPrefab, _root);
                marker.gameObject.SetActive(true);
                _markers[cell] = marker;
            }
            marker.Show(placement, entry, CellToWorld(cell) + Vector3.up * 0.02f);
        }

        public void ClearMarker(Vector2Int cell)
        {
            if (!_markers.TryGetValue(cell, out var marker)) return;
            _markers.Remove(cell);
            marker.Hide(); // shrinks away, then deactivates itself
            _markerPool.Push(marker);
        }

        public void ClearMarkers()
        {
            foreach (var m in _markers.Values)
            {
                m.Hide();
                _markerPool.Push(m);
            }
            _markers.Clear();
        }

        /// <summary>Translucent marker shown on the hovered cell while dragging.</summary>
        public void ShowPreview(Vector2Int cell, Placement placement, InstructionSet.Entry entry, bool valid)
        {
            if (markerPrefab == null || _root == null) return;
            if (_preview == null)
            {
                _preview = Instantiate(markerPrefab, _root);
                _preview.name = "Drag Preview";
            }
            _preview.gameObject.SetActive(true);
            _preview.ShowPreview(placement, entry, CellToWorld(cell) + Vector3.up * 0.04f, valid);
        }

        public void HidePreview()
        {
            if (_preview != null && _preview.gameObject.activeSelf) _preview.Hide();
        }

        /// <summary>Hide the preview instantly (used when the instruction lands in the same cell).</summary>
        public void HidePreviewImmediate()
        {
            if (_preview != null) _preview.gameObject.SetActive(false);
        }
    }
}
