using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// モニターに出す「レコード用の画面」（MonitorDisplay の続き）。
    ///
    /// ・レコードの効果の発動中に、まん中の「今のポイント」の所を、そのレコードの画面に入れかえる（天気予報と同じ出し方）
    ///   上の帯・左下の札・下のレコード・右下の所持メダルはそのまま残る
    /// ・画面の上に細い帯があり、今のポイントと目標（ボス戦は残りHP）を小さく出し続ける
    /// ・中身はレコードごとのスクリプト（FolkRecord など）が作る：
    ///     var body = monitor.ShowRecordScreen("Folk", "FOLK", "弾き語りの楽譜すごろく");   // 中身を入れる四角が返る
    ///     monitor.ScreenImage(body, ...); monitor.ScreenLabel(body, ...);                   // 部品を作る道具
    ///     monitor.HideRecordScreen("Folk");                                                // 終わったら消す
    ///   中身の座標は、body の左上が (0, 0)、大きさ RecordBodyW × RecordBodyH
    /// ・別のレコードの画面を出すと、前の中身は消して入れかえる
    /// </summary>
    public partial class MonitorDisplay
    {
        /// <summary>レコード用の画面の中身の大きさ（帯の下）</summary>
        public const float RecordBodyW = 800f;
        public const float RecordBodyH = 386f;
        const float RecordHeaderH = 44f;
        const float RecordScreenCX = CanvasW * 0.5f;  // 中身の座標で、画面のまん中
        const float RecordScreenCY = 402f;            // タブの下からレコードの上まで

        /// <summary>部品の形</summary>
        public enum ScreenShape { Square, Circle, Pill, Ring }

        RectTransform recordScreenRoot, recordBody;
        CanvasGroup recordScreenGroup;
        Text recordTitleText, recordSubText, recordScoreText;
        string recordScreenOwner;
        float recordScreenAnim;

        /// <summary>レコード用の画面を出しているか</summary>
        public bool IsRecordScreenShown => recordScreenOwner != null;

        void BuildRecordScreen(RectTransform root)
        {
            float w = RecordBodyW, h = RecordBodyH + RecordHeaderH;
            recordScreenRoot = NewRect("RecordScreen", root, RecordScreenCX, RecordScreenCY, w, h);
            recordScreenGroup = recordScreenRoot.gameObject.AddComponent<CanvasGroup>();
            recordScreenGroup.alpha = 0f;

            // 枠と、上の帯
            var frame = NewImage("Frame", recordScreenRoot, w * 0.5f, h * 0.5f, w, h, Line, pillSprite);
            frame.type = Image.Type.Sliced;
            var face = NewImage("Face", recordScreenRoot, w * 0.5f, h * 0.5f, w - 6f, h - 6f, new Color32(0x1b, 0x1c, 0x24, 0xff), pillSprite);
            face.type = Image.Type.Sliced;
            NewImage("HeaderLine", recordScreenRoot, w * 0.5f, RecordHeaderH, w - 40f, 2f, Line);
            recordTitleText = Tag("Title", recordScreenRoot, 90f, RecordHeaderH * 0.5f + 2f, 130f, 28f, "", 16, Accent, Panel).text;
            recordSubText = Label("Sub", recordScreenRoot, 170f, RecordHeaderH * 0.5f + 2f, 360f, 30f, "", 20, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            recordScoreText = Label("Score", recordScreenRoot, w - 24f, RecordHeaderH * 0.5f + 2f, 300f, 30f, "", 22, Fg, TextAnchor.MiddleRight, displayFont, FontStyle.Bold);
            recordScoreText.supportRichText = true;

            recordBody = NewRect("Body", recordScreenRoot, w * 0.5f, RecordHeaderH + RecordBodyH * 0.5f, RecordBodyW, RecordBodyH);
            recordScreenRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// レコード用の画面を出す。owner はレコードのキー（消す時に同じものを渡す）。
        /// 中身を入れる四角（左上が (0, 0)、大きさ RecordBodyW × RecordBodyH）を返す。前の中身は消す。
        /// </summary>
        public RectTransform ShowRecordScreen(string owner, string title, string subtitle)
        {
            if (recordBody == null) return null;
            for (int i = recordBody.childCount - 1; i >= 0; i--) Destroy(recordBody.GetChild(i).gameObject);
            recordScreenOwner = owner;
            recordTitleText.text = title;
            recordSubText.text = subtitle;
            recordScreenRoot.gameObject.SetActive(true);
            return recordBody;
        }

        /// <summary>レコード用の画面を消す（owner が今出している画面と同じ時だけ）。</summary>
        public void HideRecordScreen(string owner)
        {
            if (recordScreenOwner == owner) recordScreenOwner = null;
        }

        /// <summary>レコード用の画面の中身の座標を、PopText に渡す座標（モニターの中身の座標）に直す。</summary>
        public Vector2 RecordBodyToCanvas(Vector2 p) =>
            new Vector2(RecordScreenCX - RecordBodyW * 0.5f + p.x, RecordScreenCY - (RecordBodyH + RecordHeaderH) * 0.5f + RecordHeaderH + p.y);

        void UpdateRecordScreen()
        {
            if (recordScreenRoot == null) return;
            recordScreenAnim = Mathf.MoveTowards(recordScreenAnim, IsRecordScreenShown ? 1f : 0f, Time.deltaTime * 4f);
            recordScreenGroup.alpha = recordScreenAnim;
            float s = Mathf.Lerp(0.96f, 1f, recordScreenAnim);
            recordScreenRoot.localScale = new Vector3(s, s, 1f);
            bool visible = recordScreenAnim > 0.001f;
            if (recordScreenRoot.gameObject.activeSelf != visible) recordScreenRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                // 消し終わったら中身も消す（次に出す時に作り直す）
                if (recordScreenOwner == null && recordBody.childCount > 0)
                    for (int i = recordBody.childCount - 1; i >= 0; i--) Destroy(recordBody.GetChild(i).gameObject);
                return;
            }

            // 上の帯：今のポイントと目標（ボス戦は残りHP）
            int quota = Mathf.Max(1, game.CurrentQuota);
            recordScoreText.text = game.IsBossRound
                ? $"<size=16>ボス HP</size> {Mathf.Max(0, game.BossHp):N0} <size=16>/ {quota:N0}</size>"
                : $"<size=16>現在</size> {game.Points:N0} <size=16>/ 目標 {quota:N0}</size>";
        }

        // ================================================================
        // レコードの画面を作る道具（レコードごとのスクリプトから使う）
        // ================================================================

        /// <summary>モニターの日本語用のフォント</summary>
        public Font BodyFont => font;
        /// <summary>モニターの英字・数字用のフォント</summary>
        public Font NumberFont => displayFont;

        /// <summary>四角や丸の部品を作る。座標は parent の左上が (0, 0)、(cx, cy) が中心。</summary>
        public Image ScreenImage(Transform parent, string name, float cx, float cy, float w, float h, Color color, ScreenShape shape = ScreenShape.Square)
        {
            Sprite sprite = shape switch
            {
                ScreenShape.Circle => circleSprite,
                ScreenShape.Pill => pillSprite,
                ScreenShape.Ring => pillRingSprite,
                _ => whiteSprite
            };
            var img = NewImage(name, parent, cx, cy, w, h, color, sprite);
            if (shape == ScreenShape.Pill) img.type = Image.Type.Sliced;
            return img;
        }

        /// <summary>文字を作る。x は左寄せなら左端・右寄せなら右端・中央寄せなら中心。number が true なら英字・数字用のフォント。</summary>
        public Text ScreenLabel(Transform parent, string name, float x, float cy, float w, float h, string text, int size, Color color,
                                TextAnchor align = TextAnchor.MiddleCenter, bool number = false, bool bold = true)
        {
            var t = Label(name, parent, x, cy, w, h, text, size, color, align, number ? displayFont : font, bold ? FontStyle.Bold : FontStyle.Normal);
            t.supportRichText = true;
            return t;
        }

        /// <summary>何も描かない入れ物（まとめて動かす時に使う）。</summary>
        public RectTransform ScreenGroup(Transform parent, string name, float cx, float cy, float w, float h) => NewRect(name, parent, cx, cy, w, h);

        /// <summary>斜めのタグ（平行四辺形の背景に、太い文字）。</summary>
        public Text ScreenTag(Transform parent, string name, float cx, float cy, float w, float h, string text, int size, Color bg, Color fg) =>
            Tag(name, parent, cx, cy, w, h, text, size, bg, fg).text;
    }
}
