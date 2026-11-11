using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N027 駆け引き「天使と悪魔の取引」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：発動した瞬間、盤面のメダルが一斉に白い天使（幸福メダル）と黒い悪魔（悪魔メダル）の輪に塗り替わる
    /// ・モニター：天秤。左に幸福メダル、右に悪魔メダルの数と割合。取引の収支（幸福で得た分・悪魔で失った分・悪魔で得た分）
    /// ・ルール（CSV のとおり）：
    ///   発動した瞬間、盤面のメダルがすべて幸福メダルか悪魔メダルになる（悪魔の割合は minDevil〜maxDevil のあいだで毎回ランダム）
    ///   幸福メダル：中央の穴で +happyBonus pt
    ///   悪魔メダル：中央の穴で −devilPenalty pt、端の穴で +devilSideBonus pt
    ///   曲が終わると元に戻る
    /// ・悪魔メダルを端の穴へ、幸福メダルを中央の穴へ、と落とし分ける遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class DealRecord : RecordScriptBase
    {
        public static DealRecord Instance { get; private set; }

        public const string HappyKind = "Happy";
        public const string DevilKind = "Devil";

        [Header("割合")]
        [Range(0f, 1f)] public float minDevil = 0.2f;
        [Range(0f, 1f)] public float maxDevil = 0.6f;

        [Header("ポイント")]
        [Range(0, 200)] public int happyBonus = 30;
        [Range(0, 200)] public int devilPenalty = 20;
        [Range(0, 200)] public int devilSideBonus = 40;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip dealSound;
        public AudioClip happySound;
        public AudioClip devilSound;

        protected override string DefaultKey => "Deal";
        protected override string ScreenTitle => "DEAL";
        protected override string ScreenSubtitle => "天使と悪魔の取引";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<DealRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        public static readonly Color Angel = new Color32(0xff, 0xfa, 0xf0, 0xff);
        public static readonly Color Devil = new Color32(0x3a, 0x08, 0x30, 0xff);
        static readonly Color DevilGlow = new Color32(0xc8, 0x2a, 0x8a, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        float devilRatio, tilt, flash;
        int gained, lost, devilGained;

        ParticleSystem feathers;

        RectTransform beam;
        Text happyText, devilText, ratioText, ledgerText, statusText;
        Image happyBox, devilBox;
        AudioClip madeDeal, madeHappy, madeDevil;

        protected override void BuildBoard()
        {
            feathers = Particles("Feathers", Board, Vector3.up, D * 2f, D * 0.12f, 1.4f, 0.1f, 60f);
            MedalMarks.RegisterEffect(HappyKind, (m, type) =>
            {
                if (type != DropZoneType.Center || effects == null) return;
                effects.AddPoints(happyBonus);
                gained += happyBonus;
                Play(happySound, ref madeHappy, () => RecordSynth.Notes("DealHappy", new[] { RecordSynth.Note(19), RecordSynth.Note(24) }, 0.06f, 0.4f, 0.25f, RecordSynth.Wave.Bell), 0.7f);
                Pop($"幸福 +{happyBonus}", Gold, new Vector2(BodyW * 0.3f, 300f));
            });
            MedalMarks.RegisterEffect(DevilKind, (m, type) =>
            {
                if (effects == null) return;
                if (type == DropZoneType.Center)
                {
                    effects.AddPoints(-devilPenalty);
                    lost += devilPenalty;
                    Play(devilSound, ref madeDevil, () => RecordSynth.Sweep("DealDevil", 220f, 110f, 0.5f, 0.3f, RecordSynth.Wave.Saw), 0.7f);
                    Pop($"悪魔 -{devilPenalty}", DevilGlow, new Vector2(BodyW * 0.7f, 300f));
                }
                else if (type == DropZoneType.Side)
                {
                    effects.AddPoints(devilSideBonus);
                    devilGained += devilSideBonus;
                    Pop($"悪魔の取引 +{devilSideBonus}", DevilGlow, new Vector2(BodyW * 0.7f, 300f));
                }
            });
        }

        protected override void OnBegin()
        {
            gained = lost = devilGained = 0;
            devilRatio = Random.Range(Mathf.Min(minDevil, maxDevil), Mathf.Max(minDevil, maxDevil));
            foreach (var m in new List<Medal>(BoardMedals))
            {
                if (m == null || m.Consumed) continue;
                bool devil = Random.value < devilRatio;
                MedalMarks.Set(m, devil ? DevilKind : HappyKind, devil ? DevilGlow : Angel, devil ? 2f : 1.5f);
            }
            flash = 1f;
            Play(dealSound, ref madeDeal, () => RecordSynth.Chord("DealDeal", new[] { RecordSynth.Note(-12), RecordSynth.Note(-6), RecordSynth.Note(0) }, 1.4f, 0.35f, RecordSynth.Wave.Saw));
        }

        protected override void OnEnd()
        {
            // 曲が終わると元に戻る
            MedalMarks.ClearKind(HappyKind);
            MedalMarks.ClearKind(DevilKind);
        }

        protected override void Tick(float dt, bool running)
        {
            flash = Mathf.MoveTowards(flash, 0f, dt);
            int h = MedalMarks.Count(HappyKind), d = MedalMarks.Count(DevilKind);
            float target = (d - h) / (float)Mathf.Max(1, h + d) * 20f;
            tilt = Mathf.Lerp(tilt, target, 1f - Mathf.Exp(-4f * dt));
            if (flash > 0.5f && Random.value < 0.5f) Emit(feathers, RandomFloorPoint(), Random.value < devilRatio ? DevilGlow : Angel, 3);
        }

        protected override bool KeepBoardVisible() => feathers != null && feathers.particleCount > 0;

        protected override void BuildScreen()
        {
            float W = BodyW;
            var panel = Panel(Screen, "Scale", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x14, 0x10, 0x1a, 0xff));
            float pw = W - 48f;
            Img(panel, "Post", pw * 0.5f, 230f, 14f, 200f, Gold);
            Img(panel, "Base", pw * 0.5f, 330f, 160f, 20f, Gold, MonitorDisplay.ScreenShape.Pill);
            var b = Grp(panel, "Beam", pw * 0.5f, 130f, pw * 0.7f, 200f);
            Img(b, "Bar", pw * 0.35f, 20f, pw * 0.7f, 10f, Gold);
            happyBox = Img(b, "Happy", 40f, 90f, 160f, 110f, Angel, MonitorDisplay.ScreenShape.Pill);
            devilBox = Img(b, "Devil", pw * 0.7f - 40f, 90f, 160f, 110f, Devil, MonitorDisplay.ScreenShape.Pill);
            happyText = Lbl(b, "HappyText", 40f, 90f, 160f, 110f, "", 22, new Color32(0x30, 0x28, 0x10, 0xff));
            devilText = Lbl(b, "DevilText", pw * 0.7f - 40f, 90f, 160f, 110f, "", 22, Ink);
            beam = b;
            ratioText = Lbl(panel, "Ratio", pw * 0.5f, 30f, pw, 40f, "", 24, Ink);
            ledgerText = Lbl(Screen, "Ledger", 24f, 440f, W - 48f, 40f, "", 22, Ink, TextAnchor.MiddleLeft);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            beam.localRotation = Quaternion.Euler(0f, 0f, -tilt);
            int h = MedalMarks.Count(HappyKind), d = MedalMarks.Count(DevilKind);
            happyText.text = $"幸福\n{h}枚";
            devilText.text = $"悪魔\n{d}枚";
            ratioText.text = $"悪魔の割合 {Mathf.RoundToInt(devilRatio * 100f)}%";
            ledgerText.text = $"幸福 <color=#FFD23F>+{gained}</color>　悪魔(中央) <color=#C82A8A>-{lost}</color>　悪魔(端) <color=#C82A8A>+{devilGained}</color>";
            statusText.text = $"幸福メダルは中央の穴で +{happyBonus}。悪魔メダルは端の穴で +{devilSideBonus}、中央だと -{devilPenalty}";
        }
    }
}
