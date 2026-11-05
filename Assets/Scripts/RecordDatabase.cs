using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Resources/RecordData.csv を読み込んで、レコードの一覧を持つ。
///
/// CSV は Excel から書き出したものをそのまま使える。
/// ・列は1行目の見出しの名前で探す（列の順番を入れ替えたり、列を足したりしても読める）
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
        if (rows.Count == 0) return list;

        // 見出しから、それぞれの列が何番目かを調べる
        var columns = new Columns(rows[0]);
        if (columns.id < 0 || columns.name < 0 || columns.key < 0)
        {
            Debug.LogError($"RecordDatabase: {ResourceName}.csv の見出しに「ID」「名前」「キー（英名）」が見つかりません。");
            return list;
        }

        for (int i = 1; i < rows.Count; i++)   // 0行目は見出し
        {
            List<string> c = rows[i];
            if (Get(c, columns.id) == "") continue;   // 空の行は飛ばす

            string unlock = Get(c, columns.unlock);
            var record = new RecordData
            {
                id = Get(c, columns.id),
                name = Get(c, columns.name),
                key = Get(c, columns.key),
                category = Get(c, columns.category),
                rarity = Get(c, columns.rarity),
                buyPrice = ParseInt(Get(c, columns.buy)),
                sellPrice = ParseInt(Get(c, columns.sell)),
                effectText = Get(c, columns.effect),
                effectType = Get(c, columns.effectType),
                value1 = ParseFloat(Get(c, columns.value1)),
                value2 = ParseFloat(Get(c, columns.value2)),
                value2Text = Get(c, columns.value2),
                duration = ParseFloat(Get(c, columns.duration)),
                gaugeCost = ParseFloat(Get(c, columns.gauge)),
                obtainMethod = Get(c, columns.obtain),
                unlockCondition = unlock == "—" ? "" : unlock,
            };
            list.Add(record);
        }

        Debug.Log($"RecordDatabase: レコードを {list.Count} 件読み込みました。");
        return list;
    }

    /// <summary>見出しの名前から調べた、列の番号（見つからなければ -1）</summary>
    class Columns
    {
        public int id = -1, name = -1, key = -1, category = -1, rarity = -1, buy = -1, sell = -1;
        public int effect = -1, effectType = -1, value1 = -1, value2 = -1, duration = -1, gauge = -1, obtain = -1, unlock = -1;

        public Columns(List<string> header)
        {
            for (int i = 0; i < header.Count; i++)
            {
                // 改行や空白を取り除いてから比べる（「購入価格\n（メダル）」→「購入価格（メダル）」）
                string h = Normalize(header[i]);
                if (h == "ID") id = i;
                else if (h.StartsWith("名前")) name = i;
                else if (h.StartsWith("キー")) key = i;
                else if (h.StartsWith("系統")) category = i;
                else if (h.StartsWith("レアリティ")) rarity = i;
                else if (h.StartsWith("購入")) buy = i;
                else if (h.StartsWith("売却")) sell = i;
                else if (h.StartsWith("効果の型")) effectType = i;
                else if (h.StartsWith("効果時間")) duration = i;
                else if (h == "効果") effect = i;
                else if (h.StartsWith("値1")) value1 = i;
                else if (h.StartsWith("値2")) value2 = i;
                else if (h.StartsWith("ゲージ")) gauge = i;
                else if (h.StartsWith("入手")) obtain = i;
                else if (h.StartsWith("解禁")) unlock = i;
            }
        }

        static string Normalize(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            }
            return sb.ToString();
        }
    }

    /// <summary>index 番目の値（列が無い・足りないときは空の文字）</summary>
    static string Get(List<string> row, int index)
    {
        if (index < 0 || index >= row.Count) return "";
        return row[index].Trim();
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
