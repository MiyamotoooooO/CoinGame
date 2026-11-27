using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// ラウンドをクリアした時に、画面の下に出る帯。「ショップへ入る」と「閉じる」のボタンがある。
    /// ・ショップへ入る：カメラがモニターに寄ってショップが開く
    /// ・閉じる：ショップに寄らずに次のラウンド（エリアの最後なら次のエリア）へ進む（Enter キーでも同じ）
    ///
    /// 次のエリアに進むとエリアのポイントがなくなる時は、進む前に確認を出す（ShowAreaChangeConfirm）。
    /// 確認はショップを閉じる時にも使うので、ショップの画面の上にも出る。
    /// </summary>
    public class RoundClearBar : MonoBehaviour
    {
        MedalPusherGame game;
        CanvasGroup group;
        Text titleText;
        float alpha;

        // 次のエリアに進む前の確認
        CanvasGroup confirmGroup;
        Text confirmText;
        System.Action confirmAction;
        float confirmAlpha;

        /// <summary>
        /// 確認を出しているか（出している間は Enter キーやショップの操作を受け付けない）。
        /// チュートリアルで先へ進むのを止めている間（TutorialGate.BlockProceed）も true にして、
        /// 「閉じる」・Enter キー・ショップを閉じる を止める（MedalPusherGame がこれを見ている）
        /// </summary>
        public bool IsConfirmOpen => confirmShowing || TutorialGate.BlockProceed;
        /// <summary>確認の画面を本当に出しているか（チュートリアルの止めは入らない）</summary>
        public bool IsConfirmShowing => confirmShowing;
        bool confirmShowing;

        /// <summary>「ショップへ入る」ボタン（チュートリアルの矢印で指す）</summary>
        public RectTransform ShopButtonRect { get; private set; }
        Button closeButton;
        /// <summary>確認を閉じたフレーム（閉じたクリックで、後ろのショップのボタンまで押さないように）</summary>
        public int ConfirmClosedFrame { get; private set; } = -1;

        static readonly Color ShopColor = new Color(0.20f, 0.62f, 0.62f);
        static readonly Color CloseColor = new Color(0.45f, 0.40f, 0.35f);

        public void Init(MedalPusherGame game)
        {
            this.game = game;
            EnsureEventSystem();
            Build();
        }

        /// <summary>ボタンを押せるように、シーンに EventSystem が無ければ作る。</summary>
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

        void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("RoundClearBar");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60; // ドット化した画面（ArcadeCamera）やモニター、ショップより上
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            group = canvasGo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            // 画面の下の帯
            var bar = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(canvasGo.transform, false);
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(0f, 170f);
            bar.anchoredPosition = Vector2.zero;
            var bg = bar.gameObject.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.04f, 0.04f, 0.72f);

            titleText = NewText(bar, font, new Vector2(0f, 40f), new Vector2(900f, 60f), "", 40, new Color(1f, 0.78f, 0.25f));
            ShopButtonRect = (RectTransform)NewButton(bar, font, new Vector2(-170f, 115f), "ショップへ入る", ShopColor, () => game.OpenShop()).transform;
            closeButton = NewButton(bar, font, new Vector2(170f, 115f), "閉じる", CloseColor, () => game.RequestProceed());

            BuildConfirm(canvasGo.transform, font);
        }

        /// <summary>次のエリアに進む前の確認（画面全体を暗くして、真ん中に出す）。</summary>
        void BuildConfirm(Transform canvas, Font font)
        {
            var root = new GameObject("AreaChangeConfirm", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(canvas, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var dim = root.gameObject.AddComponent<Image>(); // 後ろのボタンを押せないようにする
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            confirmGroup = root.gameObject.AddComponent<CanvasGroup>();
            confirmGroup.alpha = 0f;
            confirmGroup.interactable = false;
            confirmGroup.blocksRaycasts = false;
            confirmGroup.ignoreParentGroups = true; // ショップの時は帯が消えているが、確認は出す

            var box = new GameObject("Box", typeof(RectTransform)).GetComponent<RectTransform>();
            box.SetParent(root, false);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(980f, 400f);
            box.anchoredPosition = Vector2.zero;
            var boxBg = box.gameObject.AddComponent<Image>();
            boxBg.color = new Color(0.10f, 0.08f, 0.07f, 0.96f);
            var outline = box.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.30f, 0.20f, 0.9f);
            outline.effectDistance = new Vector2(4f, -4f);

            // box の中は、下を 0 にした位置で並べる（NewText / NewButton は下の真ん中が基準）
            NewText(box, font, new Vector2(0f, 330f), new Vector2(900f, 60f), "！ 注意 ！", 44, new Color(1f, 0.40f, 0.30f));
            confirmText = NewText(box, font, new Vector2(0f, 220f), new Vector2(900f, 130f), "", 32, Color.white);
            confirmText.horizontalOverflow = HorizontalWrapMode.Wrap;
            confirmText.lineSpacing = 1.2f;
            NewButton(box, font, new Vector2(-170f, 70f), "次のエリアへ", new Color(0.75f, 0.28f, 0.22f), OnConfirmYes);
            NewButton(box, font, new Vector2(170f, 70f), "戻る", CloseColor, HideConfirm);
        }

        /// <summary>
        /// 「次のエリアに進むと今のポイントがなくなる」確認を出す。
        /// 「次のエリアへ」を押すと onConfirm を呼ぶ。「戻る」（Esc キー）なら何もしない。
        /// </summary>
        public void ShowAreaChangeConfirm(int points, System.Action onConfirm)
        {
            confirmAction = onConfirm;
            confirmText.text =
                $"次のエリアに進むと、今のポイント {points} pt はなくなります。\n本当に進みますか？\n" +
                $"<size=24><color=#9FE0D0>（特殊ポイント {game.SpecialPoints} は次のエリアにも持っていけます）</color></size>";
            confirmText.supportRichText = true;
            confirmShowing = true;
        }

        public void HideConfirm()
        {
            if (confirmShowing) ConfirmClosedFrame = Time.frameCount;
            confirmShowing = false;
            confirmAction = null;
        }

        void OnConfirmYes()
        {
            var action = confirmAction;
            HideConfirm();
            action?.Invoke();
        }

        void Update()
        {
            if (game == null) return;
            bool show = game.State == GameState.Cleared;
            alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            group.alpha = alpha;
            group.interactable = show;
            group.blocksRaycasts = show;
            if (show)
                titleText.text = game.IsLastRoundOfArea ? "すごい！ エリアをクリア！" : "すごい！ ラウンドをクリア！";

            // 確認：クリア後の帯かショップの時だけ（それ以外になったら閉じる）
            if (confirmShowing && game.State != GameState.Cleared && game.State != GameState.Shop) HideConfirm();
            if (confirmShowing && InputCompat.Down(GameKey.Cancel)) HideConfirm();
            confirmAlpha = Mathf.MoveTowards(confirmAlpha, confirmShowing ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            confirmGroup.alpha = confirmAlpha;
            confirmGroup.interactable = confirmShowing;
            confirmGroup.blocksRaycasts = confirmShowing;
            if (confirmShowing) group.interactable = false; // 後ろの帯のボタンは押せない

            // チュートリアルの間は「閉じる」を押せなくし、「ショップへ入る」をはずませる
            if (closeButton != null) closeButton.interactable = !TutorialGate.BlockProceed;
            if (ShopButtonRect != null)
            {
                float s = show && TutorialGate.BlockProceed ? 1f + 0.06f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f)) : 1f;
                ShopButtonRect.localScale = new Vector3(s, s, 1f);
            }
        }

        static Text NewText(RectTransform parent, Font font, Vector2 pos, Vector2 size, string text, int fontSize, Color color)
        {
            var rt = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        static Button NewButton(RectTransform parent, Font font, Vector2 pos, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var rt = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(280f, 66f);
            rt.anchoredPosition = pos;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var button = rt.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f); // カーソルを合わせると少し暗く
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;
            button.onClick.AddListener(onClick);
            var t = NewText(rt, font, Vector2.zero, rt.sizeDelta, label, 30, Color.white);
            var trt = t.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            return button;
        }
    }
}
