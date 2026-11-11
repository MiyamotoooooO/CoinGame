using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N014 城壁「籠城戦」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：端の穴の前に、石の城壁が左右からせり上がる（当たり判定あり。メダルは端の穴に落ちなくなる）。
    ///   城壁の上で旗がはためく。城壁にメダルが寄りかかると、城壁のふちが光る
    /// ・モニター：お城と、左右の城壁にたまった兵（城壁に寄りかかっているメダルの数）、籠城ポイント
    /// ・ルール：
    ///   発動中は端の穴の前に城壁が立つ（CSV の「端の穴をふさぐ」）
    ///   siegeSeconds 秒ごとに、城壁の近くにいるメダルを数え、1枚 +pointsPerDefender pt（1回で最大 maxPerSiege 枚）
    ///   城壁の近くのメダルが多いほど、中央の穴へ流れやすい
    /// ・端の発射口からメダルを入れて城壁の前に兵を集める遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// （前の効果 FieldGimmick / BlockSides は RecordEffectSystem の柵。Scripted にすると、こちらの城壁だけが出る）
    /// </summary>
    [DisallowMultipleComponent]
    public class CastleRecord : RecordScriptBase
    {
        public static CastleRecord Instance { get; private set; }

        [Header("城壁")]
        [Tooltip("城壁の高さ（メダルの直径に対する倍率）")]
        [Range(0.2f, 3f)] public float wallHeight = 0.7f;

        [Header("籠城")]
        [Range(1f, 20f)] public float siegeSeconds = 3f;
        [Tooltip("城壁の近くとみなす距離（メダルの直径に対する倍率）")]
        [Range(0.3f, 4f)] public float defendRange = 1.2f;
        [Range(0, 20)] public int pointsPerDefender = 1;
        [Range(1, 50)] public int maxPerSiege = 10;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip riseSound;
        public AudioClip siegeSound;
        [Tooltip("城壁1つの Prefab（空なら仮の石の壁）。当たり判定はこのスクリプトが付ける")]
        public GameObject wallPrefab;

        protected override string DefaultKey => "Castle";
        protected override string ScreenTitle => "CASTLE";
        protected override string ScreenSubtitle => "籠城戦";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<CastleRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Stone = new Color32(0x8a, 0x84, 0x7a, 0xff);
        static readonly Color Banner = new Color32(0xd9, 0x3a, 0x3a, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color Sky = new Color32(0x1a, 0x1e, 0x3a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        float siegeTimer, siegePulse;
        int total;
        readonly int[] defenders = new int[2];
        readonly List<Medal> near = new List<Medal>();

        readonly Transform[] walls = new Transform[2];
        readonly Transform[] flags = new Transform[2];
        readonly Material[] rimMaterials = new Material[2];

        readonly Text[] defenderTexts = new Text[2];
        readonly List<Image>[] soldiers = { new List<Image>(), new List<Image>() };
        Text totalText, timerText, statusText;
        AudioClip madeRise, madeSiege;

        float WallX(int side) => side * (HoleHalf + (HalfW - HoleHalf) * 0.5f);
        float WallW => HalfW - HoleHalf;

        protected override void BuildBoard()
        {
            var stone = Glow(Stone, 0.05f);
            var banner = Glow(Banner, 0.4f);
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                var holder = new GameObject(side < 0 ? "WallLeft" : "WallRight").transform;
                holder.SetParent(Board, false);
                holder.position = new Vector3(WallX(side), 0f, FrontZ - D * 0.08f);
                float h = D * wallHeight;
                if (wallPrefab != null) Instantiate(wallPrefab, holder).transform.localPosition = Vector3.zero;
                else
                {
                    Cube("Wall", holder, new Vector3(0f, h * 0.5f, 0f), new Vector3(WallW, h, D * 0.16f), stone);
                    for (int k = 0; k < 4; k++)
                        Cube($"Merlon{k}", holder, new Vector3(-WallW * 0.375f + k * WallW * 0.25f, h + D * 0.08f, 0f), new Vector3(WallW * 0.12f, D * 0.16f, D * 0.16f), stone);
                }
                // 当たり判定（メダルを止める）
                var col = holder.gameObject.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, h * 0.5f, 0f);
                col.size = new Vector3(WallW, h, D * 0.16f);
                rimMaterials[i] = Glow(Gold, 0f);
                Cube("Rim", holder, new Vector3(0f, D * 0.02f, D * 0.1f), new Vector3(WallW * 0.98f, D * 0.03f, D * 0.03f), rimMaterials[i]);
                Cube("Pole", holder, new Vector3(side * WallW * 0.4f, h + D * 0.45f, 0f), new Vector3(D * 0.04f, D * 0.8f, D * 0.04f), stone);
                var flag = Cube("Flag", holder, new Vector3(side * WallW * 0.4f - side * D * 0.2f, h + D * 0.7f, 0f), new Vector3(D * 0.4f, D * 0.25f, D * 0.02f), banner);
                flags[i] = flag.transform;
                walls[i] = holder;
            }
        }

        protected override void OnBegin()
        {
            siegeTimer = siegeSeconds;
            total = 0;
            defenders[0] = defenders[1] = 0;
            Play(riseSound, ref madeRise, () => RecordSynth.Noise("CastleRise", 0.9f, 0.04f, 0.2f, 0.5f, 1f, 5));
        }

        protected override void Tick(float dt, bool running)
        {
            siegePulse = Mathf.MoveTowards(siegePulse, 0f, dt * 2f);
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                defenders[i] = MedalsNear(new Vector3(WallX(side), 0f, FrontZ + D * 0.3f), D * defendRange + WallW * 0.4f, near).Count;
            }
            if (!running) return;
            siegeTimer -= dt;
            if (siegeTimer > 0f) return;
            siegeTimer = siegeSeconds;
            int n = Mathf.Min(maxPerSiege, defenders[0] + defenders[1]);
            if (n <= 0) return;
            int pts = n * pointsPerDefender;
            total += pts;
            AddPoints(pts);
            siegePulse = 1f;
            Play(siegeSound, ref madeSiege, () => RecordSynth.Notes("CastleSiege", new[] { 392f, 523f, 659f }, 0.07f, 0.25f, 0.25f, RecordSynth.Wave.Square), 0.6f);
            Pop($"籠城 +{pts}", Gold, new Vector2(BodyW * 0.5f, 120f));
        }

        protected override void UpdateBoard(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                var p = walls[i].position;
                walls[i].position = new Vector3(p.x, -D * wallHeight * 1.2f * (1f - BoardShown), p.z);
                flags[i].localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 6f + i) * 20f, 0f);
                SetGlow(rimMaterials[i], Gold, (0.3f + 0.4f * Mathf.Min(1f, defenders[i] / 5f) + 2f * siegePulse) * BoardShown);
            }
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var sky = Panel(Screen, "Sky", W * 0.5f, 200f, W - 48f, 360f, Sky);
            float sw = W - 48f;
            Img(sky, "Moon", sw * 0.85f, 60f, 60f, 60f, new Color32(0xf8, 0xf0, 0xc8, 0xff), MonitorDisplay.ScreenShape.Circle);
            Img(sky, "Ground", sw * 0.5f, 330f, sw, 60f, new Color32(0x2a, 0x30, 0x22, 0xff));
            Img(sky, "Keep", sw * 0.5f, 210f, 160f, 200f, Stone);
            Img(sky, "KeepRoof", sw * 0.5f, 100f, 200f, 30f, Stone);
            Img(sky, "Gate", sw * 0.5f, 270f, 60f, 80f, new Color32(0x30, 0x22, 0x18, 0xff), MonitorDisplay.ScreenShape.Pill);
            Img(sky, "Banner", sw * 0.5f + 20f, 60f, 50f, 30f, Banner);
            Img(sky, "Pole", sw * 0.5f - 6f, 70f, 4f, 60f, Ink);
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                float cx = sw * 0.5f + side * sw * 0.3f;
                Img(sky, $"Wall{i}", cx, 270f, sw * 0.28f, 80f, Stone);
                soldiers[i].Clear();
                for (int k = 0; k < 10; k++)
                    soldiers[i].Add(Img(sky, $"Soldier{i}_{k}", cx - sw * 0.12f + k * sw * 0.026f, 220f, 14f, 22f, Gold, MonitorDisplay.ScreenShape.Pill));
                defenderTexts[i] = Lbl(sky, $"Def{i}", cx, 160f, sw * 0.28f, 36f, "", 24, Ink);
            }
            totalText = Lbl(Screen, "Total", 24f, 440f, 500f, 50f, "", 30, Gold, TextAnchor.MiddleLeft);
            timerText = Lbl(Screen, "Timer", W - 24f, 440f, 400f, 40f, "", 22, Ink, TextAnchor.MiddleRight);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                defenderTexts[i].text = $"{(i == 0 ? "左" : "右")}の城壁  兵 {defenders[i]}";
                for (int k = 0; k < soldiers[i].Count; k++)
                {
                    bool on = k < defenders[i];
                    soldiers[i][k].color = on ? Gold : new Color(1f, 1f, 1f, 0.1f);
                    Place(soldiers[i][k].rectTransform, soldiers[i][k].rectTransform.anchoredPosition.x, 220f - (on ? Mathf.Abs(Mathf.Sin(Time.time * 6f + k)) * 4f : 0f));
                }
            }
            totalText.text = $"籠城ポイント {total}";
            timerText.text = $"次の籠城まで {siegeTimer:0.0}秒";
            statusText.text = $"端の穴は城壁でふさがれている。城壁の近くの兵 1枚につき +{pointsPerDefender}pt（{siegeSeconds:0}秒ごと）";
        }
    }
}
