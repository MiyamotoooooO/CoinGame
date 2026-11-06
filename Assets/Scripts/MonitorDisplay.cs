using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// 筐体のモニターに映す画面。UI を RenderTexture に描き、MonitorScreen に貼る。
    ///
    /// エリアの始まりの演出（PlayIntro）：
    ///  1. カメラがモニターに寄り、モニターが画面いっぱいに映る
    ///  2. 画面上部にタイトルの背景が出る
    ///  3. 画面下のフィルムが左から右へ出る → フィルムの上にラウンドが順に並ぶ
    ///  4. レコードが右から左へ入ってきて、回り始める
    ///     同時に、タイトルの背景にエリア名、左にマスコット、その上にラウンド名が出る
    ///  5. カメラが引いて、いつものプレイ画面に戻る（モニターは筐体の中に収まる）
    ///
    /// プレイ中は、ノルマとポイント（ボス戦はHPバー）を映す。
    /// 画面は 1500×600（モニターと同じ 2.5:1）の座標で組んでいる。左上が (0, 0)。
    /// </summary>
    public partial class MonitorDisplay : MonoBehaviour
    {
        const int UiLayer = 5;               // Unity 標準の「UI」レイヤー。メインカメラには映さない
        const float CanvasW = 1500f;
        const float CanvasH = 600f;
        const float ItemStep = 340f;         // フィルム上のラウンドの間隔
        const float ItemsY = 96f;            // フィルムの上端からラウンドの並びの中心まで
        const float RecordX = 1230f;         // レコードの中心（中段の左上から）
        const float RecordY = 137f;
        static readonly Vector3 RigPosition = new Vector3(0f, -500f, 0f); // メインカメラから見えない場所

        static readonly Color BgColor = new Color(0.10f, 0.02f, 0.02f);
        static readonly Color MiddleColor = new Color(0.16f, 0.03f, 0.03f);
        static readonly Color BandColor = new Color(0.30f, 0.03f, 0.03f);
        static readonly Color LineColor = new Color(0.90f, 0.12f, 0.10f);
        static readonly Color TitleColor = new Color(1.00f, 0.55f, 0.15f);

        MedalPusherGame game;
        RenderTexture renderTexture;
        Renderer screenRenderer;
        Material screenMaterial;
        Font font;
        Sprite whiteSprite, circleSprite, ringSprite, recordSprite;

        // 部品
        RectTransform titleRoot, filmMask, itemsRoot, recordRoot, recordDisc, mascotRoot, playPanel;
        CanvasGroup titleGroup, arrowsGroup, mascotGroup, roundNameGroup, playGroup;
        Text titleText, roundNameText, playLabel, playValue, playSub;
        Image barFill;
        readonly List<RoundItem> items = new List<RoundItem>();

        class RoundItem
        {
            public RectTransform root;
            public CanvasGroup group;
            public Image highlight;
        }

        // 状態
        int areaIndex, roundIndex;
        bool spinning;
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

        // ================================================================
        // 初期化
        // ================================================================

        public void Init(MedalPusherGame game)
        {
            this.game = game;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            renderTexture = new RenderTexture(width, Mathf.RoundToInt(width / 2.5f), 24) { name = "MonitorScreen" };
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
                quad.transform.position = new Vector3(0f, game.layout.wallHeight + w * 0.3f, game.BackWallFrontZ + 0.5f);
                quad.transform.localScale = new Vector3(w, w / 2.5f, 1f);
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
            NewImage("Background", root, 750f, 300f, CanvasW, CanvasH, BgColor);

            // ---- 上段：タイトルの背景 ----
            titleRoot = NewRect("Title", root, 750f, 58f, CanvasW, 116f);
            titleGroup = titleRoot.gameObject.AddComponent<CanvasGroup>();
            NewImage("Band", titleRoot, 750f, 58f, CanvasW, 116f, BandColor);
            NewImage("EllipseOuter", titleRoot, 750f, 58f, 1260f, 104f, new Color(0.22f, 0.02f, 0.02f), circleSprite);
            NewImage("EllipseInner", titleRoot, 750f, 58f, 1000f, 84f, new Color(0.11f, 0.01f, 0.01f), circleSprite);
            NewImage("LineBottom", titleRoot, 750f, 114f, CanvasW, 4f, LineColor);
            titleText = NewText("TitleText", titleRoot, 750f, 58f, 1000f, 80f, "", 52, TitleColor);

            // ---- 中段：レコード・マスコット・ラウンド名・プレイ中の表示（はみ出しは切る） ----
            var middle = NewRect("Middle", root, 750f, 257f, CanvasW, 274f);
            middle.gameObject.AddComponent<RectMask2D>();
            NewImage("MiddleBg", middle, 750f, 137f, CanvasW, 274f, MiddleColor);

            recordRoot = NewRect("Record", middle, RecordX, RecordY, 0f, 0f);
            NewImage("Ring1", recordRoot, 0f, 0f, 720f, 720f, new Color(0.55f, 0.05f, 0.05f), ringSprite);
            NewImage("Ring2", recordRoot, 0f, 0f, 640f, 640f, new Color(0.80f, 0.10f, 0.08f), ringSprite);
            recordDisc = NewImage("Disc", recordRoot, 0f, 0f, 540f, 540f, Color.white, recordSprite).rectTransform;

            BuildMascot(middle);

            roundNameText = NewText("RoundName", middle, 330f, 48f, 620f, 60f, "", 42, Color.white);
            roundNameGroup = roundNameText.gameObject.AddComponent<CanvasGroup>();

            BuildPlayPanel(middle);

            // ---- 下段：フィルム（左から右へ出す） ----
            var film = NewRect("Film", root, 750f, 498f, CanvasW, 204f);
            filmMask = NewRect("FilmMask", film, 0f, 0f, 0f, 0f);
            filmMask.anchorMin = new Vector2(0f, 0f);
            filmMask.anchorMax = new Vector2(0f, 1f);
            filmMask.pivot = new Vector2(0f, 0.5f);
            filmMask.anchoredPosition = Vector2.zero;
            filmMask.sizeDelta = new Vector2(CanvasW, 0f);
            filmMask.gameObject.AddComponent<RectMask2D>();

            NewImage("FilmBg", filmMask, 750f, 102f, CanvasW, 204f, Color.black);
            NewImage("LineTop", filmMask, 750f, 2f, CanvasW, 4f, LineColor);
            NewImage("LineBottom", filmMask, 750f, 202f, CanvasW, 4f, LineColor);
            for (float x = 30f; x < CanvasW; x += 96f)
            {
                NewImage("Hole", filmMask, x + 25f, 22f, 50f, 16f, new Color(0.55f, 0.55f, 0.55f));
                NewImage("Hole", filmMask, x + 25f, 182f, 50f, 16f, new Color(0.55f, 0.55f, 0.55f));
            }
            itemsRoot = NewRect("Rounds", filmMask, 750f, ItemsY, 0f, 0f);

            var arrows = NewRect("Arrows", film, 750f, 102f, CanvasW, 204f);
            arrowsGroup = arrows.gameObject.AddComponent<CanvasGroup>();
            NewImage("ArrowL", arrows, 52f, 87f, 64f, 64f, new Color(0.75f, 0.75f, 0.75f), circleSprite);
            NewText("ArrowLText", arrows, 50f, 87f, 64f, 64f, "◀", 30, new Color(0.15f, 0.15f, 0.15f));
            NewImage("ArrowR", arrows, 1448f, 87f, 64f, 64f, new Color(0.85f, 0.15f, 0.12f), circleSprite);
            NewText("ArrowRText", arrows, 1450f, 87f, 64f, 64f, "▶", 30, Color.white);

            BuildEffectLayer(root); // レコードの演出の文字（MonitorDisplay.Effects.cs）
            BuildShopPage(root); // ショップの画面（MonitorDisplay.Shop.cs）。普段は隠しておく
        }

        /// <summary>左のマスコット。画像が無ければ仮の図形を出す。</summary>
        void BuildMascot(RectTransform middle)
        {
            mascotRoot = NewRect("Mascot", middle, 270f, 170f, 240f, 200f);
            mascotGroup = mascotRoot.gameObject.AddComponent<CanvasGroup>();

            if (game.monitor.mascot != null)
            {
                var raw = NewRect("MascotImage", mascotRoot, 120f, 100f, 200f, 200f).gameObject.AddComponent<RawImage>();
                raw.texture = game.monitor.mascot;
                raw.raycastTarget = false;
                return;
            }
            NewImage("Sofa", mascotRoot, 120f, 170f, 240f, 50f, new Color(0.25f, 0.45f, 0.20f));
            NewImage("Body", mascotRoot, 120f, 130f, 90f, 80f, new Color(0.30f, 0.50f, 0.85f));
            NewImage("Head", mascotRoot, 120f, 62f, 100f, 100f, new Color(0.90f, 0.75f, 0.90f), circleSprite);
            NewImage("EyeL", mascotRoot, 102f, 66f, 12f, 16f, Color.black, circleSprite);
            NewImage("EyeR", mascotRoot, 138f, 66f, 12f, 16f, Color.black, circleSprite);
            NewImage("Phones", mascotRoot, 66f, 64f, 26f, 40f, new Color(0.90f, 0.15f, 0.12f), circleSprite);
            NewText("Label", mascotRoot, 120f, 186f, 240f, 30f, "マスコット（仮）", 20, Color.white);
        }

        /// <summary>中央：プレイ中のノルマとポイント（ボス戦はHPバー）。</summary>
        void BuildPlayPanel(RectTransform middle)
        {
            playPanel = NewRect("PlayPanel", middle, 700f, 150f, 520f, 210f);
            playGroup = playPanel.gameObject.AddComponent<CanvasGroup>();
            NewImage("PanelBg", playPanel, 260f, 105f, 520f, 210f, new Color(0f, 0f, 0f, 0.45f));
            playLabel = NewText("Label", playPanel, 260f, 34f, 500f, 44f, "", 32, TitleColor);
            NewImage("BarBg", playPanel, 260f, 96f, 460f, 38f, new Color(0.05f, 0.05f, 0.05f));
            barFill = NewImage("BarFill", playPanel, 260f, 96f, 452f, 30f, Color.yellow, whiteSprite);
            barFill.type = Image.Type.Filled;
            barFill.fillMethod = Image.FillMethod.Horizontal;
            barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            playValue = NewText("Value", playPanel, 260f, 150f, 500f, 50f, "", 38, Color.white);
            playSub = NewText("Sub", playPanel, 260f, 190f, 500f, 30f, "", 22, new Color(0.8f, 0.8f, 0.8f));
        }

        void RebuildRoundItems()
        {
            foreach (var item in items) Destroy(item.root.gameObject);
            items.Clear();

            var rounds = game.areas[areaIndex].rounds;
            for (int i = 0; i < rounds.Length; i++)
            {
                var r = rounds[i];
                // フィルムの穴（上 y=14〜30、下 y=174〜190）に重ならないよう、サムネイルは y=35〜139、名前は y=157 付近
                var root = NewRect($"Round{i + 1}", itemsRoot, i * ItemStep, 0f, 300f, 150f);
                var item = new RoundItem { root = root, group = root.gameObject.AddComponent<CanvasGroup>() };
                item.highlight = NewImage("Highlight", root, 150f, 66f, 264f, 104f, LineColor);
                NewImage("Frame", root, 150f, 66f, 254f, 94f, new Color(0.06f, 0.16f, 0.18f));
                if (r.thumbnail != null)
                {
                    var raw = NewRect("Thumb", root, 150f, 66f, 224f, 82f).gameObject.AddComponent<RawImage>();
                    raw.texture = r.thumbnail;
                    raw.raycastTarget = false;
                }
                else
                {
                    NewImage("Thumb", root, 150f, 66f, 224f, 82f, r.placeholderColor);
                    NewText("ThumbText", root, 150f, 66f, 224f, 82f, r.isBoss ? "BOSS" : $"ROUND {i + 1}", 30, Color.white);
                }
                NewText("Title", root, 150f, 136f, ItemStep, 30f, r.title, 24, Color.white);
                items.Add(item);
            }
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
            RebuildRoundItems();
            ShowRound(round, true);
            SetFinalVisuals();
        }

        /// <summary>ラウンドが変わった時。フィルムを送り、ラウンド名と表示を切り替える。</summary>
        public void ShowRound(int round, bool instant = false)
        {
            roundIndex = round;
            var rounds = game.areas[areaIndex].rounds;
            var r = rounds[Mathf.Clamp(round, 0, rounds.Length - 1)];
            roundNameText.text = r.title;
            for (int i = 0; i < items.Count; i++) items[i].highlight.enabled = i == round;
            if (instant) itemsRoot.anchoredPosition = new Vector2(ItemsTargetX, -ItemsY);
            barFill.color = r.isBoss ? new Color(0.90f, 0.15f, 0.12f) : new Color(1.00f, 0.80f, 0.20f);
        }

        float ItemsTargetX => 750f - roundIndex * ItemStep;

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

            // 1. タイトルの背景
            yield return Tween(0.45f, t =>
            {
                titleGroup.alpha = t;
                titleRoot.localScale = new Vector3(1f, EaseOut(t), 1f);
            });

            // 2. フィルムが左から右へ
            yield return Tween(0.9f, t => filmMask.sizeDelta = new Vector2(CanvasW * EaseInOut(t), 0f));

            // 3. ラウンドを順に並べる
            foreach (var item in items)
            {
                StartCoroutine(PopIn(item.group, item.root, 0.3f));
                yield return Wait(0.18f);
            }
            StartCoroutine(Tween(0.3f, t => arrowsGroup.alpha = t));
            yield return Wait(0.35f);

            // 4. レコードが右から左へ
            var target = new Vector2(RecordX, -RecordY);
            var from = new Vector2(RecordX + 900f, -RecordY);
            yield return Tween(0.7f, t => recordRoot.anchoredPosition = Vector2.LerpUnclamped(from, target, EaseOutBack(t)));

            // 5. 回り始めると同時に、エリア名・マスコット・ラウンド名
            spinning = true;
            titleText.text = game.areas[areaIndex].title;
            StartCoroutine(Tween(0.5f, t => titleText.color = new Color(TitleColor.r, TitleColor.g, TitleColor.b, t)));
            StartCoroutine(PopIn(mascotGroup, mascotRoot, 0.4f));
            yield return Wait(0.25f);
            StartCoroutine(Tween(0.4f, t => roundNameGroup.alpha = t));
            yield return Wait(1.8f);

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
            yield return Tween(0.3f, t => playGroup.alpha = t);

            FinishIntro();
        }

        /// <summary>演出の始まりの状態：背景だけ。</summary>
        void SetIntroStartVisuals()
        {
            titleGroup.alpha = 0f;
            titleRoot.localScale = new Vector3(1f, 0f, 1f);
            titleText.text = "";
            filmMask.sizeDelta = new Vector2(0f, 0f);
            foreach (var item in items)
            {
                item.group.alpha = 0f;
                item.root.localScale = Vector3.zero;
            }
            arrowsGroup.alpha = 0f;
            recordRoot.anchoredPosition = new Vector2(RecordX + 900f, -RecordY);
            recordDisc.localRotation = Quaternion.identity;
            spinning = false;
            mascotGroup.alpha = 0f;
            roundNameGroup.alpha = 0f;
            playGroup.alpha = 0f;
        }

        /// <summary>すべて出そろった状態（プレイ中の画面）。</summary>
        void SetFinalVisuals()
        {
            titleGroup.alpha = 1f;
            titleRoot.localScale = Vector3.one;
            titleText.text = game.areas[areaIndex].title;
            titleText.color = TitleColor;
            filmMask.sizeDelta = new Vector2(CanvasW, 0f);
            foreach (var item in items) item.root.localScale = Vector3.one;
            arrowsGroup.alpha = 1f;
            recordRoot.anchoredPosition = new Vector2(RecordX, -RecordY);
            spinning = true;
            mascotGroup.alpha = 1f;
            mascotRoot.localScale = Vector3.one;
            roundNameGroup.alpha = 1f;
            playGroup.alpha = 1f;
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

            // 画面の横幅がちょうど収まる距離（縦は上下に少し筐体が見える）
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

            // ラウンドが変わった時にフィルムを送る
            var pos = itemsRoot.anchoredPosition;
            pos.x = Mathf.Lerp(pos.x, ItemsTargetX, 1f - Mathf.Exp(-6f * Time.deltaTime));
            itemsRoot.anchoredPosition = new Vector2(pos.x, -ItemsY);

            // 今のラウンド以外は少し暗く（演出中は PopIn に任せる）
            if (introRoutine == null)
                for (int i = 0; i < items.Count; i++) items[i].group.alpha = i == roundIndex ? 1f : 0.55f;

            UpdatePlayPanel();
            UpdateEffects();
            UpdateShop();
        }

        void UpdatePlayPanel()
        {
            int quota = Mathf.Max(1, game.CurrentQuota);
            if (game.IsBossRound)
            {
                playLabel.text = "BOSS";
                barFill.fillAmount = (float)game.BossHp / quota;
                playValue.text = $"HP {game.BossHp} / {quota}";
            }
            else
            {
                playLabel.text = "ノルマ";
                barFill.fillAmount = Mathf.Clamp01((float)game.Points / quota);
                playValue.text = $"{game.Points} / {quota} pt";
            }

            string sub = $"残り投入 {Mathf.Max(0, game.InsertLimit - game.InsertedThisRound)}　所持 {game.AreaPoints} pt";
            switch (game.State)
            {
                case GameState.Cleared: sub = game.IsLastRoundOfArea ? "エリアクリア！" : "クリア！"; break;
                case GameState.Failed: sub = "失敗…"; break;
                case GameState.Shop: sub = "ショップ"; break;
                case GameState.Finished: sub = "おしまい（仮）"; break;
            }
            playSub.text = sub;
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

        // ---- 仮の絵（コードで描く） ----

        void CreateSprites()
        {
            whiteSprite = MakeSprite(4, (x, y) => Color.white);
            circleSprite = MakeSprite(256, (x, y) => new Color(1f, 1f, 1f, Edge(1f - Radius(x, y), 256)));
            ringSprite = MakeSprite(512, (x, y) =>
            {
                float r = Radius(x, y);
                return new Color(1f, 1f, 1f, Mathf.Min(Edge(1f - r, 512), Edge(r - 0.9f, 512)));
            });
            recordSprite = MakeSprite(512, RecordPixel);
        }

        /// <summary>レコード：黒い盤と溝、赤いふち、仮のラベル（回っているのが分かるよう色分け）。</summary>
        static Color RecordPixel(float x, float y)
        {
            float r = Radius(x, y);
            float a = Edge(1f - r, 512);
            if (r > 0.95f) return new Color(0.75f, 0.08f, 0.08f, a);
            if (r > 0.36f)
            {
                float groove = 0.07f + 0.04f * Mathf.Sin(r * 260f);
                float shine = Mathf.Abs(x - y) < 0.12f ? 0.06f : 0f; // 回ると流れて見える反射
                float g = groove + shine;
                return new Color(g, g, g, a);
            }
            if (r < 0.035f) return Color.black;
            bool quarter = (x > 0f) == (y > 0f);
            var label = quarter ? new Color(0.95f, 0.30f, 0.20f) : new Color(0.20f, 0.55f, 0.85f);
            return r > 0.33f ? new Color(0.95f, 0.85f, 0.60f) : label;
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
    }
}
