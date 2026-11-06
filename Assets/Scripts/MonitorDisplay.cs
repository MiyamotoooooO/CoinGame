using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// 筐体のモニター（背板いっぱいの大きな画面）に映す画面。UI を RenderTexture に描き、MonitorScreen に貼る。
    ///
    /// 画面の割り振り（1500×900、左上が (0, 0)）：
    ///  ・上の帯（いつも同じ）：斜めのタグ「AREA 01」、エリア名、右にラウンドとスコア
    ///  ・左のステージ（演出ごとに入れ替わる）：ROUND 番号・ラウンド名・マスコット。
    ///    レコード再生中は NOW PLAYING、天気予報などの演出はステージごと差し替える（MonitorDisplay.Stage.cs）
    ///  ・右のデッキ：回るレコードと、流しているレコードの名前（グルーヴゲージは筐体の GrooveGauge が出す）
    ///  ・数字の帯：所持メダル・投入・落ちた枚数・直近の1枚・エリアのポイント
    ///  ・下の帯（いつも同じ）：ラウンドのチップと、ノルマ（ボス戦はHP）の目盛りバー
    ///  ・色は黒に近いグレー＋白＋差し色1色（オレンジ）。ボスは赤、天気は水色
    ///
    /// エリアの始まりの演出（PlayIntro）：
    ///  1. カメラがモニターに寄る
    ///  2. 上の帯が出る
    ///  3. ステージが左から右へ開く（オレンジの線が走る）
    ///  4. ラウンドのチップと数字の帯が順に出る
    ///  5. レコードが右から入って回り始め、エリア名・ROUND・マスコットが出る
    ///  6. カメラが引いて、いつものプレイ画面に戻る
    ///
    /// 文字のフォント：Resources/MonitorFonts/Display（英字・数字用）と Resources/MonitorFonts/Body（日本語用）を
    /// 置くと使う。無ければ Unity 標準のフォントで出す。
    /// 画面の四隅は筐体の壁に少し隠れるので、大事な物は端から 30 くらい内側に置く。
    /// </summary>
    public partial class MonitorDisplay : MonoBehaviour
    {
        const int UiLayer = 5;               // Unity 標準の「UI」レイヤー。メインカメラには映さない
        const float CanvasW = 1500f;
        const float CanvasH = 900f;          // モニターの画面と同じ 5:3

        // 枠の大きさ
        const float TopH = 84f;              // 上の帯
        const float StageTop = 86f;          // ステージとデッキの上端
        const float StageH = 554f;           // ステージとデッキの高さ（86〜640）
        const float StageW = 1000f;          // ステージの幅（左）
        const float DeckLeft = 1004f;        // デッキ（右）の左端
        const float DeckW = CanvasW - DeckLeft;
        const float InfoTop = 642f;          // 数字の帯の上端
        const float InfoH = 148f;
        const float BottomTop = 792f;        // 下の帯の上端
        const float BottomH = CanvasH - BottomTop;

        // デッキの中身
        const float RecordLocalY = 250f;     // レコードの中心（デッキの上端から）
        const float RecordX = DeckLeft + DeckW * 0.5f;      // レコードの中心（画面の左から）
        const float RecordCanvasY = StageTop + RecordLocalY; // レコードの中心（画面の上から）
        const float RecordSize = 380f;

        // 下の帯
        const float ChipsLeft = 44f;
        const float ChipW = 96f;
        const float ChipH = 42f;
        const float ChipStep = 108f;
        const int QuotaSegments = 25;
        const float QuotaLeft = 760f;
        const float QuotaRight = 1456f;

        static readonly Vector3 RigPosition = new Vector3(0f, -500f, 0f); // メインカメラから見えない場所

        /// <summary>フォントを置く場所（Resources の中）</summary>
        public const string FontFolder = "MonitorFonts";

        // 色（黒に近いグレー＋白＋差し色1色）
        static readonly Color Bg = new Color32(0x0c, 0x0c, 0x0e, 0xff);
        static readonly Color Panel = new Color32(0x15, 0x15, 0x18, 0xff);
        static readonly Color Line = new Color32(0x2b, 0x2b, 0x31, 0xff);
        static readonly Color Mute = new Color32(0x7c, 0x7c, 0x86, 0xff);
        static readonly Color Fg = new Color32(0xf2, 0xf2, 0xee, 0xff);
        static readonly Color Accent = new Color32(0xff, 0x7a, 0x1a, 0xff);  // 差し色（オレンジ）
        static readonly Color Yellow = new Color32(0xff, 0xd2, 0x3f, 0xff);  // ノルマ達成・ゲージ満タン
        static readonly Color Boss = new Color32(0xff, 0x2e, 0x4d, 0xff);    // ボス
        static readonly Color Sky = new Color32(0x38, 0xc8, 0xff, 0xff);     // 天気

        MedalPusherGame game;
        RenderTexture renderTexture;
        Renderer screenRenderer;
        Material screenMaterial;
        Font font;          // 日本語用
        Font displayFont;   // 英字・数字用
        Sprite whiteSprite, circleSprite, ringSprite, recordSprite, stripeSprite, fineStripeSprite, dotSprite;

        // 上の帯
        RectTransform topRoot;
        CanvasGroup topGroup, statsGroup;
        TagParts areaTag;
        Text titleText, subtitleText, statRoundValue, statScoreValue;

        // ステージ
        RectTransform stageMask, stageEdge, normalLayer, mascotSlot, mascotRoot, infoRoot, roundBlock, bossBlock;
        CanvasGroup normalLayerGroup, mascotSlotGroup, mascotGroup, infoGroup;
        Text roundOutlineText, roundNumberText, roundNameText, bossNameText;

        // デッキ（レコードとグルーヴゲージ）
        RectTransform deckRoot, recordRoot, recordDisc;
        CanvasGroup deckInfoGroup;
        Text deckName, deckSub;

        // 数字の帯
        CanvasGroup infoBandGroup;
        readonly List<InfoTile> tiles = new List<InfoTile>();
        InfoTile tileMedals, tileInsert, tileDrops, tileLast, tileArea;

        // 下の帯
        RectTransform chipsRoot;
        CanvasGroup quotaGroup;
        Text quotaLabel, quotaValue;
        Image[] segments;
        readonly List<RoundChip> chips = new List<RoundChip>();

        // クリア・失敗などの帯
        RectTransform statusRoot;
        CanvasGroup statusGroup;
        Image statusBg;
        Text statusEn, statusJa;
        string statusKey;
        float statusAnim;

        class RoundChip
        {
            public RectTransform root;
            public CanvasGroup group;
            public Image outer, inner, underline;
            public Text label;
            public bool boss;
        }

        /// <summary>斜めのタグ（背景と文字）</summary>
        class TagParts
        {
            public RectTransform root;
            public Image bg;
            public Text text;
        }

        /// <summary>数字の帯の1マス（見出し・大きな数字・小さな補足）</summary>
        class InfoTile
        {
            public RectTransform root;
            public CanvasGroup group;
            public Text value, sub;
            public string last;
            public float pulse;
        }

        // 状態
        int areaIndex, roundIndex;
        bool spinning;
        float roundPulse;
        float shownRatio;
        Coroutine introRoutine;
        Action introFinished;
        bool cameraOverridden;
        Vector3 closePos, playPos;
        Quaternion closeRot, playRot;
        float playLens;

        // くっきり表示（ドット化した画面の上に、モニターだけ細かく描いて重ねる）
        Camera overlayCamera;
        LensDistortion overlayLens;
        RenderTexture overlayTexture;
        GameObject overlayCanvas;
        RawImage overlayImage;
        bool overlayActive;
        float particleScanTimer;
        int screenOriginalLayer;

        public bool IsPlayingIntro => introRoutine != null;
        public bool CanPlayIntro => screenRenderer != null && Camera.main != null;

        bool IsBoss
        {
            get
            {
                var rounds = game.areas[areaIndex].rounds;
                return rounds.Length > 0 && rounds[Mathf.Clamp(roundIndex, 0, rounds.Length - 1)].isBoss;
            }
        }

        string AreaTitle => game.areas[areaIndex].title;

        // ================================================================
        // 初期化
        // ================================================================

        public void Init(MedalPusherGame game)
        {
            this.game = game;
            var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            font = Resources.Load<Font>(FontFolder + "/Body");
            if (font == null) font = builtin;
            displayFont = Resources.Load<Font>(FontFolder + "/Display");
            if (displayFont == null) displayFont = font;
            CreateSprites();
            CreateRig();
            AttachToScreen();
            CreateOverlay();
        }

        void OnDestroy()
        {
            if (overlayTexture != null) overlayTexture.Release();
            if (renderTexture != null) renderTexture.Release();
            if (screenMaterial != null) Destroy(screenMaterial);
        }

        /// <summary>UI 用のカメラと Canvas を、メインカメラから見えない場所に作る。</summary>
        void CreateRig()
        {
            int width = Mathf.Max(256, game.monitor.resolutionWidth);
            renderTexture = new RenderTexture(width, Mathf.RoundToInt(width * CanvasH / CanvasW), 24) { name = "MonitorScreen" };
            renderTexture.Create();

            var rig = new GameObject("MonitorRig");
            rig.transform.position = RigPosition;

            var camGo = new GameObject("MonitorCamera");
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = CanvasH * 0.01f * 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << UiLayer;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            cam.targetTexture = renderTexture;

            // メインカメラには UI レイヤーを映さない
            if (Camera.main != null) Camera.main.cullingMask &= ~(1 << UiLayer);

            var canvasGo = new GameObject("MonitorCanvas");
            canvasGo.layer = UiLayer;
            canvasGo.transform.SetParent(rig.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = width / CanvasW; // 文字を映像の解像度で描く
            var root = (RectTransform)canvasGo.transform;
            root.sizeDelta = new Vector2(CanvasW, CanvasH);
            root.localScale = Vector3.one * 0.01f;

            Build(root);
        }

        /// <summary>MonitorScreen に映像を貼る。見つからなければ（仮盤面など）板を作る。</summary>
        void AttachToScreen()
        {
            var go = GameObject.Find(game.monitor.screenObjectName);
            if (go != null) screenRenderer = go.GetComponentInChildren<Renderer>();

            if (screenRenderer == null && game.boardSource == BoardSource.Generate)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "MonitorScreen (仮)";
                DestroyImmediate(quad.GetComponent<Collider>());
                float w = game.layout.boardWidth * 0.8f;
                float h = w * CanvasH / CanvasW;
                quad.transform.position = new Vector3(0f, game.layout.wallHeight + h * 0.6f, game.BackWallFrontZ + 0.5f);
                quad.transform.localScale = new Vector3(w, h, 1f);
                screenRenderer = quad.GetComponent<Renderer>();
            }
            if (screenRenderer == null)
            {
                Debug.LogWarning($"[MedalPusher] モニターの画面「{game.monitor.screenObjectName}」が見つからないため、モニターの演出を出しません。");
                return;
            }

            var shader = Shader.Find("Hidden/MedalPusher/MonitorUnlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            screenMaterial = new Material(shader) { name = "MonitorScreen (実行時)" };
            screenMaterial.mainTexture = renderTexture;
            var m = game.monitor;
            screenMaterial.mainTextureScale = new Vector2(m.flipX ? -1f : 1f, m.flipY ? -1f : 1f);
            screenMaterial.mainTextureOffset = new Vector2(m.flipX ? 1f : 0f, m.flipY ? 1f : 0f);
            screenRenderer.sharedMaterial = screenMaterial;
        }

        // ================================================================
        // くっきり表示
        // ================================================================

        /// <summary>
        /// モニターの画面だけを描くカメラと、それを画面に重ねる Canvas を作る。
        /// ArcadeCamera の画面（Sort Order -100）のすぐ上に重ねるので、モニターだけドット化や減色がかからない。
        /// </summary>
        void CreateOverlay()
        {
            if (screenRenderer == null) return;
            screenOriginalLayer = screenRenderer.gameObject.layer;

            var camGo = new GameObject("MonitorOverlayCamera");
            overlayCamera = camGo.AddComponent<Camera>();
            overlayCamera.clearFlags = CameraClearFlags.SolidColor;
            overlayCamera.backgroundColor = new Color(0f, 0f, 0f, 0f); // モニター以外は透明
            overlayCamera.cullingMask = 1 << game.monitor.overlayLayer;
            overlayCamera.enabled = false;
            overlayLens = camGo.AddComponent<LensDistortion>();      // メインカメラと同じゆがみをかけて位置を合わせる

            overlayCanvas = new GameObject("MonitorOverlayCanvas");
            var canvas = overlayCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var arcade = Camera.main != null ? Camera.main.GetComponent<ArcadeCamera>() : null;
            canvas.sortingOrder = arcade != null ? arcade.sortingOrder + 1 : 0;

            var imageGo = new GameObject("Monitor");
            imageGo.transform.SetParent(overlayCanvas.transform, false);
            overlayImage = imageGo.AddComponent<RawImage>();
            overlayImage.raycastTarget = false;
            // パーティクルも重ねるので、半透明の所が暗くならない重ね方（乗算済みアルファ）にする
            var premultiplied = Shader.Find("Hidden/MedalPusher/OverlayPremultiplied");
            if (premultiplied != null) overlayImage.material = new Material(premultiplied);
            var rt = overlayImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            overlayCanvas.SetActive(false);
        }

        /// <summary>カメラが動いたあと（演出のズームも含む）に、重ねるカメラをメインカメラに合わせる。</summary>
        void LateUpdate()
        {
            if (overlayCamera == null) return;
            var main = Camera.main;
            var arcade = main != null ? main.GetComponent<ArcadeCamera>() : null;
            bool want = game.monitor.sharpMonitor && arcade != null && arcade.isActiveAndEnabled && arcade.effectEnabled;
            int layerMask = 1 << game.monitor.overlayLayer;
            int fxLayerMask = 1 << game.monitor.frontEffectsLayer;
            bool fxInFront = want && game.monitor.effectsInFrontOfMonitor;

            if (want != overlayActive)
            {
                overlayActive = want;
                overlayCanvas.SetActive(want);
                overlayCamera.enabled = want;
                // 重ねる時は、メインカメラ（ドット化される方）にはモニターを映さない
                screenRenderer.gameObject.layer = want ? game.monitor.overlayLayer : screenOriginalLayer;
                if (main != null)
                {
                    if (want) main.cullingMask &= ~layerMask;
                    else main.cullingMask |= layerMask;
                }
            }
            // パーティクル：重ねる時は、メインカメラでは描かずにモニターと一緒に上に描く
            if (main != null)
            {
                if (fxInFront) main.cullingMask &= ~fxLayerMask;
                else main.cullingMask |= fxLayerMask;
            }
            if (!want) return;
            overlayCamera.cullingMask = layerMask | (fxInFront ? fxLayerMask : 0);
            if (fxInFront) MoveParticlesToFrontLayer();

            int h = game.monitor.monitorPixelHeight > 0 ? game.monitor.monitorPixelHeight : Screen.height;
            h = Mathf.Clamp(h, 64, 4096);
            int w = Mathf.Max(1, Mathf.RoundToInt(h * (float)Screen.width / Mathf.Max(1, Screen.height)));
            if (overlayTexture == null || overlayTexture.width != w || overlayTexture.height != h)
            {
                if (overlayTexture != null) overlayTexture.Release();
                overlayTexture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
                {
                    name = "MonitorOverlay",
                    filterMode = FilterMode.Point, // 引き伸ばす時にぼかさない（ドットはドットのまま）
                    wrapMode = TextureWrapMode.Clamp
                };
                overlayTexture.Create();
                overlayCamera.targetTexture = overlayTexture;
                overlayImage.texture = overlayTexture;
            }

            overlayCamera.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            overlayCamera.fieldOfView = main.fieldOfView;
            overlayCamera.nearClipPlane = main.nearClipPlane;
            overlayCamera.farClipPlane = main.farClipPlane;
            var mainLens = main.GetComponent<LensDistortion>();
            overlayLens.strength = mainLens != null ? mainLens.strength : 0f;
        }

        /// <summary>
        /// シーンのパーティクル（レイヤーが Default の物）を、上に重ねるレイヤーに移す。
        /// 重ねるカメラはモニターとパーティクルだけを描くので、モニターより手前のパーティクルはモニターの上に、
        /// 奥のパーティクルはモニターに隠れて見える。新しく出たパーティクルも拾うように、0.25秒ごとに探す。
        /// </summary>
        void MoveParticlesToFrontLayer()
        {
            particleScanTimer -= Time.unscaledDeltaTime;
            if (particleScanTimer > 0f) return;
            particleScanTimer = 0.25f;
            int layer = game.monitor.frontEffectsLayer;
            foreach (var r in FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r.gameObject.layer == 0) r.gameObject.layer = layer;
        }

        // ================================================================
        // 画面の組み立て
        // ================================================================

        void Build(RectTransform root)
        {
            NewImage("Background", root, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH, Bg);

            BuildStage(root);
            BuildDeck(root);
            BuildInfoBand(root);
            BuildTopBar(root);
            BuildBottomBar(root);
            BuildStatus(root);

            BuildEffectLayer(root); // レコードの演出の文字（MonitorDisplay.Effects.cs）
            BuildShopPage(root);    // ショップの画面（MonitorDisplay.Shop.cs）。普段は隠しておく
        }

        // ---- 上の帯 ----

        void BuildTopBar(RectTransform root)
        {
            topRoot = NewRect("Top", root, CanvasW * 0.5f, TopH * 0.5f, CanvasW, TopH);
            topGroup = topRoot.gameObject.AddComponent<CanvasGroup>();
            NewImage("TopBg", topRoot, CanvasW * 0.5f, TopH * 0.5f, CanvasW, TopH, Panel);
            NewImage("TopLine", topRoot, CanvasW * 0.5f, TopH + 1f, CanvasW, 2f, Line);
            NewImage("AccentBlock", topRoot, 34f, TopH * 0.5f, 8f, TopH - 24f, Accent);

            areaTag = Tag("AreaTag", topRoot, 130f, TopH * 0.5f + 2f, 140f, 38f, "AREA 01", 22, Fg, Bg);
            titleText = Label("AreaTitle", topRoot, 225f, 36f, 700f, 40f, "", 32, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            subtitleText = Label("AreaSub", topRoot, 227f, 64f, 700f, 18f, "", 13, Mute, TextAnchor.MiddleLeft, displayFont);

            var stats = NewRect("Stats", topRoot, CanvasW * 0.5f, TopH * 0.5f, CanvasW, TopH);
            statsGroup = stats.gameObject.AddComponent<CanvasGroup>();
            statRoundValue = Stat(stats, 1280f, "ROUND");
            Slash(stats, 1330f);
            statScoreValue = Stat(stats, 1450f, "SCORE");
        }

        /// <summary>右上の数字（小さい見出し＋大きい数字）。right は右端の x。</summary>
        Text Stat(RectTransform parent, float right, string label)
        {
            Label(label + "Label", parent, right, 26f, 160f, 18f, label, 13, Mute, TextAnchor.MiddleRight, displayFont);
            return Label(label + "Value", parent, right, 55f, 200f, 36f, "0", 30, Fg, TextAnchor.MiddleRight, displayFont, FontStyle.Bold);
        }

        void Slash(RectTransform parent, float cx)
        {
            Label("Slash", parent, cx, TopH * 0.5f + 2f, 30f, 60f, "/", 44, Accent, TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
        }

        // ---- ステージ（左） ----

        void BuildStage(RectTransform root)
        {
            var stage = NewRect("Stage", root, StageW * 0.5f, StageTop + StageH * 0.5f, StageW, StageH);

            // 左から右へ開くための枠（はみ出しは切る）
            stageMask = NewRect("StageMask", stage, 0f, 0f, 0f, 0f);
            stageMask.anchorMin = new Vector2(0f, 0f);
            stageMask.anchorMax = new Vector2(0f, 1f);
            stageMask.pivot = new Vector2(0f, 0.5f);
            stageMask.anchoredPosition = Vector2.zero;
            stageMask.sizeDelta = new Vector2(StageW, 0f);
            stageMask.gameObject.AddComponent<RectMask2D>();
            var s = stageMask;

            // 飾り（斜めの線・網点・うすい大きな文字）
            var stripes = NewImage("Stripes", s, 850f, StageH * 0.5f, 300f, StageH, WithAlpha(Fg, 0.035f), fineStripeSprite);
            stripes.type = Image.Type.Tiled;
            var dots = NewImage("Dots", s, 330f, 490f, 660f, 128f, WithAlpha(Fg, 0.06f), dotSprite);
            dots.type = Image.Type.Tiled;
            var back = Label("BackWord", s, 990f, 470f, 900f, 200f, "MEDAL", 170, WithAlpha(Fg, 0.04f),
                TextAnchor.MiddleRight, displayFont, FontStyle.Bold);
            Skew(back, 0.12f);
            NewImage("AccentLine", s, 44f, StageH * 0.5f, 3f, 380f, Accent);
            Label("Caption", s, 74f, 34f, 200f, 20f, Spaced("ON STAGE"), 13, Mute, TextAnchor.MiddleLeft, displayFont);
            NewImage("CaptionLine", s, 440f, 34f, 400f, 1f, Line);

            // いつもの中身（天気予報などの演出の時は、まとめて隠す）
            normalLayer = NewRect("Normal", s, StageW * 0.5f, StageH * 0.5f, StageW, StageH);
            normalLayerGroup = normalLayer.gameObject.AddComponent<CanvasGroup>();
            BuildMascot(normalLayer);
            BuildInfo(normalLayer);
            BuildNowPlaying(normalLayer); // MonitorDisplay.Stage.cs

            BuildWeather(s);              // MonitorDisplay.Stage.cs

            // 開く時に走るオレンジの線
            stageEdge = NewImage("WipeEdge", stage, 0f, StageH * 0.5f, 6f, StageH, Accent).rectTransform;
            stageEdge.gameObject.SetActive(false);
        }

        /// <summary>マスコット。画像が無ければ仮の図形（レコード頭のキャラ）を出す。</summary>
        void BuildMascot(RectTransform parent)
        {
            mascotSlot = NewRect("MascotSlot", parent, 770f, 300f, 260f, 300f);
            mascotSlotGroup = mascotSlot.gameObject.AddComponent<CanvasGroup>(); // NOW PLAYING の時に隠す
            mascotRoot = NewRect("Mascot", mascotSlot, 130f, 150f, 260f, 300f);
            mascotGroup = mascotRoot.gameObject.AddComponent<CanvasGroup>();     // 始まりの演出で出す

            if (game.monitor.mascot != null)
            {
                var raw = NewRect("MascotImage", mascotRoot, 130f, 150f, 260f, 260f).gameObject.AddComponent<RawImage>();
                raw.texture = game.monitor.mascot;
                raw.raycastTarget = false;
                return;
            }
            NewImage("Shadow", mascotRoot, 130f, 284f, 170f, 14f, new Color(0f, 0f, 0f, 0.6f), circleSprite);
            // 台形の体（真ん中の四角＋左右を斜めにした四角）
            NewImage("BodyCenter", mascotRoot, 130f, 228f, 80f, 100f, Line);
            Skew(NewImage("BodyLeft", mascotRoot, 100f, 228f, 60f, 100f, Line), 0.3f);
            Skew(NewImage("BodyRight", mascotRoot, 160f, 228f, 60f, 100f, Line), -0.3f);
            NewImage("Collar", mascotRoot, 130f, 181f, 96f, 6f, Accent);
            // レコードの頭
            NewImage("Head", mascotRoot, 130f, 100f, 170f, 170f, Color.white, recordSprite);
            NewImage("EyeL", mascotRoot, 119f, 98f, 9f, 14f, Fg, circleSprite);
            NewImage("EyeR", mascotRoot, 141f, 98f, 9f, 14f, Fg, circleSprite);
            NewImage("PhoneL", mascotRoot, 40f, 104f, 22f, 58f, Accent);
            NewImage("PhoneR", mascotRoot, 220f, 104f, 22f, 58f, Accent);
            Label("MascotLabel", mascotRoot, 130f, 298f, 260f, 16f, "MASCOT（仮）", 12, Mute, TextAnchor.MiddleCenter, font);
        }

        /// <summary>左の ROUND 番号とラウンド名（ボス戦は BOSS に切り替わる）。</summary>
        void BuildInfo(RectTransform parent)
        {
            infoRoot = NewRect("Info", parent, StageW * 0.5f, StageH * 0.5f, StageW, StageH);
            infoGroup = infoRoot.gameObject.AddComponent<CanvasGroup>();

            // ---- いつものラウンド ----
            roundBlock = NewRect("RoundBlock", infoRoot, 300f, StageH * 0.5f, 540f, StageH); // 左端は x=30
            roundOutlineText = Label("RoundOutline", roundBlock, 44f, 165f, 500f, 140f, "ROUND", 120, Bg,
                TextAnchor.MiddleLeft, displayFont, FontStyle.Bold);
            var outline = roundOutlineText.gameObject.AddComponent<Outline>(); // 中を地の色で塗り、ふちだけ白く見せる
            outline.effectColor = WithAlpha(Fg, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);
            Skew(roundOutlineText, 0.12f);
            roundNumberText = Label("RoundNumber", roundBlock, 250f, 245f, 280f, 230f, "01", 210, Accent,
                TextAnchor.MiddleLeft, displayFont, FontStyle.Bold);
            Skew(roundNumberText, 0.12f);
            Tag("StageTag", roundBlock, 109f, 400f, 130f, 26f, "STAGE NAME", 14, Fg, Bg);
            roundNameText = Label("RoundName", roundBlock, 44f, 448f, 500f, 54f, "", 44, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);

            // ---- ボス戦 ----
            bossBlock = NewRect("BossBlock", infoRoot, 300f, StageH * 0.5f, 540f, StageH);
            var hazardTop = NewImage("HazardTop", bossBlock, 280f, 88f, 500f, 22f, Boss, stripeSprite);
            hazardTop.type = Image.Type.Tiled;
            Tag("Warning", bossBlock, 104f, 132f, 120f, 26f, "WARNING", 14, Boss, Bg);
            var bossWord = Label("BossWord", bossBlock, 44f, 240f, 500f, 200f, "BOSS", 180, Boss,
                TextAnchor.MiddleLeft, displayFont, FontStyle.Bold);
            Skew(bossWord, 0.12f);
            var hazardBottom = NewImage("HazardBottom", bossBlock, 280f, 352f, 500f, 8f, Boss, stripeSprite);
            hazardBottom.type = Image.Type.Tiled;
            Tag("TargetTag", bossBlock, 99f, 400f, 110f, 26f, "TARGET", 14, Boss, Bg);
            bossNameText = Label("BossName", bossBlock, 44f, 448f, 500f, 54f, "", 44, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            bossBlock.gameObject.SetActive(false);
        }

        // ---- デッキ（右）：回るレコード（グルーヴゲージは筐体の GrooveGauge に移した） ----

        void BuildDeck(RectTransform root)
        {
            deckRoot = NewRect("Deck", root, DeckLeft + DeckW * 0.5f, StageTop + StageH * 0.5f, DeckW, StageH);
            NewImage("DeckBg", deckRoot, DeckW * 0.5f, StageH * 0.5f, DeckW, StageH, Panel);
            NewImage("DeckLine", deckRoot, -1f, StageH * 0.5f, 2f, StageH, Line);
            var stripes = NewImage("DeckStripes", deckRoot, DeckW * 0.5f, StageH * 0.5f, DeckW, StageH, WithAlpha(Fg, 0.025f), fineStripeSprite);
            stripes.type = Image.Type.Tiled;
            Label("DeckCaption", deckRoot, 28f, 34f, 200f, 20f, Spaced("ON DECK"), 13, Mute, TextAnchor.MiddleLeft, displayFont);
            NewImage("CornerH", deckRoot, DeckW - 48f, 26f, 36f, 2f, Mute);
            NewImage("CornerV", deckRoot, DeckW - 31f, 42f, 2f, 34f, Mute);

            // レコード
            recordRoot = NewRect("Record", deckRoot, DeckW * 0.5f, RecordLocalY, 0f, 0f);
            NewImage("Ring", recordRoot, 0f, 0f, RecordSize + 30f, RecordSize + 30f, Line, ringSprite);
            recordDisc = NewImage("Disc", recordRoot, 0f, 0f, RecordSize, RecordSize, Color.white, recordSprite).rectTransform;
            BuildDiscLabel(recordDisc); // 再生中のレコードのジャケットをラベルに貼る（MonitorDisplay.Stage.cs）

            // 流しているレコードの名前（流していない時は NO RECORD）
            var info = NewRect("DeckInfo", deckRoot, DeckW * 0.5f, 500f, DeckW, 80f);
            deckInfoGroup = info.gameObject.AddComponent<CanvasGroup>();
            deckName = Label("DeckName", info, DeckW * 0.5f, 28f, DeckW - 60f, 40f, "", 30, Fg, TextAnchor.MiddleCenter, font, FontStyle.Bold);
            deckSub = Label("DeckSub", info, DeckW * 0.5f, 62f, DeckW - 60f, 20f, "", 14, Mute, TextAnchor.MiddleCenter, font);
        }

        // ---- 数字の帯 ----

        void BuildInfoBand(RectTransform root)
        {
            var band = NewRect("InfoBand", root, CanvasW * 0.5f, InfoTop + InfoH * 0.5f, CanvasW, InfoH);
            infoBandGroup = band.gameObject.AddComponent<CanvasGroup>();
            NewImage("BandBg", band, CanvasW * 0.5f, InfoH * 0.5f, CanvasW, InfoH, Bg);
            NewImage("BandLine", band, CanvasW * 0.5f, -1f, CanvasW, 2f, Line);

            const float left = 30f;
            float w = (CanvasW - left * 2f) / 5f;
            tileMedals = Tile(band, left + w * 0.5f, w, "MEDALS", "所持メダル");
            tileInsert = Tile(band, left + w * 1.5f, w, "INSERT", "投入");
            tileDrops = Tile(band, left + w * 2.5f, w, "DROPS", "落ちた枚数");
            tileLast = Tile(band, left + w * 3.5f, w, "LAST DROP", "直近の1枚");
            tileArea = Tile(band, left + w * 4.5f, w, "AREA PT", "エリアのポイント");
            for (int i = 1; i < 5; i++)
                Skew(NewImage("Sep", band, left + w * i, InfoH * 0.5f, 2f, InfoH - 40f, Line), 0.25f);
        }

        InfoTile Tile(RectTransform parent, float cx, float w, string en, string ja)
        {
            var t = new InfoTile { root = NewRect(en, parent, cx, InfoH * 0.5f, w, InfoH) };
            t.group = t.root.gameObject.AddComponent<CanvasGroup>();
            float x = 24f;
            Label("En", t.root, x, 30f, w, 18f, Spaced(en), 13, Mute, TextAnchor.MiddleLeft, displayFont, FontStyle.Bold);
            Label("Ja", t.root, w - 20f, 30f, w, 18f, ja, 14, Mute, TextAnchor.MiddleRight, font);
            t.value = Label("Value", t.root, x, 80f, w - 40f, 60f, "0", 52, Fg, TextAnchor.MiddleLeft, displayFont, FontStyle.Bold);
            Skew(t.value, 0.12f);
            t.sub = Label("Sub", t.root, x, 124f, w - 40f, 20f, "", 14, Mute, TextAnchor.MiddleLeft, font);
            tiles.Add(t);
            return t;
        }

        /// <summary>マスの数字を入れる。変わった時だけ少しはずむ。</summary>
        static void SetTile(InfoTile t, string value, string sub, Color color)
        {
            t.value.color = color;
            t.sub.text = sub;
            if (value == t.last) return;
            if (t.last != null) t.pulse = 1f;
            t.last = value;
            t.value.text = value;
        }

        // ---- 下の帯 ----

        void BuildBottomBar(RectTransform root)
        {
            var bottom = NewRect("Bottom", root, CanvasW * 0.5f, BottomTop + BottomH * 0.5f, CanvasW, BottomH);
            NewImage("BottomBg", bottom, CanvasW * 0.5f, BottomH * 0.5f, CanvasW, BottomH, Panel);
            NewImage("BottomLine", bottom, CanvasW * 0.5f, -1f, CanvasW, 2f, Line);
            Label("RoundsLabel", bottom, ChipsLeft, 24f, 300f, 18f, Spaced("ROUNDS"), 13, Mute, TextAnchor.MiddleLeft, displayFont);
            chipsRoot = NewRect("Chips", bottom, 0f, 64f, 0f, 0f);

            var quota = NewRect("Quota", bottom, CanvasW * 0.5f, BottomH * 0.5f, CanvasW, BottomH);
            quotaGroup = quota.gameObject.AddComponent<CanvasGroup>();
            quotaLabel = Label("QuotaLabel", quota, QuotaLeft, 24f, 300f, 18f, "QUOTA", 13, Mute, TextAnchor.MiddleLeft, displayFont);
            quotaValue = Label("QuotaValue", quota, QuotaRight, 24f, 400f, 30f, "", 24, Fg, TextAnchor.MiddleRight, displayFont, FontStyle.Bold);

            const float gap = 5f;
            float segW = (QuotaRight - QuotaLeft - (QuotaSegments - 1) * gap) / QuotaSegments;
            segments = new Image[QuotaSegments];
            for (int i = 0; i < QuotaSegments; i++)
            {
                float cx = QuotaLeft + segW * 0.5f + i * (segW + gap);
                segments[i] = NewImage($"Seg{i + 1}", quota, cx, 66f, segW, 26f, Line);
                Skew(segments[i], 0.35f);
            }
        }

        void RebuildRoundChips()
        {
            foreach (var chip in chips) Destroy(chip.root.gameObject);
            chips.Clear();

            var rounds = game.areas[areaIndex].rounds;
            for (int i = 0; i < rounds.Length; i++)
            {
                var r = rounds[i];
                var root = NewRect($"Round{i + 1}", chipsRoot, ChipsLeft + ChipW * 0.5f + i * ChipStep, 0f, ChipW, ChipH);
                var chip = new RoundChip { root = root, group = root.gameObject.AddComponent<CanvasGroup>(), boss = r.isBoss };
                chip.outer = NewImage("Outer", root, ChipW * 0.5f, ChipH * 0.5f, ChipW, ChipH, Line);
                Skew(chip.outer, 0.35f);
                chip.inner = NewImage("Inner", root, ChipW * 0.5f, ChipH * 0.5f, ChipW - 4f, ChipH - 4f, Panel);
                Skew(chip.inner, 0.35f);
                chip.label = Label("No", root, ChipW * 0.5f, ChipH * 0.5f, ChipW, ChipH, r.isBoss ? "BOSS" : (i + 1).ToString("00"),
                    20, Fg, TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
                Skew(chip.label, 0.12f);
                chip.underline = NewImage("Mark", root, ChipW * 0.5f, ChipH + 8f, ChipW * 0.6f, 3f, Accent);
                chips.Add(chip);
            }
        }

        /// <summary>チップの色：今のラウンドは塗りつぶし、終わったラウンドは暗く、これからのラウンドは枠だけ。</summary>
        void RefreshChips()
        {
            for (int i = 0; i < chips.Count; i++)
            {
                var c = chips[i];
                bool current = i == roundIndex;
                bool cleared = i < roundIndex;
                Color hi = c.boss ? Boss : Accent;
                c.outer.color = current ? hi : Line;
                c.inner.color = current ? hi : (cleared ? Line : Panel);
                c.label.color = current ? Bg : (cleared ? Mute : (c.boss ? Boss : Fg));
                c.underline.enabled = current;
                c.underline.color = hi;
            }
        }

        // ---- クリア・失敗などの帯 ----

        void BuildStatus(RectTransform root)
        {
            statusRoot = NewRect("Status", root, CanvasW * 0.5f, StageTop + StageH * 0.5f, CanvasW, 150f);
            statusRoot.localRotation = Quaternion.Euler(0f, 0f, 3f);
            statusGroup = statusRoot.gameObject.AddComponent<CanvasGroup>();
            statusGroup.alpha = 0f;
            statusBg = NewImage("Band", statusRoot, CanvasW * 0.5f, 75f, CanvasW + 140f, 130f, Accent);
            var stripes = NewImage("BandStripes", statusRoot, CanvasW * 0.5f, 75f, CanvasW + 140f, 130f, WithAlpha(Bg, 0.10f), stripeSprite);
            stripes.type = Image.Type.Tiled;
            NewImage("BandLineTop", statusRoot, CanvasW * 0.5f, 6f, CanvasW + 140f, 2f, Bg);
            NewImage("BandLineBottom", statusRoot, CanvasW * 0.5f, 144f, CanvasW + 140f, 2f, Bg);
            statusEn = Label("En", statusRoot, CanvasW * 0.5f, 62f, 1400f, 100f, "", 88, Bg, TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
            Skew(statusEn, 0.15f);
            statusJa = Label("Ja", statusRoot, CanvasW * 0.5f, 120f, 1400f, 26f, "", 22, Bg, TextAnchor.MiddleCenter, font, FontStyle.Bold);
        }

        // ================================================================
        // 外から呼ぶもの
        // ================================================================

        /// <summary>エリアの内容を画面に入れる（演出なしの完成した状態）。</summary>
        public void ShowArea(int area, int round)
        {
            StopIntro(false);
            StopShop();
            areaIndex = Mathf.Clamp(area, 0, game.areas.Length - 1);
            roundIndex = round;
            areaTag.text.text = $"AREA {areaIndex + 1:00}";
            RebuildRoundChips();
            ShowRound(round, true);
            SetFinalVisuals();
        }

        /// <summary>ラウンドが変わった時。ROUND 番号・ラウンド名・チップを切り替える。</summary>
        public void ShowRound(int round, bool instant = false)
        {
            roundIndex = round;
            var rounds = game.areas[areaIndex].rounds;
            var r = rounds[Mathf.Clamp(round, 0, rounds.Length - 1)];
            roundNumberText.text = (round + 1).ToString("00");
            roundNameText.text = r.title;
            bossNameText.text = r.title;
            roundBlock.gameObject.SetActive(!r.isBoss);
            bossBlock.gameObject.SetActive(r.isBoss);
            subtitleText.text = Spaced($"ROUND {round + 1:00}") + "   /   " + Spaced(r.isBoss ? "BOSS BATTLE" : "MEDAL PUSHER");
            statRoundValue.text = $"{round + 1:00} / {rounds.Length:00}";
            RefreshChips();
            ApplyTagColor();
            if (!instant) roundPulse = 1f; // 番号がはずむ
        }

        /// <summary>左上のタグの色（ボスは赤、天気は水色、いつもは白）。</summary>
        void ApplyTagColor()
        {
            if (areaTag == null) return;
            areaTag.bg.color = weatherOn ? Sky : (IsBoss ? Boss : Fg);
            areaTag.text.color = Bg;
        }

        /// <summary>エリアの始まりの演出を流す。終わったら（スキップした時も）onFinished を呼ぶ。</summary>
        public void PlayIntro(Action onFinished)
        {
            StopIntro(false);
            introFinished = onFinished;
            if (!CanPlayIntro)
            {
                FinishIntro();
                return;
            }
            introRoutine = StartCoroutine(IntroRoutine());
        }

        /// <summary>演出を飛ばして、すぐプレイ画面にする。</summary>
        public void SkipIntro()
        {
            if (introRoutine == null) return;
            StopIntro(true);
        }

        void StopIntro(bool finish)
        {
            if (introRoutine != null)
            {
                StopAllCoroutines();
                introRoutine = null;
                SetFinalVisuals();
                RestoreCamera();
                if (finish) FinishIntro();
            }
        }

        void FinishIntro()
        {
            introRoutine = null;
            var callback = introFinished;
            introFinished = null;
            callback?.Invoke();
        }

        // ================================================================
        // 演出
        // ================================================================

        IEnumerator IntroRoutine()
        {
            SetIntroStartVisuals();
            BeginCloseup();
            yield return Wait(0.4f);

            // 1. 上の帯
            yield return Tween(0.4f, t =>
            {
                topGroup.alpha = t;
                topRoot.localScale = new Vector3(1f, EaseOut(t), 1f);
            });

            // 2. ステージが左から右へ開く（オレンジの線が走る）。デッキも一緒に出る
            stageEdge.gameObject.SetActive(true);
            StartCoroutine(Tween(0.5f, t => deckRoot.localScale = new Vector3(1f, EaseOut(t), 1f)));
            yield return Tween(0.7f, t =>
            {
                float w = StageW * EaseInOut(t);
                stageMask.sizeDelta = new Vector2(w, 0f);
                stageEdge.anchoredPosition = new Vector2(w, -StageH * 0.5f);
            });
            stageEdge.gameObject.SetActive(false);

            // 3. ラウンドのチップと数字の帯を順に
            infoBandGroup.alpha = 1f;
            foreach (var tile in tiles)
            {
                StartCoroutine(PopIn(tile.group, tile.root, 0.25f));
                yield return Wait(0.07f);
            }
            foreach (var chip in chips)
            {
                StartCoroutine(PopIn(chip.group, chip.root, 0.25f));
                yield return Wait(0.08f);
            }
            yield return Wait(0.15f);

            // 4. レコードが右から入る
            var target = new Vector2(DeckW * 0.5f, -RecordLocalY);
            var from = new Vector2(DeckW * 0.5f + 600f, -RecordLocalY);
            yield return Tween(0.6f, t => recordRoot.anchoredPosition = Vector2.LerpUnclamped(from, target, EaseOutBack(t)));

            // 5. 回り始めると同時に、レコードの名前・エリア名・ROUND・マスコット
            spinning = true;
            StartCoroutine(Tween(0.4f, t => deckInfoGroup.alpha = t));
            titleText.text = AreaTitle;
            StartCoroutine(Tween(0.4f, t =>
            {
                titleText.color = WithAlpha(Fg, t);
                subtitleText.color = WithAlpha(Mute, t);
            }));
            var infoBase = new Vector2(StageW * 0.5f, -StageH * 0.5f);
            StartCoroutine(Tween(0.45f, t =>
            {
                infoGroup.alpha = t;
                infoRoot.anchoredPosition = infoBase + new Vector2(-80f * (1f - EaseOut(t)), 0f);
            }));
            StartCoroutine(PopIn(mascotGroup, mascotRoot, 0.4f));
            yield return Wait(2.0f);

            // 6. 縮んで筐体のモニターに戻る
            yield return Tween(1.4f, t =>
            {
                float e = EaseInOut(t);
                var cam = Camera.main;
                if (cam == null) return;
                cam.transform.SetPositionAndRotation(Vector3.Lerp(closePos, playPos, e), Quaternion.Slerp(closeRot, playRot, e));
                SetLens(cam, Mathf.Lerp(0f, playLens, e));
            });
            RestoreCamera();
            yield return Tween(0.3f, t =>
            {
                statsGroup.alpha = t;
                quotaGroup.alpha = t;
            });

            FinishIntro();
        }

        /// <summary>演出の始まりの状態：背景だけ。</summary>
        void SetIntroStartVisuals()
        {
            topGroup.alpha = 0f;
            topRoot.localScale = new Vector3(1f, 0f, 1f);
            titleText.text = "";
            stageMask.sizeDelta = new Vector2(0f, 0f);
            stageEdge.gameObject.SetActive(false);
            deckRoot.localScale = new Vector3(1f, 0f, 1f);
            foreach (var chip in chips)
            {
                chip.group.alpha = 0f;
                chip.root.localScale = Vector3.zero;
            }
            infoBandGroup.alpha = 0f;
            foreach (var tile in tiles)
            {
                tile.group.alpha = 0f;
                tile.root.localScale = Vector3.zero;
            }
            recordRoot.anchoredPosition = new Vector2(DeckW * 0.5f + 600f, -RecordLocalY);
            recordDisc.localRotation = Quaternion.identity;
            spinning = false;
            deckInfoGroup.alpha = 0f;
            mascotGroup.alpha = 0f;
            mascotRoot.localScale = Vector3.zero;
            infoGroup.alpha = 0f;
            statsGroup.alpha = 0f;
            quotaGroup.alpha = 0f;
        }

        /// <summary>すべて出そろった状態（プレイ中の画面）。</summary>
        void SetFinalVisuals()
        {
            topGroup.alpha = 1f;
            topRoot.localScale = Vector3.one;
            titleText.text = AreaTitle;
            titleText.color = Fg;
            subtitleText.color = Mute;
            stageMask.sizeDelta = new Vector2(StageW, 0f);
            stageEdge.gameObject.SetActive(false);
            deckRoot.localScale = Vector3.one;
            foreach (var chip in chips) chip.root.localScale = Vector3.one;
            infoBandGroup.alpha = 1f;
            foreach (var tile in tiles)
            {
                tile.group.alpha = 1f;
                tile.root.localScale = Vector3.one;
            }
            recordRoot.anchoredPosition = new Vector2(DeckW * 0.5f, -RecordLocalY);
            spinning = true;
            deckInfoGroup.alpha = 1f;
            mascotGroup.alpha = 1f;
            mascotRoot.localScale = Vector3.one;
            infoGroup.alpha = 1f;
            infoRoot.anchoredPosition = new Vector2(StageW * 0.5f, -StageH * 0.5f);
            statsGroup.alpha = 1f;
            quotaGroup.alpha = 1f;
        }

        /// <summary>カメラをモニターの正面に寄せ、モニターが画面いっぱいに映るようにする。</summary>
        void BeginCloseup()
        {
            ComputeCloseup();
            var cam = Camera.main;
            cam.transform.SetPositionAndRotation(closePos, closeRot);
            SetLens(cam, 0f);
            cameraOverridden = true;
        }

        /// <summary>プレイ中のカメラの位置（playPos）と、モニターに寄った位置（closePos）を求める。カメラは動かさない。</summary>
        void ComputeCloseup()
        {
            var cam = Camera.main;
            game.ApplyCameraPreset();            // まずプレイ中の位置を求める
            playPos = cam.transform.position;
            playRot = cam.transform.rotation;
            var lens = cam.GetComponent<LensDistortion>();
            playLens = lens != null ? lens.strength : 0f;

            // 画面がちょうど収まる距離
            var b = screenRenderer.bounds;
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cam.aspect);
            float dist = Mathf.Max(b.extents.x / Mathf.Tan(hHalf), b.extents.y / Mathf.Tan(vHalf));
            closePos = new Vector3(b.center.x, b.center.y, b.min.z - dist);
            closeRot = Quaternion.identity;
            cam.transform.SetPositionAndRotation(playPos, playRot);
        }

        void RestoreCamera()
        {
            if (!cameraOverridden) return;
            cameraOverridden = false;
            game.ApplyCameraPreset();
        }

        static void SetLens(Camera cam, float strength)
        {
            var lens = cam.GetComponent<LensDistortion>();
            if (lens != null) lens.strength = strength;
        }

        IEnumerator PopIn(CanvasGroup group, RectTransform rt, float duration)
        {
            yield return Tween(duration, t =>
            {
                group.alpha = Mathf.Clamp01(t * 2f);
                rt.localScale = Vector3.one * EaseOutBack(t);
            });
        }

        float Speed => Mathf.Max(0.01f, game.monitor.introSpeed);

        IEnumerator Wait(float seconds)
        {
            yield return new WaitForSeconds(seconds / Speed);
        }

        IEnumerator Tween(float duration, Action<float> step)
        {
            float d = duration / Speed;
            for (float t = 0f; t < d; t += Time.deltaTime)
            {
                step(t / d);
                yield return null;
            }
            step(1f);
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
        static float EaseInOut(float t) => t * t * (3f - 2f * t);
        static float EaseOutBack(float t)
        {
            const float c = 1.6f;
            float u = t - 1f;
            return 1f + (c + 1f) * u * u * u + c * u * u;
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (game == null || recordDisc == null) return;

            if (spinning)
                recordDisc.Rotate(0f, 0f, -game.monitor.recordSpinSpeed * Time.deltaTime);

            // マスコットが少し上下に揺れる
            mascotRoot.anchoredPosition = new Vector2(130f, -(150f + Mathf.Sin(Time.time * 2.2f) * 4f));

            // ラウンドが変わった時に番号がはずむ
            roundPulse = Mathf.MoveTowards(roundPulse, 0f, Time.deltaTime * 2.5f);
            float ps = 1f + 0.25f * roundPulse * roundPulse;
            roundNumberText.rectTransform.localScale = new Vector3(ps, ps, 1f);

            // 終わったラウンドのチップは少し暗く（演出中は PopIn に任せる）。今のラウンドの下線は点滅
            if (introRoutine == null)
                for (int i = 0; i < chips.Count; i++) chips[i].group.alpha = i < roundIndex ? 0.6f : 1f;
            if (roundIndex >= 0 && roundIndex < chips.Count)
            {
                var mark = chips[roundIndex].underline;
                mark.color = WithAlpha(mark.color, 0.55f + 0.45f * Mathf.Sin(Time.time * 5f));
            }

            UpdateHud();
            UpdateDeck();
            UpdateInfoBand();
            UpdateStatus();
            UpdateStage();   // MonitorDisplay.Stage.cs
            UpdateEffects(); // MonitorDisplay.Effects.cs
            UpdateShop();    // MonitorDisplay.Shop.cs
        }

        /// <summary>上の帯のスコアと、下の帯のノルマ（ボス戦はHP）。</summary>
        void UpdateHud()
        {
            int quota = Mathf.Max(1, game.CurrentQuota);
            bool boss = game.IsBossRound;
            float ratio;
            if (boss)
            {
                quotaLabel.text = Spaced("BOSS HP");
                ratio = Mathf.Clamp01((float)game.BossHp / quota);
                quotaValue.text = $"{game.BossHp} / {quota}";
            }
            else
            {
                quotaLabel.text = Spaced("QUOTA");
                ratio = Mathf.Clamp01((float)game.Points / quota);
                quotaValue.text = $"{game.Points} / {quota} PT";
            }
            shownRatio = Mathf.Lerp(shownRatio, ratio, 1f - Mathf.Exp(-10f * Time.deltaTime));

            Color on = boss ? Boss : (ratio >= 1f ? Yellow : Accent);
            float filled = shownRatio * QuotaSegments;
            for (int i = 0; i < segments.Length; i++)
            {
                float f = Mathf.Clamp01(filled - i);
                segments[i].color = Color.Lerp(Line, on, f < 1f ? f * 0.6f : 1f); // 途中の目盛りは薄く
            }

            statScoreValue.text = game.Points.ToString();
        }

        /// <summary>右のデッキ：流しているレコードの名前。</summary>
        void UpdateDeck()
        {
            var fx = EffectSystem;
            var cur = fx != null ? fx.Current : null;
            if (!RecordData.IsNullOrEmpty(cur))
            {
                deckName.text = cur.name;
                deckName.color = Fg;
                deckSub.text = Spaced("NOW PLAYING");
                deckSub.color = Accent;
            }
            else
            {
                deckName.text = "NO RECORD";
                deckName.color = Mute;
                deckSub.text = "E キーでレコードを選ぶ";
                deckSub.color = Mute;
            }
        }

        /// <summary>数字の帯：所持メダル・投入・落ちた枚数・直近の1枚・エリアのポイント。</summary>
        void UpdateInfoBand()
        {
            var fx = EffectSystem;
            int left = Mathf.Max(0, game.InsertLimit - game.InsertedThisRound);
            int drops = game.CenterDrops + game.SideDrops;
            int last = fx != null ? fx.LastDropPoints : 0;

            SetTile(tileMedals, game.OwnedMedals.ToString(), "手持ちのメダル", Fg);
            SetTile(tileInsert, $"{game.InsertedThisRound}/{game.InsertLimit}", $"あと {left} 枚", left <= 5 ? Boss : Fg);
            SetTile(tileDrops, drops.ToString(), $"中央 {game.CenterDrops}　横 {game.SideDrops}　ロスト {game.LostDrops}", Fg);
            SetTile(tileLast, last > 0 ? $"+{last}" : "0", fx != null ? $"レコード {fx.PlayCount} 回再生" : "", last > 0 ? Accent : Fg);
            SetTile(tileArea, game.AreaPoints.ToString(), $"特殊ポイント {game.SpecialPoints}", Yellow);

            foreach (var t in tiles)
            {
                t.pulse = Mathf.MoveTowards(t.pulse, 0f, Time.deltaTime * 4f);
                float s = 1f + 0.18f * t.pulse * t.pulse;
                t.value.rectTransform.localScale = new Vector3(s, s, 1f);
            }
        }

        /// <summary>クリア・失敗などの時に、ステージに斜めの帯を出す。</summary>
        void UpdateStatus()
        {
            string en = null, ja = null;
            Color c = Accent;
            switch (game.State)
            {
                case GameState.Cleared:
                    en = game.IsLastRoundOfArea ? "AREA CLEAR" : "ROUND CLEAR";
                    ja = game.IsLastRoundOfArea ? "エリアクリア！" : "クリア！";
                    break;
                case GameState.Failed: en = "FAILED"; ja = "失敗…"; c = Boss; break;
                case GameState.Shop: en = "SHOP"; ja = "ショップ"; c = Fg; break;
                case GameState.Finished: en = "THANK YOU FOR PLAYING"; ja = "おしまい（仮）"; c = Fg; break;
            }

            bool show = en != null && introRoutine == null && !shopOpen;
            if (show && en != statusKey)
            {
                statusKey = en;
                statusEn.text = en;
                statusJa.text = ja;
                statusBg.color = c;
                statusAnim = 0f;
            }
            if (!show) statusKey = null;

            statusAnim = Mathf.MoveTowards(statusAnim, show ? 1f : 0f, Time.deltaTime * 5f);
            statusGroup.alpha = statusAnim;
            statusRoot.localScale = new Vector3(1f, EaseOut(statusAnim), 1f);
        }

        // ================================================================
        // UI を作る小さな道具
        // ================================================================

        /// <summary>親の左上を (0, 0) として、中心 (cx, cy)・大きさ (w, h) の四角を作る。</summary>
        RectTransform NewRect(string name, Transform parent, float cx, float cy, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(cx, -cy);
            return rt;
        }

        Image NewImage(string name, Transform parent, float cx, float cy, float w, float h, Color color, Sprite sprite = null)
        {
            var img = NewRect(name, parent, cx, cy, w, h).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>影付きの文字（ショップの画面などで使う）。</summary>
        Text NewText(string name, Transform parent, float cx, float cy, float w, float h, string text, int size, Color color)
        {
            var t = NewRect(name, parent, cx, cy, w, h).gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var shadow = t.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(3f, -3f);
            return t;
        }

        /// <summary>
        /// 影なしの文字（モニターの本体で使う）。
        /// x は、左寄せなら左端・右寄せなら右端・中央寄せなら中心。cy は上下の中心。
        /// </summary>
        Text Label(string name, Transform parent, float x, float cy, float w, float h, string text, int size, Color color,
                   TextAnchor align = TextAnchor.MiddleLeft, Font f = null, FontStyle style = FontStyle.Normal)
        {
            float cx = IsLeft(align) ? x + w * 0.5f : IsRight(align) ? x - w * 0.5f : x;
            var t = NewRect(name, parent, cx, cy, w, h).gameObject.AddComponent<Text>();
            t.font = f != null ? f : font;
            t.fontStyle = style;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static bool IsLeft(TextAnchor a) => a == TextAnchor.UpperLeft || a == TextAnchor.MiddleLeft || a == TextAnchor.LowerLeft;
        static bool IsRight(TextAnchor a) => a == TextAnchor.UpperRight || a == TextAnchor.MiddleRight || a == TextAnchor.LowerRight;

        /// <summary>斜めのタグ（平行四辺形の背景に、太い文字）。</summary>
        TagParts Tag(string name, Transform parent, float cx, float cy, float w, float h, string text, int size, Color bg, Color fg)
        {
            var p = new TagParts { root = NewRect(name, parent, cx, cy, w, h) };
            p.bg = NewImage("Bg", p.root, w * 0.5f, h * 0.5f, w, h, bg);
            Skew(p.bg, 0.35f);
            p.text = Label("Text", p.root, w * 0.5f, h * 0.5f, w, h, text, size, fg, TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
            return p;
        }

        static void Skew(Graphic g, float amount)
        {
            var s = g.gameObject.AddComponent<MonitorSkew>();
            s.skew = amount;
        }

        static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        /// <summary>「ROUND」→「R O U N D」のように字の間をあける（見出しを広めに見せる）。</summary>
        static string Spaced(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length * 2);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ---- 絵（コードで描く） ----

        void CreateSprites()
        {
            whiteSprite = MakeSprite(4, (x, y) => Color.white);
            circleSprite = MakeSprite(256, (x, y) => new Color(1f, 1f, 1f, Edge(1f - Radius(x, y), 256)));
            ringSprite = MakeSprite(512, (x, y) =>
            {
                float r = Radius(x, y);
                return new Color(1f, 1f, 1f, Mathf.Min(Edge(1f - r, 512), Edge(r - 0.985f, 512)));
            });
            recordSprite = MakeSprite(512, RecordPixel);
            stripeSprite = MakeTileSprite(32, (i, j) => (i + j) % 32 < 16 ? 1f : 0f);     // 太い斜め線（ボス・帯）
            fineStripeSprite = MakeTileSprite(24, (i, j) => (i + j) % 24 < 3 ? 1f : 0f);  // 細い斜め線（飾り）
            dotSprite = MakeTileSprite(14, (i, j) =>
            {
                float dx = i + 0.5f - 7f, dy = j + 0.5f - 7f;
                return Mathf.Clamp01(2.6f - Mathf.Sqrt(dx * dx + dy * dy));                // 網点
            });
        }

        /// <summary>レコード：平たい黒い盤に細い溝、オレンジのラベル（回っているのが分かるよう白い線を1本）。</summary>
        static Color RecordPixel(float x, float y)
        {
            float r = Radius(x, y);
            float a = Edge(1f - r, 512);
            if (r > 0.97f) return new Color(0.13f, 0.13f, 0.15f, a);                       // ふち
            if (r > 0.40f)
            {
                float g = Mathf.Repeat(r * 36f, 1f) < 0.08f ? 0.11f : 0.075f;                // 溝
                return new Color(g, g, g * 1.1f, a);
            }
            if (r > 0.37f) return new Color(0.05f, 0.05f, 0.06f);                         // ラベルのまわり
            if (r < 0.04f) return new Color(0.05f, 0.05f, 0.06f);                         // 穴
            if (Mathf.Abs(y) < 0.025f && x > 0.12f && x < 0.30f) return new Color(0.95f, 0.95f, 0.93f); // 回転が分かる線
            return r < 0.09f ? new Color(0.85f, 0.38f, 0.05f) : (Color)new Color32(0xff, 0x7a, 0x1a, 0xff);
        }

        /// <summary>中心からの距離（-1〜1 の座標で）。</summary>
        static float Radius(float x, float y) => Mathf.Sqrt(x * x + y * y);

        /// <summary>ふちをなめらかにする（d が 0 の所で半透明）。</summary>
        static float Edge(float d, int size) => Mathf.Clamp01(d * size * 0.5f + 0.5f);

        static Sprite MakeSprite(int size, Func<float, float, Color> pixel)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var colors = new Color[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float x = (i + 0.5f) / size * 2f - 1f;
                    float y = (j + 0.5f) / size * 2f - 1f;
                    colors[j * size + i] = pixel(x, y);
                }
            tex.SetPixels(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>くり返して敷きつめる模様（Image.Type.Tiled で使う）。alpha はピクセル (i, j) の濃さ。</summary>
        static Sprite MakeTileSprite(int size, Func<int, int, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            var colors = new Color[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                    colors[j * size + i] = new Color(1f, 1f, 1f, alpha(i, j));
            tex.SetPixels(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }
    }
}
