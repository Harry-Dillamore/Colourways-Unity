using System.Collections;
using UnityEngine;

namespace ColourWays.Level
{
    /// <summary>Shows the level's first tutorial line shortly after it starts (e.g. "WASD to move").</summary>
    public sealed class LevelIntro : MonoBehaviour
    {
        [TextArea(1, 3)] [SerializeField] private string message = "";
        [SerializeField] private float delay = 1.2f;
        [SerializeField] private float seconds = 6f;

        private IEnumerator Start()
        {
            if (string.IsNullOrEmpty(message)) yield break;
            yield return new WaitForSeconds(delay);
            PromptUI.Show(message, seconds);
        }
    }
}
