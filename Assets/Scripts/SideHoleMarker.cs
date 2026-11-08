using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MedalPusher
{
    /// <summary>
    /// 盤面の手前のふちに、「中央の穴」と「端の穴」の範囲がわかる帯と文字を出す。
    ///
    /// 使い方：何もしなくてよい。再生すると、MedalPusherGame があるシーンに自動で「SideHoleMarker」を作る。
    ///   色や大きさを Inspector で変えたい時だけ、空の GameObject にこのスクリプトを付けておく（その時は自動では作らない）。
    ///   文字のフォントは、モニターと同じフォント（Resources/MonitorFonts/Body）を自動で使う。
    ///   左右の範囲は、シーンにある穴の判定（DropZone）の大きさから自動で決める。
    ///   見つからない時は、MedalPusherGame の盤面の寸法（盤面の幅・中央の穴の幅）から決める。高さと前後は盤面の手前のふち。
    ///   ずれている時は Offset で直す（再生中に変えると、すぐに反映される）。
    ///
    /// 見た目：
    ///  ・手前のふちの前の面に、横長の帯を出す
    ///      端の穴：赤と黒のしましま（注意の柄）に「端 0pt」
    ///      中央の穴：緑の帯に「中央 10pt」（Show Center をオフにすると出さない）
    ///  ・ふちの上にも、同じ色の細い線を引く（上から見ても範囲がわかる）
    ///  ・中央と端のさかい目に、白い縦線
    ///  ・メダルが落ちると、その穴の帯が光り、「+10」や「0pt」の文字が浮かぶ
    ///  ・ポイントの数字は MedalPusherGame の Rules（Center Points / Side Points）をそのまま出す
    ///
    /// 見た目はすべてコードで作るので、モデルや画像はいらない（再生中だけ出る）。
    /// </summary>
    [DisallowMultipleComponent]
    public class SideHoleMarker : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        [Tooltip("文字のフォント（空ならモニターと同じフォント。それも無ければ Unity に入っているフォント）")]
        public Font font;

        [Header("出すもの")]
        [Tooltip("中央の穴の帯も出す")]
        public bool showCenter = true;
        [Tooltip("ふちの上の細い線を出す")]
        public bool showTopLine = true;
        [Tooltip("メダルが落ちた時に、帯を光らせて文字を浮かべる")]
        public bool flashOnDrop = true;

        [Header("大きさ・位置")]
        [Tooltip("帯の高さ（メダルの直径に対する割合）")]
        [Range(0.3f, 3f)] public float barHeight = 0.9f;
        [Tooltip("位置のずれ（ワールドの向き。X 左右・Y 上下・Z 前後）")]
        public Vector3 offset = Vector3.zero;

        [Header("色")]
        public Color sideColor = new Color32(0xe0, 0x3a, 0x3a, 0xff);
        public Color sideStripeColor = new Color32(0x18, 0x14, 0x16, 0xff);
        public Color centerColor = new Color32(0x3a, 0xc4, 0x6d, 0xff);
        public Color textColor = new Color32(0xf8, 0xf6, 0xf0, 0xff);

        class Zone
        {
            public DropZoneType type;
            public float x0, x1;                // 左右の範囲（ワールドの X）
            public GameObject bar, line;
            public Material barMat, lineMat;
            public TextMesh label;
            public Color baseColor;
            public float flash;
        }

        class Pop
        {
            public TextMesh text;
            public Vector3 start;
            public float time = -1f;
            public Color color;
        }

        readonly List<Zone> zones = new List<Zone>();
        readonly List<GameObject> dividers = new List<GameObject>();
        readonly List<Pop> pops = new List<Pop>();
        readonly List<Object> owned = new List<Object>();
        Transform root;
        Texture2D stripeTex;
        Material dividerMat;
        Mesh quad;
        bool subscribed;
        string builtKey = "";
        float measuredInner;
        float remeasureTimer;

        // ================================================================
        // 作る・片づける
        // ================================================================

        void OnDisable()
        {
            if (game != null) game.MedalDropped -= OnMedalDropped;
            subscribed = false;
            Cleanup();
        }

        void Cleanup()
        {
            if (root != null) Destroy(root.gameObject);
            foreach (var o in owned) if (o != null) Destroy(o);
            owned.Clear();
            zones.Clear();
            dividers.Clear();
            pops.Clear();
            root = null;
            builtKey = "";
        }

        Font UseFont
        {
            get
            {
                if (font != null) return font;
                var monitorFont = Resources.Load<Font>(MonitorDisplay.FontFolder + "/Body");   // モニターと同じ日本語フォント
                return monitorFont != null ? monitorFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        // ================================================================
        // 自動で置く
        // ================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreateOnStart()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AutoCreate();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AutoCreate();

        /// <summary>MedalPusherGame があって、SideHoleMarker が無いシーンなら、作って置く。</summary>
        static void AutoCreate()
        {
            if (FindFirstObjectByType<MedalPusherGame>() == null) return;
            if (FindFirstObjectByType<SideHoleMarker>() != null) return;
            new GameObject("SideHoleMarker").AddComponent<SideHoleMarker>();
        }

        /// <summary>
        /// 中央の穴の左右の範囲（inner = 中央の穴の半分の幅）と、盤面の半分の幅を調べる。
        /// シーンの穴の判定（DropZone）があれば、その大きさから。無ければ盤面の寸法から。
        /// </summary>
        void Measure(out float inner, out float half)
        {
            BoardLayout l = game.layout;
            half = l.boardWidth * 0.5f;
            inner = Mathf.Clamp(l.centerHoleWidth, 0f, l.boardWidth) * 0.5f;

            float cMin = float.MaxValue, cMax = float.MinValue;
            foreach (var z in FindObjectsByType<DropZone>(FindObjectsSortMode.None))
            {
                if (z.type != DropZoneType.Center) continue;
                var col = z.GetComponent<Collider>();
                if (col == null) continue;
                cMin = Mathf.Min(cMin, col.bounds.min.x);
                cMax = Mathf.Max(cMax, col.bounds.max.x);
            }
            if (cMax > cMin) inner = Mathf.Clamp((cMax - cMin) * 0.5f, 0f, half);
        }

        /// <summary>穴の範囲から、帯を作る。</summary>
        void Build()
        {
            Cleanup();
            BoardLayout l = game.layout;
            float d = Mathf.Max(0.01f, game.tuning.medalDiameter);
            Measure(out float inner, out float half);
            measuredInner = inner;
            float h = d * barHeight;
            float front = l.floorFrontZ;

            root = new GameObject("SideHoleMarker_Visual").transform;
            root.position = offset;

            quad = MakeQuad();
            stripeTex = MakeStripeTexture();
            var shader = Shader.Find("Sprites/Default");   // 色と画像をそのまま出す（ライトの影響なし）
            dividerMat = new Material(shader) { name = "SideHoleDivider", color = Color.white };
            owned.Add(dividerMat);

            if (inner < half - 0.001f)
            {
                AddZone(DropZoneType.Side, -half, -inner, front, h, d, shader);
                AddZone(DropZoneType.Side, inner, half, front, h, d, shader);
            }
            if (showCenter && inner > 0.001f) AddZone(DropZoneType.Center, -inner, inner, front, h, d, shader);

            // 中央と端のさかい目の白い縦線
            foreach (float x in new[] { -inner, inner })
            {
                if (inner <= 0.001f || inner >= half - 0.001f) continue;
                var go = QuadObject("Divider", dividerMat);
                go.transform.localPosition = new Vector3(x, -h * 0.5f, front - 0.004f);
                go.transform.localScale = new Vector3(d * 0.06f, h * 1.25f, 1f);
                dividers.Add(go);
            }

            // 落ちた時に浮かぶ文字
            for (int i = 0; i < 6; i++)
            {
                var tm = NewText("Pop", d * 0.06f);
                tm.gameObject.SetActive(false);
                pops.Add(new Pop { text = tm });
            }
            builtKey = Key();
        }

        void AddZone(DropZoneType type, float x0, float x1, float front, float h, float d, Shader shader)
        {
            var z = new Zone { type = type, x0 = x0, x1 = x1 };
            bool side = type == DropZoneType.Side;
            z.baseColor = side ? sideColor : centerColor;
            float w = x1 - x0;
            float cx = (x0 + x1) * 0.5f;

            // 前の面の帯（ふちの上の面にそろえて、下へのばす）
            z.barMat = new Material(shader) { name = "SideHoleBar", color = Color.white };
            if (side)
            {
                z.barMat.mainTexture = stripeTex;
                z.barMat.mainTextureScale = new Vector2(Mathf.Max(1f, w / h), 1f);   // しましまの太さを、帯の長さに合わせる
            }
            owned.Add(z.barMat);
            z.bar = QuadObject(side ? "SideBar" : "CenterBar", z.barMat);
            z.bar.transform.localPosition = new Vector3(cx, -h * 0.5f, front - 0.002f);
            z.bar.transform.localScale = new Vector3(w, h, 1f);

            // ふちの上の細い線（盤面に寝かせる）
            if (showTopLine)
            {
                z.lineMat = new Material(shader) { name = "SideHoleLine", color = z.baseColor };
                owned.Add(z.lineMat);
                z.line = QuadObject("TopLine", z.lineMat);
                float depth = d * 0.12f;
                z.line.transform.localPosition = new Vector3(cx, 0.003f, front + depth * 0.5f);
                z.line.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                z.line.transform.localScale = new Vector3(w, depth, 1f);
            }

            // 文字（帯のまん中。端は黒い札の上に白い文字）
            z.label = NewText("Label", d * 0.065f * barHeight);
            z.label.transform.localPosition = new Vector3(cx, -h * 0.5f, front - 0.006f);
            if (side)
            {
                var plate = QuadObject("LabelPlate", dividerMat);
                plate.GetComponent<MeshRenderer>().material.color = new Color(0.08f, 0.07f, 0.08f, 0.92f);
                owned.Add(plate.GetComponent<MeshRenderer>().material);
                plate.transform.localPosition = new Vector3(cx, -h * 0.5f, front - 0.004f);
                plate.transform.localScale = new Vector3(Mathf.Min(w * 0.9f, h * 3.2f), h * 0.78f, 1f);
            }
            zones.Add(z);
        }

        GameObject QuadObject(string name, Material mat)
        {
            var go = new GameObject("SideHoleMarker_" + name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        TextMesh NewText(string name, float charSize)
        {
            var go = new GameObject("SideHoleMarker_" + name);
            go.transform.SetParent(root, false);
            var tm = go.AddComponent<TextMesh>();
            var f = UseFont;
            tm.font = f;
            if (f != null) go.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = charSize;
            tm.color = textColor;
            return tm;
        }

        /// <summary>前（-Z）を向いた 1×1 の四角（まん中が原点）。</summary>
        Mesh MakeQuad()
        {
            var m = new Mesh { name = "SideHoleQuad" };
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            m.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            owned.Add(m);
            return m;
        }

        /// <summary>赤と黒のななめのしましま（注意の柄）。</summary>
        Texture2D MakeStripeTexture()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                name = "SideHoleStripes"
            };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, ((x + y) / (size / 2)) % 2 == 0 ? sideColor : sideStripeColor);
            tex.Apply();
            owned.Add(tex);
            return tex;
        }

        /// <summary>盤面の寸法や設定が変わったら作り直すための目印。</summary>
        string Key()
        {
            BoardLayout l = game.layout;
            return $"{l.boardWidth:0.###}/{l.centerHoleWidth:0.###}/{l.floorFrontZ:0.###}/{game.tuning.medalDiameter:0.###}/{barHeight:0.##}/{showCenter}/{showTopLine}/{sideColor}/{centerColor}/{sideStripeColor}/{font}";
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null) return;
            if (!subscribed)
            {
                game.MedalDropped += OnMedalDropped;
                subscribed = true;
            }
            // 穴の判定はゲームが作るので、はじめの数秒は大きさが変わっていないか確かめ直す
            remeasureTimer += Time.unscaledDeltaTime;
            bool remeasure = false;
            if (root != null && remeasureTimer > 1f && Time.realtimeSinceStartup < 10f)
            {
                remeasureTimer = 0f;
                Measure(out float inner, out _);
                remeasure = Mathf.Abs(inner - measuredInner) > 0.001f;
            }
            if (root == null || remeasure || builtKey != Key()) Build();
            root.position = offset;

            float dt = Time.unscaledDeltaTime;
            foreach (var z in zones)
            {
                bool side = z.type == DropZoneType.Side;
                int pts = side ? game.rules.sidePoints : game.rules.centerPoints;
                z.label.text = side ? $"端 {pts}pt" : $"中央 {pts}pt";
                z.flash = Mathf.MoveTowards(z.flash, 0f, dt * 3f);
                float f = z.flash * z.flash;
                z.barMat.color = side ? Color.Lerp(Color.white, new Color(1.6f, 1.6f, 1.6f), f)
                                      : Color.Lerp(centerColor * 0.85f, Color.Lerp(centerColor, Color.white, 0.6f), f);
                if (z.lineMat != null) z.lineMat.color = Color.Lerp(z.baseColor, Color.white, f * 0.7f);
                z.label.color = Color.Lerp(textColor, z.baseColor, f * 0.5f);
                float s = 1f + 0.25f * f;
                z.label.transform.localScale = new Vector3(s, s, 1f);
            }

            // 浮かぶ文字
            float d = game.tuning.medalDiameter;
            foreach (var p in pops)
            {
                if (p.time < 0f) continue;
                p.time += dt;
                float t = p.time / 0.8f;
                if (t >= 1f)
                {
                    p.time = -1f;
                    p.text.gameObject.SetActive(false);
                    continue;
                }
                p.text.transform.localPosition = p.start + new Vector3(0f, d * (0.4f + 1.2f * t), 0f);
                float pop = 1f + 0.5f * Mathf.Max(0f, 1f - t * 5f);
                p.text.transform.localScale = new Vector3(pop, pop, 1f);
                Color c = p.color;
                c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                p.text.color = c;
            }
        }

        /// <summary>メダルが落ちた：その穴の帯を光らせ、ポイントの文字を浮かべる</summary>
        void OnMedalDropped(Medal medal, DropZoneType type)
        {
            if (!flashOnDrop || type == DropZoneType.Lost || root == null) return;
            float x = medal != null ? medal.transform.position.x - offset.x : 0f;
            Zone hit = null;
            foreach (var z in zones)
            {
                if (z.type != type) continue;
                if (hit == null || (x >= z.x0 && x <= z.x1)) hit = z;   // 左右どちらの端か、メダルの位置で決める
            }
            if (hit == null) return;
            hit.flash = 1f;

            bool side = type == DropZoneType.Side;
            int pts = side ? game.rules.sidePoints : game.rules.centerPoints;
            Pop p = pops[0];
            foreach (var q in pops)
            {
                if (q.time < 0f) { p = q; break; }
                if (q.time > p.time) p = q;
            }
            float h = game.tuning.medalDiameter * barHeight;
            float cx = Mathf.Clamp(x, hit.x0 + h, hit.x1 - h);
            p.start = new Vector3(cx, 0f, game.layout.floorFrontZ - 0.01f);
            p.time = 0f;
            p.color = side && pts <= 0 ? sideColor : Color.Lerp(hit.baseColor, Color.white, 0.4f);
            p.text.text = pts > 0 ? $"+{pts}" : "0pt";
            p.text.gameObject.SetActive(true);
        }
    }
}
