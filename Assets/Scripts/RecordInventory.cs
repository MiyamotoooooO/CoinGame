using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤーが所持しているレコード。同じレコードは1枚まで持てる。
/// ショップでの購入や報酬で手に入れたら Add、売ったら Remove を呼ぶ。
/// 中身が変わると Changed が呼ばれる。
/// </summary>
public class RecordInventory : MonoBehaviour
{
    [Header("最初から持っているレコード（テスト用）")]
    [Tooltip("オンにすると、CSV で「初期」になっているレコードを全部持った状態で始める")]
    public bool ownAllStarterRecords = true;
    [Tooltip("ほかに最初から持たせるレコードのキー（英名）")]
    public string[] startOwnedKeys = new string[0];

    /// <summary>所持しているレコード（手に入れた順）</summary>
    public IReadOnlyList<RecordData> Owned => owned;

    /// <summary>所持しているレコードが変わったときに呼ばれる</summary>
    public event System.Action Changed;

    readonly List<RecordData> owned = new List<RecordData>();
    List<RecordData> allRecords;

    void Awake()
    {
        allRecords = RecordDatabase.LoadFromResources();

        if (ownAllStarterRecords)
        {
            foreach (RecordData record in allRecords)
            {
                if (record.IsStarter) AddWithoutNotify(record);
            }
        }
        if (startOwnedKeys != null)
        {
            foreach (string key in startOwnedKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                RecordData record = FindInDatabase(key);
                if (record == null) Debug.LogWarning($"RecordInventory: キー「{key}」のレコードが CSV にありません。");
                else AddWithoutNotify(record);
            }
        }
    }

    /// <summary>CSV の全レコードからキー（英名）で探す。見つからなければ null。</summary>
    public RecordData FindInDatabase(string key)
    {
        if (allRecords == null || string.IsNullOrWhiteSpace(key)) return null;
        string k = key.Trim();
        return allRecords.Find(r => r.key == k);
    }

    /// <summary>そのレコードを持っているか</summary>
    public bool Has(string key) => owned.Exists(r => r.key == key);

    /// <summary>レコードを手に入れる。すでに持っていたら何もしない（false を返す）。</summary>
    public bool Add(RecordData record)
    {
        if (!AddWithoutNotify(record)) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>キー（英名）でレコードを手に入れる。</summary>
    public bool Add(string key) => Add(FindInDatabase(key));

    /// <summary>レコードを手放す（売るなど）。持っていなければ false。</summary>
    public bool Remove(string key)
    {
        int removed = owned.RemoveAll(r => r.key == key);
        if (removed == 0) return false;
        Changed?.Invoke();
        return true;
    }

    bool AddWithoutNotify(RecordData record)
    {
        if (RecordData.IsNullOrEmpty(record) || Has(record.key)) return false;
        owned.Add(record);
        return true;
    }
}
