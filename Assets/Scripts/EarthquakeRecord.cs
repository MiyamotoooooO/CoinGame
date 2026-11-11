using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N039 地震「緊急地震速報」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：揺れる時、盤面のメダルがガタガタ跳ね、画面ごと揺れる。揺れる向き（左・右）に矢印が光る
    /// ・モニター：ニュース速報。「緊急地震速報」のテロップ、揺れまでのカウントダウン、揺れる向き、震度7
    /// ・ルール：
    ///   発動した瞬間に強い揺れが来る（CSV の「強く3回揺らす」）。その後も quakeInterval 秒ごとに揺れる
    ///   1回の地震で jolts 回揺れる（joltGap 秒おき）。揺れはメダルを、知らせた向き（左か右）へ押し、少し跳ね上げる
    ///   揺れる warnSeconds 秒前に速報が出て、向きがわかる
    ///   揺れてから aftershockWindow 秒の間に中央の穴に落ちると「余震ボーナス」：1枚 +aftershockBonus pt
    /// ・速報を見て、揺れる向きの反対側にメダルを寄せておく遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class EarthquakeRecord : RecordScriptBase
    {
        public static EarthquakeRecord Instance { get; private set; }

        [Header("地震")]
        [Range(5f, 60f)] public float quakeInterval = 15f;
        [Range(1, 10)] public int jolts = 3;
        [Range(0.1f, 3f)] public float joltGap = 0.6f;
        [Range(0f, 20f)] public float joltStrength = 3f;
        [Tooltip("上に跳ね上げる強さ（横の強さに対する割合）")]
        [Range(0f, 2f)] public float joltUp = 0.5f;
        [Range(0f, 10f)] public float warnSeconds = 3f;

        [Header("余震ボーナス")]
        [Range(0f, 10f)] public float aftershockWindow = 3f;
        [Range(0, 100)] public int aftershockBonus = 10;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip alarmSound;
        public AudioClip quakeSound;

        protected override string DefaultKey => "Earthquake";
        protected override string ScreenTitle => "EARTHQUAKE";
        protected override string ScreenSubtitle => "緊急地震速報";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<EarthquakeRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Alert = new Color32(0xff, 0x2a, 0x2a, 0xff);
        static readonly Color Yellow = new Color32(0xff, 0xe0, 0x2a, 0xff);
        static readonly Color NewsBlue = new Color32(0x0e, 0x22, 0x5a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        float nextQuake, joltTimer, aftershock, quakeFlash;
        int joltsLeft, direction = 1, quakes, bonusCount;
        bool Warning => joltsLeft == 0 && nextQuake <= warnSeconds;

        readonly GameObject[] arrows = new GameObject[2];
        readonly Material[] arrowMaterials = new Material[2];
        ParticleSystem dust;

        Text alertText, countText, dirText, statusText, scaleText;
        Image ticker;
        AudioClip madeAlarm, madeQuake;

        protected override void BuildBoard()
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                arrowMaterials[i] = Glow(Alert, 0f);
                arrows[i] = Cube(side < 0 ? "ArrowLeft" : "ArrowRight", Board, new Vector3(side * HalfW * 0.7f, D * 0.02f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f)), new Vector3(D * 1.2f, D * 0.02f, D * 0.3f), arrowMaterials[i]);
            }
            dust = Particles("Dust", Board, Vector3.up, D * 1.5f, D * 0.25f, 1f, 0.05f, 80f);
        }

        protected override void OnBegin()
        {
            quakes = 0;
            bonusCount = 0;
            direction = Random.value < 0.5f ? -1 : 1;
            StartQuake();   // 発動した瞬間に揺れる
        }

        void StartQuake()
        {
            joltsLeft = jolts;
            joltTimer = 0f;
            nextQuake = quakeInterval;
            quakes++;
            Play(quakeSound, ref madeQuake, () => RecordSynth.Noise("QuakeRumble", 2.2f, 0.02f, 0.2f, 1.2f, 1.6f, 29));
        }

        protected override void Tick(float dt, bool running)
        {
            quakeFlash = Mathf.MoveTowards(quakeFlash, 0f, dt * 2f);
            if (aftershock > 0f) aftershock -= dt;
            if (!running) return;
            if (joltsLeft > 0)
            {
                joltTimer -= dt;
                if (joltTimer <= 0f)
                {
                    joltTimer = joltGap;
                    joltsLeft--;
                    Jolt();
                    if (joltsLeft == 0) direction = Random.value < 0.5f ? -1 : 1;   // 次の地震の向き
                }
                return;
            }
            float before = nextQuake;
            nextQuake -= dt;
            if (before > warnSeconds && nextQuake <= warnSeconds)
                Play(alarmSound, ref madeAlarm, () => RecordSynth.Notes("QuakeAlarm", new[] { 988f, 1319f, 988f, 1319f, 988f, 1319f }, 0.18f, 0.3f, 0.25f, RecordSynth.Wave.Square));
            if (nextQuake <= 0f) StartQuake();
        }

        void Jolt()
        {
            quakeFlash = 1f;
            aftershock = aftershockWindow;
            foreach (var m in BoardMedals)
            {
                if (m == null || m.Consumed) continue;
                Push(m, new Vector3(direction, joltUp, Random.Range(-0.4f, 0.1f)) * joltStrength * Random.Range(0.6f, 1.2f));
            }
            for (int i = 0; i < 6; i++) Emit(dust, RandomFloorPoint(), new Color(0.7f, 0.6f, 0.5f), 4);
            Shake(0.08f, 0.5f);
            Speaker(1f);
            PopCenter("ゆれ！", Alert);
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center || aftershock <= 0f) return;
            bonusCount++;
            AddPoints(aftershockBonus);
            Pop($"余震 +{aftershockBonus}", Yellow, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 420f));
        }

        protected override bool KeepBoardVisible() => dust != null && dust.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                bool on = Active && side == direction && (Warning || joltsLeft > 0);
                arrows[i].SetActive(BoardShown > 0.01f);
                SetGlow(arrowMaterials[i], Alert, on ? 1.5f + 2f * Mathf.Repeat(Time.time * 4f, 1f) : 0f);
            }
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var news = Panel(Screen, "News", W * 0.5f, 200f, W - 48f, 360f, NewsBlue);
            float nw = W - 48f;
            ticker = Img(news, "Alert", nw * 0.5f, 40f, nw, 70f, Alert);
            alertText = Lbl(news, "AlertText", nw * 0.5f, 40f, nw, 70f, "", 34, Ink);
            scaleText = Lbl(news, "Scale", nw * 0.28f, 200f, nw * 0.4f, 160f, "", 90, Yellow);
            countText = Lbl(news, "Count", nw * 0.72f, 170f, nw * 0.4f, 120f, "", 90, Ink, TextAnchor.MiddleCenter, true);
            dirText = Lbl(news, "Dir", nw * 0.72f, 280f, nw * 0.4f, 40f, "", 26, Yellow);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            bool shaking = joltsLeft > 0 || quakeFlash > 0f;
            ticker.color = Warning || shaking ? Color.Lerp(Alert, Yellow, Mathf.Repeat(Time.time * 3f, 1f) < 0.5f ? 0f : 0.4f) : new Color(0.3f, 0.3f, 0.45f);
            alertText.text = Warning || shaking ? "緊急地震速報　強い揺れに警戒" : "地震情報";
            scaleText.text = shaking ? "震度7" : "";
            countText.text = shaking ? "" : $"{Mathf.CeilToInt(nextQuake)}";
            countText.color = Warning ? Alert : Mute;
            dirText.text = Warning || shaking ? $"揺れる向き：{(direction < 0 ? "← 左" : "右 →")}" : "次の地震まで";
            statusText.text = aftershock > 0f
                ? $"<color=#FFE02A>余震ボーナス！</color>  いま中央の穴に落ちると +{aftershockBonus}pt（{bonusCount}枚）"
                : $"速報で揺れる向きがわかる。揺れたあと {aftershockWindow:0}秒は中央の穴で +{aftershockBonus}pt";
        }
    }
}
