using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// モニターに出す「レコード用の画面」（MonitorDisplay の続き）。
    ///
    /// ・レコードの効果の発動中は、モニターの画面ぜんたいを、そのレコードの画面にする
    /// ・いつもの情報は残して、空いている所へ動かす
    ///     上の帯（ラウンド・ラウンド名）　　　　　　　　　→ そのまま
    ///     まん中の今のポイント・目標のバー　　　　　　　　→ 左上の「SCORE」の枠へ、小さくして動かす
    ///     NOW PLAYING の帯（効果の残り時間）　　　　　　　→ 下のまん中へ動かす
    ///     左下の札・右下の所持メダル　　　　　　　　　　　→ そのまま（画面の上に重ねて出す）
    ///     下のレコードとイコライザー　　　　　　　　　　　→ 下へしまう
    ///   右上の枠には、レコードの名前と効果の残り時間を出す
    /// ・中身はレコードごとのスクリプト（FolkRecord など）が作る：
    ///     var body = monitor.ShowRecordScreen("Folk", "FOLK", "弾き語りの楽譜すごろく");   // 中身を入れる四角が返る
    ///     monitor.ScreenImage(body, ...); monitor.ScreenLabel(body, ...);                   // 部品を作る道具
    ///     monitor.HideRecordScreen("Folk");                                                // 終わったら消す
    ///   中身の座標は、body の左上が (0, 0)、大きさ RecordBodyW × RecordBodyH
    ///   （横の幅 RecordBodyW はモニターの広げ方で変わるので、ShowRecordScreen のあとに読む）
    /// ・別のレコードの画面を出すと、前の中身は消して入れかえる
    /// </summary>
    public partial class MonitorDisplay
    {
        // ---- 配置（中身の座標。画面の上から） ----
        const float RecordFrameTop = 110f;      // 画面の枠の上（上の帯の紫の線の下）
        const float RecordFrameBottom = 890f;
        const float RecordBodyTop = 186f;       // 中身の上（タブの下）
        const float RecordBodyBottom = 790f;    // 中身の下（NOW PLAYING の上）
        const float RecordBodyMinW = 800f;      // 左下の札と右下の所持メダルの間（広げる前）
        const float RecordSideX = 185f;         // 左右の枠の中心（左の枠。右は CanvasW - これ）
        const float RecordSideW = 290f;
        const float RecordSideTop = 124f;
        const float RecordSideH = 200f;
        const float RecordScoreScale = 0.33f;   // 今のポイントを小さくする割合
        const float RecordScoreY = 250f;        // 小さくした今のポイントの中心
        const float RecordNowPlayingY = 838f;   // NOW PLAYING を動かす先

        /// <summary>レコード用の画面の中身の高さ</summary>
        public const float RecordBodyH = RecordBodyBottom - RecordBodyTop;
        /// <summary>レコード用の画面の中身の横の幅（ShowRecordScreen の時に決まる）</summary>
        public float RecordBodyW { get; private set; } = RecordBodyMinW;

        /// <summary>部品の形</summary>
        public enum ScreenShape { Square, Circle, Pill, Ring }

        RectTransform recordScreenRoot, recordBody, recordFrame, recordFace, recordLeftCard, recordRightCard;
        CanvasGroup recordScreenGroup;
        Text recordTitleText, recordSubText, recordTimeText;
        Image recordTimeFill;
        string recordScreenOwner;
        float recordScreenAnim;
        bool recordLayoutMoved;   // いつもの物を動かしているか（戻し終わるまで true）

        /// <summary>レコード用の画面を出しているか</summary>
        public bool IsRecordScreenShown => recordScreenOwner != null;

        void BuildRecordScreen(RectTransform root)
        {
            // 画面ぜんたいの入れ物（中身の座標そのまま）。今のポイントの層より奥に置く
            recordScreenRoot = NewRect("RecordScreen", root, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH);
            if (normalLayer != null) recordScreenRoot.SetSiblingIndex(normalLayer.GetSiblingIndex());
            recordScreenGroup = recordScreenRoot.gameObject.AddComponent<CanvasGroup>();
            recordScreenGroup.alpha = 0f;

            // 枠（横の大きさは毎フレーム、広げ方に合わせる）
            float fh = RecordFrameBottom - RecordFrameTop, fy = (RecordFrameTop + RecordFrameBottom) * 0.5f;
            var frame = NewImage("Frame", recordScreenRoot, CanvasW * 0.5f, fy, CanvasW, fh, Line, pillSprite);
            frame.type = Image.Type.Sliced;
            recordFrame = frame.rectTransform;
            var face = NewImage("Face", recordScreenRoot, CanvasW * 0.5f, fy, CanvasW - 6f, fh - 6f, new Color32(0x1b, 0x1c, 0x24, 0xff), pillSprite);
            face.type = Image.Type.Sliced;
            recordFace = face.rectTransform;

            // 左上の枠：小さくした今のポイントが入る
            recordLeftCard = SideCard("ScoreCard", RecordSideX, -1f);
            Tag("ScoreTag", recordLeftCard, 70f, 24f, 100f, 24f, "SCORE", 14, Accent, Panel);

            // 右上の枠：レコードの名前と、効果の残り時間
            recordRightCard = SideCard("TitleCard", CanvasW - RecordSideX, 1f);
            recordTitleText = Tag("Title", recordRightCard, 80f, 24f, 120f, 24f, "", 14, Accent, Panel).text;
            recordSubText = Label("Sub", recordRightCard, 20f, 74f, RecordSideW - 40f, 56f, "", 20, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            recordSubText.horizontalOverflow = HorizontalWrapMode.Wrap;
            recordSubText.verticalOverflow = VerticalWrapMode.Truncate;
            Label("TimeLabel", recordRightCard, 20f, 132f, 120f, 30f, "残り", 18, Mute, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            recordTimeText = Label("Time", recordRightCard, RecordSideW - 20f, 132f, 180f, 50f, "", 40, Fg, TextAnchor.MiddleRight, displayFont, FontStyle.Bold);
            NewImage("TimeBg", recordRightCard, RecordSideW * 0.5f, 172f, RecordSideW - 40f, 6f, Line);
            recordTimeFill = NewImage("TimeFill", recordRightCard, RecordSideW * 0.5f, 172f, RecordSideW - 40f, 6f, Accent, whiteSprite);
            recordTimeFill.type = Image.Type.Filled;
            recordTimeFill.fillMethod = Image.FillMethod.Horizontal;

            // 中身（レコードごとのスクリプトが作る）
            recordBody = NewRect("Body", recordScreenRoot, CanvasW * 0.5f, (RecordBodyTop + RecordBodyBottom) * 0.5f, RecordBodyMinW, RecordBodyH);
            recordScreenRoot.gameObject.SetActive(false);
        }

        /// <summary>左右の枠（side は -1 が左、1 が右。広げる時に外へずらす）。</summary>
        RectTransform SideCard(string name, float cx, float side)
        {
            var r = NewRect(name, recordScreenRoot, cx, RecordSideTop + RecordSideH * 0.5f, RecordSideW, RecordSideH);
            Spread(r, side);
            var border = NewImage("Border", r, RecordSideW * 0.5f, RecordSideH * 0.5f, RecordSideW, RecordSideH, Line, pillSprite);
            border.type = Image.Type.Sliced;
            var fill = NewImage("Fill", r, RecordSideW * 0.5f, RecordSideH * 0.5f, RecordSideW - 4f, RecordSideH - 4f, Panel, pillSprite);
            fill.type = Image.Type.Sliced;
            return r;
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

            // 横の幅：左下の札と右下の所持メダルの間いっぱい（広げた分も使う）
            RecordBodyW = RecordBodyMinW + Mathf.Max(0f, spread) * 2f;
            recordBody.sizeDelta = new Vector2(RecordBodyW, RecordBodyH);

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
            new Vector2(CanvasW * 0.5f - RecordBodyW * 0.5f + p.x, RecordBodyTop + p.y);

        void UpdateRecordScreen()
        {
            if (recordScreenRoot == null) return;
            recordScreenAnim = Mathf.MoveTowards(recordScreenAnim, IsRecordScreenShown ? 1f : 0f, Time.deltaTime * 4f);
            float e = EaseInOut(recordScreenAnim);

            // いつもの物を、空いている所へ動かす（始まりの演出の間はさわらない）
            if (introRoutine == null && (recordScreenAnim > 0f || recordLayoutMoved))
            {
                ApplyRecordLayout(e);
                recordLayoutMoved = recordScreenAnim > 0f;
            }

            recordScreenGroup.alpha = recordScreenAnim;
            bool visible = recordScreenAnim > 0.001f;
            if (recordScreenRoot.gameObject.activeSelf != visible) recordScreenRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                // 消し終わったら中身も消す（次に出す時に作り直す）
                if (recordScreenOwner == null && recordBody.childCount > 0)
                    for (int i = recordBody.childCount - 1; i >= 0; i--) Destroy(recordBody.GetChild(i).gameObject);
                return;
            }

            // 枠：画面の左右の端まで（広げた分も）。出る時に少し大きい所から縮む
            float fw = CanvasW - 48f + Mathf.Max(0f, spread) * 2f;
            float fh = RecordFrameBottom - RecordFrameTop;
            float s = Mathf.Lerp(1.03f, 1f, e);
            recordFrame.sizeDelta = new Vector2(fw * s, fh * s);
            recordFace.sizeDelta = new Vector2(fw * s - 6f, fh * s - 6f);

            // 右上：効果の残り時間
            var fx = EffectSystem;
            if (fx != null && fx.IsActive)
            {
                float dur = fx.CurrentDuration;
                float rem = Mathf.Max(0f, fx.Remaining);
                int sec = Mathf.CeilToInt(rem);
                recordTimeText.text = dur > 0f ? $"{sec / 60}:{sec % 60:00}" : "--";
                recordTimeFill.fillAmount = dur > 0f ? Mathf.Clamp01(rem / dur) : 1f;
            }
        }

        /// <summary>
        /// いつもの物の置き場所を、レコード用の画面の置き方に寄せる（e：0 がいつも、1 がレコード用の画面）。
        /// </summary>
        void ApplyRecordLayout(float e)
        {
            // 今のポイントとバー → 左上の枠の中へ小さく
            // （まん中の中身の見た目の中心は、Score の四角の中心から少し左上にずれているので、その分を足す）
            var scoreRt = (RectTransform)scoreGroup.transform;
            var scoreHome = new Vector2(CanvasW * 0.5f, ScoreTop + ScoreH * 0.5f);
            const float LookCX = 672f, LookCY = 328f; // 「現在」の札から目標の数字までの真ん中
            float k = RecordScoreScale;
            var scoreTarget = new Vector2(RecordSideX - spread + (scoreHome.x - LookCX) * k, RecordScoreY + (scoreHome.y - LookCY) * k);
            var sp = Vector2.Lerp(scoreHome, scoreTarget, e);
            scoreRt.anchoredPosition = new Vector2(sp.x, -sp.y);
            float ss = Mathf.Lerp(1f, k, e);
            scoreRt.localScale = new Vector3(ss, ss, 1f);

            // NOW PLAYING → 下のまん中
            if (npRoot != null)
                npRoot.anchoredPosition = new Vector2(CanvasW * 0.5f, -Mathf.Lerp(562f, RecordNowPlayingY, e));

            // 下のレコードとイコライザー → しまう
            recordRoot.anchoredPosition = new Vector2(RecordX, -(RecordCanvasY + RecordHiddenOffset * e));
            eqGroup.alpha = 1f - e;
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
