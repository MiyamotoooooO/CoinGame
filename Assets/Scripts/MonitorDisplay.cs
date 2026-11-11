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
    ///  ・上の帯：左にレコードのマークとラウンド名、まん中に垂れ下がったタブ（ラウンド 01/15）、右に再生ボタン風の飾り
    ///  ・まん中：「現在」と今のポイント（大きく）、ノルマまでのバー、目標のポイント（ボス戦は残りHP）
    ///    レコード再生中は、その下に NOW PLAYING の帯（MonitorDisplay.Stage.cs）
    ///  ・左下：所持ポイント（ショップで使える）と特殊ポイントの札
    ///  ・下のまん中：回るレコード（下半分は画面の外）と、左右のイコライザー
    ///  ・右下：所持メダル
    ///  ・色はスレート（青みのグレー）の地に白い文字。差し色はオレンジ、札は紫とベージュ。ボスは赤、天気は水色
    ///
    /// エリアの始まりの演出（PlayIntro）：
    ///  1. カメラがモニターに寄る
    ///  2. 上の帯が出て、まん中にエリア名が出る
    ///  3. ラウンドのタブが上から下りてきて、紫の線が左右に伸びる
    ///  4. エリア名が消え、今のポイントとバーが出る
    ///  5. 札と所持メダルが順に出て、レコードが下から上がって回り始める
    ///  6. カメラが引いて、いつものプレイ画面に戻る
    ///
    /// 文字のフォント：Resources/MonitorFonts/Display（英字・数字用）と Resources/MonitorFonts/Body（日本語用）を
    /// 置くと使う。無ければ Unity 標準のフォントで出す。
    /// 画面の四隅は筐体の壁に少し隠れるので、大事な物は端から 30 くらい内側に置く。
    ///
    /// プレイ中のカメラではモニターの上の方が画面の外に切れるので、中身（1500×900 で組んだもの）は
    /// 設定の contentTop より下に収まるよう、まとめて縮めて下に寄せる（ApplyContentLayout）。
    /// 縮めて空いた左右には、左の物（ラウンド名・札）と右の物（再生ボタン風の飾り・所持メダル）を広げる。
    /// 上の切れる所は上の帯の色で塗る。
    /// カメラがモニターに寄っている間（始まりの演出）はモニター全体が見えるので、縮めずに画面いっぱいに出し、
    /// カメラが引くのに合わせてプレイ中の配置に変える（layoutBlend）。
    /// ショップの画面は寄った時にしか見ないので、いつも画面いっぱいに出す。
    /// </summary>
    public partial class MonitorDisplay : MonoBehaviour
    {
        const int UiLayer = 5;               // Unity 標準の「UI」レイヤー。メインカメラには映さない
        const float CanvasW = 1500f;
        const float CanvasH = 900f;          // モニターの画面と同じ 5:3

        // 上の帯
        const float TopH = 96f;
        const float TabTop = 0f;
        const float TabH = 176f;
        const float TabTopW = 400f;          // タブの上の幅
        const float TabBottomW = 330f;       // タブの下の幅

        // まん中（今のポイント）
        const float ScoreTop = 190f;         // まん中の上端
        const float ScoreH = 330f;
        const float BarLeft = 420f;
        const float BarRight = 1080f;
        const float BarY = 395f;             // バーの上下の中心（画面の上から）
        const float BarH = 46f;

        // 天気予報などでまん中を差し替える時の大きさ（MonitorDisplay.Stage.cs）
        const float StageW = 1000f;
        const float StageH = 554f;
        const float StageScale = 0.85f;
        const float StageCenterY = 365f;

        // レコード（下のまん中。下半分は画面の外）
        const float RecordX = CanvasW * 0.5f;
        const float RecordCanvasY = 940f;
        const float RecordSize = 600f;
        const float RecordHiddenOffset = 440f; // 始まりの演出で、画面の下に隠しておく量

        // イコライザー（レコードの左右）
        const int EqColumns = 5;
        const int EqBlocks = 6;
        const float EqBlockW = 14f;
        const float EqBlockH = 13f;
        const float EqStepX = 19f;
        const float EqStepY = 17f;
        const float EqBaseY = 880f;          // 一番下のブロックの中心

        // 「LAST 5」などの大きな文字（MonitorDisplay.Effects.cs）
        const float BannerX = 1255f;
        const float BannerY = 270f;

        static readonly Vector3 RigPosition = new Vector3(0f, -500f, 0f); // メインカメラから見えない場所

        /// <summary>フォントを置く場所（Resources の中）</summary>
        public const string FontFolder = "MonitorFonts";

        // 色（スレートの地＋白＋差し色）
        static readonly Color Bg = new Color32(0x2b, 0x2d, 0x37, 0xff);       // 地
        static readonly Color Panel = new Color32(0x20, 0x22, 0x2a, 0xff);    // 濃い板（「現在」の札など）
        static readonly Color Line = new Color32(0x4a, 0x4d, 0x5b, 0xff);     // 線
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);     // 薄い文字
        static readonly Color Fg = new Color32(0xf4, 0xf4, 0xf6, 0xff);       // 文字
        static readonly Color Accent = new Color32(0xff, 0x8a, 0x1f, 0xff);   // 差し色（オレンジ）
        static readonly Color Yellow = new Color32(0xff, 0xd2, 0x3f, 0xff);   // ノルマ達成
        static readonly Color Boss = new Color32(0xff, 0x3d, 0x5a, 0xff);     // ボス
        static readonly Color Sky = new Color32(0x38, 0xc8, 0xff, 0xff);      // 天気
        static readonly Color TopBarColor = new Color32(0x22, 0x24, 0x2d, 0xff);
        static readonly Color TabFill = new Color32(0x2c, 0x2e, 0x39, 0xff);
        static readonly Color TabBorder = new Color32(0x63, 0x66, 0x75, 0xff);
        static readonly Color Lavender = new Color32(0x9d, 0x90, 0xe6, 0xff); // 上の帯の下の線
        static readonly Color PillPurple = new Color32(0x7b, 0x70, 0xb6, 0xff);
        static readonly Color PillBeige = new Color32(0xd9, 0xbc, 0x8c, 0xff);
        static readonly Color PillDarkText = new Color32(0x24, 0x22, 0x26, 0xff); // ベージュの札の文字
        static readonly Color EqColor = new Color32(0x8a, 0x8d, 0x9e, 0xff);

        MedalPusherGame game;
        RectTransform contentRoot;    // 中身（ショップ以外）。contentTop より下に縮めて置く
        Image topBarFull;             // 画面の上の端から上の帯までを塗る
        float layoutBlend = 1f;       // 0：画面いっぱい（カメラが寄っている時）、1：プレイ中の配置
        float spread;                 // 左右の物を外へずらす量（中身の座標で）
        float lineT = 1f;             // 紫の線の伸び具合（0〜1）
        const float EdgeMargin = 40f; // 左右に広げる時、画面の端から空ける量

        /// <summary>左右に広げる物（side：-1 が左へ、1 が右へ）</summary>
        class SpreadItem
        {
            public RectTransform rt;
            public float baseX;
            public float side;
        }
        readonly List<SpreadItem> spreadItems = new List<SpreadItem>();
        RenderTexture renderTexture;
        Renderer screenRenderer;
        Material screenMaterial;
        Font font;          // 日本語用
        Font displayFont;   // 英字・数字用
        Sprite whiteSprite, circleSprite, ringSprite, recordSprite, stripeSprite;
        Sprite pillSprite, pillRingSprite, triangleSprite, trapezoidSprite, coinSprite, pointCoinSprite, backgroundSprite;

        /// <summary>ポイントの印（緑のメダル）。画面の右上の表示（PointsHud）やショップで使う</summary>
        public Sprite PointCoinSprite => pointCoinSprite;
        /// <summary>メダルの印（金色のメダル）。画面の左上の表示（PointsHud）で使う</summary>
        public Sprite MedalCoinSprite => coinSprite;

        // 上の帯
        RectTransform topRoot, tabRoot, lineLeft, lineRight, smallRecord;
        CanvasGroup topGroup, topLeftGroup, iconsGroup;
        Image tabBorder, playIcon;
        Text roundNameText, tabRoundText;

        // まん中（今のポイント）。天気予報などの時はまとめて隠す（normalLayerGroup）
        RectTransform normalLayer;
        CanvasGroup normalLayerGroup, scoreGroup;
        Image nowPill, barFill;
        Text nowPillText, scoreText, targetText;
        float scorePulse;
        int lastScore = -1;

        // エリアの始まりの演出で、まん中に出すエリア名
        CanvasGroup areaTitleGroup;
        Text areaNumberText, areaTitleText;

        // 左下の札
        RectTransform pillsRoot;
        RectTransform medalsBox;      // 所持メダルと仕切りの線の入れ物（プレイ中にまとめて大きくする）
        RectTransform topLeftRt, iconsRt;
        readonly List<CanvasGroup> pillGroups = new List<CanvasGroup>();
        readonly List<RectTransform> pillRoots = new List<RectTransform>();
        Text pointsValue, specialValue;

        // 右下の所持メダル
        RectTransform medalRoot;
        CanvasGroup medalGroup;
        Text medalValue;
        int lastMedals = -1;
        float medalPulse;

        // レコードとイコライザー
        RectTransform recordRoot, recordDisc;
        CanvasGroup eqGroup;
        Image[,] eqLeft, eqRight;
        float[] eqLevels;

        // クリア・失敗などの帯
        RectTransform statusRoot;
        CanvasGroup statusGroup;
        Image statusBg;
        Text statusEn, statusJa;
        string statusKey;
        float statusAnim;

        /// <summary>斜めのタグ（背景と文字）</summary>
        class TagParts
        {
            public RectTransform root;
            public Image bg;
            public Text text;
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
            // 地（スレートのグラデーションと、ゆるい波）
            NewImage("Background", root, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH, Color.white, backgroundSprite);
            topBarFull = NewImage("TopBarFull", root, CanvasW * 0.5f, 0f, CanvasW, 0f, TopBarColor);

            // 中身は 1500×900 のつもりで組み、あとでまとめて縮めて下に寄せる
            contentRoot = NewRect("Content", root, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH);
            var c = contentRoot;
            BuildRecord(c);
            BuildScore(c);
            BuildNowPlaying(normalLayer); // MonitorDisplay.Stage.cs（天気予報の時は一緒に隠す）
            BuildWeather(c);              // MonitorDisplay.Stage.cs
            BuildRecordScreen(c);         // レコード用の画面（MonitorDisplay.RecordScreen.cs）
            BuildPills(c);
            BuildMedals(c);
            BuildTopBar(c);
            BuildStatus(c);
            BuildEffectLayer(c);          // レコードの演出の文字（MonitorDisplay.Effects.cs）
            BuildBoss(c);                 // ボス戦の表示（MonitorDisplay.Boss.cs）。いちばん上に出す
            ApplyContentLayout();

            BuildShopPage(root);    // ショップの画面（MonitorDisplay.Shop.cs）。普段は隠しておく
        }

        /// <summary>
        /// 中身を、設定の contentTop（画面の上から）より下に収まるように縮めて置く。
        /// 横も同じ割合で縮めるので、左右に少し余白ができる（文字がつぶれないように）。
        /// </summary>
        void ApplyContentLayout()
        {
            float top = Mathf.Clamp(game.monitor.contentTop, 0f, CanvasH * 0.5f) * Mathf.Clamp01(layoutBlend);
            float s = (CanvasH - top) / CanvasH;
            contentRoot.localScale = new Vector3(s, s, 1f);
            contentRoot.anchoredPosition = new Vector2(CanvasW * 0.5f, -(top + CanvasH * 0.5f * s));

            // 縮めて空いた左右へ、左の物と右の物を広げる（中身の x=40 が画面の x=40 に来る量）
            float half = CanvasW * 0.5f - EdgeMargin;
            spread = half / s - half;
            foreach (var item in spreadItems)
                item.rt.anchoredPosition = new Vector2(item.baseX + item.side * spread, item.rt.anchoredPosition.y);
            ApplyPlayBoost();
            if (pillsRoot != null && pillsRoot.gameObject.activeSelf != game.monitor.showPointPills)
                pillsRoot.gameObject.SetActive(game.monitor.showPointPills); // ふだんは画面の右上（PointsHud）に出すので、モニターには出さない
            if (medalsBox != null && medalsBox.gameObject.activeSelf != game.monitor.showMedalsOnMonitor)
                medalsBox.gameObject.SetActive(game.monitor.showMedalsOnMonitor); // ふだんは画面の左上（PointsHud）に出すので、モニターには出さない
            SetTopLines(lineT);

            // 画面の上の端から、縮めた上の帯の下までを、上の帯の色で塗る
            float h = top + TopH * s;
            topBarFull.rectTransform.sizeDelta = new Vector2(CanvasW, h);
            topBarFull.rectTransform.anchoredPosition = new Vector2(CanvasW * 0.5f, -h * 0.5f);
            topBarFull.color = WithAlpha(TopBarColor, topGroup != null ? topGroup.alpha : 1f);
        }

        // ---- プレイ中だけ、小さくて見えにくい物を大きくする ----

        const float PillsLeft = 80f, PillsBottom = 854f;    // 左下の札の、左下の角
        const float MedalsRight = 1470f, MedalsBottom = 875f; // 右下の所持メダルの、右下の角

        /// <summary>
        /// プレイ中（カメラが引いて、モニターが小さく見える時）は、左下の札・右下の所持メダル・上の帯を大きくする。
        /// 倍率は設定の playPillScale・playMedalScale・playTopScale。カメラが寄っている時（layoutBlend 0）は元の大きさ。
        /// 札とメダルは画面の角を動かさずに、内側へ大きくする。
        /// </summary>
        void ApplyPlayBoost()
        {
            var m = game.monitor;
            float b = Mathf.Clamp01(layoutBlend);
            float kp = Mathf.Lerp(1f, Mathf.Max(1f, m.playPillScale), b);
            float km = Mathf.Lerp(1f, Mathf.Max(1f, m.playMedalScale), b);
            float kt = Mathf.Lerp(1f, Mathf.Max(1f, m.playTopScale), b);

            if (pillsRoot != null) ScaleAbout(pillsRoot, kp, PillsLeft, PillsBottom, -spread);
            if (medalsBox != null) ScaleAbout(medalsBox, km, MedalsRight, MedalsBottom, spread);

            // 上の帯：左は左の端、右は右の端を動かさずに大きくする（上下は帯のまん中のまま）
            if (topLeftRt != null)
            {
                topLeftRt.localScale = new Vector3(kt, kt, 1f);
                var p = topLeftRt.anchoredPosition;
                topLeftRt.anchoredPosition = new Vector2(p.x + (kt - 1f) * topLeftRt.sizeDelta.x * 0.5f, p.y);
            }
            if (iconsRt != null)
            {
                iconsRt.localScale = new Vector3(kt, kt, 1f);
                var p = iconsRt.anchoredPosition;
                iconsRt.anchoredPosition = new Vector2(p.x - (kt - 1f) * iconsRt.sizeDelta.x * 0.5f, p.y);
            }
            // タブ：上のふちを動かさずに大きくする。始まりの演出でタブが下りてくる間（layoutBlend 0）はさわらない
            if (tabRoot != null)
            {
                tabRoot.localScale = new Vector3(kt, kt, 1f);
                if (layoutBlend > 0.0001f)
                    tabRoot.anchoredPosition = new Vector2(CanvasW * 0.5f, -(TabTop + TabH * 0.5f) - (kt - 1f) * (TabH + 6f) * 0.5f);
            }
        }

        /// <summary>
        /// 中身の左上に置いた入れ物（大きさ 0）を、点 (ax, ay) を動かさずに k 倍にする。dx は左右に広げた量。
        /// </summary>
        static void ScaleAbout(RectTransform rt, float k, float ax, float ay, float dx)
        {
            rt.localScale = new Vector3(k, k, 1f);
            rt.anchoredPosition = new Vector2(dx + (1f - k) * ax, -(1f - k) * ay);
        }

        // ---- 上の帯 ----

        void BuildTopBar(RectTransform root)
        {
            topRoot = NewRect("Top", root, CanvasW * 0.5f, TopH * 0.5f, CanvasW, TopH);
            topGroup = topRoot.gameObject.AddComponent<CanvasGroup>();
            NewImage("TopBg", topRoot, CanvasW * 0.5f, TopH * 0.5f, CanvasW, TopH, TopBarColor);

            // 帯の下の紫の線（タブの左右から外へ伸びる）
            lineLeft = NewImage("LineLeft", topRoot, 0f, TopH + 3f, 0f, 6f, Lavender, pillSprite).rectTransform;
            lineLeft.pivot = new Vector2(1f, 0.5f);
            lineRight = NewImage("LineRight", topRoot, 0f, TopH + 3f, 0f, 6f, Lavender, pillSprite).rectTransform;
            lineRight.pivot = new Vector2(0f, 0.5f);
            lineLeft.GetComponent<Image>().type = Image.Type.Sliced;
            lineRight.GetComponent<Image>().type = Image.Type.Sliced;
            SetTopLines(1f);

            // 左：レコードのマークとラウンド名
            var left = NewRect("TopLeft", topRoot, 300f, TopH * 0.5f, 560f, TopH);
            Spread(left, -1f);
            topLeftRt = left;
            topLeftGroup = left.gameObject.AddComponent<CanvasGroup>();
            smallRecord = NewImage("RecordIcon", left, 70f, TopH * 0.5f, 60f, 60f, Color.white, recordSprite).rectTransform;
            roundNameText = Label("RoundName", left, 118f, TopH * 0.5f, 430f, 50f, "", 36, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);

            // 右：再生ボタン風の飾り（▶ は、レコードを流している間だけ明るい）
            var icons = NewRect("Icons", topRoot, 1330f, TopH * 0.5f, 300f, TopH);
            Spread(icons, 1f);
            iconsRt = icons;
            iconsGroup = icons.gameObject.AddComponent<CanvasGroup>();
            playIcon = NewImage("Play", icons, 60f, TopH * 0.5f, 40f, 44f, Fg, triangleSprite);
            NewImage("PauseL", icons, 142f, TopH * 0.5f, 12f, 40f, Fg, pillSprite).type = Image.Type.Sliced;
            NewImage("PauseR", icons, 164f, TopH * 0.5f, 12f, 40f, Fg, pillSprite).type = Image.Type.Sliced;
            NewImage("Ff1", icons, 236f, TopH * 0.5f, 32f, 40f, Accent, triangleSprite);
            NewImage("Ff2", icons, 264f, TopH * 0.5f, 32f, 40f, Accent, triangleSprite);

            // まん中：上から垂れ下がったタブ（ラウンド 01/15）
            tabRoot = NewRect("Tab", topRoot, CanvasW * 0.5f, TabTop + TabH * 0.5f, TabTopW + 12f, TabH + 6f);
            tabBorder = NewImage("TabBorder", tabRoot, (TabTopW + 12f) * 0.5f, (TabH + 6f) * 0.5f, TabTopW + 12f, TabH + 6f, TabBorder, trapezoidSprite);
            NewImage("TabFill", tabRoot, (TabTopW + 12f) * 0.5f, TabH * 0.5f, TabTopW, TabH, TabFill, trapezoidSprite);
            Label("TabLabel", tabRoot, (TabTopW + 12f) * 0.5f, 48f, 300f, 40f, "ラウンド", 32, Fg, TextAnchor.MiddleCenter, font, FontStyle.Bold);
            tabRoundText = Label("TabRound", tabRoot, (TabTopW + 12f) * 0.5f, 120f, 360f, 90f, "01", 80, Fg, TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
            tabRoundText.supportRichText = true;
        }

        /// <summary>左右に広げる物として覚えておく。</summary>
        void Spread(RectTransform rt, float side)
        {
            spreadItems.Add(new SpreadItem { rt = rt, baseX = rt.anchoredPosition.x, side = side });
        }

        /// <summary>帯の下の紫の線の長さ（0〜1）。タブの左右から外へ伸びる。</summary>
        void SetTopLines(float t)
        {
            lineT = t;
            float tabHalf = TabTopW * 0.5f - 10f;
            float len = (CanvasW * 0.5f - tabHalf - 40f + spread) * t;
            lineLeft.anchoredPosition = new Vector2(CanvasW * 0.5f - tabHalf, -(TopH + 3f));
            lineLeft.sizeDelta = new Vector2(len, 6f);
            lineRight.anchoredPosition = new Vector2(CanvasW * 0.5f + tabHalf, -(TopH + 3f));
            lineRight.sizeDelta = new Vector2(len, 6f);
        }

        // ---- まん中：今のポイント ----

        void BuildScore(RectTransform root)
        {
            // 天気予報などの時は、この層ごと隠す
            normalLayer = NewRect("Normal", root, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH);
            normalLayerGroup = normalLayer.gameObject.AddComponent<CanvasGroup>();

            var score = NewRect("Score", normalLayer, CanvasW * 0.5f, ScoreTop + ScoreH * 0.5f, CanvasW, ScoreH);
            scoreGroup = score.gameObject.AddComponent<CanvasGroup>();
            float y0 = ScoreTop; // score の中の座標に直す時に引く

            // 「現在」の札（ボス戦は「ボス」）
            nowPill = NewImage("NowPill", score, 330f, 265f - y0, 130f, 64f, Panel, pillSprite);
            nowPill.type = Image.Type.Sliced;
            nowPillText = Label("NowPillText", nowPill.rectTransform, 65f, 32f, 130f, 64f, "現在", 30, Fg, TextAnchor.MiddleCenter, font, FontStyle.Bold);

            scoreText = Label("Score", score, 418f, 262f - y0, 640f, 210f, "0", 190, Fg, TextAnchor.MiddleLeft, displayFont, FontStyle.Bold);

            // ノルマまでのバー（ふちだけの枠の中に、白い中身が伸びる）
            float barW = BarRight - BarLeft;
            var frame = NewImage("BarFrame", score, BarLeft + barW * 0.5f, BarY - y0, barW, BarH, WithAlpha(Fg, 0.85f), pillRingSprite);
            frame.type = Image.Type.Sliced;
            barFill = NewImage("BarFill", score, BarLeft + 7f, BarY - y0, 0f, BarH - 14f, Fg, pillSprite);
            barFill.type = Image.Type.Sliced;
            barFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            barFill.rectTransform.anchoredPosition = new Vector2(BarLeft + 7f, -(BarY - y0));

            targetText = Label("Target", score, BarRight, 470f - y0, 600f, 60f, "", 52, Fg, TextAnchor.MiddleRight, displayFont, FontStyle.Bold);
            targetText.supportRichText = true;

            // エリアの始まりの演出で出すエリア名（ふだんは隠す）
            var areaTitle = NewRect("AreaTitle", normalLayer, CanvasW * 0.5f, 360f, CanvasW, 300f);
            areaTitleGroup = areaTitle.gameObject.AddComponent<CanvasGroup>();
            areaTitleGroup.alpha = 0f;
            areaNumberText = Label("AreaNumber", areaTitle, CanvasW * 0.5f, 110f, 900f, 140f, "AREA 01", 120, Accent,
                TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
            areaTitleText = Label("AreaName", areaTitle, CanvasW * 0.5f, 210f, 1100f, 70f, "", 56, Fg, TextAnchor.MiddleCenter, font, FontStyle.Bold);
        }

        // ---- 左下：所持ポイント・特殊ポイント ----

        void BuildPills(RectTransform root)
        {
            pillsRoot = NewRect("Pills", root, 0f, 0f, 0f, 0f);
            Spread(pillsRoot, -1f);
            pointsValue = Pill("Points", 205f, 600f, "所持ポイント", PillPurple, Fg);
            specialValue = Pill("Special", 205f, 742f, "特殊ポイント", PillBeige, PillDarkText);
        }

        /// <summary>見出しと、色の付いた丸い札に数字。cx は中心、top は見出しの上下の中心。</summary>
        Text Pill(string name, float cx, float top, string label, Color color, Color textColor)
        {
            const float w = 250f, h = 66f;
            var r = NewRect(name, pillsRoot, cx, top + 52f, w, 120f);
            var g = r.gameObject.AddComponent<CanvasGroup>();
            pillRoots.Add(r);
            pillGroups.Add(g);
            Label("Label", r, w * 0.5f, 12f, w + 40f, 34f, label, 26, Fg, TextAnchor.MiddleCenter, font, FontStyle.Bold);
            var bg = NewImage("Bg", r, w * 0.5f, 72f, w, h, color, pillSprite);
            bg.type = Image.Type.Sliced;
            return Label("Value", r, w * 0.5f, 72f, w, h, "0", 40, textColor, TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
        }

        // ---- 右下：所持メダル ----

        void BuildMedals(RectTransform root)
        {
            // 仕切りの線と所持メダルを、大きさ 0 の入れ物（中身の左上）に入れて、まとめて右へ広げる・大きくする
            medalsBox = NewRect("MedalsBox", root, 0f, 0f, 0f, 0f);
            Spread(medalsBox, 1f);
            NewImage("MedalDivider", medalsBox, 1185f, 765f, 3f, 220f, Line);
            medalRoot = NewRect("Medals", medalsBox, 1330f, 765f, 280f, 220f);
            medalGroup = medalRoot.gameObject.AddComponent<CanvasGroup>();
            Label("Label", medalRoot, 140f, 30f, 280f, 34f, "所持メダル", 26, Fg, TextAnchor.MiddleCenter, font, FontStyle.Bold);
            NewImage("Coin", medalRoot, 62f, 125f, 84f, 84f, Color.white, coinSprite);
            medalValue = Label("Value", medalRoot, 116f, 125f, 170f, 90f, "0", 76, Fg, TextAnchor.MiddleLeft, displayFont, FontStyle.Bold);
        }

        // ---- 下のまん中：回るレコードとイコライザー ----

        void BuildRecord(RectTransform root)
        {
            recordRoot = NewRect("Record", root, RecordX, RecordCanvasY, 0f, 0f);
            NewImage("Ring", recordRoot, 0f, 0f, RecordSize + 20f, RecordSize + 20f, WithAlpha(Fg, 0.35f), ringSprite);
            recordDisc = NewImage("Disc", recordRoot, 0f, 0f, RecordSize, RecordSize, Color.white, recordSprite).rectTransform;
            BuildDiscLabel(recordDisc); // 再生中のレコードのジャケットをラベルに貼る（MonitorDisplay.Stage.cs）

            var eq = NewRect("Equalizer", root, 0f, 0f, 0f, 0f);
            eqGroup = eq.gameObject.AddComponent<CanvasGroup>();
            eqLeft = BuildEqualizer(eq, "EqLeft", 352f);
            eqRight = BuildEqualizer(eq, "EqRight", CanvasW - 352f - (EqColumns - 1) * EqStepX);
            eqLevels = new float[EqColumns * 2];
        }

        /// <summary>四角いブロックを積んだイコライザー（left は一番左の列の中心）。</summary>
        Image[,] BuildEqualizer(RectTransform parent, string name, float left)
        {
            var blocks = new Image[EqColumns, EqBlocks];
            for (int c = 0; c < EqColumns; c++)
                for (int b = 0; b < EqBlocks; b++)
                    blocks[c, b] = NewImage($"{name}_{c}_{b}", parent, left + c * EqStepX, EqBaseY - b * EqStepY, EqBlockW, EqBlockH, EqColor, whiteSprite);
            return blocks;
        }

        // ---- クリア・失敗などの帯 ----

        void BuildStatus(RectTransform root)
        {
            statusRoot = NewRect("Status", root, CanvasW * 0.5f, 400f, CanvasW, 150f);
            statusRoot.localRotation = Quaternion.Euler(0f, 0f, 3f);
            statusGroup = statusRoot.gameObject.AddComponent<CanvasGroup>();
            statusGroup.alpha = 0f;
            statusBg = NewImage("Band", statusRoot, CanvasW * 0.5f, 75f, CanvasW + 1000f, 130f, Accent);
            var stripes = NewImage("BandStripes", statusRoot, CanvasW * 0.5f, 75f, CanvasW + 1000f, 130f, WithAlpha(Panel, 0.10f), stripeSprite);
            stripes.type = Image.Type.Tiled;
            NewImage("BandLineTop", statusRoot, CanvasW * 0.5f, 6f, CanvasW + 1000f, 2f, Panel);
            NewImage("BandLineBottom", statusRoot, CanvasW * 0.5f, 144f, CanvasW + 1000f, 2f, Panel);
            statusEn = Label("En", statusRoot, CanvasW * 0.5f, 62f, 1400f, 100f, "", 88, Panel, TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
            Skew(statusEn, 0.15f);
            statusJa = Label("Ja", statusRoot, CanvasW * 0.5f, 120f, 1400f, 26f, "", 22, Panel, TextAnchor.MiddleCenter, font, FontStyle.Bold);
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
            areaNumberText.text = $"AREA {areaIndex + 1:00}";
            areaTitleText.text = AreaTitle;
            ShowRound(round, true);
            SetFinalVisuals();
        }

        /// <summary>ラウンドが変わった時。タブのラウンド番号とラウンド名を切り替える。</summary>
        public void ShowRound(int round, bool instant = false)
        {
            roundIndex = round;
            var rounds = game.areas[areaIndex].rounds;
            var r = rounds[Mathf.Clamp(round, 0, rounds.Length - 1)];
            roundNameText.text = r.isBoss ? $"BOSS　{r.title}" : r.title;
            roundNameText.color = r.isBoss ? Boss : Fg;
            tabRoundText.text = $"{round + 1:00}<size=44>/{rounds.Length:00}</size>";
            nowPillText.text = r.isBoss ? "ボス" : "現在";
            nowPill.color = r.isBoss ? Boss : Panel;
            ApplyTagColor();
            if (!instant) roundPulse = 1f; // タブの番号がはずむ
        }

        /// <summary>タブのふちの色（ボスは赤、天気は水色、いつもはグレー）。</summary>
        void ApplyTagColor()
        {
            if (tabBorder == null) return;
            tabBorder.color = weatherOn ? Sky : (IsBoss ? Boss : TabBorder);
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

            // 1. 上の帯と、まん中のエリア名
            yield return Tween(0.4f, t =>
            {
                topGroup.alpha = t;
                topRoot.localScale = new Vector3(1f, EaseOut(t), 1f);
            });
            StartCoroutine(Tween(0.5f, t =>
            {
                areaTitleGroup.alpha = t;
                areaTitleGroup.transform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, EaseOut(t));
            }));

            // 2. タブが上から下りてきて、紫の線が左右に伸びる
            var tabHome = new Vector2(CanvasW * 0.5f, -(TabTop + TabH * 0.5f));
            var tabFrom = tabHome + new Vector2(0f, TabH + 20f);
            yield return Tween(0.5f, t => tabRoot.anchoredPosition = Vector2.LerpUnclamped(tabFrom, tabHome, EaseOutBack(t)));
            yield return Tween(0.4f, t => SetTopLines(EaseOut(t)));
            StartCoroutine(Tween(0.35f, t =>
            {
                topLeftGroup.alpha = t;
                iconsGroup.alpha = t;
            }));
            yield return Wait(0.9f);

            // 3. エリア名が消え、今のポイントとバーが出る
            yield return Tween(0.3f, t => areaTitleGroup.alpha = 1f - t);
            var scoreRt = (RectTransform)scoreGroup.transform;
            var scoreHome = new Vector2(CanvasW * 0.5f, -(ScoreTop + ScoreH * 0.5f));
            yield return Tween(0.45f, t =>
            {
                scoreGroup.alpha = t;
                scoreRt.anchoredPosition = scoreHome + new Vector2(-80f * (1f - EaseOut(t)), 0f);
            });

            // 4. 札と所持メダルが順に出て、レコードが下から上がる
            for (int i = 0; i < pillGroups.Count; i++)
            {
                StartCoroutine(PopIn(pillGroups[i], pillRoots[i], 0.3f));
                yield return Wait(0.1f);
            }
            StartCoroutine(PopIn(medalGroup, medalRoot, 0.3f));
            var recordHome = new Vector2(RecordX, -RecordCanvasY);
            var recordFrom = recordHome + new Vector2(0f, -RecordHiddenOffset);
            spinning = true;
            yield return Tween(0.6f, t => recordRoot.anchoredPosition = Vector2.LerpUnclamped(recordFrom, recordHome, EaseOutBack(t)));
            StartCoroutine(Tween(0.3f, t => eqGroup.alpha = t));
            yield return Wait(1.6f);

            // 5. 縮んで筐体のモニターに戻る
            yield return Tween(1.4f, t =>
            {
                float e = EaseInOut(t);
                var cam = Camera.main;
                if (cam == null) return;
                cam.transform.SetPositionAndRotation(Vector3.Lerp(closePos, playPos, e), Quaternion.Slerp(closeRot, playRot, e));
                SetLens(cam, Mathf.Lerp(0f, playLens, e));
                layoutBlend = e; // カメラが引くのに合わせて、プレイ中の配置に変える
            });
            RestoreCamera();

            FinishIntro();
        }

        /// <summary>演出の始まりの状態：背景だけ。</summary>
        void SetIntroStartVisuals()
        {
            layoutBlend = 0f; // カメラが寄っている間は画面いっぱい
            topGroup.alpha = 0f;
            topRoot.localScale = new Vector3(1f, 0f, 1f);
            tabRoot.anchoredPosition = new Vector2(CanvasW * 0.5f, -(TabTop + TabH * 0.5f) + TabH + 20f);
            SetTopLines(0f);
            topLeftGroup.alpha = 0f;
            iconsGroup.alpha = 0f;
            areaTitleGroup.alpha = 0f;
            scoreGroup.alpha = 0f;
            foreach (var g in pillGroups) g.alpha = 0f;
            foreach (var r in pillRoots) r.localScale = Vector3.zero;
            medalGroup.alpha = 0f;
            medalRoot.localScale = Vector3.zero;
            recordRoot.anchoredPosition = new Vector2(RecordX, -RecordCanvasY - RecordHiddenOffset);
            recordDisc.localRotation = Quaternion.identity;
            spinning = false;
            eqGroup.alpha = 0f;
        }

        /// <summary>すべて出そろった状態（プレイ中の画面）。</summary>
        void SetFinalVisuals()
        {
            layoutBlend = 1f;
            topGroup.alpha = 1f;
            topRoot.localScale = Vector3.one;
            tabRoot.anchoredPosition = new Vector2(CanvasW * 0.5f, -(TabTop + TabH * 0.5f));
            SetTopLines(1f);
            topLeftGroup.alpha = 1f;
            iconsGroup.alpha = 1f;
            areaTitleGroup.alpha = 0f;
            scoreGroup.alpha = 1f;
            ((RectTransform)scoreGroup.transform).anchoredPosition = new Vector2(CanvasW * 0.5f, -(ScoreTop + ScoreH * 0.5f));
            foreach (var g in pillGroups) g.alpha = 1f;
            foreach (var r in pillRoots) r.localScale = Vector3.one;
            medalGroup.alpha = 1f;
            medalRoot.localScale = Vector3.one;
            recordRoot.anchoredPosition = new Vector2(RecordX, -RecordCanvasY);
            spinning = true;
            eqGroup.alpha = 1f;
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

            ApplyContentLayout(); // 再生中に Inspector で contentTop を変えても反映する

            if (spinning)
            {
                float spin = -game.monitor.recordSpinSpeed * Time.deltaTime;
                recordDisc.Rotate(0f, 0f, spin);
                smallRecord.Rotate(0f, 0f, spin);
            }

            // ラウンドが変わった時に、タブの番号がはずむ
            roundPulse = Mathf.MoveTowards(roundPulse, 0f, Time.deltaTime * 2.5f);
            float ps = 1f + 0.25f * roundPulse * roundPulse;
            tabRoundText.rectTransform.localScale = new Vector3(ps, ps, 1f);

            UpdateScore();
            UpdateCounters();
            UpdateEqualizer();
            UpdateStatus();
            UpdateStage();   // MonitorDisplay.Stage.cs
            UpdateRecordScreen(); // MonitorDisplay.RecordScreen.cs
            UpdateEffects(); // MonitorDisplay.Effects.cs
            UpdateBoss();    // MonitorDisplay.Boss.cs
            UpdateShop();    // MonitorDisplay.Shop.cs
        }

        /// <summary>まん中：今のポイントとノルマのバー（ボス戦は残りHP）。</summary>
        void UpdateScore()
        {
            int quota = Mathf.Max(1, game.CurrentQuota);
            bool boss = game.IsBossRound;
            int shown;
            float ratio;
            if (boss)
            {
                shown = Mathf.Max(0, game.BossHp);
                ratio = Mathf.Clamp01((float)game.BossHp / quota);
                targetText.text = $"{quota:N0} <size=30>HP</size>";
            }
            else
            {
                shown = game.Points;
                ratio = Mathf.Clamp01((float)game.Points / quota);
                targetText.text = $"{quota:N0} <size=30>目標</size>";
            }
            shownRatio = Mathf.Lerp(shownRatio, ratio, 1f - Mathf.Exp(-10f * Time.deltaTime));

            float inner = BarRight - BarLeft - 14f;
            barFill.rectTransform.sizeDelta = new Vector2(inner * shownRatio, BarH - 14f);
            barFill.color = boss ? Boss : (ratio >= 1f ? Yellow : Fg);

            // 数字が変わった時に少しはずむ
            if (shown != lastScore)
            {
                if (lastScore >= 0) scorePulse = 1f;
                lastScore = shown;
                scoreText.text = shown.ToString("N0");
            }
            scorePulse = Mathf.MoveTowards(scorePulse, 0f, Time.deltaTime * 4f);
            float s = 1f + 0.08f * scorePulse * scorePulse;
            scoreText.rectTransform.localScale = new Vector3(s, s, 1f);

            // ▶ はレコードを流している間はオレンジ、効果の発動中はオレンジで点滅
            var fx = EffectSystem;
            bool loaded = fx != null && !RecordData.IsNullOrEmpty(fx.Loaded);
            bool active = fx != null && !RecordData.IsNullOrEmpty(fx.Current);
            playIcon.color = active ? Color.Lerp(Fg, Accent, 0.5f + 0.5f * Mathf.Sin(Time.time * 6f)) : loaded ? Accent : Fg;
        }

        /// <summary>左下の札（所持ポイント・特殊ポイント）と、右下の所持メダル。</summary>
        void UpdateCounters()
        {
            pointsValue.text = $"{game.AreaPoints:N0}";
            specialValue.text = $"{game.SpecialPoints:N0}";

            int medals = game.OwnedMedals;
            if (medals != lastMedals)
            {
                if (lastMedals >= 0) medalPulse = 1f;
                lastMedals = medals;
                medalValue.text = medals.ToString();
            }
            medalPulse = Mathf.MoveTowards(medalPulse, 0f, Time.deltaTime * 4f);
            float s = 1f + 0.15f * medalPulse * medalPulse;
            medalValue.rectTransform.localScale = new Vector3(s, s, 1f);
            medalValue.color = medals <= 5 ? Boss : Fg; // 残りわずか
        }

        /// <summary>
        /// レコードの左右のイコライザー。効果の発動中は大きく動き、流しているだけの時は中くらい、
        /// 何も流していない時は静かに揺れる。
        /// </summary>
        void UpdateEqualizer()
        {
            var fx = EffectSystem;
            bool playing = fx != null && !RecordData.IsNullOrEmpty(fx.Current);
            bool loaded = fx != null && !RecordData.IsNullOrEmpty(fx.Loaded);
            float t = Time.time;
            for (int i = 0; i < eqLevels.Length; i++)
            {
                float wave = Mathf.Abs(Mathf.Sin(t * (2.6f + i * 0.31f) + i * 1.7f)) * (0.55f + 0.45f * Mathf.PerlinNoise(t * 1.8f, i * 0.9f));
                float target = playing ? 1f + wave * (EqBlocks - 1) : loaded ? 1f + wave * (EqBlocks - 1) * 0.5f : 1f + wave * 1.5f;
                eqLevels[i] = Mathf.Lerp(eqLevels[i], target, 1f - Mathf.Exp(-12f * Time.deltaTime));
            }
            for (int c = 0; c < EqColumns; c++)
            {
                // 外側ほど低く（レコードに近い方が高い山になる）
                float shapeL = 0.6f + 0.4f * c / (EqColumns - 1f);
                float shapeR = 0.6f + 0.4f * (EqColumns - 1 - c) / (EqColumns - 1f);
                int levelL = Mathf.RoundToInt(eqLevels[c] * shapeL);
                int levelR = Mathf.RoundToInt(eqLevels[EqColumns + c] * shapeR);
                for (int b = 0; b < EqBlocks; b++)
                {
                    eqLeft[c, b].enabled = b < levelL;
                    eqRight[c, b].enabled = b < levelR;
                }
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

            // 丸い札（9スライスで、どの長さでも両端が丸くなる）と、そのふちだけの版
            pillSprite = MakeSlicedSprite(64, 32, (x, y) => new Color(1f, 1f, 1f, Edge(1f - Radius(x, y), 64)));
            pillRingSprite = MakeSlicedSprite(64, 32, (x, y) =>
            {
                float r = Radius(x, y);
                return new Color(1f, 1f, 1f, Mathf.Min(Edge(1f - r, 64), Edge(r - 0.86f, 64)));
            });

            // ▶（左の辺がまっすぐで、右がとがる）
            triangleSprite = MakeSprite(128, (x, y) =>
            {
                float half = 0.9f * (0.9f - x) / 1.7f;     // x=-0.8 で ±0.9、x=0.9 で 0
                float d = Mathf.Min(x + 0.8f, half - Mathf.Abs(y));
                return new Color(1f, 1f, 1f, Edge(d, 128));
            });

            // 上が広く下が狭い台形（ラウンドのタブ）
            float bottom = TabBottomW / TabTopW;
            trapezoidSprite = MakeSprite(256, (x, y) =>
            {
                float t = (y + 1f) * 0.5f;                  // 下が 0、上が 1
                float half = Mathf.Lerp(bottom, 1f, t);
                return new Color(1f, 1f, 1f, Edge(half - Mathf.Abs(x), 256));
            });

            // メダル（金色、ふちに濃い輪、左上に光）
            coinSprite = MakeSprite(256, (x, y) =>
            {
                float r = Radius(x, y);
                float a = Edge(1f - r, 256);
                Color gold = new Color(0.98f, 0.70f, 0.20f);
                Color dark = new Color(0.78f, 0.45f, 0.08f);
                Color c = r > 0.86f ? dark : (r > 0.78f ? gold * 1.08f : Color.Lerp(gold, dark, 0.25f + 0.25f * (r / 0.78f)));
                if (r < 0.78f && Mathf.Abs(x) < 0.12f && Mathf.Abs(y) < 0.42f) c = dark;   // まん中の印
                float light = Mathf.Clamp01(1f - Radius(x + 0.35f, y - 0.35f) / 0.5f) * 0.35f;
                c = Color.Lerp(c, Color.white, light);
                c.a = a;
                return c;
            });

            // ポイントの印（緑のメダル。まん中に P の形の印）
            pointCoinSprite = MakeSprite(256, (x, y) =>
            {
                float r = Radius(x, y);
                float a = Edge(1f - r, 256);
                Color green = new Color(0.36f, 0.88f, 0.48f);
                Color dark = new Color(0.10f, 0.52f, 0.24f);
                Color c = r > 0.86f ? dark : (r > 0.78f ? green * 1.08f : Color.Lerp(green, dark, 0.25f + 0.25f * (r / 0.78f)));
                if (r < 0.78f)
                {
                    // P：縦の棒と、右上の輪
                    bool stem = x > -0.30f && x < -0.12f && Mathf.Abs(y) < 0.46f;
                    float br = Radius(x + 0.02f, y - 0.18f);
                    bool bowl = br < 0.30f && br > 0.13f && x > -0.14f;
                    if (stem || bowl) c = dark;
                }
                float light = Mathf.Clamp01(1f - Radius(x + 0.35f, y - 0.35f) / 0.5f) * 0.35f;
                c = Color.Lerp(c, Color.white, light);
                c.a = a;
                return c;
            });

            // 地：上が少し明るいスレートに、ゆるい波を2本
            backgroundSprite = MakeRectSprite(300, 180, (u, v) =>
            {
                Color top = new Color32(0x36, 0x39, 0x46, 0xff);
                Color bottomColor = new Color32(0x27, 0x29, 0x33, 0xff);
                Color c = Color.Lerp(bottomColor, top, v);
                float wave1 = 0.42f + 0.10f * Mathf.Sin(u * 5.2f + 0.6f) + 0.04f * Mathf.Sin(u * 11f);
                float wave2 = 0.28f + 0.08f * Mathf.Sin(u * 4.1f + 2.4f);
                if (v < wave1) c = Color.Lerp(c, new Color32(0x3d, 0x40, 0x4e, 0xff), 0.55f);
                if (v < wave2) c = Color.Lerp(c, new Color32(0x30, 0x32, 0x3e, 0xff), 0.6f);
                return c;
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

        /// <summary>9スライス用の絵（border はふちの幅のピクセル数）。両端が丸い札やバーに使う。</summary>
        static Sprite MakeSlicedSprite(int size, int border, Func<float, float, Color> pixel)
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
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// <summary>横長などの絵。pixel には左下が (0, 0)、右上が (1, 1) の座標を渡す。</summary>
        static Sprite MakeRectSprite(int width, int height, Func<float, float, Color> pixel)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var colors = new Color[width * height];
            for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++)
                    colors[j * width + i] = pixel((i + 0.5f) / width, (j + 0.5f) / height);
            tex.SetPixels(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
