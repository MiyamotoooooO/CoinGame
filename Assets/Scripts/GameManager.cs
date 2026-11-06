using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public int currentArea = 1;
    public int currentRound = 1;

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
            Debug.Log("セーブ");
        }

        if (Input.GetKeyDown(KeyCode.F9))
        {
            LoadGame();
            Debug.Log("ロード");
        }
    }

    public void SaveGame()
    {
        SaveManager.Instance.SaveGame(currentArea, currentRound, medal, point, "Pops", "Jazz", "Rock");
    }

    public void LoadGame()
    {
        SaveData data = SaveManager.Instance.LoadGame();

        if (data == null)
            return;

        currentArea = data.area;
        currentRound = data.round;

        medal = data.medal;
        point = data.point;

        Debug.Log($"エリア{currentArea} ラウンド{currentRound}");
    }

    private void OnApplicationQuit()
    {
        SaveGame();
    }
}
