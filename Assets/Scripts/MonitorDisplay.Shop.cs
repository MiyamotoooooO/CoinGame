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
    /// ・画面は1枚：上にタイトルと所持ポイント・特殊ポイント、中央にレコード6枚（ジャケット・名前・値段・購入）、
    ///   右に投入上限アップと特殊ポイントへの交換、左下にリロール、右下に閉じる
    /// ・支払いはエリアのポイント（pt）
    /// ・レコードにカーソルを合わせると、効果の説明を Resources/ShopTooltipFrame の枠に出す
    /// ・モニターは RenderTexture に描いているので、マウスの位置をモニターの画面の座標に直して、ボタンを自分で判定する
    /// </summary>
    public partial class MonitorDisplay
    {
        // 色はモニター本体（MonitorDisplay.cs）に合わせる：黒に近いグレー＋白＋差し色のオレンジ
        static readonly Color ShopBg = new Color32(0x0c, 0x0c, 0x0e, 0xff);
        static readonly Color ShopPanel = new Color32(0x15, 0x15, 0x18, 0xff);
        static readonly Color ShopLine = new Color32(0x3a, 0x3a, 0x42, 0xff);
        static readonly Color CardHighlight = new Color32(0xff, 0x7a, 0x1a, 0xff);
        static readonly Color PointColor = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color SpecialColor = new Color32(0x38, 0xc8, 0xff, 0xff);
        static readonly Color BuyColor = new Color32(0xff, 0x7a, 0x1a, 0xff);
        static readonly Color RerollColor = new Color32(0xf2, 0xf2, 0xee, 0xff);
        static readonly Color CloseColor = new Color32(0xff, 0x2e, 0x4d, 0xff);
        static readonly Color DisabledColor = new Color32(0x3a, 0x3a, 0x42, 0xff);
        static readonly Color DarkText = new Color32(0x0c, 0x0c, 0x0e, 0xff);

        const float ShopH = 600f;                       // ショップの中身の高さ
        const float ShopTop = (CanvasH - ShopH) * 0.5f; // ショップの中身の上端（画面の上下のまん中に置く）

        const float CardTop = 140f;     // カードの上端
        const float CardW = 164f;
        const float CardH = 300f;
        const float CardStep = 176f;
        const float FirstCardX = 115f;

        class ShopCard
        {
            public RectTransform root;
            public CanvasGroup group;
            public Image highlight, jacketColor, button;
            public RawImage jacket;
            public Text initial, name, sub, price, buttonText;
            public Rect area;           // カード全体（カーソルを合わせると説明が出る）
        }

        /// <summary>クリックできる場所（モニターの画面の座標。左上が (0, 0)）</summary>
        class Hot
        {
            public Rect rect;
            public RectTransform target;
            public Action onClick;
            public bool hovered;
        }

        RectTransform shopRoot, tooltip;
        Text shopPointsText, shopSpecialText, shopMessageText, rerollCostText, limitValueText, limitCostText, limitButtonText;
        Text exchangeValueText;
        Text tipName, tipSub, tipEffect;
        Image rerollButton, limitButton, exchangeButton;
        readonly ShopCard[] cards = new ShopCard[RecordShop.OfferCount];
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
            shopMessageText.text = "";
            lastPoints = -1;
            RefreshShop();
            ShowTooltip(-1);
            shopRoot.gameObject.SetActive(true);
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
            }
            shopRoutine = null;
            done?.Invoke();
        }

        // ================================================================
        // 画面の組み立て
        // ================================================================

        void BuildShopPage(RectTransform root)
        {
            // ショップの中身は 1500×600 で組んである。画面（1500×900）の上下のまん中に置き、背景は画面いっぱいに敷く
            shopRoot = NewRect("Shop", root, 750f, ShopTop + ShopH * 0.5f, CanvasW, ShopH);
            NewImage("ShopBg", shopRoot, 750f, ShopH * 0.5f, CanvasW, CanvasH, ShopBg);
            NewImage("LineTop", shopRoot, 750f, 8f - ShopTop, CanvasW - 20f, 4f, ShopLine);
            NewImage("LineBottom", shopRoot, 750f, ShopH + ShopTop - 8f, CanvasW - 20f, 4f, ShopLine);
            NewImage("LineLeft", shopRoot, 8f, ShopH * 0.5f, 4f, CanvasH - 20f, ShopLine);
            NewImage("LineRight", shopRoot, 1492f, ShopH * 0.5f, 4f, CanvasH - 20f, ShopLine);

            // タイトル（参考作品のように1文字ずつ色を変える）
            string[] letters = { "S", "H", "O", "P" };
            Color[] colors =
            {
                new Color(1.00f, 0.60f, 0.15f), new Color(0.90f, 0.20f, 0.20f),
                new Color(0.25f, 0.55f, 0.95f), new Color(0.30f, 0.80f, 0.30f)
            };
            for (int i = 0; i < letters.Length; i++)
                NewText("Title" + letters[i], shopRoot, 70f + i * 50f, 52f, 60f, 70f, letters[i], 62, colors[i]);

            // 所持ポイント（このエリアで使える）と特殊ポイント（右上）
            shopPointsText = NewText("Points", shopRoot, 960f, 52f, 420f, 50f, "", 32, PointColor);
            shopPointsText.alignment = TextAnchor.MiddleRight;
            NewImage("SpecialIcon", shopRoot, 1218f, 52f, 26f, 26f, SpecialColor, circleSprite);
            shopSpecialText = NewText("Special", shopRoot, 1360f, 52f, 240f, 50f, "", 28, SpecialColor);
            shopSpecialText.alignment = TextAnchor.MiddleLeft;

            // 見出し
            var header = NewText("RecordsHeader", shopRoot, 340f, 108f, 600f, 40f, "レコード  ›", 28, Color.white);
            header.alignment = TextAnchor.MiddleLeft;

            for (int i = 0; i < cards.Length; i++) cards[i] = BuildCard(i, FirstCardX + i * CardStep);

            BuildLimitPanel();

            // リロール（左下）
            rerollCostText = NewText("RerollCost", shopRoot, 130f, 458f, 200f, 34f, "", 26, Color.white);
            rerollButton = NewImage("Reroll", shopRoot, 130f, 515f, 200f, 62f, RerollColor);
            ButtonLabel(rerollButton, "リロール", 30, DarkText);
            AddHot(rerollButton.rectTransform, 130f, 515f, 200f, 62f, OnReroll);

            // メッセージ（購入した・ポイントが足りない など）
            shopMessageText = NewText("Message", shopRoot, 750f, 470f, 900f, 40f, "", 24, new Color(1f, 0.85f, 0.35f));

            // 閉じる（右下）
            var close = NewImage("Close", shopRoot, 1350f, 532f, 220f, 62f, CloseColor);
            ButtonLabel(close, "閉じる", 30, Color.white).GetComponent<Shadow>().enabled = true;
            AddHot(close.rectTransform, 1350f, 532f, 220f, 62f, () => game.CloseShop());

            NewText("Hint", shopRoot, 750f, 572f, 1000f, 26f, "レコードにカーソルを合わせると効果を表示 ／ ポイントは次のエリアに持ち越せません", 17, new Color(0.7f, 0.8f, 0.8f));

            BuildTooltip(); // 一番上に描くように最後に作る
            shopRoot.gameObject.SetActive(false);
        }

        /// <summary>レコード1枚分のカード（ジャケット・名前・系統とレアリティ・値段・購入ボタン）。</summary>
        ShopCard BuildCard(int index, float cx)
        {
            var c = new ShopCard();
            float cy = CardTop + CardH * 0.5f;
            c.root = NewRect($"Card{index + 1}", shopRoot, cx, cy, CardW, CardH);
            c.group = c.root.gameObject.AddComponent<CanvasGroup>();
            c.area = new Rect(cx - CardW * 0.5f, CardTop, CardW, CardH);

            float w = CardW, mid = w * 0.5f;
            c.highlight = NewImage("Highlight", c.root, mid, CardH * 0.5f, w + 8f, CardH + 8f, CardHighlight);
            c.highlight.enabled = false;
            NewImage("Frame", c.root, mid, CardH * 0.5f, w, CardH, ShopPanel);

            // ジャケット（画像が無いレコードは、系統の色と名前の1文字目）
            c.jacketColor = NewImage("JacketColor", c.root, mid, 82f, 140f, 140f, Color.gray);
            c.initial = NewText("Initial", c.root, mid, 82f, 140f, 140f, "", 64, Color.white);
            var jacketRt = NewRect("Jacket", c.root, mid, 82f, 140f, 140f);
            c.jacket = jacketRt.gameObject.AddComponent<RawImage>();
            c.jacket.raycastTarget = false;

            c.name = NewText("Name", c.root, mid, 172f, w, 30f, "", 22, Color.white);
            c.sub = NewText("Sub", c.root, mid, 198f, w, 24f, "", 16, Color.white);
            c.price = NewText("Price", c.root, mid, 228f, w, 30f, "", 22, PointColor);

            c.button = NewImage("Buy", c.root, mid, 270f, 124f, 38f, BuyColor);
            c.buttonText = ButtonLabel(c.button, "購入", 22, DarkText);
            AddHot(c.button.rectTransform, cx, CardTop + 270f, 124f, 38f, () => OnBuy(index));
            return c;
        }

        /// <summary>右側の上「投入上限アップ」と、下「特殊ポイントに交換」（カードと同じ高さを2段に分ける）。</summary>
        void BuildLimitPanel()
        {
            var subColor = new Color(0.7f, 0.8f, 0.8f);

            // 上段：投入上限アップ（y 140〜285）
            NewImage("LimitPanel", shopRoot, 1305f, 212.5f, 330f, 145f, ShopPanel);
            NewText("LimitHeader", shopRoot, 1305f, 160f, 320f, 32f, "投入上限アップ", 24, Color.white);
            NewText("LimitSub", shopRoot, 1305f, 184f, 320f, 22f, "1ラウンドに投入できる枚数", 15, subColor);
            limitValueText = NewText("LimitValue", shopRoot, 1305f, 220f, 320f, 44f, "", 32, PointColor);
            limitCostText = NewText("LimitCost", shopRoot, 1222f, 262f, 140f, 30f, "", 22, PointColor);
            limitButton = NewImage("LimitBuy", shopRoot, 1375f, 262f, 130f, 38f, BuyColor);
            limitButtonText = ButtonLabel(limitButton, "購入", 22, DarkText);
            AddHot(limitButton.rectTransform, 1375f, 262f, 130f, 38f, OnBuyLimit);

            // 下段：特殊ポイントに交換（y 295〜440）
            NewImage("ExchangePanel", shopRoot, 1305f, 367.5f, 330f, 145f, ShopPanel);
            NewText("ExchangeHeader", shopRoot, 1305f, 315f, 320f, 32f, "特殊ポイントに交換", 24, SpecialColor);
            NewText("ExchangeSub", shopRoot, 1305f, 339f, 320f, 22f, "エリアを移っても残るポイント", 15, subColor);
            exchangeValueText = NewText("ExchangeValue", shopRoot, 1305f, 374f, 320f, 40f, "", 26, Color.white);
            exchangeButton = NewImage("ExchangeBuy", shopRoot, 1305f, 417f, 150f, 38f, SpecialColor);
            ButtonLabel(exchangeButton, "交換", 22, DarkText);
            AddHot(exchangeButton.rectTransform, 1305f, 417f, 150f, 38f, OnExchange);
        }

        /// <summary>レコードの効果を出す説明の枠。</summary>
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

        /// <summary>ボタンの中に文字を置く（ボタンと一緒に大きくなる）。影は付けない。</summary>
        Text ButtonLabel(Image button, string text, int size, Color color)
        {
            var s = button.rectTransform.sizeDelta;
            var t = NewText("Label", button.rectTransform, s.x * 0.5f, s.y * 0.5f, s.x, s.y, text, size, color);
            t.GetComponent<Shadow>().enabled = false;
            return t;
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

            for (int i = 0; i < cards.Length; i++)
            {
                var c = cards[i];
                var r = shop.Offers[i];
                if (r == null)
                {
                    c.group.alpha = 0.3f;
                    c.name.text = "―";
                    c.sub.text = "";
                    c.price.text = "";
                    c.initial.text = "";
                    c.jacket.enabled = false;
                    c.jacketColor.color = DisabledColor;
                    c.buttonText.text = "なし";
                    c.button.color = DisabledColor;
                    continue;
                }

                bool sold = shop.Sold[i];
                c.group.alpha = sold ? 0.45f : 1f;
                c.name.text = r.name;
                c.sub.text = $"{r.category}・{r.rarity}";
                c.sub.color = RarityColor(r.rarity);
                int price = shop.RecordPrice(r);
                c.price.text = $"{price} pt";
                var tex = GetJacket(r);
                c.jacket.texture = tex;
                c.jacket.enabled = tex != null;
                c.jacketColor.color = RecordArtwork.CategoryColor(r.category);
                c.initial.text = tex == null && r.name.Length > 0 ? r.name.Substring(0, 1) : "";
                c.buttonText.text = sold ? "購入済み" : "購入";
                c.button.color = sold || points < price ? DisabledColor : BuyColor;
            }

            rerollCostText.text = $"{shop.RerollCost} pt";
            rerollButton.color = points >= shop.RerollCost ? RerollColor : DisabledColor;

            if (shop.CanUpgradeLimit)
            {
                limitValueText.text = $"{game.InsertLimit} → {shop.NextInsertLimit}";
                limitCostText.text = $"{shop.LimitUpgradeCost} pt";
                limitButtonText.text = "購入";
                limitButton.color = points >= shop.LimitUpgradeCost ? BuyColor : DisabledColor;
            }
            else
            {
                limitValueText.text = $"最大 {game.InsertLimit}";
                limitCostText.text = "―";
                limitButtonText.text = "最大";
                limitButton.color = DisabledColor;
            }

            exchangeValueText.text = $"{shop.ExchangeCost} pt → 特殊 +{shop.ExchangeAmount}";
            exchangeButton.color = points >= shop.ExchangeCost ? SpecialColor : DisabledColor;

            shopPointsText.text = $"ポイント  {points} pt";
            shopSpecialText.text = $"特殊ポイント  {game.SpecialPoints}";
            lastPoints = points;
            lastSpecial = game.SpecialPoints;
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

        void ShowMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            shopMessageText.text = message;
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
                if (messageTimer <= 0f) shopMessageText.text = "";
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

            // カード：カーソルを合わせると説明を出す
            int hovered = -1;
            for (int i = 0; i < cards.Length; i++)
            {
                bool inside = game.Shop.Offers[i] != null && cards[i].area.Contains(p);
                cards[i].highlight.enabled = inside;
                if (inside) hovered = i;
            }
            ShowTooltip(hovered);

            if (hasPoint && InputCompat.MouseDown())
            {
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

            // カードの右に出す。はみ出す時は左に出す
            var area = cards[index].area;
            float w = tooltip.sizeDelta.x, h = tooltip.sizeDelta.y;
            float x = area.xMax + 10f + w * 0.5f;
            if (x + w * 0.5f > CanvasW - 10f) x = area.xMin - 10f - w * 0.5f;
            float y = area.yMin + h * 0.5f;
            tooltip.anchoredPosition = new Vector2(x, -y);
            tooltip.gameObject.SetActive(true);
        }

        /// <summary>
        /// マウスの位置を、ショップの中身の座標（左上が (0, 0)、1500×600）に直す。モニターの外なら false。
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
            point = new Vector2(u * CanvasW, (1f - v) * CanvasH - ShopTop); // ショップの中身の座標に直す
            return true;
        }
    }
}
