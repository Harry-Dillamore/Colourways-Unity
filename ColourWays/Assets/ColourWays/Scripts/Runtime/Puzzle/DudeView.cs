using ColourWays.Paint;
using UnityEngine;

namespace ColourWays.Puzzle
{
    /// <summary>
    /// Visual for one simulated dude: hops cell to cell, leaves footprints in its own colour,
    /// splats walls it bumps into and the abyss it falls into. This painting is what slowly
    /// reveals the hidden level.
    /// </summary>
    public sealed class DudeView : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [Tooltip("Renderers tinted with this dude's paint colour (feet).")]
        [SerializeField] private Renderer[] tinted;

        [Header("Motion")]
        [SerializeField] private float hopHeight = 0.08f;
        [SerializeField] private float jumpHeight = 1.4f;
        [SerializeField] private float turnSpeed = 900f;
        [SerializeField] private float fallGravity = 14f;

        public enum TrailStyle { Line, Footprints }

        [Header("Paint")]
        [Tooltip("Line: a continuous coloured route like a metro line. Footprints: alternating prints.")]
        [SerializeField] private TrailStyle trailStyle = TrailStyle.Line;
        [SerializeField] private float lineWidth = 0.13f;
        [SerializeField] private float lineStep = 0.1f;
        [SerializeField] private float footSpacing = 0.1f;
        [SerializeField] private float footprintRadius = 0.1f;
        [SerializeField, Range(0f, 1f)] private float dripChance = 0.15f;
        [SerializeField] private float bumpSplatRadius = 0.6f;
        [SerializeField] private float fallSplatRadius = 0.8f;
        [Tooltip("How far (cells) spatter droplets can fly from an impact.")]
        [SerializeField] private float spatterReach = 1.9f;
        [SerializeField] private Color pitInk = new Color(0.16f, 0.16f, 0.2f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public Dude Dude { get; private set; }
        public Color Colour { get; private set; }

        private LevelBuilder _builder;
        private MaterialPropertyBlock _mpb;
        private Vector3 _bodyScale;
        private Vector3 _bodyPos;
        private Vector2Int _segmentFrom = new Vector2Int(int.MinValue, 0);
        private float _lastProgress;
        private int _foot;
        private bool _fallSplatted;
        private DudeState _lastState;
        private Vector3 _lastStamp;
        private bool _hasLastStamp;
        private float _squash;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (body != null)
            {
                _bodyScale = body.localScale;
                _bodyPos = body.localPosition;
            }
        }

        public void Bind(Dude dude, Color colour, LevelBuilder builder)
        {
            Dude = dude;
            Colour = colour;
            _builder = builder;
            _segmentFrom = new Vector2Int(int.MinValue, 0);
            _fallSplatted = false;
            _hasLastStamp = false;
            _lastState = dude.State;
            _squash = 0f;
            transform.localScale = Vector3.one;
            transform.SetPositionAndRotation(builder.CellToWorld(dude.From), Quaternion.Euler(0f, dude.Dir.Yaw(), 0f));
            SetVisible(true);

            _mpb.SetColor(BaseColorId, colour);
            if (tinted != null)
                foreach (var r in tinted)
                    if (r != null) r.SetPropertyBlock(_mpb);
        }

        public void Sync(float dt)
        {
            var d = Dude;
            if (d == null || _builder == null) return;

            Vector3 from = _builder.CellToWorld(d.From);
            Vector3 to = _builder.CellToWorld(d.To);
            Vector3 pos = transform.position;

            switch (d.State)
            {
                case DudeState.Walking:
                {
                    float p = d.Progress;
                    pos = Vector3.Lerp(from, to, p);
                    if (d.From != d.To)
                    {
                        pos.y = Mathf.Abs(Mathf.Sin(p * Mathf.PI)) * hopHeight;
                        Footprints(d, p);
                    }
                    else pos.y = 0f;
                    break;
                }
                case DudeState.Jumping:
                {
                    float p = d.Progress;
                    pos = Vector3.Lerp(from, to, p);
                    pos.y = Mathf.Sin(p * Mathf.PI) * jumpHeight;
                    break;
                }
                case DudeState.Falling:
                {
                    // Sink into the (hidden) pit: the board hides the dude as it drops below y = 0.
                    float t = d.StateTime;
                    pos = from;
                    pos.y = -0.5f * fallGravity * t * t;
                    transform.localScale = Vector3.one * Mathf.Clamp01(1f - t * 0.8f);
                    if (!_fallSplatted)
                    {
                        _fallSplatted = true;
                        FallSplat(from);
                    }
                    break;
                }
                case DudeState.Bonked:
                {
                    // Lunge into the wall face, then flatten against it and fade.
                    float t = d.StateTime;
                    float lunge = Mathf.Clamp01(t / 0.1f);
                    pos = from + d.Dir.World() * (0.32f * (1f - (1f - lunge) * (1f - lunge)));
                    pos.y = 0f;
                    float squish = Mathf.Clamp01((t - 0.1f) / 0.4f);
                    transform.localScale = new Vector3(1f + squish * 0.5f, 1f - squish * 0.85f, 1f - squish * 0.6f);
                    break;
                }
                case DudeState.Saved:
                {
                    float t = Mathf.Clamp01(d.StateTime / 0.35f);
                    pos = from + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.4f);
                    transform.localScale = Vector3.one * (1f - t);
                    break;
                }
            }

