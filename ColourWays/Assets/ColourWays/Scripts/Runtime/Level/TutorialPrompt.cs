using ColourWays.Player;
using UnityEngine;

namespace ColourWays.Level
{
    /// <summary>
    /// A trigger zone that shows a tutorial line ("Hold Shift to run") when the dude walks in.
    /// Place them along a level to teach things right where they matter.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TutorialPrompt : MonoBehaviour
    {
        [TextArea(1, 3)] [SerializeField] private string message = "WASD to move";
        [SerializeField] private float seconds = 5f;
        [SerializeField] private bool once = true;

        private bool _shown;

        private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            if ((_shown && once) || other.GetComponentInParent<PlayerController>() == null) return;
            _shown = true;
            PromptUI.Show(message, seconds);
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
