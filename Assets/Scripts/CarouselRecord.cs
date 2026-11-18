using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N015 メリーゴーランド「回転木馬のファンファーレ」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：プッシャーの前の床のまん中に、木馬の付いた回転台（しま模様のとんがり屋根・金の柱・電球・上下する6頭の木馬）がせり上がって回る（当たり判定あり）。
    ///   上に乗ったメダルを回しながら運ぶ。回る向きは flipSeconds 秒ごとに入れかわる
    /// ・モニター：メリーゴーランドの絵（回る）。ファンファーレまでの時間、回る向き、ファンファーレ中の残り
    /// ・ルール：
    ///   fanfareInterval 秒ごとに fanfareSeconds 秒の「ファンファーレ」。その間は回転が速くなり、
    ///   中央の穴に落ちると 1枚 +fanfareBonus pt
    ///   回転台の上に乗っているメダルの数だけ、ファンファーレの始まりに +pointsPerRider pt
    /// ・ファンファーレの前に、回転台の上にメダルを乗せておく遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class CarouselRecord : RecordScriptBase
    {
        public static CarouselRecord Instance { get; private set; }

        [Header("回転台")]
        [Tooltip("回転台の半径（メダルの直径に対する倍率）")]
        [Range(0.5f, 4f)] public float radius = 2.1f;
        [Tooltip("屋根までの高さ（メダルの直径に対する倍率）")]
        [Range(1f, 5f)] public float height = 2.3f;
        [Tooltip("ふだんの回る速さ（1秒あたりの角度）")]
        [Range(0f, 360f)] public float spinSpeed = 40f;
        [Tooltip("ファンファーレ中の回る速さ")]
        [Range(0f, 720f)] public float fanfareSpin = 110f;
        [Range(1f, 60f)] public float flipSeconds = 10f;

        [Header("ファンファーレ")]
        [Range(2f, 60f)] public float fanfareInterval = 10f;
        [Range(1f, 20f)] public float fanfareSeconds = 3.5f;
        [Range(0, 100)] public int fanfareBonus = 15;
        [Range(0, 50)] public int pointsPerRider = 2;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip fanfareSound;
        public AudioClip musicBoxSound;
        [Tooltip("回転台の Prefab（空なら仮の回転台）。当たり判定はこのスクリプトが付ける")]
        public GameObject carouselPrefab;

        protected override string DefaultKey => "Carousel";
        protected override string ScreenTitle => "CAROUSEL";
        protected override string ScreenSubtitle => "回転木馬のファンファーレ";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<CarouselRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Pink = new Color32(0xff, 0x8a, 0xc8, 0xff);
        static readonly Color Mint = new Color32(0x8a, 0xf0, 0xd0, 0xff);
        static readonly Color Cream = new Color32(0xff, 0xf0, 0xd0, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        float angle, flipTimer, nextFanfare, fanfareTimer;
        int spinDir = 1, riders;
        bool Fanfare => fanfareTimer > 0f;
        readonly List<Medal> near = new List<Medal>();

        Transform platform;
        Rigidbody platformBody;
        readonly List<Transform> horses = new List<Transform>();
        readonly List<Light> bulbs = new List<Light>();
        readonly List<Material> bulbMats = new List<Material>();
        Transform mirrorColumn;
        ParticleSystem confetti, sparkle;

        RectTransform wheel;
        readonly List<RectTransform> screenHorses = new List<RectTransform>();
        Text timerText, dirText, statusText;
        AudioClip madeFanfare, madeBox;

        Vector3 Center => new Vector3(0f, 0f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f));

        protected override void BuildBoard()
        {
            platform = new GameObject("Carousel").transform;
            platform.SetParent(Board, false);
            platform.position = Center;
            platformBody = platform.gameObject.AddComponent<Rigidbody>();
            platformBody.isKinematic = true;
            platformBody.interpolation = RigidbodyInterpolation.Interpolate;
            float r = D * radius;
            if (carouselPrefab != null) Instantiate(carouselPrefab, platform).transform.localPosition = Vector3.zero;
            else BuildModel(r, D * height);
            // 当たり判定：台の上面（メダルが乗る）
            var col = platform.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = ProtoAssets.CreateCylinderMesh(r, D * 0.16f, 24);   // 中心が原点。上の面が床より少し上に出る
            col.convex = true;
            confetti = Particles("Confetti", Board, Vector3.up, D * 4f, D * 0.12f, 1.8f, 0.4f, 40f);
            sparkle = Particles("Sparkle", Board, Vector3.up, D * 1.5f, D * 0.07f, 0.9f, -0.1f, 80f);
        }

        static Material Shiny(Color c, float metal, float smooth, float glow)
        {
            var m = ProtoAssets.MakeMaterial(c, metal, smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            return m;
        }

        /// <summary>回転台の見た目を組み立てる（r は台の半径、H は屋根の下までの高さ）</summary>
        void BuildModel(float r, float H)
        {
            var cream = Shiny(Cream, 0.1f, 0.7f, 0.15f);
            var pink = Shiny(Pink, 0.1f, 0.75f, 0.35f);
            var gold = Shiny(Gold, 0.95f, 0.9f, 0.35f);
            var mint = Shiny(Mint, 0.1f, 0.75f, 0.3f);
            var mirror = Shiny(new Color(0.8f, 0.92f, 1f), 1f, 0.97f, 0.5f);
            var deep = Shiny(new Color32(0x6a, 0x1c, 0x4a, 0xff), 0.2f, 0.7f, 0.1f);

            // 台（クリーム色の床と、金のふち、ふちの電球）
            Shape(PrimitiveType.Cylinder, "Deck", platform, new Vector3(0f, D * 0.05f, 0f), new Vector3(r * 2f, D * 0.05f, r * 2f), cream);
            Shape(PrimitiveType.Cylinder, "DeckRim", platform, new Vector3(0f, D * 0.03f, 0f), new Vector3(r * 2.06f, D * 0.04f, r * 2.06f), gold);
            Shape(PrimitiveType.Cylinder, "DeckInlay", platform, new Vector3(0f, D * 0.101f, 0f), new Vector3(r * 1.5f, D * 0.002f, r * 1.5f), deep);
            for (int i = 0; i < 28; i++)
            {
                float a = i * Mathf.PI * 2f / 28f;
                var m = Shiny(i % 2 == 0 ? Gold : Color.white, 0f, 0.8f, 2.5f);
                bulbMats.Add(m);
                Shape(PrimitiveType.Sphere, "RimBulb", platform, new Vector3(Mathf.Cos(a) * r * 1.01f, D * 0.08f, Mathf.Sin(a) * r * 1.01f), Vector3.one * D * 0.09f, m);
            }

            // まん中の柱（鏡の板つき）
            mirrorColumn = new GameObject("Column").transform;
            mirrorColumn.SetParent(platform, false);
            Shape(PrimitiveType.Cylinder, "ColumnCore", mirrorColumn, new Vector3(0f, H * 0.5f, 0f), new Vector3(r * 0.34f, H * 0.5f, r * 0.34f), deep);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var panel = Cube("Mirror", mirrorColumn, Quaternion.Euler(0f, a, 0f) * new Vector3(0f, H * 0.5f, r * 0.175f), new Vector3(r * 0.12f, H * 0.62f, D * 0.02f), mirror);
                panel.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            }
            Shape(PrimitiveType.Cylinder, "ColumnTopRing", mirrorColumn, new Vector3(0f, H * 0.86f, 0f), new Vector3(r * 0.4f, D * 0.05f, r * 0.4f), gold);
            Shape(PrimitiveType.Cylinder, "ColumnLowRing", mirrorColumn, new Vector3(0f, H * 0.14f, 0f), new Vector3(r * 0.4f, D * 0.05f, r * 0.4f), gold);

            // とんがり屋根（ピンクとクリームのしま）
            int seg = 14;
            float roofH = D * 1.05f, roofR = r * 1.1f;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * 360f / seg, a1 = (i + 1) * 360f / seg;
                RecordMeshKit.Make("Roof", RecordMeshKit.ConeSector(roofR, roofH, a0, a1, 4), i % 2 == 0 ? pink : cream, platform, new Vector3(0f, H, 0f), Vector3.one, Vector3.zero);
            }
            Shape(PrimitiveType.Cylinder, "RoofBase", platform, new Vector3(0f, H, 0f), new Vector3(roofR * 2f, D * 0.04f, roofR * 2f), gold);
            // 屋根のふちのフリルと電球
            for (int i = 0; i < seg * 2; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / (seg * 2);
                Vector3 p = new Vector3(Mathf.Cos(a) * roofR * 0.99f, H - D * 0.1f, Mathf.Sin(a) * roofR * 0.99f);
                var flap = Shape(PrimitiveType.Sphere, "Flap", platform, p, new Vector3(roofR * 0.22f, D * 0.22f, D * 0.05f), i % 2 == 0 ? pink : cream);
                flap.transform.localRotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                var m = Shiny(i % 2 == 0 ? Color.white : Gold, 0f, 0.8f, 2.5f);
                bulbMats.Add(m);
                Shape(PrimitiveType.Sphere, "RoofBulb", platform, p + Vector3.down * D * 0.13f, Vector3.one * D * 0.08f, m);
            }
            // 屋根のてっぺん
            Shape(PrimitiveType.Sphere, "TopBall", platform, new Vector3(0f, H + roofH + D * 0.08f, 0f), Vector3.one * D * 0.22f, gold);
            Shape(PrimitiveType.Cylinder, "FlagPole", platform, new Vector3(0f, H + roofH + D * 0.35f, 0f), new Vector3(D * 0.03f, D * 0.25f, D * 0.03f), gold);
            Cube("Flag", platform, new Vector3(D * 0.14f, H + roofH + D * 0.5f, 0f), new Vector3(D * 0.26f, D * 0.15f, D * 0.01f), pink);

            // 金の柱と、上下する木馬（6頭）
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f;
                Vector3 at = new Vector3(Mathf.Cos(a) * r * 0.68f, 0f, Mathf.Sin(a) * r * 0.68f);
                Shape(PrimitiveType.Cylinder, $"Pole{i}", platform, at + Vector3.up * H * 0.5f, new Vector3(D * 0.06f, H * 0.5f, D * 0.06f), gold);
                for (int k = 0; k < 10; k++)   // ねじれた柱に見えるらせんの玉
                {
                    float t = (k + 0.5f) / 10f;
                    float sa = t * Mathf.PI * 6f;
                    Shape(PrimitiveType.Sphere, "Twist", platform, at + new Vector3(Mathf.Cos(sa) * D * 0.035f, H * t, Mathf.Sin(sa) * D * 0.035f), Vector3.one * D * 0.045f, gold);
                }
                var horse = BuildHorse(platform, D * 0.95f, i % 3 == 0 ? Color.white : i % 3 == 1 ? Pink : Mint, i % 2 == 0 ? gold : pink);
                horse.localPosition = at + Vector3.up * H * 0.38f;
                horse.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
                horses.Add(horse);
            }

            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.4f;
                bulbs.Add(PointLight($"Bulb{i}", platform, new Vector3(Mathf.Cos(a) * r * 0.6f, H * 0.9f, Mathf.Sin(a) * r * 0.6f), i % 2 == 0 ? Pink : Gold, r * 1.8f));
            }
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
            // 足（前足は上げて、跳ねているように）
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

        protected override void OnBegin()
        {
            flipTimer = flipSeconds;
            nextFanfare = fanfareInterval;
            fanfareTimer = 0f;
        }

        protected override void Tick(float dt, bool running)
        {
            if (!running) return;
            flipTimer -= dt;
            if (flipTimer <= 0f)
            {
                flipTimer = flipSeconds;
                spinDir = -spinDir;
            }
            riders = MedalsNear(Center, D * radius, near).Count;
            if (Fanfare)
            {
                fanfareTimer -= dt;
                return;
            }
            nextFanfare -= dt;
            if (nextFanfare <= 0f)
            {
                nextFanfare = fanfareInterval;
                fanfareTimer = fanfareSeconds;
                int pts = riders * pointsPerRider;
                AddPoints(pts);
                Emit(confetti, Center + Vector3.up * D * 1.2f, Pink, 40);
                Emit(confetti, Center + Vector3.up * D * 1.2f, Gold, 40);
                Play(fanfareSound, ref madeFanfare, () => RecordSynth.Notes("CarouselFanfare", new[] { RecordSynth.Note(0), RecordSynth.Note(4), RecordSynth.Note(7), RecordSynth.Note(12), RecordSynth.Note(7), RecordSynth.Note(12) }, 0.1f, 0.6f, 0.3f, RecordSynth.Wave.Square));
                PopCenter(pts > 0 ? $"ファンファーレ！ 乗客 {riders}枚 +{pts}" : "ファンファーレ！", Gold);
                Speaker(0.8f, 10, Pink);
            }
        }

        void FixedUpdate()
        {
            if (platformBody == null || !Active) return;
            float speed = (Fanfare ? fanfareSpin : spinSpeed) * spinDir;
            angle += speed * Time.fixedDeltaTime;
            platformBody.MoveRotation(Quaternion.Euler(0f, angle, 0f));
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            if (!Fanfare)
            {
                Play(musicBoxSound, ref madeBox, () => RecordSynth.Notes("CarouselBox", new[] { RecordSynth.Note(19 + Random.Range(0, 5)) }, 0.05f, 0.4f, 0.2f, RecordSynth.Wave.Bell), 0.5f);
                return;
            }
            AddPoints(fanfareBonus);
            Pop($"+{fanfareBonus}", Pink, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 300f));
        }

        protected override bool KeepBoardVisible() => (confetti != null && confetti.particleCount > 0) || (sparkle != null && sparkle.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            var c = Center;
            platform.position = new Vector3(c.x, -D * (height + 1.4f) * (1f - BoardShown), c.z);
            if (platformBody != null) platformBody.position = platform.position;
            float H = D * height;
            for (int i = 0; i < horses.Count; i++)
            {
                var p = horses[i].localPosition;
                horses[i].localPosition = new Vector3(p.x, H * 0.38f + Mathf.Sin(Time.time * (Fanfare ? 7f : 2.6f) + i * 1.05f) * D * 0.28f, p.z);
                float tilt = Mathf.Cos(Time.time * (Fanfare ? 7f : 2.6f) + i * 1.05f) * 10f;
                var e = horses[i].localRotation.eulerAngles;
                horses[i].localRotation = Quaternion.Euler(tilt, e.y, 0f);
            }
            if (mirrorColumn != null) mirrorColumn.localRotation = Quaternion.Euler(0f, -Time.time * 40f, 0f);
            for (int i = 0; i < bulbs.Count; i++)
            {
                bool on = Fanfare ? Mathf.Repeat(Time.time * 6f + i, 2f) < 1f : true;
                bulbs[i].intensity = (on ? 2f : 0.3f) * BoardShown;
            }
            for (int i = 0; i < bulbMats.Count; i++)
            {
                bool on = Fanfare ? (i + Mathf.FloorToInt(Time.time * 12f)) % 3 == 0 : (i + Mathf.FloorToInt(Time.time * 3f)) % 2 == 0;
                SetGlow(bulbMats[i], bulbMats[i].color, on ? 3f : 0.6f);
            }
            if (Active && Fanfare && Random.value < dt * 20f)
                Emit(sparkle, platform.position + new Vector3(Random.Range(-1f, 1f) * D * radius, H * Random.Range(0.3f, 1.1f), Random.Range(-1f, 1f) * D * radius), Random.value < 0.5f ? Gold : Pink, 2);
        }

        protected override void BuildScreen()
        {
            screenHorses.Clear();
            float W = BodyW;
            var stage = Panel(Screen, "Night", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x1c, 0x12, 0x2a, 0xff));
            float sw = W - 48f;
            Img(stage, "Tent", sw * 0.5f, 70f, 420f, 90f, Pink, MonitorDisplay.ScreenShape.Pill);
            Img(stage, "TentTop", sw * 0.5f, 30f, 60f, 60f, Gold, MonitorDisplay.ScreenShape.Circle);
            var w = Grp(stage, "Wheel", sw * 0.5f, 220f, 380f, 380f);
            Img(w, "Rim", 190f, 190f, 380f, 120f, Cream, MonitorDisplay.ScreenShape.Ring);
            wheel = w;
            for (int i = 0; i < 6; i++)
            {
                var h = Grp(stage, $"Horse{i}", sw * 0.5f, 220f, 50f, 60f);
                Img(h, "Body", 25f, 30f, 46f, 30f, i % 2 == 0 ? Pink : Mint, MonitorDisplay.ScreenShape.Pill);
                Img(h, "Head", 42f, 14f, 20f, 22f, i % 2 == 0 ? Pink : Mint, MonitorDisplay.ScreenShape.Pill);
                Img(h, "Pole", 25f, 30f, 4f, 70f, Gold);
                screenHorses.Add(h);
            }
            timerText = Lbl(Screen, "Timer", 24f, 440f, 500f, 50f, "", 30, Gold, TextAnchor.MiddleLeft);
            dirText = Lbl(Screen, "Dir", W - 24f, 440f, 400f, 40f, "", 22, Ink, TextAnchor.MiddleRight);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float sw = BodyW - 48f;
            for (int i = 0; i < screenHorses.Count; i++)
            {
                float a = (angle * Mathf.Deg2Rad) + i * Mathf.PI * 2f / screenHorses.Count;
                float x = sw * 0.5f + Mathf.Cos(a) * 170f;
                float y = 220f + Mathf.Sin(a) * 45f - Mathf.Abs(Mathf.Sin(Time.time * 4f + i)) * 10f;
                Place(screenHorses[i], x, y);
                screenHorses[i].SetSiblingIndex(screenHorses[i].parent.childCount - 1);
                float depth = 0.75f + 0.25f * Mathf.Sin(a);
                Scale(screenHorses[i], depth);
            }
            timerText.text = Fanfare ? $"ファンファーレ！ {Mathf.CeilToInt(fanfareTimer)}" : $"ファンファーレまで {Mathf.CeilToInt(nextFanfare)}";
            timerText.color = Fanfare ? Pink : Gold;
            dirText.text = $"回る向き {(spinDir > 0 ? "↺" : "↻")}  乗客 {riders}枚";
            statusText.text = Fanfare
                ? $"<color=#FF8AC8>ファンファーレ中！</color>  中央の穴に落とすと +{fanfareBonus}pt"
                : $"ファンファーレの始まりに、回転台の乗客 1枚につき +{pointsPerRider}pt";
        }
    }
}
