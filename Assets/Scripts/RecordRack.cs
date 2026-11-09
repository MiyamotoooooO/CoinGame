using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// 所持レコードの棚（画面の右はし）。E キーの一覧画面（RecordCollectionScreen）の代わり。
    ///
    /// ・ふだんは右はしに細いバー（つまみ）だけが出ている。クリック（または E キー）で左へ広がる。Esc か、もう一度クリックで閉じる
    ///   つまみには E のキーの絵を付け、カーソルを合わせた時と、遊び始めてしばらくの間は「E キーで開く」の札を出す
    /// ・大きさは uiScale でまとめて変えられる（盤面が見えにくい時は小さく）
    /// ・棚は細長い形で、画面の上から topRatio の所から下のはしまで：上にプレーヤー、その下にゲージ、下に所持レコード（2列）
    /// ・広がった棚：右の列に所持レコード、左にレコードプレーヤーの欄、その下にグルーヴゲージ
    /// ・所持レコードをプレーヤーの欄へドラッグするとセットされ、曲が流れる（RecordEffectSystem.Play）。盤が回る
    /// ・セットしたレコードは、Q キー（またはプレーヤーの盤をクリック）で効果を発動する（グルーヴゲージを使う）
    /// ・プレーヤーの下に、今のゲージで発動できるか（できる／あといくつ足りない／発動中）を出す
    /// ・レコードにカーソルを合わせると、ショップと同じ枠で説明を出す
    /// ・棚の上にカーソルがある間とドラッグ中は、メダルの投入を止める（クリックでメダルが出ないように）
    /// ・ショップと、エリアの始まりの演出の間は隠す
    ///
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。
    /// 付いた時は、前の一覧画面（RecordCollectionScreen）を止める（E キーが二重に効かないように）。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecordRack : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        public RecordEffectSystem effectSystem;
        public RecordArtwork artwork;

        [Header("大きさ")]
        [Tooltip("棚全体の大きさ（1 で元の大きさ）。盤面が見えにくい時は小さく。再生中に変えても反映する")]
        [Range(0.4f, 1f)] public float uiScale = 0.7f;
        [Tooltip("棚の上はしの位置（画面の高さに対する、上からの割合）。棚はここから画面の下はしまで伸びる")]
        [Range(0f, 0.8f)] public float topRatio = 0.28f;
        [Tooltip("遊び始めてから「E キーで開く」の札を出しておく秒数")]
        public float hintSeconds = 10f;

        [Header("動き")]
        [Tooltip("開く・閉じるのにかかる秒数")]
        [Range(0.05f, 1f)] public float slideSeconds = 0.25f;
        [Tooltip("セットしたレコードの回る速さ（度/秒）。200 で筐体のターンテーブル（33.3 回/分）と同じ")]
        public float spinSpeed = 200f;
        [Tooltip("効果の発動中の回る速さの倍率（1 で筐体のターンテーブルと同じ速さのまま）")]
        [Range(1f, 3f)] public float activeSpinMultiplier = 1f;

        /// <summary>棚を開いているか</summary>
        public bool IsOpen { get; private set; }

        // 大きさ（画面の基準 1920×1080）
        const float TabW = 46f;
        const float PanelW = 320f;
        const float TabH = 580f;          // つまみの高さ（縦のまん中に置く）
        const float SlotSize = 288f;
        const float SlotX = 16f;          // パネルの中の、プレーヤーの欄の左上
        const float SlotY = 16f;
        const float ListX = 16f;          // 所持レコードの欄（プレーヤーとゲージの下）
        const float ListTop = 520f;
        const float ListW = 288f;
        const float ItemSize = 120f;
        const float ItemGap = 12f;
        const int ListColumns = 2;
        float panelH = 1080f;             // 棚の高さ（画面の上から下まで。毎フレーム合わせる）
        RectTransform panelFace, listFrame, listView;

        // 色（明るい枠に、濃い文字。プレーヤーは木と黒）
        static readonly Color FrameColor = new Color32(0xee, 0xec, 0xe4, 0xff);
        static readonly Color FrameEdge = new Color32(0x3a, 0x38, 0x40, 0xff);
        static readonly Color InnerColor = new Color32(0xdc, 0xd9, 0xcf, 0xff);
        static readonly Color TextDark = new Color32(0x2a, 0x28, 0x30, 0xff);
        static readonly Color TextMute = new Color32(0x6e, 0x6b, 0x74, 0xff);
        static readonly Color DeckColor = new Color32(0x4a, 0x3a, 0x34, 0xff);
        static readonly Color PlatterColor = new Color32(0x2c, 0x2c, 0x31, 0xff);
        static readonly Color ArmColor = new Color32(0xe6, 0xe2, 0xd8, 0xff);
        static readonly Color GaugeFill = new Color32(0xff, 0xc9, 0x3c, 0xff);
        static readonly Color OkColor = new Color32(0x3c, 0xb8, 0x5a, 0xff);
        static readonly Color NgColor = new Color32(0xd9, 0x46, 0x46, 0xff);
        static readonly Color ActiveColor = new Color32(0xff, 0x8a, 0x1f, 0xff);
        static readonly Color DropColor = new Color32(0xc6, 0xee, 0x3a, 0xff);
        static readonly Color CostBadge = new Color32(0xd9, 0x8a, 0x4a, 0xff);

        RecordInventory inventory;
        Font font;
        Sprite whiteSprite, circleSprite, discSprite, triangleSprite;
        Canvas canvas;
        RectTransform canvasRect, rackRoot, panel, tabArrow, listContent, tooltip, ghost;
        CanvasGroup rackGroup;
        float slide;                       // 0：閉じている、1：開いている

        // つまみ（閉じている時に見える細いバー）
        RectTransform tabRect, keyCap, hintRoot;
        CanvasGroup hintGroup;
        Text hintText;
        float startTime;
        readonly List<RawImage> tabThumbs = new List<RawImage>();
        readonly List<Image> tabThumbBgs = new List<Image>();

        // プレーヤー
        RectTransform slotRoot, discRoot, armRoot;
        Image slotDropFrame, labelBg;
        RawImage labelJacket;
        Text slotEmptyText, slotNameText, removeText;
        GameObject removeButton;
        float armAngle;

        // ゲージ
        Image gaugeFill, gaugeMarker, statusBg;
        Text statusText, gaugeText;

        // 所持レコード
        readonly List<RackItem> items = new List<RackItem>();
        int listedCount = -1;
        Text listHeader;

        // 説明
        Text tipName, tipSub, tipEffect, tipStatus;
        RackItem hovered;

        // ドラッグ
        RackItem dragging;
        RawImage ghostJacket;
        Image ghostBg;
        Text ghostInitial;

        // メッセージ（セットできなかった理由など）
        Text messageText;
        float messageTimer;

        bool launcherBlocked;

        /// <summary>シーンに無ければ、RecordEffectSystem の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<RecordRack>() != null) return;
            var system = FindFirstObjectByType<RecordEffectSystem>();
            if (system != null) system.gameObject.AddComponent<RecordRack>();
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (effectSystem == null) effectSystem = FindFirstObjectByType<RecordEffectSystem>();
            if (artwork == null) artwork = FindFirstObjectByType<RecordArtwork>();
            inventory = game != null && game.Shop != null ? game.Shop.Inventory : FindFirstObjectByType<RecordInventory>();

            // 前の一覧画面は止める（E キーが二重に効かないように）
            foreach (var old in FindObjectsByType<RecordCollectionScreen>(FindObjectsSortMode.None)) old.enabled = false;

            EnsureEventSystem();
            CreateSprites();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
            slide = 0f;
            startTime = Time.unscaledTime;
            ApplySlide();
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        // ================================================================
        // 組み立て
        // ================================================================

        void Build()
        {
            var canvasGo = new GameObject("RecordRack");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40; // ドット化した画面とモニターより上、クリア後の帯（60）より下
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasRect = (RectTransform)canvasGo.transform;

            // 右はしの真ん中に置く。開くと左へ出てくる（つまみはいつも見えている）
            rackRoot = NewRect("Rack", canvasRect, Vector2.zero, new Vector2(TabW + PanelW, panelH));
            rackRoot.anchorMin = rackRoot.anchorMax = new Vector2(1f, 0f);   // 右下に置いて、上へ伸ばす
            rackRoot.pivot = new Vector2(1f, 0f);
            rackGroup = rackRoot.gameObject.AddComponent<CanvasGroup>();

            BuildTab();
            BuildHint();
            BuildPanel();
            BuildTooltip();
            BuildGhost();
        }

        /// <summary>つまみ：細い枠に、開く向きの矢印と、所持レコードの小さなジャケット。</summary>
        void BuildTab()
        {
            var tab = NewImage("Tab", rackRoot, new Vector2(TabW * 0.5f, -panelH * 0.5f), new Vector2(TabW, TabH), FrameEdge);
            tabRect = tab.rectTransform;
            var inner = NewImage("TabInner", tab.rectTransform, new Vector2(TabW * 0.5f, -TabH * 0.5f), new Vector2(TabW - 8f, TabH - 8f), FrameColor);
            inner.rectTransform.anchorMin = Vector2.zero;   // つまみの高さが変わっても、ふち 4 を残して合わせる
            inner.rectTransform.anchorMax = Vector2.one;
            inner.rectTransform.offsetMin = new Vector2(4f, 4f);
            inner.rectTransform.offsetMax = new Vector2(-4f, -4f);
            var button = tab.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SetOpen(!IsOpen));

            float mid = (TabW - 8f) * 0.5f;
            tabArrow = NewImage("Arrow", inner.rectTransform, new Vector2(mid, -24f), new Vector2(16f, 20f), TextDark, triangleSprite).rectTransform;

            // E のキーの絵（E キーで開けることが一目で分かるように）
            keyCap = NewImage("KeyCap", inner.rectTransform, new Vector2(mid, -62f), new Vector2(34f, 36f), TextDark).rectTransform;
            NewImage("KeyFace", keyCap, new Vector2(17f, -16f), new Vector2(28f, 28f), Color.white);
            var key = NewText("Key", keyCap, new Vector2(17f, -16f), new Vector2(28f, 28f), "E", 22, TextDark);
            key.fontStyle = FontStyle.Bold;

            var label = NewText("Label", inner.rectTransform, new Vector2(mid, -150f), new Vector2(30f, 100f), "Ｒ\nＥ\nＣ\nＯ\nＲ\nＤ", 14, TextDark);
            label.lineSpacing = 0.85f;
            for (int i = 0; i < 6; i++)
            {
                var bg = NewImage($"Thumb{i + 1}", inner.rectTransform, new Vector2(mid, -230f - i * 40f), new Vector2(32f, 32f), InnerColor);
                var jacket = NewRaw("Jacket", bg.rectTransform, new Vector2(16f, -16f), new Vector2(30f, 30f));
                tabThumbBgs.Add(bg);
                tabThumbs.Add(jacket);
            }
        }

        /// <summary>つまみの左に出す「E キーで開く」の札。</summary>
        void BuildHint()
        {
            hintRoot = NewImage("Hint", rackRoot, new Vector2(-96f, -(panelH * 0.5f - TabH * 0.5f + 66f)), new Vector2(180f, 44f), new Color(0.1f, 0.1f, 0.12f, 0.85f)).rectTransform;
            hintRoot.GetComponent<Image>().raycastTarget = false;
            hintGroup = hintRoot.gameObject.AddComponent<CanvasGroup>();
            hintGroup.blocksRaycasts = false;
            hintText = NewText("Text", hintRoot, new Vector2(90f, -22f), new Vector2(180f, 44f), "E キーで開く", 20, Color.white);
            hintText.fontStyle = FontStyle.Bold;
            var tip = NewImage("Tip", hintRoot, new Vector2(186f, -22f), new Vector2(12f, 16f), new Color(0.1f, 0.1f, 0.12f, 0.85f), triangleSprite);
            tip.raycastTarget = false;
        }

        void BuildPanel()
        {
            panel = NewImage("Panel", rackRoot, new Vector2(TabW + PanelW * 0.5f, -panelH * 0.5f), new Vector2(PanelW, panelH), FrameEdge).rectTransform;
            panelFace = NewImage("Face", panel, new Vector2(PanelW * 0.5f, -panelH * 0.5f), new Vector2(PanelW - 8f, panelH - 8f), FrameColor).rectTransform;

            BuildSlot();
            BuildGauge();
            BuildList();

            messageText = NewText("Message", panel, new Vector2(SlotX + SlotSize * 0.5f, -(ListTop - 20f)), new Vector2(SlotSize + 20f, 30f), "", 16, NgColor);
            messageText.fontStyle = FontStyle.Bold;
        }

        /// <summary>プレーヤーの欄：木の台・黒い皿・盤（ラベルにジャケット）・アーム。</summary>
        void BuildSlot()
        {
            slotRoot = NewImage("Player", panel, new Vector2(SlotX + SlotSize * 0.5f, -(SlotY + SlotSize * 0.5f)), new Vector2(SlotSize, SlotSize), DeckColor).rectTransform;
            var hit = slotRoot.gameObject.AddComponent<Button>(); // セットしたレコードをクリックで発動
            hit.transition = Selectable.Transition.None;
            hit.onClick.AddListener(OnSlotClick);

            Vector2 c = new Vector2(SlotSize * 0.5f - SlotSize * 0.04f, -SlotSize * 0.5f - SlotSize * 0.02f);
            float platter = SlotSize * 0.88f, disc = SlotSize * 0.84f, label = disc * 0.4f, arm = SlotSize * 0.66f;
            NewImage("Platter", slotRoot, c, new Vector2(platter, platter), PlatterColor, circleSprite);

            // 盤（回す）
            discRoot = NewImage("Disc", slotRoot, c, new Vector2(disc, disc), Color.white, discSprite).rectTransform;
            labelBg = NewImage("Label", discRoot, new Vector2(disc * 0.5f, -disc * 0.5f), new Vector2(label, label), Color.white, circleSprite);
            labelBg.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            labelJacket = NewRaw("Jacket", labelBg.rectTransform, new Vector2(label * 0.5f, -label * 0.5f), new Vector2(label, label));
            NewImage("Hole", discRoot, new Vector2(disc * 0.5f, -disc * 0.5f), new Vector2(10f, 10f), new Color(0.1f, 0.1f, 0.1f), circleSprite);

            // アーム（右上の付け根から下へ。セットすると盤の上へ動く）
            var armBase = new Vector2(SlotSize - 30f, -34f);
            NewImage("ArmBase", slotRoot, armBase, new Vector2(30f, 30f), new Color(0.75f, 0.73f, 0.68f), circleSprite);
            armRoot = NewRect("Arm", slotRoot, armBase, new Vector2(14f, arm));
            armRoot.pivot = new Vector2(0.5f, 1f);
            armRoot.anchoredPosition = armBase;
            NewImage("Bar", armRoot, new Vector2(7f, -arm * 0.5f), new Vector2(9f, arm), ArmColor);
            NewImage("Head", armRoot, new Vector2(7f, -arm + 6f), new Vector2(20f, 30f), new Color(0.35f, 0.33f, 0.36f));

            slotEmptyText = NewText("Empty", slotRoot, new Vector2(SlotSize * 0.5f, -SlotSize * 0.5f), new Vector2(SlotSize - 40f, 80f),
                "ここにレコードを\nドラッグしてセット", 20, new Color(1f, 1f, 1f, 0.85f));
            slotEmptyText.fontStyle = FontStyle.Bold;
            slotEmptyText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);

            // ドラッグ中に、ここへ落とせることを示す枠
            slotDropFrame = NewImage("DropFrame", slotRoot, new Vector2(SlotSize * 0.5f, -SlotSize * 0.5f), new Vector2(SlotSize + 10f, SlotSize + 10f), DropColor);
            slotDropFrame.transform.SetAsFirstSibling();
            slotDropFrame.raycastTarget = false;
            slotDropFrame.enabled = false;

            // 取り出す
            var remove = NewImage("Remove", slotRoot, new Vector2(56f, -22f), new Vector2(96f, 30f), new Color(0f, 0f, 0f, 0.55f));
            var rb = remove.gameObject.AddComponent<Button>();
            rb.transition = Selectable.Transition.None;
            rb.onClick.AddListener(() => { if (effectSystem != null) effectSystem.Stop(); });
            removeText = NewText("Text", remove.rectTransform, new Vector2(48f, -15f), new Vector2(96f, 30f), "取り出す", 15, Color.white);
            removeButton = remove.gameObject;

            slotNameText = NewText("Name", panel, new Vector2(SlotX + SlotSize * 0.5f, -(SlotY + SlotSize + 24f)), new Vector2(SlotSize, 32f), "", 22, TextDark);
            slotNameText.fontStyle = FontStyle.Bold;
        }

        /// <summary>プレーヤーの下：グルーヴゲージ（必要な量の印つき）と、発動できるかの札。</summary>
        void BuildGauge()
        {
            float top = SlotY + SlotSize + 48f;
            float cx = SlotX + SlotSize * 0.5f;
            NewText("GaugeLabel", panel, new Vector2(SlotX + 52f, -(top + 12f)), new Vector2(110f, 24f), "グルーヴ", 16, TextMute).alignment = TextAnchor.MiddleLeft;
            gaugeText = NewText("GaugeValue", panel, new Vector2(SlotX + SlotSize - 80f, -(top + 12f)), new Vector2(160f, 24f), "", 16, TextDark);
            gaugeText.alignment = TextAnchor.MiddleRight;

            var bar = NewImage("GaugeBar", panel, new Vector2(cx, -(top + 40f)), new Vector2(SlotSize, 24f), new Color(0.2f, 0.2f, 0.24f));
            gaugeFill = NewImage("Fill", bar.rectTransform, new Vector2(SlotSize * 0.5f, -12f), new Vector2(SlotSize - 6f, 18f), GaugeFill);
            gaugeFill.type = Image.Type.Filled;
            gaugeFill.fillMethod = Image.FillMethod.Horizontal;
            gaugeFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            gaugeMarker = NewImage("CostMarker", bar.rectTransform, new Vector2(0f, -12f), new Vector2(4f, 32f), Color.white);

            statusBg = NewImage("Status", panel, new Vector2(cx, -(top + 92f)), new Vector2(SlotSize, 50f), OkColor);
            statusText = NewText("StatusText", statusBg.rectTransform, new Vector2(SlotSize * 0.5f, -25f), new Vector2(SlotSize - 16f, 46f), "", 20, Color.white);
            // 長い文（「ラウンドのプレイ中だけ発動できます」など）は、札に収まるように文字を小さくする
            statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusText.verticalOverflow = VerticalWrapMode.Truncate;
            statusText.resizeTextForBestFit = true;
            statusText.resizeTextMinSize = 11;
            statusText.resizeTextMaxSize = 20;
            statusText.fontStyle = FontStyle.Bold;
        }

        /// <summary>右の列：所持レコード（縦に並べる。多い時はホイールで送る）。</summary>
        void BuildList()
        {
            var frame = NewImage("ListFrame", panel, new Vector2(ListX + ListW * 0.5f, -(ListTop + 100f)), new Vector2(ListW, 200f), InnerColor);
            listFrame = frame.rectTransform;
            listHeader = NewText("ListHeader", frame.rectTransform, new Vector2(ListW * 0.5f, -18f), new Vector2(ListW, 28f), "所持レコード", 15, TextDark);
            listHeader.fontStyle = FontStyle.Bold;

            var view = NewRect("View", frame.rectTransform, new Vector2(ListW * 0.5f, -118f), new Vector2(ListW, 164f));
            listView = view;
            view.gameObject.AddComponent<RectMask2D>();
            var viewImage = view.gameObject.AddComponent<Image>(); // ホイールを受けるため
            viewImage.color = new Color(0f, 0f, 0f, 0f);

            listContent = NewRect("Content", view, Vector2.zero, new Vector2(ListW, 0f));
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.anchoredPosition = Vector2.zero;
            listContent.sizeDelta = new Vector2(0f, 0f);

            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = listContent;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
        }

        /// <summary>説明の枠（ショップと同じ見た目）。棚の左に出す。</summary>
        void BuildTooltip()
        {
            tooltip = NewRect("Tooltip", rackRoot, Vector2.zero, new Vector2(430f, 230f));
            tooltip.pivot = new Vector2(1f, 0.5f);
            var bg = tooltip.gameObject.AddComponent<Image>();
            bg.raycastTarget = false;
            var tex = Resources.Load<Texture2D>("ShopTooltipFrame");
            if (tex != null)
            {
                tex.filterMode = FilterMode.Point;
                bg.sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(18f, 18f, 18f, 18f));
                bg.type = Image.Type.Sliced;
            }
            else
            {
                bg.color = new Color(0.11f, 0.13f, 0.20f, 0.97f);
            }
            tipName = NewText("Name", tooltip, new Vector2(215f, -38f), new Vector2(380f, 34f), "", 26, Color.white);
            tipName.alignment = TextAnchor.MiddleLeft;
            tipSub = NewText("Sub", tooltip, new Vector2(215f, -70f), new Vector2(380f, 24f), "", 16, Color.white);
            tipSub.alignment = TextAnchor.MiddleLeft;
            tipEffect = NewText("Effect", tooltip, new Vector2(215f, -128f), new Vector2(380f, 80f), "", 18, new Color(0.92f, 0.92f, 0.95f));
            tipEffect.alignment = TextAnchor.UpperLeft;
            tipStatus = NewText("Status", tooltip, new Vector2(215f, -198f), new Vector2(380f, 24f), "", 16, GaugeFill);
            tipStatus.alignment = TextAnchor.MiddleLeft;
            foreach (var t in new[] { tipName, tipSub, tipEffect, tipStatus })
            {
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.supportRichText = true;
            }
            tooltip.gameObject.SetActive(false);
        }

        /// <summary>ドラッグ中にカーソルについてくるジャケット。</summary>
        void BuildGhost()
        {
            ghostBg = NewImage("DragGhost", canvasRect, Vector2.zero, new Vector2(ItemSize, ItemSize), Color.white);
            ghost = ghostBg.rectTransform;
            ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
            ghostBg.raycastTarget = false;
            ghostJacket = NewRaw("Jacket", ghost, new Vector2(ItemSize * 0.5f, -ItemSize * 0.5f), new Vector2(ItemSize, ItemSize));
            ghostInitial = NewText("Initial", ghost, new Vector2(ItemSize * 0.5f, -ItemSize * 0.5f), new Vector2(ItemSize, ItemSize), "", 44, Color.white);
            ghost.gameObject.AddComponent<CanvasGroup>().alpha = 0.85f;
            ghost.gameObject.SetActive(false);
        }

        // ================================================================
        // 開く・閉じる
        // ================================================================

        public void SetOpen(bool open)
        {
            if (open == IsOpen) return;
            IsOpen = open;
            if (!open) ShowTooltip(null);
        }

        /// <summary>棚の高さを、topRatio の位置から画面の下はしまでに合わせ、所持レコードの欄を下まで伸ばす。</summary>
        void ApplyLayout()
        {
            float h = canvasRect.rect.height * (1f - Mathf.Clamp(topRatio, 0f, 0.8f)) / Mathf.Max(0.01f, uiScale);
            if (Mathf.Abs(h - panelH) < 0.5f) return;
            panelH = h;
            rackRoot.sizeDelta = new Vector2(TabW + PanelW, h);
            // つまみは棚の上の方に置く（棚が短い時は棚に収まる高さに）
            float tabH = Mathf.Min(TabH, h - 40f);
            tabRect.sizeDelta = new Vector2(TabW, tabH);
            tabRect.anchoredPosition = new Vector2(TabW * 0.5f, -(20f + tabH * 0.5f));
            hintRoot.anchoredPosition = new Vector2(hintRoot.anchoredPosition.x, -(20f + 66f));
            panel.sizeDelta = new Vector2(PanelW, h);
            panel.anchoredPosition = new Vector2(TabW + PanelW * 0.5f, -h * 0.5f);
            panelFace.sizeDelta = new Vector2(PanelW - 8f, h - 8f);
            panelFace.anchoredPosition = new Vector2(PanelW * 0.5f, -h * 0.5f);
            float frameH = Mathf.Max(140f, h - ListTop - 16f);
            listFrame.sizeDelta = new Vector2(ListW, frameH);
            listFrame.anchoredPosition = new Vector2(ListX + ListW * 0.5f, -(ListTop + frameH * 0.5f));
            listView.sizeDelta = new Vector2(ListW, frameH - 40f);
            listView.anchoredPosition = new Vector2(ListW * 0.5f, -(36f + (frameH - 40f) * 0.5f));
        }

        void ApplySlide()
        {
            float e = slide * slide * (3f - 2f * slide);
            rackRoot.localScale = new Vector3(uiScale, uiScale, 1f);
            rackRoot.anchoredPosition = new Vector2(Mathf.Lerp(PanelW * uiScale, 0f, e), 0f); // 大きさに合わせて隠す量も変える
            tabArrow.localRotation = Quaternion.Euler(0f, 0f, IsOpen ? 0f : 180f); // 閉じている時は左向き（開く向き）
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (canvas == null) return;

            // ショップと始まりの演出の間は隠す
            bool hidden = game != null && (game.State == GameState.Intro || (game.Monitor != null && game.Monitor.IsShopOpen));
            rackGroup.alpha = Mathf.MoveTowards(rackGroup.alpha, hidden ? 0f : 1f, Time.unscaledDeltaTime * 6f);
            rackGroup.blocksRaycasts = !hidden;
            rackGroup.interactable = !hidden;
            if (hidden && IsOpen) SetOpen(false);

            if (!hidden && InputCompat.Down(GameKey.ToggleRecordList)) SetOpen(!IsOpen);
            if (IsOpen && InputCompat.Down(GameKey.Cancel)) SetOpen(false);

            slide = Mathf.MoveTowards(slide, IsOpen ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, slideSeconds));
            ApplyLayout();
            ApplySlide();
            UpdateHint(hidden);

            if (inventory == null) inventory = game != null && game.Shop != null ? game.Shop.Inventory : FindFirstObjectByType<RecordInventory>();
            if (inventory != null && inventory.Owned.Count != listedCount) RebuildList();

            UpdateItems();
            UpdatePlayer();
            UpdateGauge();
            UpdateDrag();
            UpdateLauncherBlock();

            if (messageTimer > 0f)
            {
                messageTimer -= Time.unscaledDeltaTime;
                if (messageTimer <= 0f) messageText.text = "";
            }
        }

        /// <summary>
        /// 「E キーで開く」の札：閉じている時、つまみにカーソルを合わせている間と、遊び始めてしばらくの間だけ出す。
        /// キーの絵は、閉じている間ゆっくり光る。
        /// </summary>
        void UpdateHint(bool hidden)
        {
            bool overTab = RectTransformUtility.RectangleContainsScreenPoint(tabRect, InputCompat.MousePosition(), null);
            bool early = Time.unscaledTime - startTime < hintSeconds;
            bool show = !hidden && !IsOpen && (overTab || early);
            hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            hintText.text = overTab ? "クリック／E キーで開く" : "E キーで開く";
            hintRoot.sizeDelta = new Vector2(overTab ? 250f : 180f, 44f);
            hintRoot.anchoredPosition = new Vector2(-hintRoot.sizeDelta.x * 0.5f - 6f, hintRoot.anchoredPosition.y);
            hintText.rectTransform.anchoredPosition = new Vector2(hintRoot.sizeDelta.x * 0.5f, -22f);
            hintText.rectTransform.sizeDelta = hintRoot.sizeDelta;
            hintRoot.GetChild(1).GetComponent<RectTransform>().anchoredPosition = new Vector2(hintRoot.sizeDelta.x + 6f, -22f);

            float pulse = IsOpen ? 1f : 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 4f);
            keyCap.localScale = new Vector3(pulse, pulse, 1f);
        }

        void ShowMessage(string text)
        {
            messageText.text = text;
            messageTimer = 3f;
        }

        // ---- 所持レコード ----

        void RebuildList()
        {
            foreach (var item in items) Destroy(item.gameObject);
            items.Clear();
            var owned = inventory.Owned;
            listedCount = owned.Count;
            listHeader.text = $"所持レコード {owned.Count}";

            for (int i = 0; i < owned.Count; i++)
            {
                var r = owned[i];
                // 2列に並べる
                int col = i % ListColumns, row = i / ListColumns;
                float y = 8f + row * (ItemSize + ItemGap);
                float x = (col - (ListColumns - 1) * 0.5f) * (ItemSize + ItemGap);
                var bg = NewImage($"Item_{r.key}", listContent, Vector2.zero, new Vector2(ItemSize, ItemSize),
                    RecordArtwork.CategoryColor(r.category));
                bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                bg.rectTransform.anchoredPosition = new Vector2(x, -(y + ItemSize * 0.5f));

                var item = bg.gameObject.AddComponent<RackItem>();
                item.rack = this;
                item.record = r;
                item.background = bg;
                var tex = GetJacket(r);
                item.jacket = NewRaw("Jacket", bg.rectTransform, new Vector2(ItemSize * 0.5f, -ItemSize * 0.5f), new Vector2(ItemSize - 6f, ItemSize - 6f));
                item.jacket.texture = tex;
                item.jacket.enabled = tex != null;
                var initial = NewText("Initial", bg.rectTransform, new Vector2(ItemSize * 0.5f, -ItemSize * 0.5f), new Vector2(ItemSize, ItemSize),
                    tex == null && r.name.Length > 0 ? r.name.Substring(0, 1) : "", 44, Color.white);
                initial.fontStyle = FontStyle.Bold;
                item.dim = NewImage("Dim", bg.rectTransform, new Vector2(ItemSize * 0.5f, -ItemSize * 0.5f), new Vector2(ItemSize, ItemSize), new Color(0f, 0f, 0f, 0.55f));
                item.dim.raycastTarget = false;
                item.highlight = NewImage("Highlight", bg.rectTransform, new Vector2(ItemSize * 0.5f, -ItemSize * 0.5f), new Vector2(ItemSize + 8f, ItemSize + 8f), DropColor);
                item.highlight.transform.SetAsFirstSibling();
                item.highlight.raycastTarget = false;
                item.highlight.enabled = false;

                // ゲージの札（右下）と、セット中の印（左上）
                var badge = NewImage("Cost", bg.rectTransform, new Vector2(ItemSize - 22f, -(ItemSize - 12f)), new Vector2(46f, 24f), CostBadge);
                badge.raycastTarget = false;
                item.cost = NewText("Text", badge.rectTransform, new Vector2(23f, -12f), new Vector2(46f, 24f), "", 15, Color.white);
                item.cost.fontStyle = FontStyle.Bold;
                var playing = NewImage("Playing", bg.rectTransform, new Vector2(30f, -14f), new Vector2(56f, 24f), ActiveColor);
                playing.raycastTarget = false;
                item.playingText = NewText("Text", playing.rectTransform, new Vector2(28f, -12f), new Vector2(56f, 24f), "♪ セット", 13, Color.white);
                item.playingTag = playing.gameObject;
                items.Add(item);
            }
            int rows = (owned.Count + ListColumns - 1) / ListColumns;
            listContent.sizeDelta = new Vector2(0f, 16f + rows * (ItemSize + ItemGap));

            // つまみの小さなジャケット
            for (int i = 0; i < tabThumbs.Count; i++)
            {
                bool has = i < owned.Count;
                tabThumbBgs[i].gameObject.SetActive(has);
                if (!has) continue;
                var tex = GetJacket(owned[i]);
                tabThumbBgs[i].color = RecordArtwork.CategoryColor(owned[i].category);
                tabThumbs[i].texture = tex;
                tabThumbs[i].enabled = tex != null;
            }
        }

        void UpdateItems()
        {
            foreach (var item in items)
            {
                var r = item.record;
                bool current = IsLoaded(r);
                bool canPlay = effectSystem == null || current || effectSystem.CanPlay(r, out _);
                float cost = effectSystem != null ? effectSystem.GetGaugeCost(r) : r.gaugeCost;
                bool enough = effectSystem == null || effectSystem.freePlay || effectSystem.Gauge >= cost;
                item.cost.text = $"G{cost:0}";
                item.cost.color = enough ? Color.white : new Color(1f, 0.85f, 0.85f);
                item.dim.enabled = !canPlay || current;
                item.playingTag.SetActive(current);
                item.playingText.text = current && effectSystem != null && effectSystem.IsActive ? "★ 発動" : "♪ セット";
                bool hot = item == hovered && dragging == null;
                item.highlight.enabled = hot;
                item.transform.localScale = hot ? new Vector3(1.05f, 1.05f, 1f) : Vector3.one;
            }
        }

        // ---- プレーヤー ----

        void UpdatePlayer()
        {
            var loaded = effectSystem != null ? effectSystem.Loaded : null;
            bool has = loaded != null;
            discRoot.gameObject.SetActive(has);
            slotEmptyText.gameObject.SetActive(!has);
            removeButton.SetActive(has);
            slotNameText.text = has ? loaded.name : "未セット";
            slotNameText.color = has ? TextDark : TextMute;

            if (has)
            {
                var tex = GetJacket(loaded);
                labelJacket.texture = tex;
                labelJacket.enabled = tex != null;
                labelBg.color = RecordArtwork.CategoryColor(loaded.category);
                float speed = spinSpeed * (effectSystem.IsActive ? activeSpinMultiplier : 1f);
                discRoot.Rotate(0f, 0f, -speed * Time.unscaledDeltaTime);
            }

            // アーム：セットすると盤の上へ（少し揺れる）。付け根は右上なので、時計回り（マイナスの角度）で盤の方へ倒す
            float target = has ? -(26f + (effectSystem.IsActive ? Mathf.Sin(Time.time * 9f) * 1.5f : Mathf.Sin(Time.time * 3f) * 0.4f)) : 0f;
            armAngle = Mathf.MoveTowards(armAngle, target, Time.unscaledDeltaTime * 90f);
            armRoot.localRotation = Quaternion.Euler(0f, 0f, armAngle);
        }

        void OnSlotClick()
        {
            if (dragging != null || effectSystem == null || effectSystem.Loaded == null || effectSystem.IsActive) return;
            if (!effectSystem.CanActivate(out string why))
            {
                ShowMessage(why);
                return;
            }
            effectSystem.Activate();
        }

        // ---- ゲージ ----

        void UpdateGauge()
        {
            if (effectSystem == null) return;
            float max = Mathf.Max(1f, effectSystem.gaugeMax);
            float gauge = effectSystem.Gauge;
            gaugeFill.fillAmount = Mathf.Clamp01(gauge / max);

            var loaded = effectSystem.Loaded;
            if (loaded == null)
            {
                gaugeMarker.enabled = false;
                gaugeText.text = $"{gauge:0} / {max:0}";
                SetStatus("レコードをセットしてください", new Color(0.55f, 0.53f, 0.58f));
                return;
            }

            float cost = effectSystem.GetGaugeCost(loaded);
            gaugeMarker.enabled = true;
            gaugeMarker.rectTransform.anchoredPosition = new Vector2(3f + (SlotSize - 6f) * Mathf.Clamp01(cost / max), -12f);
            gaugeText.text = $"{gauge:0} / {max:0}　必要 {cost:0}";

            if (effectSystem.IsActive)
                SetStatus($"★ 発動中　残り {effectSystem.Remaining:0}秒", ActiveColor);
            else if (!RecordEffectSystem.IsImplemented(loaded))
                SetStatus("この効果はまだありません", new Color(0.55f, 0.53f, 0.58f));
            else if (effectSystem.CanActivate(out string why))
                SetStatus("発動できます　Q キー／盤をクリック", OkColor);
            else
                SetStatus(effectSystem.freePlay || gauge >= cost ? why : $"ゲージ不足　あと {Mathf.CeilToInt(cost - gauge)}", NgColor);
        }

        void SetStatus(string text, Color color)
        {
            statusText.text = text;
            statusBg.color = color;
        }

        // ---- ドラッグ ----

        internal void BeginDrag(RackItem item)
        {
            if (effectSystem == null) return;
            dragging = item;
            ShowTooltip(null);
            var tex = item.jacket.texture;
            ghostJacket.texture = tex;
            ghostJacket.enabled = tex != null;
            ghostBg.color = RecordArtwork.CategoryColor(item.record.category);
            ghostInitial.text = tex == null && item.record.name.Length > 0 ? item.record.name.Substring(0, 1) : "";
            ghost.gameObject.SetActive(true);
            ghost.SetAsLastSibling();
        }

        internal void EndDrag(RackItem item)
        {
            if (dragging != item) return;
            dragging = null;
            ghost.gameObject.SetActive(false);
            slotDropFrame.enabled = false;
            if (OverSlot()) SetRecord(item.record);
        }

        void UpdateDrag()
        {
            if (dragging == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, InputCompat.MousePosition(), null, out Vector2 p);
            ghost.anchoredPosition = p;
            slotDropFrame.enabled = OverSlot();
        }

        bool OverSlot() => RectTransformUtility.RectangleContainsScreenPoint(slotRoot, InputCompat.MousePosition(), null);

        /// <summary>レコードをプレーヤーにセットして流す（曲だけ。発動は Q キー）。</summary>
        void SetRecord(RecordData r)
        {
            if (effectSystem == null || r == null) return;
            if (IsLoaded(r)) return;
            if (!effectSystem.CanPlay(r, out string reason))
            {
                ShowMessage(reason);
                return;
            }
            effectSystem.Play(r);
        }

        // ---- 説明 ----

        internal void Hover(RackItem item, bool enter)
        {
            if (enter) hovered = item;
            else if (hovered == item) hovered = null;
            ShowTooltip(dragging == null ? hovered : null);
        }

        void ShowTooltip(RackItem item)
        {
            if (tooltip == null) return;
            if (item == null || !IsOpen)
            {
                tooltip.gameObject.SetActive(false);
                return;
            }
            var r = item.record;
            tipName.text = r.name;
            string len = "";
            if (effectSystem != null)
            {
                float d = effectSystem.GetDuration(r);
                len = effectSystem.HasSong(r) ? $"　曲 {d:0}秒" : $"　{d:0.#}秒";
            }
            tipSub.text = $"{r.category}・{r.rarity}　ゲージ {(effectSystem != null ? effectSystem.GetGaugeCost(r) : r.gaugeCost):0}{len}";
            tipEffect.text = r.effectText;
            string status;
            if (IsLoaded(r)) status = effectSystem.IsActive ? "発動中" : "セット中（Q キーで発動）";
            else if (effectSystem != null && !effectSystem.CanPlay(r, out string reason)) status = $"<color=#FF9090>{reason}</color>";
            else status = "プレーヤーへドラッグしてセット";
            if (!RecordEffectSystem.IsImplemented(r)) status += "　<color=#FF9090>※効果は未実装</color>";
            tipStatus.text = status;

            // 棚の左、そのレコードの高さに出す（画面からはみ出さないように）
            Vector3[] corners = new Vector3[4];
            ((RectTransform)item.transform).GetWorldCorners(corners);
            float itemY = (corners[0].y + corners[1].y) * 0.5f;
            Vector3 local = rackRoot.InverseTransformPoint(new Vector3(0f, itemY, 0f));
            float half = tooltip.sizeDelta.y * 0.5f;
            float y = Mathf.Clamp(local.y, -panelH + half, -half);
            tooltip.anchoredPosition = new Vector2(TabW - 6f, y);
            tooltip.gameObject.SetActive(true);
            tooltip.SetAsLastSibling();
        }

        // ---- メダルの投入を止める ----

        /// <summary>棚の上にカーソルがある間とドラッグ中は、メダルの投入を止める。</summary>
        void UpdateLauncherBlock()
        {
            if (game == null || game.Launcher == null) return;
            Vector2 mouse = InputCompat.MousePosition();
            bool over = rackGroup.blocksRaycasts &&
                        (RectTransformUtility.RectangleContainsScreenPoint(panel, mouse, null) && slide > 0.05f ||
                         RectTransformUtility.RectangleContainsScreenPoint((RectTransform)rackRoot.GetChild(0), mouse, null));
            bool block = over || dragging != null;
            if (block && !launcherBlocked && game.Launcher.enabled)
            {
                game.Launcher.enabled = false;
                launcherBlocked = true;
            }
            else if (!block && launcherBlocked)
            {
                game.Launcher.enabled = true;
                launcherBlocked = false;
            }
        }

        // ================================================================
        // 小さな道具
        // ================================================================

        bool IsLoaded(RecordData r) => effectSystem != null && effectSystem.Loaded != null && r != null && effectSystem.Loaded.key == r.key;

        Texture2D GetJacket(RecordData r)
        {
            if (artwork == null) artwork = FindFirstObjectByType<RecordArtwork>();
            if (artwork != null) return artwork.GetJacket(r);
            return Resources.Load<Texture2D>(RecordArtwork.ResourceFolder + "/" + r.key);
        }

        /// <summary>左上が (0, 0) の座標で、中心 pos・大きさ size の四角を作る。</summary>
        static RectTransform NewRect(string name, RectTransform parent, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        Image NewImage(string name, RectTransform parent, Vector2 pos, Vector2 size, Color color, Sprite sprite = null)
        {
            var img = NewRect(name, parent, pos, size).gameObject.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : whiteSprite;
            img.color = color;
            return img;
        }

        static RawImage NewRaw(string name, RectTransform parent, Vector2 pos, Vector2 size)
        {
            var raw = NewRect(name, parent, pos, size).gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            return raw;
        }

        Text NewText(string name, RectTransform parent, Vector2 pos, Vector2 size, string text, int fontSize, Color color)
        {
            var t = NewRect(name, parent, pos, size).gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        void CreateSprites()
        {
            whiteSprite = MakeSprite(4, (x, y) => Color.white);
            circleSprite = MakeSprite(256, (x, y) => new Color(1f, 1f, 1f, Mathf.Clamp01((1f - Mathf.Sqrt(x * x + y * y)) * 128f)));
            // 黒い盤（溝と、ななめの光）
            discSprite = MakeSprite(512, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                float a = Mathf.Clamp01((1f - d) * 256f);
                float shade = 0.07f;
                if (d > 0.42f && d < 0.96f) shade += 0.03f * Mathf.Abs(Mathf.Sin(d * 160f));
                if (d > 0.94f) shade = 0.12f;
                float angle = Mathf.Atan2(y, x);
                shade += 0.07f * Mathf.Pow(Mathf.Abs(Mathf.Cos(angle - 0.8f)), 12f) * Mathf.Clamp01((d - 0.42f) * 4f);
                return new Color(shade, shade, shade, a);
            });
            // ▶（右向き。左右は回して使う）
            triangleSprite = MakeSprite(64, (x, y) =>
            {
                float half = 0.9f * (0.9f - x) / 1.7f;
                float dist = Mathf.Min(x + 0.8f, half - Mathf.Abs(y));
                return new Color(1f, 1f, 1f, Mathf.Clamp01(dist * 32f + 0.5f));
            });
        }

        static Sprite MakeSprite(int size, System.Func<float, float, Color> pixel)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var colors = new Color[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                    colors[j * size + i] = pixel((i + 0.5f) / size * 2f - 1f, (j + 0.5f) / size * 2f - 1f);
            tex.SetPixels(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>棚の中のレコード1枚（カーソルを合わせると説明、ドラッグでプレーヤーへ）。</summary>
    public class RackItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal RecordRack rack;
        internal RecordData record;
        internal Image background, dim, highlight;
        internal RawImage jacket;
        internal Text cost, playingText;
        internal GameObject playingTag;

        public void OnPointerEnter(PointerEventData e) => rack.Hover(this, true);
        public void OnPointerExit(PointerEventData e) => rack.Hover(this, false);
        public void OnBeginDrag(PointerEventData e) => rack.BeginDrag(this);
        public void OnDrag(PointerEventData e) { } // 位置は RecordRack が毎フレーム合わせる
        public void OnEndDrag(PointerEventData e) => rack.EndDrag(this);
    }
}
