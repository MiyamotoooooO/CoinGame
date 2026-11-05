/// <summary>
/// レコード1枚のデータ。Resources/RecordData.csv の1行に対応する。
/// 列の意味は Docs/レコード一覧_メダルゲーム.xlsx と同じ。
/// </summary>
[System.Serializable]
public class RecordData
{
    public string id;
    public string name;
    public string key;
    public string type;          // 常時 / 再生
    public string category;      // 加算 / 倍率 / 物理 / フィールド / 経済 / 条件 / 変化
    public string rarity;        // コモン / レア / エピック / レジェンド

    public int buyPrice;
    public int sellPrice;

    public string effectText;
    public string effectType;    // 効果の型（AddPerDrop など）

    public float value1;
    public float value2;
    /// <summary>値2を文字のまま持つ（Friction、BlockSides など数字でない値のため）</summary>
    public string value2Text;
    public float duration;       // 効果時間（秒）。0 なら時間の決まりなし
    public float gaugeCost;      // 再生に使うグルーヴゲージの量

    public string obtainMethod;  // 初期 / 解禁
    public string unlockCondition;

    /// <summary>再生型（グルーヴゲージを使って再生する）か</summary>
    public bool IsPlayType => type == "再生";

    /// <summary>最初から使えるレコードか</summary>
    public bool IsStarter => obtainMethod == "初期";

    /// <summary>
    /// 中身が空か。Unity は空の枠も「中身が空のデータ」として保存するため、null だけでなくこれで確かめる。
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(key);

    public static bool IsNullOrEmpty(RecordData record) => record == null || record.IsEmpty;
}
