using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N001 ポップス「コール&レスポンス・ライブ」。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・ルール：
    ///   中央の穴を左・中・右の3レーンに分ける。光っているレーン（コール）に落ちると +callBonus pt、ほかのレーンは +otherBonus pt
    ///   コールは callSeconds 秒ごとか、成功するたびに、ほかのレーンへ変わる
    ///   successesForEncore 回成功すると「アンコール」：盤面にメダルが encoreMedals 枚降ってくる（数え直してまた続く）
    /// ・盤面：
    ///   中央の穴の手前に、赤・黄・青のスポットライトと、レーンの色の光の帯。コールされたレーンは強く点滅し、上に「▼」が弾む
    ///   盤面の左右のふちに、ペンライトの客席（光る棒が並ぶ）。いつもゆらゆら揺れ、成功すると一斉にコールの色で振れる
    ///   アンコールの時は、左右から紙吹雪の大砲
    ///   まん中の上に「アンコールまで あと 3」の文字。成功した時は、落ちた所に「+15」が弾ける
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class PopsRecord : RecordScriptBase
    {
        public static PopsRecord Instance { get; private set; }

        [Header("コール")]
        [Tooltip("コールのレーンに落ちた時のポイント（中央の穴のふだんのポイントに足す）")]
        [Range(0, 100)] public int callBonus = 15;
        [Tooltip("ほかのレーンに落ちた時のポイント")]
        [Range(0, 50)] public int otherBonus = 3;
        [Tooltip("コールが変わる秒数（成功した時もすぐ変わる）")]
        [Range(1f, 20f)] public float callSeconds = 4f;

        [Header("アンコール")]
        [Tooltip("何回成功するとアンコールか")]
        [Range(1, 20)] public int successesForEncore = 5;
        [Tooltip("アンコールで盤面に降ってくるメダルの枚数")]
        [Range(0, 30)] public int encoreMedals = 5;

        [Header("盤面")]
        [Range(0f, 10f)] public float spotIntensity = 4f;
        [Tooltip("左右の客席のペンライトの数（片側）")]
        [Range(2, 20)] public int penlightsPerSide = 8;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip successSound;
        public AudioClip otherSound;
        public AudioClip callSound;
        public AudioClip encoreSound;

        protected override string DefaultKey => "Pops";
        protected override string ScreenTitle => "POPS";
        protected override string ScreenSubtitle => "コール&レスポンス・ライブ";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<PopsRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color[] LaneColors =
        {
            new Color32(0xff, 0x3c, 0x5a, 0xff),   // 赤
            new Color32(0xff, 0xd2, 0x3c, 0xff),   // 黄
            new Color32(0x3c, 0x9c, 0xff, 0xff),   // 青
        };
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);

        int call;                // コールのレーン（0 左、1 中、2 右）
        float callTimer, callFlash, cheer;
        int successes;
        Color cheerColor = Color.white;

        readonly Light[] spots = new Light[3];
        readonly Material[] stripMaterials = new Material[3];
        readonly GameObject[] strips = new GameObject[3];
        Transform[] penlights;
        Material[] penMaterials;
        ParticleSystem confetti, sparkle;
        BoardText arrow, countText;
        AudioClip madeSuccess, madeOther, madeCall, madeEncore;

        float LaneW => HoleHalf * 2f / 3f;
        float LaneX(int lane) => -HoleHalf + LaneW * (lane + 0.5f);
        float LaneZ => FrontZ + D * 0.5f;
        int LaneOf(float x) => Mathf.Clamp(Mathf.FloorToInt((x + HoleHalf) / (HoleHalf * 2f) * 3f), 0, 2);

        protected override void BuildBoard()
        {
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject($"Spot{i}");
                go.transform.SetParent(Board, false);
                var from = new Vector3(LaneX(i) * 0.7f, Layout.wallHeight * 1.1f, LaneZ + D * 2.5f);
                var to = new Vector3(LaneX(i), 0f, LaneZ);
                go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 26f;
                l.range = Layout.wallHeight * 3f;
                l.shadows = LightShadows.None;
                l.color = LaneColors[i];
                l.intensity = 0f;
                spots[i] = l;

                stripMaterials[i] = Glow(LaneColors[i], 1f);
                strips[i] = Cube($"Strip{i}", Board, new Vector3(LaneX(i), D * 0.02f, FrontZ + D * 0.15f),
                                 new Vector3(LaneW * 0.9f, D * 0.04f, D * 0.25f), stripMaterials[i]);
            }

            // 左右のふちの客席（ペンライト）
            int n = penlightsPerSide * 2;
            penlights = new Transform[n];
            penMaterials = new Material[n];
            for (int i = 0; i < n; i++)
            {
                int side = i < penlightsPerSide ? -1 : 1;
                int k = i % penlightsPerSide;
                float z = Mathf.Lerp(FrontZ + D * 0.5f, PusherFrontZ, (k + 0.5f) / penlightsPerSide);
                var pivot = new GameObject($"Penlight{i}").transform;
                pivot.SetParent(Board, false);
                pivot.position = new Vector3(side * (HalfW + D * 0.15f), D * 1.6f, z);
                penMaterials[i] = Glow(LaneColors[i % 3], 2f);
                Cube("Stick", pivot, new Vector3(0f, D * 0.35f, 0f), new Vector3(D * 0.08f, D * 0.7f, D * 0.08f), penMaterials[i]);
                Cube("Grip", pivot, Vector3.zero, new Vector3(D * 0.1f, D * 0.18f, D * 0.1f), Glow(new Color(0.15f, 0.15f, 0.18f), 0f));
                penlights[i] = pivot;
            }

            confetti = Particles("Confetti", Board, Vector3.up, D * 6f, D * 0.12f, 2.2f, 0.6f, 25f);
            sparkle = Particles("Sparkle", Board, Vector3.up, D * 2.5f, D * 0.1f, 0.8f, 0.3f, 40f);
            arrow = BoardLabel("CallArrow", Board, new Vector3(0f, D * 1.6f, LaneZ), "▼", D * 0.9f, Ink);
            countText = BoardLabel("EncoreCount", Board, new Vector3(0f, D * 3.2f, (FrontZ + PusherFrontZ) * 0.5f), "", D * 0.6f, Ink);
        }

        protected override void OnBegin()
        {
            call = Random.Range(0, 3);
            callTimer = callSeconds;
            successes = 0;
            callFlash = 1f;
        }

        void NextCall()
        {
            call = (call + Random.Range(1, 3)) % 3;
            callTimer = callSeconds;
            callFlash = 1f;
            Play(callSound, ref madeCall, () => RecordSynth.Notes("PopsCall", new[] { RecordSynth.Note(12), RecordSynth.Note(16) }, 0.08f, 0.2f, 0.22f, RecordSynth.Wave.Square), 0.6f);
        }

        protected override void Tick(float dt, bool running)
        {
            callFlash = Mathf.MoveTowards(callFlash, 0f, dt * 2f);
            cheer = Mathf.MoveTowards(cheer, 0f, dt * 1.2f);
            if (!running) return;
            callTimer -= dt;
            if (callTimer <= 0f) NextCall();
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            Vector3 p = medal.transform.position;
            int lane = LaneOf(p.x);
            var popPos = new Vector3(LaneX(lane), D * 1.2f, LaneZ);
            if (lane != call)
            {
                AddPoints(otherBonus);
                BoardPop($"+{CenterPoints + otherBonus}", Ink, popPos, D * 0.6f);
                Play(otherSound, ref madeOther, () => RecordSynth.Notes("PopsOther", new[] { RecordSynth.Note(0) }, 0.05f, 0.15f, 0.18f, RecordSynth.Wave.Triangle), 0.5f);
                return;
            }

            AddPoints(callBonus);
            successes++;
            cheer = 1f;
            cheerColor = LaneColors[lane];
            BoardPop($"+{CenterPoints + callBonus}", LaneColors[lane], popPos, D * 0.9f);
            Emit(sparkle, new Vector3(LaneX(lane), D * 0.3f, LaneZ), LaneColors[lane], 24);
            Play(successSound, ref madeSuccess, () => RecordSynth.Notes("PopsSuccess", new[] { RecordSynth.Note(7), RecordSynth.Note(12), RecordSynth.Note(16) }, 0.06f, 0.3f, 0.3f, RecordSynth.Wave.Square));
            Speaker(0.5f, 4, LaneColors[lane]);

            if (successes >= successesForEncore)
            {
                successes = 0;
                Encore();
            }
            NextCall();
        }

        void Encore()
        {
            cheer = 1.6f;
            cheerColor = Color.white;
            if (encoreMedals > 0) RainMedals(encoreMedals);
            for (int s = -1; s <= 1; s += 2)
            {
                var from = new Vector3(s * HalfW, D * 2f, (FrontZ + PusherFrontZ) * 0.5f);
                for (int i = 0; i < 3; i++) EmitConfetti(from, new Vector3(-s, 1.4f, 0.2f), LaneColors[i], 30);
            }
            BoardPop("アンコール！", LaneColors[1], new Vector3(0f, D * 2.5f, LaneZ + D), D * 1.2f);
            Play(encoreSound, ref madeEncore, () => RecordSynth.Notes("PopsEncore", new[] { RecordSynth.Note(0), RecordSynth.Note(4), RecordSynth.Note(7), RecordSynth.Note(12), RecordSynth.Note(16), RecordSynth.Note(19) }, 0.09f, 0.6f, 0.35f, RecordSynth.Wave.Square));
            Speaker(1f, 16, LaneColors[0]);
        }

        void EmitConfetti(Vector3 from, Vector3 dir, Color color, int count)
        {
            if (confetti == null) return;
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                p.position = from;
                p.velocity = (dir.normalized + Random.insideUnitSphere * 0.5f) * D * Random.Range(5f, 9f);
                p.startColor = color;
                confetti.Emit(p, 1);
            }
        }

        protected override bool KeepBoardVisible() =>
            (confetti != null && confetti.particleCount > 0) || (sparkle != null && sparkle.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            for (int i = 0; i < 3; i++)
            {
                bool called = Active && i == call;
                float blink = called ? 0.6f + 0.4f * Mathf.Sin(t * 14f) : 0f;
                if (spots[i] != null)
                {
                    spots[i].intensity = spotIntensity * BoardShown * (called ? 1f + 0.5f * blink + callFlash : 0.25f);
                    spots[i].enabled = spots[i].intensity > 0.001f;
                }
                SetGlow(stripMaterials[i], LaneColors[i], BoardShown * (called ? 2.5f + 2f * blink : 0.4f));
                if (strips[i] != null) strips[i].SetActive(BoardShown > 0.01f);
            }

            if (arrow != null)
            {
                arrow.gameObject.SetActive(Active);
                arrow.transform.position = new Vector3(LaneX(call), D * (1.5f + 0.25f * Mathf.Abs(Mathf.Sin(t * 6f))), LaneZ);
                arrow.Color = LaneColors[call];
            }
            if (countText != null)
            {
                countText.gameObject.SetActive(Active);
                countText.text = $"アンコールまで あと {Mathf.Max(0, successesForEncore - successes)}";
            }

            // ペンライト：いつもゆらゆら。成功するとコールの色で一斉に大きく振る
            for (int i = 0; i < penlights.Length; i++)
            {
                if (penlights[i] == null) continue;
                float sway = Mathf.Sin(t * (2.2f + 1.5f * cheer) + i * 0.7f) * (15f + 30f * cheer);
                int side = i < penlightsPerSide ? -1 : 1;
                penlights[i].localRotation = Quaternion.Euler(0f, 0f, sway + side * 10f);
                penlights[i].gameObject.SetActive(BoardShown > 0.01f);
                Color c = cheer > 0.05f ? cheerColor : LaneColors[(i + Mathf.FloorToInt(t * 0.5f)) % 3];
                SetGlow(penMaterials[i], c, BoardShown * (1.5f + 2f * cheer));
            }
        }
    }
}
