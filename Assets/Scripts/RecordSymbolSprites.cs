using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// スロットの絵柄（7・ベル・さくらんぼ・星・ドクロ）の画像を、コードで描いて作る。画像ファイルはいらない。
    /// 番号は GambleRecord と同じ：0 = 7、1 = ベル、2 = さくらんぼ、3 = 星、4 = ドクロ。
    /// 1回作ったら覚えておいて、使い回す。
    /// </summary>
    public static class RecordSymbolSprites
    {
        public const int Seven = 0, Bell = 1, Cherry = 2, StarSymbol = 3, Skull = 4, Count = 5;
        const int Size = 160;

        static readonly Sprite[] cache = new Sprite[Count];

        /// <summary>絵柄の画像</summary>
        public static Sprite Get(int symbol)
        {
            symbol = Mathf.Clamp(symbol, 0, Count - 1);
            if (cache[symbol] != null) return cache[symbol];
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "SlotSymbol" + symbol, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[Size * Size];
            var layers = Layers(symbol);
            float pixel = 2f / Size;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                    Color c = new Color(0f, 0f, 0f, 0f);
                    foreach (var l in layers)
                    {
                        float d = l.sd(p);
                        if (l.outline > 0f)
                        {
                            float ao = Mathf.Clamp01(0.5f - (d - l.outline) / pixel);
                            c = Over(c, new Color(l.outlineColor.r, l.outlineColor.g, l.outlineColor.b, l.outlineColor.a * ao));
                        }
                        float af = Mathf.Clamp01(0.5f - d / pixel);
                        if (af <= 0f) continue;
                        float k = Mathf.Clamp01(p.y * 0.5f + 0.5f);
                        Color f = Color.Lerp(l.bottom, l.top, k);
                        c = Over(c, new Color(f.r, f.g, f.b, f.a * af));
                    }
                    px[y * Size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            cache[symbol] = Sprite.Create(tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            cache[symbol].name = tex.name;
            return cache[symbol];
        }

        /// <summary>絵柄の色（モニターの光などに使う）</summary>
        public static Color ColorOf(int symbol)
        {
            switch (symbol)
            {
                case Seven: return new Color32(0xff, 0x3c, 0x3c, 0xff);
                case Bell: return new Color32(0xff, 0xd2, 0x3f, 0xff);
                case Cherry: return new Color32(0xff, 0x4a, 0x7a, 0xff);
                case StarSymbol: return new Color32(0x6c, 0xd8, 0xff, 0xff);
                default: return new Color32(0xc0, 0xc0, 0xd0, 0xff);
            }
        }

        // ================================================================
        // 描き方（形の「ふちまでの距離」で描くので、ふちがなめらか）
        // ================================================================

        class Layer
        {
            public Func<Vector2, float> sd;
            public Color top, bottom, outlineColor;
            public float outline;
        }

        static Layer L(Func<Vector2, float> sd, Color top, Color bottom, float outline = 0f, Color? outlineColor = null)
            => new Layer { sd = sd, top = top, bottom = bottom, outline = outline, outlineColor = outlineColor ?? new Color(0.08f, 0.05f, 0.14f, 1f) };

        static Color Over(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0.0001f) return new Color(0f, 0f, 0f, 0f);
            Color c = (over * over.a + under * under.a * (1f - over.a)) / a;
            c.a = a;
            return c;
        }

        static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, Vector2.Dot(ba, ba)));
            return (pa - ba * h).magnitude - r;
        }

        static float Polygon(Vector2 p, Vector2[] v)
        {
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i], w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Mathf.Max(1e-6f, Vector2.Dot(e, e)));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }

        static Vector2[] StarPoints(float rOut, float rIn, Vector2 c)
        {
            var v = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float r = i % 2 == 0 ? rOut : rIn;
                v[i] = c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
            return v;
        }

        static Color Hex(uint rgb, float a = 1f) => new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, a);

        static List<Layer> Layers(int symbol)
        {
            var list = new List<Layer>();
            Color white = new Color(1f, 1f, 1f, 0.85f);
            switch (symbol)
            {
                case Seven:
                {
                    var seven = new[]
                    {
                        new Vector2(-0.6f, 0.78f), new Vector2(0.64f, 0.78f), new Vector2(0.64f, 0.5f), new Vector2(0.06f, -0.8f),
                        new Vector2(-0.3f, -0.8f), new Vector2(0.26f, 0.5f), new Vector2(-0.6f, 0.5f),
                    };
                    list.Add(L(p => Polygon(p, seven) - 0.09f, Hex(0xfff0a0), Hex(0xffb020)));                 // 金のふち
                    list.Add(L(p => Polygon(p, seven), Hex(0xff6a5a), Hex(0xc80818), 0.04f));                 // 赤い 7
                    list.Add(L(p => Segment(p, new Vector2(-0.48f, 0.68f), new Vector2(0.48f, 0.68f), 0.035f), white, white));
                    list.Add(L(p => Segment(p, new Vector2(0.4f, 0.52f), new Vector2(-0.08f, -0.62f), 0.03f), new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0.2f)));
                    break;
                }
                case Bell:
                {
                    var flare = new[] { new Vector2(-0.48f, 0.15f), new Vector2(0.48f, 0.15f), new Vector2(0.7f, -0.48f), new Vector2(-0.7f, -0.48f) };
                    Func<Vector2, float> bell = p => Mathf.Min(Mathf.Min(Circle(p, new Vector2(0f, 0.12f), 0.5f), Polygon(p, flare)),
                                                               Segment(p, new Vector2(-0.7f, -0.52f), new Vector2(0.7f, -0.52f), 0.1f));
                    list.Add(L(p => Circle(p, new Vector2(0f, 0.7f), 0.11f), Hex(0xffe27a), Hex(0xd89a10), 0.04f));
                    list.Add(L(p => Circle(p, new Vector2(0f, -0.68f), 0.15f), Hex(0xffe27a), Hex(0xd89a10), 0.04f));
                    list.Add(L(bell, Hex(0xfff2a0), Hex(0xe0a010), 0.05f));
                    list.Add(L(p => Segment(p, new Vector2(-0.68f, -0.5f), new Vector2(0.68f, -0.5f), 0.025f), Hex(0xb07808), Hex(0xb07808)));
                    list.Add(L(p => Segment(p, new Vector2(-0.22f, 0.42f), new Vector2(-0.4f, -0.25f), 0.055f), white, new Color(1f, 1f, 1f, 0.3f)));
                    break;
                }
                case Cherry:
                {
                    Vector2 a = new Vector2(-0.36f, -0.36f), b = new Vector2(0.3f, -0.46f), top = new Vector2(0.12f, 0.62f);
                    list.Add(L(p => Segment(p, a + new Vector2(0.05f, 0.28f), top, 0.05f), Hex(0x6ad04a), Hex(0x2a8a2a), 0.035f));
                    list.Add(L(p => Segment(p, b + new Vector2(0f, 0.28f), top, 0.05f), Hex(0x6ad04a), Hex(0x2a8a2a), 0.035f));
                    // 葉っぱ（2つの円の重なり）
                    list.Add(L(p => Mathf.Max(Circle(p, new Vector2(0.22f, 0.42f), 0.38f), Circle(p, new Vector2(0.58f, 0.86f), 0.38f)), Hex(0x8ae05a), Hex(0x2f9a3a), 0.035f));
                    list.Add(L(p => Circle(p, a, 0.33f), Hex(0xff5a6a), Hex(0xb0081a), 0.045f));
                    list.Add(L(p => Circle(p, b, 0.33f), Hex(0xff5a6a), Hex(0xb0081a), 0.045f));
                    list.Add(L(p => Circle(p, a + new Vector2(-0.12f, 0.12f), 0.08f), white, white));
                    list.Add(L(p => Circle(p, b + new Vector2(-0.12f, 0.12f), 0.08f), white, white));
                    break;
                }
                case StarSymbol:
                {
                    var outer = StarPoints(0.86f, 0.38f, new Vector2(0f, -0.04f));
                    var inner = StarPoints(0.5f, 0.22f, new Vector2(-0.04f, 0.02f));
                    list.Add(L(p => Polygon(p, outer) - 0.06f, Hex(0xffffff), Hex(0xbfe8ff)));
                    list.Add(L(p => Polygon(p, outer), Hex(0x8ae8ff), Hex(0x1a7ad8), 0.04f));
                    list.Add(L(p => Polygon(p, inner), new Color(1f, 1f, 1f, 0.55f), new Color(1f, 1f, 1f, 0.1f)));
                    break;
                }
                default:
                {
                    var jaw = new[] { new Vector2(-0.4f, -0.12f), new Vector2(0.4f, -0.12f), new Vector2(0.32f, -0.74f), new Vector2(-0.32f, -0.74f) };
                    var nose = new[] { new Vector2(0f, -0.08f), new Vector2(0.09f, -0.26f), new Vector2(-0.09f, -0.26f) };
                    list.Add(L(p => Mathf.Min(Circle(p, new Vector2(0f, 0.16f), 0.62f), Polygon(p, jaw) - 0.06f), Hex(0xf4f4f8), Hex(0xa8a8b8), 0.05f));
                    Color hole = Hex(0x1a1426);
                    list.Add(L(p => Circle(p, new Vector2(-0.25f, 0.12f), 0.18f), hole, hole));
                    list.Add(L(p => Circle(p, new Vector2(0.25f, 0.12f), 0.18f), hole, hole));
                    list.Add(L(p => Circle(p, new Vector2(-0.25f, 0.12f), 0.05f), Hex(0xff3a5a), Hex(0xff3a5a)));
                    list.Add(L(p => Circle(p, new Vector2(0.25f, 0.12f), 0.05f), Hex(0xff3a5a), Hex(0xff3a5a)));
                    list.Add(L(p => Polygon(p, nose), hole, hole));
                    for (int i = -1; i <= 1; i++)
                    {
                        float x = i * 0.17f;
                        list.Add(L(p => Segment(p, new Vector2(x, -0.46f), new Vector2(x, -0.7f), 0.025f), hole, hole));
                    }
                    list.Add(L(p => Segment(p, new Vector2(-0.3f, -0.46f), new Vector2(0.3f, -0.46f), 0.02f), hole, hole));
                    break;
                }
            }
            return list;
        }
    }
}
