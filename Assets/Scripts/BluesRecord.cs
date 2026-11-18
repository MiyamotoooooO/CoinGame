using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N003 ブルース「哀愁をためて、ソロで返す」。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・ルール：
    ///   端の穴に落ちた時と、idleSeconds 秒どの穴にも落ちなかった時に、グラスにブルーノート（青いメダル）が1枚たまる（最大 maxNotes）
    ///   次のどれかで「ソロ」：グラスがいっぱい・残り投入数が lastInserts 以下（LAST 5）・曲の残りが endSeconds 秒以下
    ///   ソロになるとグラスが傾き、pourInterval 秒ごとに1枚ずつ、本物のメダルを盤面へ流し出す
    ///   流れ出たメダルは青く光る「ブルーノートメダル」。中央の穴に落ちると +notePoints pt（曲が終わっても、落ちるまで効く）
    ///   ソロは、グラスが空になるまで続く。LAST 5 と曲の終わりの間は、たまったそばから流し出す
    ///   曲が終わった時にグラスに残っていた分は、まとめて流し出す
    /// ・盤面：
    ///   左の壁に棚と大きなグラス。中の青いメダルが1枚ずつ積み上がっていくのが見える。上に「7 / 15」の文字
    ///   端の穴の手前のふちが青く光り、端の穴に落ちると強く光る。奥に雨が降る（盤面の青い照明は RecordVisualEffects が出す）
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class BluesRecord : RecordScriptBase
    {
        public static BluesRecord Instance { get; private set; }

        [Header("ためる")]
        [Tooltip("グラスにたまる最大の枚数")]
        [Range(1, 40)] public int maxNotes = 15;
        [Tooltip("この秒数どの穴にも落ちないと、1枚たまる")]
        [Range(1f, 20f)] public float idleSeconds = 5f;

        [Header("ソロ")]
        [Tooltip("残り投入数がこの数以下になるとソロ（LAST 5）")]
        [Range(0, 30)] public int lastInserts = 5;
        [Tooltip("曲の残りがこの秒数以下になるとソロ")]
        [Range(0f, 30f)] public float endSeconds = 10f;
        [Tooltip("ソロで1枚ずつ流し出す間かく（秒）")]
        [Range(0.05f, 2f)] public float pourInterval = 0.3f;
        [Tooltip("ブルーノートメダルが中央の穴に落ちた時のポイント")]
        [Range(0, 100)] public int notePoints = 20;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip noteSound;
        public AudioClip soloSound;
        public AudioClip pourSound;

        protected override string DefaultKey => "Blues";
        protected override string ScreenTitle => "BLUES";
        protected override string ScreenSubtitle => "哀愁をためて、ソロで返す";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<BluesRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        const string Mark = "BlueNote";
        static readonly Color Blue = new Color32(0x3c, 0x8c, 0xff, 0xff);
        static readonly Color GlassColor = new Color(0.75f, 0.9f, 1f, 0.22f);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        // ブルースのペンタトニック（ギターの1音ずつ）
        static readonly int[] SoloScale = { 0, 3, 5, 6, 7, 10, 12, 15 };

        int notes;
        float idleTimer, pourTimer, noteFlash, sideFlash;
        bool solo;
        float tilt;              // グラスの傾き（0〜1）
        int soloStep;

        Transform glass;
        readonly List<GameObject> stack = new List<GameObject>();
        readonly Material[] lipMaterials = new Material[2];
        ParticleSystem rain, sparkle;
        BoardText countText;
        AudioClip madeNote, madeSolo;
        readonly AudioClip[] madePour = new AudioClip[8];

        int InsertsLeft => Mathf.Max(0, game.InsertLimit - game.InsertedThisRound);
        float GlassR => D * 0.75f;
        float GlassH => D * 2.4f;
        Vector3 GlassBase => new Vector3(-HalfW + D * 0.85f, D * 1.6f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.6f));
        float SideX(int side) => side * (HoleHalf + (HalfW - HoleHalf) * 0.5f);

        protected override void BuildBoard()
        {
            // 左の壁の棚
            var shelfMat = Glow(new Color(0.25f, 0.16f, 0.1f), 0f);
            Cube("Shelf", Board, new Vector3(-HalfW + D * 0.7f, GlassBase.y - D * 0.05f, GlassBase.z), new Vector3(D * 1.6f, D * 0.1f, D * 1.8f), shelfMat);

            // グラス（すける筒と底）。傾ける時は、右下（棚のはし）を中心に回す
            glass = new GameObject("Glass").transform;
            glass.SetParent(Board, false);
            glass.position = GlassBase + new Vector3(GlassR, 0f, 0f);
            var glassMat = new Material(Shader.Find("Sprites/Default")) { color = GlassColor };
            Shape(PrimitiveType.Cylinder, "Body", glass, new Vector3(-GlassR, GlassH * 0.5f, 0f), new Vector3(GlassR * 2f, GlassH * 0.5f, GlassR * 2f), glassMat);
            Shape(PrimitiveType.Cylinder, "Bottom", glass, new Vector3(-GlassR, D * 0.05f, 0f), new Vector3(GlassR * 2.1f, D * 0.05f, GlassR * 2.1f), Glow(new Color(0.7f, 0.85f, 1f), 0.4f));

            // 中にたまる青いメダル（見た目だけ）
            var noteMat = Glow(Blue, 1.6f);
            float th = Mathf.Max(D * 0.08f, GlassH * 0.9f / Mathf.Max(1, maxNotes));
            for (int i = 0; i < maxNotes; i++)
            {
                var m = Shape(PrimitiveType.Cylinder, $"Note{i}", glass, new Vector3(-GlassR + Random.Range(-D, D) * 0.08f, D * 0.1f + th * (i + 0.5f), 0f),
                              new Vector3(D * 0.95f, th * 0.45f, D * 0.95f), noteMat);
                m.transform.localRotation = Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(-6f, 6f));
                m.SetActive(false);
                stack.Add(m);
            }

            // 端の穴の手前のふち（青く光る）
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                lipMaterials[i] = Glow(Blue, 1f);
                Cube($"SideLip{i}", Board, new Vector3(SideX(side), D * 0.02f, FrontZ + D * 0.12f), new Vector3((HalfW - HoleHalf) * 0.9f, D * 0.04f, D * 0.2f), lipMaterials[i]);
            }

            rain = Particles("Rain", Board, Vector3.down, D * 8f, D * 0.04f, 0.6f, 1f, 2f);
            var main = rain.main;
            main.loop = true;
            main.startColor = new Color(0.6f, 0.75f, 1f, 0.6f);
            var shape = rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(HalfW * 2f, D, 0.01f);
            rain.transform.SetPositionAndRotation(new Vector3(0f, Layout.wallHeight, BackZ - D * 0.6f), Quaternion.Euler(90f, 0f, 0f));
            sparkle = Particles("Sparkle", Board, Vector3.up, D * 2f, D * 0.09f, 0.8f, 0.3f, 40f);

            countText = BoardLabel("NoteCount", Board, GlassBase + new Vector3(GlassR, GlassH + D * 0.7f, 0f), "", D * 0.55f, Ink);

            MedalMarks.RegisterEffect(Mark, OnNoteMedalDropped);
        }

        void OnNoteMedalDropped(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            AddPoints(notePoints);
            BoardPop($"+{CenterPoints + notePoints}", Blue, new Vector3(medal.transform.position.x, D * 1.2f, FrontZ + D * 0.5f), D * 0.8f);
        }

        protected override void OnBegin()
        {
            notes = 0;
            idleTimer = idleSeconds;
            pourTimer = 0f;
            solo = false;
            soloStep = 0;
        }

        protected override void OnEnd()
        {
            // 残っていた分は、まとめて流し出す
            int n = notes;
            for (int i = 0; i < n; i++) PourOne(i * 0.15f);
            notes = 0;
            solo = false;
        }

        void AddNote()
        {
            if (notes >= maxNotes) return;
            notes++;
            noteFlash = 1f;
            Emit(sparkle, GlassBase + new Vector3(0f, GlassH, 0f), Blue, 10);
            Play(noteSound, ref madeNote, () => RecordSynth.Notes("BluesNote", new[] { RecordSynth.Note(3) }, 0.05f, 0.4f, 0.22f, RecordSynth.Wave.Triangle), 0.7f);
        }

        protected override void Tick(float dt, bool running)
        {
            noteFlash = Mathf.MoveTowards(noteFlash, 0f, dt * 2f);
            sideFlash = Mathf.MoveTowards(sideFlash, 0f, dt * 1.5f);
            if (!running) return;

            idleTimer -= dt;
            if (idleTimer <= 0f)
            {
                idleTimer = idleSeconds;
                AddNote();
            }

            bool last = InsertsLeft <= lastInserts;
            bool ending = effects.Remaining <= endSeconds;
            if (!solo && notes > 0 && (notes >= maxNotes || last || ending))
            {
                solo = true;
                pourTimer = 0.4f;
                Play(soloSound, ref madeSolo, () => RecordSynth.Sweep("BluesSolo", 196f, 392f, 0.8f, 0.3f, RecordSynth.Wave.Saw));
                BoardPop("SOLO", Blue, GlassBase + new Vector3(GlassR * 2f, GlassH + D * 1.2f, 0f), D * 1f);
                Speaker(0.8f, 8, Blue);
            }
            if (solo)
            {
                pourTimer -= dt;
                if (pourTimer <= 0f && notes > 0)
                {
                    pourTimer = pourInterval;
                    notes--;
                    PourOne(0f);
                }
                // 空になったら終わり（LAST 5・曲の終わりの間は、またたまったらすぐ流す）
                if (notes == 0 && !last && !ending) solo = false;
            }
        }

        /// <summary>グラスのふちから、本物のメダルを1枚流し出す（delay 秒あとに出す時は、すこし遅らせる）</summary>
        void PourOne(float delay)
        {
            var lip = glass != null ? glass.TransformPoint(new Vector3(0f, GlassH, 0f)) : GlassBase + Vector3.up * GlassH;
            lip.y += delay * D;   // まとめて出す時に重ならないように
            var m = SpawnMedal(lip, new Vector3(D * Random.Range(1.6f, 2.4f), D * 0.5f, -D * Random.Range(0.2f, 0.8f)));
            if (m != null) MedalMarks.Set(m, Mark, Blue);
            int slot = soloStep % madePour.Length;
            soloStep++;
            Play(pourSound, ref madePour[slot], () => RecordSynth.Notes($"BluesPour{slot}", new[] { RecordSynth.Note(SoloScale[slot % SoloScale.Length] - 5) }, 0.05f, 0.35f, 0.3f, RecordSynth.Wave.Saw), 0.7f);
            Emit(sparkle, lip, Blue, 8);
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            idleTimer = idleSeconds;
            if (type != DropZoneType.Side) return;
            sideFlash = 1f;
            AddNote();
            BoardPop("ブルーノート", Blue, new Vector3(medal.transform.position.x, D * 1.2f, FrontZ + D * 0.5f), D * 0.5f);
        }

        protected override bool KeepBoardVisible() => sparkle != null && sparkle.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            tilt = Mathf.MoveTowards(tilt, solo && Active ? 1f : 0f, dt * 2.5f);
            if (glass != null)
            {
                glass.localRotation = Quaternion.Euler(0f, 0f, -55f * Ease.OutCubic(tilt));   // 右へ傾く
                glass.localScale = Vector3.one * (1f + 0.06f * noteFlash);
            }
            for (int i = 0; i < stack.Count; i++)
                if (stack[i] != null) stack[i].SetActive(i < notes && BoardShown > 0.01f);

            for (int i = 0; i < 2; i++)
                SetGlow(lipMaterials[i], Blue, BoardShown * (0.8f + 0.3f * Mathf.Sin(Time.time * 2f) + 3f * sideFlash));

            if (rain != null)
            {
                var em = rain.emission;
                em.rateOverTime = Active ? 120f : 0f;
                if (Active && !rain.isPlaying) rain.Play();
            }
            if (countText != null)
            {
                countText.gameObject.SetActive(Active);
                countText.text = solo ? "SOLO" : $"{notes} / {maxNotes}";
                countText.Color = solo ? Color.Lerp(Ink, Blue, 0.5f + 0.5f * Mathf.Sin(Time.time * 8f)) : Ink;
            }
        }
    }
}
