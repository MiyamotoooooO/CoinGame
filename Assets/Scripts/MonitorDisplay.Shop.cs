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
    /// ・画面は1枚：上にタイトルと所持メダル、中央にレコード6枚（ジャケット・名前・値段・購入）、
    ///   右に投入上限アップ、左下にリロール、右下に閉じる
    /// ・レコードにカーソルを合わせると、効果の説明を Resources/ShopTooltipFrame の枠に出す
    /// ・モニターは RenderTexture に描いているので、マウスの位置をモニターの画面の座標に直して、ボタンを自分で判定する
    /// </summary>
    public partial class MonitorDisplay
    {
        static readonly Color ShopBg = new Color(0.05f, 0.16f, 0.18f);
        static readonly Color ShopPanel = new Color(0.08f, 0.25f, 0.28f);
        static readonly Color ShopLine = new Color(0.35f, 0.75f, 0.75f);
        static readonly Color CardHighlight = new Color(0.60f, 0.95f, 0.95f);
        static readonly Color CoinColor = new Color(0.95f, 0.62f, 0.22f);
        static readonly Color BuyColor = new Color(0.86f, 0.80f, 0.62f);      // 参考作品の「購入」ボタン風
        static readonly Color RerollColor = new Color(0.82f, 0.82f, 0.82f);
        static readonly Color CloseColor = new Color(0.75f, 0.22f, 0.18f);
        static readonly Color DisabledColor = new Color(0.35f, 0.38f, 0.40f);
        static readonly Color DarkText = new Color(0.15f, 0.12f, 0.10f);

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
        Text shopMedalsText, shopMessageText, rerollCostText, limitValueText, limitCostText, limitButtonText;
        Text tipName, tipSub, tipEffect;
        Image rerollButton, limitButton;
        readonly ShopCard[] cards = new ShopCard[RecordShop.OfferCount];
        readonly List<Hot> hots = new List<Hot>();
        Coroutine shopRoutine;
        bool shopOpen;      // ショップの画面を出している
        bool shopReady;     // カメラが寄り切って、操作できる
        float messageTimer;
        int lastMedals = -1;
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
            lastMedals = -1;
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
            shopRoot = NewRect("Shop", root, 750f, 300f, CanvasW, CanvasH);
            NewImage("ShopBg", shopRoot, 750f, 300f, CanvasW, CanvasH, ShopBg);
            NewImage("LineTop", shopRoot, 750f, 8f, CanvasW - 20f, 4f, ShopLine);
            NewImage("LineBottom", shopRoot, 750f, 592f, CanvasW - 20f, 4f, ShopLine);
            NewImage("LineLeft", shopRoot, 8f, 300f, 4f, CanvasH - 20f, ShopLine);
            NewImage("LineRight", shopRoot, 1492f, 300f, 4f, CanvasH - 20f, ShopLine);

            // タイトル（参考作品のように1文字ずつ色を変える）
            string[] letters = { "S", "H", "O", "P" };
            Color[] colors =
            {
                new Color(1.00f, 0.60f, 0.15f), new Color(0.90f, 0.20f, 0.20f),
                new Color(0.25f, 0.55f, 0.95f), new Color(0.30f, 0.80f, 0.30f)
            };
            for (int i = 0; i < letters.Length; i++)
                NewText("Title" + letters[i], shopRoot, 70f + i * 50f, 52f, 60f, 70f, letters[i], 62, colors[i]);

            // 所持メダル（右上）
            NewImage("MedalIcon", shopRoot, 1150f, 52f, 34f, 34f, CoinColor, circleSprite);
            shopMedalsText = NewText("Medals", shopRoot, 1335f, 52f, 300f, 50f, "", 32, Color.white);
            shopMedalsText.alignment = TextAnchor.MiddleLeft;

            // 見出し
            var header = NewText("RecordsHeader", shopRoot, 340f, 108f, 600f, 40f, "レコード  ›", 28, Color.white);
            header.alignment = TextAnchor.MiddleLeft;

            for (int i = 0; i < cards.Length; i++) cards[i] = BuildCard(i, FirstCardX + i * CardStep);

            BuildLimitPanel();

            // リロール（左下）
            NewImage("RerollIcon", shopRoot, 80f, 458f, 24f, 24f, CoinColor, circleSprite);
            rerollCostText = NewText("RerollCost", shopRoot, 160f, 458f, 120f, 34f, "", 26, Color.white);
            rerollCostText.alignment = TextAnchor.MiddleLeft;
            rerollButton = NewImage("Reroll", shopRoot, 130f, 515f, 200f, 62f, RerollColor);
            ButtonLabel(rerollButton, "リロール", 30, DarkText);
            AddHot(rerollButton.rectTransform, 130f, 515f, 200f, 62f, OnReroll);

            // メッセージ（購入した・メダルが足りない など）
            shopMessageText = NewText("Message", shopRoot, 750f, 470f, 900f, 40f, "", 24, new Color(1f, 0.85f, 0.35f));

            // 閉じる（右下）
            var close = NewImage("Close", shopRoot, 1350f, 532f, 220f, 62f, CloseColor);
            ButtonLabel(close, "閉じる", 30, Color.white).GetComponent<Shadow>().enabled = true;
            AddHot(close.rectTransform, 1350f, 532f, 220f, 62f, () => game.CloseShop());

            NewText("Hint", shopRoot, 750f, 572f, 1000f, 26f, "レコードにカーソルを合わせると効果を表示 ／ クリックで購入", 17, new Color(0.7f, 0.8f, 0.8f));

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
            NewImage("PriceIcon", c.root, mid - 22f, 228f, 20f, 20f, CoinColor, circleSprite);
            c.price = NewText("Price", c.root, mid + 22f, 228f, 60f, 30f, "", 22, Color.white);
            c.price.alignment = TextAnchor.MiddleLeft;

            c.button = NewImage("Buy", c.root, mid, 270f, 124f, 38f, BuyColor);
            c.buttonText = ButtonLabel(c.button, "購入", 22, DarkText);
            AddHot(c.button.rectTransform, cx, CardTop + 270f, 124f, 38f, () => OnBuy(index));
            return c;
        }

        /// <summary>右側の「投入上限アップ」。</summary>
        void BuildLimitPanel()
        {
            NewImage("LimitPanel", shopRoot, 1305f, 290f, 330f, CardH, ShopPanel);
            NewText("LimitHeader", shopRoot, 1305f, 170f, 320f, 40f, "投入上限アップ", 26, Color.white);
            NewText("LimitSub", shopRoot, 1305f, 203f, 320f, 26f, "1ラウンドに投入できる枚数", 17, new Color(0.7f, 0.8f, 0.8f));
            limitValueText = NewText("LimitValue", shopRoot, 1305f, 262f, 320f, 60f, "", 44, new Color(1f, 0.85f, 0.35f));
            NewImage("LimitIcon", shopRoot, 1275f, 318f, 22f, 22f, CoinColor, circleSprite);
            limitCostText = NewText("LimitCost", shopRoot, 1345f, 318f, 100f, 30f, "", 24, Color.white);
            limitCostText.alignment = TextAnchor.MiddleLeft;
            limitButton = NewImage("LimitBuy", shopRoot, 1305f, 380f, 150f, 42f, BuyColor);
            limitButtonText = ButtonLabel(limitButton, "購入", 24, DarkText);
            AddHot(limitButton.rectTransform, 1305f, 380f, 150f, 42f, OnBuyLimit);
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
            int medals = game.OwnedMedals;

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
                c.price.text = r.buyPrice.ToString();
                var tex = GetJacket(r);
                c.jacket.texture = tex;
                c.jacket.enabled = tex != null;
                c.jacketColor.color = RecordArtwork.CategoryColor(r.category);
                c.initial.text = tex == null && r.name.Length > 0 ? r.name.Substring(0, 1) : "";
                c.buttonText.text = sold ? "購入済み" : "購入";
                c.button.color = sold || medals < r.buyPrice ? DisabledColor : BuyColor;
            }

            rerollCostText.text = shop.RerollCost.ToString();
            rerollButton.color = medals >= shop.RerollCost ? RerollColor : DisabledColor;

            if (shop.CanUpgradeLimit)
            {
                limitValueText.text = $"{game.InsertLimit} → {shop.NextInsertLimit}";
                limitCostText.text = shop.LimitUpgradeCost.ToString();
                limitButtonText.text = "購入";
                limitButton.color = medals >= shop.LimitUpgradeCost ? BuyColor : DisabledColor;
            }
            else
            {
                limitValueText.text = $"最大 {game.InsertLimit}";
                limitCostText.text = "―";
                limitButtonText.text = "最大";
                limitButton.color = DisabledColor;
            }

            shopMedalsText.text = $"所持メダル  {medals}";
            lastMedals = medals;
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

        // ================================================================
        // マウス
        // ================================================================

        void UpdateShop()
        {
            if (!shopOpen) return;
            if (game.OwnedMedals != lastMedals) RefreshShop();   // 払い戻しなどで所持メダルが変わった時

            if (messageTimer > 0f)
            {
                messageTimer -= Time.deltaTime;
                if (messageTimer <= 0f) shopMessageText.text = "";
            }

            var outside = new Vector2(-1000f, -1000f);
            Vector2 p = outside;
            bool hasPoint = shopReady && TryGetCanvasPoint(out p);
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
            tipSub.text = $"{r.category}・{r.rarity}　　購入 {r.buyPrice} / 売却 {r.sellPrice}";
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
        /// マウスの位置を、モニターの画面の座標（左上が (0, 0)、1500×600）に直す。モニターの外なら false。
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
