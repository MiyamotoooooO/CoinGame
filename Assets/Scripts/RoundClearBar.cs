using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// ラウンドをクリアした時に、画面の下に出る帯。「ショップへ入る」と「閉じる」のボタンがある。
    /// ・ショップへ入る：カメラがモニターに寄ってショップが開く
    /// ・閉じる：ショップに寄らずに次のラウンド（エリアの最後なら次のエリア）へ進む（Enter キーでも同じ）
    /// </summary>
    public class RoundClearBar : MonoBehaviour
    {
        MedalPusherGame game;
        CanvasGroup group;
        Text titleText;
        float alpha;

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
            canvas.sortingOrder = 50; // ドット化した画面（ArcadeCamera）やモニターより上
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
            NewButton(bar, font, new Vector2(-170f, 115f), "ショップへ入る", ShopColor, () => game.OpenShop());
            NewButton(bar, font, new Vector2(170f, 115f), "閉じる", CloseColor, () => game.ProceedAfterClear());
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

        static void NewButton(RectTransform parent, Font font, Vector2 pos, string label, Color color, UnityEngine.Events.UnityAction onClick)
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
        }
    }
}
