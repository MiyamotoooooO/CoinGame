using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N024 おみくじ「ネオン神社」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：中央の穴の上に、しめ縄と鈴。大吉の時は金色に光る
    /// ・モニター：神社。おみくじの筒を振ると、紙が出てくる（大吉・吉・凶）。凶の時は「結ぶ」までの残り枚数
    /// ・ルール（CSV のとおり）：
    ///   発動した瞬間におみくじを引く。
    ///     大吉：中央の穴のポイント ×greatMultiplier
    ///     吉  ：×goodMultiplier
    ///     凶  ：ポイント ×badMultiplier（減る）。その代わり、メダルが落ちた時のゲージのたまり方が ×badGaugeMultiplier
    ///   凶の時、中央の穴に tieCount 枚落とすと、おみくじを木に「結ぶ」：もう一度引ける（1曲で1回）
    /// ・凶をそのまま使ってゲージをためるか、結んで引き直すかを決める遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class OmikujiRecord : RecordScriptBase
    {
        public static OmikujiRecord Instance { get; private set; }

        public enum Fortune { Great, Good, Bad }

        [Header("出やすさ（大吉・吉・凶）")]
        [Range(0f, 1f)] public float greatChance = 0.2f;
        [Range(0f, 1f)] public float goodChance = 0.5f;

        [Header("効果")]
        [Range(1f, 10f)] public float greatMultiplier = 3f;
        [Range(1f, 5f)] public float goodMultiplier = 1.5f;
        [Range(0f, 1f)] public float badMultiplier = 0.5f;
        [Range(1f, 10f)] public float badGaugeMultiplier = 3f;

        [Header("結ぶ（凶の時）")]
        [Range(1, 50)] public int tieCount = 8;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip shakeSound;
        public AudioClip greatSound;
        public AudioClip bellSound;

        protected override string DefaultKey => "Omikuji";
        protected override string ScreenTitle => "OMIKUJI";
        protected override string ScreenSubtitle => "ネオン神社";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<OmikujiRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly string[] Names = { "大吉", "吉", "凶" };
        static readonly Color[] Colors = { new Color32(0xff, 0xd2, 0x3f, 0xff), new Color32(0xff, 0x8a, 0x8a, 0xff), new Color32(0x8a, 0x9a, 0xc8, 0xff) };
        static readonly Color Vermilion = new Color32(0xe0, 0x46, 0x2a, 0xff);
        static readonly Color Paper = new Color32(0xfa, 0xf4, 0xe8, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Sumi = new Color32(0x22, 0x1c, 0x18, 0xff);

        Fortune fortune;
        int tied;
        bool redrawUsed;
        float drawAnim;      // 1 → 0：筒を振って紙が出る演出

        Transform rope;
        Material bellMaterial;
        ParticleSystem petals;

        RectTransform tube, paper;
        Text paperText, effectText, tieText, statusText;
        AudioClip madeShake, madeGreat, madeBell;

        float Multiplier => fortune == Fortune.Great ? greatMultiplier : fortune == Fortune.Good ? goodMultiplier : badMultiplier;

        protected override void BuildBoard()
        {
            rope = new GameObject("Shimenawa").transform;
            rope.SetParent(Board, false);
            rope.position = new Vector3(0f, Layout.wallHeight * 0.75f, FrontZ + D * 0.4f);
            Cube("Rope", rope, Vector3.zero, new Vector3(HoleHalf * 2f, D * 0.12f, D * 0.12f), Glow(new Color(0.85f, 0.75f, 0.5f), 0.2f));
            for (int i = 0; i < 4; i++)
                Cube($"Shide{i}", rope, new Vector3(-HoleHalf * 0.75f + i * HoleHalf * 0.5f, -D * 0.25f, 0f), new Vector3(D * 0.12f, D * 0.35f, D * 0.01f), Glow(Paper, 0.4f));
            bellMaterial = Glow(Colors[0], 1f);
            Shape(PrimitiveType.Sphere, "Bell", rope, new Vector3(0f, -D * 0.3f, 0f), Vector3.one * D * 0.35f, bellMaterial);
            petals = Particles("Petals", Board, Vector3.down, D * 0.6f, D * 0.1f, 3f, 0.05f, 60f);
        }

        protected override void OnBegin()
        {
            redrawUsed = false;
            Draw();
        }

        void Draw()
        {
            float r = Random.value;
            fortune = r < greatChance ? Fortune.Great : r < greatChance + goodChance ? Fortune.Good : Fortune.Bad;
            tied = 0;
            drawAnim = 1f;
            Play(shakeSound, ref madeShake, () => RecordSynth.Noise("OmikujiShake", 0.6f, 0.4f, 0.01f, 0.3f, 0.7f, 19));
        }

        void Reveal()
        {
            if (fortune == Fortune.Great)
            {
                Play(greatSound, ref madeGreat, () => RecordSynth.Notes("OmikujiGreat", new[] { RecordSynth.Note(12), RecordSynth.Note(14), RecordSynth.Note(19), RecordSynth.Note(24) }, 0.1f, 0.8f, 0.3f, RecordSynth.Wave.Bell));
                Emit(petals, new Vector3(0f, Layout.wallHeight, FrontZ + D), new Color32(0xff, 0xb8, 0xd0, 0xff), 60);
                Speaker(1f, 16, Colors[0]);
            }
            PopCenter($"{Names[(int)fortune]}！", Colors[(int)fortune]);
        }

        protected override void Tick(float dt, bool running)
        {
            if (drawAnim > 0f)
            {
                float before = drawAnim;
                drawAnim = Mathf.MoveTowards(drawAnim, 0f, dt * 0.7f);
                if (before > 0.35f && drawAnim <= 0.35f) Reveal();
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type == DropZoneType.Lost || drawAnim > 0.35f) return;
            if (fortune == Fortune.Bad) AddGauge(effects.gaugePerDrop * (badGaugeMultiplier - 1f));
            if (type != DropZoneType.Center) return;

            int extra = AddMultiplied(Multiplier);
            Pop(extra >= 0 ? $"{Names[(int)fortune]} +{CenterPoints + extra}" : $"凶 {CenterPoints + extra}", Colors[(int)fortune], new Vector2(BodyW * Random.Range(0.55f, 0.85f), 300f));
            if (fortune == Fortune.Bad && !redrawUsed)
            {
                tied++;
                if (tied >= tieCount)
                {
                    redrawUsed = true;
                    Play(bellSound, ref madeBell, () => RecordSynth.Notes("OmikujiBell", new[] { 1975f, 1975f }, 0.15f, 0.6f, 0.3f, RecordSynth.Wave.Bell));
                    PopCenter("おみくじを結んだ！ もう一度引く", Paper);
                    Draw();
                }
            }
        }

        protected override bool KeepBoardVisible() => petals != null && petals.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            rope.localScale = new Vector3(Mathf.Max(0.01f, BoardShown), 1f, 1f);
            rope.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2f) * 2f);
            Color c = fortune == Fortune.Great ? Colors[0] : new Color(0.8f, 0.7f, 0.4f);
            SetGlow(bellMaterial, c, fortune == Fortune.Great ? 2f + Mathf.Sin(Time.time * 6f) : 0.4f);
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var shrine = Panel(Screen, "Shrine", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x16, 0x10, 0x1c, 0xff));
            float sw = W - 48f;
            // 鳥居
            Img(shrine, "Kasagi", sw * 0.3f, 50f, 360f, 24f, Vermilion);
            Img(shrine, "Nuki", sw * 0.3f, 90f, 300f, 16f, Vermilion);
            Img(shrine, "PillarL", sw * 0.3f - 120f, 220f, 22f, 300f, Vermilion);
            Img(shrine, "PillarR", sw * 0.3f + 120f, 220f, 22f, 300f, Vermilion);
            // 筒
            var t = Grp(shrine, "Tube", sw * 0.3f, 230f, 80f, 180f);
            Img(t, "Body", 40f, 90f, 70f, 170f, new Color32(0xc8, 0x8a, 0x4a, 0xff), MonitorDisplay.ScreenShape.Pill);
            Lbl(t, "Label", 40f, 90f, 70f, 170f, "御\n神\n籤", 20, Sumi);
            tube = t;
            // 紙
            var p = Grp(shrine, "Paper", sw * 0.68f, 190f, 160f, 280f);
            Img(p, "Sheet", 80f, 140f, 160f, 280f, Paper);
            Img(p, "Top", 80f, 20f, 160f, 30f, Vermilion);
            paperText = Lbl(p, "Text", 80f, 150f, 160f, 200f, "", 64, Sumi);
            paper = p;
            effectText = Lbl(Screen, "Effect", 24f, 430f, W - 48f, 40f, "", 26, Ink, TextAnchor.MiddleLeft);
            tieText = Lbl(Screen, "Tie", W - 24f, 480f, 500f, 30f, "", 20, Mute, TextAnchor.MiddleRight);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float sw = BodyW - 48f;
            bool shaking = drawAnim > 0.35f;
            tube.localRotation = Quaternion.Euler(0f, 0f, shaking ? Mathf.Sin(Time.time * 30f) * 25f : 0f);
            paper.gameObject.SetActive(!shaking);
            float rise = Mathf.Clamp01(1f - drawAnim / 0.35f);
            Place(paper, sw * 0.68f, 190f + (1f - rise) * 80f);
            paperText.text = shaking ? "" : string.Join("\n", Names[(int)fortune].ToCharArray());
            paperText.color = fortune == Fortune.Bad ? new Color32(0x40, 0x40, 0x70, 0xff) : Vermilion;
            effectText.text = shaking ? "おみくじを引いています…" : fortune switch
            {
                Fortune.Great => $"大吉：中央の穴のポイント ×{greatMultiplier:0.#}",
                Fortune.Good => $"吉：中央の穴のポイント ×{goodMultiplier:0.#}",
                _ => $"凶：ポイント ×{badMultiplier:0.#}、でもゲージのたまり方 ×{badGaugeMultiplier:0.#}",
            };
            effectText.color = Colors[(int)fortune];
            tieText.text = fortune == Fortune.Bad && !redrawUsed ? $"結ぶまで あと {tieCount - tied}枚" : "";
            statusText.text = fortune == Fortune.Bad && !redrawUsed
                ? $"凶のままゲージをためるか、中央の穴に {tieCount}枚落として結び、引き直すか"
                : "おみくじの結果は、この曲が終わるまで続く";
        }
    }
}
