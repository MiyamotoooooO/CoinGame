using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N020 ヒーロー「メダルナイツ」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：下段（プッシャーの前の盤面）に、オリジナルの変身ヒーロー5人（メダルナイツ）と、敵の怪人（ミュート大魔王）が出てくる
    ///   ヒーロー：赤＝光線銃、青＝剣、黄＝ロケットランチャー、緑＝マシンガン、ピンク＝ハンマー
    ///   （丸いヘルメットに横長のバイザー、おでこにレコードの紋章、銀の肩と胸のよろい）
    ///   怪人：こわれたスピーカーでできた大きな怪人。赤く光る目から光線を出したり、地面をふみつけたり、紫の玉を投げたりする
    /// ・バトル：ヒーローたちは下段を走り回りながら、順番に攻撃する。怪人も反撃してくる。ド派手な光線・爆発・火花
    /// ・曲が終わりそう（残り finishLead 秒）になったら、5人の必殺技で怪人を倒す（大爆発）。
    ///   そのあと5人が盤面のまん中に集まって決めポーズ（うしろで5色の爆発）。最後にジャンプして撤収する
    /// ・効果：攻撃が当たった所・爆発・ヒーローが走った所にあるメダルは、その衝撃で吹き飛ぶ
    /// ・ヒーローと怪人は見た目だけ（当たり判定なし）。メダルを動かすのは、衝撃の時だけ
    /// ・モニター：使わない（いつものラウンドの画面のまま）
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class HeroRecord : RecordScriptBase
    {
        public static HeroRecord Instance { get; private set; }

        [Header("大きさ（メダルの直径に対する倍率）")]
        [Range(0.5f, 6f)] public float heroHeight = 2.4f;
        [Range(1f, 10f)] public float enemyHeight = 4.2f;
        [Tooltip("武器の大きさ（1 がもとの大きさ）")]
        [Range(0.5f, 4f)] public float weaponScale = 1.8f;

        [Header("バトル")]
        [Tooltip("だれかが攻撃する間隔（秒）。この間でばらつく")]
        public Vector2 attackInterval = new Vector2(0.45f, 0.9f);
        [Tooltip("怪人が攻撃する間隔（秒）。この間でばらつく")]
        public Vector2 enemyInterval = new Vector2(2f, 3.2f);
        [Tooltip("ヒーローの走る速さ（メダルの直径 / 秒）")]
        [Range(0.5f, 30f)] public float runSpeed = 8f;
        [Tooltip("怪人の歩く速さ（メダルの直径 / 秒）")]
        [Range(0.2f, 15f)] public float enemySpeed = 4f;
        [Tooltip("ヒーローが空を飛ぶ高さ（メダルの直径に対する倍率）。この間でばらつく")]
        public Vector2 flyHeight = new Vector2(1.8f, 3.5f);
        [Tooltip("ヒーローが空を飛び始めやすさ（1秒あたりの確率）")]
        [Range(0f, 1f)] public float flyChance = 0.3f;
        [Tooltip("曲の残りが何秒になったら、必殺技で怪人を倒すか（このあと決めポーズと撤収）")]
        [Range(3f, 20f)] public float finishLead = 8.5f;

        [Header("効果（メダルが吹き飛ぶ強さ。メダルの直径 / 秒）")]
        [Range(0f, 60f)] public float hitPower = 15f;
        [Range(0f, 80f)] public float explodePower = 22f;
        [Range(0f, 100f)] public float finishPower = 30f;
        [Tooltip("ヒーローが走った所のメダルをはじく強さ（走る速さに対する倍率）")]
        [Range(0f, 4f)] public float runKick = 1.4f;

        [Header("ド派手さ")]
        [Tooltip("攻撃の光・爆発・斬撃の大きさ（1 がふつう）")]
        [Range(0.5f, 4f)] public float fxScale = 1.6f;
        [Tooltip("マシンガンの1回の連射の弾の数")]
        [Range(4, 60)] public int gunBurst = 24;
        [Tooltip("ロケットを1回に何発撃つか")]
        [Range(1, 8)] public int rocketSalvo = 3;

        [Header("ほか")]
        [Tooltip("盤面に出る大きな演出（ヒーローのモデル）を出さない（ヒーローと怪人が多いので、盤面がごちゃごちゃしないように）")]
        public bool hideBigSpectacle = true;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip beamSound;
        public AudioClip slashSound;
        public AudioClip boomSound;
        public AudioClip gunSound;
        public AudioClip finishSound;

        protected override string DefaultKey => "Hero";
        protected override string ScreenTitle => "HERO";
        protected override string ScreenSubtitle => "メダルナイツ";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;   // モニターはいつもの画面のまま

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<HeroRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color[] HeroColors =
        {
            new Color32(0xff, 0x2e, 0x3a, 0xff), new Color32(0x2e, 0x7c, 0xff, 0xff), new Color32(0xff, 0xd0, 0x20, 0xff),
            new Color32(0x2e, 0xd0, 0x6a, 0xff), new Color32(0xff, 0x6e, 0xc8, 0xff),
        };
        static readonly string[] HeroNames = { "RED", "BLUE", "YELLOW", "GREEN", "PINK" };
        static readonly Color EnemyGlow = new Color32(0xff, 0x30, 0x30, 0xff);
        static readonly Color Orb = new Color32(0xb0, 0x40, 0xff, 0xff);
        static readonly Color Fire = new Color32(0xff, 0x9a, 0x3c, 0xff);

        enum Phase { None, Enter, Battle, Finisher, Blowup, Gather, Pose, Leave, Done }
        enum Weapon { Ray, Sword, Rocket, Gun, Hammer }

        Phase phase;
        float phaseTime;

        // ---- 盤面の場所 ----
        float cz, depth;
        float sH, sE;   // ヒーロー・怪人の「1」の大きさ（どちらも高さ 2 の形で作る）

        // ---- ヒーロー ----
        class Hero
        {
            public Weapon weapon;
            public Color color;
            public Transform root, torso, head, armL, armR, legL, legR, muzzle;
            public Material suit;
            public Vector3 pos, target, vel;
            public float walk, act, hitT, jumpY, jumpV;
            public float air, airTarget, airTimer;   // 空を飛ぶ高さ・目標の高さ・残りの秒数
            public float airVel, phase;              // なめらかに上下するための速さ・ふわふわの位相
            public Vector3 velS;                     // なめらかにした速さ（急に止まったり曲がったりしない）
            public Vector3 dashSide;                 // 突進する時の、怪人のどちら側か
            public float poseDelay, poseAt = -1f;     // 決めポーズを決める時間・決めた時間
            public TextMesh nameTag;                 // 決めポーズの時に頭の上に出る名前
            public float busy;           // 攻撃のポーズの残り秒数
            public int burstLeft;        // マシンガンの残りの弾
            public float burstTimer;
            public bool dash;            // 剣・ハンマーの突進中
        }
        readonly List<Hero> heroes = new List<Hero>();
        float nextAttack;
        int lastAttacker = -1;

        // ---- 怪人 ----
        Transform enemy, enemyBody, enemyHead, enemyArmL, enemyArmR, enemyLegL, enemyLegR;
        Material enemySkin, enemyEye;
        Vector3 enemyPos;
        float enemyFlash, enemyTimer, enemyAnim, enemyShow, enemyJump;
        Vector3 enemyTarget, enemyVel;
        float enemyWalk, enemyWander;
        int enemyMove;              // 0 なし、1 光線、2 ふみつけ、3 玉
        float laserT = -1f;
        LineRenderer laserCore, laserGlow;

        // ---- 光線・弾・ロケット・斬撃など ----
        class Beam { public LineRenderer core, glow; public float age, life; public Vector3 a, b; }
        readonly List<Beam> beams = new List<Beam>();
        class Shot
        {
            public Transform t;
            public Vector3 from, to, v;
            public float age, life, arc;
            public int kind;          // 0 弾、1 ロケット、2 紫の玉、3 銃の薬きょう（飛び散るだけ）
            public Color color;
        }
        readonly List<Shot> shots = new List<Shot>();
        // kind 0 床の光の輪、1 斬撃、2 光の玉（爆発の火の玉・発射の光）、3 飛ぶ斬撃、4 光線にそって進む光の輪
        class Fx { public Transform t; public Material m; public float age, life, size; public int kind; public Color color; public Vector3 a, b; }
        readonly List<Fx> fxs = new List<Fx>();

        ParticleSystem sparks, smoke, fire, dust, stars;
        Quaternion? fxFace;   // 光の玉をカメラに向けるための向き
        Material casingMat;
        Light flashLight;
        float flash;
        Material tracerMat, rocketMat, orbMat, slashMat;

        readonly List<Renderer> frontParts = new List<Renderer>();
        Renderer monitorRenderer;
        float monitorSearch;
        readonly List<Medal> near = new List<Medal>();
        float lastGunSound;

        AudioClip madeBeam, madeSlash, madeBoom, madeGun, madeFinish, madeLaser, madeStomp, madeJump, madeSnap;

        // ================================================================
        // 部品
        // ================================================================

        static Material Mat(Color c, float metal = 0.1f, float smooth = 0.6f, float glow = 0.15f)
        {
            var m = ProtoAssets.MakeMaterial(c, metal, smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            return m;
        }

        static Transform Pivot(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        protected override void BuildBoard()
        {
            depth = Mathf.Max(D, PusherFrontZ - FrontZ);
            cz = (FrontZ + PusherFrontZ) * 0.5f;
            sH = D * heroHeight / 2f;
            sE = D * enemyHeight / 2f;

            for (int i = 0; i < 5; i++) heroes.Add(BuildHero((Weapon)i, HeroColors[i]));
            foreach (var h in heroes) h.nameTag = NameTag(HeroNames[(int)h.weapon], h.color);
            BuildEnemy();

            sparks = Particles("Sparks", Board, Vector3.up, D * 5f, D * 0.08f, 0.5f, 1f, 70f);
            smoke = Particles("Smoke", Board, Vector3.up, D * 1.5f, D * 0.8f, 1.3f, -0.05f, 70f);
            fire = Particles("Flame", Board, Vector3.up, D * 3f, D * 0.35f, 0.6f, -0.1f, 60f);
            dust = Particles("Dust", Board, Vector3.up, D * 1f, D * 0.35f, 0.8f, 0f, 80f);
            stars = Particles("Stars", Board, Vector3.up, D * 3f, D * 0.12f, 1f, 0.2f, 80f);
            flashLight = PointLight("BattleLight", Board, new Vector3(0f, D * 2f, cz), Fire, Layout.boardWidth * 1.4f);

            tracerMat = RecordMeshKit.Additive(RecordMeshKit.SoftDot, new Color(0.6f, 1f, 0.6f));
            rocketMat = Mat(new Color(0.85f, 0.85f, 0.9f), 0.6f, 0.8f, 0.2f);
            orbMat = RecordMeshKit.Additive(RecordMeshKit.SoftDot, Orb);
            slashMat = RecordMeshKit.Additive(Texture2D.whiteTexture, Color.white);
            casingMat = Mat(new Color(1f, 0.78f, 0.3f), 0.9f, 0.85f, 0.3f);

            laserGlow = Line("EnemyLaserGlow", 2, sE * 0.35f, RecordMeshKit.Additive(RecordMeshKit.SoftDot, EnemyGlow));
            laserCore = Line("EnemyLaserCore", 2, sE * 0.08f, RecordMeshKit.Additive(Texture2D.whiteTexture, new Color(1f, 0.85f, 0.8f)));
            for (int i = 0; i < 8; i++)
            {
                beams.Add(new Beam
                {
                    glow = Line("BeamGlow", 2, D * 0.35f, RecordMeshKit.Additive(RecordMeshKit.SoftDot, Color.white)),
                    core = Line("BeamCore", 2, D * 0.08f, RecordMeshKit.Additive(Texture2D.whiteTexture, Color.white)),
                    age = -1f,
                });
            }

            foreach (var r in Board.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer)) frontParts.Add(r);
            HideAll();
            SkipBigSpectacle();
        }

        static Font tagFont;

        /// <summary>決めポーズの時に頭の上に出る名前（ふだんは隠しておく）</summary>
        TextMesh NameTag(string text, Color c)
        {
            if (tagFont == null)
            {
                try { tagFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { tagFont = null; }
            }
            var go = new GameObject("NameTag_" + text);
            go.transform.SetParent(Board, false);
            var tm = go.AddComponent<TextMesh>();
            if (tagFont != null)
            {
                tm.font = tagFont;
                go.GetComponent<MeshRenderer>().sharedMaterial = tagFont.material;
            }
            tm.text = text;
            tm.fontSize = 96;
            tm.fontStyle = FontStyle.Bold;
            tm.characterSize = D * 0.5f / 9.6f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = c;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return tm;
        }

        LineRenderer Line(string name, int points, float width, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Board, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = points;
            lr.widthMultiplier = width;
            lr.sharedMaterial = m;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        /// <summary>ヒーロー（高さ 2 の形を sH 倍。前が +Z）</summary>
        Hero BuildHero(Weapon w, Color color)
        {
            var h = new Hero { weapon = w, color = color };
            float s = sH;
            h.suit = Mat(color, 0.2f, 0.75f, 0.35f);
            var black = Mat(new Color(0.08f, 0.08f, 0.1f), 0.3f, 0.7f, 0f);
            var silver = Mat(new Color(0.85f, 0.87f, 0.92f), 0.9f, 0.9f, 0.15f);
            var visor = Mat(new Color(0.05f, 0.06f, 0.12f), 0.9f, 0.97f, 0.05f);
            var gold = Mat(new Color(1f, 0.8f, 0.3f), 0.9f, 0.8f, 0.5f);

            h.root = Pivot("Hero_" + HeroNames[(int)w], Board, Vector3.zero);
            // 脚（腰から下へ）
            for (int k = -1; k <= 1; k += 2)
            {
                var leg = Pivot(k < 0 ? "LegL" : "LegR", h.root, new Vector3(k * s * 0.13f, s * 0.95f, 0f));
                Shape(PrimitiveType.Capsule, "Leg", leg, new Vector3(0f, -s * 0.45f, 0f), new Vector3(s * 0.17f, s * 0.47f, s * 0.17f), h.suit);
                Shape(PrimitiveType.Capsule, "Boot", leg, new Vector3(0f, -s * 0.82f, s * 0.03f), new Vector3(s * 0.2f, s * 0.13f, s * 0.24f), silver);
                if (k < 0) h.legL = leg; else h.legR = leg;
            }
            // 体
            h.torso = Pivot("Torso", h.root, new Vector3(0f, s * 0.95f, 0f));
            Shape(PrimitiveType.Capsule, "Body", h.torso, new Vector3(0f, s * 0.35f, 0f), new Vector3(s * 0.46f, s * 0.38f, s * 0.3f), h.suit);
            Cube("Waist", h.torso, new Vector3(0f, s * 0.04f, 0f), new Vector3(s * 0.47f, s * 0.08f, s * 0.31f), black);
            Cube("ChestPlate", h.torso, new Vector3(0f, s * 0.5f, s * 0.13f), new Vector3(s * 0.38f, s * 0.24f, s * 0.08f), silver);
            Shape(PrimitiveType.Cylinder, "ChestMedal", h.torso, new Vector3(0f, s * 0.5f, s * 0.18f), new Vector3(s * 0.13f, s * 0.015f, s * 0.13f), gold).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int k = -1; k <= 1; k += 2)
                Shape(PrimitiveType.Sphere, "Shoulder", h.torso, new Vector3(k * s * 0.27f, s * 0.62f, 0f), new Vector3(s * 0.2f, s * 0.14f, s * 0.22f), silver);
            // 頭：丸いヘルメット、横長の暗いバイザー、おでこのレコードの紋章
            h.head = Pivot("Head", h.torso, new Vector3(0f, s * 0.75f, 0f));
            Shape(PrimitiveType.Sphere, "Helmet", h.head, new Vector3(0f, s * 0.18f, 0f), new Vector3(s * 0.34f, s * 0.36f, s * 0.36f), h.suit);
            Shape(PrimitiveType.Sphere, "Visor", h.head, new Vector3(0f, s * 0.17f, s * 0.07f), new Vector3(s * 0.3f, s * 0.11f, s * 0.24f), visor);
            Cube("Chin", h.head, new Vector3(0f, s * 0.05f, s * 0.12f), new Vector3(s * 0.16f, s * 0.07f, s * 0.08f), silver);
            Shape(PrimitiveType.Cylinder, "Disc", h.head, new Vector3(0f, s * 0.3f, s * 0.15f), new Vector3(s * 0.12f, s * 0.01f, s * 0.12f), black).transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            Shape(PrimitiveType.Cylinder, "Label", h.head, new Vector3(0f, s * 0.302f, s * 0.157f), new Vector3(s * 0.05f, s * 0.012f, s * 0.05f), Mat(Color.Lerp(color, Color.white, 0.4f), 0f, 0.5f, 1.2f)).transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            // 腕（肩から下へ）。右手に武器
            for (int k = -1; k <= 1; k += 2)
            {
                var arm = Pivot(k < 0 ? "ArmL" : "ArmR", h.torso, new Vector3(k * s * 0.3f, s * 0.6f, 0f));
                Shape(PrimitiveType.Capsule, "Arm", arm, new Vector3(0f, -s * 0.24f, 0f), new Vector3(s * 0.13f, s * 0.25f, s * 0.13f), h.suit);
                Shape(PrimitiveType.Sphere, "Glove", arm, new Vector3(0f, -s * 0.5f, 0f), Vector3.one * s * 0.13f, silver);
                if (k < 0) h.armL = arm; else h.armR = arm;
            }
            BuildWeapon(h, s, black, silver);
            return h;
        }

        /// <summary>右手（腕を前に上げると、武器が前を向く）の武器</summary>
        void BuildWeapon(Hero h, float s, Material black, Material silver)
        {
            var glow = Mat(h.color, 0.2f, 0.8f, 2f);
            var hand = Pivot("Weapon", h.armR, new Vector3(0f, -s * 0.52f, 0f));
            hand.localScale = Vector3.one * weaponScale;
            // 腕は下向き（-Y）が伸びる向き。武器はその先（-Y）へ伸ばす
            switch (h.weapon)
            {
                case Weapon.Ray:
                    Cube("Grip", hand, new Vector3(0f, -s * 0.02f, -s * 0.05f), new Vector3(s * 0.06f, s * 0.08f, s * 0.12f), black);
                    Cube("Body", hand, new Vector3(0f, -s * 0.14f, 0f), new Vector3(s * 0.09f, s * 0.24f, s * 0.11f), silver);
                    Shape(PrimitiveType.Sphere, "Tip", hand, new Vector3(0f, -s * 0.28f, 0f), Vector3.one * s * 0.08f, glow);
                    h.muzzle = Pivot("Muzzle", hand, new Vector3(0f, -s * 0.32f, 0f));
                    break;
                case Weapon.Sword:
                    Cube("Hilt", hand, new Vector3(0f, -s * 0.02f, 0f), new Vector3(s * 0.2f, s * 0.04f, s * 0.05f), silver);
                    Cube("Blade", hand, new Vector3(0f, -s * 0.42f, 0f), new Vector3(s * 0.07f, s * 0.75f, s * 0.02f), glow);
                    h.muzzle = Pivot("Muzzle", hand, new Vector3(0f, -s * 0.8f, 0f));
                    break;
                case Weapon.Rocket:
                    // 肩にかつぐ筒（右肩の上）
                    var tube = Pivot("Launcher", h.torso, new Vector3(s * 0.32f, s * 0.75f, 0f));
                    tube.localScale = Vector3.one * weaponScale;
                    Shape(PrimitiveType.Cylinder, "Tube", tube, new Vector3(0f, 0f, s * 0.12f), new Vector3(s * 0.16f, s * 0.38f, s * 0.16f), Mat(new Color(0.3f, 0.35f, 0.3f), 0.5f, 0.6f, 0.05f)).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Shape(PrimitiveType.Cylinder, "Ring", tube, new Vector3(0f, 0f, s * 0.48f), new Vector3(s * 0.19f, s * 0.03f, s * 0.19f), glow).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    h.muzzle = Pivot("Muzzle", tube, new Vector3(0f, 0f, s * 0.52f));
                    Cube("Grip", hand, new Vector3(0f, -s * 0.03f, 0f), new Vector3(s * 0.06f, s * 0.08f, s * 0.08f), black);
                    break;
                case Weapon.Gun:
                    Cube("Body", hand, new Vector3(0f, -s * 0.12f, 0f), new Vector3(s * 0.1f, s * 0.32f, s * 0.13f), black);
                    Shape(PrimitiveType.Cylinder, "Barrel", hand, new Vector3(0f, -s * 0.38f, 0f), new Vector3(s * 0.05f, s * 0.12f, s * 0.05f), silver);
                    Cube("Mag", hand, new Vector3(0f, -s * 0.1f, s * 0.09f), new Vector3(s * 0.05f, s * 0.08f, s * 0.08f), glow);
                    h.muzzle = Pivot("Muzzle", hand, new Vector3(0f, -s * 0.52f, 0f));
                    break;
                case Weapon.Hammer:
                    Shape(PrimitiveType.Cylinder, "Handle", hand, new Vector3(0f, -s * 0.25f, 0f), new Vector3(s * 0.05f, s * 0.3f, s * 0.05f), black);
                    var head = Cube("HammerHead", hand, new Vector3(0f, -s * 0.58f, 0f), new Vector3(s * 0.36f, s * 0.2f, s * 0.2f), silver);
                    Cube("Face", hand, new Vector3(0f, -s * 0.58f, s * 0.105f), new Vector3(s * 0.3f, s * 0.15f, s * 0.02f), glow);
                    h.muzzle = head.transform;
                    break;
            }
        }

        /// <summary>怪人「ミュート大魔王」（こわれたスピーカーの体。高さ 2 の形を sE 倍。前が +Z）</summary>
        void BuildEnemy()
        {
            float s = sE;
            enemySkin = Mat(new Color(0.22f, 0.12f, 0.28f), 0.4f, 0.7f, 0.1f);
            var dark = Mat(new Color(0.06f, 0.05f, 0.08f), 0.5f, 0.8f, 0f);
            var cone = Mat(new Color(0.12f, 0.1f, 0.14f), 0.2f, 0.4f, 0.05f);
            enemyEye = Mat(EnemyGlow, 0f, 0.8f, 3f);
            var spike = Mat(new Color(0.55f, 0.15f, 0.2f), 0.4f, 0.7f, 0.4f);

            enemy = Pivot("MuteKing", Board, Vector3.zero);
            enemyBody = Pivot("Body", enemy, Vector3.zero);
            // 脚（腰から下へ。歩く時にふる）
            for (int k = -1; k <= 1; k += 2)
            {
                var leg = Pivot(k < 0 ? "LegL" : "LegR", enemyBody, new Vector3(k * s * 0.22f, s * 0.7f, 0f));
                Shape(PrimitiveType.Capsule, "Leg", leg, new Vector3(0f, -s * 0.35f, 0f), new Vector3(s * 0.26f, s * 0.36f, s * 0.26f), enemySkin);
                Cube("Foot", leg, new Vector3(0f, -s * 0.65f, s * 0.08f), new Vector3(s * 0.3f, s * 0.1f, s * 0.4f), dark);
                if (k < 0) enemyLegL = leg; else enemyLegR = leg;
            }
            // 胴（スピーカーの箱。前に大きなコーン）
            Cube("Box", enemyBody, new Vector3(0f, s * 1.05f, 0f), new Vector3(s * 0.85f, s * 0.75f, s * 0.55f), enemySkin);
            Shape(PrimitiveType.Cylinder, "Cone", enemyBody, new Vector3(0f, s * 1.0f, s * 0.28f), new Vector3(s * 0.55f, s * 0.03f, s * 0.55f), cone).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shape(PrimitiveType.Sphere, "Dust", enemyBody, new Vector3(0f, s * 1.0f, s * 0.3f), new Vector3(s * 0.18f, s * 0.18f, s * 0.08f), Mat(EnemyGlow, 0f, 0.6f, 1.2f));
            Cube("Crack", enemyBody, new Vector3(s * 0.2f, s * 1.25f, s * 0.29f), new Vector3(s * 0.25f, s * 0.02f, s * 0.01f), Mat(EnemyGlow, 0f, 0.6f, 2f)).transform.localRotation = Quaternion.Euler(0f, 0f, -30f);
            // 肩のとげ
            for (int k = -1; k <= 1; k += 2)
                for (int i = 0; i < 3; i++)
                {
                    var sp = Cube("Spike", enemyBody, new Vector3(k * s * (0.38f + i * 0.06f), s * (1.45f + i * 0.04f), -s * 0.05f + i * s * 0.08f), new Vector3(s * 0.08f, s * 0.24f, s * 0.08f), spike);
                    sp.transform.localRotation = Quaternion.Euler(0f, 0f, -k * (25f + i * 10f));
                }
            // 頭（小さなスピーカー。赤く光る目）
            enemyHead = Pivot("Head", enemyBody, new Vector3(0f, s * 1.5f, 0f));
            Cube("HeadBox", enemyHead, new Vector3(0f, s * 0.2f, 0f), new Vector3(s * 0.45f, s * 0.38f, s * 0.4f), enemySkin);
            Shape(PrimitiveType.Cylinder, "Eye", enemyHead, new Vector3(0f, s * 0.22f, s * 0.2f), new Vector3(s * 0.24f, s * 0.02f, s * 0.24f), enemyEye).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int k = -1; k <= 1; k += 2)
            {
                var horn = Cube("Horn", enemyHead, new Vector3(k * s * 0.2f, s * 0.45f, 0f), new Vector3(s * 0.06f, s * 0.22f, s * 0.06f), spike);
                horn.transform.localRotation = Quaternion.Euler(0f, 0f, -k * 30f);
            }
            // 腕（大きなつめ）
            for (int k = -1; k <= 1; k += 2)
            {
                var arm = Pivot(k < 0 ? "ArmL" : "ArmR", enemyBody, new Vector3(k * s * 0.52f, s * 1.35f, 0f));
                Shape(PrimitiveType.Capsule, "Arm", arm, new Vector3(0f, -s * 0.3f, 0f), new Vector3(s * 0.2f, s * 0.32f, s * 0.2f), enemySkin);
                for (int c = -1; c <= 1; c++)
                {
                    var claw = Cube("Claw", arm, new Vector3(c * s * 0.06f, -s * 0.7f, s * 0.04f), new Vector3(s * 0.04f, s * 0.18f, s * 0.04f), spike);
                    claw.transform.localRotation = Quaternion.Euler(20f, 0f, c * 12f);
                }
                if (k < 0) enemyArmL = arm; else enemyArmR = arm;
            }
        }

        void SkipBigSpectacle()
        {
            if (!hideBigSpectacle) return;
            var dir = RecordSpectacleDirector.Instance != null ? RecordSpectacleDirector.Instance : FindFirstObjectByType<RecordSpectacleDirector>();
            if (dir == null) return;
            if (dir.skipKeys == null) dir.skipKeys = new List<string>();
            if (!dir.skipKeys.Contains(recordKey)) dir.skipKeys.Add(recordKey);
        }

        void HideAll()
        {
            foreach (var h in heroes) { h.root.gameObject.SetActive(false); h.nameTag.gameObject.SetActive(false); h.poseAt = -1f; }
            enemy.gameObject.SetActive(false);
            foreach (var b in beams) { b.age = -1f; b.core.enabled = b.glow.enabled = false; }
            laserCore.enabled = laserGlow.enabled = false;
            laserT = -1f;
            foreach (var s in shots) if (s.t != null) Destroy(s.t.gameObject);
            shots.Clear();
            foreach (var f in fxs) { if (f.t != null) Destroy(f.t.gameObject); if (f.m != null) Destroy(f.m); }
            fxs.Clear();
            pending.Clear();
            phase = Phase.None;
        }

        // ================================================================
        // 場所
        // ================================================================

        Vector3 EnemyHome => new Vector3(0f, 0f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.62f));

        /// <summary>下段の、怪人から少しはなれた場所（ヒーローが走っていく所）</summary>
        Vector3 RandomSpot(Vector3 from)
        {
            for (int i = 0; i < 12; i++)
            {
                var p = new Vector3(Random.Range(-HalfW, HalfW) * 0.82f, 0f, Mathf.Lerp(FrontZ + D * 0.5f, PusherFrontZ - D * 0.3f, Random.value));
                Vector3 d = p - enemyPos;
                d.y = 0f;
                if (d.magnitude > sE * 0.9f + D * 0.6f && (p - from).magnitude > D * 1f) return p;
            }
            return new Vector3(Random.value < 0.5f ? -HalfW * 0.7f : HalfW * 0.7f, 0f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.3f));
        }

        Vector3 EnemyChest => enemyPos + Vector3.up * (sE * 1.05f + enemyJump);
        Vector3 EnemyFeet => enemyPos;

        // ================================================================
        // 流れ
        // ================================================================

        protected override void OnBegin()
        {
            SkipBigSpectacle();
            HideAll();
            enemyPos = EnemyHome;
            enemy.position = enemyPos;
            enemy.rotation = Quaternion.Euler(0f, 180f, 0f);   // 手前（カメラ）を向く
            enemy.gameObject.SetActive(true);
            enemyShow = 0f;
            enemyFlash = 0f;
            enemyJump = 0f;
            enemyMove = 0;
            enemyTimer = 2.5f;
            enemyTarget = enemyPos;
            enemyVel = Vector3.zero;
            enemyWander = 2f;
            for (int i = 0; i < heroes.Count; i++)
            {
                var h = heroes[i];
                h.root.gameObject.SetActive(true);
                h.pos = RandomSpot(new Vector3(0f, 0f, FrontZ));
                h.target = h.pos;
                h.jumpY = D * 6f + i * D * 0.8f;   // 空から順番に落ちてくる
                h.jumpV = 0f;
                h.busy = 0f; h.hitT = 0f; h.burstLeft = 0; h.dash = false;
                h.air = 0f; h.airTarget = 0f; h.airTimer = 0f; h.airVel = 0f;
                h.velS = Vector3.zero;
                h.phase = i * 1.3f;
                h.root.position = h.pos + Vector3.up * h.jumpY;
            }
            nextAttack = 1.6f;
            SetPhase(Phase.Enter);
            PopCenter("HERO TIME!!", HeroColors[0]);
        }

        protected override void OnEnd()
        {
            foreach (var h in heroes) if (h.root.gameObject.activeSelf) Emit(stars, h.root.position + Vector3.up * sH, h.color, 10);
            HideAll();
        }

        protected override void OnStateChanged(GameState state)
        {
            bool scoring = state == GameState.Playing || state == GameState.Grace;
            if (!scoring && Active) HideAll();
        }

        void SetPhase(Phase p)
        {
            phase = p;
            phaseTime = 0f;
        }

        protected override void Tick(float dt, bool running)
        {
            if (!running || phase == Phase.None || phase == Phase.Done) return;
            phaseTime += dt;
            float left = effects.Remaining;
            switch (phase)
            {
                case Phase.Enter:
                    if (phaseTime > 1.4f) SetPhase(Phase.Battle);
                    break;

                case Phase.Battle:
                    if (left <= finishLead && left > 0f) { StartFinisher(); break; }
                    // ヒーローの攻撃
                    nextAttack -= dt;
                    if (nextAttack <= 0f)
                    {
                        nextAttack = Random.Range(Mathf.Min(attackInterval.x, attackInterval.y), Mathf.Max(attackInterval.x, attackInterval.y));
                        HeroAttack();
                    }
                    // 怪人の攻撃
                    enemyTimer -= dt;
                    if (enemyTimer <= 0f && enemyMove == 0)
                    {
                        enemyTimer = Random.Range(Mathf.Min(enemyInterval.x, enemyInterval.y), Mathf.Max(enemyInterval.x, enemyInterval.y));
                        EnemyAttack(Random.Range(1, 4));
                    }
                    // ときどき空を飛ぶ（ジェットで飛び上がって、空中で動き回りながら攻撃する）
                    foreach (var h in heroes)
                    {
                        if (h.airTarget <= 0f && !h.dash && h.jumpY <= 0.01f && Random.value < dt * flyChance)
                        {
                            h.airTarget = D * Random.Range(Mathf.Min(flyHeight.x, flyHeight.y), Mathf.Max(flyHeight.x, flyHeight.y));
                            h.airTimer = Random.Range(2.5f, 4.5f);
                            h.target = RandomSpot(h.pos);
                            Emit(dust, h.pos, Color.white, 10);
                            Play(null, ref madeJump, () => RecordSynth.Sweep("HeroJump", 300f, 1400f, 0.35f, 0.25f, RecordSynth.Wave.Square), 0.6f);
                        }
                        else if (h.airTarget > 0f)
                        {
                            h.airTimer -= dt;
                            if (h.airTimer <= 0f) h.airTarget = 0f;   // 着地する
                            else if (h.busy <= 0f && (h.target - h.pos).magnitude < D * 1.2f) h.target = RandomSpot(h.pos);   // 空中は止まらずに、大きく回りこむ
                        }
                    }
                    // 攻撃していないヒーローは、走り回る（飛んでいる時は空中を動き回る）
                    foreach (var h in heroes)
                        if (h.busy <= 0f && !h.dash && (h.target - h.pos).magnitude < D * 0.2f && Random.value < dt * 3f)
                            h.target = RandomSpot(h.pos);
                    break;

                case Phase.Finisher:
                    if (phaseTime >= 1.3f) Blowup();
                    break;

                case Phase.Blowup:
                    if (phaseTime >= 1.1f)
                    {
                        // 5人がまん中に集まる
                        for (int i = 0; i < heroes.Count; i++)
                        {
                            // V の字にならぶ（まん中が一番前。外側ほど少しうしろ）
                            int slot = i - 2;
                            heroes[i].target = ClampSpot(new Vector3(slot * D * 1.25f * heroHeight / 1.7f, 0f, cz - D * 0.4f + Mathf.Abs(slot) * D * 0.45f));
                            // 決めポーズは、まん中 → となり → 外側 の順に決める
                            heroes[i].poseDelay = Mathf.Abs(slot) * 0.35f;
                            heroes[i].poseAt = -1f;
                            heroes[i].busy = 0f;
                            heroes[i].dash = false;
                            heroes[i].airTarget = 0f;   // 着地して集まる
                        }
                        SetPhase(Phase.Gather);
                    }
                    break;

                case Phase.Gather:
                {
                    bool arrived = true;
                    foreach (var h in heroes) if ((h.target - h.pos).magnitude > D * 0.15f) arrived = false;
                    if (arrived || phaseTime > 2f)
                    {
                        SetPhase(Phase.Pose);
                        groupPosed = false;
                    }
                    break;
                }

                case Phase.Pose:
                    // 1人ずつ：くるっと回って、ポーズを決める（うしろでその色の爆発、頭の上に名前）
                    foreach (var h in heroes)
                        if (h.poseAt < 0f && phaseTime >= h.poseDelay) SnapPose(h);
                    // 全員そろったら：5色の大爆発と光の柱
                    if (!groupPosed && phaseTime >= 1.15f) GroupPose();
                    if (phaseTime >= 3.2f || left <= 0.9f)
                    {
                        SetPhase(Phase.Leave);
                        foreach (var h in heroes) { h.jumpV = D * 14f; Emit(dust, h.pos, Color.white, 6); }
                        Play(null, ref madeJump, () => RecordSynth.Sweep("HeroJump", 300f, 1400f, 0.35f, 0.25f, RecordSynth.Wave.Square));
                    }
                    break;

                case Phase.Leave:
                    if (phaseTime >= 1.2f)
                    {
                        foreach (var h in heroes) h.root.gameObject.SetActive(false);
                        SetPhase(Phase.Done);
                    }
                    break;
            }
        }

        // ================================================================
        // ヒーローの攻撃
        // ================================================================

        void HeroAttack()
        {
            // 前と同じ人が続かないように
            int i = Random.Range(0, heroes.Count);
            if (i == lastAttacker) i = (i + 1 + Random.Range(0, heroes.Count - 1)) % heroes.Count;
            var h = heroes[i];
            if (h.busy > 0f || h.dash || h.jumpY > 0.01f) return;
            // 飛んでいる時に剣・ハンマーの番が来たら、着地してから突進する
            if (h.air > D * 0.3f && (h.weapon == Weapon.Sword || h.weapon == Weapon.Hammer)) h.airTarget = 0f;
            lastAttacker = i;
            switch (h.weapon)
            {
                case Weapon.Ray:
                    h.busy = 0.6f;
                    After(h, 0.15f, () => FireBeam(h.muzzle.position, EnemyChest, h.color, 0.35f, true));
                    break;
                case Weapon.Sword:
                case Weapon.Hammer:
                    // 怪人の前まで突進して、切る・たたく
                    h.dash = true;
                    h.dashSide = h.pos.x < enemyPos.x ? Vector3.left : Vector3.right;
                    h.target = enemyPos + h.dashSide * (sE * 0.55f + D * 0.4f) + Vector3.back * D * 0.3f;
                    break;
                case Weapon.Rocket:
                    h.busy = 0.5f + rocketSalvo * 0.12f;
                    for (int r = 0; r < rocketSalvo; r++)
                    {
                        After(h, 0.15f + r * 0.12f, () =>
                        {
                            Vector3 hit = EnemyFeet + new Vector3(Random.Range(-1f, 1f) * sE * 0.7f, 0f, -Random.Range(0.1f, 0.6f) * sE);
                            LaunchShot(1, h.muzzle.position, hit, 0.6f, D * Random.Range(1.2f, 2.4f), h.color);
                            Flash(h.muzzle.position, D * 0.9f * fxScale, Fire, 0.15f);
                            Emit(smoke, h.muzzle.position, Color.white, 10);
                            Emit(fire, h.muzzle.position, Fire, 6);
                        });
                    }
                    break;
                case Weapon.Gun:
                    h.busy = 0.3f + gunBurst * 0.05f;
                    h.burstLeft = gunBurst;
                    h.burstTimer = 0.1f;
                    break;
            }
        }

        // 少し待ってから動く（武器を構えてから撃つ）
        readonly List<(Hero h, float t, System.Action a)> pending = new List<(Hero, float, System.Action)>();
        void After(Hero h, float t, System.Action a) => pending.Add((h, t, a));

        void UpdatePending(float dt)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                p.t -= dt;
                if (p.t <= 0f)
                {
                    pending.RemoveAt(i);
                    if (p.h.root.gameObject.activeSelf) p.a();
                }
                else pending[i] = p;
            }
        }

        /// <summary>突進が着いた：剣は切りつけ、ハンマーは地面ごとたたく</summary>
        void MeleeHit(Hero h)
        {
            h.dash = false;
            h.busy = 0.6f;
            if (h.weapon == Weapon.Sword)
            {
                // 3連続の斬撃と、怪人のうしろまで飛んでいく光の斬撃
                for (int k = 0; k < 3; k++)
                {
                    int kk = k;
                    After(h, kk * 0.1f, () =>
                    {
                        SpawnSlash(EnemyChest + (h.pos - enemyPos).normalized * sE * 0.3f + Random.insideUnitSphere * sE * 0.15f, h.color);
                        HitEnemy(h.color, 1f);
                        Emit(sparks, EnemyChest, Color.white, 15);
                        Flash(EnemyChest, sE * 0.6f * fxScale, h.color, 0.15f);
                        Play(slashSound, ref madeSlash, () => RecordSynth.Sweep("HeroSlash", 2400f, 400f, 0.18f, 0.3f, RecordSynth.Wave.Saw), 0.7f);
                    });
                }
                After(h, 0.3f, () =>
                {
                    Vector3 dir = enemyPos - h.pos; dir.y = 0f;
                    dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
                    Vector3 from = h.pos + Vector3.up * sH * 0.9f;
                    SpawnWave(from, from + dir * (sE + D * 4f), h.color);
                    ImpactGround(h.pos + dir * D * 0.8f, D * 1.4f * weaponScale, hitPower, h.color);
                    Shake(0.04f, 0.2f);
                });
            }
            else
            {
                // ハンマー：大きくジャンプして、光の柱が立つほどたたきつける
                h.jumpV = D * 7f;
                After(h, 0.45f, () =>
                {
                    Vector3 at = h.pos + (enemyPos - h.pos).normalized * D * 0.5f;
                    at.y = 0f;
                    for (int r = 0; r < 3; r++) SpawnRing(at + Vector3.up * D * (0.03f + r * 0.01f), D * (2.5f + r * 1.5f) * fxScale, r == 1 ? Color.white : h.color, 0.4f + r * 0.15f);
                    FireBeam(at, at + Vector3.up * D * 6f, h.color, 0.4f, false);
                    Flash(at + Vector3.up * D * 0.3f, D * 2.5f * fxScale, h.color, 0.3f);
                    Emit(dust, at, new Color(0.85f, 0.8f, 0.7f, 0.8f), 30);
                    Emit(sparks, at, h.color, 40);
                    HitEnemy(h.color, 1.5f);
                    ImpactGround(at, D * 1.8f * weaponScale, explodePower * 0.9f, h.color);
                    Shake(0.08f, 0.4f);
                    Play(null, ref madeStomp, () => RecordSynth.Thud("HeroStomp", 70f, 0.5f, 1f), 0.9f);
                });
            }
            // 突進のあとは、はなれる
            After(h, 0.65f, () => { h.target = RandomSpot(h.pos); });
        }

        void FireBeam(Vector3 a, Vector3 b, Color c, float life, bool hitsEnemy)
        {
            Beam bm = null;
            foreach (var x in beams) if (x.age < 0f) { bm = x; break; }
            if (bm == null) bm = beams[0];
            bm.a = a; bm.b = b; bm.age = 0f; bm.life = life;
            RecordMeshKit.SetColor(bm.glow.sharedMaterial, c);
            bm.core.enabled = bm.glow.enabled = true;
            bm.core.SetPosition(0, a); bm.core.SetPosition(1, b);
            bm.glow.SetPosition(0, a); bm.glow.SetPosition(1, b);
            // 発射口の光・光線にそって進む光の輪・当たった所の大きな光
            Flash(a, D * 1.1f * fxScale, c, 0.25f);
            Flash(a, D * 0.6f * fxScale, Color.white, 0.15f);
            for (int r = 0; r < 4; r++) SpawnTravelRing(a, b, c, life * 0.8f, r * 0.06f);
            Emit(sparks, a, c, 10);
            if (hitsEnemy)
            {
                Flash(b, sE * 0.9f * fxScale, c, 0.35f);
                Flash(b, sE * 0.45f * fxScale, Color.white, 0.2f);
                Emit(sparks, b, c, 30);
                Emit(sparks, b, Color.white, 15);
                HitEnemy(c, 1.2f);
                Vector3 g = EnemyFeet + new Vector3(Random.Range(-1f, 1f) * sE * 0.6f, 0f, -sE * 0.4f);
                ImpactGround(g, D * 1.6f, hitPower, c);
                SpawnRing(g + Vector3.up * D * 0.03f, D * 2.2f * fxScale, c, 0.4f);
                flash = Mathf.Max(flash, 0.6f);
                flashLight.color = c;
                flashLight.transform.position = b;
            }
            Play(beamSound, ref madeBeam, () => RecordSynth.Sweep("HeroBeam", 1800f, 300f, 0.35f, 0.3f, RecordSynth.Wave.Square), 0.6f);
        }

        void LaunchShot(int kind, Vector3 from, Vector3 to, float life, float arc, Color c)
        {
            Transform t;
            if (kind == 1)
            {
                var go = new GameObject("Rocket");
                go.transform.SetParent(Board, false);
                Shape(PrimitiveType.Capsule, "Body", go.transform, Vector3.zero, new Vector3(D * 0.14f, D * 0.22f, D * 0.14f), rocketMat).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Shape(PrimitiveType.Sphere, "Nose", go.transform, new Vector3(0f, 0f, D * 0.2f), Vector3.one * D * 0.12f, Mat(c, 0.2f, 0.8f, 1.5f));
                t = go.transform;
            }
            else
            {
                t = Shape(PrimitiveType.Quad, kind == 2 ? "Orb" : "Tracer", Board, Vector3.zero, Vector3.one * (kind == 2 ? D * 0.7f : D * 0.32f * fxScale), kind == 2 ? orbMat : tracerMat).transform;
            }
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) frontParts.Add(r);
            t.position = from;
            shots.Add(new Shot { t = t, from = from, to = to, life = life, arc = arc, kind = kind, color = c });
        }

        void UpdateShots(float dt, Quaternion face)
        {
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                s.age += dt;
                if (s.kind == 3)
                {
                    // 薬きょう：放物線で飛んで、くるくる回って消える
                    s.v += Physics.gravity * dt;
                    s.t.position += s.v * dt;
                    s.t.Rotate(720f * dt, 0f, 540f * dt, Space.Self);
                    if (s.age >= s.life) { Destroy(s.t.gameObject); shots.RemoveAt(i); }
                    continue;
                }
                float k = Mathf.Clamp01(s.age / s.life);
                Vector3 p = Vector3.Lerp(s.from, s.to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * s.arc;
                Vector3 prev = s.t.position;
                s.t.position = p;
                if (s.kind == 1)
                {
                    if ((p - prev).sqrMagnitude > 1e-6f) s.t.rotation = Quaternion.LookRotation(p - prev);
                    Emit(smoke, p, new Color(0.9f, 0.9f, 0.9f, 0.5f), 2);
                    Emit(fire, p, Fire, 2);
                }
                else
                {
                    s.t.rotation = face;
                    if (s.kind == 2) s.t.localScale = Vector3.one * D * 0.7f * (1f + 0.2f * Mathf.Sin(s.age * 30f));
                }
                if (k >= 1f)
                {
                    switch (s.kind)
                    {
                        case 0:   // 弾：当たった所に火花。床に当たった弾は、そこのメダルをはじく
                            Emit(sparks, p, s.color, 6);
                            Flash(p, D * 0.5f * fxScale, Color.Lerp(s.color, Color.white, 0.5f), 0.08f);
                            if (p.y < D * 0.5f) ImpactGround(p, D * 0.9f, hitPower * 0.8f, s.color);
                            else enemyFlash = Mathf.Max(enemyFlash, 0.5f);
                            break;
                        case 1:   // ロケット：大爆発
                            Explode(p, D * 1.6f * weaponScale, explodePower, s.color);
                            HitEnemy(s.color, 1.5f);
                            break;
                        case 2:   // 紫の玉：ヒーローの近くで爆発
                            Explode(p, D * 1.4f, explodePower * 0.8f, Orb);
                            KnockHeroes(p, D * 1.4f);
                            break;
                    }
                    Destroy(s.t.gameObject);
                    shots.RemoveAt(i);
                }
            }
        }

        // ================================================================
        // 怪人の攻撃
        // ================================================================

        void EnemyAttack(int move)
        {
            enemyMove = move;
            enemyAnim = 0f;
            switch (move)
            {
                case 1:   // 目からの光線で、床をなぎはらう
                    laserT = 0f;
                    Play(null, ref madeLaser, () => RecordSynth.Sweep("EnemyLaser", 200f, 900f, 1.1f, 0.3f, RecordSynth.Wave.Saw));
                    break;
                case 2:   // ふみつけ
                    Play(null, ref madeStomp, () => RecordSynth.Thud("HeroStomp", 70f, 0.5f, 1f));
                    break;
                case 3:   // 紫の玉を3つ、ヒーローに投げる
                    for (int i = 0; i < 3; i++)
                    {
                        var h = heroes[Random.Range(0, heroes.Count)];
                        LaunchShot(2, EnemyChest + Vector3.up * sE * 0.3f, h.pos + Vector3.up * D * 0.1f, 0.8f + i * 0.15f, sE * 0.8f, Orb);
                    }
                    break;
            }
        }

        void UpdateEnemyMove(float dt)
        {
            if (enemyMove == 0) return;
            enemyAnim += dt;
            switch (enemyMove)
            {
                case 1:
                {
                    // 左から右へ、床をなぎはらう（光線が当たった所のメダルが吹き飛ぶ）
                    float k = enemyAnim / 1.1f;
                    if (k >= 1f) { laserCore.enabled = laserGlow.enabled = false; enemyMove = 0; break; }
                    float x = Mathf.Lerp(-HalfW * 0.85f, HalfW * 0.85f, k);
                    Vector3 hit = new Vector3(x, 0f, Mathf.Lerp(FrontZ + D * 0.6f, enemyPos.z - D * 0.5f, 0.3f + 0.2f * Mathf.Sin(k * 6f)));
                    Vector3 eye = enemyHead.position + enemyHead.rotation * new Vector3(0f, sE * 0.22f, sE * 0.22f);
                    laserCore.enabled = laserGlow.enabled = true;
                    laserCore.SetPosition(0, eye); laserCore.SetPosition(1, hit);
                    laserGlow.SetPosition(0, eye); laserGlow.SetPosition(1, hit);
                    laserGlow.widthMultiplier = sE * 0.3f * Random.Range(0.8f, 1.2f);
                    laserT -= dt;
                    if (laserT <= 0f)
                    {
                        laserT = 0.07f;
                        ImpactGround(hit, D * 0.9f, hitPower, EnemyGlow);
                        Emit(fire, hit, Fire, 3);
                        KnockHeroes(hit, D * 0.8f);
                    }
                    break;
                }
                case 2:
                {
                    // ぴょんと跳んで、ドスン
                    float k = enemyAnim / 0.8f;
                    enemyJump = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * sE * 0.35f;
                    if (k >= 1f)
                    {
                        enemyJump = 0f;
                        enemyMove = 0;
                        SpawnRing(enemyPos + Vector3.up * D * 0.03f, D * 4f, EnemyGlow, 0.6f);
                        ImpactGround(enemyPos, sE * 0.9f + D * 1.5f, explodePower, EnemyGlow);
                        KnockHeroes(enemyPos, sE + D * 1.5f);
                        Emit(dust, enemyPos, new Color(0.7f, 0.6f, 0.6f, 0.8f), 30);
                        Shake(0.09f, 0.5f);
                    }
                    break;
                }
                case 3:
                    if (enemyAnim > 1.2f) enemyMove = 0;
                    break;
            }
        }

        void HitEnemy(Color c, float power)
        {
            enemyFlash = Mathf.Max(enemyFlash, 0.6f * power);
            Emit(sparks, EnemyChest + Random.insideUnitSphere * sE * 0.3f, c, Mathf.RoundToInt(14 * power));
        }

        /// <summary>まわりのヒーローが、攻撃を受けてふっとぶ（小さくはねる）</summary>
        void KnockHeroes(Vector3 at, float r)
        {
            foreach (var h in heroes)
            {
                Vector3 d = h.pos - at;
                d.y = 0f;
                if (d.magnitude > r || h.jumpY > 0.05f) continue;
                h.jumpV = D * 4f;
                h.hitT = 0.5f;
                Vector3 away = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.left;
                h.target = ClampSpot(h.pos + away * D * 1.2f);
                Emit(sparks, h.pos + Vector3.up * sH, h.color, 8);
            }
        }

        Vector3 ClampSpot(Vector3 p) => new Vector3(Mathf.Clamp(p.x, -HalfW * 0.85f, HalfW * 0.85f), 0f, Mathf.Clamp(p.z, FrontZ + D * 0.4f, PusherFrontZ - D * 0.2f));

        // ================================================================
        // 必殺技・決着
        // ================================================================

        bool groupPosed;

        void SnapPose(Hero h)
        {
            h.poseAt = phaseTime;
            Vector3 back = h.pos + new Vector3(0f, sH * 1.1f, D * 1.1f);
            Emit(fire, back, h.color, 35);
            Emit(smoke, back, Color.Lerp(h.color, Color.white, 0.4f), 12);
            Emit(sparks, h.pos + Vector3.up * sH, h.color, 25);
            Flash(back, D * 2.4f * fxScale, h.color, 0.35f);
            Flash(h.pos + Vector3.up * sH, D * 1.2f * fxScale, Color.white, 0.15f);
            SpawnRing(h.pos + Vector3.up * D * 0.04f, D * 2f * fxScale, h.color, 0.5f);
            h.nameTag.gameObject.SetActive(true);
            h.nameTag.text = HeroNames[(int)h.weapon];
            flash = Mathf.Max(flash, 0.7f);
            flashLight.color = h.color;
            flashLight.transform.position = back;
            Shake(0.03f, 0.15f);
            Play(null, ref madeSnap, () => RecordSynth.Thud("HeroSnap", 90f, 0.35f, 0.9f), 0.8f);
        }

        void GroupPose()
        {
            groupPosed = true;
            foreach (var h in heroes)
            {
                Vector3 back = h.pos + new Vector3(0f, sH * 1.4f, D * 1.6f);
                Emit(fire, back, h.color, 50);
                Emit(fire, back, Fire, 20);
                Emit(smoke, back, Color.Lerp(h.color, Color.white, 0.3f), 20);
                Flash(back, D * 3.6f * fxScale, h.color, 0.6f);
                // うしろに、その色の光の柱
                FireBeam(h.pos + new Vector3(0f, 0f, D * 1.6f), h.pos + new Vector3(0f, D * 8f, D * 1.6f), h.color, 1.4f, false);
            }
            Vector3 mid = new Vector3(0f, sH, cz);
            Flash(mid + Vector3.forward * D * 1.5f, D * 6f * fxScale, Color.white, 0.4f);
            SpawnRing(new Vector3(0f, D * 0.05f, cz), D * 6f * fxScale, Color.white, 0.8f);
            SpawnRing(new Vector3(0f, D * 0.06f, cz), D * 9f * fxScale, Fire, 1.1f);
            Emit(stars, mid + Vector3.up * sH, Color.white, 60);
            flash = 1.5f;
            flashLight.color = Color.white;
            Shake(0.12f, 0.8f);
            Speaker(1f, 16, HeroColors[Random.Range(0, 5)]);
            Play(boomSound, ref madeBoom, () => RecordSynth.Thud("HeroBoom", 55f, 0.9f, 1f));
            Play(finishSound, ref madeFinish, () => RecordSynth.Sweep("HeroFinish", 200f, 2000f, 1.2f, 0.35f, RecordSynth.Wave.Saw), 0.6f);
            PopCenter("MEDAL KNIGHTS!!", Color.white);
        }

        void StartFinisher()
        {
            SetPhase(Phase.Finisher);
            enemyMove = 0;
            laserCore.enabled = laserGlow.enabled = false;
            foreach (var h in heroes) { h.dash = false; h.busy = 1.3f; h.burstLeft = 0; h.target = h.pos; h.airTimer = 2f; if (h.airTarget <= 0f) h.airTarget = D * flyHeight.x; }   // 必殺技は空中から
            pending.Clear();
            PopCenter("FINAL ATTACK!!", Color.white);
            // 5人がいっせいに、それぞれの色の光線を怪人へ
            for (int i = 0; i < heroes.Count; i++)
            {
                var h = heroes[i];
                After(h, 0.25f + i * 0.08f, () => FireBeam(h.muzzle.position, EnemyChest, h.color, 1.0f, false));
            }
            Play(finishSound, ref madeFinish, () => RecordSynth.Sweep("HeroFinish", 200f, 2000f, 1.2f, 0.35f, RecordSynth.Wave.Saw));
        }

        void Blowup()
        {
            SetPhase(Phase.Blowup);
            Vector3 at = EnemyChest;
            Explode(enemyPos, sE + D * 2.5f, finishPower, Fire);
            for (int i = 0; i < 5; i++) Emit(fire, at + Random.insideUnitSphere * sE * 0.6f, HeroColors[i], 25);
            Emit(sparks, at, Color.white, 80);
            Emit(smoke, at, new Color(0.3f, 0.25f, 0.35f, 0.8f), 40);
            SpawnRing(enemyPos + Vector3.up * D * 0.05f, D * 7f, Color.white, 0.9f);
            enemy.gameObject.SetActive(false);
            flash = 1.5f;
            Shake(0.16f, 1f);
            Speaker(1f, 16, Fire);
            Play(boomSound, ref madeBoom, () => RecordSynth.Thud("HeroBoom", 55f, 0.9f, 1f));
            PopCenter("VICTORY!!", Fire);
        }

        // ================================================================
        // 爆発・衝撃（【効果】ここでメダルが吹き飛ぶ）
        // ================================================================

        void ImpactGround(Vector3 at, float radius, float power, Color c)
        {
            Blast(new Vector3(at.x, 0f, at.z), radius, D * power, 0.8f);
            Emit(sparks, new Vector3(at.x, D * 0.1f, at.z), c, 5);
        }

        void Explode(Vector3 at, float radius, float power, Color c)
        {
            Vector3 g = new Vector3(at.x, 0f, at.z);
            Blast(g, radius, D * power, 1f);
            Emit(fire, at, Fire, 30);
            Emit(fire, at, c, 15);
            Emit(sparks, at, Color.white, 25);
            Emit(smoke, at, new Color(0.35f, 0.33f, 0.35f, 0.8f), 14);
            SpawnRing(g + Vector3.up * D * 0.03f, radius * 1.5f * fxScale, c, 0.5f);
            SpawnRing(g + Vector3.up * D * 0.04f, radius * 2.2f * fxScale, Color.white, 0.7f);
            Flash(at + Vector3.up * D * 0.3f, radius * 1.6f * fxScale, Fire, 0.45f);
            Flash(at + Vector3.up * D * 0.3f, radius * 1.1f * fxScale, c, 0.35f);
            Flash(at + Vector3.up * D * 0.3f, radius * 0.6f * fxScale, Color.white, 0.2f);
            flash = Mathf.Max(flash, 0.8f);
            flashLight.color = Color.Lerp(Fire, c, 0.4f);
            flashLight.transform.position = at + Vector3.up * D;
            Shake(0.05f, 0.3f);
            Play(boomSound, ref madeBoom, () => RecordSynth.Thud("HeroBoom", 55f, 0.9f, 1f), 0.8f);
        }

        void SpawnRing(Vector3 at, float size, Color c, float life)
        {
            var m = RecordMeshKit.Additive(RecordMeshKit.Ripple, c);
            var t = RecordMeshKit.Make("Shock", RecordMeshKit.Annulus(0.6f, 1f, 64, 3), m, Board, at, Vector3.one * size * 0.1f, Vector3.zero);
            fxs.Add(new Fx { t = t, m = m, life = life, size = size, kind = 0, color = c });
        }

        void SpawnSlash(Vector3 at, Color c)
        {
            var m = RecordMeshKit.Additive(Texture2D.whiteTexture, Color.Lerp(c, Color.white, 0.4f));
            var t = RecordMeshKit.Make("Slash", RecordMeshKit.Torus(1f, 0.07f, 20f, 150f, 24, 6), m, Board, at, Vector3.one * sE * 0.6f * fxScale, Vector3.zero);
            var cam = Camera.main;
            if (cam != null) t.rotation = Quaternion.LookRotation(at - cam.transform.position) * Quaternion.Euler(0f, 0f, Random.Range(-60f, 60f));
            frontParts.Add(t.GetComponent<Renderer>());
            fxs.Add(new Fx { t = t, m = m, life = 0.25f, size = sE * 0.6f * fxScale, kind = 1, color = c });
        }

        /// <summary>光の玉（爆発の火の玉・発射の光）。いつもカメラの方を向いて、ふくらんで消える</summary>
        void Flash(Vector3 at, float size, Color c, float life)
        {
            var m = RecordMeshKit.Additive(RecordMeshKit.SoftDot, c);
            var t = Shape(PrimitiveType.Quad, "Flash", Board, at, Vector3.one * size * 0.3f, m).transform;
            frontParts.Add(t.GetComponent<Renderer>());
            fxs.Add(new Fx { t = t, m = m, life = life, size = size, kind = 2, color = c, a = at });
        }

        /// <summary>光線にそって、発射口から当たる所へ進む光の輪</summary>
        void SpawnTravelRing(Vector3 a, Vector3 b, Color c, float life, float delay)
        {
            var m = RecordMeshKit.Additive(Texture2D.whiteTexture, Color.Lerp(c, Color.white, 0.3f));
            var t = RecordMeshKit.Make("BeamRing", RecordMeshKit.Torus(1f, 0.12f, 0f, 360f, 32, 6), m, Board, a, Vector3.one * D * 0.4f * fxScale, Vector3.zero);
            if ((b - a).sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(b - a);
            frontParts.Add(t.GetComponent<Renderer>());
            fxs.Add(new Fx { t = t, m = m, life = life, size = D * 0.4f * fxScale, kind = 4, color = c, a = a, b = b, age = -delay });
        }

        /// <summary>飛んでいく光の斬撃（三日月の形）。通った所のメダルもはじく</summary>
        void SpawnWave(Vector3 a, Vector3 b, Color c)
        {
            var m = RecordMeshKit.Additive(Texture2D.whiteTexture, Color.Lerp(c, Color.white, 0.3f));
            var t = RecordMeshKit.Make("SlashWave", RecordMeshKit.Torus(1f, 0.08f, 30f, 120f, 24, 6), m, Board, a, Vector3.one * sE * 0.5f * fxScale, Vector3.zero);
            if ((b - a).sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(b - a);
            frontParts.Add(t.GetComponent<Renderer>());
            fxs.Add(new Fx { t = t, m = m, life = 0.45f, size = sE * 0.5f * fxScale, kind = 3, color = c, a = a, b = b });
        }

        void LaunchCasing(Vector3 at, Vector3 v)
        {
            var t = Shape(PrimitiveType.Cylinder, "Casing", Board, at, new Vector3(D * 0.05f, D * 0.06f, D * 0.05f), casingMat).transform;
            shots.Add(new Shot { t = t, from = at, to = at, v = v, life = 0.6f, kind = 3, color = Color.white });
        }

        void UpdateFx(float dt)
        {
            for (int i = fxs.Count - 1; i >= 0; i--)
            {
                var f = fxs[i];
                f.age += dt;
                if (f.age < 0f) { f.t.localScale = Vector3.zero; continue; }   // まだ出ない（少し遅れて出る）
                float k = Mathf.Clamp01(f.age / f.life);
                switch (f.kind)
                {
                    case 0: f.t.localScale = Vector3.one * Mathf.Lerp(f.size * 0.1f, f.size, 1f - (1f - k) * (1f - k)); break;
                    case 2:
                        f.t.localScale = Vector3.one * Mathf.Lerp(f.size * 0.3f, f.size, 1f - (1f - k) * (1f - k));
                        if (fxFace.HasValue) f.t.rotation = fxFace.Value;
                        break;
                    case 3:
                    {
                        Vector3 p = Vector3.Lerp(f.a, f.b, k);
                        f.t.position = p;
                        f.t.localScale = Vector3.one * f.size * (1f + 0.6f * k);
                        if (Random.value < 0.6f) ImpactGround(new Vector3(p.x, 0f, p.z), D * 0.8f, hitPower * 0.7f, f.color);
                        if (Random.value < 0.5f) Emit(sparks, p, f.color, 2);
                        break;
                    }
                    case 4:
                        f.t.position = Vector3.Lerp(f.a, f.b, k);
                        f.t.localScale = Vector3.one * f.size * (1f + 1.5f * k);
                        break;
                    default: f.t.localScale = Vector3.one * f.size * (1f + 0.3f * k); break;
                }
                RecordMeshKit.SetColor(f.m, new Color(f.color.r, f.color.g, f.color.b, 1f - k));
                if (k >= 1f)
                {
                    Destroy(f.t.gameObject);
                    Destroy(f.m);
                    fxs.RemoveAt(i);
                }
            }
        }

        // ================================================================
        // 走ったヒーローの足もとのメダルは、けとばされる（【効果】）
        // ================================================================

        void FixedUpdate()
        {
            if (!Running || phase == Phase.None || phase == Phase.Done) return;
            foreach (var h in heroes)
            {
                if (!h.root.gameObject.activeSelf || h.jumpY + h.air > D * 0.3f) continue;
                Vector3 v = h.vel;
                v.y = 0f;
                float sp = v.magnitude;
                if (sp < D * 0.5f) continue;
                Vector3 dir = v / sp;
                foreach (var m in MedalsNear(h.pos + dir * D * 0.25f, D * 0.5f * heroHeight / 1.7f, near))
                {
                    if (m.Body == null || m.Consumed || m.Body.isKinematic || m.transform.position.y > Layout.pusherHeight * 0.6f) continue;
                    Vector3 side = m.transform.position - h.pos;
                    side.y = 0f;
                    Vector3 kick = dir * sp * runKick + (side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.zero) * D * 1.5f + Vector3.up * D * 1.8f;
                    if (Vector3.Dot(m.Body.velocity, dir) < sp) m.Body.velocity = kick;
                    m.Body.WakeUp();
                }
            }
        }

        // ================================================================
        // モニターより手前に描く（ジャンプした時や、怪人の頭など）
        // ================================================================

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
            for (int i = frontParts.Count - 1; i >= 0; i--)
            {
                var r = frontParts[i];
                if (r == null) { frontParts.RemoveAt(i); continue; }
                if (!r.gameObject.activeInHierarchy) continue;
                int want = use && r.bounds.center.y > limit ? front : 0;
                if (r.gameObject.layer != want) r.gameObject.layer = want;
            }
        }

        // ================================================================
        // 見た目（毎フレーム）
        // ================================================================

        protected override bool KeepBoardVisible() =>
            (sparks != null && sparks.particleCount > 0) || (smoke != null && smoke.particleCount > 0) || (fire != null && fire.particleCount > 0) || fxs.Count > 0;

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            var cam = Camera.main;
            Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
            bool on = Active && phase != Phase.None;
            if (on && IsScoring) UpdatePending(dt);

            // 怪人：下段の奥のあたりを、のっしのっしと歩き回る。ヒーローたちの方を向く
            if (enemy.gameObject.activeSelf && on && IsScoring)
            {
                bool still = enemyMove == 1 || phase == Phase.Finisher || phase == Phase.Enter;
                enemyWander -= dt;
                if (enemyWander <= 0f && !still)
                {
                    enemyWander = Random.Range(1.5f, 3f);
                    enemyTarget = new Vector3(Random.Range(-HalfW, HalfW) * 0.45f, 0f, Mathf.Lerp(FrontZ, PusherFrontZ, Random.Range(0.5f, 0.72f)));
                }
                Vector3 goal = still ? enemyPos : enemyTarget;
                Vector3 before = enemyPos;
                enemyPos = Vector3.SmoothDamp(enemyPos, goal, ref enemyVel, 0.7f, D * enemySpeed);
                float esp = dt > 0f ? (enemyPos - before).magnitude / dt : 0f;
                enemyWalk += dt * 8f * Mathf.Clamp01(esp / D);
                // ヒーローたちのまん中の方を向く
                Vector3 mid = Vector3.zero;
                int n = 0;
                foreach (var hh in heroes) if (hh.root.gameObject.activeSelf) { mid += hh.pos; n++; }
                Vector3 face2 = n > 0 ? mid / n - enemyPos : Vector3.back;
                face2.y = 0f;
                if (face2.sqrMagnitude > 1e-4f) enemy.rotation = Quaternion.Slerp(enemy.rotation, Quaternion.LookRotation(face2), 1f - Mathf.Exp(-dt * 2.5f));
            }
            if (enemy.gameObject.activeSelf)
            {
                float estep = Mathf.Sin(enemyWalk);
                float emoving = Mathf.Clamp01(enemyVel.magnitude / D);
                enemyLegL.localRotation = Quaternion.Euler(estep * 25f * emoving, 0f, 0f);
                enemyLegR.localRotation = Quaternion.Euler(-estep * 25f * emoving, 0f, 0f);
                enemyShow = Mathf.MoveTowards(enemyShow, 1f, dt * 1.2f);
                float rise = Mathf.SmoothStep(0f, 1f, enemyShow);
                enemy.position = enemyPos + Vector3.up * (enemyJump - sE * 2.2f * (1f - rise) + Mathf.Abs(estep) * sE * 0.04f * emoving);
                enemyFlash = Mathf.MoveTowards(enemyFlash, 0f, dt * 3f);
                SetGlow(enemySkin, Color.Lerp(new Color(0.22f, 0.12f, 0.28f), Color.white, enemyFlash * 0.6f), 0.1f + enemyFlash * 2f);
                SetGlow(enemyEye, EnemyGlow, enemyMove == 1 ? 6f : 2.5f + Mathf.Sin(t * 6f));
                float breath = Mathf.Sin(t * 2f);
                enemyBody.localRotation = Quaternion.Euler(breath * 2f + (phase == Phase.Finisher ? Mathf.Sin(t * 40f) * 4f : 0f), 0f, Mathf.Sin(t * 1.3f) * 3f + estep * 5f * emoving);
                float swipe = enemyMove == 3 ? Mathf.Sin(Mathf.Clamp01(enemyAnim / 0.5f) * Mathf.PI) * -110f : 0f;
                enemyArmL.localRotation = Quaternion.Euler(-20f + breath * 8f, 0f, -15f);
                enemyArmR.localRotation = Quaternion.Euler(-20f - breath * 8f + swipe, 0f, 15f);
                enemyHead.localRotation = Quaternion.Euler(enemyMove == 1 ? 25f : breath * 4f, 0f, 0f);
                if (Random.value < dt * 6f) Emit(smoke, enemyPos + Vector3.up * sE * Random.Range(0.8f, 1.6f), new Color(0.3f, 0.15f, 0.35f, 0.4f), 1);
            }
            if (on && IsScoring) UpdateEnemyMove(dt);

            // ヒーロー
            foreach (var h in heroes)
            {
                if (!h.root.gameObject.activeSelf) continue;
                // 空から落ちてくる・はねる・ジャンプして帰る
                if (h.jumpY > 0f || h.jumpV != 0f)
                {
                    h.jumpV -= 9.81f * 2.2f * D * dt;
                    h.jumpY += h.jumpV * dt;
                    if (h.jumpY <= 0f && phase != Phase.Leave)
                    {
                        if (h.jumpV < -D * 3f)
                        {
                            Emit(dust, h.pos, Color.white, 8);
                            ImpactGround(h.pos, D * 0.8f, hitPower * 0.5f, h.color);
                        }
                        h.jumpY = 0f;
                        h.jumpV = 0f;
                    }
                }
                if (phase == Phase.Leave)
                {
                    if (Random.value < 0.5f) Emit(stars, h.root.position + Vector3.up * sH, h.color, 1);
                    if (phaseTime > 0.6f) h.root.localScale = Vector3.one * Mathf.Max(0.01f, 1f - (phaseTime - 0.6f) / 0.6f);
                }
                else h.root.localScale = Vector3.one;

                // 走る
                float speed = D * runSpeed * (h.dash ? 2.4f : 1f) * (phase == Phase.Gather ? 1.6f : 1f);
                // 突進中は、動いている怪人を追いかける
                if (h.dash) h.target = enemyPos + h.dashSide * (sE * 0.55f + D * 0.4f) + Vector3.back * D * 0.3f;
                Vector3 to = h.target - h.pos;
                to.y = 0f;
                float dist = to.magnitude;
                Vector3 prev = h.pos;
                bool canMove = on && IsScoring && h.busy <= 0f && phase != Phase.Pose && phase != Phase.Leave && phase != Phase.Finisher;
                // なめらかに加速・減速する（着く前にゆっくりになる。急に曲がらない）
                Vector3 want = canMove && dist > D * 0.02f ? to / dist * speed * Mathf.Clamp01(dist / (D * (h.dash ? 0.4f : 1f))) : Vector3.zero;
                float accel = h.air > D * 0.3f ? 6f : (h.dash ? 14f : 10f);   // 空中は少しふんわり
                h.velS = Vector3.Lerp(h.velS, want, 1f - Mathf.Exp(-accel * dt));
                h.pos += h.velS * dt;
                h.pos = ClampSpot(h.pos);
                if (h.dash && dist <= D * 0.2f) { MeleeHit(h); h.velS *= 0.3f; }
                h.vel = dt > 0f ? (h.pos - prev) / dt : Vector3.zero;
                float sp01 = Mathf.Clamp01(h.vel.magnitude / Mathf.Max(0.01f, D * runSpeed));
                float flying = Mathf.Clamp01(h.air / D);   // 飛んでいる度合い（0 地面 〜 1 空）
                bool moving = sp01 > 0.08f;
                h.walk += dt * 14f * sp01;
                // 空を飛ぶ高さ（なめらかに上がって、下がる）。足もとからジェットの炎
                float airWas = h.air;
                h.air = Mathf.SmoothDamp(h.air, h.airTarget, ref h.airVel, h.airTarget > h.air ? 0.3f : 0.25f);
                if (h.airTarget <= 0f && h.air < D * 0.02f) { h.air = 0f; h.airVel = 0f; }
                if (h.air > D * 0.05f && Random.value < 0.8f)
                {
                    Emit(fire, h.root.position + Vector3.down * D * 0.05f, Color.Lerp(h.color, Fire, 0.5f), 1);
                    if (Random.value < 0.3f) Emit(smoke, h.root.position, new Color(0.8f, 0.8f, 0.85f, 0.4f), 1);
                }
                if (airWas > D * 0.2f && h.air <= 0.001f)
                {
                    // 着地の衝撃
                    Emit(dust, h.pos, Color.white, 12);
                    ImpactGround(h.pos, D * 1.3f, hitPower * 0.7f, h.color);
                }
                float hover = h.air > D * 0.1f ? Mathf.Sin(t * 2.4f + h.phase) * D * 0.12f * Mathf.Clamp01(h.air / D) : 0f;
                float stepBob = h.air <= D * 0.05f ? Mathf.Abs(Mathf.Sin(h.walk)) * D * 0.04f * sp01 : 0f;
                h.root.position = h.pos + Vector3.up * (h.jumpY + h.air + hover + stepBob);

                // 向き：走る向き、攻撃する時と戦う時は怪人の方、決めポーズはカメラの方
                Vector3 look;
                if (phase == Phase.Pose || phase == Phase.Leave || (phase == Phase.Gather && !moving) || !enemy.gameObject.activeSelf && !moving)
                    look = cam != null ? cam.transform.position - h.pos : Vector3.back;
                else if (moving && !(h.busy > 0f)) look = h.velS;
                else look = enemyPos - h.pos;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-4f)
                {
                    Quaternion wantRot = Quaternion.LookRotation(look);
                    if (phase == Phase.Pose && h.poseAt >= 0f)
                    {
                        // ポーズを決める時は、くるっと1回転してからカメラの方でピタッと止まる
                        float sk = Mathf.Clamp01((phaseTime - h.poseAt) / 0.25f);
                        float spinDeg = 360f * (1f - (1f - sk) * (1f - sk) * (1f - sk));
                        int si = heroes.IndexOf(h) - 2;
                        h.root.rotation = wantRot * Quaternion.Euler(0f, spinDeg - si * 10f, 0f);   // 外側の人は少し内向き
                    }
                    else h.root.rotation = Quaternion.Slerp(h.root.rotation, wantRot, 1f - Mathf.Exp(-dt * 7f));
                }

                // ポーズ
                float swing = Mathf.Sin(h.walk) * 35f * sp01 * (1f - flying);
                float trail = flying * 25f;   // 飛んでいる時は、脚をうしろへ流す
                h.legL.localRotation = Quaternion.Slerp(h.legL.localRotation, Quaternion.Euler(swing + trail, 0f, 0f), 1f - Mathf.Exp(-dt * 15f));
                h.legR.localRotation = Quaternion.Slerp(h.legR.localRotation, Quaternion.Euler(-swing + trail + flying * 10f, 0f, 0f), 1f - Mathf.Exp(-dt * 15f));
                // 走る・飛ぶ向きに体をかたむける（曲がる時は横にもかたむく）
                Vector3 localVel = h.root.InverseTransformDirection(h.velS);
                float pitch = Mathf.Clamp(localVel.z / Mathf.Max(0.01f, D * runSpeed), -1f, 1f) * (10f + 20f * flying);
                float roll = -Mathf.Clamp(localVel.x / Mathf.Max(0.01f, D * runSpeed), -1f, 1f) * (6f + 18f * flying);
                h.torso.localRotation = Quaternion.Slerp(h.torso.localRotation, Quaternion.Euler(pitch, 0f, roll + (h.hitT > 0f ? Mathf.Sin(t * 40f) * 8f : 0f)), 1f - Mathf.Exp(-dt * 10f));
                h.hitT = Mathf.MoveTowards(h.hitT, 0f, dt);
                h.busy = Mathf.MoveTowards(h.busy, 0f, dt);
                Quaternion armR, armL;
                if (phase == Phase.Pose || phase == Phase.Leave)
                {
                    // 決めポーズ（5人それぞれ。左右の人は鏡うつし）。決める前は、少しかがんで力をためる
                    int i = heroes.IndexOf(h);
                    bool posed = phase == Phase.Leave || h.poseAt >= 0f;
                    //            0：外側左      1：内側左       2：まん中         3：内側右       4：外側右
                    float[] rx = { -180f,        -95f,           -175f,            -95f,           -40f };
                    float[] rz = { -15f,          35f,             0f,              -70f,            60f };
                    float[] lx = { -40f,         -95f,           -20f,             -95f,           -180f };
                    float[] lz = { -60f,          70f,           -35f,             -35f,             15f };
                    float[] legSpread = { 22f, 16f, 26f, 16f, 22f };
                    float[] twist = { 18f, 10f, 0f, -10f, -18f };
                    if (posed)
                    {
                        armR = Quaternion.Euler(rx[i], 0f, rz[i]);
                        armL = Quaternion.Euler(lx[i], 0f, lz[i]);
                        float hold = 1f + 0.03f * Mathf.Sin(t * 6f + i);   // ピタッと止まったまま、少しだけ力が入る
                        h.torso.localRotation = Quaternion.Euler(-6f * hold, twist[i], (i - 2) * -5f);
                        h.head.localRotation = Quaternion.Euler(-10f, -twist[i] * 0.5f, (i - 2) * 4f);
                        h.legL.localRotation = Quaternion.Euler(i % 2 == 0 ? -8f : 6f, 0f, -legSpread[i]);
                        h.legR.localRotation = Quaternion.Euler(i % 2 == 0 ? 6f : -8f, 0f, legSpread[i]);
                    }
                    else
                    {
                        armR = Quaternion.Euler(-20f, 0f, 20f);
                        armL = Quaternion.Euler(-20f, 0f, -20f);
                        h.torso.localRotation = Quaternion.Euler(14f, 0f, 0f);
                        h.head.localRotation = Quaternion.Euler(12f, 0f, 0f);
                    }
                }
                else if (h.busy > 0f || h.dash || phase == Phase.Finisher)
                {
                    // 武器を構える（剣とハンマーはふりかぶる）
                    bool melee = h.weapon == Weapon.Sword || h.weapon == Weapon.Hammer;
                    armR = melee ? Quaternion.Euler(h.dash ? -160f : Mathf.Lerp(-160f, -40f, 1f - h.busy / 0.45f), 0f, 0f) : Quaternion.Euler(-90f, 0f, 0f);
                    armL = Quaternion.Euler(h.weapon == Weapon.Rocket ? -100f : -30f + swing * 0.5f, 0f, -10f);
                }
                else
                {
                    armR = Quaternion.Euler(-swing * 0.8f - 10f, 0f, 8f);
                    armL = Quaternion.Euler(swing * 0.8f - 10f, 0f, -8f);
                }
                h.armR.localRotation = Quaternion.Slerp(h.armR.localRotation, armR, 1f - Mathf.Exp(-dt * 20f));
                h.armL.localRotation = Quaternion.Slerp(h.armL.localRotation, armL, 1f - Mathf.Exp(-dt * 20f));
                // 決めポーズの時は、体が光る
                SetGlow(h.suit, h.color, phase == Phase.Pose && h.poseAt >= 0f ? 1.2f + 0.6f * Mathf.Sin(t * 8f) : 0.35f);
                if (phase != Phase.Pose && phase != Phase.Leave) h.head.localRotation = Quaternion.Slerp(h.head.localRotation, Quaternion.identity, 1f - Mathf.Exp(-dt * 8f));
                // 名前（頭の上でぽんと出て、少しずつ上がる）
                if (h.nameTag.gameObject.activeSelf)
                {
                    float nk = phase == Phase.Pose ? phaseTime - h.poseAt : 9f;
                    if (nk > 2.4f || phase == Phase.Leave) h.nameTag.gameObject.SetActive(false);
                    else
                    {
                        float pop = nk < 0.15f ? Mathf.Lerp(1.8f, 1f, nk / 0.15f) : 1f;
                        h.nameTag.transform.position = h.root.position + Vector3.up * (sH * 2.35f + nk * D * 0.1f);
                        if (cam != null) h.nameTag.transform.rotation = cam.transform.rotation;
                        h.nameTag.transform.localScale = Vector3.one * pop;
                        var nc = h.color; nc.a = Mathf.Clamp01((2.4f - nk) / 0.4f);
                        h.nameTag.color = nc;
                    }
                }

                // マシンガン：連射
                if (h.burstLeft > 0 && on && IsScoring)
                {
                    h.burstTimer -= dt;
                    if (h.burstTimer <= 0f)
                    {
                        h.burstTimer = 0.05f;
                        h.burstLeft--;
                        // ほとんどは怪人へ、ときどき床（はねた弾がメダルをはじく）
                        Vector3 aim = Random.value < 0.65f ? EnemyChest + Random.insideUnitSphere * sE * 0.4f
                                                          : EnemyFeet + new Vector3(Random.Range(-1f, 1f) * sE * 1.2f, 0f, -Random.Range(0.3f, 1.5f) * D);
                        LaunchShot(0, h.muzzle.position, aim, 0.12f, 0f, h.color);
                        Emit(sparks, h.muzzle.position, Color.white, 3);
                        Flash(h.muzzle.position, D * 0.7f * fxScale * Random.Range(0.8f, 1.2f), Color.Lerp(h.color, Color.white, 0.5f), 0.06f);
                        // 薬きょうが横へ飛び散る
                        LaunchCasing(h.muzzle.position - h.root.forward * D * 0.3f, h.root.right * D * Random.Range(2f, 3.5f) + Vector3.up * D * Random.Range(2f, 4f));
                        if (Time.time - lastGunSound > 0.06f)
                        {
                            lastGunSound = Time.time;
                            Play(gunSound, ref madeGun, () => RecordSynth.Noise("HeroGun", 0.06f, 0.7f, 0.002f, 0.03f, 0.6f, 17), 0.4f);
                        }
                    }
                }
            }

            // 光線（少しずつ消える。ちらちら太さが変わる）
            foreach (var b in beams)
            {
                if (b.age < 0f) continue;
                b.age += dt;
                float k = b.age / b.life;
                if (k >= 1f) { b.age = -1f; b.core.enabled = b.glow.enabled = false; continue; }
                float w = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * Random.Range(0.8f, 1.2f);
                b.core.widthMultiplier = D * 0.16f * fxScale * w;
                b.glow.widthMultiplier = D * (phase == Phase.Finisher ? 0.9f : 0.6f) * fxScale * w;
                if (phase == Phase.Finisher) { b.core.SetPosition(1, EnemyChest); b.glow.SetPosition(1, EnemyChest); }
                if (Random.value < 0.5f) Emit(sparks, b.glow.GetPosition(1), Color.white, 1);
            }

            UpdateShots(dt, face);
            fxFace = cam != null ? face : (Quaternion?)null;
            UpdateFx(dt);
            flash = Mathf.MoveTowards(flash, 0f, dt * 2.5f);
            flashLight.intensity = flash * 6f;
            flashLight.enabled = flash > 0f;
            UpdateFrontLayers();
        }
    }
}
