using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// レコードのジャケット画像と、系統の色をまとめて持つ。RecordEffectSystem と同じ GameObject に付けて使う。
    ///
    /// ・ジャケット画像は「ファイル名 = レコードのキー（英名）」で紐づける（Pops.png → ポップス）
    /// ・画像は Inspector の Jackets にまとめてドラッグする。Resources/RecordJacket/ に置いた画像も、同じ名前で自動で探す
    /// ・画像が無いレコードは、所持一覧で系統の色の仮ジャケットになる
    /// ・所持一覧（RecordCollectionScreen）と、ターンテーブルのラベル（TurntableController）の両方がここを見る
    /// </summary>
    [DisallowMultipleComponent]
    public class RecordArtwork : MonoBehaviour
    {
        /// <summary>Resources の中で、ジャケット画像を自動で探すフォルダ</summary>
        public const string ResourceFolder = "RecordJacket";

        [Header("ジャケット画像")]
        [Tooltip("ジャケット画像。ファイル名をレコードのキー（英名）にする（Pops.png など）。Project ウィンドウから複数まとめてドラッグできる")]
        public Texture2D[] jackets = new Texture2D[0];

        readonly Dictionary<string, Texture2D> jacketByKey = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Texture2D> resourceCache = new Dictionary<string, Texture2D>();
        bool built;

        /// <summary>系統ごとの色（レコード一覧のスプレッドシートの「系統」シートと同じ）</summary>
        static readonly Dictionary<string, Color> CategoryColors = new Dictionary<string, Color>
        {
            { "加算", new Color32(0xF2, 0x8C, 0x28, 0xFF) },
            { "倍率", new Color32(0xD9, 0x4A, 0x4A, 0xFF) },
            { "物理", new Color32(0x3A, 0x7B, 0xD5, 0xFF) },
            { "フィールド", new Color32(0x3A, 0xA7, 0x6D, 0xFF) },
            { "経済", new Color32(0xC9, 0xA2, 0x27, 0xFF) },
            { "条件", new Color32(0x8E, 0x5B, 0xD9, 0xFF) },
            { "変化", new Color32(0xD9, 0x48, 0x8F, 0xFF) },
            // 2026/11/05 のリニューアル案の系統（「物理」は上と同じ色）
            { "定番", new Color32(0xF2, 0x8C, 0x28, 0xFF) },
            { "盤面", new Color32(0x3A, 0xA7, 0x6D, 0xFF) },
            { "モニター", new Color32(0x8E, 0x5B, 0xD9, 0xFF) },
            { "メダル", new Color32(0xD9, 0x48, 0x8F, 0xFF) },
        };

        /// <summary>系統の色。知らない系統なら灰色。</summary>
        public static Color CategoryColor(string category)
        {
            if (!string.IsNullOrEmpty(category) && CategoryColors.TryGetValue(category, out Color color)) return color;
            return new Color(0.55f, 0.55f, 0.6f);
        }

        /// <summary>系統の色があるか（TurntableController が、知らない系統のときは元の色のままにするのに使う）</summary>
        public static bool HasCategoryColor(string category) => !string.IsNullOrEmpty(category) && CategoryColors.ContainsKey(category);

        void Awake()
        {
            Build();
        }

        void OnValidate()
        {
            built = false;   // 再生中に Jackets を変えても、次に探すときに作り直す
        }

        void Build()
        {
            jacketByKey.Clear();
            if (jackets != null)
            {
                foreach (Texture2D texture in jackets)
                {
                    if (texture != null) jacketByKey[texture.name] = texture;
                }
            }
            built = true;
        }

        /// <summary>レコードのジャケット画像を探す。Inspector の Jackets → Resources/RecordJacket/ の順に探す。無ければ null。</summary>
        public Texture2D GetJacket(RecordData record)
        {
            if (RecordData.IsNullOrEmpty(record)) return null;
            if (!built) Build();
            if (jacketByKey.TryGetValue(record.key, out Texture2D texture)) return texture;
            if (!resourceCache.TryGetValue(record.key, out texture))
            {
                texture = Resources.Load<Texture2D>(ResourceFolder + "/" + record.key);
                resourceCache[record.key] = texture;   // 見つからなかったことも覚えて、毎回探さないようにする
            }
            return texture;
        }
    }
}
