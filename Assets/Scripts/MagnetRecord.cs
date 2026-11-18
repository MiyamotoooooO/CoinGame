using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N012 磁石「N極・S極」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：中央の穴の左右の奥に、赤（N）と青（S）のU字磁石がせり上がる。ON の磁石は光り、近くのメダルが細かく震えながら穴の方へ寄る
    ///   超磁力がたまると「スーパーマグネット」：巨大なU字磁石が空から下りてきて、メダルの山から最大 grabMax 枚を吸い上げ、
    ///   空中でくっつけたまま中央の穴の上まで運び、磁力を切ってまとめて落とす
    /// ・モニター：左右の磁石の ON/OFF と磁力線、入れかわりまでの時間、超磁力のメーター
    /// ・ルール：
    ///   switchSeconds 秒ごとに、ON の磁石が左右で入れかわる。ON の磁石は、近くのメダルを中央の穴の方へ引き寄せる
    ///   ON の側の半分に落ちると「くっついた」：+stickBonus pt、超磁力 +1
    ///   超磁力が chargeToSuper たまるとスーパーマグネット。運んで落としたメダルが中央の穴に入ると、1枚 +superDropBonus pt
    /// ・ON の磁石の側へ投入して、寄せてもらう遊び。スーパーマグネットはメダルの山をねらって吸い上げる
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class MagnetRecord : RecordScriptBase
    {
        public static MagnetRecord Instance { get; private set; }

        [Header("磁石")]
        [Range(1f, 30f)] public float switchSeconds = 6f;
        [Tooltip("引き寄せる半径（メダルの直径に対する倍率）")]
        [Range(0.5f, 6f)] public float pullRadius = 2.5f;
        [Tooltip("引き寄せる強さ")]
        [Range(0f, 30f)] public float pullForce = 5f;

        [Header("ポイント")]
        [Range(0, 100)] public int stickBonus = 5;

        [Header("スーパーマグネット（超磁力）")]
        [Tooltip("ON の側に何回落とすとスーパーマグネットが来るか")]
        [Range(1, 30)] public int chargeToSuper = 10;
        [Tooltip("1回で吸い上げるメダルの最大の枚数")]
        [Range(1, 40)] public int grabMax = 12;
        [Tooltip("吸い上げる範囲の半径（メダルの直径に対する倍率）")]
        [Range(0.5f, 6f)] public float grabRadius = 2.4f;
        [Tooltip("メダルをくっつけて運ぶ高さ（メダルの直径に対する倍率）")]
        [Range(0.5f, 6f)] public float carryHeight = 1.8f;
        [Tooltip("運んで落としたメダルが中央の穴に入った時のボーナス（1枚）")]
        [Range(0, 100)] public int superDropBonus = 10;
        [Range(0.2f, 3f)] public float descendSeconds = 0.9f;
        [Range(0.2f, 5f)] public float grabSeconds = 1.4f;
        [Range(0.2f, 5f)] public float carrySeconds = 1.1f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip stickSound;
        public AudioClip switchSound;
        public AudioClip superSound;
        public AudioClip dropSound;

        protected override string DefaultKey => "Magnet";
        protected override string ScreenTitle => "MAGNET";
        protected override string ScreenSubtitle => "N極・S極";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<MagnetRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color NRed = new Color32(0xff, 0x2e, 0x3a, 0xff);
        static readonly Color SBlue = new Color32(0x2e, 0x6c, 0xff, 0xff);
        static readonly Color Steel = new Color32(0xd8, 0xdc, 0xe4, 0xff);
        static readonly Color Super = new Color32(0xc0, 0x6c, 0xff, 0xff);
        static readonly Color Spark = new Color32(0xbf, 0xe4, 0xff, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int onSide, charge;
        float switchTimer, stickPulse;
        readonly List<Medal> near = new List<Medal>();

        // ---- スーパーマグネット ----
        enum Phase { None, Descend, Grab, Carry, Drop, Leave }
        Phase phase;
        float phaseT;
        Vector3 giantPos, grabCenter, carryFrom, carryTo, leaveFrom;
        class Held { public Medal m; public Vector3 offset; public bool gravity; }
        readonly List<Held> held = new List<Held>();
        readonly HashSet<Medal> dropped = new HashSet<Medal>();
        float droppedTimer;
        bool SuperOn => phase != Phase.None;

        // ---- 盤面 ----
        class MagnetModel
        {
            public Transform root;
            public Material red, blue, band;
            public Vector3 tipL, tipR;   // 先っぽ（root の中の位置）
            public float height;
        }
        readonly MagnetModel[] magnets = new MagnetModel[2];
        MagnetModel giant;
        ParticleSystem sparks, fieldDust;
        readonly List<LineRenderer> bolts = new List<LineRenderer>();
        Material boltMaterial;
        float boltTimer;
        Light giantLight;

        // ---- モニター ----
        readonly Image[] boxes = new Image[2];
        readonly Text[] labels = new Text[2];
        readonly List<Image> fieldLines = new List<Image>();
        Image chargeFill;
        Text timerText, statusText;
        AudioClip madeStick, madeSwitch, madeSuper, madeDrop;

        Vector3 MagnetPos(int side) => new Vector3(side * HoleHalf * 0.5f, 0f, FrontZ + D * 1.6f);
        Vector3 PullTarget(int side) => new Vector3(side * HoleHalf * 0.5f, 0f, FrontZ - D * 0.2f);

        static Material Shiny(Color c, float metal, float smooth, float glow)
        {
            var m = ProtoAssets.MakeMaterial(c, metal, smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            return m;
        }

        /// <summary>
        /// U字磁石を組み立てる（脚が下向き。原点は先っぽの下のまん中）。s は全体の高さのめやす。
        /// 右の脚が赤（N）、左の脚が青（S）。脚の先は銀色
        /// </summary>
        MagnetModel BuildMagnet(string name, Transform parent, float s)
        {
            var mm = new MagnetModel();
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            mm.root = root;
            mm.red = Shiny(NRed, 0.25f, 0.85f, 0.4f);
            mm.blue = Shiny(SBlue, 0.25f, 0.85f, 0.4f);
            mm.band = Shiny(Color.white, 0f, 0.6f, 0.6f);
            var steel = Shiny(Steel, 0.95f, 0.92f, 0.15f);
            float R = s * 0.3f, T = s * 0.11f, tip = s * 0.14f, leg = s * 0.45f;
            float arcY = tip + leg;
            // 上の曲がった所（右半分が赤、左半分が青）
            RecordMeshKit.Make("ArcN", RecordMeshKit.Torus(R, T, 0f, 90f, 24, 16), mm.red, root, new Vector3(0f, arcY, 0f), Vector3.one, Vector3.zero);
            RecordMeshKit.Make("ArcS", RecordMeshKit.Torus(R, T, 90f, 90f, 24, 16), mm.blue, root, new Vector3(0f, arcY, 0f), Vector3.one, Vector3.zero);
            for (int k = 0; k < 2; k++)
            {
                float x = k == 0 ? R : -R;
                var mat = k == 0 ? mm.red : mm.blue;
                Shape(PrimitiveType.Cylinder, k == 0 ? "LegN" : "LegS", root, new Vector3(x, tip + leg * 0.5f, 0f), new Vector3(T * 2f, leg * 0.5f, T * 2f), mat);
                Shape(PrimitiveType.Cylinder, "Band", root, new Vector3(x, tip + T * 0.15f, 0f), new Vector3(T * 2.04f, T * 0.15f, T * 2.04f), mm.band);
                Shape(PrimitiveType.Cylinder, "Tip", root, new Vector3(x, tip * 0.5f, 0f), new Vector3(T * 2.02f, tip * 0.5f, T * 2.02f), steel);
                AddLetter(root, k == 0 ? "N" : "S", new Vector3(x, tip + leg * 0.55f, -T * 1.02f), T * 1.5f);
            }
            mm.tipL = new Vector3(-R, 0f, 0f);
            mm.tipR = new Vector3(R, 0f, 0f);
            mm.height = arcY + R + T;
            return mm;
        }

        static Font letterFont;

        void AddLetter(Transform parent, string text, Vector3 pos, float size)
        {
            if (letterFont == null)
            {
                try { letterFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { letterFont = null; }
            }
            if (letterFont == null) return;
            var go = new GameObject("Letter" + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
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

        protected override void BuildBoard()
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                magnets[i] = BuildMagnet(side < 0 ? "MagnetLeft" : "MagnetRight", Board, D * 1.15f);
                magnets[i].root.position = MagnetPos(side);
            }
            giant = BuildMagnet("SuperMagnet", Board, D * 3.4f);
            giant.root.gameObject.SetActive(false);
            var lgo = new GameObject("SuperLight");
            lgo.transform.SetParent(giant.root, false);
            lgo.transform.localPosition = new Vector3(0f, -D * 0.5f, 0f);
            giantLight = lgo.AddComponent<Light>();
            giantLight.type = LightType.Point;
            giantLight.shadows = LightShadows.None;
            giantLight.range = D * 8f;
            giantLight.color = Spark;
            giantLight.intensity = 0f;

            boltMaterial = RecordMeshKit.Additive(Texture2D.whiteTexture, Spark);
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject("Bolt" + i);
                go.transform.SetParent(Board, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 8;
                lr.widthMultiplier = D * 0.05f;
                lr.sharedMaterial = boltMaterial;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 2;
                lr.enabled = false;
                bolts.Add(lr);
            }
            sparks = Particles("Sparks", Board, Vector3.up, D * 3f, D * 0.07f, 0.6f, 0.4f, 70f);
            fieldDust = Particles("Sparkle", Board, Vector3.up, D * 0.8f, D * 0.05f, 0.8f, 0f, 90f);
        }

        protected override void OnBegin()
        {
            onSide = Random.value < 0.5f ? -1 : 1;
            switchTimer = switchSeconds;
            charge = 0;
            phase = Phase.None;
            dropped.Clear();
        }

        protected override void OnEnd()
        {
            ReleaseAll(false);
            phase = Phase.None;
            dropped.Clear();
        }

        protected override void OnStateChanged(GameState state)
        {
            if (state != GameState.Playing && state != GameState.Grace && held.Count > 0)
            {
                ReleaseAll(false);
                if (phase != Phase.None) StartLeave();
            }
        }

        bool IsOn(int side) => SuperOn || side == onSide;

        protected override void Tick(float dt, bool running)
        {
            stickPulse = Mathf.MoveTowards(stickPulse, 0f, dt * 3f);
            if (droppedTimer > 0f)
            {
                droppedTimer -= dt;
                if (droppedTimer <= 0f) dropped.Clear();
            }
            if (phase != Phase.None)
            {
                if (!running && phase != Phase.Leave)
                {
                    ReleaseAll(false);
                    StartLeave();
                }
                AdvanceSuper(dt);
                return;
            }
            if (!running) return;
            switchTimer -= dt;
            if (switchTimer <= 0f)
            {
                switchTimer = switchSeconds;
                onSide = -onSide;
                Play(switchSound, ref madeSwitch, () => RecordSynth.Sweep("MagnetSwitch", 120f, 240f, 0.3f, 0.3f, RecordSynth.Wave.Square), 0.6f);
            }
        }

        // ================================================================
        // スーパーマグネット
        // ================================================================

        Vector3 SkyPos(Vector3 at) => new Vector3(at.x, Layout.wallHeight * 2.2f + D * 6f, at.z);
        Vector3 HoverPos(Vector3 at) => new Vector3(at.x, D * carryHeight + D * 0.25f, at.z);

        /// <summary>メダルが一番たくさん集まっている場所をさがす</summary>
        Vector3 FindPile()
        {
            Vector3 best = RandomFloorPoint(0.3f, 0.7f, 0.7f);
            int bestCount = -1;
            float r = D * grabRadius;
            for (int i = 0; i < 14; i++)
            {
                var p = RandomFloorPoint(0.2f, 0.85f, 0.75f);
                int c = MedalsNear(p, r, near).Count;
                if (c > bestCount) { bestCount = c; best = p; }
            }
            best.y = 0f;
            return best;
        }

        void StartSuper()
        {
            grabCenter = FindPile();
            giantPos = SkyPos(grabCenter);
            phase = Phase.Descend;
            phaseT = 0f;
            Play(superSound, ref madeSuper, () => RecordSynth.Sweep("MagnetSuper", 80f, 640f, 1f, 0.35f, RecordSynth.Wave.Saw));
            PopCenter("スーパーマグネット！", Super);
            Speaker(0.9f, 12, Super);
        }

        void AdvanceSuper(float dt)
        {
            phaseT += dt;
            switch (phase)
            {
                case Phase.Descend:
                {
                    float k = Mathf.Clamp01(phaseT / Mathf.Max(0.05f, descendSeconds));
                    float e = 1f - Mathf.Pow(1f - k, 3f);
                    giantPos = Vector3.Lerp(SkyPos(grabCenter), HoverPos(grabCenter), e);
                    if (k >= 1f)
                    {
                        Grab();
                        phase = Phase.Grab;
                        phaseT = 0f;
                    }
                    break;
                }
                case Phase.Grab:
                    giantPos = HoverPos(grabCenter) + new Vector3(0f, Mathf.Sin(phaseT * 30f) * D * 0.03f, 0f);
                    if (phaseT >= grabSeconds)
                    {
                        phase = Phase.Carry;
                        phaseT = 0f;
                        carryFrom = giantPos;
                        carryTo = new Vector3(0f, D * carryHeight + D * 0.6f, FrontZ + D * 0.35f);
                    }
                    break;
                case Phase.Carry:
                {
                    float k = Mathf.Clamp01(phaseT / Mathf.Max(0.05f, carrySeconds));
                    float e = k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) * 0.5f;
                    giantPos = Vector3.Lerp(carryFrom, carryTo, e) + Vector3.up * Mathf.Sin(k * Mathf.PI) * D * 0.8f;
                    if (k >= 1f)
                    {
                        DropAll();
                        phase = Phase.Drop;
                        phaseT = 0f;
                    }
                    break;
                }
                case Phase.Drop:
                    if (phaseT >= 0.45f) StartLeave();
                    break;
                case Phase.Leave:
                {
                    float k = Mathf.Clamp01(phaseT / 0.9f);
                    giantPos = Vector3.Lerp(leaveFrom, SkyPos(leaveFrom), k * k);
                    if (k >= 1f) phase = Phase.None;
                    break;
                }
            }
        }

        void StartLeave()
        {
            leaveFrom = giantPos;
            phase = Phase.Leave;
            phaseT = 0f;
        }

        void Grab()
        {
            held.Clear();
            MedalsNear(grabCenter, D * grabRadius, near);
            near.Sort((a, b) => (a.transform.position - grabCenter).sqrMagnitude.CompareTo((b.transform.position - grabCenter).sqrMagnitude));
            int n = Mathf.Min(grabMax, near.Count);
            for (int i = 0; i < n; i++)
            {
                var m = near[i];
                if (m == null || m.Body == null || m.Consumed) continue;
                int tip = i % 2;
                int layer = i / 2;
                Vector3 tipPos = tip == 0 ? giant.tipL : giant.tipR;
                var offset = tipPos + new Vector3(Random.Range(-D * 0.25f, D * 0.25f), -D * 0.12f - layer * D * 0.13f, Random.Range(-D * 0.25f, D * 0.25f));
                held.Add(new Held { m = m, offset = offset, gravity = m.Body.useGravity });
                m.Body.useGravity = false;
                m.Body.WakeUp();
                Push(m, Vector3.up * D * 3f);
                Emit(sparks, m.transform.position, Spark, 4);
            }
            Emit(sparks, grabCenter + Vector3.up * D * 0.2f, Spark, 40);
            Shake(0.05f, 0.5f);
            Play(stickSound, ref madeStick, () => RecordSynth.Notes("MagnetStick", new[] { 1568f }, 0.03f, 0.08f, 0.2f, RecordSynth.Wave.Square), 1f);
            if (held.Count > 0) Pop($"{held.Count}枚 くっついた！", Super, new Vector2(BodyW * 0.5f, 300f));
        }

        void DropAll()
        {
            int n = 0;
            foreach (var h in held)
            {
                if (h.m == null || h.m.Body == null || h.m.Consumed || !h.m.IsActiveInPool) continue;
                h.m.Body.useGravity = h.gravity;
                h.m.Body.velocity = new Vector3(Random.Range(-0.4f, 0.4f) * D, -D * 1.5f, -D * 3.2f);
                dropped.Add(h.m);
                n++;
            }
            held.Clear();
            droppedTimer = 4f;
            Emit(sparks, giantPos + Vector3.down * D * 0.3f, Super, 60);
            Emit(fieldDust, giantPos + Vector3.down * D * 0.3f, Spark, 40);
            Play(dropSound, ref madeDrop, () => RecordSynth.Thud("MagnetDrop", 70f, 0.6f, 0.9f));
            HypeBurst(Super, 1.2f);
            Pop(n > 0 ? $"MAGNET DROP!  {n}枚" : "MAGNET DROP!", Super, new Vector2(BodyW * 0.5f, 300f));
            Speaker(1f, 16, Super);
        }

        /// <summary>くっつけているメダルを全部はなす（重力を元に戻す）</summary>
        void ReleaseAll(bool toHole)
        {
            foreach (var h in held)
            {
                if (h.m == null || h.m.Body == null) continue;
                h.m.Body.useGravity = h.gravity;
                if (toHole) dropped.Add(h.m);
            }
            held.Clear();
        }

        // ================================================================
        // 物理
        // ================================================================

        void FixedUpdate()
        {
            if (!Active || game == null) return;

            // くっつけて運んでいるメダル
            if (held.Count > 0 && giant != null)
            {
                for (int i = held.Count - 1; i >= 0; i--)
                {
                    var h = held[i];
                    if (h.m == null || h.m.Body == null || h.m.Consumed || !h.m.IsActiveInPool)
                    {
                        if (h.m != null && h.m.Body != null) h.m.Body.useGravity = h.gravity;
                        held.RemoveAt(i);
                        continue;
                    }
                    Vector3 target = giantPos + h.offset + new Vector3(Mathf.Sin(Time.time * 37f + i) * D * 0.02f, 0f, 0f);
                    Vector3 v = (target - h.m.Body.position) * 9f;
                    v = Vector3.ClampMagnitude(v, D * 14f);
                    h.m.Body.velocity = v;
                    h.m.Body.angularVelocity *= 0.85f;
                }
            }

            if (!Running || SuperOn) return;
            float r = D * pullRadius;
            for (int s = -1; s <= 1; s += 2)
            {
                if (!IsOn(s)) continue;
                Vector3 target = PullTarget(s);
                MedalsNear(MagnetPos(s), r, near);
                foreach (var m in near)
                {
                    if (m.Body == null) continue;
                    Vector3 d = target - m.transform.position;
                    d.y = 0f;
                    float k = 1f - Mathf.Clamp01(Vector3.Distance(m.transform.position, MagnetPos(s)) / r);
                    m.Body.WakeUp();
                    // 引き寄せる力と、細かい震え
                    Vector3 jitter = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * 2f;
                    m.Body.AddForce((d.normalized * pullForce + jitter) * (0.3f + k), ForceMode.Acceleration);
                }
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            if (dropped.Remove(medal))
            {
                AddPoints(superDropBonus);
                Pop($"MAGNET +{superDropBonus}", Super, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 380f));
                return;
            }
            int side = medal.transform.position.x < 0f ? -1 : 1;
            if (!IsOn(side) || SuperOn) return;
            AddPoints(stickBonus);
            stickPulse = 1f;
            Emit(sparks, MagnetPos(side) + Vector3.up * D * 0.1f, side < 0 ? NRed : SBlue, 14);
            Play(stickSound, ref madeStick, () => RecordSynth.Notes("MagnetStick", new[] { 1568f }, 0.03f, 0.08f, 0.2f, RecordSynth.Wave.Square), 0.6f);
            Pop($"くっついた +{stickBonus}", side < 0 ? NRed : SBlue, new Vector2(side < 0 ? BodyW * 0.3f : BodyW * 0.7f, 300f));
            charge++;
            if (charge >= chargeToSuper)
            {
                charge = 0;
                StartSuper();
            }
        }

        protected override bool KeepBoardVisible() => (sparks != null && sparks.particleCount > 0) || SuperOn;

        // ================================================================
        // 盤面の見た目
        // ================================================================

        protected override void UpdateBoard(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                bool on = Active && IsOn(side);
                Vector3 p = MagnetPos(side);
                var mm = magnets[i];
                mm.root.position = new Vector3(p.x, -mm.height * (1f - BoardShown) + (on ? Mathf.Sin(Time.time * 40f) * D * 0.01f : 0f), p.z);
                float g = on ? 1.6f + stickPulse * 2f + (SuperOn ? 1.5f : 0f) : 0.25f;
                SetGlow(mm.red, NRed, g);
                SetGlow(mm.blue, SBlue, g);
                SetGlow(mm.band, SuperOn ? Super : Color.white, on ? 1.2f : 0.3f);
                if (on && Random.value < dt * 6f)
                    Emit(fieldDust, mm.root.position + new Vector3(Random.Range(-D * 0.4f, D * 0.4f), D * 0.05f, 0f), side < 0 ? NRed : SBlue, 1);
            }

            bool showGiant = phase != Phase.None;
            if (giant.root.gameObject.activeSelf != showGiant) giant.root.gameObject.SetActive(showGiant);
            if (showGiant)
            {
                giant.root.position = giantPos;
                giant.root.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 2.1f) * 4f, Mathf.Sin(Time.time * 1.3f) * 10f, Mathf.Sin(Time.time * 2.7f) * 3f);
                bool charged = phase == Phase.Grab || phase == Phase.Carry;
                float g = charged ? 2.5f + Mathf.Abs(Mathf.Sin(Time.time * 18f)) * 2f : 1.2f;
                SetGlow(giant.red, NRed, g);
                SetGlow(giant.blue, SBlue, g);
                SetGlow(giant.band, Super, g);
                giantLight.intensity = charged ? 3f + Random.value * 2f : 1f;
                if (charged && Random.value < 0.5f)
                {
                    Vector3 tip = giant.root.TransformPoint(Random.value < 0.5f ? giant.tipL : giant.tipR);
                    Emit(sparks, tip, Spark, 2);
                }
            }
            UpdateBolts(dt, showGiant);
        }

        void UpdateBolts(float dt, bool showGiant)
        {
            bool charged = showGiant && (phase == Phase.Grab || phase == Phase.Carry || (phase == Phase.Descend && phaseT > descendSeconds * 0.6f));
            boltTimer -= dt;
            bool regen = boltTimer <= 0f;
            if (regen) boltTimer = 0.05f;
            for (int i = 0; i < bolts.Count; i++)
            {
                var lr = bolts[i];
                bool use = charged && (i < 2 || i - 2 < held.Count || phase == Phase.Descend);
                if (lr.enabled != use) lr.enabled = use;
                if (!use || !regen) continue;
                Vector3 a = giant.root.TransformPoint(i % 2 == 0 ? giant.tipL : giant.tipR);
                Vector3 b;
                if (i < 2) b = giant.root.TransformPoint(i % 2 == 0 ? giant.tipR : giant.tipL);
                else if (i - 2 < held.Count && held[i - 2].m != null) b = held[i - 2].m.transform.position;
                else b = grabCenter + new Vector3(Random.Range(-D, D), 0f, Random.Range(-D, D));
                for (int k = 0; k < lr.positionCount; k++)
                {
                    float t = k / (float)(lr.positionCount - 1);
                    Vector3 p = Vector3.Lerp(a, b, t);
                    if (i < 2) p += Vector3.down * Mathf.Sin(t * Mathf.PI) * D * 0.5f;
                    if (k > 0 && k < lr.positionCount - 1) p += Random.insideUnitSphere * D * 0.12f;
                    lr.SetPosition(k, p);
                }
                lr.widthMultiplier = D * Random.Range(0.025f, 0.07f);
            }
            RecordMeshKit.SetColor(boltMaterial, Color.Lerp(Spark, Color.white, Random.value * 0.6f));
        }

        // ================================================================
        // モニター
        // ================================================================

        protected override void BuildScreen()
        {
            fieldLines.Clear();
            float W = BodyW;
            var panel = Panel(Screen, "Field", W * 0.5f, 200f, W - 48f, 340f, new Color32(0x12, 0x14, 0x1c, 0xff));
            float pw = W - 48f;
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                float cx = side < 0 ? pw * 0.25f : pw * 0.75f;
                for (int k = 0; k < 4; k++)
                    fieldLines.Add(Img(panel, $"Line{i}_{k}", cx, 170f, 120f + k * 70f, 80f + k * 50f, new Color(1f, 1f, 1f, 0.1f), MonitorDisplay.ScreenShape.Ring));
                boxes[i] = Img(panel, $"Mag{i}", cx, 170f, 110f, 110f, side < 0 ? NRed : SBlue, MonitorDisplay.ScreenShape.Circle);
                labels[i] = Lbl(panel, $"Label{i}", cx, 170f, 110f, 110f, side < 0 ? "N" : "S", 64, Ink, TextAnchor.MiddleCenter, true);
            }
            timerText = Lbl(panel, "Timer", pw * 0.5f, 310f, pw, 40f, "", 22, Ink);
            Lbl(Screen, "ChargeLabel", 24f, 450f, 140f, 30f, "超磁力", 20, Ink, TextAnchor.MiddleLeft);
            chargeFill = Bar(Screen, "Charge", 140f, 450f, W - 200f, 20f, Super);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                bool on = IsOn(side);
                Color c = SuperOn ? Super : side < 0 ? NRed : SBlue;
                boxes[i].color = on ? Color.Lerp(c, Color.white, stickPulse * 0.4f) : new Color(c.r, c.g, c.b, 0.25f);
                Scale(boxes[i].rectTransform, on ? 1f + (SuperOn ? 0.15f : 0.05f) * Mathf.Sin(Time.time * (SuperOn ? 24f : 12f)) : 0.85f);
                for (int k = 0; k < 4; k++)
                {
                    var line = fieldLines[i * 4 + k];
                    float a = on ? 0.15f + 0.2f * Mathf.Repeat(Time.time * (SuperOn ? 4f : 1.5f) - k * 0.25f, 1f) : 0.05f;
                    line.color = new Color(c.r, c.g, c.b, a);
                }
            }
            timerText.text = SuperOn ? $"スーパーマグネット！  {held.Count}枚 くっついている"
                                     : $"ON：{(onSide < 0 ? "左（N）" : "右（S）")}   入れかわりまで {Mathf.CeilToInt(switchTimer)}秒";
            chargeFill.fillAmount = SuperOn ? 1f : charge / (float)Mathf.Max(1, chargeToSuper);
            statusText.text = SuperOn
                ? $"<color=#C06CFF>スーパーマグネット！</color>  吸い上げたメダルを中央の穴に落とす（1枚 +{superDropBonus}pt）"
                : $"ON の磁石の側の半分に落とすと +{stickBonus}pt。{chargeToSuper}回でスーパーマグネット";
        }
    }
}
