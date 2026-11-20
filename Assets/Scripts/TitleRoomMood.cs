using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// タイトル画面のうしろに映る部屋を「夜の閉店後のネオン・ゲームセンター」にする。TitleScene に1つ置く。
    /// （メニュー「RECORD DROP/タイトルの部屋を夜にする」で置ける）
    ///
    /// ゲームを始めた時に、次のことをする（シーンのファイルやモデルは変えない。止めると元にもどる）：
    /// ・部屋全体を暗くして、左の壁をマゼンタ、右の壁をシアンの光で照らす。今あるライトは消す
    /// ・天井からあたたかいペンダントライトを吊るし、光の柱と、ほこりがきらきら舞う光を足す
    /// ・ネオン管・ゲーム機の画面・床のタイル・天井のふちを、自分で光る色にする（床はゆっくり明滅）
    /// ・左右の壁に、雨の降る夜のネオン街が見える窓を付ける
    /// ・奥の壁にロゴの看板、手前の壁に「OPEN 24H」の看板を付ける
    /// ・まん中のメダル機の裏に、ネオンのふちとロゴのパネルを付ける
    /// ・紫の霧をかける
    /// ブルーム・ビネットなどの仕上げは、カメラの Post Processing（メニューで付ける）が受け持つ。
    /// 看板と窓の絵は Resources/TitleRoom/ に置く（CityWindow・SignLogo・Open24H）。
    /// </summary>
    [DisallowMultipleComponent]
    public class TitleRoomMood : MonoBehaviour
    {
        [Header("部屋の明るさ")]
        [Tooltip("部屋全体のうす明かりの色（暗いほど、ネオンが映える）")]
        public Color ambient = new Color(0.035f, 0.03f, 0.07f);
        [Tooltip("今シーンにあるライトを消す")]
        public bool turnOffOtherLights = true;
        [Tooltip("紫の霧をかける")]
        public bool useFog = true;
        public Color fogColor = new Color(0.03f, 0.02f, 0.07f);
        [Range(0f, 0.3f)] public float fogDensity = 0.07f;

        [Header("色の光")]
        public Color leftWallColor = new Color(1f, 0.22f, 0.7f);
        public Color rightWallColor = new Color(0.15f, 0.8f, 1f);
        [Range(0f, 8f)] public float wallLightIntensity = 2.6f;
        public Color pendantColor = new Color(1f, 0.72f, 0.45f);
        [Range(0f, 8f)] public float pendantIntensity = 2.4f;

        [Header("光る物")]
        [Tooltip("ネオン管の明るさ")]
        [Range(0f, 6f)] public float neonGlow = 2.2f;
        [Tooltip("ゲーム機の画面の明るさ")]
        [Range(0f, 6f)] public float screenGlow = 1.6f;
        [Tooltip("床のタイルの明るさ（明滅の一番明るい時）")]
        [Range(0f, 3f)] public float floorGlow = 0.55f;
        [Tooltip("壁の暗さ（1 で今のまま）")]
        [Range(0f, 1f)] public float wallDarkness = 0.45f;

        [Header("足す物")]
        public bool addPendants = true;
        public bool addWindows = true;
        public bool addSigns = true;
        public bool decorateCabinetBack = true;
        [Tooltip("窓を付ける左右の壁の位置（部屋のまん中からの距離）")]
        public float sideWallX = 4.8f;
        [Tooltip("看板を付ける奥・手前の壁の位置（部屋のまん中からの距離）")]
        public float backWallZ = 4.8f;

        static readonly Color[] ScreenColors =
        {
            new Color(0.25f, 0.9f, 1f), new Color(1f, 0.3f, 0.8f), new Color(0.5f, 1f, 0.4f), new Color(1f, 0.75f, 0.2f),
        };

        Transform root;
        readonly List<Light> disabledLights = new List<Light>();
        readonly List<Material> madeMaterials = new List<Material>();
        readonly List<(Material m, Color c, float speed, float phase)> pulses = new List<(Material, Color, float, float)>();
        readonly List<(Material m, Color c)> flickers = new List<(Material, Color)>();
        readonly List<Material> rainMats = new List<Material>();
        readonly List<Light> breathing = new List<Light>();
        readonly List<float> breathingBase = new List<float>();

        // 元にもどす用
        UnityEngine.Rendering.AmbientMode oldAmbientMode;
        Color oldAmbient;
        bool oldFog;
        Color oldFogColor;
        FogMode oldFogMode;
        float oldFogDensity;

        void Start()
        {
            root = new GameObject("TitleRoomMood_Built").transform;
            root.SetParent(transform, false);

            oldAmbientMode = RenderSettings.ambientMode;
            oldAmbient = RenderSettings.ambientLight;
            oldFog = RenderSettings.fog;
            oldFogColor = RenderSettings.fogColor;
            oldFogMode = RenderSettings.fogMode;
            oldFogDensity = RenderSettings.fogDensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            if (useFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
            }

            if (turnOffOtherLights)
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l == null || !l.enabled || l.transform.IsChildOf(transform)) continue;
                    l.enabled = false;
                    disabledLights.Add(l);
                }
            }

            RestyleMaterials();
            BuildLights();
            if (addPendants) BuildPendants();
            if (addWindows) BuildWindows();
            if (addSigns) BuildSigns();
            if (decorateCabinetBack) BuildCabinetBack();
        }

        void OnDestroy()
        {
            RenderSettings.ambientMode = oldAmbientMode;
            RenderSettings.ambientLight = oldAmbient;
            RenderSettings.fog = oldFog;
            RenderSettings.fogColor = oldFogColor;
            RenderSettings.fogMode = oldFogMode;
            RenderSettings.fogDensity = oldFogDensity;
            foreach (var l in disabledLights) if (l != null) l.enabled = true;
            foreach (var m in madeMaterials) if (m != null) Destroy(m);
        }

        // ================================================================
        // 今ある物の色を変える（素材の名前で見分ける）
        // ================================================================

        static string BaseName(Material m)
        {
            if (m == null) return "";
            string n = m.name;
            int i = n.IndexOf(' ');
            return i > 0 ? n.Substring(0, i) : n;
        }

        Material Emissive(Material src, Color baseColor, Color emission)
        {
            var m = new Material(src);
            madeMaterials.Add(m);
            m.color = baseColor;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        Material Tinted(Material src, float k)
        {
            var m = new Material(src);
            madeMaterials.Add(m);
            m.color = src.color * k;
            return m;
        }

        void RestyleMaterials()
        {
            var cache = new Dictionary<(Material, int), Material>();
            int screenIndex = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r == null || r.transform.IsChildOf(transform)) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                bool ceiling = r.bounds.center.y > 1.5f;
                int myScreen = screenIndex++ % ScreenColors.Length;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    string n = BaseName(src);
                    int variant = n == "Screen" ? myScreen : ceiling ? 100 : 0;
                    if (cache.TryGetValue((src, variant), out var done)) { mats[i] = done; changed = true; continue; }
                    Material made = null;
                    switch (n)
                    {
                        case "Screen":
                            made = Emissive(src, Color.black, ScreenColors[myScreen] * screenGlow);
                            flickers.Add((made, ScreenColors[myScreen] * screenGlow));
                            break;
                        case "NeonPink": made = Emissive(src, Color.black, new Color(1f, 0.15f, 0.62f) * neonGlow); break;
                        case "NeonCyan": made = Emissive(src, Color.black, new Color(0.12f, 0.8f, 1f) * neonGlow); break;
                        case "NeonYellow": made = Emissive(src, Color.black, new Color(1f, 0.78f, 0.2f) * neonGlow); break;
                        case "CornerLight": made = Emissive(src, src.color * 0.3f, new Color(0.8f, 0.3f, 1f) * neonGlow * 0.6f); break;
                        case "Wall":
                        case "WallPanel": made = Tinted(src, wallDarkness); break;
                        case "Trim": made = Tinted(src, 0.8f); break;
                        case "Glass": made = Emissive(src, src.color * 0.4f, new Color(0.04f, 0.07f, 0.12f)); break;
                        case "TilePink":
                        case "TileBlue":
                        case "TileLavender":
                        case "TileWhite":
                        case "TileOff":
                            if (ceiling) made = Tinted(src, 0.15f);
                            else
                            {
                                Color glow = n == "TilePink" ? new Color(1f, 0.15f, 0.7f) : n == "TileBlue" ? new Color(0.1f, 0.5f, 1f)
                                           : n == "TileLavender" ? new Color(0.55f, 0.3f, 1f) : Color.black;
                                made = Emissive(src, src.color * 0.16f, Color.black);
                                if (glow != Color.black) pulses.Add((made, glow * floorGlow, n == "TilePink" ? 0.5f : n == "TileBlue" ? 0.37f : 0.29f, n.Length));
                            }
                            break;
                    }
                    if (made == null) continue;
                    cache[(src, variant)] = made;
                    mats[i] = made;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // ================================================================
        // 光
        // ================================================================

        Light AddLight(string name, Vector3 pos, Color c, float range, float intensity, bool breathe)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            if (breathe)
            {
                breathing.Add(l);
                breathingBase.Add(intensity);
            }
            return l;
        }

        void BuildLights()
        {
            AddLight("WallLeft", new Vector3(-3.9f, 2f, 0f), leftWallColor, 7f, wallLightIntensity, true);
            AddLight("WallRight", new Vector3(3.9f, 2f, 0f), rightWallColor, 7f, wallLightIntensity, true);
            AddLight("Back", new Vector3(0f, 2.2f, 4.2f), new Color(0.55f, 0.3f, 1f), 5f, 1.6f, false);
            AddLight("Front", new Vector3(0f, 2.2f, -4.2f), new Color(1f, 0.45f, 0.2f), 5f, 1.2f, false);
        }

        static Texture2D shaftTexture;

        static Texture2D ShaftTexture
        {
            get
            {
                if (shaftTexture != null) return shaftTexture;
                const int W = 8, H = 64;
                shaftTexture = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "TitleShaft", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                {
                    float v = y / (float)(H - 1);
                    float a = Mathf.Pow(1f - v, 1.6f) * Mathf.Clamp01(v / 0.05f);
                    for (int x = 0; x < W; x++) px[y * W + x] = new Color(1f, 1f, 1f, a);
                }
                shaftTexture.SetPixels32(px);
                shaftTexture.Apply();
                return shaftTexture;
            }
        }

        GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        Material Plain(Color c, float smooth = 0.4f)
        {
            var m = ProtoAssets.MakeMaterial(c, 0.2f, smooth);
            madeMaterials.Add(m);
            return m;
        }

        Material Glowing(Color c, float glow)
        {
            var m = ProtoAssets.MakeMaterial(Color.black, 0f, 0.5f);
            madeMaterials.Add(m);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            return m;
        }

        void BuildPendants()
        {
            var cord = Plain(new Color(0.05f, 0.05f, 0.06f));
            var shade = Plain(new Color(0.12f, 0.1f, 0.12f), 0.7f);
            var bulb = Glowing(pendantColor, 3f);
            Vector3[] at = { new Vector3(0f, 2.5f, 0f), new Vector3(-3.2f, 2.5f, 2.6f), new Vector3(3.2f, 2.5f, 2.6f) };
            for (int i = 0; i < at.Length; i++)
            {
                var p = at[i];
                bool main = i == 0;
                float s = main ? 1f : 0.75f;
                var g = new GameObject("Pendant" + i).transform;
                g.SetParent(root, false);
                Box("Cord", g, p + Vector3.up * 0.2f, new Vector3(0.02f, 0.4f, 0.02f), cord);
                Box("Shade", g, p, new Vector3(0.42f, 0.12f, 0.42f) * s, shade);
                Box("Bulb", g, p + Vector3.down * 0.07f * s, new Vector3(0.22f, 0.03f, 0.22f) * s, bulb);
                AddLight("PendantLight" + i, p + Vector3.down * 0.15f, pendantColor, main ? 4.2f : 2.6f, main ? pendantIntensity : pendantIntensity * 0.55f, false);
                // 光の柱
                var mat = RecordMeshKit.Additive(ShaftTexture, new Color(pendantColor.r * 0.28f, pendantColor.g * 0.2f, pendantColor.b * 0.12f, 1f));
                madeMaterials.Add(mat);
                float h = p.y - 0.1f;
                var shaft = RecordMeshKit.Make("Shaft", RecordMeshKit.Funnel(0.1f * s, 0.95f * s, h, 0f, 32, 6, 1f), mat, g, Vector3.zero, Vector3.one, Vector3.zero);
                shaft.position = p + Vector3.down * 0.08f;
                shaft.rotation = Quaternion.Euler(180f, 0f, 0f);
                // ほこり
                BuildDust(g, new Vector3(p.x, h * 0.5f, p.z), new Vector3(1.2f * s, h * 0.9f, 1.2f * s), main ? 14f : 8f);
            }
        }

        void BuildDust(Transform parent, Vector3 center, Vector3 size, float rate)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
            main.startColor = new Color(1f, 0.85f, 0.6f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = size;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 0.4f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = RecordMeshKit.Additive(RecordMeshKit.SoftDot, Color.white);
            madeMaterials.Add(mat);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        // ================================================================
        // 窓・看板
        // ================================================================

        /// <summary>絵を貼った板（自分で光る）。normal は板の表の向き</summary>
        Transform Panel(string name, Vector3 center, Vector3 normal, float w, float h, Texture tex, float glow)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.position = center;
            // Quad は -Z の向きに表を向けている
            go.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
            go.transform.localScale = new Vector3(w, h, 1f);
            var m = ProtoAssets.MakeMaterial(Color.black, 0f, 0.85f);
            madeMaterials.Add(m);
            if (tex != null)
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", tex);
                m.SetColor("_EmissionColor", Color.white * glow);
            }
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        static Texture2D rainTexture;

        static Texture2D RainTexture
        {
            get
            {
                if (rainTexture != null) return rainTexture;
                const int W = 128, H = 128;
                rainTexture = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "TitleRain", wrapMode = TextureWrapMode.Repeat };
                var px = new Color32[W * H];
                var rnd = new System.Random(11);
                for (int i = 0; i < 70; i++)
                {
                    int x = rnd.Next(W), y0 = rnd.Next(H), len = 6 + rnd.Next(18);
                    for (int k = 0; k < len; k++)
                    {
                        int y = (y0 + k) % H;
                        byte a = (byte)(60 + 120 * k / len);
                        px[y * W + x] = new Color32(200, 220, 255, a);
                    }
                }
                rainTexture.SetPixels32(px);
                rainTexture.Apply();
                return rainTexture;
            }
        }

        void BuildWindows()
        {
            var city = Resources.Load<Texture2D>("TitleRoom/CityWindow");
            var frame = Plain(new Color(0.08f, 0.07f, 0.1f), 0.6f);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * sideWallX;
                var normal = new Vector3(-side, 0f, 0f);
                Vector3 c = new Vector3(x, 2.05f, 0f);
                Panel("Window", c, normal, 3.2f, 1.1f, city, 1.15f);
                // 雨（流れる）
                var rain = GameObject.CreatePrimitive(PrimitiveType.Quad);
                rain.name = "Rain";
                Destroy(rain.GetComponent<Collider>());
                rain.transform.SetParent(root, false);
                rain.transform.position = c + normal * 0.01f;
                rain.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
                rain.transform.localScale = new Vector3(3.2f, 1.1f, 1f);
                var rm = RecordMeshKit.Additive(RainTexture, new Color(0.6f, 0.7f, 1f, 0.6f));
                rm.mainTextureScale = new Vector2(4f, 1.4f);
                madeMaterials.Add(rm);
                rainMats.Add(rm);
                rain.GetComponent<Renderer>().sharedMaterial = rm;
                // 窓わく
                Box("FrameTop", root, c + Vector3.up * 0.58f + normal * 0.02f, new Vector3(0.06f, 0.06f, 3.3f), frame);
                Box("FrameBottom", root, c + Vector3.down * 0.58f + normal * 0.04f, new Vector3(0.12f, 0.06f, 3.3f), frame);
                Box("FrameMid", root, c + normal * 0.02f, new Vector3(0.04f, 1.1f, 0.04f), frame);
                for (int k = -1; k <= 1; k += 2) Box("FrameSide", root, c + new Vector3(0f, 0f, k * 1.62f) + normal * 0.02f, new Vector3(0.06f, 1.2f, 0.06f), frame);
                // 窓から入る青い光
                AddLight("WindowLight", c + normal * 0.6f, new Color(0.35f, 0.45f, 1f), 3f, 0.8f, false);
            }
        }

        void BuildSigns()
        {
            var logo = Resources.Load<Texture2D>("TitleRoom/SignLogo");
            var open = Resources.Load<Texture2D>("TitleRoom/Open24H");
            Panel("SignLogo", new Vector3(0f, 2.42f, backWallZ), Vector3.back, 1.9f, 0.65f, logo, 1.6f);
            var o = Panel("SignOpen", new Vector3(0f, 2.45f, -backWallZ), Vector3.forward, 1.4f, 0.5f, open, 1.8f);
            var m = o.GetComponent<Renderer>().sharedMaterial;
            flickers.Add((m, Color.white * 1.8f));
            AddLight("SignLight", new Vector3(0f, 2.3f, backWallZ - 0.6f), new Color(1f, 0.4f, 0.8f), 2.5f, 0.9f, false);
        }

        void BuildCabinetBack()
        {
            var cyan = Glowing(new Color(0.12f, 0.8f, 1f), neonGlow);
            var pink = Glowing(new Color(1f, 0.15f, 0.62f), neonGlow);
            for (int k = -1; k <= 1; k += 2) Box("BackNeon", root, new Vector3(k * 0.5f, 1.04f, 0.44f), new Vector3(0.035f, 1.86f, 0.035f), cyan);
            Box("BackNeonTop", root, new Vector3(0f, 1.97f, 0.44f), new Vector3(1.03f, 0.035f, 0.035f), pink);
            Box("BackNeonBottom", root, new Vector3(0f, 0.12f, 0.44f), new Vector3(1.03f, 0.035f, 0.035f), pink);
            var logo = Resources.Load<Texture2D>("TitleRoom/SignLogo");
            Panel("BackLogo", new Vector3(0f, 1.6f, 0.445f), Vector3.forward, 0.84f, 0.29f, logo, 1.4f);
        }

        // ================================================================
        // 動き
        // ================================================================

        void Update()
        {
            float t = Time.time;
            foreach (var p in pulses)
            {
                if (p.m == null) continue;
                float k = 0.25f + 0.75f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * p.speed * Mathf.PI * 2f + p.phase), 2f);
                p.m.SetColor("_EmissionColor", p.c * k);
            }
            for (int i = 0; i < flickers.Count; i++)
            {
                var f = flickers[i];
                if (f.m == null) continue;
                // ときどき、チカッとまたたく
                float n = Mathf.PerlinNoise(t * 7f, i * 3.1f);
                float k = n < 0.18f ? 0.35f : 0.92f + 0.08f * Mathf.Sin(t * 13f + i);
                f.m.SetColor("_EmissionColor", f.c * k);
            }
            foreach (var m in rainMats)
            {
                if (m == null) continue;
                var o = m.mainTextureOffset;
                m.mainTextureOffset = new Vector2(o.x, Mathf.Repeat(o.y + Time.deltaTime * 0.9f, 1f));
            }
            for (int i = 0; i < breathing.Count; i++)
            {
                if (breathing[i] == null) continue;
                breathing[i].intensity = breathingBase[i] * (0.85f + 0.15f * Mathf.Sin(t * 0.6f + i * 2f));
            }
        }
    }
}
