using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N002 フォーク「弾き語りのコード」。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・ルール：
    ///   手前のふちを左から右へ6つに分け、それぞれに弦を1本張る（左ほど低い音）。そのうち3本が「コード」として緑に光る
    ///   メダルが光っている弦の所から落ちると（中央の穴でも端の穴でも）+chordBonus pt。その弦は金色になる
    ///   コードの3本をぜんぶ鳴らすと「ジャーン！」：プッシャーの前のメダルを手前へ一押しし、盤面にメダルが strumMedals 枚降ってくる。
    ///   そのあと、新しいコード（ちがう3本）になる
    /// ・盤面：
    ///   手前のふちの上に、6本の弦と、左右の木のこま（ギターのブリッジ）。メダルが落ちると、その場所の弦が光ってふるえ、音が鳴る
    ///   「ジャーン」の時は、6本が左から右へ波のように光る。まん中の上に「ジャーンまで あと 2」の文字
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class FolkRecord : RecordScriptBase
    {
        public static FolkRecord Instance { get; private set; }

        [Header("コード")]
        [Tooltip("光っている弦の所から落ちた時のポイント")]
        [Range(0, 100)] public int chordBonus = 12;
        [Tooltip("コードの3本をぜんぶ鳴らした時（ジャーン）に、盤面に降ってくるメダルの枚数")]
        [Range(0, 20)] public int strumMedals = 3;
        [Tooltip("ジャーンの時に、プッシャーの前のメダルを手前へ押す強さ")]
        [Range(0f, 5f)] public float strumPush = 1.2f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音。弦は左から6本ぶん）")]
        public AudioClip[] stringSounds = new AudioClip[6];
        public AudioClip strumSound;

        protected override string DefaultKey => "Folk";
        protected override string ScreenTitle => "FOLK";
        protected override string ScreenSubtitle => "弾き語りのコード";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<FolkRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        const int Strings = 6;
        // ギターの開放弦（左から 6弦 E2 → 1弦 E4）
        static readonly float[] Tuning = { 82.41f, 110.00f, 146.83f, 196.00f, 246.94f, 329.63f };
        static readonly Color StringColor = new Color32(0xd8, 0xd4, 0xc8, 0xff);
        static readonly Color ChordColor = new Color32(0x5c, 0xe0, 0x7a, 0xff);
        static readonly Color HitColor = new Color32(0xff, 0xd0, 0x4a, 0xff);
        static readonly Color Wood = new Color32(0x8a, 0x5a, 0x34, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);

        readonly bool[] inChord = new bool[Strings];
        readonly bool[] hit = new bool[Strings];
        readonly float[] vibrate = new float[Strings];
        readonly Transform[] strings = new Transform[Strings];
        readonly Material[] stringMaterials = new Material[Strings];
        float wave = -1f;        // ジャーンの光の波（0〜1。負なら無し）
        ParticleSystem sparkle;
        BoardText countText;
        readonly AudioClip[] madeStrings = new AudioClip[Strings];
        AudioClip madeStrum;

        float SegW => HalfW * 2f / Strings;
        float StringX(int i) => -HalfW + SegW * (i + 0.5f);
        int StringOf(float x) => Mathf.Clamp(Mathf.FloorToInt((x + HalfW) / SegW), 0, Strings - 1);
        float StringY => D * 0.55f;
        float StringZ => FrontZ + D * 0.35f;
        float StringLen => D * 1.6f;

        protected override void BuildBoard()
        {
            var wood = Glow(Wood, 0f);
            // 弦をのせる木のこま（手前と奥）
            Cube("BridgeFront", Board, new Vector3(0f, StringY - D * 0.08f, StringZ - StringLen * 0.5f), new Vector3(HalfW * 2f, D * 0.12f, D * 0.12f), wood);
            Cube("BridgeBack", Board, new Vector3(0f, StringY - D * 0.08f, StringZ + StringLen * 0.5f), new Vector3(HalfW * 2f, D * 0.12f, D * 0.12f), wood);
            for (int i = 0; i < Strings; i++)
            {
                stringMaterials[i] = Glow(StringColor, 0.6f);
                float thick = Mathf.Lerp(D * 0.06f, D * 0.025f, i / (Strings - 1f));   // 左ほど太い
                var s = Cube($"String{i}", Board, new Vector3(StringX(i), StringY, StringZ), new Vector3(thick, thick, StringLen), stringMaterials[i]);
                strings[i] = s.transform;
            }
            sparkle = Particles("Sparkle", Board, Vector3.up, D * 2.5f, D * 0.09f, 0.8f, 0.3f, 40f);
            countText = BoardLabel("StrumCount", Board, new Vector3(0f, D * 3.2f, (FrontZ + PusherFrontZ) * 0.5f), "", D * 0.6f, Ink);
        }

        protected override void OnBegin()
        {
            NewChord();
            for (int i = 0; i < Strings; i++) vibrate[i] = 0f;
            wave = -1f;
        }

        void NewChord()
        {
            for (int i = 0; i < Strings; i++) { inChord[i] = false; hit[i] = false; }
            int picked = 0;
            while (picked < 3)
            {
                int i = Random.Range(0, Strings);
                if (inChord[i]) continue;
                inChord[i] = true;
                picked++;
            }
        }

        int Remaining()
        {
            int n = 0;
            for (int i = 0; i < Strings; i++) if (inChord[i] && !hit[i]) n++;
            return n;
        }

        protected override void Tick(float dt, bool running)
        {
            for (int i = 0; i < Strings; i++) vibrate[i] = Mathf.MoveTowards(vibrate[i], 0f, dt * 1.6f);
            if (wave >= 0f)
            {
                wave += dt * 1.5f;
                if (wave > 1.5f) wave = -1f;
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            int i = StringOf(medal.transform.position.x);
            vibrate[i] = 1f;
            PlayString(i, 0.8f);
            var pos = new Vector3(StringX(i), StringY + D * 0.8f, StringZ);
            if (!inChord[i] || hit[i]) return;

            hit[i] = true;
            AddPoints(chordBonus);
            BoardPop($"+{chordBonus}", HitColor, pos, D * 0.8f);
            Emit(sparkle, new Vector3(StringX(i), StringY, StringZ), HitColor, 18);
            if (Remaining() == 0) Strum();
        }

        void PlayString(int i, float scale)
        {
            var clip = stringSounds != null && i < stringSounds.Length ? stringSounds[i] : null;
            int idx = i;
            Play(clip, ref madeStrings[i], () => RecordSynth.Notes($"FolkString{idx}", new[] { Tuning[idx] * 2f }, 0.05f, 1.0f, 0.35f, RecordSynth.Wave.Bell), scale);
        }

        void Strum()
        {
            wave = 0f;
            for (int i = 0; i < Strings; i++) vibrate[i] = 1f;
            Play(strumSound, ref madeStrum, () => RecordSynth.Chord("FolkStrum", new[] { Tuning[0] * 2f, Tuning[1] * 2f, Tuning[2] * 2f, Tuning[3] * 2f, Tuning[4] * 2f, Tuning[5] * 2f }, 1.6f, 0.3f, RecordSynth.Wave.Bell));
            BoardPop("ジャーン！", HitColor, new Vector3(0f, D * 2.5f, StringZ + D), D * 1.2f);
            Speaker(0.8f, 10, HitColor);

            // プッシャーの前のメダルを、手前へ一押し
            foreach (var m in BoardMedals)
            {
                if (m == null || m.Consumed) continue;
                var p = m.transform.position;
                if (p.y < -0.1f || p.y > Layout.pusherHeight * 0.9f) continue;   // 上の段（プッシャーの上）は押さない
                if (p.z < FrontZ || p.z > PusherFrontZ + D) continue;
                Push(m, new Vector3(0f, D * 0.3f, -D * strumPush));
            }
            if (strumMedals > 0) RainMedals(strumMedals);
            NewChord();
        }

        protected override bool KeepBoardVisible() => sparkle != null && sparkle.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            for (int i = 0; i < Strings; i++)
            {
                if (strings[i] == null) continue;
                // ふるえ：横に細かくゆれる
                float wob = Mathf.Sin(t * 70f + i) * D * 0.06f * vibrate[i];
                strings[i].position = new Vector3(StringX(i) + wob, StringY, StringZ);
                strings[i].gameObject.SetActive(BoardShown > 0.01f);

                Color c = !Active ? StringColor : hit[i] ? HitColor : inChord[i] ? ChordColor : StringColor;
                float glow = inChord[i] && !hit[i] ? 1.6f + 0.8f * Mathf.Sin(t * 5f) : hit[i] ? 2.2f : 0.5f;
                glow += 3f * vibrate[i];
                if (wave >= 0f)
                {
                    float d = Mathf.Abs(wave * 1.2f - i / (Strings - 1f));
                    float k = Mathf.Clamp01(1f - d * 4f);
                    glow += 4f * k;
                    c = Color.Lerp(c, HitColor, k);
                }
                SetGlow(stringMaterials[i], c, glow * BoardShown);
            }
            if (countText != null)
            {
                countText.gameObject.SetActive(Active);
                countText.text = $"ジャーンまで あと {Remaining()}";
            }
        }
    }
}
