using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N021 応援団「フレー！フレー！」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：中央の穴の左右に、応援団の旗（赤・白）が立つ。次に落とすとよい側の旗が大きく振られる。10コンボごとに紙吹雪
    /// ・モニター：学ラン姿の応援団が、コンボ数を叫ぶ。大きなコンボ数と今の倍率、コンボが切れるまでのバー
    /// ・ルール：
    ///   comboWindow 秒以内に続けて中央の穴に落ちるとコンボ。中央の穴のポイントに ×(1 + コンボ数 × perCombo) が掛かる（最大 ×maxMultiplier）
    ///   前と反対の半分（左→右→左…）に落ちると「フレー！」：コンボが +2（同じ側なら +1）
    ///   コンボが切れると 0 に戻る。10コンボごとに紙吹雪とゲージ +comboGauge
    /// ・左右の発射口を交互に使って、コンボをつなげる遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class CheerRecord : RecordScriptBase
    {
        public static CheerRecord Instance { get; private set; }

        [Header("コンボ")]
        [Tooltip("この秒数以内に次が落ちるとコンボ")]
        [Range(0.3f, 5f)] public float comboWindow = 1.2f;
        [Tooltip("コンボ1つで足す倍率（CSV の ×0.1）")]
        [Range(0f, 1f)] public float perCombo = 0.1f;
        [Range(1f, 10f)] public float maxMultiplier = 4f;
        [Range(0f, 50f)] public float comboGauge = 5f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip shoutSound;
        public AudioClip confettiSound;

        protected override string DefaultKey => "Cheer";
        protected override string ScreenTitle => "CHEER";
        protected override string ScreenSubtitle => "フレー！フレー！";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<CheerRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Red = new Color32(0xe8, 0x2a, 0x3a, 0xff);
        static readonly Color White = new Color32(0xf8, 0xf8, 0xf8, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color Gakuran = new Color32(0x16, 0x16, 0x22, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int combo, best, lastSide;
        float timer, shout;
        float Multiplier => Mathf.Min(maxMultiplier, 1f + combo * perCombo);

        readonly Transform[] flags = new Transform[2];
        ParticleSystem confetti;

        readonly List<RectTransform> members = new List<RectTransform>();
        Text comboText, multText, shoutText, statusText, bestText;
        Image windowFill;
        AudioClip madeShout, madeConfetti;

        protected override void BuildBoard()
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                var holder = new GameObject(side < 0 ? "FlagLeft" : "FlagRight").transform;
                holder.SetParent(Board, false);
                holder.position = new Vector3(side * (HoleHalf + D * 0.3f), 0f, FrontZ + D * 0.2f);
                Cube("Pole", holder, new Vector3(0f, D * 0.8f, 0f), new Vector3(D * 0.05f, D * 1.6f, D * 0.05f), Glow(new Color(0.7f, 0.6f, 0.4f), 0.2f));
                var cloth = Cube("Cloth", holder, new Vector3(-side * D * 0.35f, D * 1.35f, 0f), new Vector3(D * 0.7f, D * 0.45f, D * 0.02f), Glow(side < 0 ? Red : White, 0.6f));
                flags[i] = holder;
            }
            confetti = Particles("Confetti", Board, Vector3.up, D * 5f, D * 0.12f, 2f, 0.4f, 35f);
        }

        protected override void OnBegin()
        {
            combo = 0;
            best = 0;
            lastSide = 0;
            timer = 0f;
        }

        protected override void Tick(float dt, bool running)
        {
            shout = Mathf.MoveTowards(shout, 0f, dt * 2f);
            if (!running || combo <= 0) return;
            timer -= dt;
            if (timer <= 0f)
            {
                if (combo >= 3) Pop($"{combo} コンボ おわり", Mute, new Vector2(BodyW * 0.5f, 420f));
                combo = 0;
                lastSide = 0;
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            int side = medal.transform.position.x < 0f ? -1 : 1;
            bool alternate = lastSide != 0 && side != lastSide;
            int before = combo;
            combo += alternate ? 2 : 1;
            lastSide = side;
            timer = comboWindow;
            best = Mathf.Max(best, combo);
            shout = 1f;

            int extra = AddMultiplied(Multiplier);
            Play(shoutSound, ref madeShout, () => RecordSynth.Noise("CheerShout", 0.4f, 0.25f, 0.02f, 0.15f, 0.9f, 15), 0.6f);
            Pop(alternate ? $"フレー！ +{CenterPoints + extra}" : $"+{CenterPoints + extra}", side < 0 ? Red : White, new Vector2(BodyW * (side < 0 ? 0.35f : 0.65f), 380f));
            if (before / 10 != combo / 10)
            {
                AddGauge(comboGauge);
                Emit(confetti, new Vector3(-HalfW * 0.8f, D * 0.3f, FrontZ + D), Red, 50);
                Emit(confetti, new Vector3(HalfW * 0.8f, D * 0.3f, FrontZ + D), Gold, 50);
                Play(confettiSound, ref madeConfetti, () => RecordSynth.Notes("CheerConfetti", new[] { RecordSynth.Note(7), RecordSynth.Note(12), RecordSynth.Note(16), RecordSynth.Note(19) }, 0.06f, 0.4f, 0.3f, RecordSynth.Wave.Square));
                PopCenter($"{combo / 10 * 10} コンボ！", Gold);
                Speaker(0.9f, 12, Red);
            }
        }

        protected override bool KeepBoardVisible() => confetti != null && confetti.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            int nextSide = lastSide == 0 ? 0 : -lastSide;
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                bool wave = Active && (nextSide == 0 || nextSide == side);
                float a = wave ? Mathf.Sin(Time.time * 10f + i) * 30f : Mathf.Sin(Time.time * 2f + i) * 5f;
                flags[i].localRotation = Quaternion.Euler(0f, 0f, a);
                flags[i].localScale = new Vector3(1f, Mathf.Max(0.01f, BoardShown), 1f);
            }
        }

        protected override void BuildScreen()
        {
            members.Clear();
            float W = BodyW;
            var stand = Panel(Screen, "Stand", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x1e, 0x14, 0x14, 0xff));
            float sw = W - 48f;
            Img(stand, "Banner", sw * 0.5f, 34f, sw * 0.8f, 46f, Red);
            Lbl(stand, "BannerText", sw * 0.5f, 34f, sw * 0.8f, 46f, "必勝 ネオン通り応援団", 26, White);
            for (int i = 0; i < 5; i++)
            {
                var m = Grp(stand, $"Member{i}", sw * (0.12f + i * 0.19f), 260f, 100f, 180f);
                Img(m, "Body", 50f, 110f, 70f, 110f, Gakuran, MonitorDisplay.ScreenShape.Pill);
                Img(m, "Head", 50f, 36f, 52f, 52f, new Color32(0xff, 0xe0, 0xc8, 0xff), MonitorDisplay.ScreenShape.Circle);
                Img(m, "Band", 50f, 24f, 56f, 10f, i % 2 == 0 ? Red : White);
                Img(m, "ArmL", 12f, 80f, 14f, 60f, Gakuran, MonitorDisplay.ScreenShape.Pill);
                Img(m, "ArmR", 88f, 80f, 14f, 60f, Gakuran, MonitorDisplay.ScreenShape.Pill);
                members.Add(m);
            }
            comboText = Lbl(stand, "Combo", sw * 0.5f, 140f, sw * 0.6f, 140f, "", 120, Gold, TextAnchor.MiddleCenter, true);
            shoutText = Lbl(stand, "Shout", sw * 0.5f, 86f, sw, 40f, "", 26, White);
            multText = Lbl(Screen, "Mult", 24f, 430f, 400f, 50f, "", 34, Gold, TextAnchor.MiddleLeft, true);
            bestText = Lbl(Screen, "Best", W - 24f, 430f, 300f, 40f, "", 22, Ink, TextAnchor.MiddleRight);
            windowFill = Bar(Screen, "Window", 24f, 480f, W - 48f, 12f, Red);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float sw = BodyW - 48f;
            for (int i = 0; i < members.Count; i++)
            {
                float jump = combo > 0 ? Mathf.Abs(Mathf.Sin(Time.time * 6f + i * 0.8f)) * (10f + Mathf.Min(30f, combo)) : 0f;
                Place(members[i], sw * (0.12f + i * 0.19f), 260f - jump - shout * 10f);
            }
            comboText.text = combo > 0 ? $"{combo}" : "";
            Scale(comboText.transform, 1f + 0.2f * shout);
            shoutText.text = combo > 0 ? (combo >= 10 ? "フレー！フレー！ ネオン通り！" : "それっ！") : "";
            multText.text = $"×{Multiplier:0.0}";
            bestText.text = $"最高 {best} コンボ";
            windowFill.fillAmount = combo > 0 ? timer / Mathf.Max(0.1f, comboWindow) : 0f;
            statusText.text = lastSide != 0 && combo > 0
                ? $"次は{(lastSide < 0 ? "右" : "左")}の半分に落とすと「フレー！」（コンボ +2）"
                : $"{comboWindow:0.#}秒以内に続けて中央の穴に落とすとコンボ。左右交互で +2";
        }
    }
}
