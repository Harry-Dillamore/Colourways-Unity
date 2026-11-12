using System;
using System.Collections.Generic;
using ColourWays.Paint;
using UnityEngine;

namespace ColourWays.Puzzle
{
    /// <summary>
    /// Runs the instruction-maze puzzle: generates (daily or random) levels, holds the placed
    /// instructions, runs/stops the simulation and keeps score.
    ///
    /// Scoring is golf (lower is better):
    ///   score = sum of instruction costs on the board + run time (all runs this level) x time points.
    /// Instructions can be placed, moved, rotated or removed while dudes are running; changes take
    /// effect immediately (the simulation reads the live placement table).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class PuzzleGame : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InstructionSet instructions;
        [SerializeField] private LevelBuilder builder;
        [SerializeField] private CameraRig cameraRig;
        [SerializeField] private DudeView dudePrefab;

        [Header("Rules")]
        [SerializeField] private GeneratorSettings generator = new GeneratorSettings();
        [SerializeField] private SimSettings simulation = new SimSettings();
        [Tooltip("Score points added per second of run time.")]
        [SerializeField] private float timePointsPerSecond = 1f;
        [Tooltip("Hide the level: everything is white until dudes paint it.")]
        [SerializeField] private bool hideLayout = true;

        [Header("Startup")]
        [SerializeField] private bool startWithDaily = true;
        [Tooltip("Non-zero: always load this seed (for testing a specific level).")]
        [SerializeField] private int debugSeed;
        [SerializeField] private int targetFrameRate = 60;

        public event Action LevelLoaded;
        public event Action BoardChanged;
        public event Action RunStateChanged;
        public event Action Completed;

