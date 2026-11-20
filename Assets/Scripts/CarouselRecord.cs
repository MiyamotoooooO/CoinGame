using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N015 メリーゴーランド「回転木馬のファンファーレ」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：下段（プッシャーの前の盤面）のまん中に巨大なメリーゴーランド、その左右に小さいメリーゴーランドがせり上がって回る
    ///   （しま模様のとんがり屋根・金の柱・電球・上下する木馬）
    /// ・下段のメダルは、近いメリーゴーランドへ少しずつ寄っていく。寄ってきたメダルは、空いている木馬の背中に1枚ずつ乗る
    ///   （乗ったメダルは、木馬といっしょに回って上下する）
    /// ・効果：boostInterval 秒ごとに boostSeconds 秒「ファンファーレ」。メリーゴーランドが高速で回り、
    ///   木馬に乗っているメダルが、中央の穴をめがけて吹き飛んでいく（回る勢いで、穴の方を向いた時に飛び出す）。
    ///   高速の間も、空いた木馬には近くのメダルがどんどん乗って、また飛んでいく
    /// ・メリーゴーランドは見た目だけ（当たり判定なし）。乗っている間のメダルは、物理を止めて木馬について動く
    /// ・モニター：使わない（いつものラウンドの画面のまま）
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class CarouselRecord : RecordScriptBase
    {
        public static CarouselRecord Instance { get; private set; }

        [Header("大きさ")]
        [Tooltip("まん中の巨大なメリーゴーランドの半径（下段の奥行きの半分に対する割合）")]
        [Range(0.3f, 1.2f)] public float bigSize = 0.95f;
        [Tooltip("左右の小さいメリーゴーランドの半径（まん中に対する割合）")]
        [Range(0.2f, 1f)] public float smallSize = 0.55f;
        [Tooltip("まん中のメリーゴーランドの木馬の数")]
        [Range(3, 16)] public int bigHorses = 8;
        [Tooltip("小さいメリーゴーランドの木馬の数")]
        [Range(2, 12)] public int smallHorses = 5;

        [Header("回る")]
        [Tooltip("ふだんの回る速さ（1秒あたりの角度）")]
        [Range(0f, 360f)] public float spinSpeed = 35f;
        [Tooltip("ファンファーレ（高速）の時の回る速さ")]
        [Range(0f, 1440f)] public float boostSpin = 480f;

        [Header("メダルを寄せて、乗せる")]
        [Tooltip("下段のメダルを、メリーゴーランドへ寄せる強さ")]
        [Range(0f, 30f)] public float attractForce = 5f;
        [Tooltip("1つのメリーゴーランドが、メダルを1枚乗せる間隔（秒）")]
        [Range(0.05f, 3f)] public float boardInterval = 0.35f;
        [Tooltip("メリーゴーランドのふちから、ここまで近づいたメダルを乗せる（メダルの直径に対する倍率）")]
        [Range(0.2f, 4f)] public float boardRange = 1.1f;
        [Tooltip("ファンファーレ（高速）の間に、空いた木馬へメダルを1枚乗せる間隔（秒）。飛ばしたそばから、すぐ次のメダルが乗る")]
        [Range(0.02f, 1f)] public float boostBoardInterval = 0.1f;

        [Header("効果（ファンファーレ）")]
        [Tooltip("ファンファーレの間隔（秒）")]
        [Range(2f, 60f)] public float boostInterval = 10f;
        [Tooltip("ファンファーレ（高速）の時間（秒）")]
        [Range(0.5f, 10f)] public float boostSeconds = 3f;
        [Tooltip("メダルが中央の穴に届くまでの時間（短いほど速く、まっすぐ飛ぶ）")]
        [Range(0.2f, 2f)] public float flySeconds = 0.55f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip fanfareSound;
        public AudioClip boardSound;
        public AudioClip launchSound;

        protected override string DefaultKey => "Carousel";
        protected override string ScreenTitle => "CAROUSEL";
        protected override string ScreenSubtitle => "回転木馬のファンファーレ";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;   // モニターはいつもの画面のまま

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<CarouselRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed()
        {
            ReleaseAll(false);
            if (Instance == this) Instance = null;
        }

        static readonly Color Pink = new Color32(0xff, 0x8a, 0xc8, 0xff);
        static readonly Color Mint = new Color32(0x8a, 0xf0, 0xd0, 0xff);
        static readonly Color Cream = new Color32(0xff, 0xf0, 0xd0, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color Lilac = new Color32(0xb8, 0x9a, 0xff, 0xff);

        // ---- メリーゴーランド ----
        class Horse
        {
            public Transform t;
            public float s;           // 木馬の大きさ
            public float baseY, phase;
            public Medal rider;
            public float boardT;      // 乗る途中（0〜1）。1 で乗った
            public Vector3 boardFrom;
            public bool kinematicWas, collideWas;
            public RigidbodyInterpolation interpWas;
        }

        class Wheel
        {
            public Transform root, spin, column;
            public Vector3 center;
            public float r, H, angle, boardTimer;
            public readonly List<Horse> horses = new List<Horse>();
            public readonly List<Light> lights = new List<Light>();
        }

        readonly List<Wheel> wheels = new List<Wheel>();
        Material bulbA, bulbB;
        ParticleSystem confetti, sparkle;
        float cz, holeZ;

        // ---- 流れ ----
        float boostTimer, nextBoost;
        bool Boost => boostTimer > 0f;
        float lastLaunchSound, lastBoardSound;
        readonly List<Medal> lower = new List<Medal>();
        readonly HashSet<Medal> riding = new HashSet<Medal>();
        AudioClip madeFanfare, madeBoard, madeLaunch;

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
            cz = (FrontZ + PusherFrontZ) * 0.5f;
            holeZ = FrontZ;

            // 大きさと場所（3つが下段に入りきるように）
            float rb = Mathf.Min(depth * 0.5f * bigSize, HalfW * 0.45f);
            float rs = rb * smallSize;
            float gap = D * 0.25f;
            float xs = rb + gap + rs;
            float over = xs + rs - (HalfW - D * 0.1f);
            if (over > 0f)
            {
                // はみ出す時は、3つとも少し小さくする
                float k = (HalfW - D * 0.1f - gap) / Mathf.Max(0.01f, rb + 2f * rs);
                rb *= k; rs *= k;
                xs = rb + gap + rs;
            }

            bulbA = Shiny(Gold, 0f, 0.8f, 2.5f);
            bulbB = Shiny(Color.white, 0f, 0.8f, 2.5f);
            wheels.Add(BuildWheel("CarouselBig", new Vector3(0f, 0f, cz), rb, rb * 1.15f + D * 0.3f, bigHorses, 2));
            wheels.Add(BuildWheel("CarouselLeft", new Vector3(-xs, 0f, cz), rs, rs * 1.3f + D * 0.3f, smallHorses, 1));
            wheels.Add(BuildWheel("CarouselRight", new Vector3(xs, 0f, cz), rs, rs * 1.3f + D * 0.3f, smallHorses, 1));

            confetti = Particles("Confetti", Board, Vector3.up, D * 4f, D * 0.12f, 1.8f, 0.4f, 40f);
            sparkle = Particles("Sparkle", Board, Vector3.up, D * 1.5f, D * 0.07f, 0.9f, -0.1f, 80f);
        }

        /// <summary>メリーゴーランドを1つ組み立てる（r は台の半径、H は屋根の下までの高さ）</summary>
        Wheel BuildWheel(string name, Vector3 center, float r, float H, int horseCount, int lightCount)
        {
            var w = new Wheel { center = center, r = r, H = H };
            w.root = new GameObject(name).transform;
            w.root.SetParent(Board, false);
            w.root.position = center;
            w.spin = new GameObject("Spin").transform;
            w.spin.SetParent(w.root, false);
            var p = w.spin;

            var cream = Shiny(Cream, 0.1f, 0.7f, 0.15f);
            var pink = Shiny(Pink, 0.1f, 0.75f, 0.35f);
            var gold = Shiny(Gold, 0.95f, 0.9f, 0.35f);
            var mirror = Shiny(new Color(0.8f, 0.92f, 1f), 1f, 0.97f, 0.5f);
            var deep = Shiny(new Color32(0x6a, 0x1c, 0x4a, 0xff), 0.2f, 0.7f, 0.1f);

            // 台（クリーム色の床と、金のふち、ふちの電球）
            Shape(PrimitiveType.Cylinder, "Deck", p, new Vector3(0f, D * 0.05f, 0f), new Vector3(r * 2f, D * 0.05f, r * 2f), cream);
            Shape(PrimitiveType.Cylinder, "DeckRim", p, new Vector3(0f, D * 0.03f, 0f), new Vector3(r * 2.06f, D * 0.04f, r * 2.06f), gold);
            Shape(PrimitiveType.Cylinder, "DeckInlay", p, new Vector3(0f, D * 0.101f, 0f), new Vector3(r * 1.5f, D * 0.002f, r * 1.5f), deep);
            int rim = Mathf.Max(12, Mathf.RoundToInt(r / D * 12f));
            for (int i = 0; i < rim; i++)
            {
                float a = i * Mathf.PI * 2f / rim;
                Shape(PrimitiveType.Sphere, "RimBulb", p, new Vector3(Mathf.Cos(a) * r * 1.01f, D * 0.08f, Mathf.Sin(a) * r * 1.01f), Vector3.one * D * 0.09f, i % 2 == 0 ? bulbA : bulbB);
            }

            // まん中の柱（鏡の板つき）
            w.column = new GameObject("Column").transform;
            w.column.SetParent(p, false);
            Shape(PrimitiveType.Cylinder, "ColumnCore", w.column, new Vector3(0f, H * 0.5f, 0f), new Vector3(r * 0.34f, H * 0.5f, r * 0.34f), deep);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var panel = Cube("Mirror", w.column, Quaternion.Euler(0f, a, 0f) * new Vector3(0f, H * 0.5f, r * 0.175f), new Vector3(r * 0.12f, H * 0.62f, D * 0.02f), mirror);
                panel.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            }
            Shape(PrimitiveType.Cylinder, "ColumnTopRing", w.column, new Vector3(0f, H * 0.86f, 0f), new Vector3(r * 0.4f, D * 0.05f, r * 0.4f), gold);
            Shape(PrimitiveType.Cylinder, "ColumnLowRing", w.column, new Vector3(0f, H * 0.14f, 0f), new Vector3(r * 0.4f, D * 0.05f, r * 0.4f), gold);

            // とんがり屋根（ピンクとクリームのしま）
            int seg = 14;
            float roofH = r * 0.5f + D * 0.2f, roofR = r * 1.1f;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * 360f / seg, a1 = (i + 1) * 360f / seg;
                RecordMeshKit.Make("Roof", RecordMeshKit.ConeSector(roofR, roofH, a0, a1, 4), i % 2 == 0 ? pink : cream, p, new Vector3(0f, H, 0f), Vector3.one, Vector3.zero);
            }
            Shape(PrimitiveType.Cylinder, "RoofBase", p, new Vector3(0f, H, 0f), new Vector3(roofR * 2f, D * 0.04f, roofR * 2f), gold);
            // 屋根のふちのフリルと電球
            for (int i = 0; i < seg * 2; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / (seg * 2);
                Vector3 at = new Vector3(Mathf.Cos(a) * roofR * 0.99f, H - D * 0.1f, Mathf.Sin(a) * roofR * 0.99f);
                var flap = Shape(PrimitiveType.Sphere, "Flap", p, at, new Vector3(roofR * 0.22f, D * 0.22f, D * 0.05f), i % 2 == 0 ? pink : cream);
                flap.transform.localRotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                Shape(PrimitiveType.Sphere, "RoofBulb", p, at + Vector3.down * D * 0.13f, Vector3.one * D * 0.08f, i % 2 == 0 ? bulbB : bulbA);
            }
            // 屋根のてっぺん
            Shape(PrimitiveType.Sphere, "TopBall", p, new Vector3(0f, H + roofH + D * 0.08f, 0f), Vector3.one * D * 0.22f, gold);
            Shape(PrimitiveType.Cylinder, "FlagPole", p, new Vector3(0f, H + roofH + D * 0.35f, 0f), new Vector3(D * 0.03f, D * 0.25f, D * 0.03f), gold);
            Cube("Flag", p, new Vector3(D * 0.14f, H + roofH + D * 0.5f, 0f), new Vector3(D * 0.26f, D * 0.15f, D * 0.01f), pink);

            // 金の柱と、上下する木馬
            float hs = Mathf.Clamp(r * 0.5f, D * 0.7f, D * 1.3f);   // 木馬の大きさ
            for (int i = 0; i < horseCount; i++)
            {
                float a = i * Mathf.PI * 2f / horseCount;
                Vector3 at = new Vector3(Mathf.Cos(a) * r * 0.7f, 0f, Mathf.Sin(a) * r * 0.7f);
                Shape(PrimitiveType.Cylinder, $"Pole{i}", p, at + Vector3.up * H * 0.5f, new Vector3(D * 0.06f, H * 0.5f, D * 0.06f), gold);
                for (int k = 0; k < 10; k++)   // ねじれた柱に見えるらせんの玉
                {
                    float t = (k + 0.5f) / 10f;
                    float sa = t * Mathf.PI * 6f;
                    Shape(PrimitiveType.Sphere, "Twist", p, at + new Vector3(Mathf.Cos(sa) * D * 0.035f, H * t, Mathf.Sin(sa) * D * 0.035f), Vector3.one * D * 0.045f, gold);
                }
                Color coat = i % 4 == 0 ? Color.white : i % 4 == 1 ? Pink : i % 4 == 2 ? Mint : Lilac;
                var horse = BuildHorse(p, hs, coat, i % 2 == 0 ? gold : pink);
                float baseY = Mathf.Min(H * 0.38f, D * 0.45f + hs * 0.4f);
                horse.localPosition = at + Vector3.up * baseY;
                // 頭を回る向きへ（左回りに回すので、+角度の向き）
                horse.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
                w.horses.Add(new Horse { t = horse, s = hs, baseY = baseY, phase = i * 1.05f });
            }

            for (int i = 0; i < lightCount; i++)
            {
                float a = i * Mathf.PI + 0.4f;
                w.lights.Add(PointLight($"Bulb{i}", p, new Vector3(Mathf.Cos(a) * r * 0.6f, H * 0.9f, Mathf.Sin(a) * r * 0.6f), i % 2 == 0 ? Pink : Gold, r * 2f));
            }
            return w;
        }

        /// <summary>木馬（頭が +Z）。s は大きさのめやす</summary>
        Transform BuildHorse(Transform parent, float s, Color coat, Material accent)
        {
            var g = new GameObject("Horse").transform;
            g.SetParent(parent, false);
            var body = Shiny(coat, 0.1f, 0.85f, 0.25f);
            var dark = ProtoAssets.MakeMaterial(new Color(0.08f, 0.06f, 0.1f), 0f, 0.9f);
            var saddle = Shiny(new Color32(0xd8, 0x1c, 0x3a, 0xff), 0.2f, 0.8f, 0.3f);
            Shape(PrimitiveType.Sphere, "Body", g, Vector3.zero, new Vector3(s * 0.3f, s * 0.32f, s * 0.72f), body);
            var neck = Cube("Neck", g, new Vector3(0f, s * 0.2f, s * 0.3f), new Vector3(s * 0.15f, s * 0.38f, s * 0.17f), body);
            neck.transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            var head = Shape(PrimitiveType.Sphere, "Head", g, new Vector3(0f, s * 0.42f, s * 0.46f), new Vector3(s * 0.15f, s * 0.17f, s * 0.32f), body);
            head.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
            for (int k = -1; k <= 1; k += 2)
            {
                Cube("Ear", g, new Vector3(k * s * 0.04f, s * 0.53f, s * 0.38f), new Vector3(s * 0.03f, s * 0.08f, s * 0.03f), body);
                Shape(PrimitiveType.Sphere, "Eye", g, new Vector3(k * s * 0.07f, s * 0.46f, s * 0.48f), Vector3.one * s * 0.035f, dark);
            }
            var mane = Cube("Mane", g, new Vector3(0f, s * 0.32f, s * 0.24f), new Vector3(s * 0.05f, s * 0.36f, s * 0.12f), accent);
            mane.transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            for (int k = -1; k <= 1; k += 2)
            {
                var f = Cube("FrontLeg", g, new Vector3(k * s * 0.09f, -s * 0.12f, s * 0.3f), new Vector3(s * 0.06f, s * 0.32f, s * 0.06f), body);
                f.transform.localRotation = Quaternion.Euler(-55f, 0f, 0f);
                var b = Cube("BackLeg", g, new Vector3(k * s * 0.09f, -s * 0.24f, -s * 0.25f), new Vector3(s * 0.06f, s * 0.34f, s * 0.06f), body);
                b.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
                Shape(PrimitiveType.Sphere, "Hoof", g, new Vector3(k * s * 0.09f, -s * 0.39f, -s * 0.31f), Vector3.one * s * 0.07f, accent);
            }
            var tail = Cube("Tail", g, new Vector3(0f, s * 0.02f, -s * 0.4f), new Vector3(s * 0.05f, s * 0.3f, s * 0.07f), accent);
            tail.transform.localRotation = Quaternion.Euler(40f, 0f, 0f);
            Cube("Saddle", g, new Vector3(0f, s * 0.16f, -s * 0.02f), new Vector3(s * 0.34f, s * 0.06f, s * 0.28f), saddle);
            Cube("Blanket", g, new Vector3(0f, s * 0.13f, -s * 0.02f), new Vector3(s * 0.36f, s * 0.03f, s * 0.36f), accent);
            return g;
        }

        /// <summary>木馬の背中（メダルを乗せる所）の、ワールドの位置</summary>
        Vector3 SaddlePoint(Horse h) => h.t.TransformPoint(new Vector3(0f, h.s * 0.19f + D * 0.06f, -h.s * 0.02f));

        // ================================================================
        // 流れ
        // ================================================================

        protected override void OnBegin()
        {
            nextBoost = boostInterval;
            boostTimer = 0f;
            foreach (var w in wheels) w.boardTimer = 1f;   // せり上がってから乗せ始める
        }

        protected override void OnEnd()
        {
            ReleaseAll(false);
            boostTimer = 0f;
        }

        protected override void OnStateChanged(GameState state)
        {
            bool scoring = state == GameState.Playing || state == GameState.Grace;
            if (!scoring) ReleaseAll(false);
        }

        protected override void Tick(float dt, bool running)
        {
            if (!running) return;
            if (Boost)
            {
                boostTimer -= dt;
                if (boostTimer <= 0f) ReleaseAll(true);   // 最後まで乗っていたメダルも、穴へ飛ばす
                else BoardRiders(dt, boostBoardInterval);   // 高速の間も、空いた木馬に近くのメダルがどんどん乗る
                return;
            }
            nextBoost -= dt;
            if (nextBoost <= 0f)
            {
                nextBoost = boostInterval;
                boostTimer = boostSeconds;
                foreach (var w in wheels)
                {
                    Emit(confetti, w.center + Vector3.up * w.H, Pink, Mathf.RoundToInt(10 + w.r / D * 8f));
                    Emit(confetti, w.center + Vector3.up * w.H, Gold, Mathf.RoundToInt(10 + w.r / D * 8f));
                }
                Play(fanfareSound, ref madeFanfare, () => RecordSynth.Notes("CarouselFanfare", new[] { RecordSynth.Note(0), RecordSynth.Note(4), RecordSynth.Note(7), RecordSynth.Note(12), RecordSynth.Note(7), RecordSynth.Note(12) }, 0.1f, 0.6f, 0.3f, RecordSynth.Wave.Square));
                PopCenter("ファンファーレ！", Gold);
                Speaker(0.8f, 10, Pink);
                return;
            }
            BoardRiders(dt, boardInterval);
        }

        /// <summary>寄ってきたメダルを、空いている木馬に1枚ずつ乗せる（interval 秒ごと、メリーゴーランドごと）</summary>
        void BoardRiders(float dt, float interval)
        {
            if (BoardShown < 0.95f) return;
            foreach (var w in wheels)
            {
                w.boardTimer -= dt;
                if (w.boardTimer > 0f) continue;
                w.boardTimer = interval;
                Horse free = null;
                foreach (var h in w.horses) if (h.rider == null) { free = h; break; }
                if (free == null) continue;
                Medal pick = NearestFree(w, w.r + D * boardRange);
                if (pick != null) StartBoarding(free, pick);
            }
        }

        // ================================================================
        // メダルを寄せる
        // ================================================================

        /// <summary>下段（プッシャーの前の床の上）にあるメダルか</summary>
        bool IsLower(Medal m)
        {
            if (m == null || m.Consumed || m.Body == null || riding.Contains(m)) return false;
            Vector3 p = m.transform.position;
            return p.y > -0.2f && p.y < Layout.pusherHeight * 0.6f
                && p.z > FrontZ - D * 0.2f && p.z < Layout.pusherRetractZ
                && Mathf.Abs(p.x) < HalfW + D;
        }

        void CollectLower()
        {
            lower.Clear();
            foreach (var m in BoardMedals) if (IsLower(m)) lower.Add(m);
        }

        Medal NearestFree(Wheel w, float range)
        {
            CollectLower();
            Medal best = null;
            float bestD = range;
            foreach (var m in lower)
            {
                Vector3 d = m.transform.position - w.center;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < bestD) { bestD = dist; best = m; }
            }
            return best;
        }

        Wheel NearestWheel(Vector3 p, out float dist)
        {
            Wheel best = null;
            dist = float.MaxValue;
            foreach (var w in wheels)
            {
                Vector3 d = p - w.center;
                d.y = 0f;
                float e = d.magnitude - w.r;   // ふちからの距離
                if (e < dist) { dist = e; best = w; }
            }
            return best;
        }

        void FixedUpdate()
        {
            if (!Running || BoardShown < 0.95f || game == null || game.Pool == null) return;
            // 下段のメダルを、いちばん近いメリーゴーランドのふちへ、少しずつ寄せる
            CollectLower();
            foreach (var m in lower)
            {
                Vector3 p = m.transform.position;
                var w = NearestWheel(p, out float edge);
                if (w == null) continue;
                Vector3 d = w.center - p;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < 0.001f) continue;
                float ring = w.r + D * 0.35f;
                // ふちより外なら内へ、台の下にもぐったら外へ（ふちのまわりに集まる）
                float dir = dist > ring ? 1f : -0.6f;
                float k = Mathf.Clamp01(Mathf.Abs(dist - ring) / (D * 1.5f));
                m.Body.WakeUp();
                m.Body.AddForce(d / dist * dir * attractForce * (0.3f + 0.7f * k), ForceMode.Acceleration);
            }
        }

        // ================================================================
        // 木馬に乗せる・飛ばす
        // ================================================================

        void StartBoarding(Horse h, Medal m)
        {
            h.rider = m;
            h.boardT = 0f;
            h.boardFrom = m.transform.position;
            h.kinematicWas = m.Body.isKinematic;
            h.collideWas = m.Body.detectCollisions;
            h.interpWas = m.Body.interpolation;
            m.Body.velocity = Vector3.zero;
            m.Body.angularVelocity = Vector3.zero;
            m.Body.isKinematic = true;
            m.Body.detectCollisions = false;
            m.Body.interpolation = RigidbodyInterpolation.None;
            riding.Add(m);
            Emit(sparkle, h.boardFrom, Random.value < 0.5f ? Pink : Gold, 4);
            if (Time.time - lastBoardSound > 0.08f)
            {
                lastBoardSound = Time.time;
                Play(boardSound, ref madeBoard, () => RecordSynth.Notes("CarouselBoard", new[] { RecordSynth.Note(19), RecordSynth.Note(24) }, 0.05f, 0.3f, 0.2f, RecordSynth.Wave.Bell), 0.4f);
            }
        }

        /// <summary>メダルを木馬からおろす。toHole が true なら、中央の穴をめがけて飛ばす</summary>
        void Release(Horse h, bool toHole)
        {
            var m = h.rider;
            h.rider = null;
            if (m == null) return;
            riding.Remove(m);
            if (m.Body == null) return;
            m.Body.isKinematic = h.kinematicWas;
            m.Body.detectCollisions = h.collideWas;
            m.Body.interpolation = h.interpWas;
            if (!m.gameObject.activeInHierarchy || m.Consumed) return;
            if (!toHole || h.boardT < 1f)
            {
                m.Body.velocity = Vector3.zero;
                m.Body.WakeUp();
                return;
            }
            // 中央の穴をめがけて：flySeconds 秒で穴（手前のふちの少し先）に届く放物線の速さ
            Vector3 from = m.transform.position;
            float hx = HoleHalf * 0.6f;
            Vector3 target = new Vector3(Mathf.Clamp(from.x * 0.15f, -hx, hx) + Random.Range(-hx, hx) * 0.4f, -D * 0.3f, holeZ - D * 0.4f);
            float T = flySeconds * (0.85f + 0.03f * Vector3.Distance(from, target) / D);
            Vector3 g = Physics.gravity;
            Vector3 v = (target - from) / T - 0.5f * g * T;
            v.x *= 1.08f; v.z *= 1.08f;   // 空気の抵抗の分を少し足す
            m.Body.velocity = v;
            m.Body.angularVelocity = Random.insideUnitSphere * 30f;
            m.Body.WakeUp();
            Emit(sparkle, from, Gold, 8);
            if (Time.time - lastLaunchSound > 0.05f)
            {
                lastLaunchSound = Time.time;
                Play(launchSound, ref madeLaunch, () => RecordSynth.Sweep("CarouselLaunch", 500f, 1500f, 0.15f, 0.25f, RecordSynth.Wave.Triangle), 0.5f);
            }
        }

        void ReleaseAll(bool toHole)
        {
            foreach (var w in wheels)
                foreach (var h in w.horses)
                    if (h.rider != null) Release(h, toHole);
            riding.Clear();
        }

        // ================================================================
        // 見た目（毎フレーム）
        // ================================================================

        protected override bool KeepBoardVisible() => (confetti != null && confetti.particleCount > 0) || (sparkle != null && sparkle.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            bool boost = Active && Boost;
            float boostK = boost ? Mathf.Clamp01((boostSeconds - boostTimer) / 0.4f) : 0f;   // 0.4秒で速くなる
            float speed = Mathf.Lerp(spinSpeed, boostSpin, boostK);
            float bob = boost ? 7f : 2.6f;

            foreach (var w in wheels)
            {
                // せり上がる・沈む
                w.root.position = new Vector3(w.center.x, -(w.H + w.r * 0.6f + D * 1.4f) * (1f - BoardShown), w.center.z);
                if (Active && IsScoring) w.angle -= speed * dt;   // 上から見て左回り
                w.spin.localRotation = Quaternion.Euler(0f, w.angle, 0f);
                if (w.column != null) w.column.localRotation = Quaternion.Euler(0f, t * 60f, 0f);

                foreach (var h in w.horses)
                {
                    var p = h.t.localPosition;
                    h.t.localPosition = new Vector3(p.x, h.baseY + Mathf.Sin(t * bob + h.phase) * D * 0.22f, p.z);
                    var e = h.t.localRotation.eulerAngles;
                    h.t.localRotation = Quaternion.Euler(Mathf.Cos(t * bob + h.phase) * 10f, e.y, 0f);

                    var m = h.rider;
                    if (m == null) continue;
                    if (!m.gameObject.activeInHierarchy || m.Consumed) { Release(h, false); continue; }
                    Vector3 seat = SaddlePoint(h);
                    Quaternion rot = h.t.rotation;
                    if (h.boardT < 1f)
                    {
                        // 木馬の背中へ、ぴょんと飛び乗る
                        h.boardT = Mathf.Min(1f, h.boardT + dt / 0.35f);
                        float k = h.boardT;
                        Vector3 pos = Vector3.Lerp(h.boardFrom, seat, k * k * (3f - 2f * k)) + Vector3.up * Mathf.Sin(k * Mathf.PI) * D * 0.8f;
                        m.Body.position = pos;
                        m.transform.SetPositionAndRotation(pos, Quaternion.Slerp(m.transform.rotation, rot, k));
                        if (h.boardT >= 1f) Emit(sparkle, seat, Pink, 3);
                    }
                    else
                    {
                        m.Body.position = seat;
                        m.Body.rotation = rot;
                        m.transform.SetPositionAndRotation(seat, rot);
                        // ファンファーレ中：回る勢いで、穴の方を向いた時に飛び出す
                        if (boost && boostK >= 1f && IsScoring)
                        {
                            Vector3 tangent = h.t.forward;   // 木馬の頭の向き ＝ 回る向き
                            Vector3 toHole = new Vector3(0f, 0f, holeZ) - seat;
                            tangent.y = 0f; toHole.y = 0f;
                            if (Vector3.Dot(tangent.normalized, toHole.normalized) > 0.75f) Release(h, true);
                        }
                    }
                }

                foreach (var l in w.lights)
                {
                    bool on = boost ? Mathf.Repeat(t * 6f + w.center.x, 2f) < 1f : true;
                    l.intensity = (on ? 2f : 0.3f) * BoardShown;
                }
            }

            // 電球の点滅（ファンファーレ中は速く）
            bool flip = boost ? Mathf.FloorToInt(t * 12f) % 2 == 0 : Mathf.FloorToInt(t * 3f) % 2 == 0;
            SetGlow(bulbA, Gold, flip ? 3f : 0.6f);
            SetGlow(bulbB, Color.white, flip ? 0.6f : 3f);

            if (boost && Random.value < dt * 30f)
            {
                var w = wheels[Random.Range(0, wheels.Count)];
                Emit(sparkle, w.root.position + new Vector3(Random.Range(-1f, 1f) * w.r, w.H * Random.Range(0.3f, 1.1f), Random.Range(-1f, 1f) * w.r), Random.value < 0.5f ? Gold : Pink, 2);
            }
        }
    }
}
