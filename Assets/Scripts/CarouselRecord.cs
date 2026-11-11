using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N015 メリーゴーランド「回転木馬のファンファーレ」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：プッシャーの前の床のまん中に、木馬の付いた小さな回転台がせり上がって回る（当たり判定あり）。
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
        [Range(0.5f, 4f)] public float radius = 1.3f;
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
        ParticleSystem confetti;

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
            else
            {
                Shape(PrimitiveType.Cylinder, "Deck", platform, new Vector3(0f, D * 0.04f, 0f), new Vector3(r * 2f, D * 0.04f, r * 2f), Glow(Cream, 0.2f));
                Shape(PrimitiveType.Cylinder, "Pole", platform, new Vector3(0f, D * 0.6f, 0f), new Vector3(D * 0.12f, D * 0.55f, D * 0.12f), Glow(Gold, 0.6f));
                Shape(PrimitiveType.Cylinder, "Roof", platform, new Vector3(0f, D * 1.15f, 0f), new Vector3(r * 1.2f, D * 0.05f, r * 1.2f), Glow(Pink, 0.5f));
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI * 0.5f;
                    var horse = Cube($"Horse{i}", platform, new Vector3(Mathf.Cos(a) * r * 0.7f, D * 0.55f, Mathf.Sin(a) * r * 0.7f), new Vector3(D * 0.12f, D * 0.22f, D * 0.32f), Glow(i % 2 == 0 ? Pink : Mint, 0.6f));
                    horse.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                    Cube($"HorsePole{i}", platform, new Vector3(Mathf.Cos(a) * r * 0.7f, D * 0.7f, Mathf.Sin(a) * r * 0.7f), new Vector3(D * 0.025f, D * 0.9f, D * 0.025f), Glow(Gold, 0.6f));
                    horses.Add(horse.transform);
                    bulbs.Add(PointLight($"Bulb{i}", platform, new Vector3(Mathf.Cos(a) * r * 0.6f, D * 1.2f, Mathf.Sin(a) * r * 0.6f), i % 2 == 0 ? Pink : Gold, D * 2.5f));
                }
            }
            // 当たり判定：台の上面（メダルが乗る）
            var col = platform.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = ProtoAssets.CreateCylinderMesh(r, D * 0.16f, 24);   // 中心が原点。上の面が床より少し上に出る
            col.convex = true;
            confetti = Particles("Confetti", Board, Vector3.up, D * 4f, D * 0.12f, 1.8f, 0.4f, 40f);
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

        protected override bool KeepBoardVisible() => confetti != null && confetti.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            var c = Center;
            platform.position = new Vector3(c.x, -D * 1.4f * (1f - BoardShown), c.z);
            if (platformBody != null) platformBody.position = platform.position;
            for (int i = 0; i < horses.Count; i++)
            {
                var p = horses[i].localPosition;
                horses[i].localPosition = new Vector3(p.x, D * 0.55f + Mathf.Sin(Time.time * (Fanfare ? 8f : 3f) + i * 1.6f) * D * 0.12f, p.z);
            }
            for (int i = 0; i < bulbs.Count; i++)
            {
                bool on = Fanfare ? Mathf.Repeat(Time.time * 6f + i, 2f) < 1f : true;
                bulbs[i].intensity = (on ? 1.5f : 0.2f) * BoardShown;
            }
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
