using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    public void ContinueGame()
    {
        SceneManager.LoadScene("MainScene");
    }

    public void newGame()
    {
        PlayerPrefs.DeleteAll();

        SceneManager.LoadScene("MainScene");
    }
}
