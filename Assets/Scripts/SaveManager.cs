using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    string savePath;

    private void Awake()
    {
        savePath = Application.persistentDataPath + "/save.json";
    }

    public void DeleteSave()
    {
        if (File.Exists(savePath))
        {
            File.Delete(savePath);
            Debug.Log("セーブデータ削除");
        }
    }

    public void Save(int area, int round, int medal, int point)
    {
        SaveData data = new SaveData();

        data.area = area;
        data.round = round;
        data.medal = medal;
        data.point = point;

        string json = JsonUtility.ToJson(data, true);

        File.WriteAllText(savePath, json);

        Debug.Log("セーブ完了");
        Debug.Log(json);
    }

    public SaveData Load()
    {
        if (!File.Exists(savePath))
        {
            Debug.Log("セーブデータなし");
            return null;
        }

        string json = File.ReadAllText(savePath);

        SaveData data = JsonUtility.FromJson<SaveData>(json);

        Debug.Log("ロード完了");
        Debug.Log(json);

        return data;
    }
}
