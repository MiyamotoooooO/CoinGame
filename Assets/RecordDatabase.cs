using System.Collections.Generic;
using UnityEngine;

public class RecordDatabase : MonoBehaviour
{
    public List<RecordData> records = new();

    void Awake()
    {
        LoadCSV();
    }

    void LoadCSV()
    {
        TextAsset csv = Resources.Load<TextAsset>("RecordData");

        //Debug.Log(csv.text);

        string[] lines = csv.text.Split('\n');

        for (int i = 1; i < lines.Length; i++)
        {
            //Debug.Log(lines[i]);

            string[] data = lines[i].Split(',');

            //Debug.Log(data.Length);

            if (data.Length < 11)
                continue;

            RecordData record = new();

            record.id = data[0];
            record.name = data[1];
            record.key = data[2];
            record.type = data[3];
            record.category = data[4];
            record.rarity = data[5];

            int.TryParse(data[6], out record.buyPrice);
            int.TryParse(data[7], out record.sellPrice);

            record.effectText = data[8];
            record.effectType = data[9];

            float.TryParse(data[10], out record.value1);
            float.TryParse(data[11], out record.value2);
            float.TryParse(data[12], out record.duration);
            float.TryParse(data[13], out record.gaugeCost);

            record.obtainMethod = data[14];
            record.unlockCondition = data[15];

            records.Add(record);

            //Debug.Log("’Ç‰ÁŒã:" + records.Count);
        }

        //Debug.Log("s”:" + lines.Length);
        //Debug.Log("ÅIŒ”:" + records.Count);
    }
}