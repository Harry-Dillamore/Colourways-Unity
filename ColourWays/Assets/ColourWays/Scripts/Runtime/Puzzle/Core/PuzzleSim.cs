using System;
using System.Collections.Generic;
using UnityEngine;

namespace ColourWays.Puzzle
{
    [Serializable]
    public sealed class SimSettings
    {
        [Tooltip("Cells per second a dude walks.")]
        public float speed = 2.5f;
        [Tooltip("Seconds between dudes leaving the start.")]
        public float spawnInterval = 1.2f;
        [Tooltip("Dudes that must reach the exit to complete the level.")]
        public int requiredSaved = 3;
        [Tooltip("Cap on dudes alive at once (protects web performance when dudes loop forever).")]
        public int maxAlive = 24;
        [Tooltip("Seconds a falling dude takes before it's removed.")]
        public float fallDuration = 0.9f;
        [Tooltip("Seconds the 'saved' animation takes before removal.")]
        public float saveDuration = 0.35f;
        [Tooltip("Seconds a dude spends splatting against a wall before removal.")]
        public float bonkDuration = 0.6f;
    }

    public enum DudeState : byte
    {
        Walking,
        Jumping,
        Falling,
        Saved,
        /// <summary>Walked into a wall and splatted.</summary>
        Bonked,
        Removed,
    }

    public sealed class Dude
    {
        public int Id;
        public Vector2Int From;
        public Vector2Int To;
        public Dir Dir;
        public DudeState State;
        /// <summary>0..1 along the current From→To segment.</summary>
        public float Progress;
        /// <summary>Length of the current segment in cells (1 walk, 2 jump, 0 waiting in place).</summary>
        public int SegmentCells;
        /// <summary>Seconds spent in Falling / Saved.</summary>
        public float StateTime;
    }

    /// <summary>
    /// Deterministic grid simulation. Dudes walk straight, cell to cell; decisions happen on
    /// arrival: exit → saved, pit / off the board → fall, instruction → apply, then step forward.
    /// Walking into a wall is fatal (they splat it). Only instructions change direction.
    /// Placements are read live, so editing the board mid-run affects dudes immediately.
    /// </summary>
    public sealed class PuzzleSim
    {
        public readonly LevelData Level;
        public readonly SimSettings Settings;
        public readonly List<Dude> Dudes = new List<Dude>(32);

        public float Time { get; private set; }
        public int Saved { get; private set; }
        public int Lost { get; private set; }
        public int Spawned { get; private set; }
        public bool Complete => Saved >= Settings.requiredSaved;

        public event Action<Dude> DudeSpawned;
        /// <summary>Dude walked into a wall and splatted: (dude, cell it's in, direction of the wall).</summary>
        public event Action<Dude, Vector2Int, Dir> DudeBumped;
        public event Action<Dude> DudeFell;
        public event Action<Dude> DudeSaved;
        public event Action<Dude> DudeJumped;
        public event Action<Dude> DudeRemoved;

        private readonly IReadOnlyDictionary<Vector2Int, Placement> _placements;
        private float _spawnTimer;
        private int _nextId;

        public PuzzleSim(LevelData level, IReadOnlyDictionary<Vector2Int, Placement> placements, SimSettings settings)
        {
            Level = level;
            _placements = placements;
            Settings = settings;
            _spawnTimer = 0f; // first dude leaves immediately
        }

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            Time += dt;

            _spawnTimer -= dt;
            if (_spawnTimer <= 0f)
            {
                _spawnTimer += Settings.spawnInterval;
                if (CountActive() < Settings.maxAlive)
                    Spawn();
            }

            float cellsPerSecond = Mathf.Max(0.01f, Settings.speed);
            for (int i = 0; i < Dudes.Count; i++)
            {
                var d = Dudes[i];
                switch (d.State)
                {
                    case DudeState.Walking:
                    case DudeState.Jumping:
                        Advance(d, dt * cellsPerSecond);
                        break;
                    case DudeState.Falling:
                        d.StateTime += dt;
                        if (d.StateTime >= Settings.fallDuration) Remove(d);
                        break;
                    case DudeState.Saved:
                        d.StateTime += dt;
                        if (d.StateTime >= Settings.saveDuration) Remove(d);
                        break;
                    case DudeState.Bonked:
                        d.StateTime += dt;
                        if (d.StateTime >= Settings.bonkDuration) Remove(d);
                        break;
                }
            }

