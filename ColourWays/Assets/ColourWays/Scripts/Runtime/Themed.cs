using UnityEngine;
using UnityEngine.UI;

namespace ColourWays
{
    /// <summary>A UI graphic with a light and a dark colour; follows <see cref="Theme.Blend"/>.</summary>
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public sealed class Themed : MonoBehaviour
    {
        [SerializeField] private Color lightColour = Color.white;
        [SerializeField] private Color darkColour = Color.black;

        private Graphic _graphic;
        private float _applied = -1f;

        private void OnEnable()
        {
            _graphic = GetComponent<Graphic>();
            _applied = -1f;
            Apply();
        }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            float blend = Theme.Blend;
            if (_graphic == null || Mathf.Approximately(_applied, blend)) return;
            _applied = blend;
            _graphic.color = Color.Lerp(lightColour, darkColour, blend);
        }
    }
}
