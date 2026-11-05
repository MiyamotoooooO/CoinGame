/// <summary>
/// レコード1枚のデータ。Resources/RecordData.csv の1行に対応する。
/// 列の意味は Docs/レコード一覧_メダルゲーム.xlsx と同じ。
///
/// レコードは1枚ずつ流す。流すときに gaugeCost ぶんのグルーヴゲージを使い、
/// 効果は曲が終わるまで続く（曲が無いレコードは duration 秒）。
/// </summary>
[System.Serializable]
public class RecordData
{
    public string id;
    public string name;
    public string key;
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
    public float duration;       // 曲が無いときの効果時間（秒）
    public float gaugeCost;      // 流すときに使うグルーヴゲージの量

    public string obtainMethod;  // 初期 / 解禁
    public string unlockCondition;

    /// <summary>最初から使えるレコードか</summary>
    public bool IsStarter => obtainMethod == "初期";

    /// <summary>
    /// 中身が空か。Unity は空の枠も「中身が空のデータ」として保存するため、null だけでなくこれで確かめる。
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(key);

    public static bool IsNullOrEmpty(RecordData record) => record == null || record.IsEmpty;
}