        public LevelData Level { get; private set; }
        public InstructionSet Instructions => instructions;
        public LevelBuilder Builder => builder;
        public Camera Camera => cameraRig != null ? cameraRig.Camera : Camera.main;
        public CameraRig CameraRig => cameraRig;
        public bool IsDaily { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsComplete { get; private set; }
        public float RunTime { get; private set; }
        public int Runs { get; private set; }
        public int Saved => _sim != null ? _sim.Saved : _lastSaved;
        public int Required => simulation.requiredSaved;
        public int Par => Level != null ? Level.Par : -1;
        public IReadOnlyDictionary<Vector2Int, Placement> Placements => _placements;
        public int InstructionCost => ParSolver.InstructionCost(_placements, _costs ?? instructions.CostTable());
        public int Score => ParSolver.Score(InstructionCost, RunTime, timePointsPerSecond);
        public int BestScore => Level != null ? PlayerPrefs.GetInt(BestKey, -1) : -1;

        public string Title
        {
            get
            {
                if (Level == null) return "";
                if (!IsDaily) return $"Random #{Level.Seed}";
                var date = new DateTime(Level.Seed / 10000, Level.Seed / 100 % 100, Level.Seed % 100);
                return $"Daily · {date:d MMM yyyy}";
            }
        }

        private string BestKey => $"cw_best_{(IsDaily ? "d" : "r")}_{Level.Seed}";

        private readonly Dictionary<Vector2Int, Placement> _placements = new Dictionary<Vector2Int, Placement>();
        private readonly Dictionary<Dude, DudeView> _views = new Dictionary<Dude, DudeView>();
        private readonly Stack<DudeView> _pool = new Stack<DudeView>();
        private readonly List<Dude> _removed = new List<Dude>();
        private Transform _dudeRoot;
        private PuzzleSim _sim;
        private int[] _costs;
        private int _lastSaved;

        private void Awake()
        {
            if (targetFrameRate > 0) Application.targetFrameRate = targetFrameRate;
            _costs = instructions != null ? instructions.CostTable() : new int[5];
            _dudeRoot = new GameObject("Dudes").transform;
            _dudeRoot.SetParent(transform, false);
        }

        private void Start()
        {
            if (instructions == null || builder == null || dudePrefab == null)
            {
                Debug.LogError("[ColourWays] PuzzleGame is missing references (Instructions, Builder or Dude Prefab). " +
                               "Assign them in the Inspector or run ColourWays > Setup Puzzle Game again.", this);
                enabled = false;
                return;
            }

            if (debugSeed != 0) LoadLevel(debugSeed, false);
            else if (startWithDaily) LoadDaily();
            else NewRandomLevel();
        }

        // ------------------------------------------------------------------ Levels

        public void LoadDaily() => LoadLevel(LevelGenerator.DailySeed(DateTime.UtcNow), true);

        public void NewRandomLevel() => LoadLevel(UnityEngine.Random.Range(1, int.MaxValue), false);

        /// <summary>Restart the current level from scratch (clears paint, instructions and time).</summary>
        public void Replay()
        {
            if (Level != null) LoadLevel(Level.Seed, IsDaily);
        }

        public void LoadLevel(int seed, bool daily)
        {
            Stop();
            _costs = instructions.CostTable();
            IsDaily = daily;
            Level = LevelGenerator.Generate(seed, generator, simulation, _costs, timePointsPerSecond);

            _placements.Clear();
            RunTime = 0f;
            Runs = 0;
            _lastSaved = 0;
            IsComplete = false;

            builder.Build(Level, hideLayout);
            if (cameraRig != null) cameraRig.Fit(builder.BoardBounds);

            LevelLoaded?.Invoke();
            BoardChanged?.Invoke();
        }

        // ------------------------------------------------------------------ Board

        public bool CanPlace(Vector2Int cell) => Level != null && !IsComplete && Level.InBounds(cell) && cell != Level.Exit;

        public bool TryGetPlacement(Vector2Int cell, out Placement placement) => _placements.TryGetValue(cell, out placement);

        public bool Place(Vector2Int cell, Placement placement)
        {
            if (!CanPlace(cell)) return false;
            var entry = instructions.Get(placement.Type);
            if (entry == null || !entry.enabled) return false;

            _placements[cell] = placement;
            builder.SetMarker(cell, placement, entry);
            BoardChanged?.Invoke();
            return true;
        }

        public bool Remove(Vector2Int cell)
        {
            if (IsComplete || !_placements.Remove(cell)) return false;
            builder.ClearMarker(cell);
            BoardChanged?.Invoke();
            return true;
        }

        /// <summary>Rotate an Arrow clockwise. Returns false if the cell has no arrow.</summary>
        public bool Rotate(Vector2Int cell)
        {
            if (IsComplete || !_placements.TryGetValue(cell, out var p) || p.Type != InstructionType.Arrow) return false;
            p.Dir = p.Dir.Right();
            return Place(cell, p);
        }

        public void ClearInstructions()
        {
            if (IsComplete || _placements.Count == 0) return;
            _placements.Clear();
            builder.ClearMarkers();
            BoardChanged?.Invoke();
        }

        /// <summary>Grid cell under a screen point (ray against the floor plane, not the hidden geometry).</summary>
        public bool TryCellFromScreen(Vector2 screen, out Vector2Int cell)
        {
            cell = default;
            var cam = Camera;
            if (cam == null || Level == null) return false;
            var ray = cam.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, builder.transform.position);
            if (!plane.Raycast(ray, out float enter)) return false;
            return builder.WorldToCell(ray.GetPoint(enter), out cell);
        }

        // ------------------------------------------------------------------ Running

        public void ToggleRun()
        {
            if (IsRunning) Stop();
            else Run();
        }

        public void Run()
        {
            if (IsRunning || IsComplete || Level == null) return;
            _sim = new PuzzleSim(Level, _placements, simulation);
            _sim.DudeSpawned += OnSpawned;
            _sim.DudeBumped += OnBumped;
            _sim.DudeJumped += OnJumped;
            _sim.DudeSaved += OnSaved;
            _sim.DudeRemoved += OnRemoved;
            IsRunning = true;
            Runs++;
            Haptics.Light();
            RunStateChanged?.Invoke();
        }

