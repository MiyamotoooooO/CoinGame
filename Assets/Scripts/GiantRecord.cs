using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N033 巨大化「ジャイアント・コイン」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：巨大メダル（大きさ ×giantScale、重さ ×giantMass、オレンジの輪）がゴロンと落ちて、周りのメダルを押しのける。着地で土ぼこり
    /// ・モニター：工事現場の看板風。残りの巨大メダル（大きなコインの絵）、盤面の巨大メダル、押しのけた枚数
    /// ・ルール：
    ///   発動した瞬間から、次に投入する giantCount 枚のメダルが巨大メダルになる（CSV のとおり）
    ///   巨大メダルが中央の穴に落ちると +giantBonus pt、ゲージ +giantGauge
    ///   巨大メダルを投入した側の発射口は、giantCount 枚使い切るまで左右どちらでもよい。重いので、山を崩す向きを考えて入れる
    /// ・巨大メダルで山を押し崩す場所をえらぶ遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class GiantRecord : RecordScriptBase
    {
        public static GiantRecord Instance { get; private set; }

        public const string GiantKind = "Giant";

        [Header("巨大メダル")]
        [Range(1, 20)] public int giantCount = 5;
        [Range(1f, 4f)] public float giantScale = 2f;
        [Range(1f, 20f)] public float giantMass = 4f;
        [Range(0, 300)] public int giantBonus = 20;
        [Range(0f, 50f)] public float giantGauge = 5f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip growSound;
        public AudioClip landSound;
        public AudioClip goalSound;

        protected override string DefaultKey => "Giant";
        protected override string ScreenTitle => "GIANT";
        protected override string ScreenSubtitle => "ジャイアント・コイン";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<GiantRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        public static readonly Color GiantColor = new Color32(0xff, 0x8a, 0x1a, 0xff);
        static readonly Color Caution = new Color32(0xff, 0xd2, 0x1a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Dark = new Color32(0x1a, 0x1a, 0x1a, 0xff);

        int left, goals;
        readonly Dictionary<Medal, bool> landed = new Dictionary<Medal, bool>();
        readonly List<Medal> scratch = new List<Medal>();
        ParticleSystem dust;

        readonly List<Image> coins = new List<Image>();
        Text onBoardText, goalText, statusText;
        AudioClip madeGrow, madeLand, madeGoal;

        protected override void BuildBoard()
        {
            dust = Particles("Dust", Board, Vector3.up, D * 2f, D * 0.25f, 1f, 0.1f, 80f);
            MedalMarks.RegisterEffect(GiantKind, (m, type) =>
            {
                landed.Remove(m);
                if (type != DropZoneType.Center || effects == null) return;
                goals++;
                effects.AddPoints(giantBonus);
                effects.AddGauge(giantGauge);
                Play(goalSound, ref madeGoal, () => RecordSynth.Thud("GiantGoal", 70f, 0.8f, 1f));
                Pop($"巨大メダル +{giantBonus}", GiantColor, new Vector2(BodyW * 0.5f, 420f));
                Speaker(0.7f, 6, GiantColor);
            });
        }

        protected override void OnBegin()
        {
            left = giantCount;
            goals = 0;
        }

        protected override void OnInsert(Medal medal)
        {
            if (left <= 0) return;
            left--;
            MedalMarks.Set(medal, GiantKind, GiantColor, 2f);
            MedalMarks.SetSize(medal, giantScale, giantMass);
            landed[medal] = false;
            Play(growSound, ref madeGrow, () => RecordSynth.Sweep("GiantGrow", 200f, 80f, 0.5f, 0.35f, RecordSynth.Wave.Square));
        }

        protected override void Tick(float dt, bool running)
        {
            // 着地（床の高さまで下りてきた）で土ぼこりと音
            MedalMarks.Collect(GiantKind, scratch);
            foreach (var m in scratch)
            {
                if (!landed.TryGetValue(m, out bool done) || done) continue;
                if (m.transform.position.y > Layout.pusherHeight + D * 0.5f || (m.Body != null && m.Body.velocity.y < -0.2f)) continue;
                landed[m] = true;
                Emit(dust, m.transform.position, new Color(0.8f, 0.7f, 0.55f), 25);
                Play(landSound, ref madeLand, () => RecordSynth.Thud("GiantLand", 45f, 0.6f, 0.9f));
                Shake(0.03f, 0.25f);
            }
        }

        protected override bool KeepBoardVisible() => dust != null && dust.particleCount > 0;

        protected override void BuildScreen()
        {
            coins.Clear();
            float W = BodyW;
            var sign = Panel(Screen, "Sign", W * 0.5f, 200f, W - 48f, 360f, Dark);
            float sw = W - 48f;
            for (int i = 0; i < 14; i++)
            {
                var s = Img(sign, $"Stripe{i}", i * sw / 13f, 20f, sw / 26f, 70f, Caution);
                s.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            }
            Lbl(sign, "Title", sw * 0.5f, 90f, sw, 50f, "巨大メダル 投入注意", 30, Caution);
            float size = Mathf.Min(110f, (sw - 60f) / Mathf.Max(1, giantCount) - 14f);
            for (int i = 0; i < giantCount; i++)
            {
                var c = Img(sign, $"Coin{i}", 30f + (size + 14f) * (i + 0.5f), 210f, size, size, GiantColor, MonitorDisplay.ScreenShape.Circle);
                coins.Add(c);
            }
            onBoardText = Lbl(sign, "OnBoard", sw * 0.5f, 320f, sw, 40f, "", 22, Ink);
            goalText = Lbl(Screen, "Goal", 24f, 440f, 500f, 40f, "", 26, GiantColor, TextAnchor.MiddleLeft);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int i = 0; i < coins.Count; i++)
            {
                bool remain = i < left;
                coins[i].color = remain ? GiantColor : new Color(1f, 1f, 1f, 0.1f);
                Scale(coins[i].rectTransform, remain && i == left - 1 ? 1f + 0.08f * Mathf.Sin(Time.time * 6f) : 1f);
            }
            onBoardText.text = $"盤面の巨大メダル {MedalMarks.Count(GiantKind)}枚";
            goalText.text = $"中央に落とした巨大メダル {goals}枚";
            statusText.text = left > 0
                ? $"次に投入する {left}枚が巨大メダル。重いので、山を押し崩したい側の発射口から入れよう"
                : $"巨大メダルは中央の穴で +{giantBonus}pt・ゲージ +{giantGauge:0}";
        }
    }
}
