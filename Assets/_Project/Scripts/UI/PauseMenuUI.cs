using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LuluDungeon
{
    /// <summary>
    /// 暂停菜单UI
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        [Header("菜单按钮")]
        public Button resumeButton;
        public Button saveButton;   // 保存并返回主菜单
        public Button quitButton;   // 保存并退出游戏

        [Header("面板")]
        public GameObject panelRoot;

        private GameManager _gm;
        private DataManager _dm;

        private void Start()
        {
            _gm = GameManager.Instance;
            _dm = DataManager.Instance;

            if (resumeButton) resumeButton.onClick.AddListener(Resume);
            if (saveButton) saveButton.onClick.AddListener(SaveAndReturn);
            if (quitButton) quitButton.onClick.AddListener(QuitGame);

            panelRoot?.SetActive(false);
        }

        private void Update()
        {
            // 长按左菜单键打开暂停菜单
            if (InputDevicesHelper.IsLeftMenuDown())
            {
                if (panelRoot != null && !panelRoot.activeSelf)
                    Show();
            }
        }

        public void Show()
        {
            panelRoot?.SetActive(true);
            _gm?.PauseGame();
        }

        public void Resume()
        {
            panelRoot?.SetActive(false);
            _gm?.ResumeGame();
        }

        /// <summary>保存并返回主菜单（层内进度不丢）</summary>
        public void SaveAndReturn()
        {
            _dm?.AutoSave();
            Debug.Log("[PauseMenu] Saved. Returning to main menu.");
            _gm?.ReturnToMainMenu();
        }

        /// <summary>保存并退出游戏</summary>
        public void QuitGame()
        {
            _dm?.AutoSave();
            Debug.Log("[PauseMenu] Saved. Quitting...");
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
