using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ColourWays.Level
{
    /// <summary>
    /// Scene changes with paint: paint floods up and fills the screen, the next scene loads behind
    /// it, then the paint drains away. Creates its own overlay on first use and survives scene loads.
    /// Needs Resources/ColourWays/M_PaintCurtain (made by the ColourWays setup).
    /// </summary>
    public sealed class ScreenTransition : MonoBehaviour
    {
        private const string MaterialPath = "ColourWays/M_PaintCurtain";

        private static readonly Color[] Colours =
        {
            new Color(1f, 0.25f, 0.42f), new Color(1f, 0.55f, 0.1f), new Color(1f, 0.82f, 0.1f),
            new Color(0.2f, 0.8f, 0.35f), new Color(0.1f, 0.62f, 1f), new Color(0.55f, 0.3f, 0.95f),
            new Color(0.05f, 0.8f, 0.78f), new Color(1f, 0.4f, 0.85f),
        };

        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int[] ColourIds = { Shader.PropertyToID("_ColorA"), Shader.PropertyToID("_ColorB"), Shader.PropertyToID("_ColorC") };

        private static ScreenTransition _instance;

        /// <summary>Raised when a paint screen transition begins loading the target scene.</summary>
        public static event System.Action<string> TransitionStarted;

        /// <summary>True while a transition is running (ignore input meanwhile).</summary>
        public static bool Busy { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            Busy = false;
            TransitionStarted = null;
        }

        private RawImage _image;
        private Material _material;

        [SerializeField] private float fillSeconds = 0.9f;
        [SerializeField] private float drainSeconds = 1.1f;

        /// <summary>Fill the screen with paint, load <paramref name="sceneName"/>, then drain the paint away.</summary>
        public static void LoadScene(string sceneName)
        {
            if (Busy) return;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogWarning($"[ColourWays] Scene '{sceneName}' isn't in the build list.");
                return;
            }
            Ensure();
            if (_instance._material == null) { Time.timeScale = 1f; SceneManager.LoadScene(sceneName); return; }
            _instance.StartCoroutine(_instance.Run(sceneName));
        }

        private static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("ScreenTransition", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var imageGo = new GameObject("Paint", typeof(RectTransform), typeof(RawImage));
            imageGo.transform.SetParent(go.transform, false);
            var rect = (RectTransform)imageGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            _instance = go.AddComponent<ScreenTransition>();
            _instance._image = imageGo.GetComponent<RawImage>();
            var source = Resources.Load<Material>(MaterialPath);
            if (source != null) _instance._material = new Material(source);
            else Debug.LogWarning("[ColourWays] Missing Resources/" + MaterialPath + "; scene changes won't animate. Run ColourWays > Build Levels.");
            _instance._image.material = _instance._material;
            _instance._image.raycastTarget = true; // block clicks while the paint is up
            imageGo.SetActive(false);
        }

        private IEnumerator Run(string sceneName)
        {
            Busy = true;
            TransitionStarted?.Invoke(sceneName);
            _image.gameObject.SetActive(true);

            // Three different paint colours, a new pattern every time.
            int first = Random.Range(0, Colours.Length);
            for (int i = 0; i < 3; i++)
                _material.SetColor(ColourIds[i], Colours[(first + i * 3) % Colours.Length]);
            _material.SetFloat(SeedId, Random.Range(0f, 100f));

            yield return Animate(0f, 1f, fillSeconds);

            Time.timeScale = 1f;
            var load = SceneManager.LoadSceneAsync(sceneName);
            while (load != null && !load.isDone) yield return null;
            yield return null; // let the new scene settle for a frame (atlas build etc.)

            yield return Animate(1f, 0f, drainSeconds);
            _image.gameObject.SetActive(false);
            Busy = false;
        }

        private IEnumerator Animate(float from, float to, float seconds)
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(seconds, 0.01f))
            {
                _material.SetFloat(FillId, Mathf.Lerp(from, to, t));
                yield return null;
            }
            _material.SetFloat(FillId, to);
        }
    }
}
