using UnityEngine;

namespace ColourWays
{
    /// <summary>
    /// Applies the theme in a scene: the shader global, the camera background and the fog colour,
    /// fading smoothly when it changes. Put one on the main camera.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class ThemeApplier : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Color lightColour = Color.white;
        [SerializeField] private Color darkColour = Color.black;
        [SerializeField, Min(0.01f)] private float transition = 0.45f;

        private static readonly int DarkId = Shader.PropertyToID("_CW_Dark");
        private float _blend;

        private void Awake()
        {
            if (worldCamera == null) worldCamera = GetComponent<Camera>();
            _blend = Theme.IsDark ? 1f : 0f;
            Push();
        }

        private void Update()
        {
            float target = Theme.IsDark ? 1f : 0f;
            if (Mathf.Approximately(_blend, target)) return;
            _blend = Mathf.MoveTowards(_blend, target, Time.unscaledDeltaTime / transition);
            Push();
        }

        private void OnDisable() => Shader.SetGlobalFloat(DarkId, 0f); // keep the editor's scene view light

        private void Push()
        {
            float eased = _blend * _blend * (3f - 2f * _blend);
            Theme.Blend = eased;
            Shader.SetGlobalFloat(DarkId, eased);
            Color background = Color.Lerp(lightColour, darkColour, eased);
            if (worldCamera != null) worldCamera.backgroundColor = background;
            RenderSettings.fogColor = background;
        }
    }
}
