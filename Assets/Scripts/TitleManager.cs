using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    public SaveManager saveManager;

    public void ContinueGame()
    {
        SceneManager.LoadScene("MainScene");
    }

    public void newGame()
    {
        saveManager.DeleteSave();

        SceneManager.LoadScene("MainScene");
    }
}
