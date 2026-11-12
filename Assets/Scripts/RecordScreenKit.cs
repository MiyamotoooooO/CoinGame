using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// レコードのモニター画面の「飾り」（RecordScriptBase の続き）。1枚ずつのスクリプトは何も書かなくても付く。
    ///
    /// ・背景：テーマの色のグラデーション、流れる斜めの線、まん中のぼんやりした光（曲の盛り上がりで明るさが変わる）
    /// ・手前：走査線、ふちの暗がり、ときどき横切る光の帯、四すみの光るかぎ、曲に合わせて光る枠
    /// ・開く時：テーマの色の幕に大きなタイトルが出て、横へはけて中身が見える（ワイプ）
    /// ・Panel：上のハイライトと光る縁　・Bar：グラデーションと、先に光の玉　・Lbl：影
    /// ・Pop・PopCenter：キラキラと光の輪が飛ぶ（PopCenter は画面も光る）
    /// ・自分で呼べる道具：ScreenFlash（光らせる）、ScreenBurst（キラキラ）、ScreenRing（光の輪）
    ///
    /// テーマの色は、そのレコードの大きな演出（RecordSpectacle）と同じ色。ScreenTheme を書けば変えられる。
    /// </summary>
    public abstract partial class RecordScriptBase
    {
        [Header("モニターの飾り")]
        [Tooltip("モニターの画面に、背景・枠の光・開く時のワイプなどの飾りを付ける")]
        public bool screenDecor = true;

        /// <summary>モニターの画面のテーマの色</summary>
        protected virtual Color ScreenTheme
        {
            get
            {
                if (RecordSpectacle.TryGetTheme(recordKey, out var c)) return c;
                var r = effects != null ? effects.Current : null;
                return r != null ? RecordArtwork.CategoryColor(r.category) : new Color(1f, 0.6f, 0.2f);
            }
        }

        // ---- 作った画像（全部のレコードで使い回す） ----
        static Sprite sGradV, sGradH, sEdge, sSoft, sDiamond, sRing;
        static Texture2D tStripes, tScan;

        class BarFx { public Image fill, tip; public float left, w; }
        class Spark { public RectTransform rt; public Image img; public Vector2 vel; public float life, max, spin; }
        class RingFx { public RectTransform rt; public Image img; public float t, size; }

        readonly List<BarFx> screenBars = new List<BarFx>();
        readonly List<Spark> sparks = new List<Spark>();
        readonly List<RingFx> rings = new List<RingFx>();
        RectTransform fxFront;
        RawImage fxStripes, fxScan;
        Image fxGlow, fxSweep, fxFlash, fxCover, fxCoverEdge;
        Text fxCoverTitle, fxCoverSub;
        readonly List<Image> fxBorder = new List<Image>();
        float fxTime, fxFlashA;
        Color fxFlashColor, fxTheme;
        RecordAudioPlayer audioForHype;

        // ================================================================
        // 作る
        // ================================================================

        void BuildScreenBackdrop()
        {
            ResetScreenFx();
            if (!screenDecor || Screen == null) return;
            MakeSprites();
            fxTheme = ScreenTheme;
            float W = BodyW, H = BodyH;
            Color dark = Color.Lerp(fxTheme, Color.black, 0.86f);
            Img(Screen, "DecoBase", W * 0.5f, H * 0.5f, W, H, dark);
            var grad = Img(Screen, "DecoGrad", W * 0.5f, H * 0.5f, W, H, new Color(fxTheme.r, fxTheme.g, fxTheme.b, 0.32f));
            grad.sprite = sGradV;
            fxStripes = Raw(Screen, "DecoStripes", W * 0.5f, H * 0.5f, W, H, tStripes, new Color(fxTheme.r, fxTheme.g, fxTheme.b, 0.07f));
            fxStripes.uvRect = new Rect(0f, 0f, W / 48f, H / 48f);
            fxGlow = Img(Screen, "DecoGlow", W * 0.5f, H * 0.45f, W * 1.1f, H * 1.1f, new Color(fxTheme.r, fxTheme.g, fxTheme.b, 0.2f));
            fxGlow.sprite = sSoft;
            // 下の床の線（奥へ消えていく）
            for (int i = 0; i < 5; i++)
            {
                float y = H - 18f - i * i * 7f;
                var line = Img(Screen, "DecoFloor", W * 0.5f, y, W, 2f, new Color(fxTheme.r, fxTheme.g, fxTheme.b, 0.18f - i * 0.03f));
                line.raycastTarget = false;
            }
            foreach (var g in Screen.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        }

        void BuildScreenForeground()
        {
            if (!screenDecor || Screen == null) return;
            float W = BodyW, H = BodyH;
            fxFront = Grp(Screen, "DecoFront", W * 0.5f, H * 0.5f, W, H);

            fxScan = Raw(fxFront, "Scan", W * 0.5f, H * 0.5f, W, H, tScan, new Color(0f, 0f, 0f, 0.16f));
            fxScan.uvRect = new Rect(0f, 0f, 1f, H / 4f);

            // ふちの暗がり
            EdgeShade(W * 0.5f, 30f, W, 60f, 0f);
            EdgeShade(W * 0.5f, H - 30f, W, 60f, 180f);
            EdgeShade(30f, H * 0.5f, H, 60f, 90f, true);
            EdgeShade(W - 30f, H * 0.5f, H, 60f, -90f, true);

            // 横切る光の帯
            fxSweep = Img(fxFront, "Sweep", -200f, H * 0.5f, 140f, H * 1.8f, new Color(1f, 1f, 1f, 0.1f));
            fxSweep.sprite = sSoft;
            fxSweep.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -18f);

            // 光る枠と、四すみのかぎ
            Color edge = Color.Lerp(fxTheme, Color.white, 0.25f);
            fxBorder.Add(Img(fxFront, "BorderT", W * 0.5f, 1.5f, W, 3f, edge));
            fxBorder.Add(Img(fxFront, "BorderB", W * 0.5f, H - 1.5f, W, 3f, edge));
            fxBorder.Add(Img(fxFront, "BorderL", 1.5f, H * 0.5f, 3f, H, edge));
            fxBorder.Add(Img(fxFront, "BorderR", W - 1.5f, H * 0.5f, 3f, H, edge));
            Color hot = Color.Lerp(fxTheme, Color.white, 0.6f);
            foreach (var (x, y, sx, sy) in new[] { (0f, 0f, 1f, 1f), (W, 0f, -1f, 1f), (0f, H, 1f, -1f), (W, H, -1f, -1f) })
            {
                Img(fxFront, "Corner", x + sx * 26f, y + sy * 4f, 52f, 8f, hot);
                Img(fxFront, "Corner", x + sx * 4f, y + sy * 26f, 8f, 52f, hot);
                var dot = Img(fxFront, "CornerGlow", x + sx * 10f, y + sy * 10f, 70f, 70f, new Color(hot.r, hot.g, hot.b, 0.45f));
                dot.sprite = sSoft;
            }

            fxFlash = Img(fxFront, "Flash", W * 0.5f, H * 0.5f, W, H, new Color(1f, 1f, 1f, 0f));

            // 開く時の幕（テーマの色。大きなタイトル → 横へはける）
            fxCover = Img(fxFront, "Cover", W * 0.5f, H * 0.5f, W, H, Color.Lerp(fxTheme, Color.black, 0.35f));
            var cg = Img(fxCover.rectTransform, "CoverGrad", W * 0.5f, H * 0.5f, W, H, new Color(1f, 1f, 1f, 0.18f));
            cg.sprite = sGradV;
            fxCoverEdge = Img(fxFront, "CoverEdge", 0f, H * 0.5f, 10f, H, Color.white);
            fxCoverTitle = Lbl(fxCover.rectTransform, "CoverTitle", W * 0.5f, H * 0.44f, W, 120f, ScreenTitle, 96, Color.white, TextAnchor.MiddleCenter, true);
            fxCoverSub = Lbl(fxCover.rectTransform, "CoverSub", W * 0.5f, H * 0.62f, W, 50f, ScreenSubtitle, 30, new Color(1f, 1f, 1f, 0.85f));
            var o = fxCoverTitle.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.5f);
            o.effectDistance = new Vector2(3f, -3f);

            foreach (var g in fxFront.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            fxTime = 0f;
        }

        void EdgeShade(float cx, float cy, float w, float h, float rot, bool sideways = false)
        {
            var img = Img(fxFront, "Shade", cx, cy, sideways ? w : w, h, new Color(0f, 0f, 0f, 0.45f));
            img.sprite = sEdge;
            if (sideways) img.rectTransform.sizeDelta = new Vector2(w, h);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rot);
        }

        RawImage Raw(Transform parent, string name, float cx, float cy, float w, float h, Texture tex, Color color)
        {
            var g = Grp(parent, name, cx, cy, w, h);
            var r = g.gameObject.AddComponent<RawImage>();
            r.texture = tex;
            r.color = color;
            r.raycastTarget = false;
            return r;
        }

        void ResetScreenFx()
        {
            screenBars.Clear();
            sparks.Clear();
            rings.Clear();
            fxBorder.Clear();
            fxFront = null;
            fxStripes = fxScan = null;
            fxGlow = fxSweep = fxFlash = fxCover = fxCoverEdge = null;
            fxCoverTitle = fxCoverSub = null;
            fxFlashA = 0f;
        }

        // ---- 部品の見た目を良くする（Lbl・Panel・Bar から呼ぶ） ----

        void DecorateLabel(Text t, int size)
        {
            if (!screenDecor || t == null) return;
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            float d = Mathf.Clamp(size * 0.07f, 1.5f, 4f);
            sh.effectDistance = new Vector2(d, -d);
        }

        void DecoratePanel(Image panel, float w, float h)
        {
            if (!screenDecor || panel == null) return;
            MakeSprites();
            Color theme = ScreenTheme;
            var rt = panel.rectTransform;
            var hi = Img(rt, "PanelHi", w * 0.5f, h * 0.25f, w, h * 0.5f, new Color(1f, 1f, 1f, 0.06f));
            hi.sprite = sGradV;
            var glow = Img(rt, "PanelGlow", w * 0.5f, h, w * 1.2f, h * 0.7f, new Color(theme.r, theme.g, theme.b, 0.16f));
            glow.sprite = sSoft;
            Color edge = new Color(theme.r, theme.g, theme.b, 0.65f);
            Img(rt, "PanelEdgeT", w * 0.5f, 1f, w, 2f, edge);
            Img(rt, "PanelEdgeB", w * 0.5f, h - 1f, w, 2f, new Color(edge.r, edge.g, edge.b, 0.35f));
            Img(rt, "PanelEdgeL", 1f, h * 0.5f, 2f, h, new Color(edge.r, edge.g, edge.b, 0.35f));
            Img(rt, "PanelEdgeR", w - 1f, h * 0.5f, 2f, h, new Color(edge.r, edge.g, edge.b, 0.35f));
            foreach (var g in rt.GetComponentsInChildren<Graphic>(true)) if (g != panel) g.raycastTarget = false;
        }

        void DecorateBar(Transform parent, Image fill, float left, float cy, float w, float h)
        {
            if (!screenDecor || fill == null) return;
            MakeSprites();
            fill.sprite = sGradH;
            var shine = Img(fill.rectTransform, "Shine", w * 0.5f, h * 0.3f, w, h * 0.4f, new Color(1f, 1f, 1f, 0.25f));
            shine.raycastTarget = false;
            var tip = Img(parent, "BarTip", left, cy, h * 2.6f, h * 2.6f, Color.Lerp(fill.color, Color.white, 0.5f));
            tip.sprite = sSoft;
            tip.raycastTarget = false;
            screenBars.Add(new BarFx { fill = fill, tip = tip, left = left, w = w });
        }

        // ================================================================
        // 自分で呼べる道具
        // ================================================================

        /// <summary>モニターの画面を光らせる</summary>
        protected void ScreenFlash(Color color, float alpha = 0.35f)
        {
            fxFlashColor = color;
            fxFlashA = Mathf.Max(fxFlashA, alpha);
        }

        /// <summary>モニターの画面の pos（中身の座標）からキラキラを飛ばす</summary>
        protected void ScreenBurst(Vector2 pos, Color color, int count = 12)
        {
            if (!screenDecor || fxFront == null) return;
            for (int i = 0; i < count; i++)
            {
                var img = Img(fxFront, "Spark", pos.x, pos.y, 18f, 18f, Color.Lerp(color, Color.white, Random.Range(0f, 0.5f)));
                img.sprite = sDiamond;
                img.raycastTarget = false;
                float a = Random.Range(0f, Mathf.PI * 2f), sp = Random.Range(160f, 520f);
                float life = Random.Range(0.45f, 0.9f);
                sparks.Add(new Spark { rt = img.rectTransform, img = img, vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, life = life, max = life, spin = Random.Range(-400f, 400f) });
            }
            ScreenRing(pos, color, 220f);
        }

        /// <summary>光の輪を広げる</summary>
        protected void ScreenRing(Vector2 pos, Color color, float size = 260f)
        {
            if (!screenDecor || fxFront == null) return;
            var img = Img(fxFront, "Ring", pos.x, pos.y, 10f, 10f, color);
            img.sprite = sRing;
            img.raycastTarget = false;
            rings.Add(new RingFx { rt = img.rectTransform, img = img, size = size });
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void UpdateScreenFx(float dt)
        {
            if (fxFront == null) return;
            fxTime += dt;
            float W = BodyW, H = BodyH;
            if (audioForHype == null) audioForHype = FindFirstObjectByType<RecordAudioPlayer>();
            float hype = audioForHype != null ? Mathf.Clamp01(audioForHype.Hype) : 0.5f + 0.5f * Mathf.Sin(fxTime * 4f);

            if (fxStripes != null) fxStripes.uvRect = new Rect(-fxTime * 0.25f, fxTime * 0.1f, W / 48f, H / 48f);
            if (fxGlow != null)
            {
                fxGlow.color = new Color(fxTheme.r, fxTheme.g, fxTheme.b, 0.14f + 0.2f * hype);
                float s = 1f + 0.05f * hype;
                fxGlow.rectTransform.localScale = new Vector3(s, s, 1f);
            }
            if (fxScan != null) fxScan.uvRect = new Rect(0f, fxTime * 0.6f, 1f, H / 4f);

            // 光の帯：5秒に1回、左から右へ
            if (fxSweep != null)
            {
                float k = Mathf.Repeat(fxTime, 5f) / 1.1f;
                Place(fxSweep.rectTransform, Mathf.Lerp(-200f, W + 200f, Mathf.Clamp01(k)), H * 0.5f);
                fxSweep.enabled = k <= 1f;
            }

            // 枠：曲に合わせて光る
            float edgeA = 0.35f + 0.55f * hype;
            Color edge = Color.Lerp(fxTheme, Color.white, 0.25f);
            foreach (var b in fxBorder) if (b != null) b.color = new Color(edge.r, edge.g, edge.b, edgeA);

            // 開く時の幕
            if (fxCover != null)
            {
                float t = fxTime;
                if (t < 0.12f) fxCover.rectTransform.localScale = Vector3.one;
                float k = Mathf.Clamp01((t - 0.75f) / 0.45f);
                float e = k * k * (3f - 2f * k);
                float x = W * 0.5f + W * 1.05f * e;
                Place(fxCover.rectTransform, x, H * 0.5f);
                Place(fxCoverEdge.rectTransform, x - W * 0.5f, H * 0.5f);
                fxCoverEdge.color = new Color(1f, 1f, 1f, e > 0f && e < 1f ? 0.9f : 0f);
                float ts = 1f + 0.25f * Mathf.Max(0f, 1f - t / 0.3f);
                fxCoverTitle.rectTransform.localScale = new Vector3(ts, ts, 1f);
                if (e >= 1f)
                {
                    Destroy(fxCover.gameObject);
                    Destroy(fxCoverEdge.gameObject);
                    fxCover = null;
                    fxCoverEdge = null;
                    ScreenFlash(Color.white, 0.25f);
                }
            }

            // 光
            fxFlashA = Mathf.MoveTowards(fxFlashA, 0f, dt * 1.8f);
            if (fxFlash != null) fxFlash.color = new Color(fxFlashColor.r, fxFlashColor.g, fxFlashColor.b, fxFlashA);

            // バーの先の光
            foreach (var b in screenBars)
            {
                if (b.fill == null || b.tip == null) continue;
                float amount = b.fill.fillAmount;
                b.tip.enabled = amount > 0.005f && amount < 0.999f;
                Place(b.tip.rectTransform, b.left + b.w * amount, -b.fill.rectTransform.anchoredPosition.y);
                Color c = Color.Lerp(b.fill.color, Color.white, 0.5f);
                b.tip.color = new Color(c.r, c.g, c.b, 0.6f + 0.3f * Mathf.Sin(fxTime * 10f));
            }

            // キラキラ
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var s = sparks[i];
                s.life -= dt;
                if (s.life <= 0f || s.rt == null)
                {
                    if (s.rt != null) Destroy(s.rt.gameObject);
                    sparks.RemoveAt(i);
                    continue;
                }
                s.vel *= Mathf.Exp(-dt * 2.5f);
                s.vel.y -= 260f * dt * -1f;
                var p = s.rt.anchoredPosition + new Vector2(s.vel.x, -s.vel.y) * dt;
                s.rt.anchoredPosition = p;
                s.rt.localRotation = Quaternion.Euler(0f, 0f, s.rt.localEulerAngles.z + s.spin * dt);
                float k = s.life / s.max;
                s.rt.localScale = Vector3.one * (0.4f + 0.8f * k);
                var c = s.img.color;
                s.img.color = new Color(c.r, c.g, c.b, k);
            }

            // 光の輪
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                r.t += dt * 2.2f;
                if (r.t >= 1f || r.rt == null)
                {
                    if (r.rt != null) Destroy(r.rt.gameObject);
                    rings.RemoveAt(i);
                    continue;
                }
                float e = 1f - (1f - r.t) * (1f - r.t);
                r.rt.sizeDelta = Vector2.one * Mathf.Lerp(20f, r.size, e);
                var c = r.img.color;
                r.img.color = new Color(c.r, c.g, c.b, 1f - r.t);
            }
        }

        // ================================================================
        // 画像をコードで作る
        // ================================================================

        static void MakeSprites()
        {
            if (sGradV != null) return;
            sGradV = SpriteFrom(Tex(4, 64, (x, y) => new Color(1f, 1f, 1f, Mathf.Pow(y / 63f, 1.4f))));               // 上が濃い
            sGradH = SpriteFrom(Tex(64, 4, (x, y) => { float v = Mathf.Lerp(0.72f, 1f, x / 63f); return new Color(v, v, v, 1f); }));
            sEdge = SpriteFrom(Tex(4, 64, (x, y) => new Color(1f, 1f, 1f, Mathf.Pow(y / 63f, 2f))));                  // 上（ふち）が濃い
            sSoft = SpriteFrom(Tex(64, 64, (x, y) =>
            {
                float dx = (x + 0.5f) / 32f - 1f, dy = (y + 0.5f) / 32f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                return new Color(1f, 1f, 1f, a * a);
            }));
            sDiamond = SpriteFrom(Tex(32, 32, (x, y) =>
            {
                float dx = Mathf.Abs((x + 0.5f) / 16f - 1f), dy = Mathf.Abs((y + 0.5f) / 16f - 1f);
                float d = dx + dy * 0.6f;
                float a = Mathf.Clamp01((1f - d) * 3f);
                return new Color(1f, 1f, 1f, a);
            }));
            sRing = SpriteFrom(Tex(96, 96, (x, y) =>
            {
                float dx = (x + 0.5f) / 48f - 1f, dy = (y + 0.5f) / 48f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.85f) / 0.12f);
                return new Color(1f, 1f, 1f, a * a);
            }));
            tStripes = Tex(32, 32, (x, y) => new Color(1f, 1f, 1f, ((x + y) % 32) < 10 ? 1f : 0f));
            tStripes.wrapMode = TextureWrapMode.Repeat;
            tScan = Tex(1, 4, (x, y) => new Color(1f, 1f, 1f, y < 2 ? 1f : 0f));
            tScan.wrapMode = TextureWrapMode.Repeat;
            tScan.filterMode = FilterMode.Point;
        }

        static Texture2D Tex(int w, int h, System.Func<int, int, Color> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "RecordScreenDeco" };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Sprite SpriteFrom(Texture2D t) => Sprite.Create(t, new Rect(0f, 0f, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
    }
}
