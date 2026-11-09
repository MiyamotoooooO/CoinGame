using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// モニターの下の木の板（奥の壁の、押し出し台の上に見えている所）を小さなモニターにして、
    /// このラウンドの投入数と上限を「12/30」のように出す。
    ///
    /// ・盤面のモデル（Playfield）の座標で、奥の壁の手前の面にぴったり貼る
    /// ・残りが lowLeft 以下になるとオレンジ、0 になると赤く点滅する。投入するたびに数字がはずむ
    /// ・MedalPusherGame は変えず、public の InsertedThisRound・InsertLimit を読むだけ
    /// ・シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    /// </summary>
    [DisallowMultipleComponent]
    public class InsertCounterDisplay : MonoBehaviour
    {
        [Tooltip("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;

        [Header("置き場所（盤面のモデル Playfield の座標）")]
        [Tooltip("盤面のモデルの名前")]
        public string playfieldObjectName = "Playfield";
        [Tooltip("まん中の位置（奥の壁の手前の面は z = 0.170、y = 0.034〜0.090）")]
        public Vector3 center = new Vector3(0f, 0.062f, 0.1690f);
        [Tooltip("横の幅（左右の発射口の間）")]
        [Range(0.05f, 0.5f)] public float width = 0.39f;
        [Tooltip("高さ")]
        [Range(0.01f, 0.06f)] public float height = 0.046f;

        [Header("見た目")]
        [Tooltip("モニターと同じく、ドット化せずにくっきり描く。\nオンにすると、手前に積もったメダルより上に描かれることがある")]
        public bool sharp = false;
        [Tooltip("残りがこの数以下になるとオレンジ")]
        [Range(0, 30)] public int lowLeft = 5;
        public Color panelColor = new Color32(0x16, 0x14, 0x22, 0xff);
        public Color borderColor = new Color32(0x8a, 0x7c, 0xf0, 0xff);
        public Color textColor = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        public Color mutedColor = new Color32(0x9c, 0x9e, 0xae, 0xff);
        public Color lowColor = new Color32(0xff, 0x7a, 0x1a, 0xff);
        public Color emptyColor = new Color32(0xff, 0x4d, 0x4d, 0xff);

        const float CanvasW = 800f;

        Canvas canvas;
        Text label, value;
        Image border;
        Font fallbackFont;
        bool fontsResolved;
        int lastInserted = -1;
        float bump;

        /// <summary>シーンに無ければ、MedalPusherGame の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<InsertCounterDisplay>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<InsertCounterDisplay>();
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null)
            {
                enabled = false;
                return;
            }
            fallbackFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
        }

        void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        void Build()
        {
            var fieldGo = GameObject.Find(playfieldObjectName);
            if (fieldGo == null)
            {
                Debug.LogWarning($"InsertCounterDisplay: 盤面のモデル「{playfieldObjectName}」が見つからないため、投入数の表示を出しません。", this);
                enabled = false;
                return;
            }
            var r = fieldGo.GetComponentInChildren<Renderer>();
            Transform field = r != null ? r.transform : fieldGo.transform;

            // 世界の大きさに直す
            float worldW = field.TransformVector(new Vector3(width, 0f, 0f)).magnitude;
            float worldH = field.TransformVector(new Vector3(0f, height, 0f)).magnitude;
            float canvasH = CanvasW * worldH / Mathf.Max(0.0001f, worldW);

            var go = new GameObject("InsertCounterDisplay");
            go.transform.SetPositionAndRotation(field.TransformPoint(center), field.rotation);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(CanvasW, canvasH);
            go.transform.localScale = Vector3.one * (worldW / CanvasW);

            // 画面：ふちの光と、暗い板
            border = MakeImage("Border", go.transform, CanvasW, canvasH, borderColor);
            MakeImage("Panel", go.transform, CanvasW - 10f, canvasH - 10f, panelColor);
            // 走査線のような、うすい横線
            for (float y = 6f; y < canvasH - 6f; y += 8f)
            {
                var line = MakeImage("Scan", go.transform, CanvasW - 10f, 2f, new Color(1f, 1f, 1f, 0.03f));
                line.rectTransform.anchoredPosition = new Vector2(0f, canvasH * 0.5f - y);
            }

            label = MakeText("Label", go.transform, "投入", Mathf.RoundToInt(canvasH * 0.42f), mutedColor, TextAnchor.MiddleLeft, BodyFont());
            label.rectTransform.sizeDelta = new Vector2(240f, canvasH);
            label.rectTransform.anchoredPosition = new Vector2(-CanvasW * 0.5f + 40f + 120f, 0f);

            value = MakeText("Value", go.transform, "", Mathf.RoundToInt(canvasH * 0.72f), textColor, TextAnchor.MiddleRight, NumberFont());
            value.supportRichText = true;
            value.rectTransform.sizeDelta = new Vector2(CanvasW - 300f, canvasH);
            value.rectTransform.anchoredPosition = new Vector2(CanvasW * 0.5f - 40f - (CanvasW - 300f) * 0.5f, 0f);

            SetLayer(go.transform, sharp ? game.monitor.overlayLayer : 0);
        }

        void Update()
        {
            if (canvas == null) return;
            ResolveFonts();

            int inserted = game.InsertedThisRound;
            int limit = game.InsertLimit;
            int left = Mathf.Max(0, limit - inserted);
            if (inserted != lastInserted)
            {
                if (lastInserted >= 0 && inserted > lastInserted) bump = 1f;
                lastInserted = inserted;
            }
            bump = Mathf.MoveTowards(bump, 0f, Time.deltaTime * 5f);

            Color c = textColor;
            if (left <= 0) c = Color.Lerp(emptyColor, textColor, Mathf.PingPong(Time.time * 2f, 1f) * 0.5f);
            else if (left <= lowLeft) c = lowColor;
            string limitHex = ColorUtility.ToHtmlStringRGB(mutedColor);
            int small = Mathf.RoundToInt(value.fontSize * 0.6f);
            value.text = $"{inserted}<color=#{limitHex}><size={small}> / {limit}</size></color>";
            value.color = c;
            float s = 1f + 0.15f * bump;
            value.rectTransform.localScale = new Vector3(s, s, 1f);

            Color b = left <= 0 ? emptyColor : (left <= lowLeft ? lowColor : borderColor);
            border.color = Color.Lerp(b, Color.white, 0.4f * bump);
        }

        /// <summary>モニターができてから、フォントをモニターと同じ物にそろえる。</summary>
        void ResolveFonts()
        {
            if (fontsResolved || game.Monitor == null) return;
            label.font = BodyFont();
            value.font = NumberFont();
            fontsResolved = true;
        }

        Font NumberFont()
        {
            var f = game.Monitor != null ? game.Monitor.NumberFont : null;
            return f != null ? f : fallbackFont;
        }

        Font BodyFont()
        {
            var f = game.Monitor != null ? game.Monitor.BodyFont : null;
            return f != null ? f : fallbackFont;
        }

        static Image MakeImage(string name, Transform parent, float w, float h, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            img.rectTransform.sizeDelta = new Vector2(w, h);
            return img;
        }

        static Text MakeText(string name, Transform parent, string text, int size, Color color, TextAnchor align, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }
}
