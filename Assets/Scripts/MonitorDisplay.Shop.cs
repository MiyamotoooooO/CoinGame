using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// モニターに映すショップの画面（MonitorDisplay の続き）。
    ///
    /// ・開く時は、エリアの始まりの演出と同じようにカメラがモニターに寄る。閉じる時は盤面に戻る
    /// ・画面の割り振り（1500×900、左上が (0, 0)）：
    ///   - 上の大きな所：店員と店の 3D の場面（ShopStage。設定の shop.stagePrefab で差し替えられる）
    ///   - 左上：戻るボタン（ショップを閉じる）。右上：所持ポイントと特殊ポイント
    ///   - まん中の段：左に売り場のタブ（レコード／ロック中／ロック中）、右にリロール
    ///   - 下の帯：商品のカードを8枚（レコード6枚・投入上限アップ・特殊ポイントへの交換）
    /// ・カードをクリックすると買う。レコードにカーソルを合わせると、効果の説明を出す
    /// ・支払いはエリアのポイント（pt）
    /// ・売り場のタブは、今は「レコード」だけ。ほかの売り場（ロック中）は、あとで商品を足す
    /// ・モニターは RenderTexture に描いているので、マウスの位置をモニターの画面の座標に直して、ボタンを自分で判定する
    /// </summary>
    public partial class MonitorDisplay
    {
        // 色：濃い帯に、青いカード・白い帯・ライムの差し色
        static readonly Color ShopBand = new Color32(0x12, 0x14, 0x19, 0xee);
        static readonly Color ShopDark = new Color32(0x24, 0x27, 0x30, 0xf2);
        static readonly Color ShopLime = new Color32(0xc6, 0xee, 0x3a, 0xff);
        static readonly Color CardTop = new Color32(0x55, 0x96, 0xec, 0xff);
        static readonly Color CardBottom = new Color32(0x2a, 0x5c, 0xc2, 0xff);
        static readonly Color CardStrip = new Color32(0xec, 0xee, 0xf3, 0xff);
        static readonly Color CardEmpty = new Color32(0x3a, 0x3d, 0x47, 0xff);
        static readonly Color PointColor = new Color32(0x5c, 0xe0, 0x7a, 0xff);   // ポイントは緑（画面の右上の表示と同じ）
        static readonly Color SpecialColor = new Color32(0x38, 0xc8, 0xff, 0xff);
        static readonly Color BackColor = new Color32(0xff, 0x45, 0x45, 0xff);
        static readonly Color ShortColor = new Color32(0xff, 0x70, 0x70, 0xff); // ポイント不足
        static readonly Color DarkText = new Color32(0x1a, 0x1c, 0x22, 0xff);

        // カードの並び（8枚：レコード6枚＋投入上限アップ＋特殊ポイント交換）
        const int SlotCount = RecordShop.OfferCount + 2;
        const int LimitSlot = RecordShop.OfferCount;
        const int ExchangeSlot = RecordShop.OfferCount + 1;
        const float SlotW = 156f;
        const float SlotStep = 172f;
        const float SlotTop = 600f;      // カードの上端（名前の札）
        const float SlotH = 256f;        // 名前の札から値段の段まで
        const float BandTop = 586f;      // 下の帯の上端
        const float TabRowY = 540f;      // 売り場のタブとリロールの段（上下の中心）

        /// <summary>売り場のタブ。open が false なら「ロック中」（あとで中身を足す）</summary>
        static readonly (string label, bool open)[] ShopTabs = { ("レコード", true), ("ロック中", false), ("ロック中", false) };

        class ShopCard
        {
            public RectTransform root;
            public CanvasGroup group;
            public Image highlight, body, jacketBg, priceIcon;
            public RawImage jacket;
            public Text name, initial, tag, big, strip, price;
            public RectTransform emptyMark, diamond;
            public Rect area;            // カード全体（カーソルを合わせる・クリックする所）
            public Action onClick;
        }

        /// <summary>クリックできる場所（モニターの画面の座標。左上が (0, 0)）</summary>
        class Hot
        {
            public Rect rect;
            public RectTransform target;
            public Action onClick;
            public bool hovered;
        }

        RectTransform shopRoot, tooltip, messageRoot;
        Text shopPointsText, shopSpecialText, shopMessageText, rerollCostText;
        Text tipName, tipSub, tipEffect;
        Image rerollButton;
        RawImage stageView;
        ShopStage shopStage;
        Sprite cardBodySprite;
        readonly ShopCard[] cards = new ShopCard[SlotCount];
        readonly List<Hot> hots = new List<Hot>();
        Coroutine shopRoutine;
        bool shopOpen;      // ショップの画面を出している
        bool shopReady;     // カメラが寄り切って、操作できる
        float messageTimer;
        int lastPoints = -1, lastSpecial = -1;
        RecordArtwork artwork;
        bool artworkLookedUp;

        public bool IsShopOpen => shopOpen;

        // ================================================================
        // 開く・閉じる
        // ================================================================

        /// <summary>ショップを開く。カメラがモニターに寄ってから操作できるようになる。</summary>
        public void OpenShop()
        {
            if (screenRenderer == null || shopRoot == null) return;
            StopIntro(false);
            if (shopRoutine != null) StopCoroutine(shopRoutine);
            game.Shop.Changed -= RefreshShop;
            game.Shop.Changed += RefreshShop;
            shopOpen = true;
            shopReady = false;
            SetMessage("");
            lastPoints = -1;
            RefreshShop();
            ShowTooltip(-1);
            shopRoot.gameObject.SetActive(true);
            if (shopStage != null) shopStage.SetVisible(true);
            shopRoutine = StartCoroutine(ShopZoom(true, null));
        }

        /// <summary>ショップを閉じる。カメラが盤面に戻ってから onClosed を呼ぶ。</summary>
        public void CloseShop(Action onClosed)
        {
            if (!shopOpen || !shopReady) return;
            shopReady = false;
            ShowTooltip(-1);
            if (shopRoutine != null) StopCoroutine(shopRoutine);
            shopRoutine = StartCoroutine(ShopZoom(false, onClosed));
        }

        /// <summary>リセットなどで、ショップを途中でやめる（カメラもすぐ戻す）。</summary>
        void StopShop()
        {
            if (!shopOpen) return;
            if (shopRoutine != null) StopCoroutine(shopRoutine);
            shopRoutine = null;
            shopOpen = false;
            shopReady = false;
            shopRoot.gameObject.SetActive(false);
            if (shopStage != null) shopStage.SetVisible(false);
            RestoreCamera();
        }

        IEnumerator ShopZoom(bool opening, Action done)
        {
            if (opening)
            {
                ComputeCloseup();
                cameraOverridden = true;
            }
            Vector3 fromPos = opening ? playPos : closePos, toPos = opening ? closePos : playPos;
            Quaternion fromRot = opening ? playRot : closeRot, toRot = opening ? closeRot : playRot;
            float fromLens = opening ? playLens : 0f, toLens = opening ? 0f : playLens;

            float d = Mathf.Max(0.05f, game.monitor.shopZoomSeconds);
            for (float t = 0f; t < d; t += Time.deltaTime)
            {
                float e = EaseInOut(t / d);
                var cam = Camera.main;
                if (cam != null)
                {
                    cam.transform.SetPositionAndRotation(Vector3.Lerp(fromPos, toPos, e), Quaternion.Slerp(fromRot, toRot, e));
                    SetLens(cam, Mathf.Lerp(fromLens, toLens, e));
                }
                yield return null;
            }

            if (opening)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    cam.transform.SetPositionAndRotation(closePos, closeRot);
                    SetLens(cam, 0f);
                }
                shopReady = true;
            }
            else
            {
                RestoreCamera();
                shopOpen = false;
                shopRoot.gameObject.SetActive(false);
                if (shopStage != null) shopStage.SetVisible(false);
            }
            shopRoutine = null;
            done?.Invoke();
        }

        // ================================================================
        // 画面の組み立て
        // ================================================================

        void BuildShopPage(RectTransform root)
        {
            shopRoot = NewRect("Shop", root, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH);
            cardBodySprite = MakeRectSprite(4, 64, (u, v) => Color.Lerp(CardBottom, CardTop, v));

            // 3D の場面（画面いっぱい。下は帯で隠れる）
            NewImage("StageBg", shopRoot, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH, new Color(0.10f, 0.11f, 0.15f));
            stageView = NewRect("Stage", shopRoot, CanvasW * 0.5f, CanvasH * 0.5f, CanvasW, CanvasH).gameObject.AddComponent<RawImage>();
            stageView.raycastTarget = false;
            int w = Mathf.Max(320, Mathf.RoundToInt(game.monitor.resolutionWidth * 0.64f));
            shopStage = ShopStage.Create(game, w, Mathf.RoundToInt(w * CanvasH / CanvasW));
            stageView.texture = shopStage.Texture;
            NewImage("TopShade", shopRoot, CanvasW * 0.5f, 60f, CanvasW, 120f, new Color(0f, 0f, 0f, 0.25f));

            BuildShopTopBar();
            BuildShopTabs();

            // 下の帯と、カード8枚
            NewImage("Band", shopRoot, CanvasW * 0.5f, BandTop + (CanvasH - BandTop) * 0.5f, CanvasW, CanvasH - BandTop, ShopBand);
            float first = CanvasW * 0.5f - (SlotCount - 1) * 0.5f * SlotStep;
            for (int i = 0; i < SlotCount; i++) cards[i] = BuildCard(i, first + i * SlotStep);
            NewText("Hint", shopRoot, CanvasW * 0.5f, 878f, 1200f, 24f,
                "カードをクリックで購入　／　ポイントは次のエリアに持ち越せません", 16, new Color(0.70f, 0.74f, 0.82f)).GetComponent<Shadow>().enabled = false;

            // メッセージ（購入した・ポイントが足りない など）
            messageRoot = NewImage("Message", shopRoot, CanvasW * 0.5f, 470f, 760f, 56f, ShopDark, pillSprite).rectTransform;
            messageRoot.GetComponent<Image>().type = Image.Type.Sliced;
            shopMessageText = Label("MessageText", messageRoot, 380f, 28f, 740f, 56f, "", 26, PointColor, TextAnchor.MiddleCenter, font, FontStyle.Bold);

            BuildTooltip(); // 一番上に描くように最後に作る
            shopRoot.gameObject.SetActive(false);
        }

        /// <summary>左上の戻るボタンと、右上の所持ポイント・特殊ポイント。</summary>
        void BuildShopTopBar()
        {
            // 戻る（ショップを閉じる）
            var back = NewImage("Back", shopRoot, 115f, 68f, 160f, 70f, ShopDark, pillSprite);
            back.type = Image.Type.Sliced;
            var arrow = NewImage("Arrow", back.rectTransform, 42f, 35f, 28f, 32f, BackColor, triangleSprite);
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            Label("Text", back.rectTransform, 64f, 35f, 90f, 40f, "戻る", 28, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            AddHot(back.rectTransform, 115f, 68f, 160f, 70f, () => game.CloseShop());

            shopPointsText = Counter("Points", 1035f, PointColor, true);
            shopSpecialText = Counter("Special", 1335f, SpecialColor, false);
        }

        /// <summary>右上の数字の札（右に印、その左に数字）。coin が true ならメダルの印、false なら菱形。</summary>
        Text Counter(string name, float cx, Color color, bool coin)
        {
            const float w = 280f, h = 64f;
            var bg = NewImage(name, shopRoot, cx, 68f, w, h, ShopDark, pillSprite);
            bg.type = Image.Type.Sliced;
            if (coin)
            {
                NewImage("Icon", bg.rectTransform, w - 38f, h * 0.5f, 44f, 44f, Color.white, pointCoinSprite);
            }
            else
            {
                var d = NewImage("Icon", bg.rectTransform, w - 38f, h * 0.5f, 30f, 30f, color, whiteSprite);
                d.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
            Label("Label", bg.rectTransform, 22f, h * 0.5f, 80f, 30f, coin ? "pt" : "特殊", 18, Mute, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            return Label("Value", bg.rectTransform, w - 72f, h * 0.5f, 170f, h, "0", 34, Fg, TextAnchor.MiddleRight, displayFont, FontStyle.Bold);
        }

        /// <summary>まん中の段：左に売り場のタブ、右にリロール。</summary>
        void BuildShopTabs()
        {
            const float tabW = 196f;
            float barW = tabW * ShopTabs.Length + 16f;
            float barX = 60f + barW * 0.5f;
            var bar = NewImage("TabBar", shopRoot, barX, TabRowY, barW, 60f, ShopDark, pillSprite);
            bar.type = Image.Type.Sliced;
            for (int i = 0; i < ShopTabs.Length; i++)
            {
                float cx = 68f + tabW * (i + 0.5f);
                var tab = ShopTabs[i];
                if (i == 0) // 今の売り場（いまはレコードだけ）
                {
                    var fill = NewImage($"Tab{i + 1}", shopRoot, cx, TabRowY, tabW - 12f, 46f, ShopLime);
                    Skew(fill, 0.3f);
                    Label($"Tab{i + 1}Text", shopRoot, cx, TabRowY, tabW, 46f, tab.label, 26, DarkText, TextAnchor.MiddleCenter, font, FontStyle.Bold);
                }
                else
                {
                    Label($"Tab{i + 1}Text", shopRoot, cx, TabRowY, tabW, 46f, tab.label, 24, tab.open ? Fg : Mute, TextAnchor.MiddleCenter, font, FontStyle.Bold);
                    var target = NewRect($"Tab{i + 1}Hot", shopRoot, cx, TabRowY, tabW, 46f);
                    AddHot(target, cx, TabRowY, tabW, 46f, () => ShowMessage("この売り場はまだ開いていません"));
                }
                if (i > 0) NewImage("TabSep", shopRoot, 68f + tabW * i, TabRowY, 2f, 30f, Line);
            }

            // リロール（右）：くるっと回る印・「リロール」・値段
            const float rw = 400f, rh = 60f;
            float rx = CanvasW - 60f - rw * 0.5f;
            rerollButton = NewImage("Reroll", shopRoot, rx, TabRowY, rw, rh, ShopDark, pillSprite);
            rerollButton.type = Image.Type.Sliced;
            NewImage("Ring", rerollButton.rectTransform, 38f, rh * 0.5f, 30f, 30f, Fg, pillRingSprite);
            var tip = NewImage("RingArrow", rerollButton.rectTransform, 50f, 17f, 12f, 14f, Fg, triangleSprite);
            tip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -30f);
            Label("Text", rerollButton.rectTransform, 66f, rh * 0.5f, 160f, 40f, "リロール", 26, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            rerollCostText = Label("Cost", rerollButton.rectTransform, rw - 24f, rh * 0.5f, 180f, 40f, "", 28, PointColor, TextAnchor.MiddleRight, displayFont, FontStyle.Bold);
            AddHot(rerollButton.rectTransform, rx, TabRowY, rw, rh, OnReroll);
        }

        /// <summary>商品のカード1枚（上に名前の札、青い絵の所、白い帯、値段の段）。</summary>
        ShopCard BuildCard(int index, float cx)
        {
            var c = new ShopCard();
            float w = SlotW, mid = w * 0.5f;
            c.root = NewRect($"Card{index + 1}", shopRoot, cx, SlotTop + SlotH * 0.5f, w, SlotH);
            c.group = c.root.gameObject.AddComponent<CanvasGroup>();
            c.area = new Rect(cx - w * 0.5f, SlotTop, w, SlotH);

            c.highlight = NewImage("Highlight", c.root, mid, SlotH * 0.5f, w + 10f, SlotH + 10f, ShopLime);
            c.highlight.enabled = false;

            // 名前の札
            NewImage("NameBg", c.root, mid, 17f, w, 34f, ShopDark);
            c.name = Label("Name", c.root, mid, 17f, w - 10f, 30f, "", 18, Fg, TextAnchor.MiddleCenter, font, FontStyle.Bold);
            c.name.horizontalOverflow = HorizontalWrapMode.Wrap;
            c.name.resizeTextForBestFit = true;
            c.name.resizeTextMinSize = 11;
            c.name.resizeTextMaxSize = 18;

            // 青い絵の所（y 40〜190）
            c.body = NewImage("Body", c.root, mid, 115f, w, 150f, Color.white, cardBodySprite);
            c.jacketBg = NewImage("JacketBg", c.root, mid, 112f, 108f, 108f, Color.gray);
            c.initial = Label("Initial", c.root, mid, 112f, 108f, 108f, "", 48, Fg, TextAnchor.MiddleCenter, font, FontStyle.Bold);
            c.jacket = NewRect("Jacket", c.root, mid, 112f, 104f, 104f).gameObject.AddComponent<RawImage>();
            c.jacket.raycastTarget = false;
            c.diamond = NewImage("Diamond", c.root, mid, 88f, 50f, 50f, SpecialColor, whiteSprite).rectTransform;
            c.diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            c.big = Label("Big", c.root, mid, 112f, w, 60f, "", 36, Fg, TextAnchor.MiddleCenter, displayFont, FontStyle.Bold);
            c.tag = Label("Tag", c.root, 8f, 52f, w - 16f, 20f, "", 14, Fg, TextAnchor.MiddleLeft, font, FontStyle.Bold);
            c.tag.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);

            // 空のカード（×印）
            c.emptyMark = NewRect("Empty", c.root, mid, 115f, w, 150f);
            var x1 = NewImage("X1", c.emptyMark, mid, 75f, 8f, 120f, new Color(1f, 1f, 1f, 0.25f));
            x1.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 40f);
            var x2 = NewImage("X2", c.emptyMark, mid, 75f, 8f, 120f, new Color(1f, 1f, 1f, 0.25f));
            x2.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -40f);

            // 白い帯（y 190〜218）
            NewImage("StripBg", c.root, mid, 204f, w, 28f, CardStrip);
            c.strip = Label("Strip", c.root, mid, 204f, w - 8f, 26f, "", 15, DarkText, TextAnchor.MiddleCenter, font, FontStyle.Bold);

            // 値段の段（y 220〜256）
            NewImage("PriceBg", c.root, mid, 238f, w, 36f, ShopDark);
            c.priceIcon = NewImage("PriceIcon", c.root, 28f, 238f, 24f, 24f, Color.white, pointCoinSprite);
            c.price = Label("Price", c.root, w - 14f, 238f, w - 50f, 32f, "", 22, Fg, TextAnchor.MiddleRight, displayFont, FontStyle.Bold);

            if (index < RecordShop.OfferCount) c.onClick = () => OnBuy(index);
            else if (index == LimitSlot) c.onClick = OnBuyLimit;
            else c.onClick = OnExchange;
            return c;
        }

        /// <summary>レコードの効果を出す説明の枠（カードの上に出す）。</summary>
        void BuildTooltip()
        {
            tooltip = NewRect("Tooltip", shopRoot, 0f, 0f, 430f, 210f);
            var bg = tooltip.gameObject.AddComponent<Image>();
            bg.raycastTarget = false;
            var sprite = LoadTooltipSprite();
            if (sprite != null)
            {
                bg.sprite = sprite;
                bg.type = Image.Type.Sliced;
            }
            else
            {
                bg.color = new Color(0.11f, 0.13f, 0.20f, 0.97f);
            }

            tipName = NewText("Name", tooltip, 215f, 40f, 380f, 36f, "", 28, Color.white);
            tipName.alignment = TextAnchor.MiddleLeft;
            tipSub = NewText("Sub", tooltip, 215f, 74f, 380f, 26f, "", 18, Color.white);
            tipSub.alignment = TextAnchor.MiddleLeft;
            tipEffect = NewText("Effect", tooltip, 215f, 140f, 380f, 100f, "", 21, new Color(0.92f, 0.92f, 0.95f));
            tipEffect.alignment = TextAnchor.UpperLeft;
            tipEffect.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltip.gameObject.SetActive(false);
        }

        /// <summary>Resources/ShopTooltipFrame.png を、四隅と縁を伸ばさない（9分割）スプライトにする。</summary>
        static Sprite LoadTooltipSprite()
        {
            var tex = Resources.Load<Texture2D>("ShopTooltipFrame");
            if (tex == null) return null;
            tex.filterMode = FilterMode.Point;
            const float border = 18f;
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        void AddHot(RectTransform target, float cx, float cy, float w, float h, Action onClick)
        {
            hots.Add(new Hot { rect = new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h), target = target, onClick = onClick });
        }

        // ================================================================
        // 中身の更新
        // ================================================================

        void RefreshShop()
        {
            if (shopRoot == null || game.Shop == null) return;
            var shop = game.Shop;
            int points = game.AreaPoints;

            for (int i = 0; i < RecordShop.OfferCount; i++) RefreshRecordCard(cards[i], shop.Offers[i], shop.Sold[i], points);

            // 投入上限アップ
            var lc = cards[LimitSlot];
            SetCardKind(lc, false, true);
            lc.name.text = "投入上限アップ";
            lc.tag.text = "強化";
            lc.strip.text = "1ラウンドの投入数";
            if (shop.CanUpgradeLimit)
            {
                lc.big.text = $"{game.InsertLimit}→{shop.NextInsertLimit}";
                SetPrice(lc, shop.LimitUpgradeCost, points);
            }
            else
            {
                lc.big.text = $"最大{game.InsertLimit}";
                SetPriceText(lc, "最大", Mute, false);
            }

            // 特殊ポイントに交換
            var ec = cards[ExchangeSlot];
            SetCardKind(ec, false, true);
            ec.diamond.gameObject.SetActive(true);
            ec.name.text = "特殊ポイント";
            ec.tag.text = "交換";
            ec.big.text = $"+{shop.ExchangeAmount}";
            ec.big.rectTransform.anchoredPosition = new Vector2(SlotW * 0.5f, -155f); // 菱形の下
            ec.strip.text = "エリアを越えて残る";
            SetPrice(ec, shop.ExchangeCost, points);

            rerollCostText.text = $"{shop.RerollCost} pt";
            rerollCostText.color = points >= shop.RerollCost ? PointColor : ShortColor;

            shopPointsText.text = $"{points:N0}";
            shopSpecialText.text = $"{game.SpecialPoints:N0}";
            lastPoints = points;
            lastSpecial = game.SpecialPoints;
        }

        void RefreshRecordCard(ShopCard c, RecordData r, bool sold, int points)
        {
            if (r == null)
            {
                // 空のカード
                SetCardKind(c, false, false);
                c.emptyMark.gameObject.SetActive(true);
                c.body.color = CardEmpty;
                c.name.text = "EMPTY";
                c.name.color = Mute;
                c.tag.text = "";
                c.strip.text = "";
                SetPriceText(c, "―", Mute, false);
                c.group.alpha = 0.6f;
                return;
            }

            SetCardKind(c, true, false);
            c.group.alpha = sold ? 0.5f : 1f;
            c.name.text = r.name;
            c.tag.text = r.category;
            c.strip.text = r.rarity;
            c.strip.color = StripRarityColor(r.rarity);
            var tex = GetJacket(r);
            c.jacket.texture = tex;
            c.jacket.enabled = tex != null;
            c.jacketBg.color = RecordArtwork.CategoryColor(r.category);
            c.initial.text = tex == null && r.name.Length > 0 ? r.name.Substring(0, 1) : "";
            if (sold) SetPriceText(c, "購入済み", Mute, false);
            else SetPrice(c, game.Shop.RecordPrice(r), points);
        }

        /// <summary>カードの中身の種類を切り替える（record：レコードのジャケット、big：大きな文字）。</summary>
        static void SetCardKind(ShopCard c, bool record, bool big)
        {
            c.emptyMark.gameObject.SetActive(false);
            c.diamond.gameObject.SetActive(false);
            c.body.color = Color.white;
            c.name.color = Fg;
            c.group.alpha = 1f;
            c.jacketBg.gameObject.SetActive(record);
            c.jacket.gameObject.SetActive(record);
            c.initial.gameObject.SetActive(record);
            c.big.gameObject.SetActive(big);
            c.big.rectTransform.anchoredPosition = new Vector2(SlotW * 0.5f, -112f);
            c.strip.color = DarkText;
        }

        /// <summary>値段の段：払えれば白、足りなければ赤っぽく。</summary>
        void SetPrice(ShopCard c, int price, int points)
        {
            SetPriceText(c, $"{price:N0} pt", points >= price ? Fg : ShortColor, true);
        }

        static void SetPriceText(ShopCard c, string text, Color color, bool showIcon)
        {
            c.price.text = text;
            c.price.color = color;
            c.priceIcon.enabled = showIcon;
        }

        Texture2D GetJacket(RecordData r)
        {
            if (!artworkLookedUp)
            {
                artwork = FindFirstObjectByType<RecordArtwork>();
                artworkLookedUp = true;
            }
            if (artwork != null) return artwork.GetJacket(r);
            return Resources.Load<Texture2D>(RecordArtwork.ResourceFolder + "/" + r.key);
        }

        /// <summary>説明の枠のレアリティの色（暗い地の上）</summary>
        static Color RarityColor(string rarity)
        {
            switch (rarity)
            {
                case "レア": return new Color(0.45f, 0.70f, 1.00f);
                case "エピック": return new Color(0.80f, 0.50f, 1.00f);
                case "レジェンド": return new Color(1.00f, 0.80f, 0.30f);
                default: return new Color(0.80f, 0.82f, 0.85f); // コモン
            }
        }

        /// <summary>カードの白い帯のレアリティの色（明るい地の上）</summary>
        static Color StripRarityColor(string rarity)
        {
            switch (rarity)
            {
                case "レア": return new Color(0.15f, 0.42f, 0.90f);
                case "エピック": return new Color(0.55f, 0.22f, 0.85f);
                case "レジェンド": return new Color(0.85f, 0.50f, 0.00f);
                default: return DarkText; // コモン
            }
        }

        void SetMessage(string message)
        {
            shopMessageText.text = message;
            messageRoot.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        void ShowMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            SetMessage(message);
            messageTimer = 2.5f;
        }

        void OnBuy(int index)
        {
            game.Shop.Buy(index, out string message);
            ShowMessage(message);
            RefreshShop();
        }

        void OnReroll()
        {
            game.Shop.Reroll(out string message);
            ShowMessage(message);
            RefreshShop();
        }

        void OnBuyLimit()
        {
            game.Shop.BuyLimitUpgrade(out string message);
            ShowMessage(message);
            RefreshShop();
        }

        void OnExchange()
        {
            game.Shop.ExchangeSpecial(out string message);
            ShowMessage(message);
            RefreshShop();
        }

        // ================================================================
        // マウス
        // ================================================================

        void UpdateShop()
        {
            if (!shopOpen) return;
            // レコードの効果などでポイントが変わった時
            if (game.AreaPoints != lastPoints || game.SpecialPoints != lastSpecial) RefreshShop();

            if (messageTimer > 0f)
            {
                messageTimer -= Time.deltaTime;
                if (messageTimer <= 0f) SetMessage("");
            }

            var outside = new Vector2(-1000f, -1000f);
            Vector2 p = outside;
            // 次のエリアに進む確認を出している間は、ショップを操作できない
            bool blocked = game.ClearBar.IsConfirmOpen || game.ClearBar.ConfirmClosedFrame == Time.frameCount;
            bool hasPoint = shopReady && !blocked && TryGetCanvasPoint(out p);
            if (!hasPoint) p = outside;

            // ボタン：カーソルを合わせると少し大きく
            foreach (var hot in hots)
            {
                bool inside = hot.rect.Contains(p);
                if (inside == hot.hovered) continue;
                hot.hovered = inside;
                hot.target.localScale = inside ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one;
            }

            // カード：カーソルを合わせると縁が光って少し浮く。レコードなら説明を出す
            int hoveredCard = -1;
            for (int i = 0; i < cards.Length; i++)
            {
                bool usable = i >= RecordShop.OfferCount || game.Shop.Offers[i] != null;
                bool inside = usable && cards[i].area.Contains(p);
                cards[i].highlight.enabled = inside;
                cards[i].root.localScale = inside ? new Vector3(1.04f, 1.04f, 1f) : Vector3.one;
                if (inside) hoveredCard = i;
            }
            ShowTooltip(hoveredCard < RecordShop.OfferCount ? hoveredCard : -1);

            if (hasPoint && InputCompat.MouseDown())
            {
                if (hoveredCard >= 0)
                {
                    cards[hoveredCard].onClick?.Invoke();
                    return;
                }
                foreach (var hot in hots)
                {
                    if (!hot.rect.Contains(p)) continue;
                    hot.onClick?.Invoke();
                    break;
                }
            }
        }

        void ShowTooltip(int index)
        {
            if (tooltip == null) return;
            var r = index >= 0 ? game.Shop.Offers[index] : null;
            if (r == null)
            {
                tooltip.gameObject.SetActive(false);
                return;
            }

            tipName.text = r.name;
            tipSub.text = $"{r.category}・{r.rarity}　　{game.Shop.RecordPrice(r)} pt";
            tipSub.color = RarityColor(r.rarity);
            tipEffect.text = r.effectText;

            // カードの上に出す（左右ははみ出さないように寄せる）
            var area = cards[index].area;
            float w = tooltip.sizeDelta.x, h = tooltip.sizeDelta.y;
            float x = Mathf.Clamp(area.center.x, 10f + w * 0.5f, CanvasW - 10f - w * 0.5f);
            float y = area.yMin - 14f - h * 0.5f;
            tooltip.anchoredPosition = new Vector2(x, -y);
            tooltip.gameObject.SetActive(true);
        }

        /// <summary>
        /// マウスの位置を、モニターの画面の座標（左上が (0, 0)、1500×900）に直す。モニターの外なら false。
        /// メインカメラは ArcadeCamera で RenderTexture に描いていることがあるので、画面の割合（Viewport）から Ray を作る。
        /// </summary>
        bool TryGetCanvasPoint(out Vector2 point)
        {
            point = default;
            var cam = Camera.main;
            if (cam == null || screenRenderer == null) return false;

            Vector2 mouse = InputCompat.MousePosition();
            var ray = cam.ViewportPointToRay(new Vector3(mouse.x / Screen.width, mouse.y / Screen.height, 0f));
            var b = screenRenderer.bounds;
            var plane = new Plane(Vector3.back, b.center);
            if (!plane.Raycast(ray, out float enter)) return false;

            var hit = ray.GetPoint(enter);
            if (hit.x < b.min.x || hit.x > b.max.x || hit.y < b.min.y || hit.y > b.max.y) return false;
            float u = Mathf.InverseLerp(b.min.x, b.max.x, hit.x);
            float v = Mathf.InverseLerp(b.min.y, b.max.y, hit.y);
            if (game.monitor.flipX) u = 1f - u;
            if (game.monitor.flipY) v = 1f - v;
            point = new Vector2(u * CanvasW, (1f - v) * CanvasH);
            return true;
        }
    }
}
