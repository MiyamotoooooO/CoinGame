using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N008 海「ビッグウェーブ」：吸引・巻き込み型。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・ふだん「潮吹き」（spoutInterval 秒ごと）：
    ///   下の段のまん中あたりの床に、丸い穴がぽっかり開く（穴の上のメダルは押しのけられる）
    ///   穴からクジラがこちら向きに顔を出し、頭の穴から水を噴水のように噴き上げる
    ///   （クジラは少し前かがみで、頭の穴が見える。前かがみの角度は smallWhaleTilt）
    ///   穴の場所は、潮吹きのたびに下の段のどこかにランダムで決まる（holeDepthMin〜Max、holeSideRange）
    ///   潮吹きと同時に、噴水のてっぺんからメダル（ふつうの黄色）が spoutMedals 枚、次々に噴き出して穴のまわりに降る。
    ///   ふだんはこうして盤面にメダルがたまっていき、大技の大波でまとめて流す（たまる → 流す、のくり返し）
    ///   噴水の近くのメダル（最大 spoutRideMax 枚）は巻き込まれて、水の柱に乗ってぐるぐる回りながら上がっていき、
    ///   噴水が止まると周りへ散らばる
    ///   潮吹きのあと spoutWindow 秒のあいだに中央の穴に落ちると、1枚 +waveBonus pt
    ///   床の上には、いつも水面のゆらゆらした光が映る
    /// ・大技「大波」（bigInterval 秒ごと）：
    ///   上の段の奥の壁（投入数の札のあたり）に、こちらを向いた光る輪っかが開き、大きなクジラが輪をくぐって出てくる
    ///   はじめにこちらを向いて顔を見せ、そのあと後ろを向いて尻尾を振る
    ///   尻尾の一振りで大波が起き、上の段から手前のふちまで押し寄せる
    ///   上の段：波が通ったメダルは波の上に乗って（最大 waveRideMax 枚）いっしょに運ばれ、手前のふちで投げ出される（全部流れる）
    ///   下の段：段差を落ちた波は少しずつ弱まる。手前のふちに近いメダルほど強く押され（ふち近くの1〜2列が落ちる）、
    ///           奥のメダルは少し前へずれるだけ（押す強さは lowerPushMin〜1 倍。手前ほど強い）
    ///   最後に「大潮吹き」：下の段のまん中から大きなクジラが顔を出し、太くて高い潮を吹いて、
    ///   押し流して落ちた分（最大 pushedCap 枚）＋bigMedals 枚のメダルを噴き出す。メダルは下の段と上の段の両方に降る（上の段へ upperShare の割合）
    ///   大波が巻き込んだメダル1枚ごとにゲージ +gaugePerMedal（最大 gaugeCap 枚ぶん）
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class SeaRecord : RecordScriptBase
    {
        public static SeaRecord Instance { get; private set; }

        [Header("ふだん：潮吹き")]
        [Tooltip("潮吹きの間かく（秒）")]
        [Range(3f, 30f)] public float spoutInterval = 8f;
        [Tooltip("床の穴の場所（0 が手前のふち、1 がプッシャーの前）。潮吹きのたびに、この範囲のどこかにランダムで開く")]
        [Range(0.05f, 0.95f)] public float holeDepthMin = 0.25f;
        [Range(0.05f, 0.95f)] public float holeDepthMax = 0.6f;
        [Tooltip("床の穴の左右の範囲（盤面の幅の半分に対する割合）。この範囲のどこかにランダムで開く")]
        [Range(0f, 0.9f)] public float holeSideRange = 0.6f;
        [Tooltip("床の穴とクジラの場所のずれ（ワールド）。穴・クジラ・噴水がまとめて動く")]
        public Vector3 spoutOffset = new Vector3(0f, -0.438f, 0.572f);
        [Tooltip("噴水がメダルを巻き込む半径（メダルの直径に対する倍率）")]
        [Range(0.5f, 6f)] public float spoutRadius = 2.2f;
        [Tooltip("噴水がメダルを飛ばす強さ")]
        [Range(0f, 10f)] public float spoutPower = 3f;
        [Tooltip("噴水に乗って巻き上げられるメダルの最大の枚数")]
        [Range(0, 30)] public int spoutRideMax = 8;
        [Tooltip("潮吹きのたびに、噴水から噴き出すメダルの枚数")]
        [Range(0, 30)] public int spoutMedals = 8;
        [Tooltip("噴き出したメダルが散らばる広さ（メダルの直径に対する倍率）")]
        [Range(0.2f, 5f)] public float spoutScatter = 1.6f;
        [Tooltip("床のクジラの前かがみの角度（X の回転。-90 で真上を向く、0 で正面）")]
        [Range(-90f, 0f)] public float smallWhaleTilt = -30f;
        [Tooltip("潮吹きのあと、中央の穴でボーナスになる秒数")]
        [Range(0f, 10f)] public float spoutWindow = 3f;
        [Range(0, 100)] public int waveBonus = 10;

        [Header("大技：大波")]
        [Tooltip("大波の間かく（秒）")]
        [Range(10f, 120f)] public float bigInterval = 30f;
        [Tooltip("大波が奥から手前まで進む秒数")]
        [Range(0.5f, 5f)] public float bigWaveSeconds = 2f;
        [Tooltip("大波がメダルを押し流す強さ")]
        [Range(0f, 10f)] public float bigWavePush = 3.2f;
        [Tooltip("下の段で、いちばん奥（プッシャーの前）のメダルを押す強さの倍率。手前のふちに近いほど 1 に近づく")]
        [Range(0f, 1f)] public float lowerPushMin = 0.15f;
        [Tooltip("大潮吹きのクジラと潮の大きさ（ふだんの潮吹きに対する倍率）")]
        [Range(1f, 3f)] public float bigSpoutScale = 1.6f;
        [Tooltip("大潮吹きで、上の段へ飛ばすメダルの割合")]
        [Range(0f, 1f)] public float upperShare = 0.4f;
        [Tooltip("大波（上の段）に乗って運ばれるメダルの最大の枚数")]
        [Range(0, 80)] public int waveRideMax = 40;
        [Tooltip("大波のあとに降ってくるメダルの枚数（押し流した分とは別に足す）")]
        [Range(0, 60)] public int bigMedals = 15;
        [Tooltip("押し流して落ちた分として降らせる最大の枚数")]
        [Range(0, 80)] public int pushedCap = 40;
        [Tooltip("巻き込んだメダル1枚ごとのゲージ")]
        [Range(0f, 5f)] public float gaugePerMedal = 0.5f;
        [Tooltip("ゲージを足す最大の枚数")]
        [Range(0, 100)] public int gaugeCap = 40;

        [Header("大きさ")]
        [Tooltip("床の穴から出るクジラの大きさ（メダルの直径に対する倍率）")]
        [Range(0.3f, 3f)] public float smallWhaleSize = 0.75f;
        [Tooltip("壁の穴から出るクジラの大きさ（メダルの直径に対する倍率）")]
        [Range(0.3f, 4f)] public float bigWhaleSize = 0.95f;
        [Tooltip("壁の穴の高さ（上の段の面から。メダルの直径に対する倍率）")]
        [Range(0f, 5f)] public float wallHoleHeight = 1.15f;
        [Tooltip("壁の穴を、壁からどれだけ手前に出すか（投入数の札にかくれる時に大きく。メダルの直径に対する倍率）")]
        [Range(0f, 3f)] public float wallHoleForward = 0.6f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip spoutSound;
        public AudioClip waveSound;
        public AudioClip whaleSound;
        public AudioClip splashSound;

        protected override string DefaultKey => "Sea";
        protected override string ScreenTitle => "SEA";
        protected override string ScreenSubtitle => "ビッグウェーブ";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<SeaRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        const string SeaKind = "SeaWater";
        static readonly Color Water = new Color32(0x3c, 0xb4, 0xff, 0xff);
        static readonly Color Foam = new Color32(0xe8, 0xf8, 0xff, 0xff);
        static readonly Color WhaleColor = new Color32(0x2c, 0x4c, 0x8c, 0xff);
        static readonly Color HoleColor = new Color32(0x06, 0x10, 0x22, 0xff);
        static readonly Color DeepWater = new Color32(0x14, 0x40, 0x80, 0xff);

        // 潮吹きの流れ（秒）：穴が開く → 顔を出す → 噴く → 沈む → 穴が閉じる
        const float SpoutOpen = 0.3f, SpoutRise = 0.5f, SpoutBlow = 1.8f, SpoutSink = 0.5f, SpoutClose = 0.3f;
        const float BigSpoutBlow = 2.6f;
        float SpoutTotal => SpoutOpen + SpoutRise + curBlow + SpoutSink + SpoutClose;
        bool spoutBig;                 // 今の潮吹きが大潮吹きか
        float curScale = 1f, curBlow = SpoutBlow;
        bool finalePending;            // 大波のあと、大潮吹きを待っている
        // 大波の流れ（秒）：穴が開く → 出てくる → 後ろを向く → 尻尾を振る（大波） → もどる → 穴が閉じる
        const float BigOpen = 0.4f, BigOut = 1f, BigTurn = 0.9f, BigTail = 1.2f, BigBack = 1f, BigClose = 0.4f;
        float BigTotal => BigOpen + BigOut + BigTurn + BigTail + Mathf.Max(BigBack, bigWaveSeconds) + BigClose;

        float spoutTimer, bigTimer;
        float spoutT = -1f, bigT = -1f, waveT = -1f, windowTimer;
        float spoutPulse;
        bool rainDone;
        int waveCaught;
        readonly HashSet<Medal> touched = new HashSet<Medal>();

        /// <summary>水に乗って運ばれているメダル</summary>
        class Rider
        {
            public Medal m;
            public Vector3 start;
            public float phase, height, t;
        }
        readonly List<Rider> spoutRiders = new List<Rider>();
        readonly List<Rider> waveRiders = new List<Rider>();

        readonly List<Transform> caustics = new List<Transform>();
        readonly List<Material> causticMats = new List<Material>();
        Transform floorHole, wallHole, smallWhale, bigWhale, smallTail, bigTail, column, waveBody;
        Material columnMat, waveMat, crestMat;
        ParticleSystem spray;
        Light seaLight;
        float floorY;
        AudioClip madeSpout, madeWave, madeWhale, madeSplash;

        float holeX, holeDepth = 0.4f;   // 今の潮吹きの穴の場所（潮吹きのたびにランダムで決める）
        Vector3 HolePos => new Vector3(holeX, floorY, Mathf.Lerp(FrontZ, PusherFrontZ, holeDepth)) + spoutOffset;
        float UpperY => Layout.pusherHeight;
        Vector3 WallHolePos => new Vector3(0f, UpperY + D * wallHoleHeight, BackZ - D * wallHoleForward);
        float ColumnH => D * 3f * curScale;

        /// <summary>下の段の床の高さ（クジラの穴の高さに合わせる）</summary>
        float LowerFloorY => floorY + spoutOffset.y;
        /// <summary>上の段（プッシャーの上）の面の高さ</summary>
        float UpperFloorY => LowerFloorY + Layout.pusherHeight;

        /// <summary>潮の吹き出し口（クジラの頭の穴）</summary>
        Vector3 SpoutStart => smallWhale != null && smallWhale.gameObject.activeSelf
            ? smallWhale.TransformPoint(new Vector3(0f, 0.55f, 0.55f))
            : HolePos + Vector3.up * D * smallWhaleSize * curScale;

        // ================================================================
        // 作る
        // ================================================================

        protected override void BuildBoard()
        {
            floorY = FloorY(0f, Mathf.Lerp(FrontZ, PusherFrontZ, holeDepth));

            // 水面の光
            for (int i = 0; i < 10; i++)
            {
                var m = Glow(Water, 0.6f);
                var c = Shape(PrimitiveType.Cylinder, $"Caustic{i}", Board, Vector3.zero, new Vector3(D * 1.2f, D * 0.003f, D * 1.2f), m);
                caustics.Add(c.transform);
                causticMats.Add(m);
            }
            seaLight = PointLight("SeaLight", Board, new Vector3(0f, UpperY + D * 6f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f)), Water, HalfW * 2.5f);

            // 床の穴と、壁の穴（黒い円と、水色のふち）
            floorHole = MakeHole("FloorHole", D * smallWhaleSize * 2f);
            floorHole.position = HolePos + Vector3.up * D * 0.03f;
            wallHole = MakeWallRing("WallRing", D * bigWhaleSize * 1.15f);
            wallHole.position = WallHolePos;

            // クジラ（小さい方は床の穴、大きい方は壁の穴から）
            smallWhale = BuildWhale("SmallWhale", out smallTail);
            bigWhale = BuildWhale("BigWhale", out bigTail);

            // 噴水の水の柱
            columnMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.7f, 0.9f, 1f, 0.45f) };
            column = Shape(PrimitiveType.Cylinder, "Column", Board, Vector3.zero, Vector3.one, columnMat).transform;
            column.gameObject.SetActive(false);

            // 大波（すける水の壁と、白い泡の頭）
            waveMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.25f, 0.6f, 1f, 0.4f) };
            waveBody = new GameObject("BigWave").transform;
            waveBody.SetParent(Board, false);
            Cube("Water", waveBody, new Vector3(0f, 0f, 0f), new Vector3(HalfW * 2.05f, D * 1.6f, D * 1.2f), waveMat);
            crestMat = Glow(Foam, 1.5f);
            Cube("Crest", waveBody, new Vector3(0f, D * 0.85f, -D * 0.3f), new Vector3(HalfW * 2.1f, D * 0.3f, D * 0.6f), crestMat);
            waveBody.gameObject.SetActive(false);

            spray = Particles("Spray", Board, Vector3.up, D * 3f, D * 0.25f, 0.9f, 0.6f, 35f);
            var main = spray.main;
            main.maxParticles = 1500;

            MedalMarks.RegisterEffect(SeaKind, (m, type) => { });   // 海のメダル（見た目だけ）
        }

        readonly List<Transform> ringSegments = new List<Transform>();
        Material ringMat;

        /// <summary>
        /// 壁の輪っか（こちらを向いた、光る水色の輪と、中の暗い面）。半径 radius。
        /// 中の暗い面の後ろにいるクジラは見えないので、輪から出てくるように見える
        /// </summary>
        Transform MakeWallRing(string name, float radius)
        {
            var root = new GameObject(name).transform;
            root.SetParent(Board, false);
            ringMat = Glow(Water, 2f);
            const int n = 20;
            float seg = 2f * Mathf.PI * radius / n * 1.15f;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                var c = Cube($"Seg{i}", root, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f), new Vector3(seg, D * 0.16f, D * 0.16f), ringMat);
                c.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
                ringSegments.Add(c.transform);
            }
            // 中の暗い面（円柱を立てて、面をこちらに向ける）
            var dark = Shape(PrimitiveType.Cylinder, "Dark", root, new Vector3(0f, 0f, D * 0.02f), new Vector3(radius * 2f, D * 0.01f, radius * 2f), Glow(HoleColor, 0f));
            dark.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            root.gameObject.SetActive(false);
            return root;
        }

        Transform MakeHole(string name, float size)
        {
            var root = new GameObject(name).transform;
            root.SetParent(Board, false);
            Shape(PrimitiveType.Cylinder, "Rim", root, Vector3.zero, new Vector3(size * 1.15f, D * 0.01f, size * 1.15f), Glow(Water, 1.2f));
            Shape(PrimitiveType.Cylinder, "Dark", root, new Vector3(0f, D * 0.005f, 0f), new Vector3(size, D * 0.012f, size), Glow(HoleColor, 0f));
            root.gameObject.SetActive(false);
            return root;
        }

        /// <summary>クジラ（頭が +Z。大きさ 1 で長さ 2.6 くらい）。尻尾は振れるように別の入れ物</summary>
        Transform BuildWhale(string name, out Transform tail)
        {
            var root = new GameObject(name).transform;
            root.SetParent(Board, false);
            var body = Glow(WhaleColor, 0.25f);
            var belly = Glow(new Color(0.9f, 0.93f, 0.97f), 0.15f);
            var black = Glow(new Color(0.02f, 0.02f, 0.04f), 0f);
            Shape(PrimitiveType.Sphere, "Body", root, Vector3.zero, new Vector3(1.4f, 1.1f, 2.6f), body);
            Shape(PrimitiveType.Sphere, "Belly", root, new Vector3(0f, -0.33f, 0.15f), new Vector3(1.2f, 0.6f, 2.2f), belly);
            for (int s = -1; s <= 1; s += 2)
            {
                Shape(PrimitiveType.Sphere, "Eye", root, new Vector3(s * 0.5f, 0.12f, 0.98f), Vector3.one * 0.16f, black);
                Shape(PrimitiveType.Sphere, "Shine", root, new Vector3(s * 0.52f, 0.16f, 1.04f), Vector3.one * 0.06f, Glow(Color.white, 1f));
                var fin = Shape(PrimitiveType.Sphere, "Fin", root, new Vector3(s * 0.75f, -0.3f, 0.3f), new Vector3(0.7f, 0.1f, 0.35f), body);
                fin.transform.localRotation = Quaternion.Euler(0f, 0f, s * -25f);
            }
            Cube("Mouth", root, new Vector3(0f, -0.2f, 1.18f), new Vector3(0.7f, 0.04f, 0.1f), black);
            Shape(PrimitiveType.Sphere, "Blowhole", root, new Vector3(0f, 0.55f, 0.55f), new Vector3(0.18f, 0.05f, 0.12f), black);
            tail = new GameObject("TailPivot").transform;
            tail.SetParent(root, false);
            tail.localPosition = new Vector3(0f, 0.1f, -1.15f);
            Cube("Stem", tail, new Vector3(0f, 0f, -0.35f), new Vector3(0.35f, 0.3f, 0.8f), body);
            for (int s = -1; s <= 1; s += 2)
            {
                var fluke = Shape(PrimitiveType.Sphere, "Fluke", tail, new Vector3(s * 0.42f, 0.05f, -0.8f), new Vector3(0.8f, 0.12f, 0.42f), body);
                fluke.transform.localRotation = Quaternion.Euler(0f, s * 20f, 0f);
            }
            root.gameObject.SetActive(false);
            return root;
        }

        // ================================================================
        // 流れ
        // ================================================================

        protected override void OnBegin()
        {
            spoutTimer = 2f;
            bigTimer = bigInterval;
            spoutT = -1f;
            bigT = -1f;
            waveT = -1f;
            windowTimer = 0f;
            finalePending = false;
            spoutBig = false;
            curScale = 1f;
            curBlow = SpoutBlow;
            TakePushed(0);
        }

        protected override void OnEnd()
        {
            // 曲が終わったら、途中の演出は止める（大波が出ていたら、押し流した分はすぐ降らせる）
            if ((bigT >= 0f && waveT >= 0f) || finalePending || (spoutBig && spoutT >= 0f && !rainDone)) RainBack();
            finalePending = false;
            ReleaseSpoutRiders();
            ReleaseWaveRiders();
            spoutT = -1f;
            bigT = -1f;
            waveT = -1f;
        }

        protected override void Tick(float dt, bool running)
        {
            spoutPulse = Mathf.MoveTowards(spoutPulse, 0f, dt * 4f);
            if (windowTimer > 0f) windowTimer -= dt;

            if (spoutT >= 0f)
            {
                float before = spoutT;
                spoutT += dt;
                UpdateSpout(before);
                MoveSpoutRiders(dt);
                if (spoutT >= SpoutTotal)
                {
                    spoutT = -1f;
                    windowTimer = spoutWindow;
                }
            }
            if (bigT >= 0f)
            {
                float before = bigT;
                bigT += dt;
                UpdateBig(before);
                if (bigT >= BigTotal) bigT = -1f;
            }
            if (waveT >= 0f)
            {
                waveT += dt / bigWaveSeconds;
                PushWithWave();
                MoveWaveRiders(dt);
                if (waveT >= 1f)
                {
                    waveT = -1f;
                    ReleaseWaveRiders();
                    finalePending = true;
                    StartCoroutine(FinaleLater(1.2f));
                }
            }

            if (!running) return;
            if (bigT < 0f)
            {
                bigTimer -= dt;
                if (bigTimer <= 0f && spoutT < 0f && !finalePending)
                {
                    bigTimer = bigInterval;
                    StartBig();
                    return;
                }
                spoutTimer -= dt;
                if (spoutTimer <= 0f && spoutT < 0f && !finalePending)
                {
                    spoutTimer = spoutInterval;
                    StartSpout();
                }
            }
        }

        // ---- 潮吹き ----

        void StartSpout(bool big = false)
        {
            spoutBig = big;
            curScale = big ? bigSpoutScale : 1f;
            curBlow = big ? BigSpoutBlow : SpoutBlow;
            if (big)
            {
                // 大潮吹き：下の段のまん中
                holeX = 0f;
                holeDepth = (holeDepthMin + holeDepthMax) * 0.5f;
            }
            else
            {
                // 穴の場所：上の段の下の盤面（下の段）のどこか
                holeX = Random.Range(-1f, 1f) * HalfW * holeSideRange;
                holeDepth = Random.Range(Mathf.Min(holeDepthMin, holeDepthMax), Mathf.Max(holeDepthMin, holeDepthMax));
            }
            spoutT = 0f;
            // 穴の上のメダルを押しのける
            Blast(HolePos, D * smallWhaleSize * curScale * 1.2f, D * 2.5f, 0.3f);
            if (big)
            {
                BoardPop("大潮吹き！", Water, HolePos + Vector3.up * D * 4f, D * 1.1f);
                Speaker(1f, 12, Water);
            }
            Play(splashSound, ref madeSplash, () => RecordSynth.Noise("SeaSplash", 0.5f, 0.2f, 0.01f, 0.3f, 0.7f, 3), 0.6f);
        }

        void UpdateSpout(float before)
        {
            float blowStart = SpoutOpen + SpoutRise;
            float blowEnd = blowStart + curBlow;
            if (before < blowStart && spoutT >= blowStart)
            {
                Play(spoutSound, ref madeSpout, () => RecordSynth.Noise("SeaSpout", 1.8f, 0.15f, 0.05f, 0.8f, 0.9f, 4));
                if (spoutBig)
                {
                    // 大潮吹き：押し流して落ちた分＋bigMedals 枚を、下の段と上の段へ噴き出す
                    AddGauge(Mathf.Min(gaugeCap, waveCaught) * gaugePerMedal);
                    rainDone = true;
                    StartCoroutine(BigSpoutMedalsRoutine(Budget(TakePushed(pushedCap) + bigMedals), curBlow * 0.85f));
                }
                else
                {
                    WatchPushed(curBlow + spoutWindow + 1f);
                    // メダルが、噴水のてっぺんから次々に噴き出す
                    StartCoroutine(SpoutMedalsRoutine(Budget(spoutMedals), curBlow * 0.8f));
                }
            }
            if (spoutT >= blowStart && spoutT < blowEnd)
            {
                // 噴水：近くのメダルを巻き込んで、上へ飛ばす（0.25 秒ごと）
                if (Mathf.FloorToInt(spoutT * 4f) != Mathf.FloorToInt(before * 4f))
                {
                    spoutPulse = 1f;
                    foreach (var m in MedalsNear(HolePos, D * spoutRadius))
                    {
                        if (m.Body == null || m.Body.isKinematic) continue;
                        if (m.transform.position.y > LowerFloorY + Layout.pusherHeight * 0.6f) continue;   // 上の段のメダルは巻き込まない
                        if (spoutRiders.Count < spoutRideMax)
                        {
                            // 巻き込む：水の柱に乗せる
                            Hold(m);
                            spoutRiders.Add(new Rider { m = m, start = m.transform.position, phase = Random.Range(0f, 360f), height = Random.Range(0.35f, 1f), t = 0f });
                            continue;
                        }
                        // 乗りきれないメダルは、外へ少し押しのける
                        Vector3 d = m.transform.position - HolePos;
                        d.y = 0f;
                        Vector3 outDir = d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.back;
                        Push(m, (outDir + Vector3.up * 0.4f) * D * spoutPower * 0.3f);
                    }
                }
            }
            if (before < blowEnd && spoutT >= blowEnd) ReleaseSpoutRiders();
        }

        /// <summary>噴水のてっぺんから、メダルを count 枚、seconds 秒かけて噴き出す（穴のまわりに降る）</summary>
        System.Collections.IEnumerator SpoutMedalsRoutine(int count, float seconds)
        {
            float step = count > 0 ? seconds / count : 0f;
            for (int i = 0; i < count; i++)
            {
                var top = SpoutStart + Vector3.up * ColumnH * 0.9f;
                float a = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(0.3f, 1f) * spoutScatter;
                var v = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * D * r * 1.6f + Vector3.up * D * Random.Range(1f, 2.5f);
                SpawnFree(top, v);   // ふつうのメダル（黄色）
                Emit(spray, top, Foam, 4);
                if (step > 0f) yield return new WaitForSeconds(step);
            }
        }

        /// <summary>大潮吹き：潮のてっぺんから count 枚を、弧を描いて下の段と上の段のあちこちへ飛ばす</summary>
        System.Collections.IEnumerator BigSpoutMedalsRoutine(int count, float seconds)
        {
            float step = count > 0 ? seconds / count : 0f;
            for (int i = 0; i < count; i++)
            {
                var top = SpoutStart + Vector3.up * ColumnH * 0.9f;
                Vector3 target;
                if (Random.value < upperShare)
                {
                    // 上の段（プッシャーの上。プッシャーがいちばん引いた時の前より奥なら、いつも上の段の上）
                    float z = Random.Range(Layout.pusherRetractZ + D * 0.3f, BackZ - D * 0.3f);
                    target = new Vector3(Random.Range(-HalfW, HalfW) * 0.8f, UpperFloorY + D * 0.2f, z);
                }
                else
                {
                    float z = Mathf.Lerp(FrontZ, PusherFrontZ, Random.Range(0.15f, 0.85f));
                    target = new Vector3(Random.Range(-HalfW, HalfW) * 0.85f, LowerFloorY + D * 0.2f, z);
                }
                LaunchArc(top, target, Random.Range(0.8f, 1.2f));
                Emit(spray, top, Foam, 4);
                if (step > 0f) yield return new WaitForSeconds(step);
            }
        }

        /// <summary>水の柱に乗っているメダルを動かす：柱へ吸い寄せられ、回りながら上がっていく</summary>
        void MoveSpoutRiders(float dt)
        {
            var top = SpoutStart;
            for (int i = spoutRiders.Count - 1; i >= 0; i--)
            {
                var r = spoutRiders[i];
                if (r.m == null || !r.m.IsActiveInPool) { spoutRiders.RemoveAt(i); continue; }
                r.t += dt;
                float k = Ease.OutCubic(Mathf.Clamp01(r.t / 0.6f));
                float a = (r.phase + r.t * 500f) * Mathf.Deg2Rad;
                float rad = D * 0.35f;
                var orbit = top + new Vector3(Mathf.Cos(a) * rad, ColumnH * r.height + Mathf.Sin(r.t * 9f + r.phase) * D * 0.15f, Mathf.Sin(a) * rad);
                var p = Vector3.Lerp(r.start, orbit, k);
                var rot = Quaternion.Euler(r.t * 400f + r.phase, r.t * 300f, 0f);
                r.m.transform.SetPositionAndRotation(p, rot);
                r.m.Body.position = p;
                r.m.Body.rotation = rot;
            }
        }

        /// <summary>噴水が止まった：乗っていたメダルを周りへ散らす</summary>
        void ReleaseSpoutRiders()
        {
            foreach (var r in spoutRiders)
            {
                if (r.m == null) continue;
                Vector3 d = r.m.transform.position - HolePos;
                d.y = 0f;
                Vector3 outDir = d.sqrMagnitude > 0.0001f ? d.normalized : Random.insideUnitSphere;
                outDir.y = 0f;
                if (outDir.z > 0f) outDir.z *= 0.5f;
                Release(r.m, outDir.normalized * D * spoutPower * Random.Range(0.6f, 1.2f) + Vector3.up * D * 1.5f);
            }
            spoutRiders.Clear();
        }

        // ---- 大波 ----

        void StartBig()
        {
            bigT = 0f;
            rainDone = false;
            finalePending = false;
            touched.Clear();
            waveCaught = 0;
            Play(whaleSound, ref madeWhale, () => RecordSynth.Sweep("SeaWhale", 220f, 110f, 1.4f, 0.3f, RecordSynth.Wave.Triangle));
            BoardPop("大波！", Water, WallHolePos + new Vector3(0f, D * 2.5f, -D * 2f), D * 1.3f);
            Speaker(1f, 12, Water);
        }

        void UpdateBig(float before)
        {
            float tailStart = BigOpen + BigOut + BigTurn;
            float swing = tailStart + BigTail * 0.55f;   // 2回目の振り下ろしで大波
            if (before < swing && bigT >= swing)
            {
                waveT = 0f;
                WatchPushed(bigWaveSeconds + 2.5f);
                Play(waveSound, ref madeWave, () => RecordSynth.Noise("SeaWave", 2f, 0.1f, 0.15f, 1f, 1.1f, 2));
                Shake(0.05f, 0.6f);
                Emit(spray, bigWhale.position, Foam, 60);
            }
        }

        /// <summary>大波の頭が通った所のメダルを、手前へ押し流す（上の段も下の段も。1回の波で1枚1回）</summary>
        void PushWithWave()
        {
            float z = WaveZ;
            foreach (var m in BoardMedals)
            {
                if (m == null || m.Consumed || m.Body == null || m.Body.isKinematic) continue;
                var p = m.transform.position;
                if (p.y < LowerFloorY - D * 0.5f) continue;   // 穴に落ちかけている
                if (Mathf.Abs(p.z - z) > D * 0.6f || touched.Contains(m)) continue;
                touched.Add(m);
                waveCaught++;
                if (Random.value < 0.2f) Emit(spray, p, Foam, 3);
                bool upper = p.y > LowerFloorY + Layout.pusherHeight * 0.6f;
                if (upper && waveRiders.Count < waveRideMax)
                {
                    // 上の段：波に乗せる
                    Hold(m);
                    waveRiders.Add(new Rider { m = m, start = p, phase = Random.Range(0f, 360f), height = Random.value, t = 0f });
                    continue;
                }
                // 下の段：手前のふちに近いほど強く押す（奥は少しずれるだけ）
                float k = upper ? 1f : Mathf.InverseLerp(PusherFrontZ, FrontZ, p.z);
                float power = upper ? 1f : Mathf.Lerp(lowerPushMin, 1f, k * k);
                Push(m, new Vector3(Random.Range(-0.15f, 0.15f), 0.45f, -1f) * D * bigWavePush * power);
            }
        }

        /// <summary>波の頭の場所（波の上にメダルを乗せる所）</summary>
        Vector3 CrestPoint(float x)
        {
            float z = WaveZ;
            float baseY = z > PusherFrontZ ? UpperFloorY : LowerFloorY;
            return new Vector3(x, baseY + D * 1.1f, z - D * 0.1f);
        }

        /// <summary>波に乗っているメダルを、波の頭といっしょに運ぶ</summary>
        void MoveWaveRiders(float dt)
        {
            for (int i = waveRiders.Count - 1; i >= 0; i--)
            {
                var r = waveRiders[i];
                if (r.m == null || !r.m.IsActiveInPool) { waveRiders.RemoveAt(i); continue; }
                r.t += dt;
                float k = Ease.OutCubic(Mathf.Clamp01(r.t / 0.25f));
                var crest = CrestPoint(Mathf.Clamp(r.start.x, -HalfW * 0.9f, HalfW * 0.9f));
                crest += new Vector3(Mathf.Sin(r.t * 7f + r.phase) * D * 0.15f, r.height * D * 0.6f + Mathf.Sin(r.t * 11f + r.phase) * D * 0.08f, -r.height * D * 0.3f);
                var p = Vector3.Lerp(r.start, crest, k);
                var rot = Quaternion.Euler(r.t * 250f + r.phase, r.phase, r.t * 180f);
                r.m.transform.SetPositionAndRotation(p, rot);
                r.m.Body.position = p;
                r.m.Body.rotation = rot;
            }
        }

        /// <summary>波が手前のふちに着いた：乗っていたメダルを手前へ投げ出す</summary>
        void ReleaseWaveRiders()
        {
            foreach (var r in waveRiders)
                if (r.m != null) Release(r.m, new Vector3(Random.Range(-0.3f, 0.3f), 0.4f, -1.6f) * D * bigWavePush * 0.7f);
            waveRiders.Clear();
        }

        float WaveZ => Mathf.Lerp(BackZ - D * 0.3f, FrontZ - D * 0.2f, Mathf.Clamp01(waveT));

        /// <summary>大波のあと、少し待ってから大潮吹き（ふだんの潮吹きの途中なら、それを終わらせてから）</summary>
        System.Collections.IEnumerator FinaleLater(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (!finalePending) yield break;
            finalePending = false;
            if (spoutT >= 0f)
            {
                ReleaseSpoutRiders();
                spoutT = -1f;
            }
            StartSpout(true);
            spoutTimer = spoutInterval;
        }

        /// <summary>大波のあと：押し流して落ちた分と、bigMedals 枚のメダルを上から降らせる。ゲージも足す</summary>
        void RainBack()
        {
            if (rainDone) return;
            rainDone = true;
            AddGauge(Mathf.Min(gaugeCap, waveCaught) * gaugePerMedal);
            int n = Budget(TakePushed(pushedCap) + bigMedals);
            var center = new Vector3(0f, 0f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.55f));
            RainFromSky(n, 1.8f, center, HalfW * 0.8f, (PusherFrontZ - FrontZ) * 0.35f);   // ふつうのメダル（黄色）
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            NoteDropForPush();
            if (type != DropZoneType.Center || windowTimer <= 0f) return;
            AddPoints(waveBonus);
            BoardPop($"+{CenterPoints + waveBonus}", Water, new Vector3(medal.transform.position.x, D * 1.2f, FrontZ + D * 0.5f), D * 0.6f);
        }

        protected override bool KeepBoardVisible() => spoutT >= 0f || bigT >= 0f || waveT >= 0f || (spray != null && spray.particleCount > 0);

        // ================================================================
        // 見た目
        // ================================================================

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            for (int i = 0; i < caustics.Count; i++)
            {
                float x = Mathf.Sin(i * 2.3f + t * (0.3f + i * 0.03f)) * HalfW * 0.85f;
                float z = Mathf.Lerp(FrontZ, PusherFrontZ, 0.1f + 0.85f * Mathf.Repeat(i * 0.29f + t * 0.05f, 1f));
                caustics[i].position = new Vector3(x, floorY + D * 0.02f, z);
                float s = D * (0.8f + 0.4f * Mathf.Sin(t * 1.5f + i));
                caustics[i].localScale = new Vector3(s * 1.4f, D * 0.003f, s);
                SetGlow(causticMats[i], Color.Lerp(DeepWater, Water, 0.5f + 0.5f * Mathf.Sin(t * 2f + i)), (0.15f + 0.1f * Mathf.Sin(t * 3f + i * 1.7f)) * BoardShown);
                caustics[i].gameObject.SetActive(BoardShown > 0.01f);
            }
            if (seaLight != null)
            {
                seaLight.intensity = BoardShown * (1.2f + 0.3f * Mathf.Sin(t * 1.3f));
                seaLight.enabled = seaLight.intensity > 0.001f;
            }
            UpdateSpoutLook(t);
            UpdateBigLook(t);
            UpdateWaveLook(t);
        }

        void UpdateSpoutLook(float t)
        {
            bool on = spoutT >= 0f;
            floorHole.gameObject.SetActive(on);
            smallWhale.gameObject.SetActive(on);
            column.gameObject.SetActive(false);
            if (!on) return;

            float st = spoutT;
            float open = Mathf.Clamp01(st / SpoutOpen) * Mathf.Clamp01((SpoutTotal - st) / SpoutClose);
            floorHole.position = HolePos + Vector3.up * D * 0.03f;
            floorHole.localScale = new Vector3(Ease.OutBack(open) * curScale, 1f, Ease.OutBack(open) * curScale);

            // 顔を出す高さ：0（穴の下）→ 1（頭が出る）→ 0
            float rise = Mathf.Clamp01((st - SpoutOpen) / SpoutRise);
            float sink = Mathf.Clamp01((st - SpoutOpen - SpoutRise - curBlow) / SpoutSink);
            float h = Ease.OutBack(rise) * (1f - Ease.OutCubic(sink));
            float size = D * smallWhaleSize * curScale;
            var pos = HolePos + Vector3.up * (-size * 1.1f + size * 1.5f * h);
            smallWhale.SetPositionAndRotation(pos, Quaternion.Euler(smallWhaleTilt, 180f, Mathf.Sin(t * 6f) * 4f));   // こちら向きで、少し前かがみ（頭の穴が見える）
            smallWhale.localScale = Vector3.one * size;

            // 噴水
            float blowStart = SpoutOpen + SpoutRise;
            bool blowing = st >= blowStart && st < blowStart + curBlow;
            if (blowing)
            {
                var top = smallWhale.TransformPoint(new Vector3(0f, 0.55f, 0.55f));
                float k = Mathf.Clamp01((st - blowStart) / 0.3f) * Mathf.Clamp01((blowStart + curBlow - st) / 0.3f);
                float colH = ColumnH * k * (0.9f + 0.1f * Mathf.Sin(t * 20f));
                column.gameObject.SetActive(true);
                column.position = top + Vector3.up * colH * 0.5f;
                column.rotation = Quaternion.identity;
                float cw = D * 0.4f * curScale * (1f + 0.3f * spoutPulse);
                column.localScale = new Vector3(cw, colH * 0.5f, cw);
                columnMat.color = new Color(0.7f, 0.9f, 1f, 0.45f * k);
                Emit(spray, top + Vector3.up * colH, Foam, 6);
                if (Random.value < 0.5f) Emit(spray, top + Vector3.up * colH * Random.value, Water, 2);
            }
        }

        void UpdateBigLook(float t)
        {
            bool on = bigT >= 0f;
            wallHole.gameObject.SetActive(on);
            bigWhale.gameObject.SetActive(on);
            if (!on) return;

            float bt = bigT;
            float open = Mathf.Clamp01(bt / BigOpen) * Mathf.Clamp01((BigTotal - bt) / BigClose);
            wallHole.position = WallHolePos;
            wallHole.rotation = Quaternion.Euler(0f, 0f, t * 60f);   // 輪がゆっくり回る
            float rs = Mathf.Max(0.001f, Ease.OutBack(open));
            wallHole.localScale = new Vector3(rs, rs, 1f);
            SetGlow(ringMat, Water, 1.5f + Mathf.Sin(t * 8f) * 0.8f);

            float size = D * bigWhaleSize;
            float outK = Ease.OutCubic(Mathf.Clamp01((bt - BigOpen) / BigOut));
            float backStart = BigOpen + BigOut + BigTurn + BigTail + Mathf.Max(0f, bigWaveSeconds - BigBack);
            float backK = Ease.OutCubic(Mathf.Clamp01((bt - backStart) / BigBack));
            float turnK = Ease.OutCubic(Mathf.Clamp01((bt - BigOpen - BigOut) / BigTurn));
            // 輪の後ろ（暗い面にかくれる所）から、輪をくぐって手前へ出てくる
            float hidden = size * 1.4f, outFront = size * 1.6f;
            float outDist = -hidden + (hidden + outFront) * outK * (1f - backK);
            var pos = WallHolePos + new Vector3(0f, Mathf.Sin(t * 2f) * D * 0.1f, -outDist);
            // こちら向き（yaw 180）→ 後ろ向き（yaw 0）。もどる時はまたこちら向きで穴へ下がる
            float yaw = Mathf.Lerp(180f, 0f, turnK);
            if (backK > 0f) yaw = Mathf.Lerp(0f, 180f, backK);
            bigWhale.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            bigWhale.localScale = Vector3.one * size;

            // 尻尾：後ろを向いてから、上下に2回大きく振る
            float tailStart = BigOpen + BigOut + BigTurn;
            float tk = Mathf.Clamp01((bt - tailStart) / BigTail);
            float swing = tk > 0f && tk < 1f ? Mathf.Sin(tk * Mathf.PI * 2f) * 55f : Mathf.Sin(t * 3f) * 10f;
            bigTail.localRotation = Quaternion.Euler(swing, 0f, 0f);
        }

        void UpdateWaveLook(float t)
        {
            bool on = waveT >= 0f;
            waveBody.gameObject.SetActive(on);
            if (!on) return;
            float z = WaveZ;
            float baseY = z > PusherFrontZ ? UpperFloorY : LowerFloorY;   // 上の段の上では高く、下の段に落ちたら床の高さ
            float k = Mathf.Clamp01(waveT * 4f) * Mathf.Clamp01((1f - waveT) * 6f + 0.2f);
            waveBody.position = new Vector3(0f, baseY + D * 0.7f * k + Mathf.Sin(t * 10f) * D * 0.05f, z);
            waveBody.localScale = new Vector3(1f, Mathf.Max(0.01f, k), 1f);
            SetGlow(crestMat, Foam, 1.5f + Mathf.Sin(t * 15f) * 0.5f);
            if (Random.value < 0.8f) Emit(spray, new Vector3(Random.Range(-HalfW, HalfW), baseY + D * 1.2f, z), Foam, 3);
        }
    }
}
