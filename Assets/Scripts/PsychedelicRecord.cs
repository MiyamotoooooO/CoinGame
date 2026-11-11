using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N036 サイケデリック「虹のうねり」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：盤面の照明が虹色にうねる（色の変わるライトが3つ回る）。虹メダルは色が変わり続ける
    /// ・モニター：万華鏡。虹メダルの数と、虹メダルで発動した効果の一覧
    /// ・ルール（CSV のとおり）：
    ///   変化メダル（星・金・氷・炎・音符・小判・鉄・巨大・花火玉・幸福 など、印の付いたメダル）が中央の穴に落ちるたび、
    ///   盤面のメダル1枚を虹メダルに変える
    ///   虹メダルが中央の穴に落ちると、ほかの変化メダルの効果をすべて発動する（悪魔メダルは除く）。+rainbowBonus pt も足す
    ///   変化メダルが盤面に無い時のために、seedSeconds 秒ごとに虹メダルが1枚できる
    /// ・ほかのレコードで作った変化メダルを残しておき、サイケデリックで虹に増やす遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// （前の効果 TransformOnDrop / Rainbow を、このスクリプトで作り直した）
    /// </summary>
    [DisallowMultipleComponent]
    public class PsychedelicRecord : RecordScriptBase
    {
        public static PsychedelicRecord Instance { get; private set; }

        public const string RainbowKind = "Rainbow";

        [Header("虹メダル")]
        [Tooltip("変化メダル1枚で、虹メダルにする枚数")]
        [Range(1, 10)] public int rainbowPerDrop = 1;
        [Range(0, 300)] public int rainbowBonus = 20;
        [Range(2f, 60f)] public float seedSeconds = 12f;

        [Header("照明")]
        [Range(0f, 10f)] public float lightIntensity = 2.5f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip transformSound;
        public AudioClip rainbowSound;

        protected override string DefaultKey => "Psychedelic";
        protected override string ScreenTitle => "PSYCHEDELIC";
        protected override string ScreenSubtitle => "虹のうねり";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<PsychedelicRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed()
        {
            if (Instance == this) Instance = null;
            MedalMarks.MarkedMedalDropped -= OnMarkedDropped;
        }

        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        static Color Rainbow(float offset) => Color.HSVToRGB(Mathf.Repeat(Time.time * 0.35f + offset, 1f), 0.75f, 1f);

        float seedTimer, pulse;
        int made, triggered;
        readonly List<Medal> scratch = new List<Medal>();
        readonly List<string> lastKinds = new List<string>();

        readonly List<Light> lights = new List<Light>();
        ParticleSystem swirl;

        readonly List<Image> petals = new List<Image>();
        RectTransform kaleido;
        Text countText, kindsText, statusText;
        AudioClip madeTransform, madeRainbow;

        protected override void BuildBoard()
        {
            for (int i = 0; i < 3; i++) lights.Add(PointLight($"Wave{i}", Board, Vector3.zero, Color.white, Layout.boardWidth * 0.8f));
            swirl = Particles("Swirl", Board, Vector3.up, D * 1.5f, D * 0.12f, 1f, -0.05f, 60f);
            MedalMarks.MarkedMedalDropped += OnMarkedDropped;
            MedalMarks.RegisterEffect(RainbowKind, (m, type) =>
            {
                if (type != DropZoneType.Center || effects == null) return;
                effects.AddPoints(rainbowBonus);
                lastKinds.Clear();
                foreach (var kind in new List<string>(MedalMarks.RegisteredKinds))
                {
                    if (kind == RainbowKind || kind == DealRecord.DevilKind) continue;
                    MedalMarks.InvokeEffect(kind, m, type);
                    lastKinds.Add(kind);
                }
                triggered++;
                Play(rainbowSound, ref madeRainbow, () => RecordSynth.Notes("PsyRainbow", new[] { RecordSynth.Note(0), RecordSynth.Note(4), RecordSynth.Note(7), RecordSynth.Note(11), RecordSynth.Note(14), RecordSynth.Note(18), RecordSynth.Note(21) }, 0.05f, 0.7f, 0.3f, RecordSynth.Wave.Triangle));
                PopCenter($"虹メダル！ 効果 {lastKinds.Count}個 発動", Rainbow(0f));
                Speaker(1f, 16, Rainbow(0.3f));
            });
        }

        void OnMarkedDropped(string kind, Medal medal, DropZoneType type)
        {
            if (!Active || type != DropZoneType.Center || kind == RainbowKind || !IsScoring) return;
            for (int i = 0; i < rainbowPerDrop; i++) MakeRainbow();
        }

        void MakeRainbow()
        {
            var m = PickMedal(x => !MedalMarks.Is(x, RainbowKind) && x.transform.position.z < PusherFrontZ);
            if (m == null) return;
            MedalMarks.Set(m, RainbowKind, Rainbow(0f), 3f);
            made++;
            pulse = 1f;
            Emit(swirl, m.transform.position, Rainbow(0.5f), 20);
            Play(transformSound, ref madeTransform, () => RecordSynth.Sweep("PsyTransform", 300f, 1200f, 0.4f, 0.25f, RecordSynth.Wave.Triangle), 0.7f);
        }

        protected override void OnBegin()
        {
            seedTimer = 2f;
            made = 0;
            triggered = 0;
            lastKinds.Clear();
        }

        protected override void Tick(float dt, bool running)
        {
            pulse = Mathf.MoveTowards(pulse, 0f, dt * 2f);
            if (!running) return;
            seedTimer -= dt;
            if (seedTimer <= 0f)
            {
                seedTimer = seedSeconds;
                MakeRainbow();
            }
        }

        protected override bool KeepBoardVisible() => swirl != null && swirl.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            for (int i = 0; i < lights.Count; i++)
            {
                float a = t * 0.8f + i * Mathf.PI * 2f / 3f;
                lights[i].transform.position = new Vector3(Mathf.Cos(a) * HalfW * 0.6f, Layout.wallHeight * 0.5f, Mathf.Lerp(FrontZ, BackZ, 0.5f) + Mathf.Sin(a) * D * 2f);
                lights[i].color = Rainbow(i / 3f);
                lights[i].intensity = lightIntensity * BoardShown * (1f + 0.6f * pulse);
            }
            MedalMarks.Collect(RainbowKind, scratch);
            for (int i = 0; i < scratch.Count; i++) MedalMarks.Recolor(scratch[i], Rainbow(i * 0.13f), 3f);
        }

        protected override void BuildScreen()
        {
            petals.Clear();
            float W = BodyW;
            var panel = Panel(Screen, "Kaleido", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x10, 0x08, 0x18, 0xff));
            float pw = W - 48f;
            var k = Grp(panel, "K", pw * 0.3f, 180f, 320f, 320f);
            for (int i = 0; i < 12; i++)
            {
                var p = Img(k, $"Petal{i}", 160f, 160f, 60f, 300f, Color.white, MonitorDisplay.ScreenShape.Pill);
                p.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 15f);
                petals.Add(p);
            }
            kaleido = k;
            countText = Lbl(panel, "Count", pw * 0.72f, 120f, pw * 0.5f, 80f, "", 44, Ink);
            kindsText = Lbl(panel, "Kinds", pw * 0.72f, 230f, pw * 0.5f, 120f, "", 18, Mute);
            kindsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            kaleido.localRotation = Quaternion.Euler(0f, 0f, Time.time * 30f);
            Scale(kaleido, 1f + 0.1f * pulse + 0.05f * Mathf.Sin(Time.time * 2f));
            for (int i = 0; i < petals.Count; i++)
            {
                Color c = Rainbow(i / (float)petals.Count);
                petals[i].color = new Color(c.r, c.g, c.b, 0.55f);
            }
            countText.text = $"虹メダル {MedalMarks.Count(RainbowKind)}枚";
            countText.color = Rainbow(0f);
            kindsText.text = lastKinds.Count > 0 ? "さっき発動：" + string.Join("・", lastKinds) : $"作った虹メダル {made}枚 ／ 発動 {triggered}回";
            statusText.text = $"変化メダルが中央の穴に落ちると、盤面のメダルが虹メダルに。虹メダルは中央の穴で、ほかの変化メダルの効果をぜんぶ発動";
        }
    }
}
