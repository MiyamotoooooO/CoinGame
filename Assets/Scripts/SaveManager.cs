using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance;

    private string savePath;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        savePath = Application.persistentDataPath + "/save.json";
    }

    public void SaveGame(int area, int round, int medal, int point, string slot1, string slot2, string slot3)
    {
        SaveData data = new SaveData();

        data.area = area;
        data.round = round;
        data.medal = medal;
        data.point = point;

        data.slot1 = slot1;
        data.slot2 = slot2;
        data.slot3 = slot3;

        string json = JsonUtility.ToJson(data, true);

        File.WriteAllText(savePath, json);

        Debug.Log("セーブ完了");
    }

    public SaveData LoadGame()
    {
        if (!File.Exists(savePath))
        {
            Debug.Log("セーブデータなし");
            return null;
        }

        string json = File.ReadAllText(savePath);

        SaveData data = JsonUtility.FromJson<SaveData>(json);

        Debug.Log("ロード完了");

        return data;
    }
}
