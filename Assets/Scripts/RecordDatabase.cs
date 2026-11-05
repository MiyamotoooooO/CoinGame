using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Resources/RecordData.csv を読み込んで、レコードの一覧を持つ。
///
/// CSV は Excel から書き出したものをそのまま使える。
/// ・"..." で囲まれた値の中のカンマや改行（「累計1,000枚」や、見出しの改行）も正しく読む
/// ・値2 が文字（Friction など）の場合は value2Text に入る
/// </summary>
public class RecordDatabase : MonoBehaviour
{
    public const string ResourceName = "RecordData";

    public List<RecordData> records = new();

    void Awake()
    {
        records = LoadFromResources();
    }

    /// <summary>キー（英名）でレコードを探す。見つからなければ null。</summary>
    public RecordData FindByKey(string key) => records.Find(r => r.key == key);

    /// <summary>ID（R001 など）でレコードを探す。見つからなければ null。</summary>
    public RecordData FindById(string id) => records.Find(r => r.id == id);

    /// <summary>Resources/RecordData.csv を読み込む。シーンに RecordDatabase が無くても使える。</summary>
    public static List<RecordData> LoadFromResources()
    {
        var list = new List<RecordData>();
        TextAsset csv = Resources.Load<TextAsset>(ResourceName);
        if (csv == null)
        {
            Debug.LogError($"RecordDatabase: Resources/{ResourceName}.csv が見つかりません。");
            return list;
        }

        List<List<string>> rows = ParseCsv(csv.text);
        for (int i = 1; i < rows.Count; i++)   // 0行目は見出し
        {
            List<string> c = rows[i];
            if (c.Count < 16 || string.IsNullOrWhiteSpace(c[0]))
            {
                continue;   // 空の行や、列が足りない行は飛ばす
            }

            var record = new RecordData
            {
                id = c[0].Trim(),
                name = c[1].Trim(),
                key = c[2].Trim(),
                type = c[3].Trim(),
                category = c[4].Trim(),
                rarity = c[5].Trim(),
                buyPrice = ParseInt(c[6]),
                sellPrice = ParseInt(c[7]),
                effectText = c[8].Trim(),
                effectType = c[9].Trim(),
                value1 = ParseFloat(c[10]),
                value2 = ParseFloat(c[11]),
                value2Text = c[11].Trim(),
                duration = ParseFloat(c[12]),
                gaugeCost = ParseFloat(c[13]),
                obtainMethod = c[14].Trim(),
                unlockCondition = c[15].Trim() == "—" ? "" : c[15].Trim(),
            };
            list.Add(record);
        }

        Debug.Log($"RecordDatabase: レコードを {list.Count} 件読み込みました。");
        return list;
    }

    /// <summary>
    /// CSV の文字列を行と列に分ける。
    /// "..." で囲まれた値の中のカンマ・改行・""（二重の引用符 = 引用符1つ）に対応する。
    /// </summary>
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');   // "" は引用符そのもの
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;   // Windows の改行（\r\n）の \r は無視する
                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    break;
                case '﻿':
                    break;   // ファイルの先頭の BOM は無視する
                default:
                    cell.Append(ch);
                    break;
            }
        }

        // 最後の行（改行で終わっていない場合）
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }
        return rows;
    }

    static int ParseInt(string s)
    {
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v);
        return v;
    }

    static float ParseFloat(string s)
    {
        float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
        return v;
    }
}
