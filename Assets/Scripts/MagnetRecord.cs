using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N012 磁石「N極・S極」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：下段（プッシャーの前の盤面）の左はしに N極（赤）、右はしに S極（青）の長方形の磁石が出てくる
    /// ・流れ（1回分）：
    ///   1. ためる（chargeSeconds 秒）：磁石が床からせり上がり、2つの磁石のまわりと間に、紫色のビリビリ（電気）が走る
    ///   2. 寄る：2つの磁石が、だんだん速くなりながら中央へ寄る。寄る時に、間のメダルも中央へ寄せていく
    ///   3. 止まる：これ以上寄れなくなったら（磁石どうしがくっつくか、間のメダルがぎゅうぎゅうになったら）、その場で少し震えて
    ///   4. 消える：磁石がその場で床に沈んで消える（爆発はしない）。メダルは中央に集まったまま
    ///   5. クールタイム（cooldownSeconds 秒）：待ってから、また 1 にもどる（曲が流れている間くりかえす）
    /// ・モニター：使わない（いつものラウンドの画面のまま）
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class MagnetRecord : RecordScriptBase
    {
        public static MagnetRecord Instance { get; private set; }

        [Header("流れ（秒）")]
        [Tooltip("曲が始まってから、最初の磁石が出るまで")]
        [Range(0f, 10f)] public float firstDelay = 1f;
        [Tooltip("磁石が出て、紫色のビリビリが走る時間（このあと磁石が寄る）")]
        [Range(0.3f, 5f)] public float chargeSeconds = 1.6f;
        [Tooltip("これ以上寄れなくなってから、消え始めるまで（ぶるぶる震える時間）")]
        [Range(0f, 2f)] public float clashSeconds = 0.35f;
        [Tooltip("磁石が床に沈んで消える時間")]
        [Range(0.1f, 3f)] public float vanishSeconds = 0.6f;
        [Tooltip("クールタイム：磁石が消えてから、次の磁石が出るまで（仮で30秒）")]
        [Range(0f, 120f)] public float cooldownSeconds = 30f;
        [Tooltip("ON：曲が流れている間、クールタイムごとにくりかえす。OFF：1曲で1回だけ")]
        public bool repeat = true;

        [Header("寄る速さ（メダルの直径 / 秒）")]
        [Tooltip("寄り始めの速さ")]
        [Range(0f, 20f)] public float startSpeed = 2f;
        [Tooltip("一番速い時の速さ")]
        [Range(1f, 60f)] public float maxSpeed = 16f;
        [Tooltip("速くなる勢い（1秒あたり）")]
        [Range(1f, 200f)] public float acceleration = 40f;
        [Tooltip("寄る時に、下段のメダルを中央へ引き寄せる強さ")]
        [Range(0f, 80f)] public float pullForce = 24f;
        [Tooltip("磁石の通り道にいるメダルを、中央へ押す強さ（磁石の速さに対する倍率）")]
        [Range(1f, 4f)] public float pushPower = 1.8f;
        [Tooltip("押されたメダルが出る、一番おそい速さ（メダルの直径 / 秒）")]
        [Range(0f, 20f)] public float minPushSpeed = 6f;
        [Tooltip("ON：磁石に追いこされた（取り残された）メダルを、磁石の内側に移して、いっしょに中央へ運ぶ")]
        public bool sweepPassed = true;
        [Tooltip("ぎゅうぎゅうになった時に、メダルが何段まで積み重なるとみるか（大きいほど、磁石が中央の近くまで寄る）")]
        [Range(1f, 10f)] public float packLayers = 4f;
        [Tooltip("磁石どうしが一番近づいた時のすき間（メダルの直径に対する倍率）")]
        [Range(0f, 4f)] public float minGap = 0.6f;

        [Header("磁石の大きさ（メダルの直径に対する倍率）")]
        [Tooltip("磁石の長さ（奥行き）。下段の奥行きに対する割合")]
        [Range(0.3f, 1f)] public float magnetLength = 0.8f;
        [Range(0.3f, 3f)] public float magnetHeight = 0.9f;
        [Range(0.2f, 2f)] public float magnetThickness = 0.6f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip chargeSound;
        public AudioClip rushSound;
        public AudioClip clashSound;
        public AudioClip vanishSound;

        protected override string DefaultKey => "Magnet";
        protected override string ScreenTitle => "MAGNET";
        protected override string ScreenSubtitle => "N極・S極";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;   // モニターはいつもの画面のまま

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<MagnetRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color NRed = new Color32(0xff, 0x2e, 0x3a, 0xff);
        static readonly Color SBlue = new Color32(0x2e, 0x6c, 0xff, 0xff);
        static readonly Color Purple = new Color32(0xb8, 0x4c, 0xff, 0xff);
        static readonly Color PurpleHot = new Color32(0xe6, 0xb8, 0xff, 0xff);

        enum Phase { Wait, Charge, Rush, Clash, Vanish, Done }

        Phase phase;
        float phaseTime, waitSeconds;
        float half;        // 中央から磁石の内側の面までの距離（左右とも同じ）
        float speed, stopHalf;
        float shake;

        // 盤面の場所
        Vector3 center;
        float startHalf, barLen, barH, barT;
        readonly List<Medal> lower = new List<Medal>();

        // ---- 盤面の部品 ----
        class Bar
        {
            public Transform root;
            public Material body, cap;
            public Color color;
        }
        readonly Bar[] bars = new Bar[2];   // 0 = 左（N）、1 = 右（S）
        readonly List<LineRenderer> bolts = new List<LineRenderer>();
        Material boltMaterial;
        float boltTimer;
        ParticleSystem sparks, fieldDust;

        AudioClip madeCharge, madeRush, madeClash, madeVanish;

        // ================================================================
        // 盤面の部品
        // ================================================================

        static Material Shiny(Color c, float metal, float smooth, float glow)
        {
            var m = ProtoAssets.MakeMaterial(c, metal, smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            return m;
        }

        protected override void BuildBoard()
        {
            float depth = Mathf.Max(D, PusherFrontZ - FrontZ);
            center = new Vector3(0f, 0f, (FrontZ + PusherFrontZ) * 0.5f);
            barLen = depth * magnetLength;
            barH = D * magnetHeight;
            barT = D * magnetThickness;
            startHalf = Mathf.Max(D, HalfW - barT - D * 0.05f);

            for (int i = 0; i < 2; i++) bars[i] = BuildBar(i == 0 ? "MagnetN" : "MagnetS", i == 0 ? NRed : SBlue, i == 0 ? "N" : "S", i == 0 ? 1 : -1);

            boltMaterial = RecordMeshKit.Additive(Texture2D.whiteTexture, Purple);
            for (int i = 0; i < 12; i++)
            {
                var go = new GameObject("Bolt" + i);
                go.transform.SetParent(Board, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 10;
                lr.widthMultiplier = D * 0.05f;
                lr.sharedMaterial = boltMaterial;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 2;
                lr.enabled = false;
                bolts.Add(lr);
            }

            sparks = Particles("Sparks", Board, Vector3.up, D * 6f, D * 0.1f, 0.9f, 0.6f, 80f);
            fieldDust = Particles("Sparkle", Board, Vector3.up, D * 1.2f, D * 0.06f, 0.6f, 0f, 90f);
        }

        /// <summary>長方形の磁石（原点は床の上の、内側の面のまん中）。inward は中央の向き（左の磁石は +1）</summary>
        Bar BuildBar(string name, Color c, string letter, int inward)
        {
            var b = new Bar { color = c };
            b.root = new GameObject(name).transform;
            b.root.SetParent(Board, false);
            b.body = Shiny(c, 0.3f, 0.85f, 0.5f);
            b.cap = Shiny(new Color(0.85f, 0.87f, 0.92f), 0.9f, 0.9f, 0.1f);
            // 本体（内側の面が原点に来るように、外側へずらす）
            Cube("Body", b.root, new Vector3(-inward * barT * 0.5f, barH * 0.5f, 0f), new Vector3(barT, barH, barLen), b.body);
            // 前と後ろのはしの銀色のふた
            for (int k = -1; k <= 1; k += 2)
                Cube("Cap", b.root, new Vector3(-inward * barT * 0.5f, barH * 0.5f, k * (barLen * 0.5f + D * 0.04f)), new Vector3(barT * 1.04f, barH * 1.04f, D * 0.08f), b.cap);
            // 上の面と内側の面に、大きな文字
            AddLetter(b.root, letter, new Vector3(-inward * barT * 0.5f, barH + D * 0.01f, 0f), Quaternion.Euler(90f, 0f, 0f), Mathf.Min(barT, barLen) * 0.9f);
            AddLetter(b.root, letter, new Vector3(D * 0.01f * inward, barH * 0.5f, 0f), Quaternion.Euler(0f, inward > 0 ? -90f : 90f, 0f), barH * 0.8f);
            for (int k = -1; k <= 1; k += 2)
                AddLetter(b.root, letter, new Vector3(D * 0.01f * inward, barH * 0.5f, k * barLen * 0.32f), Quaternion.Euler(0f, inward > 0 ? -90f : 90f, 0f), barH * 0.55f);
            b.root.gameObject.SetActive(false);
            return b;
        }

        static Font letterFont;

        void AddLetter(Transform parent, string text, Vector3 pos, Quaternion rot, float size)
        {
            if (letterFont == null)
            {
                try { letterFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { letterFont = null; }
            }
            if (letterFont == null) return;
            var go = new GameObject("Letter" + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            var tm = go.AddComponent<TextMesh>();
            tm.font = letterFont;
            tm.text = text;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            tm.characterSize = size / 6.4f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = letterFont.material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ================================================================
        // 流れ
        // ================================================================

        protected override void OnBegin()
        {
            half = startHalf;
            SetPhase(Phase.Wait, firstDelay);
        }

        protected override void OnEnd()
        {
            // 曲が終わったら、磁石はその場で消える
            HideBars();
            phase = Phase.Done;
        }

        protected override void OnStateChanged(GameState state)
        {
            bool scoring = state == GameState.Playing || state == GameState.Grace;
            if (!scoring && Active)
            {
                HideBars();
                phase = Phase.Done;
            }
        }

        void SetPhase(Phase p, float wait = 0f)
        {
            phase = p;
            phaseTime = 0f;
            waitSeconds = wait;
        }

        void HideBars()
        {
            foreach (var b in bars) if (b != null) b.root.gameObject.SetActive(false);
            foreach (var lr in bolts) lr.enabled = false;
        }

        protected override void Tick(float dt, bool running)
        {
            shake = Mathf.MoveTowards(shake, 0f, dt * 3f);
            if (!running) return;
            phaseTime += dt;
            switch (phase)
            {
                case Phase.Wait:
                    if (phaseTime >= waitSeconds)
                    {
                        half = startHalf;
                        speed = 0f;
                        SetPhase(Phase.Charge);
                        foreach (var b in bars) b.root.gameObject.SetActive(true);
                        Play(chargeSound, ref madeCharge, () => RecordSynth.Sweep("MagnetCharge", 90f, 520f, 1.5f, 0.32f, RecordSynth.Wave.Square));
                    }
                    break;

                case Phase.Charge:
                    if (phaseTime >= chargeSeconds)
                    {
                        speed = D * startSpeed;
                        SetPhase(Phase.Rush);
                        Play(rushSound, ref madeRush, () => RecordSynth.Sweep("MagnetRush", 200f, 1100f, 0.6f, 0.3f, RecordSynth.Wave.Saw));
                    }
                    break;

                case Phase.Rush:
                {
                    // だんだん速くなりながら中央へ
                    speed = Mathf.Min(D * maxSpeed, speed + D * acceleration * dt);
                    stopHalf = PackedHalf();
                    half = Mathf.Max(stopHalf, half - speed * dt);
                    if (half <= stopHalf + 0.0001f)
                    {
                        // これ以上寄れない
                        SetPhase(Phase.Clash);
                        speed = 0f;   // 止まる（メダルは外へもどれないまま、ぎゅうぎゅう）
                        shake = 1f;
                        Shake(0.04f, clashSeconds + 0.2f);
                        Emit(sparks, center + Vector3.up * barH * 0.6f, PurpleHot, 30);
                        Play(clashSound, ref madeClash, () => RecordSynth.Thud("MagnetClash", 140f, 0.35f, 0.7f));
                    }
                    break;
                }

                case Phase.Clash:
                    shake = 1f;
                    if (phaseTime >= clashSeconds)
                    {
                        SetPhase(Phase.Vanish);
                        Emit(fieldDust, center + Vector3.up * barH * 0.5f, PurpleHot, 24);
                        Play(vanishSound, ref madeVanish, () => RecordSynth.Sweep("MagnetVanish", 600f, 120f, 0.5f, 0.25f, RecordSynth.Wave.Sine));
                    }
                    break;

                case Phase.Vanish:
                    shake = 0f;
                    if (phaseTime >= vanishSeconds)
                    {
                        HideBars();
                        if (repeat) SetPhase(Phase.Wait, cooldownSeconds);
                        else SetPhase(Phase.Done);
                    }
                    break;
            }
        }

        /// <summary>
        /// 間のメダルがぎゅうぎゅうになった時の、磁石のすき間の半分。
        /// 2つの磁石の間にあるメダルの面積を、磁石の長さで割って求める（メダルが多いほど、手前で止まる）
        /// </summary>
        float PackedHalf()
        {
            CollectLower();
            int n = 0;
            foreach (var m in lower)
            {
                Vector3 p = m.transform.position;
                if (Mathf.Abs(p.x) < half + D * 0.3f && Mathf.Abs(p.z - center.z) < barLen * 0.5f + D * 0.3f) n++;
            }
            float area = n * Mathf.PI * D * D * 0.25f;
            float width = area / Mathf.Max(D, barLen * packLayers);   // 何段か積み重なる分をみる
            return Mathf.Max(D * minGap * 0.5f, width * 0.5f);
        }

        // ================================================================
        // メダルを寄せる
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

        void FixedUpdate()
        {
            if (!Running || (phase != Phase.Rush && phase != Phase.Clash) || game == null || game.Pool == null) return;
            CollectLower();
            float zHalf = barLen * 0.5f + D * 0.8f;
            float face = half - D * 0.5f;   // メダルの中心がここより外なら、磁石に当たっている
            float push = speed > 0f ? Mathf.Max(speed * pushPower, D * minPushSpeed) : 0f;
            foreach (var m in lower)
            {
                Vector3 p = m.transform.position;
                if (Mathf.Abs(p.z - center.z) > zHalf) continue;
                float side = p.x < 0f ? -1f : 1f;   // 左の磁石の側なら -1
                float dist = Mathf.Abs(p.x);
                m.Body.WakeUp();
                // 磁石の通り道にいるメダルは、中央へ強く押す
                if (dist > face)
                {
                    // 磁石に追いこされたメダルは、磁石の内側へ移す（取り残さない）
                    if (sweepPassed && dist > half + D * 0.1f)
                    {
                        p.x = side * face;
                        m.Body.position = p;
                    }
                    var v = m.Body.velocity;
                    float inward = -side * push;
                    if (side < 0f ? v.x < inward : v.x > inward) v.x = inward;
                    m.Body.velocity = v;
                }
                // 間のメダルは、中央へ引き寄せる（磁力）
                m.Body.AddForce(new Vector3(-side, 0f, (center.z - p.z) * 0.3f) * pullForce, ForceMode.Acceleration);
            }
        }

        protected override bool KeepBoardVisible() =>
            (sparks != null && sparks.particleCount > 0) || (fieldDust != null && fieldDust.particleCount > 0);

        // ================================================================
        // 見た目
        // ================================================================

        protected override void UpdateBoard(float dt)
        {
            bool showBars = Active && (phase == Phase.Charge || phase == Phase.Rush || phase == Phase.Clash || phase == Phase.Vanish);
            for (int i = 0; i < 2; i++)
            {
                var b = bars[i];
                if (b.root.gameObject.activeSelf != showBars) b.root.gameObject.SetActive(showBars);
                if (!showBars) continue;
                float side = i == 0 ? -1f : 1f;
                // 出てくる時は床からせり上がる
                // 消える時は床に沈む
                float rise = phase == Phase.Charge ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseTime / 0.45f))
                           : phase == Phase.Vanish ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseTime / Mathf.Max(0.01f, vanishSeconds)))
                           : 1f;
                Vector3 jitter = shake > 0f ? Random.insideUnitSphere * D * 0.05f * shake : Vector3.zero;
                b.root.position = new Vector3(side * half, -barH * (1f - rise), center.z) + jitter;
                float g = phase == Phase.Charge ? 0.8f + 1.2f * Mathf.Abs(Mathf.Sin(Time.time * 20f))
                        : phase == Phase.Vanish ? 1.6f * rise
                        : 1.6f + 1.5f * shake;
                SetGlow(b.body, b.color, g);
                if (Random.value < dt * (phase == Phase.Charge ? 30f : 15f))
                    Emit(fieldDust, b.root.position + new Vector3(0f, barH * Random.Range(0.2f, 1f), Random.Range(-barLen, barLen) * 0.5f), Random.value < 0.5f ? Purple : PurpleHot, 1);
            }
            UpdateBolts(dt, showBars && phase != Phase.Vanish);
        }

        /// <summary>紫色のビリビリ：磁石の面を走る電気と、2つの磁石の間を飛ぶ電気</summary>
        void UpdateBolts(float dt, bool show)
        {
            boltTimer -= dt;
            bool regen = boltTimer <= 0f;
            if (regen) boltTimer = 0.04f;
            float power = phase == Phase.Charge ? Mathf.Clamp01(phaseTime / Mathf.Max(0.1f, chargeSeconds)) : 1f;
            int count = Mathf.RoundToInt(Mathf.Lerp(4f, bolts.Count, power));
            for (int i = 0; i < bolts.Count; i++)
            {
                var lr = bolts[i];
                bool use = show && i < count;
                if (lr.enabled != use) lr.enabled = use;
                if (!use || !regen) continue;
                Vector3 a, b;
                float zA = center.z + Random.Range(-barLen, barLen) * 0.48f;
                float zB = center.z + Random.Range(-barLen, barLen) * 0.48f;
                if (i % 3 == 2)
                {
                    // 磁石の面の上を走る電気
                    float s = i % 2 == 0 ? -1f : 1f;
                    a = new Vector3(s * half, barH * Random.Range(0.1f, 1f), zA);
                    b = new Vector3(s * half, barH * Random.Range(0.1f, 1f), zB);
                }
                else
                {
                    // 左の磁石から右の磁石へ
                    a = new Vector3(-half, barH * Random.Range(0.3f, 0.9f), zA);
                    b = new Vector3(half, barH * Random.Range(0.3f, 0.9f), zB);
                }
                float bend = Vector3.Distance(a, b) * 0.12f;
                for (int k = 0; k < lr.positionCount; k++)
                {
                    float t = k / (float)(lr.positionCount - 1);
                    Vector3 p = Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * bend;
                    if (k > 0 && k < lr.positionCount - 1) p += Random.insideUnitSphere * D * 0.18f;
                    lr.SetPosition(k, p);
                }
                lr.widthMultiplier = D * Random.Range(0.025f, 0.075f) * (0.6f + 0.4f * power);
            }
            if (show) RecordMeshKit.SetColor(boltMaterial, Color.Lerp(Purple, PurpleHot, Random.value));
        }
    }
}
