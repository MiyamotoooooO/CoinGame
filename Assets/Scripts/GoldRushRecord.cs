using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N028 ゴールドラッシュ「金の鉱脈」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：金メダル（金色に光る輪）。落ちるとコインが噴き出す
    /// ・モニター：金鉱。トロッコのメーター（金メダルまでの残り投入数）、盤面の金メダル数、掘り当てた数
    /// ・ルール：
    ///   投入したメダルの goldEvery 枚に1枚が金メダルになる（CSV のとおり）
    ///   金メダルが中央の穴に落ちると、盤面にメダルが goldMedals 枚降ってくる（CSV の「手持ちに3枚戻る」の代わり）、+goldBonus pt
    ///   「鉱脈」：金メダルが中央の穴に続けて veinCount 枚落ちると、さらにメダルが veinMedals 枚降ってくる（端の穴に落ちると途切れる）
    /// ・金メダルを端の穴に落とさないよう、投入する側を考える遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class GoldRushRecord : RecordScriptBase
    {
        public static GoldRushRecord Instance { get; private set; }

        public const string GoldKind = "Gold";

        [Header("金メダル")]
        [Range(1, 20)] public int goldEvery = 5;
        [Range(0, 20)] public int goldMedals = 3;
        [Range(0, 200)] public int goldBonus = 10;

        [Header("鉱脈")]
        [Range(2, 20)] public int veinCount = 3;
        [Range(0, 30)] public int veinMedals = 5;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip goldSound;
        public AudioClip veinSound;

        protected override string DefaultKey => "GoldRush";
        protected override string ScreenTitle => "GOLD RUSH";
        protected override string ScreenSubtitle => "金の鉱脈";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<GoldRushRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        public static readonly Color GoldColor = new Color32(0xff, 0xc8, 0x2a, 0xff);
        static readonly Color Rock = new Color32(0x3a, 0x2a, 0x22, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int inserted, struck, streak;
        ParticleSystem coins;

        readonly List<Image> carts = new List<Image>();
        readonly List<Image> veinPips = new List<Image>();
        Text goldText, struckText, statusText;
        AudioClip madeGold, madeVein;

        protected override void BuildBoard()
        {
            coins = Particles("Coins", Board, Vector3.up, D * 5f, D * 0.14f, 1.2f, 1.2f, 35f);
            MedalMarks.RegisterEffect(GoldKind, (m, type) =>
            {
                if (effects == null) return;
                if (type != DropZoneType.Center)
                {
                    streak = 0;
                    return;
                }
                struck++;
                streak++;
                effects.AddPoints(goldBonus);
                RainMedals(goldMedals);
                Emit(coins, new Vector3(m.transform.position.x, D * 0.2f, FrontZ), GoldColor, 40);
                Play(goldSound, ref madeGold, () => RecordSynth.Notes("GoldDrop", new[] { 2093f, 2637f, 3136f, 4186f }, 0.04f, 0.4f, 0.22f, RecordSynth.Wave.Bell));
                Pop($"金！ メダル +{goldMedals}", GoldColor, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 300f));
                if (streak >= veinCount)
                {
                    streak = 0;
                    RainMedals(veinMedals);
                    Play(veinSound, ref madeVein, () => RecordSynth.Chord("GoldVein", new[] { RecordSynth.Note(0), RecordSynth.Note(4), RecordSynth.Note(7), RecordSynth.Note(12) }, 1.2f, 0.35f, RecordSynth.Wave.Square));
                    PopCenter($"鉱脈を掘り当てた！ メダル +{veinMedals}", GoldColor);
                    Speaker(1f, 16, GoldColor);
                }
            });
        }

        protected override void OnBegin()
        {
            inserted = 0;
            struck = 0;
            streak = 0;
        }

        protected override void OnInsert(Medal medal)
        {
            inserted++;
            if (inserted % Mathf.Max(1, goldEvery) != 0) return;
            MedalMarks.Set(medal, GoldKind, GoldColor, 3f);
        }

        protected override bool KeepBoardVisible() => coins != null && coins.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            if (!Active || Random.value > 0.3f) return;
            var list = MedalMarks.Collect(GoldKind, new List<Medal>());
            if (list.Count == 0) return;
            var m = list[Random.Range(0, list.Count)];
            Emit(coins, m.transform.position + Vector3.up * D * 0.1f, new Color(1f, 0.95f, 0.6f), 1);
        }

        protected override void BuildScreen()
        {
            carts.Clear();
            veinPips.Clear();
            float W = BodyW;
            var mine = Panel(Screen, "Mine", W * 0.5f, 200f, W - 48f, 360f, Rock);
            float mw = W - 48f;
            for (int i = 0; i < 12; i++)
                Img(mine, $"Nugget{i}", Random.Range(20f, mw - 20f), Random.Range(20f, 240f), Random.Range(10f, 26f), Random.Range(8f, 20f), new Color(GoldColor.r, GoldColor.g, GoldColor.b, Random.Range(0.3f, 0.8f)), MonitorDisplay.ScreenShape.Circle);
            Img(mine, "Rail", mw * 0.5f, 300f, mw, 6f, Mute);
            float cw = Mathf.Min(90f, (mw - 40f) / Mathf.Max(1, goldEvery) - 10f);
            for (int i = 0; i < goldEvery; i++)
                carts.Add(Img(mine, $"Cart{i}", 20f + (cw + 10f) * (i + 0.5f), 270f, cw, 50f, new Color(1f, 1f, 1f, 0.1f)));
            Lbl(mine, "CartLabel", 20f, 220f, 400f, 30f, "次の金メダルまで", 18, Ink, TextAnchor.MiddleLeft);
            goldText = Lbl(mine, "Gold", mw - 20f, 90f, 420f, 70f, "", 44, GoldColor, TextAnchor.MiddleRight);
            struckText = Lbl(mine, "Struck", mw - 20f, 150f, 420f, 30f, "", 20, Ink, TextAnchor.MiddleRight);
            Lbl(Screen, "VeinLabel", 24f, 440f, 140f, 30f, "鉱脈", 20, Ink, TextAnchor.MiddleLeft);
            for (int i = 0; i < veinCount; i++)
                veinPips.Add(Img(Screen, $"Vein{i}", 110f + i * 40f, 440f, 28f, 28f, new Color(1f, 1f, 1f, 0.12f), MonitorDisplay.ScreenShape.Circle));
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            int filled = inserted % Mathf.Max(1, goldEvery);
            for (int i = 0; i < carts.Count; i++) carts[i].color = i < filled ? new Color(0.6f, 0.5f, 0.4f) : i == goldEvery - 1 ? new Color(GoldColor.r, GoldColor.g, GoldColor.b, 0.35f) : new Color(1f, 1f, 1f, 0.1f);
            for (int i = 0; i < veinPips.Count; i++) veinPips[i].color = i < streak ? GoldColor : new Color(1f, 1f, 1f, 0.12f);
            goldText.text = $"金メダル {MedalMarks.Count(GoldKind)}枚";
            struckText.text = $"掘り当てた {struck}枚";
            statusText.text = $"{goldEvery}枚に1枚が金メダル。中央の穴でメダルが {goldMedals}枚降る。{veinCount}枚続けて鉱脈！";
        }
    }
}
