using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public SaveManager saveManager;
    public MedalPusher.MedalPusherGame game;

    public int area = 0;
    public int round = 0;
    public int medal = 50;
    public int point = 0;

    private void Start()
    {
        LoadGame();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5))
        {
            SaveGame();
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            SceneManager.LoadScene("TitleScene");
        }
    }

    public void SaveGame()
    {
        Debug.Log($"•Û‘¶ Area={game.AreaIndex} Round={game.RoundIndex}");
        saveManager.Save(game.AreaIndex, game.RoundIndex, game.OwnedMedals, game.AreaPoints);
    }

    public void LoadGame()
    {
        SaveData data = saveManager.Load();

        if (data == null)
            return;

        game.LoadProgress(data.area, data.round, data.medal, data.point);

        Debug.Log($"Area:{area} Round:{round} Medal:{medal} Point:{point}");
    }
}
