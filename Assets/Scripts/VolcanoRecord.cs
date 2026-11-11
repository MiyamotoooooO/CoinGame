using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N031 火山「マグマの倍率」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：盤面の奥（プッシャーの上）に小さな火山。eruptInterval 秒ごとに噴火して溶岩が飛び、当たったメダルが赤く燃える（炎メダル）。
    ///   噴火の前は火山がゴゴゴと赤く光る
    /// ・モニター：火山の断面図。マグマの温度計（ラウンドで積み上げた倍率）、次の噴火までの時間、盤面の炎メダルの数
    /// ・ルール（CSV のとおり）：
    ///   噴火すると、プッシャーの前のメダル lavaMedals 枚が炎メダルになる
    ///   炎メダルが中央の穴に落ちると、倍率 +multPerFire（ラウンドの間は積み上がる。次のラウンドで 0 に戻る）
    ///   火山を流している間は、中央の穴のポイントに ×(1 + 積み上げた倍率) が掛かる
    /// ・炎メダルを中央に落として倍率を育て、育ってからたくさん落とす遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class VolcanoRecord : RecordScriptBase
    {
        public static VolcanoRecord Instance { get; private set; }

        public const string FireKind = "Fire";

        [Header("噴火")]
        [Range(2f, 60f)] public float eruptInterval = 10f;
        [Range(1, 20)] public int lavaMedals = 5;

        [Header("倍率")]
        [Range(0f, 1f)] public float multPerFire = 0.1f;
        [Range(0f, 10f)] public float maxStack = 3f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip eruptSound;
        public AudioClip fireSound;
        public GameObject volcanoPrefab;

        protected override string DefaultKey => "Volcano";
        protected override string ScreenTitle => "VOLCANO";
        protected override string ScreenSubtitle => "マグマの倍率";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<VolcanoRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        public static readonly Color FireColor = new Color32(0xff, 0x4a, 0x1a, 0xff);
        static readonly Color Lava = new Color32(0xff, 0x8a, 0x2a, 0xff);
        static readonly Color RockColor = new Color32(0x3a, 0x2a, 0x2a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        /// <summary>このラウンドで積み上げた倍率（ラウンドが変わると 0）</summary>
        public static float RoundStack { get; private set; }

        float eruptTimer, rumble, burst;
        Transform volcano;
        Material craterMaterial;
        Light glow;
        ParticleSystem lava;

        RectTransform magma;
        Image thermoFill;
        Text multText, timerText, fireText, statusText;
        AudioClip madeErupt, madeFire;

        protected override void BuildBoard()
        {
            volcano = new GameObject("Volcano").transform;
            volcano.SetParent(Board, false);
            volcano.position = new Vector3(0f, Layout.pusherHeight, BackZ - D * 0.8f);
            if (volcanoPrefab != null) Instantiate(volcanoPrefab, volcano).transform.localPosition = Vector3.zero;
            else
            {
                var rock = Glow(RockColor, 0.05f);
                for (int i = 0; i < 4; i++)
                {
                    float r = D * (1.1f - i * 0.22f);
                    Shape(PrimitiveType.Cylinder, $"Layer{i}", volcano, new Vector3(0f, D * (0.12f + i * 0.22f), 0f), new Vector3(r * 2f, D * 0.12f, r * 2f), rock);
                }
                craterMaterial = Glow(Lava, 1f);
                Shape(PrimitiveType.Cylinder, "Crater", volcano, new Vector3(0f, D * 0.9f, 0f), new Vector3(D * 0.5f, D * 0.03f, D * 0.5f), craterMaterial);
            }
            glow = PointLight("Glow", volcano, new Vector3(0f, D * 1.4f, -D * 0.5f), FireColor, Layout.boardWidth * 0.7f);
            lava = Particles("Lava", volcano, Vector3.up, D * 6f, D * 0.18f, 1.5f, 1.2f, 30f);
            lava.transform.localPosition = new Vector3(0f, D * 0.9f, 0f);
            MedalMarks.RegisterEffect(FireKind, (m, type) =>
            {
                if (type != DropZoneType.Center || effects == null) return;
                RoundStack = Mathf.Min(maxStack, RoundStack + multPerFire);
                Play(fireSound, ref madeFire, () => RecordSynth.Noise("VolcanoFire", 0.4f, 0.1f, 0.01f, 0.2f, 1f, 23), 0.8f);
                Pop($"倍率 +{multPerFire:0.0}", FireColor, new Vector2(BodyW * 0.7f, 200f));
            });
        }

        protected override void OnStateChanged(GameState state)
        {
            if (state == GameState.Intro || state == GameState.Warmup) RoundStack = 0f;
        }

        protected override void OnBegin() => eruptTimer = eruptInterval;

        protected override void Tick(float dt, bool running)
        {
            burst = Mathf.MoveTowards(burst, 0f, dt);
            rumble = Mathf.Clamp01(1f - eruptTimer / 3f);
            if (!running) return;
            eruptTimer -= dt;
            if (eruptTimer <= 0f)
            {
                eruptTimer = eruptInterval;
                Erupt();
            }
        }

        void Erupt()
        {
            burst = 1f;
            Emit(lava, volcano.position + Vector3.up * D * 0.9f, FireColor, 60);
            int n = 0;
            for (int i = 0; i < lavaMedals; i++)
            {
                var m = PickMedal(x => !MedalMarks.Is(x, FireKind) && x.transform.position.z < PusherFrontZ);
                if (m == null) break;
                MedalMarks.Set(m, FireKind, FireColor, 3f);
                Push(m, Vector3.up * D * 2f);
                n++;
            }
            Play(eruptSound, ref madeErupt, () => RecordSynth.Thud("VolcanoErupt", 40f, 1.4f, 1f));
            Shake(0.04f, 0.6f);
            PopCenter($"噴火！ 炎メダル +{n}", FireColor);
            Speaker(0.9f, 10, FireColor);
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center || RoundStack <= 0f) return;
            int extra = AddMultiplied(1f + RoundStack);
            if (extra > 0) Pop($"×{1f + RoundStack:0.0} +{CenterPoints + extra}", Lava, new Vector2(BodyW * Random.Range(0.3f, 0.6f), 420f));
        }

        protected override bool KeepBoardVisible() => lava != null && lava.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            volcano.localScale = Vector3.one * Mathf.Max(0.01f, BoardShown);
            var p = volcano.position;
            volcano.position = new Vector3(rumble > 0f ? Mathf.Sin(Time.time * 50f) * D * 0.02f * rumble : 0f, p.y, p.z);
            if (craterMaterial != null) SetGlow(craterMaterial, Lava, 0.8f + 3f * rumble + 4f * burst);
            glow.intensity = BoardShown * (0.5f + 2f * rumble + 4f * burst);
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var cut = Panel(Screen, "Cut", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x1a, 0x10, 0x10, 0xff));
            float cw = W - 48f;
            var mountain = Img(cut, "Mountain", cw * 0.35f, 280f, 420f, 300f, RockColor);
            mountain.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Img(cut, "Ground", cw * 0.5f, 340f, cw, 50f, new Color32(0x22, 0x18, 0x18, 0xff));
            var m = Img(cut, "Magma", cw * 0.35f, 300f, 60f, 200f, Lava);
            m.rectTransform.pivot = new Vector2(0.5f, 0f);
            Place(m.rectTransform, cw * 0.35f, 340f);
            magma = m.rectTransform;
            // 温度計
            Img(cut, "ThermoBg", cw * 0.8f, 190f, 40f, 260f, new Color(1f, 1f, 1f, 0.12f), MonitorDisplay.ScreenShape.Pill);
            thermoFill = Img(cut, "Thermo", cw * 0.8f, 190f, 40f, 260f, FireColor, MonitorDisplay.ScreenShape.Pill);
            thermoFill.type = Image.Type.Filled;
            thermoFill.fillMethod = Image.FillMethod.Vertical;
            multText = Lbl(cut, "Mult", cw * 0.8f, 40f, 300f, 60f, "", 40, Lava, TextAnchor.MiddleCenter, true);
            timerText = Lbl(Screen, "Timer", 24f, 440f, 500f, 40f, "", 24, Ink, TextAnchor.MiddleLeft);
            fireText = Lbl(Screen, "Fire", W - 24f, 440f, 400f, 40f, "", 24, FireColor, TextAnchor.MiddleRight);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            magma.sizeDelta = new Vector2(60f, 60f + 200f * Mathf.Max(rumble, burst));
            thermoFill.fillAmount = RoundStack / Mathf.Max(0.01f, maxStack);
            multText.text = $"×{1f + RoundStack:0.0}";
            timerText.text = $"次の噴火まで {Mathf.CeilToInt(eruptTimer)}秒";
            fireText.text = $"炎メダル {MedalMarks.Count(FireKind)}枚";
            statusText.text = $"炎メダルを中央の穴に落とすと倍率 +{multPerFire:0.0}（このラウンドの間ずっと積み上がる）";
        }
    }
}
