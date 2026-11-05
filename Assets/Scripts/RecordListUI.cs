using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RecordListUI : MonoBehaviour
{
    public RecordDatabase database;

    public Transform content;

    public GameObject buttonPrefab;

    private void Start()
    {
        //Debug.Log("Starté¿çs");
        //Debug.Log("Recordêî:" + database.records.Count);

        foreach (var record in database.records)
        {
            //Debug.Log("ê∂ê¨:" + record.name);

            GameObject obj = Instantiate(buttonPrefab, content);

            RecordButton button = obj.GetComponent<RecordButton>();

            button.Setup(record);
        }
    }
}
