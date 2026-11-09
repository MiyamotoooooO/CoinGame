using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// 画面の右上に、所持ポイント（緑）と特殊ポイント（青）を横にならべて出す。
    /// 画面の左上に、所持メダル（金色）を出す。
    ///
    /// ・数字のとなりに印（ポイントは緑のメダル、特殊ポイントは青い菱形、所持メダルは金色のメダル）
    /// ・数字が変わると、数字がなめらかに追いかけ、ぽんとはずんで、下に「+30」「-150」が浮かんで消える
    ///   （続けて変わった時は、1つの「+N」にまとめる）
    /// ・エリアの始まりの演出の間と、ショップの間（ショップの画面に同じ物が出るので）は隠す
    /// ・デバッグ表示（F2）が出ている間は、左上の所持メダルを、その下へずらす
    /// ・MedalPusherGame は変えず、public の AreaPoints・SpecialPoints・OwnedMedals を読むだけ
    /// ・シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    /// </summary>
    [DisallowMultipleComponent]
    public class PointsHud : MonoBehaviour
    {
        [Tooltip("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;

        [Header("置き場所・大きさ")]
        [Tooltip("画面の右上からの余白（1920×1080 の画面のつもりの数）")]
        public Vector2 margin = new Vector2(36f, 24f);
        [Tooltip("画面の左上（所持メダル）からの余白")]
        public Vector2 medalMargin = new Vector2(36f, 24f);
        [Tooltip("デバッグ表示（F2）が出ている間、所持メダルを下へずらす量")]
        public float debugOffset = 190f;
        [Tooltip("数字の大きさ")]
        [Range(20, 120)] public int fontSize = 60;
        [Tooltip("印の大きさ")]
        [Range(16f, 120f)] public float iconSize = 52f;
        [Tooltip("ポイントと特殊ポイントの間")]
        [Range(0f, 200f)] public float gap = 48f;
        [Tooltip("ほかの画面の表示より上に出す順番")]
        public int sortingOrder = 60;

        [Header("色")]
        public Color pointColor = new Color32(0x5c, 0xe0, 0x7a, 0xff);
        public Color specialColor = new Color32(0x38, 0xc8, 0xff, 0xff);
        public Color medalColor = new Color32(0xff, 0xc2, 0x3a, 0xff);
        public Color outlineColor = new Color32(0x14, 0x12, 0x1c, 0xff);
        public Color minusColor = new Color32(0xff, 0x6b, 0x6b, 0xff);

        [Header("差し替え用（空なら仮の印）")]
        [Tooltip("ポイントの印（空ならモニターと同じ緑のメダル）")]
        public Sprite pointIcon;
        [Tooltip("特殊ポイントの印（空なら青い菱形）")]
        public Sprite specialIcon;
        [Tooltip("所持メダルの印（空ならモニターと同じ金色のメダル）")]
        public Sprite medalIcon;

        class Counter
        {
            public RectTransform root, popRoot;
            public Text text;
            public Image icon;
            public float shown;
            public int last;
            public float bump;
            public Color color;
            public FloatText lastFloat;   // 続けて変わった時に、まとめる先
            public int floatSum;
        }

        class FloatText
        {
            public RectTransform rt;
            public Text text;
            public float age;
        }

        Canvas canvas;
        CanvasGroup group;
        RectTransform row, medalRow;
        Counter points, special, medals;
        readonly List<FloatText> floats = new List<FloatText>();
        Font fallbackFont;
        Sprite madeDiamond;
        bool iconsResolved;

        /// <summary>シーンに無ければ、MedalPusherGame の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<PointsHud>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<PointsHud>();
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

        // ================================================================
        // 組み立て
        // ================================================================

        void Build()
        {
            var go = new GameObject("PointsHud");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            group = go.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // 右上にそろえる横ならび（右から 特殊ポイント、ポイント）
            row = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(go.transform, false);
            row.anchorMin = row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(1f, 1f);
            row.anchoredPosition = new Vector2(-margin.x, -margin.y);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = gap;
            layout.childAlignment = TextAnchor.UpperRight;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fit = row.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            points = MakeCounter(row, "Points", pointColor, false);
            special = MakeCounter(row, "Special", specialColor, false);
            points.shown = points.last = game.AreaPoints;
            special.shown = special.last = game.SpecialPoints;
            points.text.text = Format(points.last);
            special.text.text = Format(special.last);

            // 左上：所持メダル（印が左、数字が右）
            medalRow = new GameObject("MedalRow", typeof(RectTransform)).GetComponent<RectTransform>();
            medalRow.SetParent(go.transform, false);
            medalRow.anchorMin = medalRow.anchorMax = new Vector2(0f, 1f);
            medalRow.pivot = new Vector2(0f, 1f);
            medalRow.anchoredPosition = new Vector2(medalMargin.x, -medalMargin.y);
            var mfit = medalRow.gameObject.AddComponent<ContentSizeFitter>();
            mfit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            mfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var mlayout = medalRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            mlayout.childAlignment = TextAnchor.UpperLeft;
            mlayout.childControlWidth = true;
            mlayout.childControlHeight = true;
            mlayout.childForceExpandWidth = false;
            mlayout.childForceExpandHeight = false;
            medals = MakeCounter(medalRow, "Medals", medalColor, true);
            medals.shown = medals.last = game.OwnedMedals;
            medals.text.text = Format(medals.last);
        }

        /// <summary>数字と印の組を1つ作る。iconFirst が true なら印が左（左上用）。</summary>
        Counter MakeCounter(RectTransform parent, string name, Color color, bool iconFirst)
        {
            var c = new Counter { color = color };
            var item = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            item.SetParent(parent, false);
            var h = item.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childAlignment = iconFirst ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            c.root = item;

            // 数字（ふちどりと影で、どんな背景でも読めるように）
            var textGo = new GameObject("Value", typeof(RectTransform));
            textGo.transform.SetParent(item, false);
            c.text = textGo.AddComponent<Text>();
            c.text.font = NumberFont();
            c.text.fontSize = fontSize;
            c.text.fontStyle = FontStyle.Bold;
            c.text.alignment = iconFirst ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            c.text.color = color;
            c.text.horizontalOverflow = HorizontalWrapMode.Overflow;
            c.text.verticalOverflow = VerticalWrapMode.Overflow;
            c.text.raycastTarget = false;
            var outline = textGo.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(3f, -3f);
            var shadow = textGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(4f, -6f);

            // 印
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(item, false);
            c.icon = iconGo.AddComponent<Image>();
            c.icon.raycastTarget = false;
            c.icon.preserveAspect = true;
            var le = iconGo.AddComponent<LayoutElement>();
            le.preferredWidth = iconSize;
            le.preferredHeight = iconSize;
            if (iconFirst) iconGo.transform.SetSiblingIndex(0);

            // 「+30」を出す所（数字の下）
            c.popRoot = new GameObject("Pops", typeof(RectTransform)).GetComponent<RectTransform>();
            c.popRoot.SetParent(item, false);
            c.popRoot.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            c.popRoot.anchorMin = c.popRoot.anchorMax = new Vector2(0.5f, 0f);
            c.popRoot.anchoredPosition = new Vector2(iconFirst ? iconSize * 0.5f : -iconSize * 0.5f, -10f);
            return c;
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (game == null || canvas == null) return;
            ResolveIcons();

            // エリアの始まりの演出の間と、ショップの間は隠す
            bool hide = game.State == GameState.Intro || game.State == GameState.Shop;
            group.alpha = Mathf.MoveTowards(group.alpha, hide ? 0f : 1f, Time.unscaledDeltaTime * 4f);

            // デバッグ表示（左上）が出ている間は、所持メダルをその下へ
            float y = medalMargin.y + (DebugHud.Visible ? debugOffset : 0f);
            medalRow.anchoredPosition = new Vector2(medalMargin.x, -y);

            UpdateCounter(points, game.AreaPoints);
            UpdateCounter(special, game.SpecialPoints);
            UpdateCounter(medals, game.OwnedMedals);
            UpdateFloats();
        }

        void UpdateCounter(Counter c, int value)
        {
            if (value != c.last)
            {
                int diff = value - c.last;
                c.last = value;
                c.bump = 1f;
                SpawnFloat(c, diff);
            }
            // 数字は、なめらかに追いかける（差が大きいほど速く）
            c.shown = Mathf.MoveTowards(c.shown, value, Mathf.Max(30f, Mathf.Abs(value - c.shown) * 6f) * Time.unscaledDeltaTime);
            c.text.text = Format(Mathf.RoundToInt(c.shown));

            c.bump = Mathf.MoveTowards(c.bump, 0f, Time.unscaledDeltaTime * 4f);
            float s = 1f + 0.18f * c.bump * c.bump;
            c.text.rectTransform.localScale = new Vector3(s, s, 1f);
            c.icon.rectTransform.localScale = new Vector3(s, s, 1f);
            c.text.color = Color.Lerp(c.color, Color.white, 0.5f * c.bump);
        }

        void SpawnFloat(Counter c, int diff)
        {
            // 少し前に出した「+N」が残っていて、同じ向き（増える・減る）なら、それにまとめる
            var lf = c.lastFloat;
            if (lf != null && lf.rt != null && lf.age < 0.4f && (c.floatSum > 0) == (diff > 0))
            {
                c.floatSum += diff;
                lf.text.text = c.floatSum > 0 ? $"+{c.floatSum:N0}" : $"{c.floatSum:N0}";
                lf.age = 0f;
                return;
            }
            c.floatSum = diff;

            var go = new GameObject("Float", typeof(RectTransform));
            go.transform.SetParent(c.popRoot, false);
            var t = go.AddComponent<Text>();
            t.font = NumberFont();
            t.fontSize = Mathf.RoundToInt(fontSize * 0.55f);
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.UpperCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = diff > 0 ? $"+{diff:N0}" : $"{diff:N0}";
            t.color = diff > 0 ? c.color : minusColor;
            var o = go.AddComponent<Outline>();
            o.effectColor = outlineColor;
            o.effectDistance = new Vector2(2f, -2f);
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(300f, fontSize);
            rt.anchoredPosition = Vector2.zero;
            var ft = new FloatText { rt = rt, text = t };
            floats.Add(ft);
            c.lastFloat = ft;
        }

        void UpdateFloats()
        {
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                var f = floats[i];
                f.age += Time.unscaledDeltaTime;
                if (f.rt == null || f.age > 1.2f)
                {
                    if (f.rt != null) Destroy(f.rt.gameObject);
                    floats.RemoveAt(i);
                    continue;
                }
                f.rt.anchoredPosition = new Vector2(0f, -8f - 30f * f.age);   // 下へ流れる
                var col = f.text.color;
                col.a = Mathf.Clamp01(1.6f - f.age * 1.6f);
                f.text.color = col;
            }
        }

        static string Format(int v) => v.ToString("N0");

        // ================================================================
        // 印とフォント
        // ================================================================

        /// <summary>モニターができてから、印とフォントをモニターと同じ物にそろえる。</summary>
        void ResolveIcons()
        {
            if (iconsResolved) return;
            var monitor = game.Monitor;
            Sprite coin = pointIcon != null ? pointIcon : (monitor != null ? monitor.PointCoinSprite : null);
            if (coin == null && monitor == null) return;   // モニターがまだ無い
            points.icon.sprite = coin;
            points.icon.enabled = coin != null;
            special.icon.sprite = specialIcon != null ? specialIcon : Diamond();
            Sprite medal = medalIcon != null ? medalIcon : (monitor != null ? monitor.MedalCoinSprite : null);
            medals.icon.sprite = medal;
            medals.icon.enabled = medal != null;
            var font = NumberFont();
            points.text.font = font;
            special.text.font = font;
            medals.text.font = font;
            iconsResolved = true;
        }

        Font NumberFont()
        {
            var monitor = game != null ? game.Monitor : null;
            var f = monitor != null ? monitor.NumberFont : null;
            return f != null ? f : fallbackFont;
        }

        /// <summary>仮の特殊ポイントの印：青い菱形（ふちが濃く、左上が明るい）。</summary>
        Sprite Diamond()
        {
            if (madeDiamond != null) return madeDiamond;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            Color main = specialColor;
            Color dark = new Color(main.r * 0.35f, main.g * 0.45f, main.b * 0.7f, 1f);
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    float x = (i + 0.5f) / size * 2f - 1f;
                    float y = (j + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Abs(x) + Mathf.Abs(y);           // 菱形の中は 1 より小さい
                    float a = Mathf.Clamp01((1f - d) * size * 0.5f + 0.5f);
                    Color c = d > 0.8f ? dark : main;
                    float light = Mathf.Clamp01(1f - Mathf.Sqrt((x + 0.25f) * (x + 0.25f) + (y - 0.3f) * (y - 0.3f)) / 0.45f) * 0.45f;
                    c = Color.Lerp(c, Color.white, light);
                    c.a = a;
                    px[j * size + i] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            madeDiamond = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return madeDiamond;
        }
    }
}