            // Landing from a jump: a splash with droplets.
            if (_lastState == DudeState.Jumping && d.State != DudeState.Jumping)
            {
                Stamp(from, 0);
                Stamp(from, 1);
                var paint = PaintManager.Instance;
                if (paint != null)
                    paint.Paint(new Vector3(from.x, 0.01f, from.z), Vector3.up, 0.32f, Colour, BrushShape.Splat);
                Spatter(from, d.Dir.World(), 90f, 0.25f, 1.1f, Random.Range(4, 7), new Vector2(0.04f, 0.14f));
                _squash = -0.35f;
            }
            _lastState = d.State;

            transform.position = pos;
            var targetRot = Quaternion.Euler(0f, d.Dir.Yaw(), 0f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);

            // Squash spring.
            _squash = Mathf.MoveTowards(_squash, 0f, dt * 2.5f);
            if (body != null)
            {
                float sy = 1f + _squash;
                float sxz = 1f / Mathf.Sqrt(Mathf.Max(sy, 0.3f));
                body.localScale = Vector3.Scale(_bodyScale, new Vector3(sxz, sy, sxz));
                body.localPosition = _bodyPos + Vector3.up * (_bodyPos.y * (sy - 1f));
            }
        }

        private void Footprints(Dude d, float p)
        {
            if (trailStyle == TrailStyle.Line)
            {
                LineTrail();
                return;
            }

            if (d.From != _segmentFrom)
            {
                _segmentFrom = d.From;
                _lastProgress = 0f;
            }
            if (_lastProgress < 0.3f && p >= 0.3f) Stamp(transform.position, _foot ^= 1);
            if (_lastProgress < 0.8f && p >= 0.8f) Stamp(transform.position, _foot ^= 1);
            _lastProgress = p;
        }

        /// <summary>Continuous rounded line under the dude (like a metro line), filled in steps.</summary>
        private void LineTrail()
        {
            var paint = PaintManager.Instance;
            if (paint == null) return;
            Vector3 here = new Vector3(transform.position.x, 0.01f, transform.position.z);
            if (!_hasLastStamp || (here - _lastStamp).sqrMagnitude > 0.6f * 0.6f)
            {
                // First stamp, or resuming after a jump: don't draw a line across the gap.
                paint.Paint(here, Vector3.up, lineWidth, Colour, BrushShape.Round);
                _lastStamp = here;
                _hasLastStamp = true;
                return;
            }

            float dist = Vector3.Distance(here, _lastStamp);
            if (dist < lineStep) return;
            int steps = Mathf.FloorToInt(dist / lineStep);
            Vector3 dir = (here - _lastStamp) / dist;
            for (int i = 1; i <= steps; i++)
                paint.Paint(_lastStamp + dir * (lineStep * i), Vector3.up, lineWidth, Colour, BrushShape.Round);
            _lastStamp += dir * (lineStep * steps);

            if (Random.value < dripChance * 0.15f)
            {
                Vector2 o = Random.insideUnitCircle.normalized * Random.Range(0.18f, 0.32f);
                paint.Paint(here + new Vector3(o.x, 0f, o.y), Vector3.up, lineWidth * Random.Range(0.35f, 0.6f), Colour, BrushShape.Splat);
            }
        }

        private void Stamp(Vector3 at, int foot)
        {
            var paint = PaintManager.Instance;
            if (paint == null) return;
            Vector3 side = transform.right * (foot == 0 ? -footSpacing : footSpacing);
            Vector3 p = new Vector3(at.x, 0.01f, at.z) + side;
            paint.Paint(p, Vector3.up, footprintRadius, Colour, BrushShape.Round);
            // The odd drip so trails look wet, not stencilled.
            if (Random.value < dripChance)
            {
                Vector2 o = Random.insideUnitCircle * 0.25f;
                paint.Paint(p + new Vector3(o.x, 0f, o.y), Vector3.up, footprintRadius * Random.Range(0.35f, 0.7f), Colour, BrushShape.Splat);
            }
        }

        /// <summary>
        /// Walked into a wall: a big splat on the wall, droplets streaking along its face (revealing
        /// more of it), a floor splash, and a spray of droplets thrown back across the board.
        /// </summary>
        public void OnBumped(Vector2Int cell, Dir blocked)
        {
            _squash = -0.3f;
            var paint = PaintManager.Instance;
            if (paint == null || _builder == null) return;

            Vector3 into = blocked.World();
            Vector3 along = Vector3.Cross(Vector3.up, into);
            Vector3 face = _builder.CellToWorld(cell) + into * 0.5f;
            float h = _builder.WallHeight;

            paint.Paint(face + Vector3.up * (h * 0.35f), -into, bumpSplatRadius, Colour, BrushShape.Splat);
            paint.Paint(face + Vector3.up * (h * 0.75f) + along * Random.Range(-0.3f, 0.3f), -into, bumpSplatRadius * 0.65f, Colour, BrushShape.Splat);

            // Droplets streaked along the wall face.
            int wallDrops = Random.Range(5, 9);
            for (int i = 0; i < wallDrops; i++)
            {
                float spread = Random.Range(-1f, 1f);
                Vector3 p = face + along * (spread * spread * Mathf.Sign(spread) * 1.4f) + Vector3.up * Random.Range(0.05f, h * 0.95f);
                paint.Paint(p, -into, Random.Range(0.06f, 0.2f), Colour, BrushShape.Splat);
            }

            // Floor splash and a fan of droplets thrown back from the wall.
            Vector3 floor = new Vector3(face.x, 0.01f, face.z);
            paint.Paint(floor - into * 0.25f, Vector3.up, bumpSplatRadius * 0.85f, Colour, BrushShape.Splat);
            Spatter(floor - into * 0.2f, -into, 75f, 0.3f, spatterReach, Random.Range(8, 14), new Vector2(0.05f, 0.24f));
            Haptics.Tick();
        }

        public void OnJumped()
        {
            _squash = 0.25f;
            Stamp(transform.position, 0);
            Stamp(transform.position, 1);
        }

        /// <summary>Reached the exit: a celebratory burst of paint around the goal.</summary>
        public void OnSaved()
        {
            var paint = PaintManager.Instance;
            if (paint == null || _builder == null) return;
            Vector3 p = _builder.CellToWorld(Dude.From);
            p.y = 0.01f;
            paint.Paint(p, Vector3.up, 0.45f, Colour, BrushShape.Splat);
            Spatter(p, Vector3.forward, 180f, 0.45f, spatterReach * 0.8f, Random.Range(8, 12), new Vector2(0.05f, 0.2f));
        }

        /// <summary>Reveal a pit: a big splash of the dude's colour around an ink "hole", droplets everywhere.</summary>
        private void FallSplat(Vector3 at)
        {
            var paint = PaintManager.Instance;
            if (paint == null) return;
            Vector3 p = new Vector3(at.x, 0.01f, at.z);
            paint.Paint(p, Vector3.up, fallSplatRadius, Colour, BrushShape.Splat);
            Spatter(p, Vector3.forward, 180f, fallSplatRadius * 0.6f, spatterReach, Random.Range(8, 13), new Vector2(0.05f, 0.24f));
            paint.Paint(p, Vector3.up, 0.36f, pitInk, BrushShape.Round);
        }

        /// <summary>Flick droplets across the floor in a cone around <paramref name="direction"/>; farther = smaller.</summary>
        private void Spatter(Vector3 origin, Vector3 direction, float coneDegrees, float minDistance, float maxDistance, int count, Vector2 radius)
        {
            var paint = PaintManager.Instance;
            if (paint == null) return;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) direction = Vector3.forward;
            direction.Normalize();

            for (int i = 0; i < count; i++)
            {
                Vector3 d = Quaternion.Euler(0f, Random.Range(-coneDegrees, coneDegrees), 0f) * direction;
                float t = Random.value;
                float dist = Mathf.Lerp(minDistance, maxDistance, t * t);
                float size = Mathf.Lerp(radius.y, radius.x, t) * Random.Range(0.7f, 1.15f);
                Vector3 p = origin + d * dist;
                p.y = 0.01f;
                paint.Paint(p, Vector3.up, size, Colour, BrushShape.Splat);
            }
        }

        private void SetVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                r.enabled = visible;
        }
    }
}
