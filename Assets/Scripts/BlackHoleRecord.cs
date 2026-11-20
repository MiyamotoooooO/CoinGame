using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N011 ブラックホール「イベント・ホライズン」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：下段（プッシャーの前の盤面）のまん中に、大きなブラックホールが出てくる。
    ///   見た目は「どら焼き」のような、まん中がふくらんだ平たい円盤。円盤は中から外へ 白 → 赤 → 暗い赤 → 黒 と光る。
    ///   まん中に真っ黒な球があり、球のふちが白く光る（アウトライン）
    /// ・流れ（1回分）：
    ///   1. 出現（appearSeconds 秒）：ブラックホールがふくらんで出てくる
    ///   2. 吸い込み（suckSeconds 秒）：下段のメダルを全部、回しながら吸い込む（吸い込む範囲がだんだん広がり、最後は下段全部）
    ///   3. 消える（collapseSeconds 秒）：ブラックホールがしぼんで消える
    ///   4. 降ってくる：吸い込んだ枚数 × rainMultiplier 枚のメダルが、下段のまん中に上から降ってくる
    ///   5. クールタイム（cooldownSeconds 秒）：待ってから、また 1 にもどる（曲が流れている間くりかえす）
    /// ・曲が途中で終わった時も、吸い込んだ分は必ず降らせる（メダルが消えたままにならない）
    /// ・モニター：使わない（いつものラウンドの画面のまま）
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class BlackHoleRecord : RecordScriptBase
    {
        public static BlackHoleRecord Instance { get; private set; }

        [Header("流れ（秒）")]
        [Tooltip("曲が始まってから、最初のブラックホールが出るまで")]
        [Range(0f, 10f)] public float firstDelay = 1f;
        [Tooltip("ブラックホールがふくらんで出てくる時間")]
        [Range(0.2f, 5f)] public float appearSeconds = 1.2f;
        [Tooltip("下段のメダルを吸い込む時間（最後には下段のメダルが全部吸い込まれる）")]
        [Range(1f, 20f)] public float suckSeconds = 4f;
        [Tooltip("しぼんで消える時間")]
        [Range(0.2f, 5f)] public float collapseSeconds = 0.9f;
        [Tooltip("クールタイム：メダルが降り終わってから、次のブラックホールが出るまで（仮で30秒）")]
        [Range(0f, 120f)] public float cooldownSeconds = 30f;
        [Tooltip("ON：曲が流れている間、クールタイムごとにくりかえす。OFF：1曲で1回だけ")]
        public bool repeat = true;

        [Header("効果")]
        [Tooltip("降ってくるメダルの枚数 ＝ 吸い込んだ枚数 × この倍率（デバッグで調整する）")]
        [Range(0f, 5f)] public float rainMultiplier = 1.2f;
        [Tooltip("1回に降らせる枚数（多いと一度にたくさん降る。物理が重くならないように少しずつ降らせる）")]
        [Range(1, 30)] public int rainBatch = 6;
        [Tooltip("降らせる間隔（秒）")]
        [Range(0.02f, 1f)] public float rainInterval = 0.1f;
        [Tooltip("降らせる場所の広さ：下段のまん中からの半径（メダルの直径に対する倍率）。0 ならまん中の1点に落ちる")]
        [Range(0f, 5f)] public float rainSpread = 1.2f;

        [Header("吸い込み")]
        [Tooltip("下段のメダルをまん中へ引き寄せる強さ")]
        [Range(0f, 60f)] public float pullForce = 14f;
        [Tooltip("回りながら吸い込まれる強さ（0 でまっすぐ）")]
        [Range(0f, 2f)] public float swirl = 0.9f;

        [Header("大きさ（下段の広さに対する割合）")]
        [Tooltip("円盤の半径（下段の奥行きの半分に対する割合）")]
        [Range(0.3f, 1.2f)] public float diskSize = 0.95f;
        [Tooltip("まん中の黒い球の半径（円盤の半径に対する割合）")]
        [Range(0.1f, 0.6f)] public float coreSize = 0.3f;
        [Tooltip("円盤のふくらみ（円盤の半径に対する割合）")]
        [Range(0.02f, 0.5f)] public float diskThickness = 0.16f;
        [Tooltip("円盤の光の強さ")]
        [Range(0f, 6f)] public float glow = 1.6f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip appearSound;
        public AudioClip absorbSound;
        public AudioClip collapseSound;
        public AudioClip rainSound;

        protected override string DefaultKey => "BlackHole";
        protected override string ScreenTitle => "BLACK HOLE";
        protected override string ScreenSubtitle => "イベント・ホライズン";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;   // モニターはいつもの画面のまま

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<BlackHoleRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed()
        {
            if (Instance == this) Instance = null;
            if (diskTex != null) Destroy(diskTex);
        }

        // 色（中から外へ：白 → 赤 → 暗い赤 → 黒）
        static readonly Color White = new Color(1f, 0.97f, 0.94f);
        static readonly Color Red = new Color(1f, 0.1f, 0.06f);
        static readonly Color DarkRed = new Color(0.38f, 0.02f, 0.03f);

        enum Phase { Wait, Appear, Suck, Collapse, Rain, Done }

        Phase phase;
        float phaseTime, waitSeconds;
        int stored, rainTotal, pendingRain, captureCount;
        float rainTimer, holeSize, absorbPulse, lastAbsorbSound, flash;

        // 盤面の場所
        Vector3 center;      // 下段のまん中（床の上）
        float diskR, coreR, diskH, reach;

        Transform hole, disk, core, outline, outlineGlow, halo;
        Material diskMat, outlineMat, outlineGlowMat, haloMat, floorGlowMat, shockMat;
        Texture2D diskTex;
        Transform shock;
        float shockTime = -1f;
        ParticleSystem dust, swirlFx;
        float swirlCarry;
        readonly List<Medal> lower = new List<Medal>();

        /// <summary>吸い込まれて回りながら消えていくメダル（見た目だけのコピー）</summary>
        class Ghost
        {
            public Transform t;
            public Vector3 from;
            public float a0, r0, age, life, scale;
        }

        readonly List<Ghost> ghosts = new List<Ghost>();
        readonly Stack<Transform> ghostPool = new Stack<Transform>();

        AudioClip madeAppear, madeAbsorb, madeCollapse, madeRain;

        // ================================================================
        // 盤面の部品
        // ================================================================

        protected override void BuildBoard()
        {
            // 下段：盤面の手前のふち 〜 プッシャーの前面
            float depth = Mathf.Max(D, PusherFrontZ - FrontZ);
            center = new Vector3(0f, 0f, (FrontZ + PusherFrontZ) * 0.5f);
            diskR = Mathf.Min(HalfW * 0.9f, depth * 0.5f * diskSize);
            coreR = diskR * coreSize;
            diskH = diskR * diskThickness;
            reach = Mathf.Sqrt(HalfW * HalfW + depth * depth * 0.25f) + D;

            hole = new GameObject("BlackHole").transform;
            hole.SetParent(Board, false);
            hole.position = center;

            // 床にじんわり広がる赤い光
            floorGlowMat = RecordMeshKit.Additive(RecordMeshKit.SoftDot, new Color(1f, 0.12f, 0.06f, 0.6f));
            var fg = Shape(PrimitiveType.Quad, "FloorGlow", hole, new Vector3(0f, D * 0.012f, 0f), Vector3.one * diskR * 3.2f, floorGlowMat);
            fg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // どら焼きの形の円盤（白 → 赤 → 暗い赤 → 黒 に光る）
            diskTex = MakeDiskTexture(coreSize);
            diskMat = ProtoAssets.MakeMaterial(Color.black, 0f, 0.25f);
            diskMat.SetTexture("_EmissionMap", diskTex);
            diskMat.EnableKeyword("_EMISSION");
            diskMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            diskMat.SetColor("_EmissionColor", Color.white * glow);
            disk = RecordMeshKit.Make("Dorayaki", Dorayaki(diskR, diskH, 96, 28), diskMat, hole, Vector3.zero, Vector3.one, Vector3.zero);

            // 円盤の上にうっすら重ねる光（光っている感じを強くする）
            var sheen = RecordMeshKit.Make("DiskSheen", Dorayaki(diskR * 1.01f, diskH * 1.04f, 96, 28),
                RecordMeshKit.Additive(diskTex, new Color(1f, 1f, 1f, 0.55f)), disk, Vector3.zero, Vector3.one, Vector3.zero);
            sheen.localPosition = new Vector3(0f, D * 0.004f, 0f);

            // まん中の真っ黒な球（光を返さない）
            float coreY = diskH * 0.55f + coreR * 0.45f;
            var black = ProtoAssets.MakeMaterial(Color.black, 0f, 0f);
            black.SetFloat("_Glossiness", 0f);
            core = Shape(PrimitiveType.Sphere, "Core", hole, new Vector3(0f, coreY, 0f), Vector3.one * coreR * 2f, black).transform;

            // 球のまわりの赤い光（いつもカメラの方を向く。球の後ろにだけ見える）
            haloMat = RecordMeshKit.Additive(RecordMeshKit.SoftDot, new Color(1f, 0.18f, 0.08f, 0.85f));
            halo = Shape(PrimitiveType.Quad, "Halo", hole, core.localPosition, Vector3.one * coreR * 4.2f, haloMat).transform;

            // 球のふちの白く光るアウトライン（いつもカメラの方を向く）
            outlineMat = RecordMeshKit.Additive(Texture2D.whiteTexture, Color.white);
            outline = RecordMeshKit.Make("Outline", RecordMeshKit.Torus(1f, 0.035f, 0f, 360f, 96, 8), outlineMat, hole, core.localPosition, Vector3.one * coreR * 1.03f, Vector3.zero);
            outlineGlowMat = RecordMeshKit.Additive(RecordMeshKit.LensRing, new Color(1f, 1f, 1f, 0.9f));
            outlineGlow = Shape(PrimitiveType.Quad, "OutlineGlow", hole, core.localPosition, Vector3.one * coreR * 1.03f / 0.21f, outlineGlowMat).transform;

            // 消える時に広がる光の輪
            shockMat = RecordMeshKit.Additive(RecordMeshKit.Ripple, new Color(1f, 0.5f, 0.4f, 1f));
            shock = RecordMeshKit.Make("Shock", RecordMeshKit.Annulus(0.6f, 1f, 96, 4), shockMat, Board, center + Vector3.up * D * 0.05f, Vector3.one, Vector3.zero);
            shock.gameObject.SetActive(false);

            // らせんを描いて吸い込まれる光の粒（穴について動く）
            swirlFx = Particles("Swirl", hole, Vector3.up, 0f, D * 0.07f, 1.1f, 0f, 5f);
            var main = swirlFx.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var vel = swirlFx.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(4f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
            vel.radial = new ParticleSystem.MinMaxCurve(-diskR * 0.9f);

            dust = Particles("Dust", Board, Vector3.up, D * 2.5f, D * 0.14f, 0.9f, 0.2f, 70f);
            hole.gameObject.SetActive(false);
        }

        /// <summary>
        /// どら焼きの形（まん中がふくらんで、ふちが丸く細くなる平たい円盤）。原点は底のまん中。
        /// UV：u がまわり（0〜1）、v がまん中からの距離（0〜1）
        /// </summary>
        static Mesh Dorayaki(float radius, float height, int seg, int rings)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            float lift = height * 0.35f;   // 下のふくらみの分だけ持ち上げる（底のまん中が床に付く）
            for (int side = 0; side < 2; side++)   // 0 = 上、1 = 下
            {
                int start = v.Count;
                for (int j = 0; j <= rings; j++)
                {
                    float k = j / (float)rings;
                    float r = radius * k;
                    float bulge = Mathf.Pow(Mathf.Max(0f, 1f - k * k), 0.45f);
                    float y = side == 0 ? lift + height * bulge : lift - lift * bulge;
                    for (int i = 0; i <= seg; i++)
                    {
                        float a = i / (float)seg * Mathf.PI * 2f;
                        v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                        uv.Add(new Vector2(i / (float)seg, k));
                    }
                }
                for (int j = 0; j < rings; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = start + j * (seg + 1) + i, b = a + seg + 1;
                        if (side == 0) { t.Add(a); t.Add(a + 1); t.Add(b); t.Add(a + 1); t.Add(b + 1); t.Add(b); }
                        else { t.Add(a); t.Add(b); t.Add(a + 1); t.Add(a + 1); t.Add(b); t.Add(b + 1); }
                    }
            }
            var mesh = new Mesh { name = "BlackHoleDorayaki" };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>円盤の模様：まん中（球のふち）から外へ 白 → 赤 → 暗い赤 → 黒。うすい渦の筋が入る。u がまわり、v がまん中からの距離</summary>
        static Texture2D MakeDiskTexture(float coreRatio)
        {
            const int W = 256, H = 128;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "BlackHoleDisk", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.wrapModeV = TextureWrapMode.Clamp;
            var px = new Color[W * H];
            float c0 = Mathf.Clamp01(coreRatio);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = (y + 0.5f) / H;
                    float k = Mathf.Clamp01((v - c0) / Mathf.Max(0.01f, 1f - c0));   // 球のふち 0 → 円盤のふち 1
                    Color col = k < 0.1f ? Color.Lerp(White, Color.Lerp(White, Red, 0.35f), k / 0.1f)
                              : k < 0.35f ? Color.Lerp(Color.Lerp(White, Red, 0.35f), Red, (k - 0.1f) / 0.25f)
                              : k < 0.6f ? Color.Lerp(Red, DarkRed, (k - 0.35f) / 0.25f)
                              : Color.Lerp(DarkRed, Color.black, Mathf.Clamp01((k - 0.6f) / 0.3f));
                    // 渦の筋（中ほど明るさは強いまま、外ほどはっきり）
                    float arm = 0.5f + 0.5f * Mathf.Sin((u * 4f + k * 2.6f) * Mathf.PI * 2f);
                    float fine = 0.5f + 0.5f * Mathf.Sin((u * 19f + k * 7f) * Mathf.PI * 2f);
                    float streak = Mathf.Lerp(1f, 0.6f + 0.3f * arm + 0.1f * fine, Mathf.Clamp01(k * 1.6f));
                    col *= streak;
                    col.a = Mathf.Clamp01(1.15f - k * 0.9f);
                    px[y * W + x] = col;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        // ================================================================
        // 流れ
        // ================================================================

        protected override void OnBegin()
        {
            stored = 0;
            rainTotal = 0;
            captureCount = 0;
            holeSize = 0f;
            SetPhase(Phase.Wait, firstDelay);
        }

        protected override void OnEnd()
        {
            // 曲が途中で終わっても、吸い込んだ分は必ず降らせる
            if (phase == Phase.Appear || phase == Phase.Suck || phase == Phase.Collapse)
            {
                FinishGhosts();
                StartRain(false);
            }
            holeSize = 0f;
            phase = Phase.Done;
        }

        protected override void OnStateChanged(GameState state)
        {
            // ラウンドが終わった・やり直した時は、降らせる途中の分を捨てる
            bool scoring = state == GameState.Playing || state == GameState.Grace;
            if (!scoring)
            {
                pendingRain = 0;
                ClearGhosts();
                if (Active) { holeSize = 0f; phase = Phase.Done; }
            }
        }

        void SetPhase(Phase p, float wait = 0f)
        {
            phase = p;
            phaseTime = 0f;
            waitSeconds = wait;
        }

        protected override void Tick(float dt, bool running)
        {
            absorbPulse = Mathf.MoveTowards(absorbPulse, 0f, dt * 3f);
            flash = Mathf.MoveTowards(flash, 0f, dt * 2f);
            if (!running) return;
            phaseTime += dt;
            switch (phase)
            {
                case Phase.Wait:
                    holeSize = 0f;
                    if (phaseTime >= waitSeconds)
                    {
                        stored = 0;
                        captureCount = 0;
                        SetPhase(Phase.Appear);
                        Play(appearSound, ref madeAppear, () => RecordSynth.Sweep("BlackHoleAppear", 220f, 40f, 1.2f, 0.45f, RecordSynth.Wave.Saw));
                        Shake(0.03f, appearSeconds);
                    }
                    break;

                case Phase.Appear:
                {
                    float k = Mathf.Clamp01(phaseTime / Mathf.Max(0.01f, appearSeconds));
                    // 少し大きくなりすぎてから落ち着く
                    holeSize = k < 0.75f ? Mathf.SmoothStep(0f, 1.08f, k / 0.75f) : Mathf.Lerp(1.08f, 1f, (k - 0.75f) / 0.25f);
                    if (k >= 1f) SetPhase(Phase.Suck);
                    break;
                }

                case Phase.Suck:
                {
                    holeSize = 1f + 0.04f * Mathf.Sin(Time.time * 18f) + 0.1f * absorbPulse;
                    float k = Mathf.Clamp01(phaseTime / Mathf.Max(0.1f, suckSeconds));
                    // 吸い込む範囲がだんだん広がって、最後は下段の全部
                    float r = Mathf.Lerp(coreR * 1.3f, reach, Mathf.Pow(k, 1.4f));
                    CaptureLower(r);
                    // 吸い込み終わって、飛んでいるメダルが全部穴に入ったら消える
                    if (k >= 1f && ghosts.Count == 0)
                    {
                        SetPhase(Phase.Collapse);
                        Play(collapseSound, ref madeCollapse, () => RecordSynth.Sweep("BlackHoleCollapse", 90f, 900f, 0.9f, 0.4f, RecordSynth.Wave.Sine));
                    }
                    break;
                }

                case Phase.Collapse:
                {
                    float k = Mathf.Clamp01(phaseTime / Mathf.Max(0.01f, collapseSeconds));
                    // ぐっとふくらんでから、一気にしぼむ
                    holeSize = k < 0.3f ? Mathf.Lerp(1f, 1.18f, k / 0.3f) : 1.18f * Mathf.Pow(1f - (k - 0.3f) / 0.7f, 2.2f);
                    if (k >= 1f)
                    {
                        holeSize = 0f;
                        StartRain(true);
                    }
                    break;
                }

                case Phase.Rain:
                    holeSize = 0f;
                    if (pendingRain <= 0)
                    {
                        if (repeat) SetPhase(Phase.Wait, cooldownSeconds);
                        else SetPhase(Phase.Done);
                    }
                    break;
            }
        }

        /// <summary>消えた時：吸い込んだ枚数 × 倍率 のメダルを降らせ始める</summary>
        void StartRain(bool show)
        {
            rainTotal = Mathf.CeilToInt(stored * rainMultiplier - 0.0001f);
            pendingRain += rainTotal;
            rainTimer = 0f;
            stored = 0;
            if (show)
            {
                Emit(dust, center + Vector3.up * diskH, White, 60);
                Emit(dust, center + Vector3.up * diskH, Red, 60);
                shockTime = 0f;
                Shake(0.06f, 0.7f);
                Speaker(1f, 16, Red);
                HypeBurst(Red);   // 盤面全体がぱっと光る
                if (rainTotal > 0) Play(rainSound, ref madeRain, () => RecordSynth.Notes("BlackHoleRain", new[] { RecordSynth.Note(24), RecordSynth.Note(28), RecordSynth.Note(31), RecordSynth.Note(36) }, 0.07f, 0.5f, 0.35f, RecordSynth.Wave.Bell));
            }
            SetPhase(Phase.Rain);
        }

        // ================================================================
        // 吸い込み
        // ================================================================

        /// <summary>下段（プッシャーの前の床の上）にあるメダルか</summary>
        bool IsLower(Medal m)
        {
            if (m == null || m.Consumed || m.Body == null) return false;
            Vector3 p = m.transform.position;
            return p.y > -0.2f && p.y < Layout.pusherHeight * 0.6f
                && p.z > FrontZ - D * 0.5f && p.z < Layout.pusherRetractZ
                && Mathf.Abs(p.x) < HalfW + D;
        }

        void CollectLower()
        {
            lower.Clear();
            foreach (var m in BoardMedals) if (IsLower(m)) lower.Add(m);
        }

        /// <summary>まん中から radius 以内の下段のメダルを吸い込む</summary>
        void CaptureLower(float radius)
        {
            CollectLower();
            float r2 = radius * radius;
            foreach (var m in lower)
            {
                Vector3 d = m.transform.position - center;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) Capture(m);
            }
        }

        void FixedUpdate()
        {
            if (!Running || phase != Phase.Suck || game == null || game.Pool == null) return;
            // 下段のメダルを、回しながらまん中へ引き寄せる（吸い込む前のメダルが動いて見える）
            CollectLower();
            float k = Mathf.Clamp01(phaseTime / Mathf.Max(0.1f, suckSeconds));
            foreach (var m in lower)
            {
                Vector3 d = center - m.transform.position;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < 0.001f) continue;
                Vector3 tangent = new Vector3(-d.z, 0f, d.x) / dist;
                m.Body.WakeUp();
                m.Body.AddForce((d / dist + tangent * swirl) * pullForce * (0.4f + 0.6f * k), ForceMode.Acceleration);
            }
        }

        /// <summary>メダルを1枚吸い込む：本物のメダルは消して、見た目だけのコピーが回りながら穴に入る</summary>
        void Capture(Medal m)
        {
            Vector3 p = m.transform.position;
            Vector3 d = p - center;
            d.y = 0f;
            var g = new Ghost
            {
                t = MakeGhost(m),
                from = p,
                a0 = Mathf.Atan2(d.z, d.x),
                r0 = d.magnitude,
                age = 0f,
                life = 0.35f + 0.45f * Mathf.Clamp01(d.magnitude / Mathf.Max(0.1f, reach)),
                scale = 1f,
            };
            if (g.t != null) g.scale = g.t.localScale.x;
            ghosts.Add(g);

            MedalMarks.Clear(m);
            game.Pool.Despawn(m);
            stored++;
            captureCount++;
        }

        Transform MakeGhost(Medal m)
        {
            var mf = m.GetComponentInChildren<MeshFilter>();
            var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
            if (mf == null || mr == null) return null;
            Transform t = ghostPool.Count > 0 ? ghostPool.Pop() : null;
            if (t == null)
            {
                var go = new GameObject("SuckedMedal");
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t = go.transform;
                t.SetParent(Board, false);
            }
            t.gameObject.SetActive(true);
            t.GetComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            t.GetComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
            t.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
            t.localScale = mf.transform.lossyScale;
            return t;
        }

        void UpdateGhosts(float dt)
        {
            Vector3 c = core != null ? core.position : center;
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                var g = ghosts[i];
                g.age += dt;
                float k = Mathf.Clamp01(g.age / g.life);
                float e = k * k;   // だんだん速く
                if (g.t != null)
                {
                    float r = g.r0 * (1f - e);
                    float a = g.a0 + e * Mathf.PI * 2f * (0.6f + g.r0 / Mathf.Max(0.1f, reach));
                    Vector3 pos = new Vector3(center.x + Mathf.Cos(a) * r, Mathf.Lerp(g.from.y, c.y, e), center.z + Mathf.Sin(a) * r);
                    g.t.position = pos;
                    g.t.Rotate(0f, dt * 720f, dt * 300f, Space.World);
                    float s = Mathf.Max(0.001f, 1f - e * 0.95f);
                    g.t.localScale = Vector3.one * g.scale * s;
                }
                if (k >= 1f)
                {
                    if (g.t != null) { g.t.gameObject.SetActive(false); ghostPool.Push(g.t); }
                    ghosts.RemoveAt(i);
                    absorbPulse = 1f;
                    Emit(dust, c, Random.value < 0.5f ? White : Red, 3);
                    if (Time.time - lastAbsorbSound > 0.06f)
                    {
                        lastAbsorbSound = Time.time;
                        Play(absorbSound, ref madeAbsorb, () => RecordSynth.Sweep("BlackHoleAbsorb2", 700f, 90f, 0.25f, 0.22f, RecordSynth.Wave.Sine), 0.45f);
                    }
                }
            }
        }

        /// <summary>飛んでいる途中のメダルを全部、すぐ穴に入れる（曲が終わった時）</summary>
        void FinishGhosts()
        {
            foreach (var g in ghosts)
                if (g.t != null) { g.t.gameObject.SetActive(false); ghostPool.Push(g.t); }
            ghosts.Clear();
        }

        void ClearGhosts() => FinishGhosts();

        void UpdateRain(float dt)
        {
            if (pendingRain <= 0) return;
            if (!IsScoring) { pendingRain = 0; return; }
            rainTimer -= dt;
            if (rainTimer > 0f) return;
            rainTimer = rainInterval;
            int n = Mathf.Min(pendingRain, rainBatch);
            pendingRain -= n;
            RainAtCenter(n);
        }

        /// <summary>メダルを count 枚、下段のまん中（ブラックホールがあった所）の上から落とす</summary>
        void RainAtCenter(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 o = Random.insideUnitCircle * D * rainSpread;
                float x = Mathf.Clamp(center.x + o.x, -HalfW * 0.85f, HalfW * 0.85f);
                float z = Mathf.Clamp(center.z + o.y, FrontZ + D, PusherFrontZ - D * 0.5f);
                float y = Layout.launcherHeight + D * (0.5f + i * 0.35f);
                SpawnMedal(new Vector3(x, y, z), new Vector3(0f, -D, 0f));
            }
        }

        protected override bool KeepBoardVisible() => pendingRain > 0 || ghosts.Count > 0 || (dust != null && dust.particleCount > 0) || shockTime >= 0f;

        // ================================================================
        // 見た目
        // ================================================================

        protected override void UpdateBoard(float dt)
        {
            UpdateGhosts(dt);
            UpdateRain(dt);

            // 消えた時の光の輪
            if (shockTime >= 0f)
            {
                shockTime += dt;
                float k = shockTime / 0.8f;
                shock.gameObject.SetActive(k < 1f);
                shock.localScale = Vector3.one * Mathf.Lerp(coreR, reach * 1.2f, 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 2f));
                RecordMeshKit.SetColor(shockMat, new Color(1f, 0.55f, 0.45f, 1f - Mathf.Clamp01(k)));
                if (k >= 1f) shockTime = -1f;
            }

            float size = Active ? holeSize : 0f;
            bool show = size > 0.002f;
            if (hole.gameObject.activeSelf != show) hole.gameObject.SetActive(show);
            if (!show) return;
            hole.position = center;
            hole.localScale = Vector3.one * size;

            float t = Time.time;
            bool sucking = phase == Phase.Suck;
            float spin = sucking ? 160f : 90f;
            disk.localRotation = Quaternion.Euler(0f, -t * spin, 0f);
            float pulse = 1f + 0.12f * Mathf.Sin(t * 9f) + 0.6f * absorbPulse;
            diskMat.SetColor("_EmissionColor", Color.white * glow * pulse);
            RecordMeshKit.SetColor(floorGlowMat, new Color(1f, 0.12f, 0.06f, 0.45f + 0.25f * absorbPulse));
            RecordMeshKit.SetColor(haloMat, new Color(1f, 0.18f, 0.08f, 0.7f + 0.3f * absorbPulse));

            // アウトラインと光は、いつもカメラの方を向く
            var cam = Camera.main;
            if (cam != null)
            {
                Quaternion face = Quaternion.LookRotation(core.position - cam.transform.position, Vector3.up);
                halo.rotation = face;
                outlineGlow.rotation = face;
                outline.rotation = face;
            }
            float o = 0.9f + 0.1f * Mathf.Sin(t * 23f);
            RecordMeshKit.SetColor(outlineMat, Color.white * o);
            RecordMeshKit.SetColor(outlineGlowMat, new Color(1f, 1f, 1f, 0.75f + 0.25f * absorbPulse));

            // らせんの光の粒（吸い込み中は多く）
            swirlCarry += dt * (sucking ? 140f : 50f);
            int n = Mathf.FloorToInt(swirlCarry);
            swirlCarry -= n;
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float r = diskR * Random.Range(0.75f, 1.1f);
                var ep = new ParticleSystem.EmitParams
                {
                    position = new Vector3(Mathf.Cos(a) * r, diskH * Random.Range(0.3f, 1.2f), Mathf.Sin(a) * r),
                    velocity = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * diskR * 0.4f,
                    startColor = Random.value < 0.35f ? White : Random.value < 0.7f ? Red : new Color(1f, 0.35f, 0.2f),
                    startSize = D * Random.Range(0.05f, 0.1f),
                    startLifetime = Random.Range(0.6f, 1.1f),
                    applyShapeToPosition = false,
                };
                swirlFx.Emit(ep, 1);
            }
        }
    }
}
