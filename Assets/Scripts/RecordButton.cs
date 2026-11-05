using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class RecordButton : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    private RecordData record;

    public void Setup(RecordData data)
    {
        record = data;
        nameText.text = record.name;
    }

    public void OnClick()
    {
        Debug.Log(record.name);
    }
}
