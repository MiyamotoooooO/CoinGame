using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ターンテーブルの装着枠を管理する。初期3枠、ショップで最大5枠まで増やせる。
/// 装着が変わると EquipmentChanged が呼ばれる（RecordEffectSystem が効果を付け直す）。
/// </summary>
public class EquipManager : MonoBehaviour
{
    public const int MaxSlots = 5;

    [Tooltip("今使える装着枠の数（初期3、ショップで最大5）")]
    [Range(1, MaxSlots)] public int slotCount = 3;

    public RecordData[] equippedRecords = new RecordData[MaxSlots];

    /// <summary>装着が変わったときに呼ばれる</summary>
    public event System.Action EquipmentChanged;

    void Awake()
    {
        // シーンに保存されている枠の数が足りないときは、最大数まで広げる
        if (equippedRecords == null || equippedRecords.Length < MaxSlots)
        {
            var resized = new RecordData[MaxSlots];
            if (equippedRecords != null)
            {
                System.Array.Copy(equippedRecords, resized, equippedRecords.Length);
            }
            equippedRecords = resized;
        }
    }

    /// <summary>
    /// slot 番目（0から）の枠にレコードを付ける。
    /// 同じレコードがほかの枠に付いていたら、そちらは外す（同じレコードは1枚しか持てないため）。
    /// </summary>
    public bool EquipRecord(RecordData record, int slot)
    {
        if (RecordData.IsNullOrEmpty(record) || slot < 0 || slot >= slotCount)
        {
            Debug.LogWarning($"EquipManager: 枠{slot + 1} には付けられません（使える枠は {slotCount} つ）。");
            return false;
        }

        for (int i = 0; i < equippedRecords.Length; i++)
        {
            if (i != slot && !RecordData.IsNullOrEmpty(equippedRecords[i]) && equippedRecords[i].key == record.key)
            {
                equippedRecords[i] = null;
            }
        }

        equippedRecords[slot] = record;
        Debug.Log($"枠{slot + 1} に {record.name} を装着");
        EquipmentChanged?.Invoke();
        return true;
    }

    /// <summary>slot 番目（0から）の枠のレコードを外す。</summary>
    public void Unequip(int slot)
    {
        if (slot < 0 || slot >= equippedRecords.Length || RecordData.IsNullOrEmpty(equippedRecords[slot]))
        {
            return;
        }
        Debug.Log($"枠{slot + 1} の {equippedRecords[slot].name} を外しました");
        equippedRecords[slot] = null;
        EquipmentChanged?.Invoke();
    }

    /// <summary>slot 番目（0から）の枠のレコード。空なら null。</summary>
    public RecordData GetSlot(int slot)
    {
        if (slot < 0 || slot >= slotCount)
        {
            return null;
        }
        RecordData record = equippedRecords[slot];
        return RecordData.IsNullOrEmpty(record) ? null : record;
    }

    /// <summary>使える枠に付いているレコード（空の枠は飛ばす）</summary>
    public IEnumerable<RecordData> Equipped
    {
        get
        {
            for (int i = 0; i < slotCount; i++)
            {
                RecordData record = GetSlot(i);
                if (record != null)
                {
                    yield return record;
                }
            }
        }
    }

    /// <summary>付いているレコードの枚数</summary>
    public int EquippedCount
    {
        get
        {
            int count = 0;
            foreach (RecordData _ in Equipped)
            {
                count++;
            }
            return count;
        }
    }

    /// <summary>空いている最初の枠の番号（0から）。空きが無ければ -1。</summary>
    public int FirstEmptySlot()
    {
        for (int i = 0; i < slotCount; i++)
        {
            if (GetSlot(i) == null)
            {
                return i;
            }
        }
        return -1;
    }
}
