using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EquipManager : MonoBehaviour
{
    public RecordData[] equippedRecords = new RecordData[3];

    public void EquipRecord(RecordData record, int slot)
    {
        equippedRecords[slot] = record;

        Debug.Log($"Slot{slot + 1} ‚É {record.name} ‚ð‘•’…");
    }
}
