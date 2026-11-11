using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N029 オルゴール「ちいさなメロディ」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：音符メダル（音ごとに色の違う輪）。中央の穴に落ちると、その音が鳴って音符の粒が舞う
    /// ・モニター：オルゴールの譜面。5つの音符（ド・レ・ミ・ソ・ラ）と、鳴らした順番、メロディの完成
    /// ・ルール：
    ///   発動した瞬間から、次に投入する notesCount 枚が音符メダルになる（1枚目がド、2枚目がレ…）（CSV のとおり）
    ///   音符メダルが中央の穴に落ちると、ゲージ +gaugePerNote、その音が鳴る
    ///   順番どおり（ド→レ→ミ→ソ→ラ）に落ちると、メロディが完成：+melodyBonus pt。順番が違うと完成しない
    ///   メロディが完成すると、また次の notesCount 枚が音符メダルになる
    /// ・投入するタイミングと側を考えて、順番どおりに落とす遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class MusicBoxRecord : RecordScriptBase
    {
        public static MusicBoxRecord Instance { get; private set; }

        public const string NoteKind = "Note";

        [Header("音符メダル")]
        [Range(1, 8)] public int notesCount = 5;
        [Range(0f, 50f)] public float gaugePerNote = 8f;
        [Range(0, 300)] public int melodyBonus = 50;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        [Tooltip("音符1つずつの音（ド・レ・ミ・ソ・ラ…の順）")]
        public AudioClip[] noteSounds = new AudioClip[0];
        public AudioClip melodySound;

        protected override string DefaultKey => "MusicBox";
        protected override string ScreenTitle => "MUSIC BOX";
        protected override string ScreenSubtitle => "ちいさなメロディ";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<MusicBoxRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly int[] NoteSteps = { 0, 2, 4, 7, 9, 12, 14, 16 };
        static readonly string[] NoteNames = { "ド", "レ", "ミ", "ソ", "ラ", "ド", "レ", "ミ" };
        static readonly Color[] NoteColors =
        {
            new Color32(0xff, 0x6a, 0x6a, 0xff), new Color32(0xff, 0xb0, 0x4a, 0xff), new Color32(0xff, 0xe6, 0x4a, 0xff), new Color32(0x6a, 0xe0, 0x8a, 0xff),
            new Color32(0x6a, 0xb8, 0xff, 0xff), new Color32(0xb8, 0x8a, 0xff, 0xff), new Color32(0xff, 0x8a, 0xd8, 0xff), new Color32(0xf8, 0xf8, 0xf8, 0xff),
        };
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Wood = new Color32(0x4a, 0x2c, 0x1a, 0xff);

        int toMark;              // あと何枚、音符メダルにするか
        int nextIndex;           // 次に付ける音
        int expected;            // 次に落ちてほしい音
        int melodies;
        readonly Dictionary<Medal, int> noteOf = new Dictionary<Medal, int>();
        readonly List<int> played = new List<int>();
        readonly AudioClip[] madeNotes = new AudioClip[8];
        AudioClip madeMelody;
        ParticleSystem notesFx;

        readonly List<Image> noteIcons = new List<Image>();
        readonly List<Text> noteLabels = new List<Text>();
        Text melodyText, statusText;

        int Count => Mathf.Clamp(notesCount, 1, 8);

        protected override void BuildBoard()
        {
            notesFx = Particles("Notes", Board, Vector3.up, D * 2f, D * 0.14f, 1.2f, -0.1f, 30f);
            MedalMarks.RegisterEffect(NoteKind, (m, type) =>
            {
                int idx = noteOf.TryGetValue(m, out int k) ? k : 0;
                noteOf.Remove(m);
                if (type != DropZoneType.Center || effects == null) return;
                effects.AddGauge(gaugePerNote);
                PlayNote(idx);
                Emit(notesFx, new Vector3(m.transform.position.x, D * 0.3f, FrontZ), NoteColors[idx], 12);
                Pop(NoteNames[idx], NoteColors[idx], new Vector2(BodyW * (0.15f + 0.7f * idx / Mathf.Max(1, Count - 1)), 140f));
                played.Add(idx);
                if (idx == expected)
                {
                    expected++;
                    if (expected >= Count)
                    {
                        melodies++;
                        effects.AddPoints(melodyBonus);
                        Play(melodySound, ref madeMelody, MakeMelody);
                        PopCenter($"メロディ完成！ +{melodyBonus}", NoteColors[2]);
                        Speaker(1f, 16, NoteColors[4]);
                        expected = 0;
                        played.Clear();
                        if (Active) Reload();
                    }
                }
                else
                {
                    expected = 0;
                    Pop("順番がちがう…", Mute, new Vector2(BodyW * 0.5f, 420f));
                }
            });
        }

        void PlayNote(int idx)
        {
            AudioClip custom = noteSounds != null && idx < noteSounds.Length ? noteSounds[idx] : null;
            int i = Mathf.Clamp(idx, 0, 7);
            float f = RecordSynth.Note(12 + NoteSteps[i]);
            Play(custom, ref madeNotes[i], () => RecordSynth.Notes("MusicBoxNote" + i, new[] { f }, 0.05f, 0.8f, 0.3f, RecordSynth.Wave.Bell));
        }

        AudioClip MakeMelody()
        {
            var freqs = new float[Count];
            for (int i = 0; i < Count; i++) freqs[i] = RecordSynth.Note(12 + NoteSteps[i]);
            return RecordSynth.Notes("MusicBoxMelody", freqs, 0.18f, 0.9f, 0.3f, RecordSynth.Wave.Bell);
        }

        void Reload()
        {
            toMark = Count;
            nextIndex = 0;
        }

        protected override void OnBegin()
        {
            Reload();
            expected = 0;
            melodies = 0;
            played.Clear();
        }

        protected override void OnInsert(Medal medal)
        {
            if (toMark <= 0) return;
            int idx = nextIndex;
            nextIndex++;
            toMark--;
            noteOf[medal] = idx;
            MedalMarks.Set(medal, NoteKind, NoteColors[idx], 2.5f);
        }

        protected override bool KeepBoardVisible() => notesFx != null && notesFx.particleCount > 0;

        protected override void BuildScreen()
        {
            noteIcons.Clear();
            noteLabels.Clear();
            float W = BodyW;
            var box = Panel(Screen, "Box", W * 0.5f, 200f, W - 48f, 360f, Wood);
            float bw = W - 48f;
            for (int i = 0; i < 5; i++) Img(box, $"Staff{i}", bw * 0.5f, 90f + i * 22f, bw - 60f, 2f, new Color(1f, 1f, 1f, 0.3f));
            for (int i = 0; i < Count; i++)
            {
                float x = 60f + (bw - 120f) * (Count == 1 ? 0.5f : i / (float)(Count - 1));
                float y = 178f - NoteSteps[i] * 8f;
                noteIcons.Add(Img(box, $"Note{i}", x, y, 36f, 28f, NoteColors[i], MonitorDisplay.ScreenShape.Circle));
                Img(box, $"Stem{i}", x + 16f, y - 30f, 4f, 60f, new Color(1f, 1f, 1f, 0.6f));
                noteLabels.Add(Lbl(box, $"Name{i}", x, 290f, 80f, 36f, NoteNames[i], 26, Ink));
            }
            melodyText = Lbl(Screen, "Melody", 24f, 440f, W - 48f, 40f, "", 24, Ink, TextAnchor.MiddleLeft);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int i = 0; i < noteIcons.Count; i++)
            {
                bool done = i < expected;
                bool onBoard = false;
                foreach (var kv in noteOf) if (kv.Value == i && kv.Key != null && kv.Key.IsActiveInPool) { onBoard = true; break; }
                Color c = NoteColors[i];
                noteIcons[i].color = done ? c : onBoard ? new Color(c.r, c.g, c.b, 0.5f + 0.3f * Mathf.Sin(Time.time * 5f + i)) : new Color(c.r, c.g, c.b, 0.15f);
                noteLabels[i].color = i == expected ? c : Mute;
            }
            melodyText.text = $"メロディ完成 {melodies}回　次の音：{NoteNames[Mathf.Clamp(expected, 0, 7)]}";
            statusText.text = toMark > 0
                ? $"次に投入する {toMark}枚が音符メダルになる（{NoteNames[Mathf.Clamp(nextIndex, 0, 7)]}から）"
                : $"ド→レ→ミ…の順に中央の穴へ落とすとメロディ完成 +{melodyBonus}pt";
        }
    }
}
