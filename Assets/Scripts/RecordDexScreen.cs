using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// レコード図鑑の画面（RecordDex シーンに置く）。画面はすべてコードで作る。
    ///
    /// ・左：40枚のジャケットの一覧（5列）。上のタブで系統（定番・盤面・モニター・メダル・物理）をしぼれる
    /// ・右：えらんだレコードのくわしい情報。大きなジャケットと、横から出てくるレコード盤、ID・系統・レアリティ・ゲージ消費、
    ///   効果・盤面とモニターに見えるもの（CSV の文）、値段、曲の長さ、入手の方法、流した回数・発動した回数
    /// ・試聴：Space で曲を流す（もう一度で止める）。流している間はレコード盤が回り、イコライザーが動く
    /// ・発見：ゲームで1回でも流したレコードが「発見」。まだのレコードは暗く「未発見」の札が付く
    ///   （Reveal Undiscovered がオンなら、未発見でも中身を見られる。F5 で切りかえ）
    ///
    /// 操作：← → ↑ ↓（WASD）えらぶ ／ Tab・E 次の系統、Q 前の系統 ／ Space 試聴 ／ クリックでえらぶ・タブをえらぶ ／
    ///       ホイールで一覧を動かす ／ P・Esc・Backspace もどる ／ F5 未発見を見せる・隠す ／ Shift+F9 発見の記録を消す
    ///
    /// ジャケットと曲は「ファイル名 = レコードのキー」で探す（Inspector の Jackets・Clips → Resources/RecordJacket・RecordAudio の順）。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecordDexScreen : MonoBehaviour
    {
        [Header("ジャケットと曲（ファイル名 = レコードのキー）")]
        [Tooltip("ジャケット画像。Project ウィンドウから複数まとめてドラッグできる")]
        public Texture2D[] jackets = new Texture2D[0];
        [Tooltip("曲。Project ウィンドウから複数まとめてドラッグできる")]
        public AudioClip[] clips = new AudioClip[0];

        [Header("見せ方")]
        [Tooltip("未発見のレコードも、中身（名前・効果）を見せる。F5 で切りかえ")]
        public bool revealUndiscovered = true;
        [Tooltip("試聴の音量")]
        [Range(0f, 1f)] public float previewVolume = 0.7f;
        [Tooltip("一覧の列の数")]
        [Range(3, 8)] public int columns = 5;

        [Header("文字（空ならモニターと同じフォント → 内蔵のフォント）")]
        public Font bodyFont;
        public Font numberFont;

        [Header("もどる先")]
        [Tooltip("P で飛んで来たのではない時（このシーンから始めた時）にもどるシーン。空なら Build Settings の最初のシーン")]
        public string fallbackReturnScene = "";

        // ---- 色（モニターと同じ ZZZ 風） ----
        static readonly Color Bg = new Color32(0x0d, 0x0e, 0x14, 0xff);
        static readonly Color Panel = new Color32(0x1b, 0x1c, 0x24, 0xff);
        static readonly Color PanelDark = new Color32(0x14, 0x15, 0x1c, 0xff);
        static readonly Color Line = new Color32(0x34, 0x35, 0x3f, 0xff);
        static readonly Color Accent = new Color32(0xff, 0x7a, 0x1a, 0xff);
        static readonly Color Fg = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Dark = new Color32(0x16, 0x16, 0x1c, 0xff);
        static readonly Color Neon = new Color32(0xff, 0x4f, 0xa3, 0xff);
        static readonly Color Cyan = new Color32(0x3f, 0xe0, 0xff, 0xff);

        static readonly string[] Categories = { "すべて", "定番", "盤面", "モニター", "メダル", "物理" };

        const float W = 1920f, H = 1080f;
        const float GridLeft = 60f, GridTop = 210f, GridW = 1000f, GridH = 790f;
        const float CardSize = 176f, CardGap = 22f, CardLabelH = 44f;
        const float DetailLeft = 1100f, DetailTop = 150f, DetailW = 760f, DetailH = 850f;

        // ---- データ ----
        List<RecordData> records = new List<RecordData>();
        readonly List<int> filtered = new List<int>();   // 今のタブで見えるレコード（records の番号）
        readonly Dictionary<string, Texture2D> jacketByKey = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, AudioClip> clipByKey = new Dictionary<string, AudioClip>();
        int category, selected;          // selected：filtered の中の番号
        int shownRecord = -1;            // 右に出しているレコード（records の番号）

        // ---- 画面 ----
        Font font, numFont;
        RectTransform canvasRoot, gridContent;
        Sprite whiteSprite, circleSprite, slantSprite;
        Texture2D vinylTexture, scanTexture;
        readonly List<RectTransform> tabs = new List<RectTransform>();
        readonly List<Image> tabImages = new List<Image>();
        readonly List<Text> tabTexts = new List<Text>();
        readonly List<Card> cards = new List<Card>();
        readonly List<RectTransform> stripes = new List<RectTransform>();
        float scrollY, scrollTarget;
        Text progressText, footerText, revealText;
        Image progressFill;

        // 右のくわしい情報
        RectTransform detailRoot, jacketGroup, disc, discLabel;
        RawImage bigJacket, discLabelImage;
        Image categoryStrip, jacketFrame;
        Text idText, categoryText, rarityText, gaugeText, nameText, keyText, effectText, visualText, statsText, playsText, previewText, lockText;
        Image categoryTag;
        readonly List<Image> eqBars = new List<Image>();
        float detailAnim, discOut, discAngle;

        // ---- 音 ----
        AudioSource audioSource;
        readonly float[] spectrum = new float[256];
        int playingRecord = -1;

        class Card
        {
            public int record;
            public RectTransform root;
            public Image frame, strip;
            public RawImage jacket;
            public Text label, id;
            public GameObject lockMark;
            public float scale = 1f;
        }

        // ================================================================
        // 始まり
        // ================================================================

        void Start()
        {
            records = RecordDatabase.LoadFromResources();
            records.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            foreach (var t in jackets) if (t != null) jacketByKey[t.name] = t;
            foreach (var c in clips) if (c != null) clipByKey[c.name] = c;

            font = bodyFont != null ? bodyFont : Resources.Load<Font>(MonitorDisplay.FontFolder + "/Body");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            numFont = numberFont != null ? numberFont : Resources.Load<Font>(MonitorDisplay.FontFolder + "/Display");
            if (numFont == null) numFont = font;

            EnsureCamera();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;

            MakeSprites();
            BuildCanvas();
            ApplyFilter(0, true);
        }

        void EnsureCamera()
        {
            if (Camera.main == null)
            {
                var camGo = new GameObject("DexCamera") { tag = "MainCamera" };
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Bg;
                cam.cullingMask = 0;
            }
            if (FindFirstObjectByType<AudioListener>() == null) Camera.main.gameObject.AddComponent<AudioListener>();
        }

        // ================================================================
        // データ
        // ================================================================

        Texture2D JacketOf(RecordData r)
        {
            if (r == null) return null;
            if (jacketByKey.TryGetValue(r.key, out var t)) return t;
            t = Resources.Load<Texture2D>(RecordArtwork.ResourceFolder + "/" + r.key);
            jacketByKey[r.key] = t;
            return t;
        }

        AudioClip ClipOf(RecordData r)
        {
            if (r == null) return null;
            if (clipByKey.TryGetValue(r.key, out var c)) return c;
            c = Resources.Load<AudioClip>("RecordAudio/" + r.key);
            clipByKey[r.key] = c;
            return c;
        }

        bool Discovered(RecordData r) => RecordDexProgress.IsDiscovered(r.key);
        bool Visible(RecordData r) => revealUndiscovered || Discovered(r);

        static string Stars(string rarity) => rarity switch
        {
            "コモン" => "★",
            "レア" => "★★",
            "エピック" => "★★★",
            "レジェンド" => "★★★★",
            _ => "",
        };

        static Color RarityColor(string rarity) => rarity switch
        {
            "レア" => new Color32(0x6c, 0xb8, 0xff, 0xff),
            "エピック" => new Color32(0xc0, 0x6c, 0xff, 0xff),
            "レジェンド" => new Color32(0xff, 0xc8, 0x3a, 0xff),
            _ => new Color32(0xc8, 0xc8, 0xd0, 0xff),
        };

        // ================================================================
        // 画面を作る
        // ================================================================

        void MakeSprites()
        {
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

            const int n = 128;
            var circle = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var vinyl = new Texture2D(256, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    circle.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * n * 0.5f)));
                }
            circle.Apply();
            circleSprite = Sprite.Create(circle, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));

            // レコード盤：黒い盤に、細い溝と光の筋
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f - 128f, y + 0.5f - 128f);
                    float d = p.magnitude / 128f;
                    float a = Mathf.Clamp01((1f - d) * 128f);
                    float groove = 0.07f + 0.03f * Mathf.Sin(d * 220f);
                    float angle = Mathf.Atan2(p.y, p.x);
                    float shine = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2f + 0.6f)), 12f) * 0.18f * Mathf.Clamp01((d - 0.35f) * 4f);
                    float v = groove + shine;
                    if (d < 0.36f) v = 0.05f;
                    vinyl.SetPixel(x, y, new Color(v, v, v * 1.1f, a));
                }
            vinyl.Apply();
            vinylTexture = vinyl;

            // 斜めの札（平行四辺形）
            var slant = new Texture2D(64, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 64; x++)
                {
                    float left = y * 0.25f, right = 64f - (31 - y) * 0.25f;
                    float a = Mathf.Clamp01(Mathf.Min(x + 0.5f - left, right - (x + 0.5f)));
                    slant.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            slant.Apply();
            slantSprite = Sprite.Create(slant, new Rect(0, 0, 64, 32), new Vector2(0.5f, 0.5f));

            // 走査線（横じま）
            scanTexture = new Texture2D(1, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            scanTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.35f));
            scanTexture.SetPixel(0, 1, new Color(0f, 0f, 0f, 0f));
            scanTexture.SetPixel(0, 2, new Color(0f, 0f, 0f, 0f));
            scanTexture.SetPixel(0, 3, new Color(0f, 0f, 0f, 0f));
            scanTexture.Apply();
        }

        void BuildCanvas()
        {
            var canvasGo = new GameObject("DexCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(W, H);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRoot = NewRect("Root", canvasGo.transform, W * 0.5f, H * 0.5f, W, H);
            // 画面の中心に置く（画面の比率が違っても、まん中に 1920×1080 で出す）
            canvasRoot.anchorMin = canvasRoot.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRoot.anchoredPosition = Vector2.zero;

            BuildBackground();
            BuildHeader();
            BuildTabs();
            BuildGrid();
            BuildDetail();

            var scan = new GameObject("Scanlines").AddComponent<RawImage>();
            scan.transform.SetParent(canvasRoot, false);
            scan.texture = scanTexture;
            scan.uvRect = new Rect(0f, 0f, 1f, H / 4f);
            scan.raycastTarget = false;
            Stretch(scan.rectTransform);

            footerText = Lbl(canvasRoot, "Footer", 60f, 1050f, 1800f, 36f,
                "← → ↑ ↓ えらぶ　　Tab / Q・E 系統　　Space 試聴　　クリック えらぶ　　P / Esc もどる　　F5 未発見を見せる・隠す",
                18, Mute, TextAnchor.MiddleLeft);
        }

        void BuildBackground()
        {
            Img(canvasRoot, "Bg", W * 0.5f, H * 0.5f, W, H, Bg);
            for (int i = 0; i < 6; i++)
            {
                Color c = i % 2 == 0 ? Accent : Neon;
                var s = Img(canvasRoot, $"Stripe{i}", 200f + i * 340f, H * 0.5f, 26f + (i % 3) * 18f, H * 1.8f, new Color(c.r, c.g, c.b, 0.05f));
                s.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -28f);
                stripes.Add(s.rectTransform);
            }
            var mark = Lbl(canvasRoot, "Watermark", W * 0.5f, H * 0.55f, W * 1.2f, 400f, "RECORD DEX", 300, new Color(1f, 1f, 1f, 0.025f), TextAnchor.MiddleCenter, true);
            mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -8f);
        }

        void BuildHeader()
        {
            Img(canvasRoot, "HeaderLine", W * 0.5f, 118f, W - 120f, 2f, Line);
            Img(canvasRoot, "AccentBar", 66f, 64f, 12f, 76f, Accent);
            Lbl(canvasRoot, "Title", 90f, 52f, 800f, 80f, "RECORD DEX", 64, Fg, TextAnchor.MiddleLeft, true);
            Lbl(canvasRoot, "Sub", 92f, 96f, 900f, 30f, "レコード図鑑 ─ ネオン通りに戻ってきた音楽", 22, Mute, TextAnchor.MiddleLeft);
            Tag(canvasRoot, "Badge", 560f, 52f, 150f, 34f, "198X", 18, Neon, Dark);
            progressText = Lbl(canvasRoot, "Progress", W - 60f, 46f, 600f, 50f, "", 34, Fg, TextAnchor.MiddleRight, true);
            Img(canvasRoot, "ProgressBg", W - 60f - 180f, 88f, 360f, 8f, Line);
            progressFill = Img(canvasRoot, "ProgressFill", W - 60f - 180f, 88f, 360f, 8f, Accent);
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            revealText = Lbl(canvasRoot, "Reveal", W - 440f, 46f, 360f, 30f, "", 16, Mute, TextAnchor.MiddleRight);
        }

        void BuildTabs()
        {
            tabs.Clear();
            tabImages.Clear();
            tabTexts.Clear();
            for (int i = 0; i < Categories.Length; i++)
            {
                float cx = GridLeft + 80f + i * 168f;
                var img = Img(canvasRoot, $"Tab{i}", cx, 168f, 158f, 46f, Panel);
                img.sprite = slantSprite;
                tabs.Add(img.rectTransform);
                tabImages.Add(img);
                tabTexts.Add(Lbl(canvasRoot, $"TabText{i}", cx, 168f, 158f, 46f, Categories[i], 20, Mute));
            }
        }

        void BuildGrid()
        {
            var view = NewRect("GridView", canvasRoot, GridLeft + GridW * 0.5f, GridTop + GridH * 0.5f, GridW, GridH);
            view.gameObject.AddComponent<RectMask2D>();
            gridContent = NewRect("Content", view, GridW * 0.5f, GridH * 0.5f, GridW, GridH);
            gridContent.pivot = new Vector2(0.5f, 1f);
            gridContent.anchoredPosition = new Vector2(GridW * 0.5f, 0f);

            cards.Clear();
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                var card = new Card { record = i };
                card.root = NewRect($"Card_{r.key}", gridContent, 0f, 0f, CardSize, CardSize + CardLabelH);
                card.frame = Img(card.root, "Frame", CardSize * 0.5f, CardSize * 0.5f, CardSize + 6f, CardSize + 6f, Line);
                var jacketImg = new GameObject("Jacket").AddComponent<RawImage>();
                jacketImg.transform.SetParent(card.root, false);
                Place(jacketImg.rectTransform, CardSize * 0.5f, CardSize * 0.5f, CardSize - 4f, CardSize - 4f);
                jacketImg.texture = JacketOf(r);
                jacketImg.color = jacketImg.texture != null ? Color.white : RecordArtwork.CategoryColor(r.category);
                jacketImg.raycastTarget = false;
                card.jacket = jacketImg;
                card.strip = Img(card.root, "Strip", 4f, CardSize * 0.5f, 8f, CardSize - 4f, RecordArtwork.CategoryColor(r.category));
                var idTag = Tag(card.root, "Id", 34f, 16f, 66f, 22f, r.id, 13, Dark, Fg);
                card.id = idTag;
                card.label = Lbl(card.root, "Name", CardSize * 0.5f, CardSize + CardLabelH * 0.5f + 2f, CardSize + 10f, CardLabelH, r.name, 20, Fg);
                var lockGo = NewRect("Lock", card.root, CardSize * 0.5f, CardSize * 0.5f, CardSize, CardSize);
                Img(lockGo, "Shade", CardSize * 0.5f, CardSize * 0.5f, CardSize - 4f, CardSize - 4f, new Color(0f, 0f, 0f, 0.62f));
                Tag(lockGo, "Text", CardSize * 0.5f, CardSize * 0.5f, 120f, 30f, "未発見", 16, Line, Mute);
                card.lockMark = lockGo.gameObject;
                cards.Add(card);
            }
        }

        void BuildDetail()
        {
            detailRoot = NewRect("Detail", canvasRoot, DetailLeft + DetailW * 0.5f, DetailTop + DetailH * 0.5f, DetailW, DetailH);
            Img(detailRoot, "Border", DetailW * 0.5f, DetailH * 0.5f, DetailW, DetailH, Line);
            Img(detailRoot, "Face", DetailW * 0.5f, DetailH * 0.5f, DetailW - 4f, DetailH - 4f, Panel);
            categoryStrip = Img(detailRoot, "CategoryStrip", DetailW * 0.5f, 4f, DetailW - 4f, 6f, Accent);

            // ジャケットとレコード盤
            const float js = 340f;
            jacketGroup = NewRect("JacketGroup", detailRoot, 30f + js * 0.5f, 40f + js * 0.5f, js, js);
            disc = NewRect("Disc", jacketGroup, js * 0.5f, js * 0.5f, js - 16f, js - 16f);
            var discImg = disc.gameObject.AddComponent<RawImage>();
            discImg.texture = vinylTexture;
            discImg.raycastTarget = false;
            var labelMask = Img(disc, "Label", (js - 16f) * 0.5f, (js - 16f) * 0.5f, 118f, 118f, Color.white);
            labelMask.sprite = circleSprite;
            labelMask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            discLabel = labelMask.rectTransform;
            discLabelImage = new GameObject("LabelArt").AddComponent<RawImage>();
            discLabelImage.transform.SetParent(discLabel, false);
            Stretch(discLabelImage.rectTransform);
            discLabelImage.raycastTarget = false;
            Img(disc, "Hole", (js - 16f) * 0.5f, (js - 16f) * 0.5f, 12f, 12f, Dark).sprite = circleSprite;
            jacketFrame = Img(jacketGroup, "Frame", js * 0.5f, js * 0.5f, js + 8f, js + 8f, Line);
            bigJacket = new GameObject("Jacket").AddComponent<RawImage>();
            bigJacket.transform.SetParent(jacketGroup, false);
            Place(bigJacket.rectTransform, js * 0.5f, js * 0.5f, js, js);
            bigJacket.raycastTarget = false;
            lockText = Lbl(jacketGroup, "Lock", js * 0.5f, js * 0.5f, js, 60f, "", 30, Mute);

            // 右上の札
            float rx = 560f;
            idText = Lbl(detailRoot, "Id", rx, 60f, 300f, 50f, "", 40, Fg, TextAnchor.MiddleLeft, true);
            categoryTag = Img(detailRoot, "CategoryTag", rx + 80f, 120f, 160f, 36f, Accent);
            categoryTag.sprite = slantSprite;
            categoryText = Lbl(detailRoot, "Category", rx + 80f, 120f, 160f, 36f, "", 18, Dark);
            rarityText = Lbl(detailRoot, "Rarity", rx, 170f, 300f, 36f, "", 26, Fg, TextAnchor.MiddleLeft);
            Lbl(detailRoot, "GaugeLabel", rx, 230f, 300f, 26f, "ゲージ消費", 16, Mute, TextAnchor.MiddleLeft);
            gaugeText = Lbl(detailRoot, "Gauge", rx, 272f, 300f, 60f, "", 50, Accent, TextAnchor.MiddleLeft, true);

            // 名前と文
            nameText = Lbl(detailRoot, "Name", 30f, 420f, DetailW - 60f, 60f, "", 46, Fg, TextAnchor.MiddleLeft);
            keyText = Lbl(detailRoot, "Key", 32f, 462f, DetailW - 60f, 28f, "", 18, Mute, TextAnchor.MiddleLeft, true);
            Img(detailRoot, "Divider", DetailW * 0.5f, 486f, DetailW - 60f, 2f, Line);
            Tag(detailRoot, "EffectTag", 80f, 514f, 100f, 26f, "効果", 15, Accent, Dark);
            effectText = Lbl(detailRoot, "Effect", 30f, 576f, DetailW - 60f, 96f, "", 19, Fg, TextAnchor.UpperLeft);
            effectText.horizontalOverflow = HorizontalWrapMode.Wrap;
            effectText.verticalOverflow = VerticalWrapMode.Truncate;
            effectText.lineSpacing = 1.1f;
            Tag(detailRoot, "VisualTag", 110f, 642f, 160f, 26f, "盤面・モニター", 15, Cyan, Dark);
            visualText = Lbl(detailRoot, "Visual", 30f, 700f, DetailW - 60f, 86f, "", 17, Mute, TextAnchor.UpperLeft);
            visualText.horizontalOverflow = HorizontalWrapMode.Wrap;
            visualText.verticalOverflow = VerticalWrapMode.Truncate;
            statsText = Lbl(detailRoot, "Stats", 30f, 762f, DetailW - 60f, 30f, "", 18, Fg, TextAnchor.MiddleLeft);
            playsText = Lbl(detailRoot, "Plays", 30f, 792f, DetailW - 60f, 30f, "", 18, Mute, TextAnchor.MiddleLeft);

            // 試聴（下の段）
            Img(detailRoot, "PreviewBar", DetailW * 0.5f, 828f, DetailW - 4f, 40f, PanelDark);
            previewText = Lbl(detailRoot, "Preview", 30f, 828f, 360f, 36f, "", 18, Fg, TextAnchor.MiddleLeft, true);
            eqBars.Clear();
            for (int i = 0; i < 28; i++)
            {
                var bar = Img(detailRoot, $"Eq{i}", 400f + i * 12f, 844f, 8f, 4f, Accent);
                bar.rectTransform.pivot = new Vector2(0.5f, 0f);
                bar.rectTransform.anchoredPosition = new Vector2(400f + i * 12f, -844f);
                eqBars.Add(bar);
            }
        }

        // ================================================================
        // しぼりこみ・えらぶ
        // ================================================================

        void ApplyFilter(int newCategory, bool first = false)
        {
            category = (newCategory % Categories.Length + Categories.Length) % Categories.Length;
            int keepRecord = !first && selected >= 0 && selected < filtered.Count ? filtered[selected] : -1;
            filtered.Clear();
            for (int i = 0; i < records.Count; i++)
                if (category == 0 || records[i].category == Categories[category]) filtered.Add(i);
            selected = Mathf.Max(0, filtered.IndexOf(keepRecord));

            // カードの並びを作り直す
            int col = Mathf.Max(1, columns);
            float startX = (GridW - (col * CardSize + (col - 1) * CardGap)) * 0.5f;
            for (int i = 0; i < cards.Count; i++) cards[i].root.gameObject.SetActive(false);
            for (int k = 0; k < filtered.Count; k++)
            {
                var card = cards[filtered[k]];
                card.root.gameObject.SetActive(true);
                int cx = k % col, cy = k / col;
                Place(card.root, startX + cx * (CardSize + CardGap) + CardSize * 0.5f, 10f + cy * (CardSize + CardLabelH + CardGap) + (CardSize + CardLabelH) * 0.5f, CardSize, CardSize + CardLabelH);
            }
            int rows = Mathf.CeilToInt(filtered.Count / (float)col);
            gridContent.sizeDelta = new Vector2(GridW, Mathf.Max(GridH, 20f + rows * (CardSize + CardLabelH + CardGap)));
            scrollTarget = 0f;
            Select(selected, true);
        }

        void Select(int index, bool force = false)
        {
            if (filtered.Count == 0) return;
            index = Mathf.Clamp(index, 0, filtered.Count - 1);
            if (index == selected && !force && shownRecord == filtered[index]) return;
            selected = index;
            ShowDetail(filtered[index]);
            KeepSelectedVisible();
        }

        void KeepSelectedVisible()
        {
            int col = Mathf.Max(1, columns);
            int row = selected / col;
            float top = 10f + row * (CardSize + CardLabelH + CardGap);
            float bottom = top + CardSize + CardLabelH;
            if (top < scrollTarget) scrollTarget = top - 10f;
            if (bottom > scrollTarget + GridH) scrollTarget = bottom - GridH + 10f;
            ClampScroll();
        }

        void ClampScroll()
        {
            float max = Mathf.Max(0f, gridContent.sizeDelta.y - GridH);
            scrollTarget = Mathf.Clamp(scrollTarget, 0f, max);
        }

        void ShowDetail(int recordIndex)
        {
            if (recordIndex == shownRecord) return;
            shownRecord = recordIndex;
            detailAnim = 0f;
            StopPreview();
            RefreshDetail();
        }

        void RefreshDetail()
        {
            if (shownRecord < 0 || shownRecord >= records.Count) return;
            var r = records[shownRecord];
            bool visible = Visible(r);
            bool found = Discovered(r);
            Color cat = RecordArtwork.CategoryColor(r.category);
            var tex = JacketOf(r);

            bigJacket.texture = tex;
            bigJacket.color = tex == null ? cat : visible ? Color.white : new Color(0.12f, 0.12f, 0.15f, 1f);
            discLabelImage.texture = tex;
            discLabelImage.color = tex != null ? Color.white : cat;
            lockText.text = visible ? "" : "未発見";
            categoryStrip.color = cat;
            categoryTag.color = cat;
            jacketFrame.color = found ? cat : Line;

            idText.text = r.id;
            categoryText.text = r.category;
            rarityText.text = $"{Stars(r.rarity)} {r.rarity}";
            rarityText.color = RarityColor(r.rarity);
            gaugeText.text = $"{r.gaugeCost:0}";

            nameText.text = visible ? r.name : "？？？";
            keyText.text = visible ? r.key : "-----";
            effectText.text = visible ? r.effectText : "ゲームで流すと、効果がわかる";
            visualText.text = visible ? Field(r, "visual") : "";
            var clip = ClipOf(r);
            string len = clip != null ? $"{Mathf.FloorToInt(clip.length / 60f)}:{Mathf.FloorToInt(clip.length % 60f):00}" : "なし";
            string get = r.IsStarter ? "初期" : string.IsNullOrEmpty(r.unlockCondition) || r.unlockCondition == "—" ? r.obtainMethod : $"解禁：{r.unlockCondition}";
            statsText.text = $"購入 {r.buyPrice} ／ 売却 {r.sellPrice} ／ 曲 {len} ／ {get}";
            playsText.text = found
                ? $"流した {RecordDexProgress.Heard(r.key)}回 ／ 発動した {RecordDexProgress.Activated(r.key)}回"
                : "まだゲームで流していない";
        }

        /// <summary>RecordData に無い列（盤面・モニターに見えるもの）を CSV から読む</summary>
        string Field(RecordData r, string which)
        {
            if (visualByKey == null) LoadVisuals();
            return visualByKey.TryGetValue(r.key, out var s) ? s : "";
        }

        Dictionary<string, string> visualByKey;

        void LoadVisuals()
        {
            visualByKey = new Dictionary<string, string>();
            var csv = Resources.Load<TextAsset>("RecordData");
            if (csv == null) return;
            var rows = RecordDatabase.ParseCsv(csv.text);
            if (rows.Count == 0) return;
            int keyCol = -1, visCol = -1;
            for (int i = 0; i < rows[0].Count; i++)
            {
                string h = rows[0][i].Replace("\n", "").Replace("\r", "");
                if (h.StartsWith("キー")) keyCol = i;
                if (h.StartsWith("盤面・モニター")) visCol = i;
            }
            if (keyCol < 0 || visCol < 0) return;
            for (int i = 1; i < rows.Count; i++)
                if (rows[i].Count > Mathf.Max(keyCol, visCol)) visualByKey[rows[i][keyCol].Trim()] = rows[i][visCol].Trim();
        }

        // ================================================================
        // 試聴
        // ================================================================

        void TogglePreview()
        {
            if (playingRecord == shownRecord && audioSource.isPlaying)
            {
                StopPreview();
                return;
            }
            var r = records[shownRecord];
            var clip = ClipOf(r);
            if (clip == null || !Visible(r)) return;
            audioSource.clip = clip;
            audioSource.volume = previewVolume;
            audioSource.time = 0f;
            audioSource.Play();
            playingRecord = shownRecord;
        }

        void StopPreview()
        {
            if (audioSource != null) audioSource.Stop();
            playingRecord = -1;
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (records.Count == 0) return;
            HandleInput();
            Animate();
        }

        void HandleInput()
        {
            int col = Mathf.Max(1, columns);
            if (DexKeys.Pressed(DexKeys.Key.Left)) Select(selected - 1);
            if (DexKeys.Pressed(DexKeys.Key.Right)) Select(selected + 1);
            if (DexKeys.Pressed(DexKeys.Key.Up)) Select(selected - col);
            if (DexKeys.Pressed(DexKeys.Key.Down)) Select(selected + col);
            if (DexKeys.Pressed(DexKeys.Key.Tab)) ApplyFilter(category + (DexKeys.ShiftHeld() ? -1 : 1));
            if (DexKeys.Pressed(DexKeys.Key.E)) ApplyFilter(category + 1);
            if (DexKeys.Pressed(DexKeys.Key.Q)) ApplyFilter(category - 1);
            if (DexKeys.Pressed(DexKeys.Key.Space) || DexKeys.Pressed(DexKeys.Key.Enter)) TogglePreview();
            if (DexKeys.Pressed(DexKeys.Key.F5))
            {
                revealUndiscovered = !revealUndiscovered;
                RefreshDetail();
            }
            if (DexKeys.Pressed(DexKeys.Key.F9) && DexKeys.ShiftHeld())
            {
                RecordDexProgress.Clear(records);
                RefreshDetail();
                Debug.Log("RecordDex: 発見の記録を消しました。");
            }
            if (DexKeys.Pressed(DexKeys.Key.P) || DexKeys.Pressed(DexKeys.Key.Escape) || DexKeys.Pressed(DexKeys.Key.Backspace))
            {
                GoBack();
                return;
            }

            float wheel = DexKeys.Scroll();
            if (Mathf.Abs(wheel) > 0.01f)
            {
                scrollTarget -= wheel * 120f;
                ClampScroll();
            }

            if (InputCompat.MouseDown())
            {
                Vector2 mouse = InputCompat.MousePosition();
                for (int i = 0; i < tabs.Count; i++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(tabs[i], mouse, null)) { ApplyFilter(i); return; }
                for (int k = 0; k < filtered.Count; k++)
                {
                    var card = cards[filtered[k]];
                    if (!RectTransformUtility.RectangleContainsScreenPoint(card.root, mouse, null)) continue;
                    if (k == selected) TogglePreview();   // えらんでいるカードをもう一度クリックで試聴
                    else Select(k);
                    return;
                }
                if (RectTransformUtility.RectangleContainsScreenPoint(jacketGroup, mouse, null)) TogglePreview();
            }
        }

        void GoBack()
        {
            StopPreview();
            string target = RecordDexHotkey.ReturnSceneName;
            if (string.IsNullOrEmpty(target) || target == RecordDexHotkey.DexSceneName) target = fallbackReturnScene;
            if (!string.IsNullOrEmpty(target) && Application.CanStreamedLevelBeLoaded(target))
            {
                SceneManager.LoadScene(target);
                return;
            }
            if (SceneManager.sceneCountInBuildSettings > 0 && SceneManager.GetActiveScene().buildIndex != 0)
            {
                SceneManager.LoadScene(0);
                return;
            }
            Debug.Log("RecordDex: もどる先のシーンがありません（Inspector の Fallback Return Scene に名前を入れてください）。");
        }

        void Animate()
        {
            float dt = Time.unscaledDeltaTime;
            float t = Time.unscaledTime;

            // 背景の斜めの線がゆっくり流れる
            for (int i = 0; i < stripes.Count; i++)
            {
                float x = Mathf.Repeat(200f + i * 340f + t * 20f, W + 400f) - 200f;
                stripes[i].anchoredPosition = new Vector2(x, -H * 0.5f);
            }

            // 上の数字
            int found = 0;
            foreach (var r in records) if (Discovered(r)) found++;
            progressText.text = $"発見 {found} <size=22>/ {records.Count}</size>";
            progressFill.fillAmount = records.Count > 0 ? found / (float)records.Count : 0f;
            revealText.text = revealUndiscovered ? "F5：未発見も見せている" : "F5：未発見は隠している";

            // タブ
            for (int i = 0; i < tabs.Count; i++)
            {
                bool on = i == category;
                tabImages[i].color = on ? Accent : Panel;
                tabTexts[i].color = on ? Dark : Mute;
                int count = 0;
                foreach (var r in records) if (i == 0 || r.category == Categories[i]) count++;
                tabTexts[i].text = $"{Categories[i]} <size=14>{count}</size>";
                float s = Mathf.Lerp(tabs[i].localScale.x, on ? 1.06f : 1f, 1f - Mathf.Exp(-12f * dt));
                tabs[i].localScale = new Vector3(s, s, 1f);
            }

            // 一覧のスクロール
            scrollY = Mathf.Lerp(scrollY, scrollTarget, 1f - Mathf.Exp(-14f * dt));
            gridContent.anchoredPosition = new Vector2(GridW * 0.5f, scrollY);

            // カード
            Vector2 mouse = InputCompat.MousePosition();
            for (int k = 0; k < filtered.Count; k++)
            {
                var card = cards[filtered[k]];
                var r = records[card.record];
                bool sel = k == selected;
                bool hover = RectTransformUtility.RectangleContainsScreenPoint(card.root, mouse, null);
                bool visible = Visible(r), discovered = Discovered(r);
                float target = sel ? 1.07f : hover ? 1.03f : 1f;
                card.scale = Mathf.Lerp(card.scale, target, 1f - Mathf.Exp(-14f * dt));
                card.root.localScale = new Vector3(card.scale, card.scale, 1f);
                card.frame.color = sel ? Color.Lerp(Accent, Color.white, 0.25f + 0.25f * Mathf.Sin(t * 6f)) : hover ? Mute : Line;
                card.lockMark.SetActive(!discovered);
                card.jacket.color = card.jacket.texture == null ? RecordArtwork.CategoryColor(r.category) : discovered ? Color.white : visible ? new Color(0.55f, 0.55f, 0.6f) : new Color(0.08f, 0.08f, 0.1f);
                card.label.text = visible ? r.name : "？？？";
                card.label.color = sel ? Accent : discovered ? Fg : Mute;
            }

            // 右：切りかえた時に横から入ってくる
            detailAnim = Mathf.MoveTowards(detailAnim, 1f, dt * 4f);
            float e = 1f - Mathf.Pow(1f - detailAnim, 3f);
            var dp = detailRoot.anchoredPosition;
            detailRoot.anchoredPosition = new Vector2(DetailLeft + DetailW * 0.5f + (1f - e) * 60f, dp.y);
            var cg = detailRoot.GetComponent<CanvasGroup>();
            if (cg == null) cg = detailRoot.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = e;

            // レコード盤：試聴中は右に出て回る
            bool playing = playingRecord == shownRecord && audioSource.isPlaying;
            discOut = Mathf.MoveTowards(discOut, playing ? 1f : 0.25f, dt * 2.5f);
            float eo = 1f - Mathf.Pow(1f - discOut, 2f);
            disc.anchoredPosition = new Vector2(170f + eo * 175f, -170f);
            if (playing) discAngle -= dt * 200f;
            disc.localRotation = Quaternion.Euler(0f, 0f, discAngle);

            // イコライザー
            if (playing) audioSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);
            for (int i = 0; i < eqBars.Count; i++)
            {
                float v;
                if (playing)
                {
                    int from = Mathf.FloorToInt(Mathf.Pow(i / (float)eqBars.Count, 2f) * 200f) + 1;
                    int to = Mathf.Max(from + 1, Mathf.FloorToInt(Mathf.Pow((i + 1) / (float)eqBars.Count, 2f) * 200f) + 1);
                    float sum = 0f;
                    for (int s = from; s < to && s < spectrum.Length; s++) sum += spectrum[s];
                    v = Mathf.Clamp01(Mathf.Sqrt(sum) * 4f);
                }
                else v = 0.05f + 0.03f * Mathf.Sin(t * 2f + i * 0.5f);
                var rt = eqBars[i].rectTransform;
                float h = Mathf.Lerp(rt.sizeDelta.y, 4f + v * 30f, 1f - Mathf.Exp(-18f * dt));
                rt.sizeDelta = new Vector2(8f, h);
                eqBars[i].color = Color.Lerp(Accent, Neon, i / (float)eqBars.Count);
            }

            if (shownRecord >= 0 && shownRecord < records.Count)
            {
                var r = records[shownRecord];
                var clip = ClipOf(r);
                if (!Visible(r)) previewText.text = "試聴できません";
                else if (clip == null) previewText.text = "曲がありません";
                else if (playing) previewText.text = $"■ STOP  {Mathf.FloorToInt(audioSource.time / 60f)}:{Mathf.FloorToInt(audioSource.time % 60f):00} / {Mathf.FloorToInt(clip.length / 60f)}:{Mathf.FloorToInt(clip.length % 60f):00}";
                else previewText.text = "SPACE ▶ 試聴";
            }
        }

        // ================================================================
        // 部品を作る道具（座標は親の左上が (0, 0)、(cx, cy) が中心）
        // ================================================================

        static RectTransform NewRect(string name, Transform parent, float cx, float cy, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Place(rt, cx, cy, w, h);
            return rt;
        }

        static void Place(RectTransform rt, float cx, float cy, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(cx, -cy);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        Image Img(Transform parent, string name, float cx, float cy, float w, float h, Color color)
        {
            var rt = NewRect(name, parent, cx, cy, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = whiteSprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>文字。x は左寄せなら左端・右寄せなら右端・中央寄せなら中心</summary>
        Text Lbl(Transform parent, string name, float x, float cy, float w, float h, string text, int size, Color color,
                 TextAnchor align = TextAnchor.MiddleCenter, bool number = false)
        {
            float cx = align == TextAnchor.MiddleLeft || align == TextAnchor.UpperLeft || align == TextAnchor.LowerLeft ? x + w * 0.5f
                     : align == TextAnchor.MiddleRight || align == TextAnchor.UpperRight || align == TextAnchor.LowerRight ? x - w * 0.5f : x;
            var rt = NewRect(name, parent, cx, cy, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = number ? numFont : font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>斜めの札</summary>
        Text Tag(Transform parent, string name, float cx, float cy, float w, float h, string text, int size, Color bg, Color fg)
        {
            var img = Img(parent, name, cx, cy, w, h, bg);
            img.sprite = slantSprite;
            return Lbl(img.rectTransform, "Text", w * 0.5f, h * 0.5f, w, h, text, size, fg);
        }
    }
}
