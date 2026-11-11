using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N022 リズム「ビート・ジャッジ」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：中央の穴のふちに光の帯。ビートに合わせて光る（光っている瞬間がねらい目）
    /// ・モニター：リズムゲームのレーン。ノーツが上から流れてきて、判定ラインに重なる瞬間がビート。判定（PERFECT / GOOD）とコンボ
    /// ・ルール：
    ///   ビート（bpm）の瞬間から perfectWindow 秒以内に中央の穴に落ちると PERFECT：+perfectBonus pt。goodWindow 秒以内なら GOOD：+goodBonus pt
    ///   PERFECT が feverCount 回続くと FEVER：feverSeconds 秒のあいだ、どのタイミングでも PERFECT
    ///   ビートの速さは曲に合わせて Inspector の bpm で変える（ビートの始まりは発動した時）
    /// ・ビートに合わせて投入して、落ちるタイミングをねらう遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class RhythmRecord : RecordScriptBase
    {
        public static RhythmRecord Instance { get; private set; }

        [Header("ビート")]
        [Range(40f, 240f)] public float bpm = 120f;
        [Tooltip("ビートの瞬間を、何秒ずらすか（曲と合わない時に調整）")]
        [Range(-1f, 1f)] public float beatOffset = 0f;

        [Header("判定")]
        [Range(0.02f, 0.5f)] public float perfectWindow = 0.12f;
        [Range(0.05f, 0.5f)] public float goodWindow = 0.25f;
        [Range(0, 200)] public int perfectBonus = 30;
        [Range(0, 100)] public int goodBonus = 10;

        [Header("FEVER")]
        [Range(1, 30)] public int feverCount = 5;
        [Range(1f, 30f)] public float feverSeconds = 8f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip perfectSound;
        public AudioClip goodSound;
        public AudioClip feverSound;

        protected override string DefaultKey => "Rhythm";
        protected override string ScreenTitle => "RHYTHM";
        protected override string ScreenSubtitle => "ビート・ジャッジ";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<RhythmRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Perfect = new Color32(0xff, 0xe0, 0x3c, 0xff);
        static readonly Color Good = new Color32(0x3c, 0xe0, 0xff, 0xff);
        static readonly Color Fever = new Color32(0xff, 0x4f, 0xd8, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int streak, perfects, goods;
        float feverTimer, judgePulse;
        string judgeText = "";
        Color judgeColor = Color.white;
        bool Fever2 => feverTimer > 0f;

        float BeatSeconds => 60f / Mathf.Max(1f, bpm);
        float BeatPos => (Elapsed - beatOffset) / BeatSeconds;
        /// <summary>いちばん近いビートとの時間の差（秒）</summary>
        float BeatError
        {
            get
            {
                float p = BeatPos;
                float frac = p - Mathf.Floor(p);
                return Mathf.Min(frac, 1f - frac) * BeatSeconds;
            }
        }

        GameObject rim;
        Material rimMaterial;
        ParticleSystem sparkle;

        RectTransform lane;
        readonly List<RectTransform> notes = new List<RectTransform>();
        Text judge, comboText, statusText, countText;
        Image lineImg;
        const float LaneH = 330f;
        const float JudgeY = 280f;
        AudioClip madePerfect, madeGood, madeFever;

        protected override void BuildBoard()
        {
            rimMaterial = Glow(Perfect, 0f);
            rim = Cube("BeatRim", Board, new Vector3(0f, D * 0.03f, FrontZ - D * 0.02f), new Vector3(HoleHalf * 2f, D * 0.05f, D * 0.06f), rimMaterial);
            sparkle = Particles("Sparkle", Board, Vector3.up, D * 3f, D * 0.1f, 0.7f, 0.5f, 40f);
        }

        protected override void OnBegin()
        {
            streak = 0;
            perfects = 0;
            goods = 0;
            feverTimer = 0f;
            judgeText = "";
        }

        protected override void Tick(float dt, bool running)
        {
            judgePulse = Mathf.MoveTowards(judgePulse, 0f, dt * 2.5f);
            if (feverTimer > 0f) feverTimer -= dt;
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            float err = BeatError;
            Vector3 p = new Vector3(medal.transform.position.x, D * 0.1f, FrontZ);
            if (Fever2 || err <= perfectWindow)
            {
                perfects++;
                streak++;
                AddPoints(perfectBonus);
                Show("PERFECT", Fever2 ? Fever : Perfect);
                Emit(sparkle, p, Fever2 ? Fever : Perfect, 16);
                Play(perfectSound, ref madePerfect, () => RecordSynth.Notes("RhythmPerfect", new[] { 1760f, 2637f }, 0.04f, 0.15f, 0.25f, RecordSynth.Wave.Square), 0.7f);
                Pop($"+{perfectBonus}", Perfect, new Vector2(BodyW * 0.5f, JudgeY + 30f));
                if (!Fever2 && streak >= feverCount)
                {
                    streak = 0;
                    feverTimer = feverSeconds;
                    Play(feverSound, ref madeFever, () => RecordSynth.Sweep("RhythmFever", 220f, 1760f, 0.8f, 0.3f, RecordSynth.Wave.Square));
                    PopCenter("FEVER!", Fever);
                    Speaker(1f, 16, Fever);
                }
            }
            else if (err <= goodWindow)
            {
                goods++;
                streak = 0;
                AddPoints(goodBonus);
                Show("GOOD", Good);
                Play(goodSound, ref madeGood, () => RecordSynth.Notes("RhythmGood", new[] { 1320f }, 0.04f, 0.1f, 0.2f, RecordSynth.Wave.Square), 0.6f);
            }
            else
            {
                streak = 0;
                Show("MISS", Mute);
            }
        }

        void Show(string text, Color color)
        {
            judgeText = text;
            judgeColor = color;
            judgePulse = 1f;
        }

        protected override bool KeepBoardVisible() => sparkle != null && sparkle.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            rim.SetActive(BoardShown > 0.01f);
            float p = BeatPos;
            float frac = p - Mathf.Floor(p);
            float flash = Mathf.Clamp01(1f - Mathf.Min(frac, 1f - frac) * BeatSeconds / Mathf.Max(0.01f, goodWindow));
            SetGlow(rimMaterial, Fever2 ? Fever : Perfect, (Fever2 ? 3f : 0.2f + 4f * flash * flash) * BoardShown);
        }

        protected override void BuildScreen()
        {
            notes.Clear();
            float W = BodyW;
            lane = Panel(Screen, "Lane", W * 0.5f, 30f + LaneH * 0.5f, 360f, LaneH, new Color32(0x10, 0x10, 0x1c, 0xff));
            Img(lane, "Side1", 4f, LaneH * 0.5f, 4f, LaneH, new Color(1f, 1f, 1f, 0.2f));
            Img(lane, "Side2", 356f, LaneH * 0.5f, 4f, LaneH, new Color(1f, 1f, 1f, 0.2f));
            lineImg = Img(lane, "Line", 180f, JudgeY, 360f, 8f, Perfect);
            for (int i = 0; i < 6; i++) notes.Add(Img(lane, $"Note{i}", 180f, 0f, 220f, 20f, Good, MonitorDisplay.ScreenShape.Pill).rectTransform);
            judge = Lbl(Screen, "Judge", W * 0.82f, 180f, W * 0.3f, 80f, "", 48, Perfect, TextAnchor.MiddleCenter, true);
            comboText = Lbl(Screen, "Combo", W * 0.18f, 160f, W * 0.3f, 80f, "", 56, Ink, TextAnchor.MiddleCenter, true);
            Lbl(Screen, "ComboLabel", W * 0.18f, 210f, W * 0.3f, 30f, "PERFECT 連続", 18, Mute);
            countText = Lbl(Screen, "Count", 24f, 430f, W - 48f, 40f, "", 22, Ink, TextAnchor.MiddleLeft);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float p = BeatPos;
            float pxPerBeat = 90f;
            for (int i = 0; i < notes.Count; i++)
            {
                float beatIndex = Mathf.Floor(p) + i;
                float y = JudgeY - (beatIndex - p) * pxPerBeat;
                Place(notes[i], 180f, y);
                var img = notes[i].GetComponent<Image>();
                img.color = Fever2 ? Fever : (Mathf.RoundToInt(beatIndex) % 4 == 0 ? Perfect : Good);
            }
            float frac = p - Mathf.Floor(p);
            float flash = 1f - Mathf.Clamp01(Mathf.Min(frac, 1f - frac) * 4f);
            lineImg.color = Color.Lerp(new Color(1f, 1f, 1f, 0.4f), Fever2 ? Fever : Perfect, flash);
            judge.text = judgeText;
            judge.color = judgeColor;
            Scale(judge.transform, 1f + 0.3f * judgePulse);
            comboText.text = Fever2 ? "FEVER" : $"{streak}";
            comboText.color = Fever2 ? Fever : Ink;
            countText.text = $"PERFECT {perfects}  /  GOOD {goods}    {bpm:0} BPM";
            statusText.text = Fever2
                ? $"<color=#FF4FD8>FEVER!</color>  どのタイミングでも PERFECT（残り {Mathf.CeilToInt(feverTimer)}秒）"
                : $"ノーツが線に重なる瞬間に中央の穴に落ちると PERFECT +{perfectBonus}pt。{feverCount}回続けて FEVER";
        }
    }
}
