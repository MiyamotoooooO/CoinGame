using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N017 招き猫「コバンの手招き」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：右の発射口のあたりに、招き猫（コバン）がぽんと出てくる。最初はふつうの大きさ
    /// ・メダルが中央の穴に落ちるたびに、落ちた所から青白い「魂（人魂）」がふわっと出てきて、しっぽを引きながら招き猫へ飛んでいき、
    ///   招き猫に吸いこまれる。魂を1つ吸いこむたびに、招き猫は手招きして、少しずつ巨大化していく
    /// ・効果：曲が終わりそうになったら（残り rainLead 秒）、招き猫が大きく手招きして、
    ///   吸いこんだ魂の数 × rainMultiplier（3倍）のメダルを、上から降らせる。そのあと招き猫は元の大きさにもどる
    ///   （曲が急に終わった時も、吸いこんだ分は必ず降らせる）
    /// ・招き猫と魂は見た目だけ（当たり判定なし）。大きくなってモニターの前に来た部分は、モニターより手前に描く
    /// ・モニター：使わない（いつものラウンドの画面のまま）
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class ManekiNekoRecord : RecordScriptBase
    {
        public static ManekiNekoRecord Instance { get; private set; }

        [Header("招き猫")]
        [Tooltip("最初の招き猫の高さ（メダルの直径に対する倍率）")]
        [Range(0.5f, 5f)] public float catHeight = 1.4f;
        [Tooltip("魂を1つ吸いこむたびに大きくなる割合（0.08 なら 8% ずつ）")]
        [Range(0f, 0.5f)] public float growPerSoul = 0.08f;
        [Tooltip("一番大きくなった時の大きさ（最初の何倍まで）")]
        [Range(1f, 10f)] public float maxGrow = 4f;

        [Header("魂")]
        [Tooltip("魂が招き猫に届くまでの時間（秒）")]
        [Range(0.3f, 3f)] public float soulSeconds = 1.1f;

        [Header("効果（メダルを降らせる）")]
        [Tooltip("曲の残りが何秒になったら降らせるか")]
        [Range(0.5f, 20f)] public float rainLead = 4f;
        [Tooltip("降らせる枚数 ＝ 吸いこんだ魂の数 × この倍率")]
        [Range(0f, 10f)] public float rainMultiplier = 3f;
        [Tooltip("1回に降らせる枚数（少しずつ降らせて、物理が重くならないようにする）")]
        [Range(1, 30)] public int rainBatch = 6;
        [Tooltip("降らせる間隔（秒）")]
        [Range(0.02f, 1f)] public float rainInterval = 0.08f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip appearSound;
        public AudioClip soulSound;
        public AudioClip rainSound;

        protected override string DefaultKey => "ManekiNeko";
        protected override string ScreenTitle => "MANEKI NEKO";
        protected override string ScreenSubtitle => "コバンの手招き";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;   // モニターはいつもの画面のまま

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<ManekiNekoRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color White = new Color32(0xfa, 0xf6, 0xf0, 0xff);
        static readonly Color Red = new Color32(0xe0, 0x3a, 0x3a, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xcc, 0x33, 0xff);
        static readonly Color Pink = new Color32(0xff, 0x9a, 0xb0, 0xff);
        static readonly Color SoulCore = new Color(0.85f, 0.97f, 1f);
        static readonly Color SoulBlue = new Color(0.35f, 0.75f, 1f);

        int souls, pendingRain;
        bool rained;
        float scale = 1f, scaleTarget = 1f, pulse, pawTimer, rainTimer, appear;
        Vector3 catPos;

        Transform cat, paw;
        Material coinMaterial, bodyMat;
        ParticleSystem shine, wisp;
        Material soulMat, trailMat;

        class Soul
        {
            public Transform t;
            public TrailRenderer trail;
            public Vector3 from, mid;
            public float age, life, wobble;
        }
        readonly List<Soul> flying = new List<Soul>();
        readonly Stack<Soul> soulPool = new Stack<Soul>();
        float lastSoulSound;

        AudioClip madeAppear, madeSoul, madeRain;

        // ================================================================
        // 招き猫（足もとが原点。前が +Z）
        // ================================================================

        protected override void BuildBoard()
        {
            cat = new GameObject("ManekiNeko").transform;
            cat.SetParent(Board, false);
            float s = D * catHeight / 1.35f;   // 1.35 は下の形の高さ

            bodyMat = Glow(White, 0.15f);
            var red = Glow(Red, 0.4f);
            var pink = Glow(Pink, 0.3f);
            var dark = ProtoAssets.MakeMaterial(new Color(0.08f, 0.06f, 0.08f), 0f, 0.9f);
            coinMaterial = Glow(Gold, 1f);
            var bell = Glow(Gold, 0.6f);

            Shape(PrimitiveType.Sphere, "Body", cat, new Vector3(0f, s * 0.4f, 0f), new Vector3(s * 0.7f, s * 0.8f, s * 0.6f), bodyMat);
            Shape(PrimitiveType.Sphere, "Head", cat, new Vector3(0f, s * 0.95f, 0f), new Vector3(s * 0.66f, s * 0.56f, s * 0.56f), bodyMat);
            for (int k = -1; k <= 1; k += 2)
            {
                var ear = Cube("Ear", cat, new Vector3(k * s * 0.2f, s * 1.22f, 0f), new Vector3(s * 0.15f, s * 0.17f, s * 0.1f), bodyMat);
                ear.transform.localRotation = Quaternion.Euler(0f, 0f, -k * 20f);
                var inner = Cube("EarIn", cat, new Vector3(k * s * 0.2f, s * 1.21f, s * 0.05f), new Vector3(s * 0.08f, s * 0.1f, s * 0.02f), pink);
                inner.transform.localRotation = Quaternion.Euler(0f, 0f, -k * 20f);
                // 目（にっこり）・ほっぺ・ひげ
                var eye = Cube("Eye", cat, new Vector3(k * s * 0.12f, s * 1.0f, s * 0.27f), new Vector3(s * 0.1f, s * 0.025f, s * 0.02f), dark);
                eye.transform.localRotation = Quaternion.Euler(0f, 0f, k * 12f);
                Shape(PrimitiveType.Sphere, "Cheek", cat, new Vector3(k * s * 0.19f, s * 0.9f, s * 0.24f), new Vector3(s * 0.09f, s * 0.05f, s * 0.03f), pink);
                for (int w = -1; w <= 1; w += 2)
                {
                    var wh = Cube("Whisker", cat, new Vector3(k * s * 0.3f, s * (0.9f + w * 0.03f), s * 0.24f), new Vector3(s * 0.16f, s * 0.008f, s * 0.008f), dark);
                    wh.transform.localRotation = Quaternion.Euler(0f, 0f, k * w * 8f);
                }
            }
            Shape(PrimitiveType.Sphere, "Nose", cat, new Vector3(0f, s * 0.93f, s * 0.28f), new Vector3(s * 0.05f, s * 0.035f, s * 0.03f), pink);
            Cube("Collar", cat, new Vector3(0f, s * 0.72f, 0f), new Vector3(s * 0.62f, s * 0.06f, s * 0.52f), red);
            Shape(PrimitiveType.Sphere, "Bell", cat, new Vector3(0f, s * 0.66f, s * 0.27f), Vector3.one * s * 0.1f, bell);
            // 小判（左手で持つ）
            Shape(PrimitiveType.Cylinder, "Koban", cat, new Vector3(-s * 0.08f, s * 0.42f, s * 0.31f), new Vector3(s * 0.36f, s * 0.02f, s * 0.5f), coinMaterial).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shape(PrimitiveType.Sphere, "LeftPaw", cat, new Vector3(-s * 0.22f, s * 0.42f, s * 0.28f), Vector3.one * s * 0.16f, bodyMat);
            // 上げた右手（手招きで動く）
            paw = new GameObject("Paw").transform;
            paw.SetParent(cat, false);
            paw.localPosition = new Vector3(s * 0.3f, s * 0.85f, 0f);
            Shape(PrimitiveType.Sphere, "PawBall", paw, new Vector3(0f, s * 0.25f, 0f), new Vector3(s * 0.18f, s * 0.36f, s * 0.18f), bodyMat);
            Shape(PrimitiveType.Sphere, "PawPad", paw, new Vector3(0f, s * 0.36f, s * 0.08f), new Vector3(s * 0.1f, s * 0.1f, s * 0.04f), pink);

            shine = Particles("Shine", Board, Vector3.up, D * 1.5f, D * 0.1f, 0.8f, -0.1f, 50f);
            wisp = Particles("Sparkle", Board, Vector3.up, D * 0.3f, D * 0.12f, 0.5f, -0.2f, 60f);
            soulMat = RecordMeshKit.Additive(RecordMeshKit.SoftDot, SoulCore);
            trailMat = RecordMeshKit.Additive(Texture2D.whiteTexture, SoulBlue);

            frontParts.Clear();
            foreach (var r in cat.GetComponentsInChildren<Renderer>(true)) frontParts.Add(r);
            cat.gameObject.SetActive(false);
        }

        /// <summary>右の発射口のあたり（発射口の少し手前の、プッシャーの上の右はし）</summary>
        Vector3 CatSpot()
        {
            Vector3 muzzle = game.Launcher != null ? game.Launcher.MuzzlePosition(1) : new Vector3(HalfW - D * 0.6f, Layout.launcherHeight, BackZ - D * 0.6f);
            float x = HalfW - D * catHeight * 0.32f;
            return new Vector3(x, Layout.pusherHeight, muzzle.z - D * 0.9f);
        }

        // ================================================================
        // 流れ
        // ================================================================

        protected override void OnBegin()
        {
            souls = 0;
            pendingRain = 0;
            rained = false;
            scale = scaleTarget = 1f;
            appear = 0f;
            catPos = CatSpot();
            cat.position = catPos;
            cat.rotation = Quaternion.Euler(0f, 205f, 0f);   // 中央と手前の方を向く
            cat.gameObject.SetActive(true);
            Emit(shine, catPos + Vector3.up * D * 0.6f, Gold, 30);
            Play(appearSound, ref madeAppear, () => RecordSynth.Notes("NekoAppear", new[] { 1568f, 2093f, 2637f }, 0.07f, 0.5f, 0.3f, RecordSynth.Wave.Bell));
        }

        protected override void OnEnd()
        {
            // 曲が急に終わった時も、吸いこんだ分（飛んでいる途中の魂もふくむ）は降らせる
            souls += flying.Count;
            ClearSouls();
            if (!rained && IsScoring) StartRain();
            cat.gameObject.SetActive(false);
        }

        protected override void OnStateChanged(GameState state)
        {
            bool scoring = state == GameState.Playing || state == GameState.Grace;
            if (!scoring)
            {
                pendingRain = 0;
                ClearSouls();
            }
        }

        protected override void Tick(float dt, bool running)
        {
            if (!running) return;
            // 曲が終わりそうになったら、魂の3倍のメダルを降らせる
            if (!rained && effects.Remaining <= rainLead && effects.Remaining > 0f) StartRain();
        }

        void StartRain()
        {
            rained = true;
            int n = Mathf.CeilToInt(souls * rainMultiplier - 0.0001f);
            souls = 0;
            scaleTarget = 1f;   // 魂を使ったので、元の大きさにもどる
            if (n <= 0) return;
            pendingRain += n;
            rainTimer = 0f;
            pawTimer = 2f;
            Emit(shine, cat.position + Vector3.up * D * catHeight * scale * 0.7f, Gold, 60);
            Play(rainSound, ref madeRain, () => RecordSynth.Notes("NekoRain", new[] { 1568f, 2093f, 2637f, 3136f, 4186f }, 0.06f, 0.6f, 0.3f, RecordSynth.Wave.Bell));
            PopCenter($"おいでおいで！ メダル {n}枚", Gold);
            Speaker(1f, 16, Gold);
            Shake(0.04f, 0.5f);
        }

        void UpdateRain(float dt)
        {
            if (pendingRain <= 0) return;
            if (!IsScoring) { pendingRain = 0; return; }
            rainTimer -= dt;
            if (rainTimer > 0f) return;
            rainTimer = rainInterval;
            int n = Mathf.Min(pendingRain, rainBatch);
            pendingRain -= n;
            var list = RainMedals(n);
            foreach (var m in list) Emit(shine, m.transform.position, Gold, 2);
        }

        // ================================================================
        // 魂
        // ================================================================

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center || medal == null || rained) return;
            // 落ちた所（手前のふちの少し下）から、魂が出てくる
            Vector3 at = medal.transform.position;
            at.y = Mathf.Max(at.y, -D * 0.2f);
            SpawnSoul(at);
        }

        void SpawnSoul(Vector3 at)
        {
            Soul s = soulPool.Count > 0 ? soulPool.Pop() : MakeSoul();
            s.t.gameObject.SetActive(true);
            s.from = at;
            // いったんふわっと上がってから、招き猫の方へカーブして飛ぶ
            s.mid = Vector3.Lerp(at, cat.position, 0.35f) + Vector3.up * D * Random.Range(1.6f, 2.6f) + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-0.5f, 0.5f)) * D;
            s.age = 0f;
            s.life = soulSeconds * Random.Range(0.85f, 1.2f);
            s.wobble = Random.Range(0f, 10f);
            s.t.position = at;
            if (s.trail != null) s.trail.Clear();
            flying.Add(s);
            Emit(wisp, at, SoulBlue, 6);
        }

        Soul MakeSoul()
        {
            var go = Shape(PrimitiveType.Quad, "Soul", Board, Vector3.zero, Vector3.one * D * 0.55f, soulMat);
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.35f;
            tr.minVertexDistance = D * 0.05f;
            tr.widthMultiplier = D * 0.3f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(SoulCore, 0f), new GradientColorKey(SoulBlue, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.sharedMaterial = trailMat;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            frontParts.Add(go.GetComponent<Renderer>());
            frontParts.Add(tr);
            return new Soul { t = go.transform, trail = tr };
        }

        void ClearSouls()
        {
            foreach (var s in flying)
            {
                s.t.gameObject.SetActive(false);
                soulPool.Push(s);
            }
            flying.Clear();
        }

        Vector3 CatChest => cat.position + Vector3.up * D * catHeight * scale * 0.55f;

        void UpdateSouls(float dt)
        {
            var cam = Camera.main;
            Vector3 target = CatChest;
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var s = flying[i];
                s.age += dt;
                float k = Mathf.Clamp01(s.age / s.life);
                float e = k * k * (3f - 2f * k);
                // 2次のベジェ曲線（出た所 → ふわっと上 → 招き猫）。ゆらゆら揺れる
                Vector3 a = Vector3.Lerp(s.from, s.mid, e), b = Vector3.Lerp(s.mid, target, e);
                Vector3 p = Vector3.Lerp(a, b, e) + new Vector3(Mathf.Sin(s.age * 9f + s.wobble), Mathf.Cos(s.age * 7f + s.wobble) * 0.5f, 0f) * D * 0.12f * (1f - k);
                s.t.position = p;
                if (cam != null) s.t.rotation = cam.transform.rotation;
                float flick = 1f + 0.2f * Mathf.Sin(s.age * 30f + s.wobble);
                s.t.localScale = Vector3.one * D * 0.55f * flick * (k > 0.85f ? Mathf.Lerp(1f, 0.3f, (k - 0.85f) / 0.15f) : 1f);
                if (Random.value < 0.4f) Emit(wisp, p, Random.value < 0.5f ? SoulBlue : SoulCore, 1);
                if (k >= 1f)
                {
                    // 吸いこんだ
                    s.t.gameObject.SetActive(false);
                    soulPool.Push(s);
                    flying.RemoveAt(i);
                    Absorb();
                }
            }
        }

        void Absorb()
        {
            if (rained) return;
            souls++;
            scaleTarget = Mathf.Min(maxGrow, 1f + growPerSoul * souls);
            pulse = 1f;
            pawTimer = Mathf.Max(pawTimer, 0.6f);
            Emit(wisp, CatChest, SoulCore, 10);
            if (Time.time - lastSoulSound > 0.06f)
            {
                lastSoulSound = Time.time;
                Play(soulSound, ref madeSoul, () => RecordSynth.Sweep("NekoSoul", 300f, 1200f, 0.25f, 0.22f, RecordSynth.Wave.Sine), 0.5f);
            }
        }

        // ================================================================
        // モニターより手前に描く（大きくなった招き猫と魂が、モニターに隠れないように）
        // ================================================================

        readonly List<Renderer> frontParts = new List<Renderer>();
        Renderer monitorRenderer;
        float monitorSearch;

        float MonitorBottom()
        {
            if (monitorRenderer == null)
            {
                monitorSearch -= Time.unscaledDeltaTime;
                if (monitorSearch <= 0f)
                {
                    monitorSearch = 1f;
                    var go = GameObject.Find(game.monitor.screenObjectName);
                    if (go != null) monitorRenderer = go.GetComponentInChildren<Renderer>();
                }
            }
            if (monitorRenderer != null) return monitorRenderer.bounds.min.y - D * 0.2f;
            return Layout.wallHeight - D * 0.2f;
        }

        void UpdateFrontLayers()
        {
            bool use = game.monitor.effectsInFrontOfMonitor;
            int front = Mathf.Clamp(game.monitor.frontEffectsLayer, 0, 31);
            float limit = MonitorBottom();
            for (int i = 0; i < frontParts.Count; i++)
            {
                var r = frontParts[i];
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                int want = use && r.bounds.center.y > limit ? front : 0;
                if (r.gameObject.layer != want) r.gameObject.layer = want;
            }
        }

        // ================================================================
        // 見た目（毎フレーム）
        // ================================================================

        protected override bool KeepBoardVisible() => pendingRain > 0 || (shine != null && shine.particleCount > 0) || (wisp != null && wisp.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            UpdateRain(dt);
            UpdateSouls(dt);
            if (Active && cat.gameObject.activeSelf)
            {
                appear = Mathf.MoveTowards(appear, 1f, dt * 3f);
                float pop = appear < 1f ? Mathf.Sin(appear * Mathf.PI * 0.5f) * (1f + 0.25f * Mathf.Sin(appear * Mathf.PI)) : 1f;
                scale = Mathf.MoveTowards(scale, scaleTarget, dt * Mathf.Max(0.3f, Mathf.Abs(scaleTarget - scale) * 3f));
                pulse = Mathf.MoveTowards(pulse, 0f, dt * 4f);
                float s = Mathf.Max(0.01f, scale * pop * (1f + 0.12f * pulse) * BoardShown);
                cat.localScale = Vector3.one * s;
                // 大きくなっても、足もとは同じ場所。右の壁からはみ出さないように、少し内側へずらす
                float halfW = D * catHeight * 0.35f * s;
                cat.position = new Vector3(Mathf.Min(catPos.x, HalfW - halfW), catPos.y, catPos.z);
            }
            pawTimer = Mathf.MoveTowards(pawTimer, 0f, dt * 0.8f);
            if (paw != null)
            {
                float wave = pawTimer > 0f ? Mathf.Sin(Time.time * 14f) * 40f * Mathf.Clamp01(pawTimer) : Mathf.Sin(Time.time * 2f) * 6f;
                paw.localRotation = Quaternion.Euler(wave, 0f, 0f);
            }
            if (coinMaterial != null) SetGlow(coinMaterial, Gold, 0.8f + 2.5f * Mathf.Clamp01(pawTimer));
            // 魂をためるほど、体がほんのり青白く光る
            if (bodyMat != null) SetGlow(bodyMat, White, 0.15f + 0.25f * pulse + 0.05f * Mathf.Min(souls, 20));
            UpdateFrontLayers();
        }
    }
}
