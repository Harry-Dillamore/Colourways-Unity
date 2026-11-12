using System.Collections;
using ColourWays.Player;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace ColourWays.Level
{
    /// <summary>
    /// End-of-level trigger. Port of TB_FinishLevle: on overlap, disables player input, blends
    /// the camera to <see cref="cameraTarget"/> and then loads the next level.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class LevelFinish : MonoBehaviour
    {
        [Tooltip("Where the camera flies to when the level is completed. Optional.")]
        [SerializeField] private Transform cameraTarget;
        [SerializeField] private float cameraBlendTime = 2.5f;
        [SerializeField] private AnimationCurve cameraBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Scene to load afterwards (must be in Build Profiles). Leave empty to stay.")]
        [SerializeField] private string nextSceneName = "";
        [SerializeField] private float loadDelay = 4f;

        [Tooltip("Enabled when the level is completed (e.g. a 'Level Complete' canvas).")]
        [SerializeField] private GameObject completeUI;
        [SerializeField] private UnityEvent onFinished = new UnityEvent();

        public bool IsFinished { get; private set; }

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void Awake()
        {
            if (completeUI != null)
                completeUI.SetActive(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsFinished) return;
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;

            IsFinished = true;
            StartCoroutine(Finish(player));
        }

        private IEnumerator Finish(PlayerController player)
        {
            player.InputEnabled = false;
            if (completeUI != null) completeUI.SetActive(true);
            onFinished.Invoke();

            float elapsed = 0f;
            var rig = player.CameraRig;
            if (rig != null && cameraTarget != null)
            {
                rig.enabled = false;
                Transform cam = rig.transform;
                Vector3 startPos = cam.position;
                Quaternion startRot = cam.rotation;
                while (elapsed < cameraBlendTime)
                {
                    elapsed += Time.deltaTime;
                    float t = cameraBlendCurve.Evaluate(Mathf.Clamp01(elapsed / cameraBlendTime));
                    cam.SetPositionAndRotation(
                        Vector3.LerpUnclamped(startPos, cameraTarget.position, t),
                        Quaternion.SlerpUnclamped(startRot, cameraTarget.rotation, t));
                    yield return null;
                }
            }

            if (string.IsNullOrEmpty(nextSceneName))
                yield break;

            if (elapsed < loadDelay)
                yield return new WaitForSeconds(loadDelay - elapsed);

            if (Application.CanStreamedLevelBeLoaded(nextSceneName))
                SceneManager.LoadScene(nextSceneName);
            else
                Debug.LogWarning($"[ColourWays] Next scene '{nextSceneName}' isn't in the build list.", this);
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 1f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;

            if (cameraTarget != null)
            {
                Gizmos.DrawLine(transform.position, cameraTarget.position);
                Gizmos.matrix = Matrix4x4.TRS(cameraTarget.position, cameraTarget.rotation, Vector3.one);
                Gizmos.DrawFrustum(Vector3.zero, 50f, 1.5f, 0.1f, 1.6f);
                Gizmos.matrix = Matrix4x4.identity;
            }
        }
    }
}
