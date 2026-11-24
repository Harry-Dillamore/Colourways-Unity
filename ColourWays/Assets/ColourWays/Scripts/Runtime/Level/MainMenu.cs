using UnityEngine;
using UnityEngine.UI;

namespace ColourWays.Level
{
    /// <summary>Title screen: play from level 1, pick a level, or quit. (Dark mode is in the pause menu.)</summary>
    [DisallowMultipleComponent]
    public sealed class MainMenu : MonoBehaviour
    {
        [SerializeField] private Button playButton;
        [SerializeField] private Button[] levelButtons = new Button[0];
        [SerializeField] private string[] levelScenes = { "Level_1", "Level_2", "Level_3" };
        [SerializeField] private Button quitButton;

        private void Awake()
        {
            if (playButton != null && levelScenes.Length > 0)
                playButton.onClick.AddListener(() => ScreenTransition.LoadScene(levelScenes[0]));
            for (int i = 0; i < levelButtons.Length && i < levelScenes.Length; i++)
            {
                string scene = levelScenes[i];
                if (levelButtons[i] != null) levelButtons[i].onClick.AddListener(() => ScreenTransition.LoadScene(scene));
            }
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
        }

        private void Start()
        {
            Time.timeScale = 1f;
            GameState.LockCursor(false);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
