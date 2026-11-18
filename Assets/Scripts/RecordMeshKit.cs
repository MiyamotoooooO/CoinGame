using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedalPusher
{
    /// <summary>
    /// レコードの演出で使う「形」と「模様」と「素材」をコードで作る道具箱。
    /// 盤面のスクリプト（RecordScriptBase）と、大きな演出（RecordSpectacle）の両方から使える。
    ///
    /// ・形：ねじれた水の柱（Funnel）、平たい輪（Annulus）、ドーナツの一部（Torus）、とんがり屋根の一切れ（ConeSector）、はためく布（Cloth）
    /// ・模様：水のすじ、渦潮、ブラックホールの光の円盤、波紋、光の輪
    /// ・素材：重ねるほど明るい光（Additive）、すける色（AlphaBlend）、ガラス（Glass）
    /// 作った形と模様は覚えておき、2回目からは使い回す。
    /// </summary>
    public static class RecordMeshKit
    {
        // ================================================================
        // 素材
        // ================================================================

        static Shader additiveShader, alphaShader;

        /// <summary>重ねるほど明るくなる光の素材（両面が見える）</summary>
        public static Material Additive(Texture tex, Color color)
        {
            if (additiveShader == null) additiveShader = Shader.Find("Legacy Shaders/Particles/Additive");
            var m = new Material(additiveShader != null ? additiveShader : Shader.Find("Sprites/Default"));
            m.mainTexture = tex != null ? tex : Texture2D.whiteTexture;
            SetColor(m, color);
            return m;
        }

        /// <summary>すける色の素材（両面が見える）</summary>
        public static Material AlphaBlend(Texture tex, Color color)
        {
            if (alphaShader == null) alphaShader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            var m = new Material(alphaShader != null ? alphaShader : Shader.Find("Sprites/Default"));
            m.mainTexture = tex != null ? tex : Texture2D.whiteTexture;
            SetColor(m, color);
            return m;
        }

        /// <summary>Additive・AlphaBlend の色を変える</summary>
        public static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a * 0.5f));
            m.color = c;
        }

        /// <summary>
        /// ガラスの素材（光が当たって、つやがあり、すける）。tint の a がすけ具合（0.1〜0.4 くらいがガラスらしい）
        /// </summary>
        public static Material Glass(Color tint, float smoothness = 0.97f, float metallic = 0.05f)
        {
            var m = ProtoAssets.MakeMaterial(tint, metallic, smoothness);
            if (m.HasProperty("_Mode"))
            {
                // Standard シェーダーを「Transparent」にする（光の反射は残して、色だけすける）
                m.SetFloat("_Mode", 3f);
                m.SetInt("_SrcBlend", (int)BlendMode.One);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.DisableKeyword("_ALPHABLEND_ON");
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            m.renderQueue = 3000;
            return m;
        }

        /// <summary>形の影を消す（すける物・光る物に）</summary>
        public static void NoShadow(GameObject go)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        // ================================================================
        // 形
        // ================================================================

        static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        static Mesh Cached(string key) => meshCache.TryGetValue(key, out var m) && m != null ? m : null;

        static Mesh Finish(string key, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
        {
            var mesh = new Mesh { name = "RecordMeshKit_" + key };
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// 下が細く上が太い、ねじれた筒（竜巻・渦潮の水の柱）。原点が下のまん中で、上に height 伸びる。
        /// UV：u がまわり（0〜1）、v が高さ（0〜1）。両面が見える
        /// </summary>
        public static Mesh Funnel(float rBottom, float rTop, float height, float twistTurns, int seg = 40, int rows = 24, float flare = 1.8f)
        {
            string key = $"Funnel_{rBottom:0.###}_{rTop:0.###}_{height:0.###}_{twistTurns:0.##}_{seg}_{rows}_{flare:0.##}";
            var c = Cached(key);
            if (c != null) return c;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                int start = v.Count;
                for (int j = 0; j <= rows; j++)
                {
                    float k = j / (float)rows;
                    float r = Mathf.Lerp(rBottom, rTop, Mathf.Pow(k, flare));
                    float twist = k * twistTurns * Mathf.PI * 2f;
                    for (int i = 0; i <= seg; i++)
                    {
                        float a = i / (float)seg * Mathf.PI * 2f + twist;
                        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        v.Add(dir * r + Vector3.up * (k * height));
                        n.Add(side == 0 ? dir : -dir);
                        uv.Add(new Vector2(i / (float)seg, k));
                    }
                }
                int w = seg + 1;
                for (int j = 0; j < rows; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = start + j * w + i, b = a + 1, cc = a + w, d = cc + 1;
                        if (side == 0) { t.Add(a); t.Add(cc); t.Add(b); t.Add(b); t.Add(cc); t.Add(d); }
                        else { t.Add(a); t.Add(b); t.Add(cc); t.Add(b); t.Add(d); t.Add(cc); }
                    }
            }
            return Finish(key, v, n, uv, t);
        }

        /// <summary>
        /// 平たい輪（XZ の面。上から見える）。UV：u がまわり（0〜1）、v が内側 0 → 外側 1。両面が見える
        /// </summary>
        public static Mesh Annulus(float rInner, float rOuter, int seg = 64, int rings = 6)
        {
            string key = $"Annulus_{rInner:0.###}_{rOuter:0.###}_{seg}_{rings}";
            var c = Cached(key);
            if (c != null) return c;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                int start = v.Count;
                for (int j = 0; j <= rings; j++)
                {
                    float k = j / (float)rings;
                    float r = Mathf.Lerp(rInner, rOuter, k);
                    for (int i = 0; i <= seg; i++)
                    {
                        float a = i / (float)seg * Mathf.PI * 2f;
                        v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                        n.Add(side == 0 ? Vector3.up : Vector3.down);
                        uv.Add(new Vector2(i / (float)seg, k));
                    }
                }
                int w = seg + 1;
                for (int j = 0; j < rings; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = start + j * w + i, b = a + 1, cc = a + w, d = cc + 1;
                        if (side == 0) { t.Add(a); t.Add(b); t.Add(cc); t.Add(b); t.Add(d); t.Add(cc); }
                        else { t.Add(a); t.Add(cc); t.Add(b); t.Add(b); t.Add(cc); t.Add(d); }
                    }
            }
            return Finish(key, v, n, uv, t);
        }

        /// <summary>
        /// ドーナツ（XY の面に立つ。手前から見える）。arcDeg を 360 より小さくすると、startDeg から arcDeg 分だけの曲がった管（U字磁石など）。
        /// 角度は右（+X）が 0 で、反時計回り
        /// </summary>
        public static Mesh Torus(float radius, float tube, float startDeg = 0f, float arcDeg = 360f, int seg = 40, int tubeSeg = 14)
        {
            string key = $"Torus_{radius:0.###}_{tube:0.###}_{startDeg:0.#}_{arcDeg:0.#}_{seg}_{tubeSeg}";
            var c = Cached(key);
            if (c != null) return c;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = (startDeg + arcDeg * i / seg) * Mathf.Deg2Rad;
                var center = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
                var outward = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                for (int j = 0; j <= tubeSeg; j++)
                {
                    float b = j / (float)tubeSeg * Mathf.PI * 2f;
                    var nn = outward * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    v.Add(center + nn * tube);
                    n.Add(nn);
                    uv.Add(new Vector2(i / (float)seg, j / (float)tubeSeg));
                }
            }
            int w = tubeSeg + 1;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < tubeSeg; j++)
                {
                    int a = i * w + j, b = a + 1, cc = a + w, d = cc + 1;
                    t.Add(a); t.Add(cc); t.Add(b); t.Add(b); t.Add(cc); t.Add(d);
                }
            // 曲がった管のはしにふたをする
            if (arcDeg < 359.9f)
            {
                for (int end = 0; end < 2; end++)
                {
                    float a = (startDeg + (end == 0 ? 0f : arcDeg)) * Mathf.Deg2Rad;
                    var center = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
                    var tangent = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f) * (end == 0 ? -1f : 1f);
                    int ci = v.Count;
                    v.Add(center); n.Add(tangent); uv.Add(new Vector2(0.5f, 0.5f));
                    int ring = end == 0 ? 0 : seg * w;
                    for (int j = 0; j <= tubeSeg; j++) { v.Add(v[ring + j]); n.Add(tangent); uv.Add(new Vector2(0.5f, 0.5f)); }
                    for (int j = 0; j < tubeSeg; j++)
                    {
                        if (end == 0) { t.Add(ci); t.Add(ci + 1 + j); t.Add(ci + 2 + j); }
                        else { t.Add(ci); t.Add(ci + 2 + j); t.Add(ci + 1 + j); }
                    }
                }
            }
            return Finish(key, v, n, uv, t);
        }

        /// <summary>
        /// とんがり屋根の一切れ（原点が底のまん中、上に height）。a0Deg から a1Deg までの扇形。下の面も見える
        /// </summary>
        public static Mesh ConeSector(float radius, float height, float a0Deg, float a1Deg, int seg = 6)
        {
            string key = $"ConeSector_{radius:0.###}_{height:0.###}_{a0Deg:0.#}_{a1Deg:0.#}_{seg}";
            var c = Cached(key);
            if (c != null) return c;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var top = Vector3.up * height;
            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a = Mathf.Lerp(a0Deg, a1Deg, i / (float)seg) * Mathf.Deg2Rad;
                    float b = Mathf.Lerp(a0Deg, a1Deg, (i + 1) / (float)seg) * Mathf.Deg2Rad;
                    var pa = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    var pb = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius;
                    Vector3 cr = Vector3.Cross(pb - pa, top - pa);
                    bool crossOut = Vector3.Dot(cr, pa + pb) > 0f;   // (pa, pb, top) の順で外を向くか
                    Vector3 outN = (crossOut ? cr : -cr).normalized;
                    bool wantOut = side == 0;
                    Vector3 nn = wantOut ? outN : -outN;
                    int s = v.Count;
                    v.Add(pa); v.Add(pb); v.Add(top);
                    n.Add(nn); n.Add(nn); n.Add(nn);
                    uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(0.5f, 1f));
                    if (crossOut == wantOut) { t.Add(s); t.Add(s + 1); t.Add(s + 2); }
                    else { t.Add(s); t.Add(s + 2); t.Add(s + 1); }
                }
            }
            return Finish(key, v, n, uv, t);
        }

        // ---- はためく布 ----

        /// <summary>
        /// 布（XY の面。左のはし x=0 が旗ざおにつく所、右へ width、下へ height）。両面が見える。
        /// 毎フレーム WaveCloth で波打たせる（このメッシュは1つずつ作るので、使い回さない）
        /// </summary>
        public static Mesh Cloth(float width, float height, int nx, int ny, out Vector3[] basePositions)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                int start = v.Count;
                for (int j = 0; j <= ny; j++)
                    for (int i = 0; i <= nx; i++)
                    {
                        v.Add(new Vector3(width * i / nx, -height * j / ny, 0f));
                        n.Add(side == 0 ? Vector3.back : Vector3.forward);
                        uv.Add(new Vector2(i / (float)nx, 1f - j / (float)ny));
                    }
                int w = nx + 1;
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int a = start + j * w + i, b = a + 1, c = a + w, d = c + 1;
                        if (side == 0) { t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c); }
                        else { t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d); }
                    }
            }
            var mesh = new Mesh { name = "RecordMeshKit_Cloth" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.MarkDynamic();
            mesh.RecalculateBounds();
            basePositions = v.ToArray();
            return mesh;
        }

        static Vector3[] clothScratch;

        /// <summary>布を波打たせる。amp は波の大きさ、waves は布の上の波の数、speed は波の速さ、droop は垂れ下がり</summary>
        public static void WaveCloth(Mesh mesh, Vector3[] basePositions, float width, float time, float amp, float waves, float speed, float droop = 0f)
        {
            if (mesh == null || basePositions == null) return;
            if (clothScratch == null || clothScratch.Length != basePositions.Length) clothScratch = new Vector3[basePositions.Length];
            for (int i = 0; i < basePositions.Length; i++)
            {
                var p = basePositions[i];
                float k = width > 0f ? p.x / width : 0f;   // 旗ざお側は動かない
                float ph = k * waves * Mathf.PI * 2f - time * speed;
                float z = Mathf.Sin(ph + p.y * 1.3f) * amp * k + Mathf.Sin(ph * 0.5f + 1.7f) * amp * 0.35f * k * k;
                float y = p.y - droop * k * k + Mathf.Cos(ph) * amp * 0.12f * k;
                clothScratch[i] = new Vector3(p.x * (1f - 0.05f * Mathf.Abs(Mathf.Sin(ph)) * k), y, z);
            }
            mesh.vertices = clothScratch;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        // ================================================================
        // 模様（テクスチャ）
        // ================================================================

        static Texture2D waterStreaks, whirlpool, accretion, ripple, lensRing, softDot;

        static Texture2D NewTex(string name, int w, int h, bool repeatU)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapModeU = repeatU ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 2,
            };
        }

        /// <summary>まわりでつながる（つなぎ目の無い）ノイズ。u は 0〜1 でひと回り</summary>
        static float LoopNoise(float u, float v, float scale, float seed)
        {
            float a = u * Mathf.PI * 2f;
            return Mathf.PerlinNoise(Mathf.Cos(a) * scale + seed, Mathf.Sin(a) * scale + v * scale * 1.7f + seed * 0.37f);
        }

        /// <summary>水の柱のすじ（白い泡のすじと、すける水）。u がまわり、v が高さ</summary>
        public static Texture2D WaterStreaks
        {
            get
            {
                if (waterStreaks != null) return waterStreaks;
                const int W = 256, H = 128;
                var tex = NewTex("RecordWaterStreaks", W, H, true);
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = x / (float)W, v = y / (float)H;
                        float n1 = LoopNoise(u, v * 0.4f, 6f, 3.1f);
                        float n2 = LoopNoise(u, v * 2.5f, 14f, 8.7f);
                        float streak = Mathf.Pow(Mathf.Abs(Mathf.Sin((u * 9f + n1 * 1.6f) * Mathf.PI)), 6f);
                        float foam = Mathf.Clamp01(streak * 0.9f + Mathf.Pow(n2, 3f) * 1.2f);
                        float fade = Mathf.SmoothStep(0f, 1f, v / 0.12f) * Mathf.SmoothStep(0f, 1f, (1f - v) / 0.25f);
                        float a = Mathf.Clamp01((0.25f + 0.75f * foam) * fade);
                        float w = Mathf.Lerp(0.55f, 1f, foam);
                        px[y * W + x] = new Color(Mathf.Lerp(0.55f, 1f, w), Mathf.Lerp(0.85f, 1f, w), 1f, a);
                    }
                tex.SetPixels32(px);
                tex.Apply(true);
                return waterStreaks = tex;
            }
        }

        /// <summary>渦潮（らせんの白い泡）。Annulus に貼る。u がまわり、v が内側→外側</summary>
        public static Texture2D Whirlpool
        {
            get
            {
                if (whirlpool != null) return whirlpool;
                const int W = 256, H = 64;
                var tex = NewTex("RecordWhirlpool", W, H, true);
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = x / (float)W, v = y / (float)H;
                        float n = LoopNoise(u, v, 5f, 1.3f);
                        float arm = Mathf.Pow(0.5f + 0.5f * Mathf.Sin((u * 5f + v * 2.2f + n * 0.5f) * Mathf.PI * 2f), 3f);
                        float edge = Mathf.SmoothStep(0f, 1f, v / 0.15f) * Mathf.SmoothStep(0f, 1f, (1f - v) / 0.5f);
                        float a = Mathf.Clamp01((0.15f + 0.85f * arm) * edge);
                        px[y * W + x] = new Color(Mathf.Lerp(0.6f, 1f, arm), Mathf.Lerp(0.88f, 1f, arm), 1f, a);
                    }
                tex.SetPixels32(px);
                tex.Apply(true);
                return whirlpool = tex;
            }
        }

        /// <summary>ブラックホールの光の円盤（内側ほど白く熱い、外側は紫。らせんの濃淡）。Annulus に貼る</summary>
        public static Texture2D Accretion
        {
            get
            {
                if (accretion != null) return accretion;
                const int W = 256, H = 64;
                var tex = NewTex("RecordAccretion", W, H, true);
                var px = new Color32[W * H];
                Color white = new Color(1f, 0.97f, 0.9f), gold = new Color(1f, 0.72f, 0.3f), orange = new Color(1f, 0.38f, 0.12f), purple = new Color(0.62f, 0.25f, 1f);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = x / (float)W, v = y / (float)H;
                        Color col = v < 0.15f ? Color.Lerp(white, gold, v / 0.15f)
                                  : v < 0.45f ? Color.Lerp(gold, orange, (v - 0.15f) / 0.3f)
                                  : Color.Lerp(orange, purple, (v - 0.45f) / 0.55f);
                        float n = LoopNoise(u, v, 7f, 5.5f);
                        float arm = 0.55f + 0.45f * Mathf.Sin((u * 3f + v * 3.5f + n * 0.8f) * Mathf.PI * 2f);
                        float fine = 0.75f + 0.25f * Mathf.Sin((u * 23f + v * 11f + n * 3f) * Mathf.PI * 2f);
                        float inner = Mathf.SmoothStep(0f, 1f, v / 0.04f);
                        float outer = Mathf.Pow(1f - v, 1.6f);
                        float a = Mathf.Clamp01(arm * fine * inner * outer * 1.35f);
                        px[y * W + x] = new Color(col.r, col.g, col.b, a);
                    }
                tex.SetPixels32(px);
                tex.Apply(true);
                return accretion = tex;
            }
        }

        /// <summary>波紋（外側へうすくなる光の輪）。Annulus に貼る</summary>
        public static Texture2D Ripple
        {
            get
            {
                if (ripple != null) return ripple;
                const int W = 8, H = 64;
                var tex = NewTex("RecordRipple", W, H, true);
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                {
                    float v = y / (float)(H - 1);
                    float a = Mathf.Exp(-Mathf.Pow((v - 0.75f) / 0.12f, 2f)) + 0.35f * Mathf.Exp(-Mathf.Pow((v - 0.35f) / 0.1f, 2f));
                    for (int x = 0; x < W; x++) px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
                tex.SetPixels32(px);
                tex.Apply(true);
                return ripple = tex;
            }
        }

        /// <summary>光の輪（板に貼る。まん中は空いていて、ふちが細く強く光り、外へふんわり広がる）</summary>
        public static Texture2D LensRing
        {
            get
            {
                if (lensRing != null) return lensRing;
                const int S = 128;
                var tex = NewTex("RecordLensRing", S, S, false);
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float ring = Mathf.Exp(-Mathf.Pow((d - 0.42f) / 0.025f, 2f));
                        float glow = Mathf.Exp(-Mathf.Pow((d - 0.45f) / 0.16f, 2f)) * 0.45f;
                        float a = Mathf.Clamp01(ring + glow) * Mathf.Clamp01((1f - d) * 4f);
                        px[y * S + x] = new Color(1f, 1f, 1f, a);
                    }
                tex.SetPixels32(px);
                tex.Apply(true);
                return lensRing = tex;
            }
        }

        /// <summary>ふんわりした光の玉（板に貼る）</summary>
        public static Texture2D SoftDot
        {
            get
            {
                if (softDot != null) return softDot;
                const int S = 64;
                var tex = NewTex("RecordSoftDot2", S, S, false);
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        px[y * S + x] = new Color(1f, 1f, 1f, a * a);
                    }
                tex.SetPixels32(px);
                tex.Apply(true);
                return softDot = tex;
            }
        }

        // ================================================================
        // 便利な関数
        // ================================================================

        /// <summary>メッシュを持った物を作る（当たり判定なし・影なし）。parent の中の位置</summary>
        public static Transform Make(string name, Mesh mesh, Material mat, Transform parent, Vector3 localPos, Vector3 localScale, Vector3 localEuler, int layer = -1)
        {
            var go = new GameObject(name);
            if (layer >= 0) go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        /// <summary>模様を流す（UV のずれを足す）</summary>
        public static void Scroll(Material m, float du, float dv)
        {
            if (m == null) return;
            var o = m.mainTextureOffset;
            m.mainTextureOffset = new Vector2(Mathf.Repeat(o.x + du, 1f), Mathf.Repeat(o.y + dv, 1f));
        }
    }
}
