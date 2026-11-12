using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedalPusher
{
    /// <summary>
    /// レコードの効果の発動中（曲が流れている間）に、盤面ぜんたいを飾る演出。全部のレコードに共通でかかる。
    /// RecordEffectSystem の GameObject に自動で付く（シーンに置かなくてよい）。ゲームのカメラに映るので、ドットのフィルターもかかる。
    ///
    /// ・ムービングライト：テーマの色の光が3つ、盤面の上を回る（メダルが色づいて光る）。曲の盛り上がりで明るさが変わる
    /// ・床の光：盤面のメダルの上に、テーマの色のぼんやりした光と、ビートで広がる光の輪（メダルに隠れない高さ）
    /// ・ランウェイの光：盤面の左右のふちに並んだ光が、手前へ向かって流れる
    /// ・ただよう光の粒：盤面の上に、ゆらゆら昇る光の粒
    /// ・落ちた時：中央の穴に落ちると光の柱と光の輪と火花（端の穴は小さめ）
    ///
    /// 効果のルールには一切さわらない（見た目だけ）。Inspector で、それぞれのオン・オフと強さを変えられる。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecordBoardAmbience : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        public RecordEffectSystem effects;
        public RecordAudioPlayer audioPlayer;

        [Header("演出")]
        [Tooltip("オフにすると、盤面の共通の演出を出さない")]
        public bool ambienceEnabled = true;
        [Tooltip("ムービングライト（盤面の上を回る色の光）")]
        public bool movingLights = true;
        [Range(0f, 8f)] public float lightIntensity = 3f;
        [Tooltip("床の光と、ビートで広がる光の輪")]
        public bool floorGlow = true;
        [Tooltip("左右のふちの、流れる光")]
        public bool runwayLights = true;
        [Tooltip("ただよう光の粒")]
        public bool floatingSparkles = true;
        [Tooltip("メダルが穴に落ちた時の、光の柱と火花")]
        public bool dropBursts = true;
        [Tooltip("ビートの速さ（曲の BPM。ビートで光の輪が広がる）")]
        [Range(60f, 200f)] public float bpm = 120f;

        Transform root;
        readonly List<Light> lights = new List<Light>();
        Material floorMat, ringMat;
        Transform floorQuad;
        readonly List<Transform> rings = new List<Transform>();
        readonly List<Material> ringMats = new List<Material>();
        readonly List<float> ringT = new List<float>();
        readonly List<Material> runway = new List<Material>();
        readonly List<Transform> runwayT = new List<Transform>();
        readonly List<Material> edgeMats = new List<Material>();
        ParticleSystem floaters, sparks;
        readonly List<Burst> bursts = new List<Burst>();
        float level, beatClock, time;
        Color theme = new Color(1f, 0.6f, 0.2f);
        bool hooked;

        class Burst { public Transform column; public Material mat; public Light light; public float t, len, height; }

        static Texture2D glowTex, ringTex;
        static Material additiveTemplate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<RecordBoardAmbience>() != null) return;
            var system = FindFirstObjectByType<RecordEffectSystem>();
            if (system != null) system.gameObject.AddComponent<RecordBoardAmbience>();
        }

        // ================================================================
        // 初期化
        // ================================================================

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (effects == null) effects = GetComponent<RecordEffectSystem>();
            if (effects == null) effects = FindFirstObjectByType<RecordEffectSystem>();
            if (audioPlayer == null) audioPlayer = FindFirstObjectByType<RecordAudioPlayer>();
            if (game == null || effects == null) { enabled = false; return; }
            Build();
            effects.RecordPlayed += OnPlayed;
            game.MedalDropped += OnDropped;
            hooked = true;
        }

        void OnDestroy()
        {
            if (hooked)
            {
                if (effects != null) effects.RecordPlayed -= OnPlayed;
                if (game != null) game.MedalDropped -= OnDropped;
            }
            if (root != null) Destroy(root.gameObject);
        }

        float D => Mathf.Max(0.05f, game.tuning.medalDiameter);
        float HalfW => game.BoardHalfWidth;
        float FrontZ => game.layout.floorFrontZ;
        float PusherFrontZ => game.layout.pusherRetractZ - game.tuning.pusherStroke;
        float MidZ => (FrontZ + PusherFrontZ) * 0.5f;

        void Build()
        {
            root = new GameObject("RecordBoardAmbience").transform;
            MakeTextures();

            // ムービングライト
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("MovingLight" + i);
                go.transform.SetParent(root, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.intensity = 0f;
                l.range = Mathf.Max(2f, game.layout.boardWidth * 0.55f);
                l.renderMode = LightRenderMode.ForcePixel;
                lights.Add(l);
            }

            // 床の光
            float depth = Mathf.Abs(PusherFrontZ - FrontZ) + D * 2f;
            floorMat = Additive(glowTex);
            floorQuad = Flat("FloorGlow", new Vector3(0f, D * 0.9f, MidZ), new Vector2(HalfW * 2.4f, depth * 1.4f), floorMat);
            for (int i = 0; i < 3; i++)
            {
                var m = Additive(ringTex);
                ringMats.Add(m);
                rings.Add(Flat("BeatRing", new Vector3(0f, D * 0.95f, MidZ), Vector2.one, m));
                ringT.Add(-1f);
            }

            // ランウェイの光（左右のふち）
            const int perSide = 14;
            for (int s = 0; s < 2; s++)
                for (int i = 0; i < perSide; i++)
                {
                    float z = Mathf.Lerp(PusherFrontZ + D, FrontZ + D * 0.3f, i / (float)(perSide - 1));
                    var m = Additive(glowTex);
                    runway.Add(m);
                    runwayT.Add(Flat("Runway", new Vector3((s == 0 ? -1f : 1f) * (HalfW - D * 0.2f), D * 1.0f, z), new Vector2(D * 1.3f, D * 1.3f), m));
                }

            // 左右のふちの、光るネオンの線
            for (int s = 0; s < 2; s++)
            {
                var m = Additive(glowTex);
                edgeMats.Add(m);
                Flat("NeonEdge", new Vector3((s == 0 ? -1f : 1f) * (HalfW - D * 0.2f), D * 1.0f, MidZ), new Vector2(D * 0.35f, depth), m);
            }

            // ただよう光の粒
            floaters = MakeParticles("Floaters", D * 0.18f, 2.6f, -0.02f, 400);
            var fs = floaters.shape;
            fs.enabled = true;
            fs.shapeType = ParticleSystemShapeType.Box;
            // 向きを (-90,0,0) にすると、箱の z が上向き（粒が昇る）、y が奥行きになる
            floaters.transform.SetPositionAndRotation(new Vector3(0f, D * 0.3f, MidZ), Quaternion.Euler(-90f, 0f, 0f));
            fs.scale = new Vector3(HalfW * 2f, depth, D * 0.2f);
            var fm = floaters.main;
            fm.startSpeed = new ParticleSystem.MinMaxCurve(D * 0.3f, D * 1.2f);

            sparks = MakeParticles("DropSparks", D * 0.16f, 0.9f, 0.6f, 800);
            var tr = sparks.trails;
            tr.enabled = true;
            tr.ratio = 1f;
            tr.lifetime = 0.18f;
            tr.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            tr.inheritParticleColor = true;
            tr.dieWithParticles = true;
            sparks.GetComponent<ParticleSystemRenderer>().trailMaterial = Additive(glowTex);

            root.gameObject.SetActive(false);
        }

        Transform Flat(string name, Vector3 pos, Vector2 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        ParticleSystem MakeParticles(string name, float size, float life, float gravity, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startSpeed = 0f;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.1f, 1.1f), new Keyframe(1f, 0.2f)));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = D * 0.6f;
            noise.frequency = 0.7f;
            noise.scrollSpeed = 0.5f;
            noise.damping = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = Additive(glowTex);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static Material Additive(Texture tex)
        {
            if (additiveTemplate == null)
            {
                var sh = Shader.Find("Legacy Shaders/Particles/Additive");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                additiveTemplate = new Material(sh);
            }
            var m = new Material(additiveTemplate) { mainTexture = tex };
            SetColor(m, Color.clear);
            return m;
        }

        static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a * 0.5f));
            m.color = c;
        }

        static void MakeTextures()
        {
            if (glowTex != null) return;
            const int n = 64;
            glowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "AmbienceGlow" };
            ringTex = new Texture2D(n * 2, n * 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "AmbienceRing" };
            var a = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = Mathf.Clamp01(1f - r * 3f);
                    float halo = Mathf.Clamp01(1f - r);
                    float v = Mathf.Clamp01(core * core * 0.8f + halo * halo * 0.6f);
                    a[y * n + x] = new Color(1f, 1f, 1f, v);
                }
            glowTex.SetPixels(a);
            glowTex.Apply();
            int m2 = n * 2;
            var b = new Color[m2 * m2];
            for (int y = 0; y < m2; y++)
                for (int x = 0; x < m2; x++)
                {
                    float dx = (x + 0.5f) / m2 * 2f - 1f, dy = (y + 0.5f) / m2 * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float v = Mathf.Clamp01(1f - Mathf.Abs(r - 0.86f) / 0.1f);
                    float inner = Mathf.Clamp01(1f - Mathf.Abs(r - 0.86f) / 0.03f);
                    b[y * m2 + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(v * v * 0.7f + inner));
                }
            ringTex.SetPixels(b);
            ringTex.Apply();
        }

        // ================================================================
        // イベント
        // ================================================================

        void OnPlayed(RecordData record)
        {
            if (record == null) return;
            if (!RecordSpectacle.TryGetTheme(record.key, out theme)) theme = RecordArtwork.CategoryColor(record.category);
            beatClock = 0f;
            // 発動の瞬間：床に大きな光の輪を3つ
            for (int i = 0; i < rings.Count; i++) ringT[i] = -0.15f * i;
        }

        void OnDropped(Medal medal, DropZoneType type)
        {
            if (!ambienceEnabled || !dropBursts || medal == null || type == DropZoneType.Lost || level < 0.2f) return;
            bool center = type == DropZoneType.Center;
            Vector3 at = new Vector3(Mathf.Clamp(medal.transform.position.x, -HalfW, HalfW), 0f, FrontZ);
            Color c = center ? Color.Lerp(theme, Color.white, 0.25f) : Color.Lerp(theme, new Color(0.4f, 0.7f, 1f), 0.5f);
            SpawnColumn(at, c, center ? D * 9f : D * 4f, center ? 0.55f : 0.35f, center ? D * 1.1f : D * 0.6f);
            int n = center ? 26 : 10;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                Vector2 h = Random.insideUnitCircle * D * 0.6f;
                ep.position = at + new Vector3(h.x, D * 0.2f, h.y);
                ep.velocity = new Vector3(Random.Range(-1f, 1f) * D * 2f, Random.Range(3f, 7f) * D * (center ? 1f : 0.6f), Random.Range(-1f, 0.3f) * D * 2f);
                ep.startColor = Color.Lerp(c, Color.white, Random.Range(0f, 0.5f));
                ep.startSize = D * Random.Range(0.12f, 0.24f);
                ep.startLifetime = Random.Range(0.5f, 0.9f);
                sparks.Emit(ep, 1);
            }
        }

        void SpawnColumn(Vector3 at, Color c, float height, float seconds, float width)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "DropColumn";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            go.transform.position = at + Vector3.up * height * 0.5f;
            go.transform.localScale = new Vector3(width, height * 0.5f, width);
            var r = go.GetComponent<Renderer>();
            var m = Additive(glowTex);
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var lgo = new GameObject("DropLight");
            lgo.transform.SetParent(go.transform, false);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = D * 6f;
            l.intensity = 0f;
            l.shadows = LightShadows.None;
            lgo.transform.position = at + Vector3.up * D;
            bursts.Add(new Burst { column = go.transform, mat = m, light = l, len = seconds, height = height });
            SetColor(m, c);
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (root == null) return;
            float dt = Time.deltaTime;
            time += dt;
            bool want = ambienceEnabled && effects.IsActive && (game.State == GameState.Playing || game.State == GameState.Grace);
            level = Mathf.MoveTowards(level, want ? 1f : 0f, dt * (want ? 1.5f : 0.8f));
            bool visible = level > 0.001f || bursts.Count > 0;
            if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
            if (!visible) return;

            float hype = audioPlayer != null ? Mathf.Clamp01(audioPlayer.Hype) : 0.5f;
            float beatLen = 60f / Mathf.Max(30f, bpm);
            beatClock += dt;
            float beatPhase = beatClock / beatLen;
            bool onBeat = false;
            if (beatClock >= beatLen) { beatClock -= beatLen; onBeat = true; }
            float pulse = Mathf.Exp(-Mathf.Repeat(beatPhase, 1f) * 5f);

            // ムービングライト
            Color c2 = ShiftHue(theme, 0.12f), c3 = Color.Lerp(theme, Color.white, 0.6f);
            Color[] cols = { theme, c2, c3 };
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                l.enabled = movingLights && level > 0.01f;
                if (!l.enabled) continue;
                float a = time * (0.6f + i * 0.17f) + i * 2.1f;
                float x = Mathf.Sin(a) * HalfW * 0.85f;
                float z = Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f + 0.45f * Mathf.Sin(a * 1.3f + i));
                l.transform.position = new Vector3(x, D * (2.5f + i * 0.6f), z);
                l.color = cols[i];
                l.intensity = lightIntensity * level * (0.55f + 0.45f * hype) * (0.8f + 0.4f * pulse);
            }

            // 床の光
            if (floorQuad != null)
            {
                floorQuad.gameObject.SetActive(floorGlow);
                SetColor(floorMat, new Color(theme.r, theme.g, theme.b, (0.15f + 0.2f * hype + 0.12f * pulse) * level));
            }
            if (onBeat && floorGlow && level > 0.5f && Mathf.FloorToInt(time / beatLen) % 2 == 0)
            {
                for (int i = 0; i < ringT.Count; i++) if (ringT[i] < 0f || ringT[i] >= 1f) { ringT[i] = 0f; break; }
            }
            for (int i = 0; i < rings.Count; i++)
            {
                float t = ringT[i];
                if (t < 0f && t > -1f) { ringT[i] = t + dt; rings[i].gameObject.SetActive(false); continue; }
                if (t < 0f || t >= 1f || !floorGlow) { rings[i].gameObject.SetActive(false); continue; }
                ringT[i] = t + dt / 1.1f;
                rings[i].gameObject.SetActive(true);
                float e = 1f - (1f - t) * (1f - t);
                float s = Mathf.Lerp(D * 1.5f, HalfW * 2.6f, e);
                rings[i].localScale = new Vector3(s, s, 1f);
                SetColor(ringMats[i], new Color(theme.r, theme.g, theme.b, (1f - t) * level));
            }

            // ランウェイの光：手前へ流れる
            int perSide = runway.Count / 2;
            for (int i = 0; i < runway.Count; i++)
            {
                runwayT[i].gameObject.SetActive(runwayLights);
                int k = i % perSide;
                float wave = Mathf.Repeat(time * 2.2f - k * 0.12f, 1f);
                float on = Mathf.Pow(1f - wave, 3f);
                Color c = Color.Lerp(theme, Color.white, on * 0.6f);
                SetColor(runway[i], new Color(c.r, c.g, c.b, (0.3f + 0.7f * on) * level));
            }

            foreach (var m in edgeMats)
                SetColor(m, runwayLights ? new Color(theme.r, theme.g, theme.b, (0.45f + 0.35f * pulse) * level) : Color.clear);

            // ただよう光の粒
            if (floatingSparkles && level > 0.05f)
            {
                var em = floaters.emission;
                em.rateOverTime = (10f + 30f * hype) * level;
                var main = floaters.main;
                main.startColor = new ParticleSystem.MinMaxGradient(theme, Color.Lerp(theme, Color.white, 0.7f));
            }
            else
            {
                var em = floaters.emission;
                em.rateOverTime = 0f;
            }

            // 落ちた時の光の柱
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var b = bursts[i];
                b.t += dt;
                float k = b.t / b.len;
                if (k >= 1f || b.column == null)
                {
                    if (b.column != null) Destroy(b.column.gameObject);
                    bursts.RemoveAt(i);
                    continue;
                }
                float w = Mathf.Lerp(1f, 0.15f, k);
                var ls = b.column.localScale;
                b.column.localScale = new Vector3(ls.x * Mathf.Lerp(1f, 0.92f, dt * 10f), b.height * 0.5f * (0.6f + 0.6f * Mathf.Sqrt(k)), ls.z * Mathf.Lerp(1f, 0.92f, dt * 10f));
                var c = b.mat.color;
                SetColor(b.mat, new Color(c.r, c.g, c.b, w * 0.9f));
                b.light.intensity = 4f * w * level;
            }
        }

        static Color ShiftHue(Color c, float amount)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(Mathf.Repeat(h + amount, 1f), s, v);
        }
    }
}
