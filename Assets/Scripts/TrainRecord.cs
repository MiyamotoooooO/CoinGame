using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N010 汽車「ミッドナイト・エクスプレス」：移動・回収型。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・ふだん：貨物列車がメダルを集めて、中央の穴へ届ける（trainInterval 秒ごと）
    ///   予告（warnSeconds 秒）：下段の「手前・まん中・奥」の線路から1本えらび、その線路が光る。両はしのかべにトンネルの口がせり上がり、
    ///                        踏切の光が点滅して「カンカン」と鳴る
    ///   走る（runSeconds 秒）：黒と金の機関車と、ふたのない貨車 3 両が、片方のトンネルから出てもう片方へ入る（向きは毎回交代）
    ///                        機関車の前のメダルは、ぴょんと跳ねて貨車に入る（最大 cargoMax 枚。貨車の上に見える）。いっぱいなら押していく
    ///                        機関車の上に「貨物 3/9」を出す
    ///   配達：列車がトンネルに入ると、中央の穴の上に光る輪と下向きの矢印（とどけ先）が出る。
    ///        集めたメダルが、列車が入ったトンネルの口から1枚ずつ光の線を引いて山なりに飛び、輪の中へ落ちて中央の穴に入る
    ///        （ふつうに点数とメダルが入る）。入るたびに輪が光り「配達 3/9」が数え上がる。最後に「配達完了！ +N枚」
    ///   列車が押して穴に落ちた分は数えておき、大技で戻す（配達したメダルは数えない）
    /// ・大技「ミッドナイト・エクスプレス」：bigInterval 秒ごと（ふだんの列車が走っていない時）
    ///   夜になる（盤面の明かりが暗くなり、星がまたたき、月が出る）。長い汽笛
    ///   奥のかべの大きなトンネルから、金色の大きな機関車が上段に出て、手前へ走る
    ///     上段の端から下段へ降りて、手前のふちまで走る。機関車の前の光の帯が通った所のメダルを手前へ押す
    ///     （上段は強く、下段は手前ほど強く。盤面を前後に伸ばした倍率 DepthScale もかける）
    ///   手前のふちで浮き上がり、星の線を引きながら空へ上っていく。機関車の下には、光る星の線路（銀河鉄道）が
    ///   少し先まで伸びていき、その上を走る（飛び終わったら、ゆっくり消える）。飛びながら、メダル bigMedals 枚＋押して落ちた分（最大 pushedCap 枚）を
    ///   下段と上段へまく（上段へ upperShare の割合）
    ///   夜が明けて、元の明るさにもどる
    /// ・床の高さは盤面のモデル（Playfield）から調べる。文字・空を飛ぶ機関車・月・星はモニターより手前に映す
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class TrainRecord : RecordScriptBase
    {
        public static TrainRecord Instance { get; private set; }

        [Header("ふだんの列車")]
        [Tooltip("列車が来る間かく（秒）")]
        [Range(3f, 40f)] public float trainInterval = 10f;
        [Tooltip("予告（線路が光って、踏切が鳴る）の秒数")]
        [Range(0.5f, 4f)] public float warnSeconds = 1.5f;
        [Tooltip("列車が盤面を横切る秒数")]
        [Range(1.5f, 10f)] public float runSeconds = 4.5f;
        [Tooltip("貨車に乗せる最大の枚数")]
        [Range(1, 9)] public int cargoMax = 9;
        [Tooltip("機関車の前のメダルを貨車へ入れる広さ（メダルの直径に対する倍率）")]
        [Range(0.3f, 2f)] public float scoopRadius = 0.8f;
        [Tooltip("線路の場所（0 が手前のふちの近く、1 がプッシャーの前）。手前・まん中・奥の3本")]
        public float[] lanes = { 0f, 0.5f, 1f };

        [Header("大技：ミッドナイト・エクスプレス")]
        [Tooltip("大技の間かく（秒）")]
        [Range(10f, 120f)] public float bigInterval = 30f;
        [Range(0, 60)] public int bigMedals = 25;
        [Tooltip("押して落ちた分として足す最大の枚数")]
        [Range(0, 80)] public int pushedCap = 40;
        [Tooltip("大きな機関車の大きさ（ふだんの機関車に対する倍率）")]
        [Range(1f, 3f)] public float bigScale = 1.8f;
        [Tooltip("大きな機関車が、奥から手前のふちまで走る秒数")]
        [Range(1f, 8f)] public float bigRunSeconds = 3.2f;
        [Tooltip("大きな機関車が空へ上っていく秒数")]
        [Range(1f, 6f)] public float flySeconds = 2.6f;
        [Tooltip("光の帯がメダルを手前へ押す強さ")]
        [Range(0f, 10f)] public float bigPush = 3f;
        [Tooltip("下段の奥のメダルを押す強さ（手前を 1 とした割合）")]
        [Range(0f, 1f)] public float lowerPushMin = 0.25f;
        [Tooltip("まくメダルのうち、上段へ飛ばす割合")]
        [Range(0f, 1f)] public float upperShare = 0.4f;
        [Tooltip("夜の明るさ（ふだんを 1 とした割合）")]
        [Range(0f, 1f)] public float nightBrightness = 0.3f;
        [Tooltip("夜の間、シーンの明かり（ディレクショナルライトと環境光）を暗くする")]
        public bool dimSceneLights = true;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip whistleSound;
        public AudioClip bellSound;
        public AudioClip scoopSound;
        public AudioClip deliverSound;
        public AudioClip bigWhistleSound;

        protected override string DefaultKey => "Train";
        protected override string ScreenTitle => "TRAIN";
        protected override string ScreenSubtitle => "ミッドナイト・エクスプレス";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<TrainRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed()
        {
            if (Instance == this) Instance = null;
            RestoreLights();
        }

        const string CargoKind = "TrainCargo";
        static readonly Color EngineBlack = new Color32(0x22, 0x22, 0x2c, 0xff);
        static readonly Color Brass = new Color32(0xe0, 0xb0, 0x40, 0xff);
        static readonly Color WagonRed = new Color32(0x8a, 0x24, 0x22, 0xff);
        static readonly Color WheelRed = new Color32(0xb8, 0x2a, 0x22, 0xff);
        static readonly Color Lamp = new Color32(0xff, 0xe6, 0x8a, 0xff);
        static readonly Color SmokeColor = new Color32(0xd8, 0xd8, 0xe0, 0xff);
        static readonly Color CrossRed = new Color32(0xff, 0x30, 0x30, 0xff);
        static readonly Color Stone = new Color32(0x6a, 0x60, 0x58, 0xff);
        static readonly Color RailColor = new Color32(0xb8, 0xc0, 0xd0, 0xff);
        static readonly Color GoldBody = new Color32(0xf0, 0xc0, 0x40, 0xff);
        static readonly Color GoldDark = new Color32(0x8a, 0x5a, 0x10, 0xff);
        static readonly Color NightBlue = new Color32(0x9c, 0xc0, 0xff, 0xff);
        static readonly Color StarColor = new Color32(0xff, 0xf6, 0xd0, 0xff);

        // ================================================================
        // 列車の形（前が +Z。長さはメダルの直径 D を 1 とした値）
        // ================================================================

        /// <summary>組み立てた列車（機関車と貨車をまとめた物）</summary>
        class TrainRig
        {
            public Transform root;
            public Rigidbody body;
            public BoxCollider col;
            public readonly List<Transform> groups = new List<Transform>();   // 機関車・貨車（かべの外に出たら隠す）
            public readonly List<float> groupZ = new List<float>();           // それぞれのまん中（ローカル Z。D 単位）
            public readonly List<Transform> wheels = new List<Transform>();
            public Light headLamp;
            public Vector3 chimney;      // 煙突の口（ローカル）
            public float length;         // 全体の長さ（D 単位）
            public readonly List<Vector3> slots = new List<Vector3>();   // 貨物を置く所（ローカル）
        }

        TrainRig train, bigTrain;
        ParticleSystem smoke, stars, sparks;
        Transform rails, portalL, portalR, bigPortal, station, moon, beam;
        readonly Material[] crossLamps = new Material[2];
        Material railGlow, beamMat;
        BoardText cargoLabel, stationLabel;

        // ふだんの列車
        enum RunState { Idle, Warn, Run, Done }
        RunState runState = RunState.Idle;
        float nextTrain, stateT, laneZ, lowerY;
        int direction = 1;
        class Cargo { public Medal m; public Vector3 from; public int slot; public float t; }
        readonly List<Cargo> cargo = new List<Cargo>();
        readonly HashSet<Medal> delivering = new HashSet<Medal>();
        float portalShow, stationShow, wheelAngle;
        bool stationOn;
        // 配達で飛んでいるメダル
        class Flight { public Medal m; public Vector3 p0, p1, p2; public float t; }
        readonly List<Flight> flights = new List<Flight>();
        int deliverDone, deliverTotal, exitSide = 1;
        float ringFlash;
        Material ringMat;

        // 大技
        enum BigState { None, Night, Ground, Fly, Morning }
        BigState bigState = BigState.None;
        float bigTimer, bigT, nightK;
        float bigZ;                  // 大きな機関車の前の Z（走っている時）
        int rainLeft, rainTotal;
        float rainStep, rainTimer;
        readonly HashSet<Medal> touched = new HashSet<Medal>();
        Vector3 flyP0, flyP1, flyP2;
        // 空の線路（銀河鉄道）
        const int SkySegments = 48;
        Transform skyTrack;
        readonly Transform[] skySegs = new Transform[SkySegments];
        Material skyRailMat, skyTieMat;
        float skyFade;

        // 夜（シーンの明かりを暗くする）
        readonly List<(Light light, float intensity)> dimmed = new List<(Light, float)>();
        float ambientIntensity;
        Color ambientLight;
        bool lightsStored;

        AudioClip madeWhistle, madeBell, madeScoop, madeDeliver, madeBig, madeNight;

        float SideX => HalfW;
        float UpperY => lowerY + Layout.pusherHeight;
        float CurrentPusherFront
        {
            get
            {
                var p = game.Pusher;
                if (p == null) return PusherFrontZ;
                float t = (1f - Mathf.Cos(p.Phase01 * Mathf.PI * 2f)) * 0.5f;
                return Layout.pusherRetractZ - p.stroke * t;
            }
        }

        // ================================================================
        // 作る
        // ================================================================

        protected override void BuildBoard()
        {
            lowerY = LowerFloorAt(0f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f));

            train = BuildTrain("Train", Glow(EngineBlack, 0.1f), Glow(WagonRed, 0.15f), Glow(Brass, 0.6f), 3);
            bigTrain = BuildTrain("BigTrain", Glow(GoldBody, 0.6f), Glow(GoldDark, 0.4f), Glow(Color.white, 0.9f), 1);
            bigTrain.root.localScale = Vector3.one * bigScale;

            smoke = Particles("Smoke", Board, Vector3.up, D * 1.2f, D * 0.4f, 1.6f, -0.05f, 15f);
            stars = Particles("Stars", Board, Vector3.up, D * 0.05f, D * 0.12f, 1.6f, 0f, 180f);
            sparks = Particles("Sparks", Board, Vector3.up, D * 2f, D * 0.08f, 0.6f, 0.6f, 60f);
            DrawOverMonitor(stars.transform);

            BuildRails();
            portalL = BuildPortal("PortalL", 1f);
            portalR = BuildPortal("PortalR", 1f);
            bigPortal = BuildPortal("BigPortal", bigScale);
            BuildStation();

            // 月（夜だけ。モニターより手前に映す）
            moon = Shape(PrimitiveType.Sphere, "Moon", Board, Vector3.zero, Vector3.one * D * 1.2f, Glow(new Color(1f, 0.95f, 0.75f), 2.2f)).transform;
            DrawOverMonitor(moon);
            moon.gameObject.SetActive(false);

            // 大きな機関車の前の光の帯（メダルを押す所）
            beamMat = Glow(Lamp, 2f);
            beam = Cube("Beam", Board, Vector3.zero, Vector3.one, beamMat).transform;
            beam.gameObject.SetActive(false);

            BuildSkyTrack();

            cargoLabel = BoardLabel("CargoLabel", Board, Vector3.zero, "", D * 0.45f, Lamp);
            cargoLabel.gameObject.SetActive(false);

            MedalMarks.RegisterEffect(CargoKind, (m, type) => { });   // 貨物のメダル（見た目だけ）
        }

        /// <summary>列車を組み立てる（wagons 両の貨車。大きな機関車は wagons = 1 で炭水車だけ）</summary>
        TrainRig BuildTrain(string name, Material bodyMat, Material wagonMat, Material trimMat, int wagons)
        {
            var rig = new TrainRig();
            rig.root = new GameObject(name).transform;
            rig.root.SetParent(Board, false);
            var d = D;
            var lamp = Glow(Lamp, 3f);
            var wheelMat = Glow(WheelRed, 0.3f);
            var dark = Glow(new Color(0.05f, 0.05f, 0.07f), 0f);

            // 機関車
            var loco = new GameObject("Loco").transform;
            loco.SetParent(rig.root, false);
            Cube("Frame", loco, new Vector3(0f, 0.15f, -0.9f) * d, new Vector3(0.95f, 0.12f, 1.8f) * d, dark);
            Shape(PrimitiveType.Cylinder, "Boiler", loco, new Vector3(0f, 0.48f, -0.55f) * d, new Vector3(0.62f, 0.55f, 0.62f) * d, bodyMat)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shape(PrimitiveType.Cylinder, "Face", loco, new Vector3(0f, 0.48f, 0.0f) * d, new Vector3(0.52f, 0.02f, 0.52f) * d, trimMat)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            foreach (float z in new[] { -0.3f, -0.8f })
                Shape(PrimitiveType.Cylinder, "Band", loco, new Vector3(0f, 0.48f, z) * d, new Vector3(0.66f, 0.02f, 0.66f) * d, trimMat)
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shape(PrimitiveType.Cylinder, "Chimney", loco, new Vector3(0f, 0.9f, -0.22f) * d, new Vector3(0.22f, 0.18f, 0.22f) * d, bodyMat);
            Shape(PrimitiveType.Cylinder, "ChimneyTop", loco, new Vector3(0f, 1.08f, -0.22f) * d, new Vector3(0.32f, 0.04f, 0.32f) * d, trimMat);
            Shape(PrimitiveType.Sphere, "Dome", loco, new Vector3(0f, 0.8f, -0.65f) * d, new Vector3(0.26f, 0.22f, 0.26f) * d, trimMat);
            Cube("Cab", loco, new Vector3(0f, 0.6f, -1.45f) * d, new Vector3(0.95f, 0.75f, 0.6f) * d, bodyMat);
            Cube("Roof", loco, new Vector3(0f, 1.0f, -1.45f) * d, new Vector3(1.08f, 0.06f, 0.78f) * d, trimMat);
            foreach (float s in new[] { -1f, 1f })
                Cube("Window", loco, new Vector3(s * 0.48f, 0.72f, -1.45f) * d, new Vector3(0.02f, 0.22f, 0.3f) * d, lamp);
            Shape(PrimitiveType.Cylinder, "HeadLamp", loco, new Vector3(0f, 0.66f, 0.04f) * d, new Vector3(0.22f, 0.05f, 0.22f) * d, lamp)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var catcher = Cube("Catcher", loco, new Vector3(0f, 0.12f, 0.14f) * d, new Vector3(0.9f, 0.16f, 0.28f) * d, trimMat);
            catcher.transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);
            foreach (float z in new[] { -0.3f, -0.8f, -1.3f })
                foreach (float s in new[] { -1f, 1f })
                    rig.wheels.Add(Wheel(loco, new Vector3(s * 0.5f, 0.2f, z) * d, 0.4f * d, wheelMat, trimMat));
            rig.groups.Add(loco);
            rig.groupZ.Add(-0.9f);
            rig.chimney = new Vector3(0f, 1.15f, -0.22f) * d;
            rig.headLamp = PointLight("HeadLamp", rig.root, new Vector3(0f, 0.6f, 0.6f) * d, Lamp, d * 4f);

            // 貨車
            float z0 = -1.95f;
            for (int i = 0; i < wagons; i++)
            {
                float cz = z0 - 0.7f - i * 1.55f;
                var w = new GameObject($"Wagon{i}").transform;
                w.SetParent(rig.root, false);
                Cube("Base", w, new Vector3(0f, 0.2f, cz) * d, new Vector3(1.15f, 0.1f, 1.4f) * d, dark);
                foreach (float s in new[] { -1f, 1f })
                    Cube("Side", w, new Vector3(s * 0.55f, 0.42f, cz) * d, new Vector3(0.06f, 0.36f, 1.4f) * d, wagonMat);
                foreach (float s in new[] { -1f, 1f })
                    Cube("End", w, new Vector3(0f, 0.42f, cz + s * 0.67f) * d, new Vector3(1.15f, 0.36f, 0.06f) * d, wagonMat);
                Cube("Rim", w, new Vector3(0f, 0.61f, cz) * d, new Vector3(1.2f, 0.03f, 1.46f) * d, trimMat);
                foreach (float z in new[] { cz - 0.45f, cz + 0.45f })
                    foreach (float s in new[] { -1f, 1f })
                        rig.wheels.Add(Wheel(w, new Vector3(s * 0.5f, 0.2f, z) * d, 0.36f * d, wheelMat, trimMat));
                Cube("Coupler", w, new Vector3(0f, 0.22f, cz + 0.75f) * d, new Vector3(0.12f, 0.06f, 0.2f) * d, dark);
                // 貨物を置く所（3枚。2枚を並べて、1枚を上に）
                rig.slots.Add(new Vector3(0f, 0.32f, cz - 0.3f) * d);
                rig.slots.Add(new Vector3(0f, 0.36f, cz + 0.3f) * d);
                rig.slots.Add(new Vector3(0f, 0.42f, cz) * d);
                rig.groups.Add(w);
                rig.groupZ.Add(cz);
            }
            rig.length = 1.95f + wagons * 1.55f + 0.3f;

            // 当たり判定（列車ぜんぶ。動かす物なのでキネマティック）
            rig.body = rig.root.gameObject.AddComponent<Rigidbody>();
            rig.body.isKinematic = true;
            rig.body.interpolation = RigidbodyInterpolation.Interpolate;
            rig.col = rig.root.gameObject.AddComponent<BoxCollider>();
            rig.col.center = new Vector3(0f, 0.35f, 0.2f - rig.length * 0.5f) * d;
            rig.col.size = new Vector3(1.15f, 0.6f, rig.length) * d;
            rig.root.gameObject.SetActive(false);
            return rig;
        }

        /// <summary>
        /// 空の線路（銀河鉄道）。短い区切り（SkySegments 個）を並べる。1つの区切りは、2本の光るレールと、まくら木1本。
        /// 区切りの長さ 1、レールの間 1 で作り、置く時に大きさを合わせる。モニターより手前に映す
        /// </summary>
        void BuildSkyTrack()
        {
            skyTrack = new GameObject("SkyTrack").transform;
            skyTrack.SetParent(Board, false);
            skyRailMat = Glow(NightBlue, 2.5f);
            skyTieMat = Glow(StarColor, 1.5f);
            for (int i = 0; i < SkySegments; i++)
            {
                var seg = new GameObject($"Seg{i}").transform;
                seg.SetParent(skyTrack, false);
                foreach (float s in new[] { -0.5f, 0.5f })
                    Cube("Rail", seg, new Vector3(s, 0f, 0f), new Vector3(0.06f, 0.06f, 1.05f), skyRailMat);
                if (i % 2 == 0) Cube("Tie", seg, new Vector3(0f, -0.04f, 0f), new Vector3(1.3f, 0.04f, 0.18f), skyTieMat);
                skySegs[i] = seg;
            }
            DrawOverMonitor(skyTrack);
            skyTrack.gameObject.SetActive(false);
        }

        /// <summary>空の線路を、飛ぶ道（FlyPoint）にそって並べる（まだ見せない）</summary>
        void LaySkyTrack()
        {
            for (int i = 0; i < SkySegments; i++)
            {
                float u0 = i / (float)SkySegments, u1 = (i + 1) / (float)SkySegments;
                var a = FlyPoint(u0);
                var b = FlyPoint(u1);
                var dir = b - a;
                float len = dir.magnitude;
                var seg = skySegs[i];
                seg.position = (a + b) * 0.5f;
                if (len > 0.0001f) seg.rotation = Quaternion.LookRotation(dir / len, Vector3.up);
                float gauge = D * bigScale * Mathf.Lerp(1f, 0.4f, (u0 + u1) * 0.5f);   // 機関車が小さくなるのに合わせて、レールの間もせまく
                seg.localScale = new Vector3(gauge, gauge, len);
                seg.gameObject.SetActive(false);
            }
            skyFade = 1f;
            skyTrack.gameObject.SetActive(true);
        }

        /// <summary>空の線路を見せる：機関車のいる所（道の割合 u）の少し先まで。fade が 0 に近いほど細く・暗く</summary>
        void ShowSkyTrack(float u, float fade, float t)
        {
            for (int i = 0; i < SkySegments; i++)
            {
                float u0 = i / (float)SkySegments;
                bool on = u0 <= u + 0.08f && fade > 0.01f;
                if (skySegs[i].gameObject.activeSelf != on) skySegs[i].gameObject.SetActive(on);
                if (on && u0 > u && Random.value < 0.1f) Emit(stars, skySegs[i].position, StarColor, 1);   // 伸びていく先がきらきらする
            }
            SetGlow(skyRailMat, NightBlue, (2f + Mathf.Sin(t * 8f) * 0.5f) * fade);
            SetGlow(skyTieMat, StarColor, 1.5f * fade);
            foreach (var seg in skySegs)
            {
                if (!seg.gameObject.activeSelf) continue;
                seg.GetChild(0).localScale = new Vector3(0.06f * fade, 0.06f * fade, 1.05f);
                seg.GetChild(1).localScale = new Vector3(0.06f * fade, 0.06f * fade, 1.05f);
            }
        }

        /// <summary>車輪（回す軸の入れ物を返す。軸はローカル X）</summary>
        Transform Wheel(Transform parent, Vector3 pos, float size, Material rim, Material hub)
        {
            var axle = new GameObject("Wheel").transform;
            axle.SetParent(parent, false);
            axle.localPosition = pos;
            Shape(PrimitiveType.Cylinder, "Rim", axle, Vector3.zero, new Vector3(size, D * 0.03f, size), rim)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Cube("Spoke", axle, Vector3.zero, new Vector3(D * 0.07f, size * 0.85f, D * 0.06f), hub);
            return axle;
        }

        void BuildRails()
        {
            rails = new GameObject("Rails").transform;
            rails.SetParent(Board, false);
            railGlow = Glow(RailColor, 0.4f);
            var tieMat = Glow(new Color(0.32f, 0.22f, 0.14f), 0f);
            float len = HalfW * 2f;
            foreach (float s in new[] { -1f, 1f })
                Cube("Rail", rails, new Vector3(0f, D * 0.03f, s * D * 0.3f), new Vector3(len, D * 0.04f, D * 0.05f), railGlow);
            int ties = Mathf.RoundToInt(len / (D * 0.55f));
            for (int k = 0; k < ties; k++)
                Cube("Tie", rails, new Vector3(-HalfW + (k + 0.5f) * len / ties, D * 0.01f, 0f), new Vector3(D * 0.13f, D * 0.02f, D * 0.85f), tieMat);
            rails.gameObject.SetActive(false);
        }

        /// <summary>トンネルの口（石のアーチと、まっ暗な奥。踏切の光つき）。前が +Z（盤面の中を向ける）</summary>
        Transform BuildPortal(string name, float scale)
        {
            var root = new GameObject(name).transform;
            root.SetParent(Board, false);
            var stone = Glow(Stone, 0.05f);
            var dark = Glow(new Color(0.01f, 0.01f, 0.02f), 0f);
            float w = 0.8f, h = 0.75f;   // 口の半分の幅と、柱の高さ（D 単位）
            var d = D;
            Cube("Dark", root, new Vector3(0f, h * 0.6f, -0.08f) * d, new Vector3(w * 2f, h * 1.3f, 0.04f) * d, dark);
            foreach (float s in new[] { -1f, 1f })
                Cube("Pillar", root, new Vector3(s * (w + 0.1f), h * 0.5f, 0f) * d, new Vector3(0.2f, h, 0.3f) * d, stone);
            const int seg = 7;
            for (int i = 0; i < seg; i++)
            {
                float a = Mathf.PI * i / (seg - 1);
                var c = Cube("Arch", root, new Vector3(Mathf.Cos(a) * (w + 0.1f), h + Mathf.Sin(a) * (w + 0.1f) * 0.6f, 0f) * d, new Vector3(0.36f, 0.2f, 0.3f) * d, stone);
                c.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f);
            }
            if (name != "BigPortal")
            {
                int idx = name == "PortalL" ? 0 : 1;
                crossLamps[idx] = Glow(CrossRed, 0.2f);
                foreach (float s in new[] { -1f, 1f })
                    Shape(PrimitiveType.Sphere, "CrossLamp", root, new Vector3(s * 0.35f, h + 0.75f, 0.1f) * d, Vector3.one * 0.22f * d, crossLamps[idx]);
            }
            root.localScale = Vector3.zero;
            root.gameObject.SetActive(false);
            return root;
        }

        /// <summary>とどけ先（中央の穴の上の、光る輪と下向きの矢印。配達の時だけ出る）</summary>
        void BuildStation()
        {
            station = new GameObject("DeliverGoal").transform;
            station.SetParent(Board, false);
            ringMat = Glow(Lamp, 2f);
            var d = D;
            const int seg = 20;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var c = Cube($"Ring{i}", station, new Vector3(Mathf.Cos(a) * 1.3f, 0f, Mathf.Sin(a) * 0.7f) * d, new Vector3(0.38f, 0.05f, 0.1f) * d, ringMat);
                c.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
            }
            // 下向きの矢印（「く」の字を3つ重ねる）
            var arrow = new GameObject("Arrow").transform;
            arrow.SetParent(station, false);
            for (int i = 0; i < 3; i++)
            {
                foreach (float s in new[] { -1f, 1f })
                {
                    var c = Cube("Chevron", arrow, new Vector3(s * 0.17f, (1.0f + i * 0.35f), 0f) * d, new Vector3(0.45f, 0.1f, 0.04f) * d, ringMat);
                    c.transform.localRotation = Quaternion.Euler(0f, 0f, s * 35f);
                }
            }
            DrawOverMonitor(station);
            stationLabel = BoardLabel("DeliverLabel", Board, Vector3.zero, "", D * 0.55f, Lamp);
            stationLabel.gameObject.SetActive(false);
            station.localScale = Vector3.zero;
            station.gameObject.SetActive(false);
        }

        /// <summary>
        /// その場所の、下の段の床の高さ。盤面のモデル（Playfield）の当たり判定だけを見て、上から下へ調べる。
        /// 見つからない時は FloorY から 0.438 下（海のレコードと同じ）
        /// </summary>
        float LowerFloorAt(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 5f, z), Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                bool playfield = false;
                for (var t = h.collider.transform; t != null; t = t.parent)
                    if (t.name == "Playfield") { playfield = true; break; }
                if (playfield && h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? FloorY(x, z) - 0.438f : best;
        }

        // ================================================================
        // 始め・終わり
        // ================================================================

        protected override void OnBegin()
        {
            lowerY = LowerFloorAt(0f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f));
            nextTrain = 2f;
            bigTimer = bigInterval;
            runState = RunState.Idle;
            bigState = BigState.None;
            nightK = 0f;
            cargo.Clear();
            delivering.Clear();
            TakePushed(0);
        }

        protected override void OnEnd()
        {
            // 貨車に乗っていたメダルは、すぐに配達する（盤面の外に止まったままにしない）
            foreach (var c in cargo) DeliverOne(c.m);
            cargo.Clear();
            foreach (var f in flights) DeliverOne(f.m);
            flights.Clear();
            runState = RunState.Idle;
            // 大技の途中で終わった時：まき残しをまいて、すぐ朝にもどす
            if (bigState == BigState.Fly) while (rainLeft > 0) RainOne();
            bigState = BigState.None;
            stationOn = false;
            if (train != null) train.root.gameObject.SetActive(false);
            if (bigTrain != null) bigTrain.root.gameObject.SetActive(false);
            ClearSmoke();
            if (beam != null) beam.gameObject.SetActive(false);
            if (skyTrack != null) skyTrack.gameObject.SetActive(false);
            RestoreLights();
            nightK = 0f;
        }

        protected override bool KeepBoardVisible() =>
            portalShow > 0.01f || stationShow > 0.01f || flights.Count > 0 || (skyTrack != null && skyTrack.gameObject.activeSelf) || (smoke != null && smoke.particleCount > 0);

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (delivering.Remove(medal)) return;   // 配達したメダルは「押し出した分」に数えない
            NoteDropForPush();
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        protected override void Tick(float dt, bool running)
        {
            TickRun(dt, running);
            TickBig(dt, running);
        }

        void TickRun(float dt, bool running)
        {
            switch (runState)
            {
                case RunState.Idle:
                    if (!running || bigState != BigState.None) return;
                    nextTrain -= dt;
                    if (nextTrain <= 0f) StartWarn();
                    break;
                case RunState.Warn:
                    stateT += dt;
                    if (stateT >= warnSeconds) StartRun();
                    break;
                case RunState.Run:
                    stateT += dt / runSeconds;
                    WatchPushed(1.5f);
                    if (stateT >= 1f) FinishRun();
                    break;
                case RunState.Done:
                    stateT += dt;
                    if (stateT >= 1.2f) runState = RunState.Idle;
                    break;
            }
        }

        void StartWarn()
        {
            nextTrain = trainInterval;
            runState = RunState.Warn;
            stateT = 0f;
            direction = -direction;
            float lane = lanes != null && lanes.Length > 0 ? lanes[Random.Range(0, lanes.Length)] : 0.5f;
            float zA = FrontZ + D * 1.0f, zB = PusherFrontZ - D * 0.9f;
            laneZ = Mathf.Lerp(zA, Mathf.Max(zA, zB), Mathf.Clamp01(lane));
            lowerY = LowerFloorAt(0f, laneZ);
            rails.position = new Vector3(0f, lowerY, laneZ);
            portalL.SetPositionAndRotation(new Vector3(-SideX, lowerY, laneZ), Quaternion.Euler(0f, 90f, 0f));
            portalR.SetPositionAndRotation(new Vector3(SideX, lowerY, laneZ), Quaternion.Euler(0f, -90f, 0f));
            Play(bellSound, ref madeBell, () => RecordSynth.Notes("TrainBell", new[] { 988f, 784f, 988f, 784f, 988f, 784f }, 0.22f, 0.1f, 0.25f, RecordSynth.Wave.Square), 0.6f);
            BoardPop("列車接近！", CrossRed, new Vector3(0f, lowerY + D * 2f, laneZ), D * 0.7f);
        }

        void StartRun()
        {
            runState = RunState.Run;
            stateT = 0f;
            cargo.Clear();
            var rig = train;
            rig.root.gameObject.SetActive(true);
            PlaceRun(0f);
            rig.body.position = rig.root.position;
            rig.body.rotation = rig.root.rotation;
            Play(whistleSound, ref madeWhistle, () => RecordSynth.Chord("TrainWhistle", new[] { 587f, 740f, 880f }, 1f, 0.3f, RecordSynth.Wave.Triangle));
        }

        /// <summary>ふだんの列車の前（機関車の先）の X（k は 0〜1。かべのきわから、最後の貨車がかべの外に出るまで）</summary>
        float RunX(float k)
        {
            float startX = -direction * (SideX + D * 0.3f);
            float endX = direction * (SideX + train.length * D + D * 0.3f);
            return Mathf.Lerp(startX, endX, Mathf.Clamp01(k));
        }

        void PlaceRun(float k)
        {
            train.root.SetPositionAndRotation(new Vector3(RunX(k), lowerY, laneZ), Quaternion.LookRotation(new Vector3(direction, 0f, 0f)));
        }

        void FinishRun()
        {
            runState = RunState.Done;
            stateT = 0f;
            train.root.gameObject.SetActive(false);
            ClearSmoke();   // 列車が消えたら、残っている煙も消す
            exitSide = direction;
            if (cargo.Count > 0) StartCoroutine(DeliverRoutine(new List<Cargo>(cargo)));
            cargo.Clear();
        }

        void FixedUpdate()
        {
            if (!Active) return;
            float dt = Time.fixedDeltaTime;
            if (runState == RunState.Run && train.root.gameObject.activeSelf)
            {
                var p = new Vector3(RunX(stateT), lowerY, laneZ);
                train.body.MovePosition(p);
                train.body.MoveRotation(Quaternion.LookRotation(new Vector3(direction, 0f, 0f)));
                Scoop(p);
            }
            if (bigState == BigState.Ground) MoveBigGround(dt);
        }

        /// <summary>機関車の前のメダルを、貨車へ入れる（いっぱいの時は入れない。そのまま押す）</summary>
        void Scoop(Vector3 trainPos)
        {
            if (cargo.Count >= Mathf.Min(cargoMax, train.slots.Count)) return;
            if (Mathf.Abs(trainPos.x) > SideX) return;   // かべの外
            var front = trainPos + new Vector3(direction, 0f, 0f) * D * 0.55f;
            int taken = 0;
            foreach (var m in MedalsNear(front, D * scoopRadius))
            {
                if (cargo.Count >= Mathf.Min(cargoMax, train.slots.Count) || taken >= 2) break;
                if (m.Body == null || m.Body.isKinematic || delivering.Contains(m)) continue;
                if (m.transform.position.y > lowerY + D * 1.2f) continue;
                Hold(m);
                MedalMarks.Set(m, CargoKind, Lamp, 1.2f);
                cargo.Add(new Cargo { m = m, from = m.transform.position, slot = cargo.Count, t = 0f });
                taken++;
                Emit(sparks, m.transform.position, Lamp, 4);
                Play(scoopSound, ref madeScoop, () => RecordSynth.Notes("TrainScoop", new[] { RecordSynth.Note(19), RecordSynth.Note(24) }, 0.04f, 0.1f, 0.2f, RecordSynth.Wave.Bell), 0.5f);
            }
        }

        /// <summary>
        /// 配達：とどけ先（輪と矢印）を出して、集めたメダルを、列車が入ったトンネルの口から1枚ずつ山なりに飛ばす
        /// </summary>
        IEnumerator DeliverRoutine(List<Cargo> list)
        {
            stationOn = true;
            deliverDone = 0;
            deliverTotal = 0;
            foreach (var c in list) if (c.m != null && c.m.IsActiveInPool) deliverTotal++;
            Speaker(0.6f, 4, Lamp);
            yield return new WaitForSeconds(0.45f);
            var mouth = new Vector3(exitSide * (SideX - D * 0.2f), lowerY + D * 0.6f, laneZ);
            var goal = StationPos + Vector3.up * D * 0.9f;
            foreach (var c in list)
            {
                if (c.m == null || !c.m.IsActiveInPool) continue;
                var f = new Flight { m = c.m, p0 = mouth, p2 = goal + new Vector3(Random.Range(-1f, 1f) * D * 0.4f, 0f, 0f), t = 0f };
                f.p1 = (f.p0 + f.p2) * 0.5f + Vector3.up * D * 4f;
                c.m.transform.position = mouth;
                c.m.Body.position = mouth;
                flights.Add(f);
                Emit(sparks, mouth, Lamp, 6);
                yield return new WaitForSeconds(0.16f);
            }
            while (flights.Count > 0) yield return null;
            BoardPop($"配達完了！ +{deliverDone}枚", Lamp, StationPos + Vector3.up * D * 3.2f, D * 1f);
            Speaker(0.8f, 8, Lamp);
            yield return new WaitForSeconds(0.9f);
            stationOn = false;   // しまう
        }

        /// <summary>配達で飛んでいるメダルを動かす。とどけ先に着いたら、中央の穴へ落とす</summary>
        void MoveFlights(float dt)
        {
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                var f = flights[i];
                if (f.m == null || !f.m.IsActiveInPool) { flights.RemoveAt(i); continue; }
                f.t += dt / 0.6f;
                float k = Mathf.Clamp01(f.t);
                float u = 1f - k;
                var p = u * u * f.p0 + 2f * u * k * f.p1 + k * k * f.p2;
                var rot = Quaternion.Euler(k * 720f, k * 360f, 0f);
                f.m.transform.SetPositionAndRotation(p, rot);
                f.m.Body.position = p;
                f.m.Body.rotation = rot;
                if (Random.value < 0.8f) Emit(sparks, p, Lamp, 1);   // 光の線
                if (k < 1f) continue;
                flights.RemoveAt(i);
                // 着いた：輪の中へ落とす
                delivering.Add(f.m);
                Release(f.m, Vector3.down * D * 3f);
                deliverDone++;
                ringFlash = 1f;
                Emit(sparks, p, Lamp, 14);
                Play(deliverSound, ref madeDeliver, () => RecordSynth.Notes("TrainDeliver", new[] { RecordSynth.Note(24), RecordSynth.Note(28), RecordSynth.Note(31) }, 0.05f, 0.15f, 0.2f, RecordSynth.Wave.Bell), 0.6f);
            }
        }

        /// <summary>手前のまん中（中央の穴の上）。メダル駅の場所</summary>
        Vector3 StationPos => new Vector3(0f, lowerY, FrontZ - Layout.holeDepth * 0.5f);

        /// <summary>メダルを1枚、中央の穴の上から落とす</summary>
        void DeliverOne(Medal m)
        {
            if (m == null || !m.IsActiveInPool) return;
            float half = Mathf.Min(Layout.centerHoleWidth * 0.3f, HalfW * 0.5f);
            var p = StationPos + new Vector3(Random.Range(-half, half), D * 1.1f, 0f);
            m.transform.SetPositionAndRotation(p, Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f)));
            m.Body.position = p;
            delivering.Add(m);
            Release(m, Vector3.down * D * 2f);
            Emit(sparks, p, Lamp, 6);
        }

        // ================================================================
        // 大技
        // ================================================================

        void TickBig(float dt, bool running)
        {
            switch (bigState)
            {
                case BigState.None:
                    if (!running) return;
                    bigTimer -= dt;
                    if (bigTimer <= 0f && runState == RunState.Idle) StartBig();
                    break;
                case BigState.Night:
                    bigT += dt;
                    nightK = Mathf.Clamp01(bigT / 0.9f);
                    if (bigT >= 1.2f) StartGround();
                    break;
                case BigState.Ground:
                    bigT += dt / bigRunSeconds;
                    WatchPushed(2.5f);
                    if (bigT >= 1f) StartFly();
                    break;
                case BigState.Fly:
                    bigT += dt / flySeconds;
                    TickRain(dt);
                    if (bigT >= 1f)
                    {
                        // まききれなかった分も、まとめてまく
                        while (rainLeft > 0) RainOne();
                        bigTrain.root.gameObject.SetActive(false);
                        ClearSmoke();
                        bigState = BigState.Morning;
                        bigT = 0f;
                    }
                    break;
                case BigState.Morning:
                    bigT += dt;
                    nightK = Mathf.Clamp01(1f - bigT / 1f);
                    if (bigT >= 1f)
                    {
                        nightK = 0f;
                        RestoreLights();
                        bigState = BigState.None;
                        bigTimer = bigInterval;
                        nextTrain = Mathf.Max(nextTrain, 2f);
                    }
                    break;
            }
        }

        void StartBig()
        {
            bigState = BigState.Night;
            bigT = 0f;
            touched.Clear();
            StoreLights();
            Play(bigWhistleSound, ref madeBig, () => RecordSynth.Chord("TrainBigWhistle", new[] { 392f, 494f, 587f, 740f }, 2.2f, 0.35f, RecordSynth.Wave.Triangle));
            Play(null, ref madeNight, () => RecordSynth.Sweep("TrainNight", 880f, 220f, 1f, 0.15f, RecordSynth.Wave.Sine), 0.6f);
            BoardPop("ミッドナイト・エクスプレス！", NightBlue, new Vector3(0f, UpperY + D * 4f, Mathf.Lerp(PusherFrontZ, BackZ, 0.3f)), D * 1.1f);
            Speaker(1f, 10, NightBlue);
            bigPortal.SetPositionAndRotation(new Vector3(0f, UpperY, BackZ - D * 0.05f), Quaternion.Euler(0f, 180f, 0f));
        }

        void StartGround()
        {
            bigState = BigState.Ground;
            bigT = 0f;
            var rig = bigTrain;
            SetLayer(rig.root, 0);   // 走っている間は、ふつうに映す（メダルの後ろにも隠れる）
            rig.col.enabled = true;
            rig.root.localScale = Vector3.one * bigScale;
            rig.root.gameObject.SetActive(true);
            bigZ = BackZ + rig.length * D * bigScale * 0.2f;
            PlaceBig(bigZ);
            rig.body.position = rig.root.position;
            rig.body.rotation = rig.root.rotation;
            beam.gameObject.SetActive(true);
        }

        /// <summary>大きな機関車の前が z の時の高さ（プッシャーの前のはしで、上段から下段へ降りる）</summary>
        float BigY(float z)
        {
            float edge = CurrentPusherFront;
            float k = Mathf.InverseLerp(edge - D * 0.4f, edge + D * 0.2f, z);
            return Mathf.Lerp(lowerY, UpperY, k);
        }

        void PlaceBig(float z)
        {
            var rig = bigTrain;
            var rot = Quaternion.LookRotation(Vector3.back);
            rig.root.SetPositionAndRotation(new Vector3(0f, BigY(z), z), rot);
            // 機関車と炭水車は、それぞれの場所の高さに合わせる
            for (int i = 0; i < rig.groups.Count; i++)
            {
                float gz = z - rig.groupZ[i] * D * bigScale;   // 後ろ向き（-Z へ進む）なので、ローカルの -Z は ワールドの +Z
                float y = BigY(gz) - rig.root.position.y;
                rig.groups[i].localPosition = new Vector3(0f, y / bigScale, 0f);
            }
        }

        void MoveBigGround(float dt)
        {
            var rig = bigTrain;
            float zStart = BackZ + rig.length * D * bigScale * 0.2f;
            float zEnd = FrontZ + D * 0.6f;
            bigZ = Mathf.Lerp(zStart, zEnd, Ease.InOutCubic(Mathf.Clamp01(bigT)));
            PlaceBig(bigZ);
            rig.body.MovePosition(rig.root.position);
            rig.body.MoveRotation(rig.root.rotation);
            PushWithBeam(bigZ);
        }

        /// <summary>光の帯が通った所のメダルを、手前へ押す（1回の大技で1枚1回）</summary>
        void PushWithBeam(float z)
        {
            float front = z - D * 0.4f;
            float edge = CurrentPusherFront;
            foreach (var m in BoardMedals)
            {
                if (m == null || m.Consumed || m.Body == null || m.Body.isKinematic) continue;
                var p = m.transform.position;
                if (p.y < lowerY - D * 0.5f) continue;   // 穴に落ちかけている
                if (Mathf.Abs(p.z - front) > D * 0.6f || touched.Contains(m)) continue;
                touched.Add(m);
                bool upper = p.y > lowerY + Layout.pusherHeight * 0.6f && p.z > edge - D * 0.3f;
                float k = Mathf.InverseLerp(PusherFrontZ, FrontZ, p.z);
                float power = upper ? 1f : Mathf.Lerp(lowerPushMin, 1f, k * k);
                Push(m, new Vector3(Random.Range(-0.15f, 0.15f), 0.4f, -1f) * D * bigPush * DepthScale * power);
                if (Random.value < 0.15f) Emit(sparks, p, Lamp, 2);
            }
        }

        void StartFly()
        {
            bigState = BigState.Fly;
            bigT = 0f;
            beam.gameObject.SetActive(false);
            var rig = bigTrain;
            rig.col.enabled = false;   // 空を飛ぶ間は見た目だけ（天井やメダルに当たらない）
            for (int i = 0; i < rig.groups.Count; i++) rig.groups[i].localPosition = Vector3.zero;
            DrawOverMonitor(rig.root);
            flyP0 = rig.root.position;
            flyP1 = new Vector3(0f, lowerY + D * 3.5f, FrontZ - D * 0.5f);
            flyP2 = new Vector3(HalfW * 0.75f, Mathf.Min(1.75f, SkyY + D * 4f), BackZ);
            LaySkyTrack();
            rainTotal = Budget(TakePushed(pushedCap) + bigMedals);
            rainLeft = rainTotal;
            rainStep = flySeconds * 0.7f / Mathf.Max(1, rainTotal);
            rainTimer = flySeconds * 0.2f;
            Shake(0.04f, 0.4f);
            Speaker(1f, 14, Lamp);
            BoardPop($"+{rainTotal}枚！", Lamp, new Vector3(0f, UpperY + D * 3f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f)), D * 1.1f);
        }

        Vector3 FlyPoint(float k)
        {
            float u = 1f - k;
            return u * u * flyP0 + 2f * u * k * flyP1 + k * k * flyP2;
        }

        void TickRain(float dt)
        {
            rainTimer -= dt;
            while (rainLeft > 0 && rainTimer <= 0f)
            {
                rainTimer += rainStep;
                RainOne();
            }
        }

        /// <summary>空を飛ぶ機関車から、メダルを1枚まく（下段か上段へ）</summary>
        void RainOne()
        {
            rainLeft--;
            var from = bigTrain.root.position + Vector3.down * D * 0.3f;
            Vector3 target;
            if (Random.value < upperShare)
            {
                float z = Random.Range(Layout.pusherRetractZ + D * 0.3f, BackZ - D * 0.3f);
                target = new Vector3(Random.Range(-HalfW, HalfW) * 0.8f, UpperY + D * 0.2f, z);
            }
            else
            {
                float z = Mathf.Lerp(FrontZ, PusherFrontZ, Random.Range(0.15f, 0.85f));
                target = new Vector3(Random.Range(-HalfW, HalfW) * 0.85f, lowerY + D * 0.2f, z);
            }
            LaunchArc(from, target, Random.Range(0.5f, 0.6f), m => MedalMarks.Set(m, CargoKind, Lamp, 1.2f));
            Emit(stars, from, StarColor, 3);
        }

        // ================================================================
        // 夜（シーンの明かり）
        // ================================================================

        void StoreLights()
        {
            if (lightsStored || !dimSceneLights) return;
            dimmed.Clear();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l != null && l.enabled && l.type == LightType.Directional) dimmed.Add((l, l.intensity));
            ambientIntensity = RenderSettings.ambientIntensity;
            ambientLight = RenderSettings.ambientLight;
            lightsStored = true;
        }

        void ApplyNight()
        {
            if (!lightsStored) return;
            float k = Mathf.Lerp(1f, nightBrightness, nightK);
            foreach (var (l, i) in dimmed) if (l != null) l.intensity = i * k;
            RenderSettings.ambientIntensity = ambientIntensity * k;
            RenderSettings.ambientLight = ambientLight * k;
        }

        void RestoreLights()
        {
            if (!lightsStored) return;
            foreach (var (l, i) in dimmed) if (l != null) l.intensity = i;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.ambientLight = ambientLight;
            dimmed.Clear();
            lightsStored = false;
        }

        /// <summary>出ている煙を全部消す</summary>
        void ClearSmoke()
        {
            if (smoke != null) smoke.Clear(true);
        }

        static void SetLayer(Transform root, int layer)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        // ================================================================
        // 見た目
        // ================================================================

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            bool warnOrRun = runState == RunState.Warn || runState == RunState.Run;

            // 線路とトンネルの口
            portalShow = Mathf.MoveTowards(portalShow, warnOrRun || stationOn ? 1f : 0f, dt * 3f);
            rails.gameObject.SetActive(portalShow > 0.01f);
            rails.localScale = new Vector3(1f, 1f, Mathf.Max(0.001f, portalShow));
            float railPulse = runState == RunState.Warn ? 1.5f + Mathf.Sin(t * 12f) * 1f : 0.4f;
            SetGlow(railGlow, RailColor, railPulse);
            foreach (var p in new[] { portalL, portalR })
            {
                p.gameObject.SetActive(portalShow > 0.01f);
                p.localScale = new Vector3(1f, Ease.OutBack(portalShow), 1f);
            }
            bool blink = Mathf.Repeat(t * 2.5f, 1f) < 0.5f;
            for (int i = 0; i < 2; i++)
                if (crossLamps[i] != null) SetGlow(crossLamps[i], CrossRed, warnOrRun ? ((i == 0) == blink ? 3f : 0.2f) : 0.2f);

            // ふだんの列車：かべの外に出ている所は隠す。車輪を回す。煙
            if (train.root.gameObject.activeSelf)
            {
                wheelAngle += dt * 720f;
                foreach (var w in train.wheels) w.localRotation = Quaternion.Euler(wheelAngle, 0f, 0f);
                HideOutside(train, 1f);
                // 煙は、機関車が盤面の中に見えている時だけ出す（トンネルの中では出さない）
                if (train.groups[0].gameObject.activeSelf && Random.value < 0.5f) Emit(smoke, train.root.TransformPoint(train.chimney), SmokeColor, 1);
                train.headLamp.intensity = 2f * BoardShown;
                MoveCargo(dt);
                cargoLabel.gameObject.SetActive(Mathf.Abs(train.root.position.x) < SideX + D);
                cargoLabel.transform.position = train.root.position + new Vector3(-direction * D * 1.5f, D * 1.6f, 0f);
                cargoLabel.text = $"貨物 {cargo.Count}/{Mathf.Min(cargoMax, train.slots.Count)}";
            }
            else cargoLabel.gameObject.SetActive(false);

            // とどけ先（配達の間だけ）：輪がゆっくり回り、メダルが入るとぱっと光る。矢印は上下にはずむ
            MoveFlights(dt);
            ringFlash = Mathf.MoveTowards(ringFlash, 0f, dt * 3f);
            stationShow = Mathf.MoveTowards(stationShow, stationOn ? 1f : 0f, dt * 4f);
            bool goalOn = stationShow > 0.001f;
            station.gameObject.SetActive(goalOn);
            stationLabel.gameObject.SetActive(goalOn && stationOn);
            if (goalOn)
            {
                station.position = StationPos + Vector3.up * D * 0.1f;
                station.rotation = Quaternion.Euler(0f, t * 40f, 0f);
                station.localScale = Vector3.one * (stationOn ? Ease.OutBack(stationShow) : stationShow) * (1f + 0.15f * ringFlash);
                var arrow = station.Find("Arrow");
                if (arrow != null)
                {
                    arrow.localPosition = Vector3.up * D * 0.25f * Mathf.Abs(Mathf.Sin(t * 5f));
                    arrow.rotation = Quaternion.identity;   // 矢印はいつもこちらを向く
                }
                SetGlow(ringMat, Color.Lerp(Lamp, Color.white, ringFlash), 2f + 3f * ringFlash);
                stationLabel.transform.position = StationPos + Vector3.up * D * 2.4f;
                stationLabel.text = $"配達 {deliverDone}/{deliverTotal}";
                stationLabel.Scale = 1f + 0.25f * ringFlash;
            }

            // 大技
            UpdateBig(dt, t);
            ApplyNight();
        }

        /// <summary>貨車へ跳ねて入るメダルと、乗っているメダルを動かす</summary>
        void MoveCargo(float dt)
        {
            foreach (var c in cargo)
            {
                if (c.m == null || !c.m.IsActiveInPool) continue;
                c.t += dt / 0.35f;
                var slot = train.root.TransformPoint(train.slots[Mathf.Min(c.slot, train.slots.Count - 1)]);
                float k = Mathf.Clamp01(c.t);
                var p = Vector3.Lerp(c.from, slot, Ease.OutCubic(k)) + Vector3.up * Mathf.Sin(k * Mathf.PI) * D * 0.8f;
                var rot = train.root.rotation * Quaternion.Euler(0f, c.slot * 47f, 0f);
                c.m.transform.SetPositionAndRotation(p, rot);
                c.m.Body.position = p;
                c.m.Body.rotation = rot;
                // かべの外（トンネルの中）に入った貨物は、盤面の外の見えない所で待たせる（配達の時に駅へ移す）
                if (k >= 1f && Mathf.Abs(p.x) > SideX + D * 0.3f)
                {
                    var hide = new Vector3(Mathf.Sign(p.x) * (SideX + 3f), lowerY, laneZ);
                    c.m.transform.position = hide;
                    c.m.Body.position = hide;
                }
            }
        }

        /// <summary>かべの外に出ている機関車・貨車を隠す（トンネルの中にいるように見せる）</summary>
        void HideOutside(TrainRig rig, float scale)
        {
            for (int i = 0; i < rig.groups.Count; i++)
            {
                var c = rig.root.TransformPoint(new Vector3(0f, 0f, rig.groupZ[i] * D));
                bool inside = Mathf.Abs(c.x) < SideX + D * 0.4f * scale;
                if (rig.groups[i].gameObject.activeSelf != inside) rig.groups[i].gameObject.SetActive(inside);
            }
        }

        void UpdateBig(float dt, float t)
        {
            bool night = nightK > 0.01f;
            // 月と星
            moon.gameObject.SetActive(night);
            if (night)
            {
                moon.position = new Vector3(-HalfW * 0.75f, Mathf.Min(1.7f, SkyY + D * 3.5f), BackZ - D * 0.5f);
                moon.localScale = Vector3.one * D * 1.2f * nightK;
                if (Random.value < nightK * 0.8f)
                {
                    var sp = new Vector3(Random.Range(-HalfW, HalfW) * 1.1f, Random.Range(SkyY + D * 1.5f, Mathf.Min(1.8f, SkyY + D * 5f)), Random.Range(PusherFrontZ, BackZ));
                    Emit(stars, sp, StarColor, 1);
                }
            }

            // 空の線路：飛んでいる間は機関車の少し先まで伸び、飛び終わったら（朝の間に）ゆっくり消える
            if (skyTrack.gameObject.activeSelf)
            {
                if (bigState == BigState.Fly) ShowSkyTrack(Ease.InOutCubic(Mathf.Clamp01(bigT)), 1f, t);
                else
                {
                    skyFade = Mathf.MoveTowards(skyFade, 0f, dt / 1.2f);
                    ShowSkyTrack(1f, skyFade, t);
                    if (skyFade <= 0f) skyTrack.gameObject.SetActive(false);
                }
            }

            // トンネル（奥のかべ）
            bool portalOn = bigState == BigState.Night || bigState == BigState.Ground;
            float ps = Mathf.MoveTowards(bigPortal.localScale.y / bigScale, portalOn ? 1f : 0f, dt * 2.5f);
            bigPortal.gameObject.SetActive(ps > 0.01f);
            bigPortal.localScale = new Vector3(bigScale, bigScale * ps, bigScale);

            var rig = bigTrain;
            if (!rig.root.gameObject.activeSelf) return;
            wheelAngle += dt * 900f;
            foreach (var w in rig.wheels) w.localRotation = Quaternion.Euler(wheelAngle, 0f, 0f);
            rig.headLamp.intensity = 4f;
            if (Random.value < 0.7f) Emit(smoke, rig.root.TransformPoint(rig.chimney), SmokeColor, 2);

            if (bigState == BigState.Ground)
            {
                // 光の帯（機関車の前。盤面の幅いっぱい）
                float y = BigY(bigZ - D * 0.4f);
                beam.position = new Vector3(0f, y + D * 0.03f, bigZ - D * 0.4f);
                beam.localScale = new Vector3(HalfW * 2f, D * 0.03f, D * 0.5f);
                SetGlow(beamMat, Lamp, 2f + Mathf.Sin(t * 20f) * 0.6f);
                // 奥のかべより後ろにいる所は隠す
                for (int i = 0; i < rig.groups.Count; i++)
                {
                    float gz = bigZ - rig.groupZ[i] * D * bigScale;
                    bool inside = gz < BackZ + D * 0.3f;
                    if (rig.groups[i].gameObject.activeSelf != inside) rig.groups[i].gameObject.SetActive(inside);
                }
            }
            else if (bigState == BigState.Fly)
            {
                // 空へ：曲線にそって上り、だんだん小さくなる。星の線を引く
                float k = Mathf.Clamp01(bigT);
                var p = FlyPoint(Ease.InOutCubic(k));
                var ahead = FlyPoint(Mathf.Min(1f, Ease.InOutCubic(k) + 0.02f));
                var dir = ahead - p;
                rig.root.position = p;
                if (dir.sqrMagnitude > 0.000001f) rig.root.rotation = Quaternion.Slerp(rig.root.rotation, Quaternion.LookRotation(dir), dt * 8f);
                rig.root.localScale = Vector3.one * bigScale * Mathf.Lerp(1f, 0.4f, k);
                foreach (var g in rig.groups) if (!g.gameObject.activeSelf) g.gameObject.SetActive(true);
                Emit(stars, p - dir.normalized * D * 0.6f, StarColor, 3);
            }
        }
    }
}
