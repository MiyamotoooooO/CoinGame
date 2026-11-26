using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N019 天気予報「198X ウェザーニュース」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面の演出：右の発射口のそばに、大きめの天気予報モニターとお天気お姉さんが出てきて、今の天気を伝える。
    ///   曲が終わりそうになると、手をふって引っこむ
    /// ・天気は changeSeconds 秒ごとにランダムで変わる（同じ天気は続けて出ない。コインの雨はめったに出ない）
    ///   晴れ　　：画面の上に太陽と少しの雲。中央の穴のスコア ×sunnyMultiplier
    ///   雨　　　：画面の上に雨雲。雨が降り、盤面がぬれて水たまりができる。メダルがすべりやすい
    ///   雪　　　：雪が降って盤面に積もる。降り始めて snowStopDelay 秒後にプッシャーが凍って止まる。
    ///             天気が変わる時に、積もった雪が爆発してメダルをはじく
    ///   雷雨　　：雨雲より暗い雲。雨・水たまり・すべりやすいのは雨と同じ。雷が落ちて爆発し、メダルが吹き飛ぶ
    ///   台風　　：巨大な竜巻が盤面を回る。ふれたメダルは巻き込まれていっしょに回る。最後は中央の穴へ落ちていく
    ///   コインの雨：画面の上に黄色い雲。空からメダルが降ってくる
    ///
    /// 見た目と音はコードで作る。「差し替え用」に入れると、そちらの音を使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// 物理の変更（すべりやすさ・プッシャーの停止）は、効果が終わると元に戻る。
    /// 大きな演出（WeatherSpectacle）は、この盤面の演出と重なるので出さない（RecordSpectacleDirector の skipKeys に入れる）。
    /// </summary>
    [DisallowMultipleComponent]
    public class WeatherRecord : RecordScriptBase
    {
        public static WeatherRecord Instance { get; private set; }

        public enum Kind { Sunny, Rain, Snow, Storm, Typhoon, CoinRain }
        const int KindCount = 6;

        [Header("天気の切り替わり")]
        [Tooltip("天気が変わるまでの秒数")]
        [Range(3f, 60f)] public float changeSeconds = 10f;
        [Tooltip("抽選の出やすさ（晴れ・雨・雪・雷雨・台風・コインの雨の順）。大きいほど出やすい。同じ天気は続けて出ない")]
        public float[] weights = { 1f, 1f, 1f, 1f, 1f, 0.3f };

        [Header("晴れ")]
        [Tooltip("中央の穴に落ちた時のスコアの倍率")]
        [Range(1f, 5f)] public float sunnyMultiplier = 1.5f;

        [Header("雨・雷雨")]
        [Tooltip("メダルのすべりにくさの倍率（小さいほどすべる）")]
        [Range(0.05f, 1f)] public float rainFriction = 0.35f;
        [Tooltip("水たまりの数")]
        [Range(0, 12)] public int puddleCount = 7;

        [Header("雪")]
        [Tooltip("雪が降り始めてから、プッシャーが止まるまでの秒数")]
        [Range(0f, 10f)] public float snowStopDelay = 3f;
        [Tooltip("雪がいっぱいに積もるまでの秒数")]
        [Range(1f, 30f)] public float snowFillSeconds = 7f;
        [Tooltip("積もった雪の山の数")]
        [Range(0, 24)] public int snowMoundCount = 14;
        [Tooltip("積もった雪の上のメダルのすべりにくさ（大きいほど、雪の上でメダルが止まる）")]
        [Range(0f, 30f)] public float snowGrip = 12f;
        [Tooltip("積もった雪が爆発した時に、メダルをはじく強さ")]
        [Range(0f, 20f)] public float snowBlastStrength = 3f;

        [Header("雷雨")]
        [Tooltip("雷が落ちる間かくの秒数（いちばん短い）")]
        [Range(0.3f, 5f)] public float lightningMin = 0.9f;
        [Tooltip("雷が落ちる間かくの秒数（いちばん長い）")]
        [Range(0.3f, 8f)] public float lightningMax = 2f;
        [Tooltip("雷の爆発でメダルを吹き飛ばす強さ")]
        [Range(0f, 30f)] public float lightningStrength = 9f;
        [Tooltip("雷の爆発の広さ（メダルの直径の何倍か）")]
        [Range(0.5f, 6f)] public float lightningRadius = 2.6f;

        [Header("台風")]
        [Tooltip("竜巻がメダルを巻き込む広さ（メダルの直径の何倍か）")]
        [Range(0.5f, 8f)] public float tornadoRadius = 3.4f;
        [Tooltip("巻き込んだメダルが回る速さ（メダルの直径／秒）")]
        [Range(0f, 30f)] public float tornadoSpin = 12f;
        [Tooltip("竜巻の見た目の大きさ（1 が元の大きさ）")]
        [Range(0.5f, 4f)] public float tornadoSize = 2.2f;
        [Tooltip("竜巻のまわりで、メダルを吸い寄せる広さ（巻き込む広さの何倍か）")]
        [Range(1f, 3f)] public float tornadoPullRange = 1.7f;
        [Tooltip("竜巻が盤面を1周する秒数")]
        [Range(2f, 20f)] public float tornadoLapSeconds = 6f;
        [Tooltip("いちどに巻き込むメダルの最大の枚数")]
        [Range(1, 200)] public int tornadoMaxMedals = 80;
        [Tooltip("台風が終わる何秒前から、竜巻が中央の穴へ向かうか")]
        [Range(0.5f, 6f)] public float tornadoSinkSeconds = 2.5f;

        [Header("コインの雨")]
        [Tooltip("コインの雨の間に降るメダルの枚数")]
        [Range(0, 80)] public int coinRainCount = 24;
        [Tooltip("盤面のメダルがこの枚数より多い時は降らせない")]
        [Range(50, 600)] public int medalCap = 260;

        [Header("お天気お姉さんとモニター")]
        [Tooltip("お姉さんの立つ場所（右の発射口からのずれ。メダルの直径が 1）")]
        public Vector3 casterOffset = new Vector3(-0.4f, 0.35f, -0.4f);
        [Tooltip("天気予報モニターのまん中の場所（右の発射口からのずれ。メダルの直径が 1）。上からワイヤーでつるす")]
        public Vector3 monitorOffset = new Vector3(-3.1f, 3f, -0.1f);
        [Tooltip("モニターを横に向ける角度（マイナスでお姉さんの方を向く）")]
        [Range(-60f, 60f)] public float monitorYaw = -10f;
        [Tooltip("お姉さんとモニターを、ステージのモニター（番組の画面）より手前に描く")]
        public bool drawInFront = true;
        [Tooltip("お姉さんの大きさ")]
        [Range(0.3f, 3f)] public float casterScale = 1.1f;
        [Tooltip("モニターの大きさ")]
        [Range(0.3f, 3f)] public float monitorScale = 1.3f;
        [Tooltip("曲が終わる何秒前に撤収するか")]
        [Range(0.5f, 10f)] public float withdrawSeconds = 3.5f;

        [Header("空（画面の上）")]
        [Tooltip("空の雲や太陽を置く高さ（画面の下が 0、上が 1）")]
        [Range(0.5f, 1f)] public float skyViewportY = 0.86f;
        [Tooltip("空を置く奥行き（カメラから盤面までのきょりの何倍か）")]
        [Range(0.2f, 1.2f)] public float skyDepth = 0.6f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip chimeSound;
        public AudioClip thunderSound;
        public AudioClip snowBurstSound;
        public AudioClip freezeSound;
        public AudioClip windSound;
        public AudioClip coinSound;

        protected override string DefaultKey => "Weather";
        protected override string ScreenTitle => "WEATHER";
        protected override string ScreenSubtitle => "198X ウェザーニュース";
        protected override float RecordSoundVolume => volume;
        /// <summary>ステージのモニターは、いつもの画面のまま（天気予報は盤面のモニターとお姉さんが伝える）</summary>
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<WeatherRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed()
        {
            if (Instance == this) Instance = null;
            if (overlayTexture != null) { overlayTexture.Release(); Destroy(overlayTexture); }
        }

        // ================================================================
        // 天気ごとの名前・色
        // ================================================================

        static readonly string[] KindNames = { "晴れ", "雨", "雪", "雷雨", "台風", "コインの雨" };
        static readonly string[] KindEnglish = { "SUNNY", "RAIN", "SNOW", "THUNDER", "TYPHOON", "COIN RAIN" };
        static readonly Color[] KindColors =
        {
            new Color32(0xff, 0xb8, 0x3a, 0xff), new Color32(0x4f, 0x9c, 0xf7, 0xff), new Color32(0xd8, 0xee, 0xff, 0xff),
            new Color32(0xc9, 0x8c, 0xff, 0xff), new Color32(0x7f, 0xe0, 0xc8, 0xff), new Color32(0xff, 0xd2, 0x3a, 0xff),
        };
        /// <summary>空の雲の色（晴れは使わない）</summary>
        static readonly Color[] CloudColors =
        {
            new Color32(0xf4, 0xf6, 0xfa, 0xff), new Color32(0x86, 0x8f, 0xa0, 0xff), new Color32(0xd4, 0xdb, 0xe6, 0xff),
            new Color32(0x30, 0x32, 0x40, 0xff), new Color32(0x62, 0x6a, 0x76, 0xff), new Color32(0xff, 0xc8, 0x20, 0xff),
        };
        static readonly float[] CloudGlow = { 0.6f, 0.45f, 0.6f, 0.25f, 0.4f, 1.5f };
        /// <summary>天気予報モニターの画面の色</summary>
        static readonly Color[] TvColors =
        {
            // 白い文字が読みやすいように、どれも暗めの色にする
            new Color32(0x16, 0x4a, 0x96, 0xff), new Color32(0x10, 0x22, 0x48, 0xff), new Color32(0x26, 0x42, 0x64, 0xff),
            new Color32(0x18, 0x0e, 0x2e, 0xff), new Color32(0x0e, 0x36, 0x34, 0xff), new Color32(0x46, 0x2c, 0x04, 0xff),
        };
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Studio = new Color32(0x10, 0x1c, 0x34, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3a, 0xff);
        static readonly Color BoltColor = new Color32(0xc8, 0xd8, 0xff, 0xff);

        // ================================================================
        // 状態
        // ================================================================

        Kind now;
        float timer;              // 次に天気が変わるまでの残り秒数
        float kindTime;           // 今の天気になってからの秒数
        bool snowFrozen;          // 雪でプッシャーが止まったか
        float lightningTimer;
        int coinSpawned;
        float coinTimer;
        readonly List<Kind> history = new List<Kind>();

        float castTarget, castShown;   // お姉さんとモニターの出ぐあい（0 で隠れる）
        float castTalk;                // 天気が変わった時の身ぶり（1 → 0）
        float sunShown, bandShown, smallCloudShown;
        Color bandColor = new Color(0.8f, 0.8f, 0.85f);
        float bandGlow = 0.5f;
        float skyFlash;                // 雷で雲が光る（1 → 0）
        float wetness;                 // 盤面のぬれぐあい（0〜1）
        float snowDepth;               // 積もった雪（0〜1）
        float splashAcc, sparkleAcc;

        // 台風
        bool tornadoActive;
        float tornadoAngle, tornadoSink, tornadoShown;
        Vector3 tornadoPos, tornadoVel;
        bool tornadoSinkAnnounced;
        readonly List<Medal> near = new List<Medal>();

        // 雷
        float boltTime;
        Vector3 boltPos, boltTop;
        float ringTime = -1f;

        // ================================================================
        // 盤面の部品
        // ================================================================

        Transform sky, sunGroup, sunRays, band;
        readonly List<Transform> smallClouds = new List<Transform>();
        readonly List<Transform> bandPuffs = new List<Transform>();
        readonly List<Vector3> bandBase = new List<Vector3>();
        Material bandMat, sunHaloMat;
        float skyHalfW = 8f;

        ParticleSystem rain, splash, snow, snowBurst, frost, sparks, smoke, sparkle, debris, shine;
        Light sunLight, boltLight, flashLight;
        LineRenderer boltCore, boltGlow, boltGlowPurple;
        readonly List<LineRenderer> boltBranches = new List<LineRenderer>();
        readonly List<LineRenderer> groundArcs = new List<LineRenderer>();
        float arcTime;
        static readonly Color ZapYellow = new Color(1f, 0.9f, 0.22f, 1f);
        static readonly Color ZapPurple = new Color(0.72f, 0.3f, 1f, 1f);
        readonly List<Medal> gripList = new List<Medal>();

        Transform wetSheet;
        Material wetMat;
        readonly List<Transform> puddles = new List<Transform>();
        readonly List<Transform> ripples = new List<Transform>();
        readonly List<float> puddleSize = new List<float>();
        Material puddleMat, rippleMat;

        Transform snowSheet;
        Material snowSheetMat;
        readonly List<Transform> mounds = new List<Transform>();
        readonly List<float> moundSize = new List<float>();

        Transform scorch;
        Material scorchMat;

        Transform tornado, funnelOuter, funnelInner, tornadoBase;
        Material funnelOuterMat, funnelInnerMat, tornadoBaseMat;

        // お姉さんとモニター
        Transform caster, casterBody, casterHead, pointerArm, waveArm;
        Transform tv, tvIconRoot;
        Material tvScreenMat, tvBezelMat, tvLampMatA, tvLampMatB;
        readonly Transform[] tvIcons = new Transform[KindCount];
        Label3D tvName, tvEnglish, tvTimer, tvNote;
        Light casterLight;
        Transform sunIconRays, typhoonIconSpin;

        struct Faller { public Transform t; public Vector3 basePos; public Quaternion baseRot; public Vector3 spinAxis; public float span, speed, offset; public bool spin; }
        readonly List<Faller> fallers = new List<Faller>();

        AudioClip madeChime, madeThunder, madeSnowBurst, madeFreeze, madeWind, madeCoin, madeSink, madeAppear;
        int coinSoundCount;

        static Font uiFont;
        static Font UiFont => uiFont != null ? uiFont : (uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        Material textMaterial;

        /// <summary>
        /// モニターの文字の素材。ふつうの文字の素材は何よりも手前に描かれるので、
        /// 前にある物（お姉さんなど）にかくれるように、奥行きを比べる設定にしたコピーを使う
        /// </summary>
        Material TextMaterial
        {
            get
            {
                if (textMaterial != null || UiFont == null) return textMaterial;
                textMaterial = new Material(UiFont.material) { name = "WeatherTextMaterial" };
                textMaterial.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                textMaterial.renderQueue = 3101;   // ふちどりより後に描く
                return textMaterial;
            }
        }

        Material outlineMaterial;

        /// <summary>文字のふちどりの素材（本体の文字より先に描く）</summary>
        Material OutlineMaterial
        {
            get
            {
                if (outlineMaterial != null || UiFont == null) return outlineMaterial;
                outlineMaterial = new Material(TextMaterial) { name = "WeatherTextOutline", renderQueue = 3100 };
                return outlineMaterial;
            }
        }

        // ================================================================
        // 作る
        // ================================================================

        protected override void BuildBoard()
        {
            EnsureSkipSpectacle();
            BuildSky();
            BuildWeatherParticles();
            BuildWetFloor();
            BuildSnowFloor();
            BuildLightning();
            BuildTornado();
            BuildCaster();
            BuildTv();
            int layer = PickOverlayLayer();
            if (layer >= 0)
            {
                SetLayer(caster, layer);
                SetLayer(tv, layer);
                overlayLayer = layer;
            }
        }

        /// <summary>大きな演出（WeatherSpectacle）を出さないようにする（この盤面の演出と重なるため）</summary>
        void EnsureSkipSpectacle()
        {
            var dir = RecordSpectacleDirector.Instance;
            if (dir == null) dir = FindFirstObjectByType<RecordSpectacleDirector>();
            if (dir == null) return;
            if (dir.skipKeys == null) dir.skipKeys = new List<string>();
            if (!dir.skipKeys.Contains(recordKey)) dir.skipKeys.Add(recordKey);
        }

        Transform Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 size, Material mat, Vector3 euler = default)
        {
            var go = Shape(type, name, parent, pos, size, mat);
            go.transform.localRotation = Quaternion.Euler(euler);
            return go.transform;
        }

        static Transform Group(string name, Transform parent, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go.transform;
        }

        // ---- 空（画面の上。カメラの前に置く。座標は「画面の高さが 10、上のふちが 5」） ----

        void BuildSky()
        {
            sky = Group("Sky", Board, Vector3.zero);

            // 太陽
            sunGroup = Group("Sun", sky, new Vector3(-4f, 6.5f, 0f));
            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(halo.GetComponent<Collider>());
            halo.name = "Halo";
            halo.transform.SetParent(sunGroup, false);
            halo.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            halo.transform.localScale = Vector3.one * 5.2f;
            sunHaloMat = RecordMeshKit.Additive(RecordMeshKit.SoftDot, new Color(1f, 0.75f, 0.3f, 0.8f));
            halo.GetComponent<Renderer>().sharedMaterial = sunHaloMat;
            RecordMeshKit.NoShadow(halo);
            Part(PrimitiveType.Sphere, "Core", sunGroup, Vector3.zero, Vector3.one * 2.1f, Glow(new Color32(0xff, 0xc2, 0x3a, 0xff), 2.2f));
            Part(PrimitiveType.Sphere, "Inner", sunGroup, new Vector3(-0.25f, 0.25f, -0.6f), Vector3.one * 1.1f, Glow(new Color32(0xff, 0xf0, 0xa0, 0xff), 2.4f));
            sunRays = Group("Rays", sunGroup, Vector3.zero);
            var rayMat = Glow(new Color32(0xff, 0xd8, 0x4a, 0xff), 2.5f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f;
                var pivot = Group("Ray" + i, sunRays, Vector3.zero);
                pivot.localRotation = Quaternion.Euler(0f, 0f, a);
                Part(PrimitiveType.Cube, "Beam", pivot, new Vector3(0f, 1.65f, 0.1f), new Vector3(0.22f, i % 2 == 0 ? 0.9f : 0.6f, 0.1f), rayMat);
            }

            // 晴れの小さな雲（2つ）
            var white = Glow(CloudColors[0], CloudGlow[0]);
            for (int c = 0; c < 2; c++)
            {
                var g = Group("SmallCloud" + c, sky, new Vector3(c == 0 ? 1.5f : 5f, 6.5f, 0.2f));
                MakeCloudPuffs(g, 1.4f + c * 0.3f, white, 5, c * 7 + 3);
                smallClouds.Add(g);
            }

            // 雲の帯（雨・雪・雷雨・台風・コインの雨）
            band = Group("Band", sky, new Vector3(0f, 6.5f, 0.4f));
            bandMat = Glow(CloudColors[1], CloudGlow[1]);
            var rnd = new System.Random(19);
            for (int i = 0; i < 26; i++)
            {
                float x = -11f + i * 0.88f + (float)rnd.NextDouble() * 0.4f;
                float y = (float)rnd.NextDouble() * 0.7f - (i % 2) * 0.35f;
                float s = 1.4f + (float)rnd.NextDouble() * 1.1f;
                var p = Part(PrimitiveType.Sphere, "Puff" + i, band, new Vector3(x, y, (float)rnd.NextDouble() * 0.6f), new Vector3(s * 1.25f, s, s), bandMat);
                bandPuffs.Add(p);
                bandBase.Add(p.localPosition);
            }
        }

        void MakeCloudPuffs(Transform parent, float size, Material mat, int count, int seed)
        {
            var rnd = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float x = (i - (count - 1) * 0.5f) * size * 0.45f;
                float y = (i == count / 2 ? 0.3f : 0f) * size + (float)rnd.NextDouble() * 0.15f * size;
                float s = size * (i == count / 2 ? 0.95f : 0.6f + (float)rnd.NextDouble() * 0.2f);
                Part(PrimitiveType.Sphere, "Puff" + i, parent, new Vector3(x, y, (float)rnd.NextDouble() * 0.2f), new Vector3(s * 1.2f, s, s), mat);
            }
        }

        // ---- 雨・雪などの粒 ----

        void BuildWeatherParticles()
        {
            rain = Particles("Rain", Board, Vector3.down, D * 24f, D * 0.05f, 0.4f, 0f, 0f);
            SetupSheet(rain, 2500);
            var rr = rain.GetComponent<ParticleSystemRenderer>();
            rr.renderMode = ParticleSystemRenderMode.Stretch;
            rr.velocityScale = 0.035f;
            rr.lengthScale = 1.5f;
            var rn = rain.noise;
            rn.enabled = false;
            var rmain = rain.main;
            rmain.startColor = new Color(0.75f, 0.85f, 1f, 0.55f);

            snow = Particles("Snow", Board, Vector3.down, D * 1.8f, D * 0.11f, 4f, 0f, 0f);
            SetupSheet(snow, 1500);
            var smain = snow.main;
            smain.startColor = new Color(1f, 1f, 1f, 0.95f);

            splash = Particles("Splash", Board, Vector3.up, D * 1.6f, D * 0.07f, 0.35f, 1.2f, 55f);
            snowBurst = Particles("SnowBurst", Board, Vector3.up, D * 6f, D * 0.28f, 1.3f, 0.7f, 70f);
            var sb = snowBurst.main;
            sb.maxParticles = 1500;
            frost = Particles("Frost", Board, Vector3.up, D * 4f, D * 0.12f, 0.9f, 0.4f, 80f);
            sparks = Particles("Sparks", Board, Vector3.up, D * 4f, D * 0.08f, 0.6f, 1.2f, 50f);
            var sp = sparks.main;
            sp.maxParticles = 1000;
            smoke = Particles("Smoke", Board, Vector3.up, D * 1.2f, D * 0.7f, 1.4f, -0.05f, 40f);
            sparkle = Particles("Sparkle", Board, Vector3.down, D * 1.2f, D * 0.1f, 1.6f, 0.15f, 10f);
            sparkle.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var sk = sparkle.shape;
            sk.shapeType = ParticleSystemShapeType.Box;
            sk.scale = new Vector3(Layout.boardWidth, Mathf.Max(0.1f, PusherFrontZ - FrontZ), 0.05f);
            shine = Particles("Shine", Board, Vector3.up, D * 0.6f, D * 0.14f, 0.6f, 0f, 30f);
            debris = Particles("Debris", Board, Vector3.up, D * 3f, D * 0.1f, 1.3f, -0.15f, 35f);
            var dm = debris.main;
            dm.maxParticles = 800;
            var ds = debris.shape;
            ds.radius = D * 0.8f;

            float midZ = (FrontZ + BackZ) * 0.5f;
            sunLight = PointLight("SunLight", Board, new Vector3(0f, Layout.wallHeight * 3f, midZ), new Color(1f, 0.85f, 0.6f), Layout.boardWidth * 1.6f);
        }

        /// <summary>雨・雪を盤面の上いっぱいから降らせる入れ物にする</summary>
        void SetupSheet(ParticleSystem ps, int max)
        {
            // 前が下、上が奥（+Z）。こうすると、箱の X が盤面の横、Y が盤面の奥行きになる
            ps.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.maxParticles = max;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Layout.boardWidth * 1.15f, (BackZ - FrontZ) * 1.25f, 0.05f);
        }

        // ---- ぬれた盤面・水たまり ----

        void BuildWetFloor()
        {
            float len = PusherFrontZ - FrontZ;
            float midZ = (FrontZ + PusherFrontZ) * 0.5f;
            wetMat = RecordMeshKit.Glass(new Color(0.22f, 0.36f, 0.55f, 0f), 0.98f, 0.1f);
            var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(sheet.GetComponent<Collider>());
            sheet.name = "WetSheet";
            wetSheet = sheet.transform;
            wetSheet.SetParent(Board, false);
            wetSheet.localPosition = new Vector3(0f, D * 0.42f, midZ);
            wetSheet.localRotation = Quaternion.Euler(90f, 0f, 0f);
            wetSheet.localScale = new Vector3(Layout.boardWidth * 0.98f, len, 1f);
            sheet.GetComponent<Renderer>().sharedMaterial = wetMat;
            RecordMeshKit.NoShadow(sheet);

            puddleMat = RecordMeshKit.Glass(new Color(0.32f, 0.45f, 0.62f, 0.32f), 0.99f, 0.15f);
            rippleMat = RecordMeshKit.Additive(RecordMeshKit.Ripple, new Color(0.7f, 0.85f, 1f, 0.8f));
            var ringMesh = RecordMeshKit.Annulus(0.1f, 0.5f, 48, 4);
            for (int i = 0; i < puddleCount; i++)
            {
                var p = Part(PrimitiveType.Cylinder, "Puddle" + i, Board, Vector3.zero, Vector3.zero, puddleMat);
                puddles.Add(p);
                puddleSize.Add(0f);
                var r = RecordMeshKit.Make("Ripple" + i, ringMesh, rippleMat, Board, Vector3.zero, Vector3.zero, Vector3.zero);
                ripples.Add(r);
            }
            PlacePuddles();
        }

        void PlacePuddles()
        {
            for (int i = 0; i < puddles.Count; i++)
            {
                var p = RandomFloorPoint(0.12f, 0.88f, 0.8f);
                p.y = D * 0.45f;
                puddles[i].position = p;
                puddles[i].localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                puddleSize[i] = Random.Range(1.1f, 2.1f);
                ripples[i].position = p + Vector3.up * D * 0.02f;
            }
        }

        // ---- 積もる雪 ----

        void BuildSnowFloor()
        {
            float len = PusherFrontZ - FrontZ;
            float midZ = (FrontZ + PusherFrontZ) * 0.5f;
            snowSheetMat = RecordMeshKit.AlphaBlend(null, new Color(0.96f, 0.98f, 1f, 0f));
            var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(sheet.GetComponent<Collider>());
            sheet.name = "SnowSheet";
            snowSheet = sheet.transform;
            snowSheet.SetParent(Board, false);
            snowSheet.localPosition = new Vector3(0f, D * 0.5f, midZ);
            snowSheet.localRotation = Quaternion.Euler(90f, 0f, 0f);
            snowSheet.localScale = new Vector3(Layout.boardWidth * 0.98f, len, 1f);
            sheet.GetComponent<Renderer>().sharedMaterial = snowSheetMat;
            RecordMeshKit.NoShadow(sheet);

            var moundMat = Glow(new Color32(0xf4, 0xf8, 0xff, 0xff), 0.35f);
            for (int i = 0; i < snowMoundCount; i++)
            {
                mounds.Add(Part(PrimitiveType.Sphere, "Mound" + i, Board, Vector3.zero, Vector3.zero, moundMat));
                moundSize.Add(1f);
            }
            PlaceMounds();
        }

        void PlaceMounds()
        {
            for (int i = 0; i < mounds.Count; i++)
            {
                var p = RandomFloorPoint(0.08f, 0.92f, 0.85f);
                p.y = D * 0.42f;
                mounds[i].position = p;
                mounds[i].localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                moundSize[i] = Random.Range(0.9f, 1.7f);
            }
        }

        // ---- 雷 ----

        void BuildLightning()
        {
            // 黄色と紫のビリビリ：紫の太い光・黄色の光・白い芯を重ねる
            boltGlowPurple = MakeLine("BoltGlowPurple", D * 0.8f, new Color(ZapPurple.r, ZapPurple.g, ZapPurple.b, 0.5f));
            boltGlow = MakeLine("BoltGlow", D * 0.4f, new Color(ZapYellow.r, ZapYellow.g, ZapYellow.b, 0.8f));
            boltCore = MakeLine("BoltCore", D * 0.1f, new Color(1f, 1f, 0.92f, 1f));
            for (int i = 0; i < 4; i++)
            {
                Color c = i % 2 == 0 ? ZapYellow : ZapPurple;
                boltBranches.Add(MakeLine("BoltBranch" + i, D * (i % 2 == 0 ? 0.09f : 0.14f), new Color(c.r, c.g, c.b, 0.9f)));
            }
            // 落ちた所の床を走る電気
            for (int i = 0; i < 8; i++)
            {
                Color c = i % 2 == 0 ? ZapYellow : ZapPurple;
                groundArcs.Add(MakeLine("GroundArc" + i, D * (i % 2 == 0 ? 0.07f : 0.11f), new Color(c.r, c.g, c.b, 0.95f)));
            }
            boltLight = PointLight("BoltLight", Board, Vector3.zero, BoltColor, Layout.boardWidth * 0.9f);
            flashLight = PointLight("FlashLight", Board, new Vector3(0f, Layout.wallHeight * 5f, (FrontZ + BackZ) * 0.5f), new Color(0.8f, 0.85f, 1f), Layout.boardWidth * 3f);
            scorchMat = RecordMeshKit.Additive(RecordMeshKit.Ripple, BoltColor);
            scorch = RecordMeshKit.Make("Scorch", RecordMeshKit.Annulus(0.05f, 0.5f, 48, 4), scorchMat, Board, Vector3.zero, Vector3.zero, Vector3.zero);
        }

        LineRenderer MakeLine(string name, float width, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Board, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = RecordMeshKit.Additive(null, color);
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 1;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 0;
            lr.enabled = false;
            return lr;
        }

        // ---- 竜巻 ----

        void BuildTornado()
        {
            tornado = Group("Tornado", Board, Vector3.zero);
            float h = D * 5.5f;
            funnelOuterMat = RecordMeshKit.AlphaBlend(RecordMeshKit.WaterStreaks, new Color(0.7f, 0.74f, 0.78f, 0.9f));
            funnelOuter = RecordMeshKit.Make("FunnelOuter", RecordMeshKit.Funnel(D * 0.35f, D * 2.1f, h, 1.3f), funnelOuterMat, tornado, Vector3.zero, Vector3.one, Vector3.zero);
            funnelInnerMat = RecordMeshKit.Additive(RecordMeshKit.WaterStreaks, new Color(0.55f, 0.75f, 0.85f, 0.5f));
            funnelInner = RecordMeshKit.Make("FunnelInner", RecordMeshKit.Funnel(D * 0.2f, D * 1.4f, h * 0.92f, -1.7f), funnelInnerMat, tornado, Vector3.zero, Vector3.one, Vector3.zero);
            tornadoBaseMat = RecordMeshKit.AlphaBlend(RecordMeshKit.Whirlpool, new Color(0.62f, 0.6f, 0.55f, 0.85f));
            tornadoBase = RecordMeshKit.Make("Base", RecordMeshKit.Annulus(D * 0.3f, D * 2.3f, 64, 6), tornadoBaseMat, tornado, new Vector3(0f, D * 0.35f, 0f), Vector3.one, Vector3.zero);
            tornado.gameObject.SetActive(false);
        }

        // ---- お天気お姉さん（座標はメダルの直径が 1。足元が 0、正面は -Z（カメラの方）） ----

        void BuildCaster()
        {
            caster = Group("WeatherCaster", Board, Vector3.zero);
            casterBody = Group("Body", caster, Vector3.zero);

            var skin = Glow(new Color32(0xff, 0xdc, 0xc4, 0xff), 0.25f);
            var hair = Glow(new Color32(0x4a, 0x2a, 0x1c, 0xff), 0.12f);
            var blazer = Glow(new Color32(0x23, 0x40, 0x7e, 0xff), 0.2f);
            var skirt = Glow(new Color32(0x7f, 0xb4, 0xff, 0xff), 0.25f);
            var blouse = Glow(new Color32(0xfa, 0xfa, 0xff, 0xff), 0.3f);
            var red = Glow(new Color32(0xff, 0x4f, 0x6a, 0xff), 0.5f);
            var shoe = Glow(new Color32(0xc0, 0x30, 0x4a, 0xff), 0.2f);
            var dark = Glow(new Color32(0x2a, 0x1a, 0x14, 0xff), 0f);
            var white = Glow(Color.white, 1f);
            var pink = Glow(new Color32(0xff, 0x9a, 0xb0, 0xff), 0.5f);
            var gold = Glow(Gold, 0.8f);

            // 足
            for (int s = -1; s <= 1; s += 2)
            {
                Part(PrimitiveType.Sphere, "Shoe", casterBody, new Vector3(s * 0.13f, 0.07f, -0.05f), new Vector3(0.2f, 0.13f, 0.32f), shoe);
                Part(PrimitiveType.Capsule, "Leg", casterBody, new Vector3(s * 0.12f, 0.52f, 0f), new Vector3(0.13f, 0.42f, 0.13f), skin);
            }
            // スカート（下が広がる円すい）
            RecordMeshKit.Make("Skirt", RecordMeshKit.ConeSector(0.52f, 0.95f, 0f, 360f, 20), skirt, casterBody, new Vector3(0f, 0.78f, 0f), Vector3.one, Vector3.zero);
            Part(PrimitiveType.Cylinder, "SkirtHem", casterBody, new Vector3(0f, 0.79f, 0f), new Vector3(1.05f, 0.015f, 1.05f), white);
            // 上着
            Part(PrimitiveType.Capsule, "Blazer", casterBody, new Vector3(0f, 1.55f, 0f), new Vector3(0.54f, 0.42f, 0.36f), blazer);
            Part(PrimitiveType.Cube, "Blouse", casterBody, new Vector3(0f, 1.76f, -0.15f), new Vector3(0.17f, 0.3f, 0.06f), blouse, new Vector3(0f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2)
            {
                Part(PrimitiveType.Cube, "Lapel", casterBody, new Vector3(s * 0.11f, 1.74f, -0.165f), new Vector3(0.07f, 0.3f, 0.04f), blazer, new Vector3(0f, 0f, s * 18f));
                Part(PrimitiveType.Cube, "Ribbon", casterBody, new Vector3(s * 0.07f, 1.88f, -0.19f), new Vector3(0.12f, 0.08f, 0.04f), red, new Vector3(0f, 0f, s * 25f));
            }
            Part(PrimitiveType.Sphere, "Knot", casterBody, new Vector3(0f, 1.88f, -0.2f), Vector3.one * 0.06f, red);
            Part(PrimitiveType.Sphere, "Button1", casterBody, new Vector3(0f, 1.48f, -0.175f), Vector3.one * 0.05f, gold);
            Part(PrimitiveType.Sphere, "Button2", casterBody, new Vector3(0f, 1.34f, -0.17f), Vector3.one * 0.05f, gold);
            Part(PrimitiveType.Cube, "Badge", casterBody, new Vector3(-0.15f, 1.62f, -0.17f), new Vector3(0.1f, 0.06f, 0.02f), gold);
            Part(PrimitiveType.Cylinder, "Neck", casterBody, new Vector3(0f, 2.02f, 0f), new Vector3(0.12f, 0.06f, 0.12f), skin);

            // 頭
            casterHead = Group("Head", casterBody, new Vector3(0f, 2.06f, 0f));
            Part(PrimitiveType.Sphere, "Face", casterHead, new Vector3(0f, 0.25f, 0f), Vector3.one * 0.52f, skin);
            Part(PrimitiveType.Sphere, "HairBack", casterHead, new Vector3(0f, 0.29f, 0.06f), new Vector3(0.58f, 0.6f, 0.55f), hair);
            Part(PrimitiveType.Sphere, "Bangs", casterHead, new Vector3(0f, 0.43f, -0.05f), new Vector3(0.56f, 0.26f, 0.48f), hair);
            for (int s = -1; s <= 1; s += 2)
            {
                Part(PrimitiveType.Capsule, "Lock", casterHead, new Vector3(s * 0.25f, 0.13f, -0.03f), new Vector3(0.11f, 0.17f, 0.11f), hair);
                Part(PrimitiveType.Sphere, "Eye", casterHead, new Vector3(s * 0.09f, 0.25f, -0.245f), new Vector3(0.07f, 0.09f, 0.04f), dark);
                Part(PrimitiveType.Sphere, "EyeShine", casterHead, new Vector3(s * 0.09f + 0.015f, 0.27f, -0.265f), Vector3.one * 0.022f, white);
                Part(PrimitiveType.Sphere, "Cheek", casterHead, new Vector3(s * 0.15f, 0.17f, -0.22f), new Vector3(0.08f, 0.045f, 0.02f), pink);
            }
            RecordMeshKit.Make("Smile", RecordMeshKit.Torus(0.055f, 0.012f, 200f, 140f, 16, 6), red, casterHead, new Vector3(0f, 0.15f, -0.255f), Vector3.one, Vector3.zero);
            Part(PrimitiveType.Capsule, "Ponytail", casterHead, new Vector3(0.05f, 0.2f, 0.3f), new Vector3(0.17f, 0.24f, 0.17f), hair, new Vector3(35f, 0f, -10f));
            Part(PrimitiveType.Sphere, "HairTie", casterHead, new Vector3(0.02f, 0.36f, 0.25f), Vector3.one * 0.1f, red);

            // 腕（左の腕で指し棒を持ってモニターを指す。右の腕は手をふる）
            pointerArm = MakeArm("PointerArm", new Vector3(-0.3f, 1.86f, 0f), blazer, skin);
            Part(PrimitiveType.Cylinder, "Pointer", pointerArm, new Vector3(0f, -0.95f, 0f), new Vector3(0.03f, 0.4f, 0.03f), Glow(new Color32(0xd0, 0xd4, 0xe0, 0xff), 0.4f));
            Part(PrimitiveType.Sphere, "PointerTip", pointerArm, new Vector3(0f, -1.36f, 0f), Vector3.one * 0.07f, red);
            waveArm = MakeArm("WaveArm", new Vector3(0.3f, 1.86f, 0f), blazer, skin);

            casterLight = PointLight("CasterLight", caster, new Vector3(0f, 2.2f, -1.6f), new Color(1f, 0.95f, 0.9f), D * 4f);
            caster.gameObject.SetActive(false);
        }

        Transform MakeArm(string name, Vector3 shoulder, Material sleeve, Material skin)
        {
            var pivot = Group(name, casterBody, shoulder);
            Part(PrimitiveType.Capsule, "Upper", pivot, new Vector3(0f, -0.27f, 0f), new Vector3(0.13f, 0.3f, 0.13f), sleeve);
            Part(PrimitiveType.Sphere, "Hand", pivot, new Vector3(0f, -0.6f, 0f), Vector3.one * 0.13f, skin);
            return pivot;
        }

        // ---- 天気予報モニター（座標はメダルの直径が 1。まん中が 0、正面は -Z） ----

        void BuildTv()
        {
            tv = Group("WeatherMonitor", Board, Vector3.zero);
            var frame = Glow(new Color32(0x1b, 0x22, 0x35, 0xff), 0.1f);
            var metal = ProtoAssets.MakeMaterial(new Color32(0x8a, 0x90, 0xa0, 0xff), 0.6f, 0.6f);
            tvBezelMat = Glow(KindColors[0], 2f);
            tvScreenMat = Glow(TvColors[0], 0.6f);
            tvLampMatA = Glow(new Color32(0xff, 0x50, 0x60, 0xff), 2f);
            tvLampMatB = Glow(new Color32(0x50, 0xff, 0xa0, 0xff), 2f);

            // 上からつるすワイヤー（画面の外の天井まで伸びる）と、留め金
            for (int s = -1; s <= 1; s += 2)
            {
                Part(PrimitiveType.Cube, "Clamp", tv, new Vector3(s * 1.1f, 1.16f, 0.05f), new Vector3(0.22f, 0.12f, 0.3f), metal);
                Part(PrimitiveType.Sphere, "Ring", tv, new Vector3(s * 1.1f, 1.27f, 0.05f), new Vector3(0.12f, 0.12f, 0.06f), metal);
                Part(PrimitiveType.Cylinder, "Wire", tv, new Vector3(s * 1.1f, 7.3f, 0.05f), new Vector3(0.035f, 6f, 0.035f), metal);
            }
            // 上の「ON AIR」の看板（ワイヤーの間）
            Part(PrimitiveType.Cube, "OnAirBox", tv, new Vector3(0f, 1.36f, 0f), new Vector3(1f, 0.32f, 0.16f), tvLampMatA);
            var onAir = Text3D("OnAir", tv, new Vector3(0f, 1.36f, -0.09f), 0.22f, Color.white, TextAnchor.MiddleCenter);
            onAir.text = "ON AIR";
            // 枠
            Part(PrimitiveType.Cube, "Frame", tv, Vector3.zero, new Vector3(3.1f, 2.2f, 0.28f), frame);
            Part(PrimitiveType.Cube, "Back", tv, new Vector3(0f, 0f, 0.25f), new Vector3(2.5f, 1.7f, 0.3f), frame);
            Part(PrimitiveType.Cube, "BezelTop", tv, new Vector3(0f, 1.06f, -0.15f), new Vector3(3.0f, 0.05f, 0.03f), tvBezelMat);
            Part(PrimitiveType.Cube, "BezelBottom", tv, new Vector3(0f, -1.06f, -0.15f), new Vector3(3.0f, 0.05f, 0.03f), tvBezelMat);
            Part(PrimitiveType.Cube, "BezelL", tv, new Vector3(-1.5f, 0f, -0.15f), new Vector3(0.05f, 2.1f, 0.03f), tvBezelMat);
            Part(PrimitiveType.Cube, "BezelR", tv, new Vector3(1.5f, 0f, -0.15f), new Vector3(0.05f, 2.1f, 0.03f), tvBezelMat);
            // 画面
            Part(PrimitiveType.Cube, "Screen", tv, new Vector3(0f, -0.04f, -0.13f), new Vector3(2.8f, 1.9f, 0.02f), tvScreenMat);
            Part(PrimitiveType.Cube, "Header", tv, new Vector3(0f, 0.74f, -0.15f), new Vector3(2.8f, 0.36f, 0.02f), Glow(new Color32(0xe8, 0x30, 0x48, 0xff), 1.4f));
            var head = Text3D("HeaderText", tv, new Vector3(-1.3f, 0.74f, -0.17f), 0.26f, Color.white, TextAnchor.MiddleLeft);
            head.text = "WEATHER NEWS";
            Part(PrimitiveType.Sphere, "LampB", tv, new Vector3(1.2f, 0.74f, -0.16f), Vector3.one * 0.14f, tvLampMatB);
            // 文字の後ろの、少し暗い帯（文字を読みやすくする）
            var plate = Glow(new Color(0f, 0f, 0.03f), 0f);
            Part(PrimitiveType.Cube, "NamePlate", tv, new Vector3(0.55f, -0.05f, -0.145f), new Vector3(1.5f, 1.05f, 0.01f), plate);
            Part(PrimitiveType.Cube, "NotePlate", tv, new Vector3(0f, -0.78f, -0.145f), new Vector3(2.7f, 0.32f, 0.01f), plate);

            // 文字（大きく、黒いふちどり付き）
            tvName = Text3D("Name", tv, new Vector3(0.55f, 0.22f, -0.17f), 0.5f, Color.white, TextAnchor.MiddleCenter);
            tvEnglish = Text3D("English", tv, new Vector3(0.55f, -0.12f, -0.17f), 0.2f, new Color(0.85f, 0.92f, 1f), TextAnchor.MiddleCenter);
            tvTimer = Text3D("Timer", tv, new Vector3(0.55f, -0.38f, -0.17f), 0.24f, Gold, TextAnchor.MiddleCenter);
            tvNote = Text3D("Note", tv, new Vector3(0f, -0.78f, -0.17f), 0.21f, Color.white, TextAnchor.MiddleCenter);

            // 天気マーク
            tvIconRoot = Group("Icons", tv, new Vector3(-0.78f, -0.08f, -0.22f));
            BuildTvIcons();
            tv.gameObject.SetActive(false);
        }

        const int TextFontSize = 96;

        /// <summary>モニターの文字（まわりに黒いふちどりを付けて、ドットのフィルターがかかっても読めるようにする）</summary>
        sealed class Label3D
        {
            public TextMesh main;
            public readonly List<TextMesh> outline = new List<TextMesh>();
            string current;
            public string text
            {
                set
                {
                    if (current == value) return;
                    current = value;
                    main.text = value;
                    foreach (var o in outline) o.text = value;
                }
            }
            public Color color { set => main.color = value; }
            public float height
            {
                set
                {
                    float cs = value * 10f / TextFontSize;
                    main.characterSize = cs;
                    foreach (var o in outline) o.characterSize = cs;
                    for (int i = 0; i < outline.Count; i++) outline[i].transform.localPosition = OutlineOffset(i, value);
                }
            }
            public static Vector3 OutlineOffset(int i, float h)
            {
                float d = h * 0.07f;
                float a = i * Mathf.PI * 2f / 8f;
                return new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.004f);
            }
        }

        Label3D Text3D(string name, Transform parent, Vector3 pos, float height, Color color, TextAnchor anchor)
        {
            var label = new Label3D();
            label.main = MakeTextMesh(name, parent, pos, height, color, anchor, TextMaterial);
            for (int i = 0; i < 8; i++)
            {
                var o = MakeTextMesh(name + "Outline", label.main.transform, Label3D.OutlineOffset(i, height), height, new Color(0f, 0f, 0f, 0.95f), anchor, OutlineMaterial);
                label.outline.Add(o);
            }
            return label;
        }

        TextMesh MakeTextMesh(string name, Transform parent, Vector3 pos, float height, Color color, TextAnchor anchor, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = UiFont;
            tm.fontSize = TextFontSize;
            tm.fontStyle = FontStyle.Bold;
            tm.characterSize = height * 10f / TextFontSize;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            var r = go.GetComponent<MeshRenderer>();
            if (tm.font != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return tm;
        }

        void BuildTvIcons()
        {
            var sun = Glow(new Color32(0xff, 0xc0, 0x30, 0xff), 2f);
            var cloudW = Glow(new Color32(0xf2, 0xf4, 0xf8, 0xff), 0.7f);
            var cloudG = Glow(new Color32(0x9a, 0xa2, 0xb2, 0xff), 0.5f);
            var cloudD = Glow(new Color32(0x4a, 0x4c, 0x5c, 0xff), 0.4f);
            var cloudY = Glow(new Color32(0xff, 0xcc, 0x30, 0xff), 1.4f);
            var drop = Glow(new Color32(0x5a, 0xb0, 0xff, 0xff), 1.6f);
            var flake = Glow(Color.white, 1.8f);
            var bolt = Glow(new Color32(0xff, 0xf0, 0x60, 0xff), 3f);
            var swirl = Glow(KindColors[(int)Kind.Typhoon], 1.6f);
            var coin = Glow(Gold, 2f);

            for (int k = 0; k < KindCount; k++) tvIcons[k] = Group("Icon" + (Kind)k, tvIconRoot, Vector3.zero);

            // 晴れ
            var s = tvIcons[(int)Kind.Sunny];
            Part(PrimitiveType.Sphere, "Sun", s, Vector3.zero, Vector3.one * 0.62f, sun);
            sunIconRays = Group("Rays", s, Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var p = Group("Ray", sunIconRays, Vector3.zero);
                p.localRotation = Quaternion.Euler(0f, 0f, i * 45f);
                Part(PrimitiveType.Cube, "Beam", p, new Vector3(0f, 0.5f, 0f), new Vector3(0.08f, 0.2f, 0.05f), sun);
            }

            // 雨
            var r = tvIcons[(int)Kind.Rain];
            IconCloud(r, cloudG, new Vector3(0f, 0.18f, 0f), 0.75f);
            for (int i = 0; i < 4; i++) AddFaller(r, PrimitiveType.Capsule, new Vector3(-0.3f + i * 0.2f, -0.1f, 0f), new Vector3(0.05f, 0.08f, 0.05f), drop, 0.45f, 1.6f, i * 0.27f, false);

            // 雪
            var sn = tvIcons[(int)Kind.Snow];
            IconCloud(sn, cloudW, new Vector3(0f, 0.18f, 0f), 0.75f);
            for (int i = 0; i < 3; i++)
            {
                var f = AddFaller(sn, PrimitiveType.Cube, new Vector3(-0.25f + i * 0.25f, -0.1f, 0f), new Vector3(0.14f, 0.03f, 0.03f), flake, 0.45f, 0.55f, i * 0.33f, true);
                Part(PrimitiveType.Cube, "Arm2", f, Vector3.zero, new Vector3(1f, 1f, 1f), flake, new Vector3(0f, 0f, 60f));
                Part(PrimitiveType.Cube, "Arm3", f, Vector3.zero, new Vector3(1f, 1f, 1f), flake, new Vector3(0f, 0f, -60f));
            }

            // 雷雨
            var st = tvIcons[(int)Kind.Storm];
            IconCloud(st, cloudD, new Vector3(0f, 0.2f, 0f), 0.75f);
            var b = Group("Bolt", st, new Vector3(0.02f, -0.15f, -0.02f));
            Part(PrimitiveType.Cube, "B1", b, new Vector3(0.03f, 0.08f, 0f), new Vector3(0.09f, 0.26f, 0.04f), bolt, new Vector3(0f, 0f, -25f));
            Part(PrimitiveType.Cube, "B2", b, new Vector3(-0.02f, -0.12f, 0f), new Vector3(0.09f, 0.26f, 0.04f), bolt, new Vector3(0f, 0f, -25f));
            Part(PrimitiveType.Cube, "B3", b, new Vector3(0.01f, -0.02f, 0f), new Vector3(0.16f, 0.06f, 0.04f), bolt);
            for (int i = 0; i < 2; i++) AddFaller(st, PrimitiveType.Capsule, new Vector3(-0.3f + i * 0.6f, -0.1f, 0f), new Vector3(0.05f, 0.08f, 0.05f), drop, 0.4f, 1.8f, i * 0.5f, false);

            // 台風（うずまき）
            var ty = tvIcons[(int)Kind.Typhoon];
            typhoonIconSpin = Group("Spin", ty, Vector3.zero);
            RecordMeshKit.Make("Arc1", RecordMeshKit.Torus(0.4f, 0.05f, 0f, 250f, 32, 8), swirl, typhoonIconSpin, Vector3.zero, Vector3.one, Vector3.zero);
            RecordMeshKit.Make("Arc2", RecordMeshKit.Torus(0.26f, 0.05f, 120f, 250f, 28, 8), swirl, typhoonIconSpin, Vector3.zero, Vector3.one, Vector3.zero);
            RecordMeshKit.Make("Arc3", RecordMeshKit.Torus(0.12f, 0.045f, 240f, 250f, 20, 8), swirl, typhoonIconSpin, Vector3.zero, Vector3.one, Vector3.zero);
            Part(PrimitiveType.Sphere, "Eye", typhoonIconSpin, Vector3.zero, Vector3.one * 0.08f, flake);

            // コインの雨
            var cr = tvIcons[(int)Kind.CoinRain];
            IconCloud(cr, cloudY, new Vector3(0f, 0.2f, 0f), 0.75f);
            for (int i = 0; i < 3; i++)
            {
                AddFaller(cr, PrimitiveType.Cylinder, new Vector3(-0.25f + i * 0.25f, -0.1f, 0f), new Vector3(0.16f, 0.015f, 0.16f), coin, 0.45f, 0.9f, i * 0.33f, true,
                          new Vector3(90f, 0f, 0f), Vector3.up);
            }
        }

        void IconCloud(Transform parent, Material mat, Vector3 pos, float size)
        {
            var g = Group("Cloud", parent, pos);
            Part(PrimitiveType.Sphere, "C1", g, new Vector3(-size * 0.32f, -size * 0.05f, 0f), new Vector3(size * 0.5f, size * 0.42f, size * 0.4f), mat);
            Part(PrimitiveType.Sphere, "C2", g, new Vector3(0f, size * 0.08f, 0f), new Vector3(size * 0.6f, size * 0.55f, size * 0.45f), mat);
            Part(PrimitiveType.Sphere, "C3", g, new Vector3(size * 0.32f, -size * 0.04f, 0f), new Vector3(size * 0.48f, size * 0.4f, size * 0.4f), mat);
        }

        Transform AddFaller(Transform parent, PrimitiveType type, Vector3 pos, Vector3 size, Material mat, float span, float speed, float offset, bool spin,
                            Vector3 euler = default, Vector3 spinAxis = default)
        {
            var t = Part(type, "Faller", parent, pos, size, mat, euler);
            fallers.Add(new Faller
            {
                t = t, basePos = pos, baseRot = Quaternion.Euler(euler), spinAxis = spinAxis == Vector3.zero ? Vector3.forward : spinAxis,
                span = span, speed = speed, offset = offset, spin = spin,
            });
            return t;
        }

        // ================================================================
        // 始まり・終わり
        // ================================================================

        protected override void OnStateChanged(GameState state) => EnsureSkipSpectacle();

        protected override void OnBegin()
        {
            EnsureSkipSpectacle();
            history.Clear();
            snowDepth = 0f;
            snowFrozen = false;
            tornadoActive = false;
            tornadoSink = 0f;
            castTarget = 1f;
            castTalk = 1f;
            now = PickWeather(-1);
            timer = changeSeconds;
            kindTime = 0f;
            EnterWeather(now, true);
            Play(null, ref madeAppear, () => RecordSynth.Notes("WeatherAppear", new[] { RecordSynth.Note(0), RecordSynth.Note(4), RecordSynth.Note(7), RecordSynth.Note(12), RecordSynth.Note(16) }, 0.07f, 0.4f, 0.22f, RecordSynth.Wave.Square), 0.7f);
        }

        protected override void OnEnd()
        {
            if (now == Kind.Snow) ExplodeSnow();
            castTarget = 0f;
            tornadoActive = false;
        }

        // ================================================================
        // 抽選
        // ================================================================

        float Weight(int i)
        {
            if (weights != null && i < weights.Length) return Mathf.Max(0f, weights[i]);
            return i == (int)Kind.CoinRain ? 0.3f : 1f;
        }

        /// <summary>天気をえらぶ（exclude の天気はえらばない。-1 なら全部から）</summary>
        Kind PickWeather(int exclude)
        {
            float total = 0f;
            for (int i = 0; i < KindCount; i++) if (i != exclude) total += Weight(i);
            if (total <= 0f)
            {
                int k = Random.Range(0, exclude >= 0 ? KindCount - 1 : KindCount);
                if (exclude >= 0 && k >= exclude) k++;
                return (Kind)k;
            }
            float r = Random.value * total;
            for (int i = 0; i < KindCount; i++)
            {
                if (i == exclude) continue;
                r -= Weight(i);
                if (r <= 0f) return (Kind)i;
            }
            for (int i = KindCount - 1; i >= 0; i--) if (i != exclude && Weight(i) > 0f) return (Kind)i;
            return exclude == 0 ? Kind.Rain : Kind.Sunny;
        }

        // ================================================================
        // 毎フレーム（ルール）
        // ================================================================

        protected override void Tick(float dt, bool running)
        {
            bool wet = now == Kind.Rain || now == Kind.Storm;
            MedalFrictionScale = wet ? rainFriction : 1f;
            PusherHold = now == Kind.Snow && kindTime >= snowStopDelay;
            castTarget = effects.Remaining > withdrawSeconds ? 1f : 0f;
            castTalk = Mathf.MoveTowards(castTalk, 0f, dt * 0.8f);
            if (snowDepth > 0.05f) ApplySnowGrip(dt);
            if (!running) return;

            kindTime += dt;
            timer -= dt;

            switch (now)
            {
                case Kind.Snow:
                    if (!snowFrozen && kindTime >= snowStopDelay)
                    {
                        snowFrozen = true;
                        Pop("プッシャーが凍った！", KindColors[(int)Kind.Snow], new Vector2(BodyW * 0.5f, 360f));
                        Play(freezeSound, ref madeFreeze, () => RecordSynth.Sweep("WeatherFreeze", 2200f, 500f, 0.6f, 0.25f, RecordSynth.Wave.Triangle));
                        Speaker(0.4f);
                    }
                    break;
                case Kind.Storm:
                    lightningTimer -= dt;
                    if (lightningTimer <= 0f)
                    {
                        lightningTimer = Random.Range(Mathf.Min(lightningMin, lightningMax), Mathf.Max(lightningMin, lightningMax));
                        Strike();
                    }
                    break;
                case Kind.Typhoon:
                    TickTyphoon(dt);
                    break;
                case Kind.CoinRain:
                    TickCoinRain(dt);
                    break;
            }

            if (timer <= 0f) ChangeWeather();
        }

        void ChangeWeather()
        {
            Kind old = now;
            if (old == Kind.Snow) ExplodeSnow();
            if (old == Kind.Typhoon) tornadoActive = false;
            snowFrozen = false;
            now = PickWeather((int)old);
            timer = changeSeconds;
            kindTime = 0f;
            EnterWeather(now, false);
        }

        void EnterWeather(Kind k, bool first)
        {
            history.Add(k);
            if (history.Count > 6) history.RemoveAt(0);
            castTalk = 1f;
            Color c = KindColors[(int)k];
            if (!first) Play(chimeSound, ref madeChime, () => RecordSynth.Notes("WeatherChime", new[] { RecordSynth.Note(16), RecordSynth.Note(12), RecordSynth.Note(7), RecordSynth.Note(0) }, 0.18f, 0.6f, 0.25f, RecordSynth.Wave.Bell));
            PopCenter($"天気予報：{KindNames[(int)k]}", c);
            switch (k)
            {
                case Kind.Rain:
                case Kind.Storm:
                    PlacePuddles();
                    lightningTimer = 0.8f;
                    break;
                case Kind.Snow:
                    PlaceMounds();
                    break;
                case Kind.Typhoon:
                    StartTyphoon();
                    break;
                case Kind.CoinRain:
                    coinSpawned = 0;
                    coinTimer = 0.4f;
                    Speaker(0.6f, 6, Gold);
                    break;
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center || now != Kind.Sunny) return;
            int extra = AddMultiplied(sunnyMultiplier);
            Pop($"晴れ ×{sunnyMultiplier:0.#} +{CenterPoints + extra}", KindColors[0], new Vector2(BodyW * Random.Range(0.3f, 0.7f), 300f));
            if (medal != null) Emit(shine, medal.transform.position + Vector3.up * D * 0.3f, KindColors[0], 10);
        }

        // ---- 雪の爆発 ----

        void ExplodeSnow()
        {
            float k = snowDepth;
            snowDepth = 0f;
            if (k < 0.05f) return;
            for (int i = 0; i < mounds.Count; i++)
            {
                Vector3 p = mounds[i].position;
                Emit(snowBurst, p + Vector3.up * D * 0.2f, Color.white, Mathf.RoundToInt(24 * k));
                Emit(frost, p, new Color(0.7f, 0.9f, 1f), Mathf.RoundToInt(8 * k));
                Blast(p, D * 1.4f, snowBlastStrength * (0.4f + 0.6f * k), 0.6f);
            }
            // 雪の板の上からも舞い上げる
            for (int i = 0; i < 10; i++)
                Emit(snowBurst, RandomFloorPoint(0.05f, 0.95f, 0.9f) + Vector3.up * D * 0.3f, new Color(0.95f, 0.97f, 1f), Mathf.RoundToInt(10 * k));
            Shake(0.08f * k + 0.02f, 0.45f);
            Play(snowBurstSound, ref madeSnowBurst, () => RecordSynth.Noise("WeatherSnowBurst", 0.9f, 0.25f, 0.004f, 0.5f, 1.2f, 5));
            Pop("積もった雪が爆発！", KindColors[(int)Kind.Snow], new Vector2(BodyW * 0.5f, 420f));
            Speaker(0.8f, 4, KindColors[(int)Kind.Snow]);
            flashLight.color = new Color(0.85f, 0.93f, 1f);
            skyFlash = Mathf.Max(skyFlash, 0.6f);
        }

        /// <summary>積もった雪の山の上にいるメダルだけ、すべりにくくする（動きをすばやく弱める）</summary>
        void ApplySnowGrip(float dt)
        {
            if (snowGrip <= 0f) return;
            float k = Mathf.Clamp01(dt * snowGrip * Mathf.Clamp01(snowDepth * 1.5f));
            for (int i = 0; i < mounds.Count; i++)
            {
                if (!mounds[i].gameObject.activeSelf) continue;
                float r = mounds[i].localScale.x * 0.5f + D * 0.3f;
                MedalsNear(mounds[i].position, r, gripList);
                foreach (var m in gripList)
                {
                    if (m == null || m.Body == null || m.Body.isKinematic) continue;
                    Vector3 v = m.Body.velocity;
                    v.x *= 1f - k;
                    v.z *= 1f - k;
                    m.Body.velocity = v;
                    m.Body.angularVelocity *= 1f - k;
                }
            }
        }

        // ---- 雷 ----

        void Strike()
        {
            Vector3 p = RandomFloorPoint(0.08f, 0.95f, 0.9f);
            p.y = D * 0.35f;
            boltPos = p;
            var cam = Camera.main;
            float topY = cam != null ? cam.transform.position.y + D * 2f : Layout.wallHeight * 5f;
            boltTop = new Vector3(p.x * 0.8f + Random.Range(-D, D), topY, p.z + Random.Range(-D, D * 2f));
            MakeBoltShape();
            boltTime = 0.5f;
            arcTime = 0.9f;
            ringTime = 0f;
            scorch.position = p + Vector3.up * D * 0.05f;

            Blast(p, D * lightningRadius, lightningStrength, 0.85f);
            Blast(p, D * lightningRadius * 0.5f, lightningStrength * 0.6f, 1.2f);   // まん中のメダルは高く吹き飛ぶ
            Emit(sparks, p, ZapYellow, 40);
            Emit(sparks, p, ZapPurple, 40);
            Emit(smoke, p, new Color(0.35f, 0.35f, 0.4f, 0.7f), 10);
            Emit(splash, p, new Color(0.8f, 0.9f, 1f), 30);
            Shake(0.1f, 0.45f);
            skyFlash = 1f;
            flashLight.color = Color.Lerp(ZapYellow, ZapPurple, Random.value);
            Shake(0.14f, 0.5f);
            Speaker(0.9f);
            Play(thunderSound, ref madeThunder, () => RecordSynth.Noise("WeatherThunder", 1.4f, 0.08f, 0.01f, 0.5f, 1.3f, 11));
        }

        void MakeBoltShape()
        {
            const int n = 12;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1);
                Vector3 q = Vector3.Lerp(boltTop, boltPos, k);
                float j = Mathf.Sin(k * Mathf.PI) * D * 0.7f;
                if (i > 0 && i < n - 1) q += new Vector3(Random.Range(-j, j), 0f, Random.Range(-j, j) * 0.5f);
                pts[i] = q;
            }
            boltCore.positionCount = n;
            boltCore.SetPositions(pts);
            boltGlow.positionCount = n;
            boltGlow.SetPositions(pts);
            // 紫の光は少しずらして、ビリビリと二重に見せる
            var pp = new Vector3[n];
            for (int i = 0; i < n; i++)
                pp[i] = pts[i] + (i > 0 && i < n - 1 ? new Vector3(Random.Range(-D, D) * 0.35f, 0f, Random.Range(-D, D) * 0.2f) : Vector3.zero);
            boltGlowPurple.positionCount = n;
            boltGlowPurple.SetPositions(pp);
            // 枝わかれ
            foreach (var b in boltBranches)
            {
                int from = Random.Range(2, n - 2);
                const int m = 5;
                var br = new Vector3[m];
                br[0] = pts[from];
                Vector3 dir = new Vector3(Random.Range(-1f, 1f), -Random.Range(0.6f, 1.4f), Random.Range(-0.4f, 0.4f)).normalized;
                for (int i = 1; i < m; i++) br[i] = br[i - 1] + dir * D * Random.Range(0.5f, 0.9f) + new Vector3(Random.Range(-D, D) * 0.35f, 0f, Random.Range(-D, D) * 0.2f);
                b.positionCount = m;
                b.SetPositions(br);
            }
        }

        /// <summary>落ちた所から、床の上を四方へ走る電気の形を作り直す</summary>
        void MakeGroundArcs()
        {
            for (int a = 0; a < groundArcs.Count; a++)
            {
                const int m = 6;
                var pts = new Vector3[m];
                float ang = (a / (float)groundArcs.Count + Random.Range(-0.05f, 0.05f)) * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var side = new Vector3(-dir.z, 0f, dir.x);
                float len = D * lightningRadius * Random.Range(0.5f, 1f);
                for (int i = 0; i < m; i++)
                {
                    float k = i / (float)(m - 1);
                    float j = i == 0 ? 0f : D * 0.3f;
                    pts[i] = boltPos + dir * (len * k) + side * Random.Range(-j, j) + Vector3.up * (D * 0.15f + Random.Range(0f, D * 0.25f));
                }
                groundArcs[a].positionCount = m;
                groundArcs[a].SetPositions(pts);
            }
        }

        // ---- 台風 ----

        Vector3 OrbitPoint(float a)
        {
            float cz = Mathf.Lerp(FrontZ, PusherFrontZ, 0.58f);
            float rx = HalfW * 0.55f;
            float rz = (PusherFrontZ - FrontZ) * 0.25f;
            return new Vector3(Mathf.Cos(a) * rx, 0f, cz + Mathf.Sin(a) * rz);
        }

        void StartTyphoon()
        {
            tornadoActive = true;
            tornadoSink = 0f;
            tornadoSinkAnnounced = false;
            tornadoAngle = Random.Range(0f, Mathf.PI * 2f);
            tornadoPos = OrbitPoint(tornadoAngle);
            tornadoVel = Vector3.zero;
            Play(windSound, ref madeWind, () => RecordSynth.Noise("WeatherWind", 2.2f, 0.7f, 0.4f, 1.2f, 0.9f, 7));
            Shake(0.04f, 1f);
        }

        void TickTyphoon(float dt)
        {
            if (!tornadoActive || dt <= 0f) return;
            Vector3 prev = tornadoPos;
            if (timer > tornadoSinkSeconds)
            {
                tornadoAngle += dt * Mathf.PI * 2f / Mathf.Max(0.5f, tornadoLapSeconds);
                tornadoPos = OrbitPoint(tornadoAngle);
            }
            else
            {
                // 中央の穴へ向かって、落ちていく
                if (!tornadoSinkAnnounced)
                {
                    tornadoSinkAnnounced = true;
                    Pop("竜巻が穴に吸い込まれる！", KindColors[(int)Kind.Typhoon], new Vector2(BodyW * 0.5f, 400f));
                }
                Vector3 hole = new Vector3(0f, 0f, FrontZ - D * 0.5f);
                tornadoPos = Vector3.MoveTowards(tornadoPos, hole, dt * D * 5f);
                float left = Mathf.Max(0.01f, tornadoSinkSeconds);
                bool there = (tornadoPos - hole).sqrMagnitude < D * D * 0.25f;
                if (there || timer < left * 0.55f) tornadoSink = Mathf.MoveTowards(tornadoSink, 1f, dt / (left * 0.5f));
                if (tornadoSink >= 1f)
                {
                    tornadoActive = false;
                    Emit(splash, hole + Vector3.up * D * 0.3f, new Color(0.8f, 0.9f, 0.95f), 40);
                    Emit(debris, hole, new Color(0.7f, 0.68f, 0.6f), 30);
                    Shake(0.07f, 0.5f);
                    Speaker(0.8f);
                    Play(null, ref madeSink, () => RecordSynth.Sweep("WeatherSink", 420f, 50f, 0.8f, 0.35f, RecordSynth.Wave.Triangle));
                }
            }
            tornadoVel = (tornadoPos - prev) / dt;
            CaptureMedals(dt);
        }

        /// <summary>竜巻にふれたメダルを、竜巻のまわりで回す</summary>
        void CaptureMedals(float dt)
        {
            float R = D * tornadoRadius * (1f - 0.35f * tornadoSink);
            float pull = R * tornadoPullRange;
            MedalsNear(tornadoPos, pull, near);
            int count = 0;
            float follow = Mathf.Clamp01(dt * 14f);
            foreach (var m in near)
            {
                if (count >= tornadoMaxMedals) break;
                if (m == null || m.Body == null || m.Body.isKinematic) continue;
                count++;
                Vector3 rel = m.transform.position - tornadoPos;
                rel.y = 0f;
                float r = rel.magnitude;
                Vector3 radial = r > 0.0001f ? rel / r : Vector3.right;
                Vector3 tangent = Vector3.Cross(radial, Vector3.up);   // 上から見て反時計回り
                m.Body.WakeUp();
                if (r > R)
                {
                    // 巻き込む範囲の外：風で竜巻の方へ吸い寄せ、回り始める
                    float k = 1f - Mathf.Clamp01((r - R) / Mathf.Max(0.01f, pull - R));
                    Vector3 suck = -radial * D * 5f * k + tangent * D * tornadoSpin * 0.5f * k;
                    m.Body.velocity = Vector3.Lerp(m.Body.velocity, m.Body.velocity * 0.5f + suck, Mathf.Clamp01(dt * 6f));
                    continue;
                }
                // 巻き込む範囲の中：竜巻のまわりを、いっしょにぐるぐる回る（内側ほど速い）
                float speed = D * tornadoSpin * Mathf.Lerp(1.3f, 0.8f, r / Mathf.Max(0.01f, R));
                Vector3 want = tornadoVel + tangent * speed - radial * (r - R * 0.5f) * 6f;
                float h = m.transform.position.y;
                // 少し浮かせて回す（ふちの壁は越えない高さまで）。高さは回る位置で波打たせる
                float lift = D * (0.55f + 0.3f * Mathf.Sin(Time.time * 6f + r * 3f));
                want.y = tornadoSink > 0f ? -D * 3f : Mathf.Clamp((lift - h) * 6f, -D * 2.5f, D * 4f);
                m.Body.velocity = Vector3.Lerp(m.Body.velocity, want, follow);
                m.Body.angularVelocity = Vector3.Lerp(m.Body.angularVelocity, new Vector3(Random.Range(-6f, 6f), 20f, Random.Range(-6f, 6f)), follow);
            }
        }

        // ---- コインの雨 ----

        void TickCoinRain(float dt)
        {
            if (coinSpawned >= coinRainCount) return;
            coinTimer -= dt;
            float interval = changeSeconds * 0.8f / Mathf.Max(1, coinRainCount);
            while (coinTimer <= 0f && coinSpawned < coinRainCount)
            {
                coinTimer += interval;
                coinSpawned++;
                if (BoardMedals.Count >= medalCap) continue;
                float x = Random.Range(-HalfW * 0.8f, HalfW * 0.8f);
                float z = Mathf.Lerp(FrontZ, PusherFrontZ, Random.Range(0.3f, 0.85f));
                var pos = new Vector3(x, Layout.launcherHeight + D * 3f, z);
                var m = SpawnMedal(pos, new Vector3(Random.Range(-0.3f, 0.3f) * D, -D * 4f, 0f));
                if (m == null) continue;
                Emit(shine, pos, Gold, 6);
                if (coinSoundCount++ % 3 == 0)
                    Play(coinSound, ref madeCoin, () => RecordSynth.Notes("WeatherCoin", new[] { RecordSynth.Note(24), RecordSynth.Note(31) }, 0.06f, 0.2f, 0.18f, RecordSynth.Wave.Square), 0.5f);
            }
        }

        // ================================================================
        // 毎フレーム（見た目）
        // ================================================================

        protected override bool KeepBoardVisible() =>
            castShown > 0.001f || sunShown > 0.001f || bandShown > 0.001f || wetness > 0.01f || snowDepth > 0.01f || tornadoShown > 0.001f ||
            (rain != null && rain.particleCount > 0) || (snow != null && snow.particleCount > 0) ||
            (snowBurst != null && snowBurst.particleCount > 0) || (sparks != null && sparks.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            bool on = Active;
            Kind k = now;
            if (!on) castTarget = 0f;

            // 出ぐあい
            sunShown = Mathf.MoveTowards(sunShown, on && k == Kind.Sunny ? 1f : 0f, dt * 1.2f);
            smallCloudShown = Mathf.MoveTowards(smallCloudShown, on && k == Kind.Sunny ? 1f : 0f, dt * 0.9f);
            bandShown = Mathf.MoveTowards(bandShown, on && k != Kind.Sunny ? 1f : 0f, dt * 1.2f);
            bool wet = on && (k == Kind.Rain || k == Kind.Storm);
            wetness = Mathf.MoveTowards(wetness, wet ? 1f : 0f, dt * (wet ? 0.35f : 0.18f));
            if (on && k == Kind.Snow) snowDepth = Mathf.MoveTowards(snowDepth, 1f, dt / Mathf.Max(0.5f, snowFillSeconds));
            else if (!on) snowDepth = Mathf.MoveTowards(snowDepth, 0f, dt);
            castShown = Mathf.MoveTowards(castShown, castTarget, dt * (castTarget > castShown ? 1.3f : 0.8f));
            tornadoShown = Mathf.MoveTowards(tornadoShown, on && tornadoActive ? 1f : 0f, dt * 2f);
            skyFlash = Mathf.MoveTowards(skyFlash, 0f, dt * 2.5f);
            boltTime = Mathf.MoveTowards(boltTime, 0f, dt);

            UpdateSky(dt, t, k);
            UpdatePrecipitation(dt, on, k);
            UpdateWetFloor(dt, t);
            UpdateSnowFloor();
            UpdateLightning(dt);
            UpdateTornado(dt, t);
            UpdateCaster(dt, t);
            UpdateOverlay(castShown > 0.001f);
            UpdateTv(t);

            sunLight.intensity = sunShown * 2.2f * BoardShown;
            sunLight.enabled = sunLight.intensity > 0.01f;
            if (on && k == Kind.Sunny && Random.value < dt * 3f)
            {
                var p = RandomFloorPoint(0.1f, 0.9f, 0.85f);
                p.y = D * 0.5f;
                Emit(shine, p, new Color(1f, 0.9f, 0.6f), 2);
            }
        }

        void UpdateSky(float dt, float t, Kind k)
        {
            var cam = Camera.main;
            bool show = sunShown > 0f || bandShown > 0f || smallCloudShown > 0f;
            if (sky.gameObject.activeSelf != show) sky.gameObject.SetActive(show);
            if (!show || cam == null) return;

            // 画面の上の、カメラの前に置く
            float boardDist = Vector3.Distance(cam.transform.position, new Vector3(0f, 0f, (FrontZ + BackZ) * 0.5f));
            float dist = Mathf.Max(cam.nearClipPlane * 4f, boardDist * skyDepth);
            float height = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float unit = height / 10f;
            Vector3 center = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, dist));
            sky.position = center;
            sky.rotation = cam.transform.rotation;
            sky.localScale = Vector3.one * unit;
            skyHalfW = 5f * Mathf.Max(0.5f, cam.aspect);
            float topY = (skyViewportY - 0.5f) * 10f;   // 「画面の高さ 10」での高さ

            // 太陽
            float es = Ease(sunShown);
            sunGroup.localPosition = new Vector3(-skyHalfW * 0.55f, Mathf.Lerp(topY + 4.5f, topY - 0.1f, es), 0f);
            sunGroup.localScale = Vector3.one * (0.9f + 0.05f * Mathf.Sin(t * 2f));
            sunRays.localRotation = Quaternion.Euler(0f, 0f, -t * 25f);
            RecordMeshKit.SetColor(sunHaloMat, new Color(1f, 0.75f, 0.3f, 0.55f + 0.2f * Mathf.Sin(t * 3f)));

            float ec = Ease(smallCloudShown);
            for (int i = 0; i < smallClouds.Count; i++)
            {
                float x = (i == 0 ? -skyHalfW * 0.15f : skyHalfW * 0.45f) + Mathf.Sin(t * 0.3f + i * 2f) * 0.6f;
                smallClouds[i].localPosition = new Vector3(x, Mathf.Lerp(topY + 4f, topY - 0.4f - i * 0.6f, ec), 0.2f);
            }

            // 雲の帯
            float eb = Ease(bandShown);
            band.localPosition = new Vector3(0f, Mathf.Lerp(topY + 5f, topY + 0.6f, eb), 0.4f);
            float bandScaleX = skyHalfW / 9f;
            band.localScale = new Vector3(Mathf.Max(1f, bandScaleX), 1f, 1f);
            float wind = k == Kind.Typhoon ? 2.4f : k == Kind.Storm ? 0.9f : 0.35f;
            for (int i = 0; i < bandPuffs.Count; i++)
            {
                Vector3 b = bandBase[i];
                float x = Mathf.Repeat(b.x + 11f + t * wind, 23f) - 11.5f;
                bandPuffs[i].localPosition = new Vector3(x, b.y + Mathf.Sin(t * 0.8f + i) * 0.12f, b.z);
            }
            if (Active)
            {
                Color target = CloudColors[(int)k];
                bandColor = Color.Lerp(bandColor, target, Mathf.Clamp01(dt * 3f));
                bandGlow = Mathf.Lerp(bandGlow, CloudGlow[(int)k], Mathf.Clamp01(dt * 3f));
            }
            float flash = skyFlash * skyFlash * (k == Kind.Storm ? 4f : 2f);
            float pulse = k == Kind.CoinRain ? 0.3f * Mathf.Sin(t * 4f) : 0f;
            SetGlow(bandMat, Color.Lerp(bandColor, Color.white, skyFlash * 0.5f), bandGlow + flash + pulse);
        }

        void UpdatePrecipitation(float dt, bool on, Kind k)
        {
            var cam = Camera.main;
            float top = cam != null ? cam.transform.position.y + D * 2.5f : Layout.wallHeight * 5f;
            float midZ = (FrontZ + BackZ) * 0.5f;

            bool raining = on && (k == Kind.Rain || k == Kind.Storm);
            rain.transform.position = new Vector3(0f, top, midZ);
            var rm = rain.main;
            float rs = D * 24f;
            rm.startSpeed = new ParticleSystem.MinMaxCurve(rs * 0.9f, rs * 1.1f);
            rm.startLifetime = top / rs;
            var re = rain.emission;
            re.rateOverTime = raining ? (k == Kind.Storm ? 900f : 650f) : 0f;
            if (raining && !rain.isPlaying) rain.Play();

            bool snowing = on && k == Kind.Snow;
            snow.transform.position = new Vector3(0f, top, midZ);
            var sm = snow.main;
            float ss = D * 1.8f;
            sm.startSpeed = new ParticleSystem.MinMaxCurve(ss * 0.7f, ss * 1.2f);
            sm.startLifetime = top / (ss * 0.7f);
            var se = snow.emission;
            se.rateOverTime = snowing ? 180f : 0f;
            if (snowing && !snow.isPlaying) snow.Play();

            // 雨のしぶき
            if (raining)
            {
                splashAcc += dt * (k == Kind.Storm ? 40f : 28f);
                while (splashAcc >= 1f)
                {
                    splashAcc -= 1f;
                    var p = RandomFloorPoint(0.02f, 0.98f, 0.95f);
                    p.y = D * 0.45f;
                    Emit(splash, p, new Color(0.75f, 0.85f, 1f, 0.8f), 2);
                }
            }
            else splashAcc = 0f;

            // コインの雨のきらきら
            if (on && k == Kind.CoinRain)
            {
                sparkle.transform.position = new Vector3(0f, Layout.launcherHeight + D * 4f, (FrontZ + PusherFrontZ) * 0.5f);
                sparkleAcc += dt * 30f;
                int n = Mathf.FloorToInt(sparkleAcc);
                sparkleAcc -= n;
                if (n > 0)
                {
                    var pr = new ParticleSystem.EmitParams { startColor = Gold, applyShapeToPosition = true };
                    sparkle.Emit(pr, n);
                }
            }
        }

        void UpdateWetFloor(float dt, float t)
        {
            float w = wetness * BoardShown;
            bool show = w > 0.005f;
            if (wetSheet.gameObject.activeSelf != show) wetSheet.gameObject.SetActive(show);
            if (show) wetMat.color = new Color(0.22f, 0.36f, 0.55f, 0.32f * w);
            bool raining = Active && (now == Kind.Rain || now == Kind.Storm);
            for (int i = 0; i < puddles.Count; i++)
            {
                float grow = Mathf.Clamp01(wetness * 1.4f - i * 0.05f);
                float size = D * puddleSize[i] * grow;
                bool vis = size > D * 0.05f;
                if (puddles[i].gameObject.activeSelf != vis) puddles[i].gameObject.SetActive(vis);
                if (vis) puddles[i].localScale = new Vector3(size * 1.3f, 0.002f, size);

                bool rip = vis && raining;
                if (ripples[i].gameObject.activeSelf != rip) ripples[i].gameObject.SetActive(rip);
                if (rip)
                {
                    float ph = Mathf.Repeat(t * 0.9f + i * 0.37f, 1f);
                    float rs = size * (0.3f + ph * 1.1f);
                    ripples[i].localScale = new Vector3(rs * 1.3f, 1f, rs);
                }
            }
            if (raining || w > 0.005f)
                RecordMeshKit.SetColor(rippleMat, new Color(0.7f, 0.85f, 1f, 0.6f * Mathf.Clamp01(wetness)));
        }

        void UpdateSnowFloor()
        {
            float d = snowDepth * BoardShown;
            bool show = d > 0.005f;
            if (snowSheet.gameObject.activeSelf != show) snowSheet.gameObject.SetActive(show);
            if (show) RecordMeshKit.SetColor(snowSheetMat, new Color(0.96f, 0.98f, 1f, 0.75f * Mathf.Clamp01(d * 1.3f)));
            for (int i = 0; i < mounds.Count; i++)
            {
                float grow = Mathf.Clamp01(d * 1.3f - (i % 4) * 0.08f);
                float r = D * moundSize[i] * grow;
                bool vis = r > D * 0.05f;
                if (mounds[i].gameObject.activeSelf != vis) mounds[i].gameObject.SetActive(vis);
                if (vis) mounds[i].localScale = new Vector3(r * 1.4f, r * 0.55f, r * 1.2f);
            }
        }

        void UpdateLightning(float dt)
        {
            bool show = boltTime > 0f;
            bool flicker = show && (boltTime > 0.32f || (boltTime > 0.12f && Random.value < 0.5f));
            boltCore.enabled = flicker;
            boltGlow.enabled = flicker;
            boltGlowPurple.enabled = flicker;
            foreach (var b in boltBranches) b.enabled = flicker && boltTime > 0.2f && Random.value < 0.8f;
            if (flicker && Random.value < 0.4f) MakeBoltShape();
            // 床を走る電気（ビリビリと形を変えながら、少しのこる）
            arcTime = Mathf.MoveTowards(arcTime, 0f, dt);
            bool arcs = arcTime > 0f && Random.value < 0.85f;
            for (int i = 0; i < groundArcs.Count; i++) groundArcs[i].enabled = arcs && (arcTime > 0.4f || i % 2 == (Time.frameCount & 1));
            if (arcs) MakeGroundArcs();
            if (arcTime > 0f && Random.value < dt * 20f)
                Emit(sparks, boltPos + new Vector3(Random.Range(-D, D), 0f, Random.Range(-D, D)) * lightningRadius * 0.5f, Random.value < 0.5f ? ZapYellow : ZapPurple, 3);
            boltLight.transform.position = boltPos + Vector3.up * D * 1.2f;
            boltLight.color = Random.value < 0.5f ? ZapYellow : ZapPurple;
            boltLight.intensity = flicker ? 9f * (boltTime / 0.5f) : arcTime > 0f ? 3f * arcTime : 0f;
            boltLight.enabled = boltLight.intensity > 0.01f;
            flashLight.intensity = skyFlash * skyFlash * 3f;
            flashLight.enabled = flashLight.intensity > 0.01f;

            if (ringTime >= 0f)
            {
                ringTime += dt;
                float k = ringTime / 0.6f;
                if (k >= 1f)
                {
                    ringTime = -1f;
                    scorch.gameObject.SetActive(false);
                }
                else
                {
                    if (!scorch.gameObject.activeSelf) scorch.gameObject.SetActive(true);
                    float s = D * lightningRadius * 2.2f * Ease(k);
                    scorch.localScale = new Vector3(s, 1f, s);
                    RecordMeshKit.SetColor(scorchMat, new Color(BoltColor.r, BoltColor.g, BoltColor.b, 1f - k));
                }
            }
            else if (scorch.gameObject.activeSelf) scorch.gameObject.SetActive(false);
        }

        void UpdateTornado(float dt, float t)
        {
            bool show = tornadoShown > 0.001f;
            if (tornado.gameObject.activeSelf != show) tornado.gameObject.SetActive(show);
            if (!show) return;
            float h = D * 5.5f * tornadoSize;
            float e = Ease(tornadoShown);
            float sink = tornadoSink * tornadoSink;
            tornado.position = tornadoPos + Vector3.down * (h * 1.05f * sink);
            float wobble = Mathf.Sin(t * 3.1f) * 4f;
            tornado.rotation = Quaternion.Euler(wobble, 0f, Mathf.Cos(t * 2.3f) * 4f);
            float sx = e * (1f - 0.4f * sink) * tornadoSize;
            tornado.localScale = new Vector3(sx, e * tornadoSize, sx);
            funnelOuter.localRotation = Quaternion.Euler(0f, -t * 540f, 0f);
            funnelInner.localRotation = Quaternion.Euler(0f, t * 380f, 0f);
            tornadoBase.localRotation = Quaternion.Euler(0f, -t * 300f, 0f);
            RecordMeshKit.Scroll(funnelOuterMat, 0f, -dt * 0.6f);
            RecordMeshKit.Scroll(funnelInnerMat, 0f, -dt * 0.9f);
            RecordMeshKit.SetColor(funnelOuterMat, new Color(0.7f, 0.74f, 0.78f, 0.9f * (1f - sink * 0.5f)));
            tornadoBase.gameObject.SetActive(sink < 0.3f);
            if (tornadoActive && Random.value < dt * 60f)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var p = tornadoPos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * D * Random.Range(0.4f, 1.8f) * tornadoSize + Vector3.up * D * 0.3f;
                Emit(debris, p, Random.value < 0.5f ? new Color(0.75f, 0.72f, 0.65f, 0.9f) : new Color(0.55f, 0.75f, 0.45f, 0.9f), 3);
            }
        }

        void UpdateCaster(float dt, float t)
        {
            bool show = castShown > 0.001f;
            if (caster.gameObject.activeSelf != show) caster.gameObject.SetActive(show);
            if (tv.gameObject.activeSelf != show) tv.gameObject.SetActive(show);
            if (!show) return;

            Vector3 muzzle = game.Launcher != null ? game.Launcher.MuzzlePosition(1)
                                                   : new Vector3(HalfW - D * 0.6f, Layout.launcherHeight, BackZ - D * 0.7f);
            var cam = Camera.main;
            float e = EaseBack(castShown);
            float hide = 1f - Ease(castShown);

            // お姉さん
            Vector3 cpos = muzzle + casterOffset * D;
            caster.position = cpos + Vector3.down * (hide * D * 4f);
            caster.rotation = FaceCamera(cam, cpos);
            // モニターが右にある時は左右を反対にして、指し棒がモニターの方を向くようにする
            float mirror = monitorOffset.x >= casterOffset.x ? -1f : 1f;
            float cs = D * casterScale * Mathf.Max(0.01f, e);
            caster.localScale = new Vector3(cs * mirror, cs, cs);
            float talk = castTalk;
            casterBody.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(t * 2.2f)) * 0.04f + Mathf.Sin(talk * Mathf.PI) * 0.25f, 0f);
            casterBody.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.7f) * 8f - 12f, 0f);
            casterHead.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.7f) * 4f, 0f, Mathf.Sin(t * 1.3f) * 7f);
            bool leaving = castTarget < 0.5f;
            // 指し棒でモニターを指す（天気が変わった時は大きく動かす）
            float point = -100f + Mathf.Sin(t * 3f) * (4f + talk * 18f);
            pointerArm.localRotation = Quaternion.Euler(0f, 0f, leaving ? Mathf.Lerp(point, -20f, hide) : point);
            // もう片方の手：ふだんは腰、撤収の時は手をふる
            float waveZ = leaving ? 150f + Mathf.Sin(t * 10f) * 25f : 25f + talk * 120f * Mathf.Sin(Mathf.Clamp01(talk) * Mathf.PI);
            waveArm.localRotation = Quaternion.Slerp(waveArm.localRotation, Quaternion.Euler(0f, 0f, waveZ), Mathf.Clamp01(dt * 10f));
            casterLight.intensity = 1.2f * castShown;

            // モニター
            Vector3 mpos = muzzle + monitorOffset * D;
            // 上から下りてきて、ワイヤーでゆらゆら揺れる
            tv.position = mpos + Vector3.up * (hide * D * 8f);
            float swing = Mathf.Sin(t * 1.3f) * 1.5f + castTalk * Mathf.Sin(t * 9f) * 2f;
            tv.rotation = FaceCamera(cam, mpos) * Quaternion.Euler(0f, monitorYaw, swing);
            tv.localScale = Vector3.one * D * monitorScale * Mathf.Max(0.01f, e);
        }

        // ================================================================
        // お姉さんとモニターを、ステージのモニターより手前に描く
        // （専用のカメラで別の絵に描いて、モニターの画面の Canvas より上の Canvas に重ねる）
        // ================================================================

        int overlayLayer = -1;
        Camera overlayCam;
        RenderTexture overlayTexture;
        Canvas overlayCanvas;
        RawImage overlayImage;
        Material overlayDotMat, overlayPlainMat;
        float overlayMaskTimer;

        /// <summary>お姉さんとモニターを描くレイヤー（名前の付いていない空きレイヤー。モニター・大きな演出のレイヤーはさける）</summary>
        int PickOverlayLayer()
        {
            int named = LayerMask.NameToLayer("WeatherCaster");
            if (named >= 0) return named;
            int skipA = game != null ? game.monitor.overlayLayer : 29;
            int skipB = game != null ? game.monitor.frontEffectsLayer : 28;
            int skipC = RecordSpectacleDirector.Instance != null ? RecordSpectacleDirector.Instance.Layer : 31;
            for (int i = 8; i < 31; i++)
                if (i != skipA && i != skipB && i != skipC && string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return -1;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        void BuildOverlay()
        {
            var camGo = new GameObject("WeatherCasterCamera");
            camGo.transform.SetParent(Board, false);
            overlayCam = camGo.AddComponent<Camera>();
            overlayCam.cullingMask = 1 << overlayLayer;
            overlayCam.clearFlags = CameraClearFlags.Depth;
            overlayCam.allowHDR = false;
            overlayCam.allowMSAA = false;
            overlayCam.useOcclusionCulling = false;
            overlayCam.enabled = false;

            var cgo = new GameObject("WeatherCasterScreen");
            cgo.transform.SetParent(Board, false);
            overlayCanvas = cgo.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var igo = new GameObject("Caster");
            igo.transform.SetParent(cgo.transform, false);
            overlayImage = igo.AddComponent<RawImage>();
            overlayImage.raycastTarget = false;
            var rt = overlayImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var premultiplied = Shader.Find("Hidden/MedalPusher/OverlayPremultiplied");
            if (premultiplied != null) overlayPlainMat = new Material(premultiplied);
            var dots = Shader.Find("MedalGame/SpectacleDots");
            if (dots == null) dots = Resources.Load<Shader>("SpectacleDots");
            if (dots != null) overlayDotMat = new Material(dots);
            overlayImage.material = overlayDotMat != null ? overlayDotMat : overlayPlainMat;
            cgo.SetActive(false);
        }

        void UpdateOverlay(bool show)
        {
            if (overlayLayer < 0 || !drawInFront)
            {
                // 手前に描かない時は、ふつうのカメラに描かせる
                if (overlayCam != null) overlayCam.enabled = false;
                if (overlayCanvas != null && overlayCanvas.gameObject.activeSelf) overlayCanvas.gameObject.SetActive(false);
                if (overlayLayer >= 0 && Camera.main != null) Camera.main.cullingMask |= 1 << overlayLayer;
                return;
            }
            if (overlayCam == null) BuildOverlay();
            var main = Camera.main;
            bool on = show && main != null;
            if (overlayCam.enabled != on) overlayCam.enabled = on;
            if (!on)
            {
                if (overlayCanvas.gameObject.activeSelf) overlayCanvas.gameObject.SetActive(false);
                return;
            }

            // ほかのカメラには、このレイヤーを描かせない（1秒に1回だけ確かめる）
            overlayMaskTimer -= Time.unscaledDeltaTime;
            if (overlayMaskTimer <= 0f)
            {
                overlayMaskTimer = 1f;
                int bit = 1 << overlayLayer;
                foreach (var c in Camera.allCameras)
                    if (c != overlayCam && (c.cullingMask & bit) != 0) c.cullingMask &= ~bit;
            }

            // ゲームのカメラと同じ所から、同じ写り方で描く
            overlayCam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            overlayCam.fieldOfView = main.fieldOfView;
            overlayCam.nearClipPlane = main.nearClipPlane;
            overlayCam.farClipPlane = main.farClipPlane;

            var target = main.targetTexture;
            if (target != null)
            {
                if (overlayTexture == null || overlayTexture.width != target.width || overlayTexture.height != target.height)
                {
                    if (overlayTexture != null) { overlayTexture.Release(); Destroy(overlayTexture); }
                    overlayTexture = new RenderTexture(target.width, target.height, 24, RenderTextureFormat.ARGB32)
                    {
                        name = "WeatherCaster",
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    overlayTexture.Create();
                }
                overlayCam.targetTexture = overlayTexture;
                overlayCam.clearFlags = CameraClearFlags.SolidColor;
                overlayCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                overlayImage.texture = overlayTexture;
                // ゲームの画面（ArcadeCamera）の Canvas から +3（モニターの画面は +1）
                var arcade = main.GetComponent<ArcadeCamera>();
                overlayCanvas.sortingOrder = (arcade != null ? arcade.sortingOrder : -100) + 3;
                if (overlayDotMat != null && overlayImage.material == overlayDotMat)
                {
                    overlayDotMat.SetVector("_DotCount", new Vector4(target.width, target.height, 0f, 0f));
                    var src = arcade != null ? arcade.screenMaterial : null;
                    if (src != null)
                    {
                        foreach (var prop in new[] { "_ColorLevels", "_DitherStrength", "_OutlineStrength", "_OutlineThreshold", "_ScanlineStrength" })
                            if (src.HasProperty(prop)) overlayDotMat.SetFloat(prop, src.GetFloat(prop));
                        if (src.HasProperty("_OutlineColor")) overlayDotMat.SetColor("_OutlineColor", src.GetColor("_OutlineColor"));
                    }
                }
                if (!overlayCanvas.gameObject.activeSelf) overlayCanvas.gameObject.SetActive(true);
            }
            else
            {
                // ドット化していない時は、画面に直接、ゲームのカメラの後に描く
                overlayCam.targetTexture = null;
                overlayCam.clearFlags = CameraClearFlags.Depth;
                overlayCam.depth = main.depth + 1f;
                if (overlayCanvas.gameObject.activeSelf) overlayCanvas.gameObject.SetActive(false);
            }
        }

        /// <summary>正面（-Z）がカメラの方を向く回転（横の向きだけ）</summary>
        static Quaternion FaceCamera(Camera cam, Vector3 pos)
        {
            if (cam == null) return Quaternion.identity;
            Vector3 d = pos - cam.transform.position;
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
        }

        void UpdateTv(float t)
        {
            if (!tv.gameObject.activeSelf) return;
            // 文字の絵（フォントのテクスチャ）は作り直されることがあるので、毎回合わせる
            if (textMaterial != null && UiFont != null && textMaterial.mainTexture != UiFont.material.mainTexture)
                textMaterial.mainTexture = UiFont.material.mainTexture;
            if (outlineMaterial != null && UiFont != null && outlineMaterial.mainTexture != UiFont.material.mainTexture)
                outlineMaterial.mainTexture = UiFont.material.mainTexture;
            Kind k = now;
            for (int i = 0; i < KindCount; i++)
            {
                bool on = i == (int)k;
                if (tvIcons[i].gameObject.activeSelf != on) tvIcons[i].gameObject.SetActive(on);
            }
            float pop = 1f + castTalk * 0.35f * Mathf.Sin(castTalk * Mathf.PI);
            tvIconRoot.localScale = Vector3.one * (pop + 0.04f * Mathf.Sin(t * 2.5f));
            if (sunIconRays != null) sunIconRays.localRotation = Quaternion.Euler(0f, 0f, -t * 40f);
            if (typhoonIconSpin != null) typhoonIconSpin.localRotation = Quaternion.Euler(0f, 0f, t * 220f);
            foreach (var f in fallers)
            {
                if (!f.t.gameObject.activeInHierarchy) continue;
                float ph = Mathf.Repeat(t * f.speed + f.offset, 1f);
                f.t.localPosition = f.basePos + Vector3.down * (ph * f.span);
                if (f.spin) f.t.localRotation = Quaternion.AngleAxis(t * 160f, f.spinAxis) * f.baseRot;
            }
            var stormBolt = tvIcons[(int)Kind.Storm].Find("Bolt");
            if (stormBolt != null) stormBolt.gameObject.SetActive(Mathf.Repeat(t * 1.3f, 1f) < 0.55f || boltTime > 0f);

            Color c = KindColors[(int)k];
            SetGlow(tvScreenMat, Color.Lerp(TvColors[(int)k], Color.white, skyFlash * 0.25f), 0.6f + castTalk * 0.5f);
            SetGlow(tvBezelMat, c, 2f + Mathf.Sin(t * 4f) * 0.6f);
            bool blink = Mathf.Repeat(t * 1.5f, 1f) < 0.5f;
            SetGlow(tvLampMatA, new Color(1f, 0.3f, 0.4f), blink ? 3f : 0.3f);
            SetGlow(tvLampMatB, new Color(0.3f, 1f, 0.6f), blink ? 0.3f : 3f);

            tvName.text = KindNames[(int)k];
            tvName.height = KindNames[(int)k].Length > 3 ? 0.34f : 0.5f;
            tvName.color = Color.Lerp(Color.white, c, 0.25f);
            tvEnglish.text = KindEnglish[(int)k];
            tvTimer.text = Active ? $"あと {Mathf.Max(0, Mathf.CeilToInt(timer))}秒" : "";
            tvNote.text = EffectShort(k);
        }

        string EffectShort(Kind k) => k switch
        {
            Kind.Sunny => $"中央の穴 スコア×{sunnyMultiplier:0.#}",
            Kind.Rain => "メダルがすべる！",
            Kind.Snow => snowFrozen ? "プッシャー停止中" : "まもなくプッシャー停止",
            Kind.Storm => "雷とスリップに注意！",
            Kind.Typhoon => "竜巻がメダルを巻き込む",
            _ => "空からメダルが降る！",
        };

        static float Ease(float x)
        {
            x = Mathf.Clamp01(x);
            return 1f - (1f - x) * (1f - x) * (1f - x);
        }

        static float EaseBack(float x)
        {
            x = Mathf.Clamp01(x);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float y = x - 1f;
            return 1f + c3 * y * y * y + c1 * y * y;
        }

        // ================================================================
        // モニター（ゲームのモニターに映す天気予報の番組）
        // ================================================================

        RectTransform iconNow;
        RectTransform historyRow;
        Text nowText, timerText, statusText, effectText;
        Image nowBox, timerBar;
        Kind shownNow = (Kind)(-1);
        int shownHistory = -1;

        void DrawIcon(RectTransform icon, Kind k, float size)
        {
            for (int i = icon.childCount - 1; i >= 0; i--) Destroy(icon.GetChild(i).gameObject);
            float c = size * 0.5f;
            Color col = KindColors[(int)k];
            switch (k)
            {
                case Kind.Sunny:
                    for (int i = 0; i < 8; i++)
                    {
                        var ray = Img(icon, $"Ray{i}", c, c, size * 0.08f, size * 0.95f, col);
                        ray.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 22.5f);
                    }
                    Img(icon, "Sun", c, c, size * 0.6f, size * 0.6f, col, MonitorDisplay.ScreenShape.Circle);
                    break;
                case Kind.Rain:
                    Img(icon, "Cloud", c, c * 0.8f, size * 0.9f, size * 0.5f, new Color(0.7f, 0.74f, 0.82f), MonitorDisplay.ScreenShape.Pill);
                    for (int i = 0; i < 3; i++) Img(icon, $"Drop{i}", c - size * 0.25f + i * size * 0.25f, c * 1.5f, size * 0.07f, size * 0.22f, col, MonitorDisplay.ScreenShape.Pill);
                    break;
                case Kind.Snow:
                    for (int i = 0; i < 3; i++)
                    {
                        var arm = Img(icon, $"Arm{i}", c, c, size * 0.08f, size * 0.85f, col);
                        arm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 60f);
                    }
                    Img(icon, "Core", c, c, size * 0.25f, size * 0.25f, col, MonitorDisplay.ScreenShape.Circle);
                    break;
                case Kind.Storm:
                    Img(icon, "Cloud", c, c * 0.7f, size * 0.95f, size * 0.5f, new Color(0.3f, 0.3f, 0.38f), MonitorDisplay.ScreenShape.Pill);
                    var b1 = Img(icon, "Bolt1", c + size * 0.03f, c * 1.2f, size * 0.12f, size * 0.32f, new Color(1f, 0.93f, 0.4f));
                    b1.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 25f);
                    var b2 = Img(icon, "Bolt2", c - size * 0.03f, c * 1.55f, size * 0.12f, size * 0.32f, new Color(1f, 0.93f, 0.4f));
                    b2.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 25f);
                    Img(icon, "Drop", c - size * 0.3f, c * 1.45f, size * 0.07f, size * 0.2f, KindColors[(int)Kind.Rain], MonitorDisplay.ScreenShape.Pill);
                    Img(icon, "Drop2", c + size * 0.3f, c * 1.45f, size * 0.07f, size * 0.2f, KindColors[(int)Kind.Rain], MonitorDisplay.ScreenShape.Pill);
                    break;
                case Kind.Typhoon:
                    Img(icon, "R1", c, c, size * 0.9f, size * 0.9f, col, MonitorDisplay.ScreenShape.Ring);
                    Img(icon, "R2", c + size * 0.05f, c, size * 0.6f, size * 0.6f, col, MonitorDisplay.ScreenShape.Ring);
                    Img(icon, "R3", c + size * 0.08f, c, size * 0.3f, size * 0.3f, col, MonitorDisplay.ScreenShape.Ring);
                    Img(icon, "Eye", c + size * 0.08f, c, size * 0.1f, size * 0.1f, Color.white, MonitorDisplay.ScreenShape.Circle);
                    break;
                default:
                    Img(icon, "Cloud", c, c * 0.7f, size * 0.95f, size * 0.5f, new Color(1f, 0.8f, 0.2f), MonitorDisplay.ScreenShape.Pill);
                    for (int i = 0; i < 3; i++)
                        Img(icon, $"Coin{i}", c - size * 0.28f + i * size * 0.28f, c * 1.5f + (i % 2) * size * 0.08f, size * 0.2f, size * 0.2f, col, MonitorDisplay.ScreenShape.Circle);
                    break;
            }
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var studio = Panel(Screen, "Studio", W * 0.5f, 200f, W - 48f, 360f, Studio);
            float sw = W - 48f;
            Tag(studio, "News", 110f, 30f, 180f, 34f, "WEATHER NEWS", 16, KindColors[0], new Color32(0x10, 0x10, 0x18, 0xff));
            // キャスター
            float cx = sw * 0.15f;
            Img(studio, "CasterBody", cx, 290f, 120f, 150f, new Color32(0x23, 0x40, 0x7e, 0xff), MonitorDisplay.ScreenShape.Pill);
            Img(studio, "CasterBlouse", cx, 240f, 30f, 50f, new Color32(0xfa, 0xfa, 0xff, 0xff));
            Img(studio, "CasterHair", cx, 175f, 96f, 96f, new Color32(0x4a, 0x2a, 0x1c, 0xff), MonitorDisplay.ScreenShape.Circle);
            Img(studio, "CasterHead", cx, 185f, 76f, 76f, new Color32(0xff, 0xdc, 0xc4, 0xff), MonitorDisplay.ScreenShape.Circle);
            Img(studio, "CasterBangs", cx, 152f, 84f, 30f, new Color32(0x4a, 0x2a, 0x1c, 0xff), MonitorDisplay.ScreenShape.Pill);
            Img(studio, "CasterRibbon", cx, 220f, 36f, 14f, new Color32(0xff, 0x4f, 0x6a, 0xff), MonitorDisplay.ScreenShape.Pill);
            nowBox = Img(studio, "NowBox", sw * 0.47f, 190f, 260f, 260f, new Color(1f, 1f, 1f, 0.06f), MonitorDisplay.ScreenShape.Pill);
            iconNow = Grp(studio, "IconNow", sw * 0.47f, 170f, 160f, 160f);
            nowText = Lbl(studio, "Now", sw * 0.47f, 300f, 300f, 40f, "", 30, Ink);
            Lbl(studio, "HistoryLabel", sw * 0.8f, 70f, 300f, 30f, "これまでの天気", 18, Mute);
            historyRow = Grp(studio, "History", sw * 0.8f, 150f, 300f, 100f);
            Lbl(studio, "TimerLabel", sw * 0.8f, 235f, 300f, 30f, "つぎの天気まで", 18, Mute);
            timerText = Lbl(studio, "Timer", sw * 0.8f, 270f, 300f, 36f, "", 26, Ink);
            timerBar = Bar(studio, "TimerBar", sw * 0.8f - 130f, 310f, 260f, 12f, KindColors[0]);
            shownNow = (Kind)(-1);
            shownHistory = -1;
            effectText = Lbl(Screen, "Effect", 24f, 440f, W - 48f, 40f, "", 24, Ink, TextAnchor.MiddleLeft);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            if (shownNow != now) { DrawIcon(iconNow, now, 160f); shownNow = now; }
            int hv = history.Count * 16 + (history.Count > 0 ? (int)history[history.Count - 1] : 0);
            if (shownHistory != hv)
            {
                shownHistory = hv;
                for (int i = historyRow.childCount - 1; i >= 0; i--) Destroy(historyRow.GetChild(i).gameObject);
                int n = history.Count;
                for (int i = 0; i < n; i++)
                {
                    var g = Grp(historyRow, "H" + i, 300f - 26f - (n - 1 - i) * 48f, 50f, 44f, 44f);
                    DrawIcon(g, history[i], 44f);
                }
            }
            Color c = KindColors[(int)now];
            nowBox.color = new Color(c.r, c.g, c.b, 0.15f + 0.05f * Mathf.Sin(Time.time * 3f));
            nowText.text = $"いま：{KindNames[(int)now]}";
            timerText.text = $"{Mathf.Max(0, Mathf.CeilToInt(timer))}秒";
            timerBar.fillAmount = Mathf.Clamp01(timer / Mathf.Max(0.1f, changeSeconds));
            timerBar.color = c;
            Scale(iconNow, 1f + 0.04f * Mathf.Sin(Time.time * 2f));
            if (now == Kind.Typhoon) iconNow.localRotation = Quaternion.Euler(0f, 0f, Time.time * 120f);
            else iconNow.localRotation = Quaternion.identity;
            effectText.text = now switch
            {
                Kind.Sunny => $"晴れ：中央の穴のスコア ×{sunnyMultiplier:0.#}",
                Kind.Rain => "雨：盤面がぬれて、メダルがすべりやすい",
                Kind.Snow => $"雪：{snowStopDelay:0.#}秒後にプッシャーが凍って止まる。天気が変わると、積もった雪が爆発",
                Kind.Storm => "雷雨：すべりやすい＋雷が落ちてメダルが吹き飛ぶ",
                Kind.Typhoon => "台風：竜巻がメダルを巻き込んで回る。最後は中央の穴へ",
                _ => "コインの雨：空からメダルが降ってくる！",
            };
            effectText.color = c;
            statusText.text = effects.Remaining <= withdrawSeconds ? "本日の天気予報は以上です。また見てね！" : "天気は10秒ごとに変わる。晴れの時に中央の穴をねらおう";
        }
    }
}
