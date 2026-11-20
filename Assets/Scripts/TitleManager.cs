using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    public SaveManager saveManager;

    [Tooltip("「つづきから」「はじめから」で移るシーンの名前（Build Settings に入っているシーン）")]
    public string gameSceneName = "MainScene";

    public void ContinueGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    public void newGame()
    {
        saveManager.DeleteSave();

        SceneManager.LoadScene(gameSceneName);
    }
}