        public void Stop()
        {
            if (!IsRunning && _sim == null) return;
            IsRunning = false;
            if (_sim != null) _lastSaved = _sim.Saved;
            _sim = null;
            foreach (var view in _views.Values) Recycle(view);
            _views.Clear();
            RunStateChanged?.Invoke();
        }

        private void Update()
        {
            if (!IsRunning || _sim == null) return;

            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _sim.Step(dt);
            RunTime += dt;

            foreach (var pair in _views)
                pair.Value.Sync(dt);

            foreach (var d in _removed)
            {
                if (_views.TryGetValue(d, out var v))
                {
                    Recycle(v);
                    _views.Remove(d);
                }
            }
            _removed.Clear();

            if (_sim.Complete)
                Complete();
        }

        private void Complete()
        {
            IsRunning = false;
            IsComplete = true;
            _lastSaved = _sim.Saved;

            int score = Score;
            int best = BestScore;
            if (best < 0 || score < best)
                PlayerPrefs.SetInt(BestKey, score);
            PlayerPrefs.Save();

            Haptics.Success();
            RunStateChanged?.Invoke();
            Completed?.Invoke();
        }

        /// <summary>
        /// Spoiler-free result for sharing (NYT style): title, score vs par, and one coloured
        /// square per instruction used (no positions).
        /// </summary>
        public string ShareText()
        {
            if (Level == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append("ColourWays ").AppendLine(IsDaily ? Title.Replace("Daily · ", "") : $"#{Level.Seed}");
            int diff = Par >= 0 ? Score - Par : 0;
            string vsPar = Par < 0 ? "" : diff == 0 ? " (par)" : diff < 0 ? $" ({diff} under par)" : $" (+{diff})";
            sb.Append("Score ").Append(Score).AppendLine(vsPar);

            var counts = new int[5];
            foreach (var p in _placements.Values) counts[(int)p.Type]++;
            for (int t = 0; t < counts.Length; t++)
                for (int i = 0; i < counts[t]; i++)
                    sb.Append(Emoji((InstructionType)t));
            sb.Append("  ").Append(RunTime.ToString("0.0")).Append("s");
            if (Runs > 1) sb.Append(" · ").Append(Runs).Append(" runs");
            return sb.ToString();
        }

        private static string Emoji(InstructionType t)
        {
            switch (t)
            {
                // Written as escapes so editor/tool encodings can never mangle them.
                case InstructionType.TurnLeft: return "\U0001F7E6"; // blue square
                case InstructionType.TurnRight: return "\U0001F7E7"; // orange square
                case InstructionType.Arrow: return "\U0001F7E9"; // green square
                case InstructionType.Reverse: return "\U0001F7E5"; // red square
                default: return "\U0001F7EA"; // purple square
            }
        }

        // ------------------------------------------------------------------ Sim events -> views

        private void OnSpawned(Dude d)
        {
            var view = _pool.Count > 0 ? _pool.Pop() : Instantiate(dudePrefab, _dudeRoot);
            view.gameObject.SetActive(true);
            Color colour = PaintManager.Instance != null ? PaintManager.Instance.NextColour() : Color.white;
            view.Bind(d, colour, builder);
            _views[d] = view;
        }

        private void OnBumped(Dude d, Vector2Int cell, Dir blocked)
        {
            if (_views.TryGetValue(d, out var v)) v.OnBumped(cell, blocked);
        }

        private void OnJumped(Dude d)
        {
            if (_views.TryGetValue(d, out var v)) v.OnJumped();
        }

        private void OnSaved(Dude d)
        {
            if (_views.TryGetValue(d, out var v)) v.OnSaved();
            Haptics.Tick();
        }

        private void OnRemoved(Dude d) => _removed.Add(d);

        private void Recycle(DudeView view)
        {
            view.gameObject.SetActive(false);
            _pool.Push(view);
        }
    }
}
