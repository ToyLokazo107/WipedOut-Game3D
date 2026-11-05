using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VNCreator
{
    public class VNCreator_EndScreen : MonoBehaviour
    {
        public Button restartButton;
        public Button mainMenuButton;
        [Scene]
        public string mainMenu;

        void Start()
        {
            if (restartButton != null && restartButton.onClick.GetPersistentEventCount() == 0)
                restartButton.onClick.AddListener(Restart);

            if (mainMenuButton != null && mainMenuButton.onClick.GetPersistentEventCount() == 0)
                mainMenuButton.onClick.AddListener(MainMenu);
        }

        public void Restart()
        {
            GameSaveManager.NewLoad("MainGame");
            SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
        }

        public void MainMenu()
        {
            if (string.IsNullOrEmpty(mainMenu))
            {
                Debug.LogWarning("VNCreator: no hay escena de menú configurada.");
                return;
            }

            SceneManager.LoadScene(mainMenu, LoadSceneMode.Single);
        }
    }
}
