using System.Collections.Generic;
using UnityEngine;

namespace ColourWays.Player
{
    /// <summary>
    /// Procedural run cycle for the stickman exported from the Unreal project (Character_1):
    /// swings legs and arms in step with <see cref="FootstepPainter"/> (so footprints land where
    /// the feet do), bends knees and elbows, leans into speed, bobs, and recoils on a wall bonk.
    ///
    /// Works on the bones by name and rotates whole limb groups about their joints in world
    /// space, so it doesn't depend on the rig's bone axes or on whether the exported stretch /
    /// twist bones are nested or siblings. Swap in an Animator with real clips whenever the
    /// Unreal animations are exported; painting never depends on this.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StickmanAnimator : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private FootstepPainter footsteps;
        [Tooltip("Root of the imported model (bones are searched under it).")]
        [SerializeField] private Transform model;

        [Header("Gait")]
        [SerializeField] private float legSwing = 32f;
        [SerializeField] private float kneeBend = 55f;
        [SerializeField] private float armSwing = 38f;
        [SerializeField] private float elbowBend = 35f;
        [SerializeField] private float lean = 12f;
        [SerializeField] private float bob = 0.045f;
        [Tooltip("Arms are lowered to this angle from straight down (the bind pose may be a T/A-pose).")]
        [SerializeField] private float armRestAngle = 12f;

        [Header("Bonk")]
        [SerializeField] private float recoilLean = 28f;
        [SerializeField] private float recoilArms = 70f;

        [Header("Animator (real clips)")]
        [Tooltip("Float parameter: 0 idle .. 1 full run. Set from the dude's speed.")]
        [SerializeField] private string speedParameter = "Speed";
        [Tooltip("Playback speed while moving. The clips are authored for a full-size person; the dude is tiny, so his legs need to go faster to match his speed.")]
        [SerializeField] private float movingPlayback = 1.7f;

        private sealed class Bone
        {
            public Transform T;
            public Vector3 LocalPos;
            public Quaternion LocalRot;
        }

        private readonly List<Bone> _bones = new List<Bone>();
        private Transform[] _upperBody = System.Array.Empty<Transform>();
        private Transform[] _legL, _legR, _shinL, _shinR, _armL, _armR, _foreL, _foreR;
        private Transform _spine, _thighL, _thighR, _kneeL, _kneeR, _shoulderL, _shoulderR, _elbowL, _elbowR;
        private Quaternion _armRestL = Quaternion.identity, _armRestR = Quaternion.identity; // character space
        private Vector3 _modelRestPos;
        private float _speed01, _lean, _recoil, _idleTime;
        private Animator _animator;
        private bool _useAnimator;
        private int _speedId;
        private Transform _hips;
        private Vector3 _hipsBind; // in model space

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (footsteps == null) footsteps = GetComponentInParent<FootstepPainter>();
            if (model == null) model = transform;
            _modelRestPos = model.localPosition;

            // Real clips (exported from Unreal) if the setup gave the model an Animator Controller;
            // otherwise the procedural cycle below, and the idle Animator is switched off.
            _animator = model.GetComponentInChildren<Animator>();
            _useAnimator = _animator != null && _animator.runtimeAnimatorController != null;
            if (_animator != null && !_useAnimator) _animator.enabled = false;
            if (_useAnimator)
            {
                _animator.applyRootMotion = false;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _speedId = Animator.StringToHash(speedParameter);
            }