            Dudes.RemoveAll(d => d.State == DudeState.Removed);
        }

        private int CountActive()
        {
            int n = 0;
            foreach (var d in Dudes)
                if (d.State == DudeState.Walking || d.State == DudeState.Jumping) n++;
            return n;
        }

        private void Spawn()
        {
            var d = new Dude
            {
                Id = _nextId++,
                From = Level.Start,
                To = Level.Start,
                Dir = Level.StartDir,
                State = DudeState.Walking,
            };
            Dudes.Add(d);
            Spawned++;
            DudeSpawned?.Invoke(d);
            Arrive(d, Level.Start);
        }

        private void Advance(Dude d, float cells)
        {
            // Carry leftover movement through as many arrivals as it covers.
            int guard = 0;
            while (cells > 0f && (d.State == DudeState.Walking || d.State == DudeState.Jumping) && guard++ < 8)
            {
                float segment = Mathf.Max(1, d.SegmentCells);
                float remaining = (1f - d.Progress) * segment;
                if (cells < remaining)
                {
                    d.Progress += cells / segment;
                    return;
                }
                cells -= remaining;
                d.Progress = 1f;
                Arrive(d, d.To);
            }
        }

        private void Arrive(Dude d, Vector2Int cell)
        {
            d.From = cell;
            d.To = cell;
            d.Progress = 0f;
            d.State = DudeState.Walking;

            if (cell == Level.Exit)
            {
                d.State = DudeState.Saved;
                d.StateTime = 0f;
                Saved++;
                DudeSaved?.Invoke(d);
                return;
            }

            if (Level.Get(cell) == CellType.Pit)
            {
                d.State = DudeState.Falling;
                d.StateTime = 0f;
                Lost++;
                DudeFell?.Invoke(d);
                return;
            }

            Dir heading = d.Dir;
            bool jump = false;
            if (_placements != null && _placements.TryGetValue(cell, out var p))
            {
                heading = p.Apply(heading);
                jump = p.Type == InstructionType.Jump;
            }

            if (jump)
            {
                var target = cell + heading.Offset() * 2;
                if (Level.InBounds(target) && !Level.IsBlocked(target))
                {
                    d.Dir = heading;
                    d.To = target;
                    d.SegmentCells = 2;
                    d.State = DudeState.Jumping;
                    DudeJumped?.Invoke(d);
                    return;
                }
            }

            d.Dir = heading;
            var next = cell + heading.Offset();
            if (Level.IsBlocked(next))
            {
                // Straight into a wall: splat.
                d.To = cell;
                d.State = DudeState.Bonked;
                d.StateTime = 0f;
                Lost++;
                DudeBumped?.Invoke(d, cell, heading);
                return;
            }

            d.To = next;
            d.SegmentCells = 1;
        }

        private void Remove(Dude d)
        {
            d.State = DudeState.Removed;
            DudeRemoved?.Invoke(d);
        }

        /// <summary>
        /// Runs a headless simulation and returns the time taken to save the required dudes,
        /// or -1 if it doesn't finish within <paramref name="maxSeconds"/>.
        /// </summary>
        public static float Evaluate(LevelData level, IReadOnlyDictionary<Vector2Int, Placement> placements, SimSettings settings, float maxSeconds = 90f)
        {
            var sim = new PuzzleSim(level, placements, settings);
            // Step at half-cell resolution: exact enough and cheap.
            float dt = 0.5f / Mathf.Max(0.01f, settings.speed);
            while (sim.Time < maxSeconds)
            {
                sim.Step(dt);
                if (sim.Complete) return sim.Time;
            }
            return -1f;
        }
    }
}
