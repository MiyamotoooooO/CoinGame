using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N038 月「満月のお月見」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：盤面の奥に大きな月が昇り、星がきらめく。満月の間は月が明るく光り、手前のメダルがふわっと浮かぶ
    /// ・モニター：夜空の月。月の満ち欠け（三日月 → 半月 → 満月）と、満月までの時間、お月見の枚数
    /// ・ルール：
    ///   発動中は重力が弱くなる（×gravity）。メダルがふわっと跳ねて、山が崩れやすい（CSV のとおり）
    ///   moonCycle 秒ごとに満月が fullSeconds 秒。満月の間はさらに重力が弱く（×fullGravity）、手前のメダルがふわっと浮く
    ///   満月の間に中央の穴に落ちると「お月見」：1枚 +moonBonus pt
    /// ・満月に合わせてメダルを入れる遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// 重力は、効果が終わると元に戻る。
    /// </summary>
    [DisallowMultipleComponent]
    public class MoonRecord : RecordScriptBase
    {
        public static MoonRecord Instance { get; private set; }

        [Header("重力")]
        [Range(0.05f, 1f)] public float gravity = 0.45f;
        [Range(0.02f, 1f)] public float fullGravity = 0.2f;

        [Header("満月")]
        [Range(3f, 60f)] public float moonCycle = 12f;
        [Range(1f, 20f)] public float fullSeconds = 4f;
        [Tooltip("満月の始まりに、手前のメダルを浮かせる強さ")]
        [Range(0f, 10f)] public float floatStrength = 1.2f;
        [Range(0, 100)] public int moonBonus = 15;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip fullMoonSound;
        public AudioClip moonDropSound;
        public GameObject moonPrefab;

        protected override string DefaultKey => "Moon";
        protected override string ScreenTitle => "MOON";
        protected override string ScreenSubtitle => "満月のお月見";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<MoonRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color MoonColor = new Color32(0xff, 0xf2, 0xc8, 0xff);
        static readonly Color NightSky = new Color32(0x08, 0x0c, 0x22, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        float cycleTimer, fullTimer;
        int viewed;
        bool Full => fullTimer > 0f;

        Transform moon;
        Material moonMaterial;
        Light moonLight;
        ParticleSystem stars;

        RectTransform shadow;
        Image moonImg;
        Text timerText, viewText, statusText;
        AudioClip madeFull, madeDrop;

        protected override void BuildBoard()
        {
            moon = new GameObject("Moon").transform;
            moon.SetParent(Board, false);
            if (moonPrefab != null) Instantiate(moonPrefab, moon).transform.localPosition = Vector3.zero;
            else
            {
                moonMaterial = Glow(MoonColor, 1.5f);
                Shape(PrimitiveType.Sphere, "Ball", moon, Vector3.zero, Vector3.one * D * 2.2f, moonMaterial);
            }
            moonLight = PointLight("MoonLight", moon, new Vector3(0f, 0f, -D * 2f), new Color(0.75f, 0.8f, 1f), Layout.boardWidth * 1.5f);
            stars = Particles("Stars", Board, Vector3.up, D * 0.2f, D * 0.08f, 2f, 0f, 90f);
        }

        protected override void OnBegin()
        {
            cycleTimer = moonCycle * 0.5f;
            fullTimer = 0f;
            viewed = 0;
        }

        protected override void Tick(float dt, bool running)
        {
            GravityScale = Full ? fullGravity : gravity;
            if (!running) return;
            if (Full)
            {
                fullTimer -= dt;
                return;
            }
            cycleTimer -= dt;
            if (cycleTimer <= 0f)
            {
                cycleTimer = moonCycle;
                fullTimer = fullSeconds;
                foreach (var m in BoardMedals)
                {
                    if (m == null || m.Consumed || m.transform.position.z > PusherFrontZ) continue;
                    Push(m, new Vector3(Random.Range(-0.2f, 0.2f), 1f, Random.Range(-0.3f, 0.1f)) * floatStrength * D);
                }
                Play(fullMoonSound, ref madeFull, () => RecordSynth.Chord("MoonFull", new[] { RecordSynth.Note(-5), RecordSynth.Note(2), RecordSynth.Note(7), RecordSynth.Note(14) }, 2f, 0.3f, RecordSynth.Wave.Sine));
                PopCenter("満月！", MoonColor);
                Speaker(0.6f, 8, MoonColor);
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center || !Full) return;
            viewed++;
            AddPoints(moonBonus);
            Play(moonDropSound, ref madeDrop, () => RecordSynth.Notes("MoonDrop", new[] { RecordSynth.Note(19), RecordSynth.Note(26) }, 0.1f, 0.6f, 0.2f, RecordSynth.Wave.Bell), 0.6f);
            Pop($"お月見 +{moonBonus}", MoonColor, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 420f));
        }

        protected override bool KeepBoardVisible() => stars != null && stars.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            float rise = BoardShown;
            moon.position = new Vector3(HalfW * 0.4f, Mathf.Lerp(-D * 2f, Layout.wallHeight * 0.9f, rise), BackZ + D * 0.5f);
            if (moonMaterial != null) SetGlow(moonMaterial, MoonColor, Full ? 3f : 1.2f);
            moonLight.intensity = BoardShown * (Full ? 2.5f : 0.8f);
            if (Active && Random.value < 0.3f)
                Emit(stars, new Vector3(Random.Range(-HalfW, HalfW), Layout.wallHeight * Random.Range(0.6f, 1.2f), BackZ), Color.white, 1);
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var sky = Panel(Screen, "Sky", W * 0.5f, 200f, W - 48f, 360f, NightSky);
            float sw = W - 48f;
            for (int i = 0; i < 40; i++)
                Img(sky, $"S{i}", Random.Range(0f, sw), Random.Range(0f, 300f), 3f, 3f, new Color(1f, 1f, 1f, Random.Range(0.2f, 0.9f)), MonitorDisplay.ScreenShape.Circle);
            Img(sky, "Hill", sw * 0.5f, 350f, sw * 1.2f, 120f, new Color32(0x10, 0x18, 0x14, 0xff), MonitorDisplay.ScreenShape.Circle);
            moonImg = Img(sky, "Moon", sw * 0.35f, 160f, 220f, 220f, MoonColor, MonitorDisplay.ScreenShape.Circle);
            shadow = Img(sky, "Shadow", sw * 0.35f, 160f, 220f, 220f, NightSky, MonitorDisplay.ScreenShape.Circle).rectTransform;
            for (int i = 0; i < 3; i++)
                Img(sky, $"Dango{i}", sw * 0.72f + (i - 1) * 40f, 290f - (i == 1 ? 30f : 0f), 40f, 40f, new Color32(0xf8, 0xf0, 0xe8, 0xff), MonitorDisplay.ScreenShape.Circle);
            timerText = Lbl(sky, "Timer", sw * 0.72f, 120f, sw * 0.45f, 60f, "", 30, MoonColor);
            viewText = Lbl(Screen, "View", 24f, 440f, 600f, 40f, "", 24, MoonColor, TextAnchor.MiddleLeft);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float sw = BodyW - 48f;
            // 満ち欠け：影を横にずらす（満月で影が消える）
            float phase = Full ? 1f : 1f - Mathf.Clamp01(cycleTimer / Mathf.Max(0.1f, moonCycle));
            Place(shadow, sw * 0.35f + Mathf.Lerp(-60f, -260f, phase), 160f);
            moonImg.color = Full ? Color.Lerp(MoonColor, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 4f)) : MoonColor;
            timerText.text = Full ? $"満月 {Mathf.CeilToInt(fullTimer)}" : $"満月まで {Mathf.CeilToInt(cycleTimer)}";
            viewText.text = $"お月見 {viewed}枚　重力 ×{(Full ? fullGravity : gravity):0.00}";
            statusText.text = Full
                ? $"<color=#FFF2C8>満月！</color>  中央の穴に落とすと +{moonBonus}pt"
                : "重力が弱く、メダルがふわっと跳ねる。満月に合わせてメダルを入れよう";
        }
    }
}