            var all = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (!all.ContainsKey(t.name)) all.Add(t.name, t);
                _bones.Add(new Bone { T = t, LocalPos = t.localPosition, LocalRot = t.localRotation });
            }
            Transform B(string n) => all.TryGetValue(n, out var t) ? t : null;
            Transform[] Group(params string[] names)
            {
                var list = new List<Transform>();
                foreach (var n in names) { var t = B(n); if (t != null) list.Add(t); }
                return list.ToArray();
            }

            _spine = B("spine_01_x");
            _thighL = B("thigh_stretch_l"); _thighR = B("thigh_stretch_r");
            _kneeL = B("leg_stretch_l"); _kneeR = B("leg_stretch_r");
            _shoulderL = B("arm_stretch_l"); _shoulderR = B("arm_stretch_r");
            _elbowL = B("forearm_stretch_l"); _elbowR = B("forearm_stretch_r");

            _legL = Group("thigh_stretch_l", "thigh_twist_l", "leg_stretch_l", "leg_twist_l", "foot_l", "toes_01_l");
            _legR = Group("thigh_stretch_r", "thigh_twist_r", "leg_stretch_r", "leg_twist_r", "foot_r", "toes_01_r");
            _shinL = Group("leg_stretch_l", "leg_twist_l", "foot_l", "toes_01_l");
            _shinR = Group("leg_stretch_r", "leg_twist_r", "foot_r", "toes_01_r");
            _armL = Group("arm_stretch_l", "arm_twist_l", "forearm_stretch_l", "forearm_twist_l", "hand_l");
            _armR = Group("arm_stretch_r", "arm_twist_r", "forearm_stretch_r", "forearm_twist_r", "hand_r");
            _foreL = Group("forearm_stretch_l", "forearm_twist_l", "hand_l");
            _foreR = Group("forearm_stretch_r", "forearm_twist_r", "hand_r");
            _upperBody = Group("spine_01_x", "spine_02_x", "neck_x", "head_x", "shoulder_l", "shoulder_r",
                "arm_stretch_l", "arm_twist_l", "forearm_stretch_l", "forearm_twist_l", "hand_l",
                "arm_stretch_r", "arm_twist_r", "forearm_stretch_r", "forearm_twist_r", "hand_r");

            _hips = B("root_x");
            if (_hips != null) _hipsBind = model.InverseTransformPoint(_hips.position);

            if (!_useAnimator && (_thighL == null || _shoulderL == null))
                Debug.LogWarning("[ColourWays] StickmanAnimator: expected stickman bones (thigh_stretch_l, arm_stretch_l, ...) not found; only body motion will play.", this);

            // Bring the arms down from the bind pose (stored in model space, applied every frame).
            _armRestL = ArmRest(_shoulderL, B("hand_l"), -1f);
            _armRestR = ArmRest(_shoulderR, B("hand_r"), 1f);
        }

        private Quaternion ArmRest(Transform shoulder, Transform hand, float side)
        {
            if (shoulder == null || hand == null) return Quaternion.identity;
            // In the character frame (this transform: +Z forward, +X right), not the imported model's own axes.
            Vector3 dir = transform.InverseTransformDirection(hand.position - shoulder.position).normalized;
            Vector3 down = Quaternion.AngleAxis(side * armRestAngle, Vector3.forward) * Vector3.down; // slightly out to the side
            return Vector3.Angle(dir, down) > 10f ? Quaternion.FromToRotation(dir, down) : Quaternion.identity;
        }

        private void OnEnable()
        {
            if (player == null) return;
            player.WallHit += OnWallHit;
            player.Splatted += OnSplatted;
            player.Respawned += OnRespawned;
        }

        private void OnDisable()
        {
            if (player == null) return;
            player.WallHit -= OnWallHit;
            player.Splatted -= OnSplatted;
            player.Respawned -= OnRespawned;
        }

        // Splatted against a wall: the dude is gone (he's the paint on the wall now) until he respawns.
        private void OnSplatted()
        {
            if (model != null) model.gameObject.SetActive(false);
        }

        private void OnRespawned()
        {
            if (model != null) model.gameObject.SetActive(true);
            _recoil = 0f;
        }

        private void OnWallHit(Vector3 point, Vector3 normal, float speed) => _recoil = Mathf.Clamp01(speed / 6f + 0.4f);

        private void LateUpdate()
        {
            if (player == null || model == null) return;
            float dt = Time.deltaTime;

            if (_useAnimator)
            {
                AnimatorPass(dt);
                return;
            }

            // Rest pose first, then every adjustment is applied on top in world space.
            foreach (var b in _bones)
            {
                b.T.localPosition = b.LocalPos;
                b.T.localRotation = b.LocalRot;
            }

            bool grounded = player.IsGrounded;
            float target = Mathf.Clamp(player.HorizontalSpeed / Mathf.Max(player.SprintSpeed, 0.01f), 0f, 1.2f);
            _speed01 = Mathf.Lerp(_speed01, grounded ? target : 0.2f, 1f - Mathf.Exp(-10f * dt));
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 2.2f);
            _idleTime += dt;

            float phase = footsteps != null ? footsteps.StridePhase : 0f;
            float a = phase * Mathf.PI * 2f;
            float gait = Mathf.Clamp01(_speed01 * 1.6f);
            float recoil = Mathf.Sin(_recoil * Mathf.PI * 0.5f); // fast in, eased out
            Vector3 right = player.transform.right;
            Quaternion frame = transform.rotation;

            // ---- Lean: forward with speed, snapped back on a bonk. Idle: a little breathing sway.
            _lean = Mathf.Lerp(_lean, _speed01 * lean, 1f - Mathf.Exp(-8f * dt));
            float breathe = Mathf.Sin(_idleTime * 2.2f) * 1.5f * (1f - gait);
            if (_spine != null)
                Rotate(_upperBody, _spine.position, Quaternion.AngleAxis(_lean + breathe - recoil * recoilLean, right));

            // ---- Arms: lowered to rest, swing opposite to the legs, bend at the elbow; fling up on a bonk.
            float armL = -Mathf.Cos(a) * armSwing * gait;
            float bend = elbowBend * (0.35f + 0.65f * gait);
            Arm(_armL, _shoulderL, _foreL, _elbowL, frame * _armRestL * Quaternion.Inverse(frame), armL, bend, right, recoil, -1f);
            Arm(_armR, _shoulderR, _foreR, _elbowR, frame * _armRestR * Quaternion.Inverse(frame), -armL, bend, right, recoil, 1f);

            // ---- Legs: left is forward-most (planted) at phase 0, right at phase 0.5. Knee bends while the foot swings.
            Leg(_legL, _thighL, _shinL, _kneeL, a, gait, right);
            Leg(_legR, _thighR, _shinR, _kneeR, a + Mathf.PI, gait, right);

            // ---- Bob (two bounces per stride) and a squash on the bonk.
            float bounce = grounded ? Mathf.Abs(Mathf.Sin(a)) * bob * gait : 0f;
            model.localPosition = _modelRestPos + Vector3.up * (bounce - recoil * 0.03f);
        }

        /// <summary>Drive the real clips from the dude's speed, keep him in place, add the bonk recoil on top.</summary>
        private void AnimatorPass(float dt)
        {
            float target = player.IsGrounded ? Mathf.Clamp01(player.HorizontalSpeed / Mathf.Max(player.SprintSpeed, 0.01f)) : 0.2f;
            _speed01 = Mathf.Lerp(_speed01, target, 1f - Mathf.Exp(-12f * dt));
            _animator.SetFloat(_speedId, _speed01);
            _animator.speed = Mathf.Lerp(1f, movingPlayback, Mathf.Clamp01(_speed01 * 4f));

            // Clips with forward root movement would drift: pin the hips over the capsule (height stays animated).
            if (_hips != null)
            {
                Vector3 bind = model.TransformPoint(_hipsBind);
                Vector3 p = _hips.position;
                _hips.position = new Vector3(bind.x, p.y, bind.z);
            }

            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 2.2f);
            if (_recoil > 0f && _spine != null)
            {
                float recoil = Mathf.Sin(_recoil * Mathf.PI * 0.5f);
                Rotate(_upperBody, _spine.position, Quaternion.AngleAxis(-recoil * recoilLean, player.transform.right));
            }
        }

        private void Arm(Transform[] arm, Transform shoulder, Transform[] fore, Transform elbow, Quaternion rest, float swing, float bend,
            Vector3 right, float recoil, float side)
        {
            if (shoulder == null) return;
            Vector3 pivot = shoulder.position;
            Rotate(arm, pivot, rest);
            Quaternion fling = Quaternion.AngleAxis(-recoil * recoilArms, right) *
                               Quaternion.AngleAxis(side * recoil * recoilArms * 0.4f, player.transform.forward);
            Rotate(arm, pivot, fling * Quaternion.AngleAxis(-swing, right));
            if (elbow != null) Rotate(fore, elbow.position, Quaternion.AngleAxis(-bend, right));
        }

        private void Leg(Transform[] leg, Transform thigh, Transform[] shin, Transform knee, float a, float gait, Vector3 right)
        {
            if (thigh == null) return;
            float swing = Mathf.Cos(a) * legSwing * gait;           // + = forward
            float lift = Mathf.Max(0f, -Mathf.Sin(a)) * kneeBend * gait; // bend while swinging through
            Rotate(leg, thigh.position, Quaternion.AngleAxis(-swing, right));
            if (knee != null) Rotate(shin, knee.position, Quaternion.AngleAxis(lift, right));
        }

        /// <summary>
        /// Rotate a set of bones rigidly about a pivot. Bones that are descendants of another bone
        /// in the set follow their parent, so only the top-most ones are moved.
        /// </summary>
        private static void Rotate(Transform[] set, Vector3 pivot, Quaternion delta)
        {
            if (set == null) return;
            for (int i = 0; i < set.Length; i++)
            {
                var t = set[i];
                bool nested = false;
                for (int j = 0; j < set.Length && !nested; j++)
                    if (j != i && t != set[j] && t.IsChildOf(set[j])) nested = true;
                if (nested) continue;
                t.SetPositionAndRotation(pivot + delta * (t.position - pivot), delta * t.rotation);
            }
        }
    }
}
