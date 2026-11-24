using System;
using System.Collections;
using ColourWays.Player;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ColourWays.Level
{
    /// <summary>
    /// End-of-level trigger. Port of TB_FinishLevle: on overlap, stops the player, flies the
    /// camera up above the level to show off everything that got painted, then shows the
    /// "Level complete" card with a Next level button (paint fills the screen, the next level
    /// loads, the paint drains away).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class LevelFinish : MonoBehaviour
    {
        [Tooltip("Where the camera flies to when the level is completed (above the level, looking down).")]
        [SerializeField] private Transform cameraTarget;
        [SerializeField] private float cameraBlendTime = 2.5f;
        [SerializeField] private AnimationCurve cameraBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Scene loaded by the Next button (must be in the build list). Empty = back to the menu.")]
        [SerializeField] private string nextSceneName = "";
        [SerializeField] private string menuScene = "MainMenu";

        [Header("UI")]
        [Tooltip("The 'Level complete' card (a CanvasGroup), shown after the camera pan.")]
        [SerializeField] private CanvasGroup completeUI;
        [SerializeField] private Button nextButton;
        [SerializeField] private Text nextLabel;
        [SerializeField] private Button menuButton;
        [SerializeField] private UnityEvent onFinished = new UnityEvent();

        /// <summary>Raised whenever any level finish trigger is reached.</summary>
        public static event Action<LevelFinish> AnyLevelFinished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => AnyLevelFinished = null;

        public bool IsFinished { get; private set; }

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void Awake()
        {
            if (completeUI != null) { completeUI.alpha = 0f; completeUI.gameObject.SetActive(false); }
            bool last = string.IsNullOrEmpty(nextSceneName);
            if (nextLabel != null) nextLabel.text = last ? "Back to menu" : "Next level";
            if (nextButton != null) nextButton.onClick.AddListener(() => ScreenTransition.LoadScene(last ? menuScene : nextSceneName));
            if (menuButton != null)
            {
                menuButton.onClick.AddListener(() => ScreenTransition.LoadScene(menuScene));
                menuButton.gameObject.SetActive(!last);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsFinished) return;
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null || player.IsSplatted) return;

            IsFinished = true;
            StartCoroutine(Finish(player));
        }

        private IEnumerator Finish(PlayerController player)
        {
            player.InputEnabled = false;
            onFinished.Invoke();
            AnyLevelFinished?.Invoke(this);

            // Pan up above the level to look at the paint.
            var rig = player.CameraRig;
            if (rig != null && cameraTarget != null)
            {
                rig.enabled = false;
                Transform cam = rig.transform;
                Vector3 startPos = cam.position;
                Quaternion startRot = cam.rotation;
                for (float elapsed = 0f; elapsed < cameraBlendTime; elapsed += Time.deltaTime)
                {
                    float t = cameraBlendCurve.Evaluate(Mathf.Clamp01(elapsed / cameraBlendTime));
                    cam.SetPositionAndRotation(
                        Vector3.LerpUnclamped(startPos, cameraTarget.position, t),
                        Quaternion.SlerpUnclamped(startRot, cameraTarget.rotation, t));
                    yield return null;
                }
                cam.SetPositionAndRotation(cameraTarget.position, cameraTarget.rotation);
            }

            // Then the card with the Next button.
            GameState.LockCursor(false);
            if (completeUI == null) yield break;
            completeUI.gameObject.SetActive(true);
            completeUI.blocksRaycasts = completeUI.interactable = true;
            var rect = (RectTransform)completeUI.transform;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f)
            {
                float e = 1f - (1f - t) * (1f - t) * (1f - t);
                completeUI.alpha = e;
                rect.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, e);
                yield return null;
            }
            completeUI.alpha = 1f;
            rect.localScale = Vector3.one;
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
