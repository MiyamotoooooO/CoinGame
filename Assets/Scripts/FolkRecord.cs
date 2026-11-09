using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>フォークの楽譜すごろくの報酬の種類</summary>
    public enum FolkRewardType
    {
        Points,     // ポイントをもらう
        Gauge,      // グルーヴゲージをもらう
        DoubleNext  // 次に落ちた1枚のポイントが ×2
    }

    /// <summary>フォークの楽譜すごろくの報酬1つ</summary>
    [Serializable]
    public class FolkReward
    {
        public FolkRewardType type = FolkRewardType.Points;
        [Tooltip("ポイントやゲージの量（次の1枚 ×2 の時は使わない）")]
        public float value = 30f;

        public FolkReward() { }
        public FolkReward(FolkRewardType type, float value)
        {
            this.type = type;
            this.value = value;
        }

        /// <summary>モニターに出す短い文字</summary>
        public string Label => type switch
        {
            FolkRewardType.Gauge => $"G+{value:0}",
            FolkRewardType.DoubleNext => "×2",
            _ => $"+{value:0}"
        };
    }

    /// <summary>
    /// N002 フォーク「弾き語りの楽譜すごろく」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：手前のふちの左右いっぱいに弦を6本張る（ふちを6つに分ける）。落ちた場所の弦が光ってふるえ、
    ///   その弦の音が鳴る（左ほど低い音）。中央の穴でも端の穴でも鳴る。狙いの弦は緑に光る
    /// ・モニター：五線譜の上に、すごろくのマス（音符）が並ぶ。コマ（♪）が進むと楽譜が横に流れる。
    ///   報酬のマスには中身（+30・G+10・×2）が見えている。下に6本の弦と、狙いの弦を出す
    /// ・ルール：
    ///   メダルを1枚投入するたびに1マス進む
    ///   狙いの弦の場所に落ちると「ハモリ！」で、さらに harmonySteps マス進む（狙いの弦は変わる）
    ///   報酬のマスを通るともらえる（+30pt・ゲージ+10・次に落ちた1枚のポイント ×2）
    ///   最後のマスまで行くと「1曲完成！」：completeSeconds 秒のあいだポイントが ×completeMultiplier。
    ///   次の楽譜は growPerSong マス長くなる
    /// ・ポイントが入るのは、ラウンドのプレイ中（判定待ちを含む）だけ
    ///
    /// 見た目と音はコードで作る仮のもの。Inspector の「差し替え用」に Prefab・画像・音を入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。
    /// CSV の「効果の型」は Scripted にしておく（前の効果と二重にならないように）。
    /// </summary>
    [DisallowMultipleComponent]
    public class FolkRecord : MonoBehaviour
    {
        /// <summary>シーンにあるフォークのスクリプト（RecordVisualEffects が、前の仮の演出を止めるのに使う）</summary>
        public static FolkRecord Instance { get; private set; }

        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        public RecordEffectSystem effects;

        [Header("レコード")]
        [Tooltip("このスクリプトが受け持つレコードのキー（英名）")]
        public string recordKey = "Folk";

        [Header("楽譜すごろく")]
        [Tooltip("最初の楽譜のマスの数")]
        [Range(6, 40)] public int baseSquares = 16;
        [Tooltip("1曲完成するたびに、次の楽譜が長くなるマスの数")]
        [Range(0, 10)] public int growPerSong = 4;
        [Tooltip("楽譜の長さの上限")]
        [Range(6, 60)] public int maxSquares = 32;
        [Tooltip("狙いの弦に落ちた時（ハモリ）に、さらに進むマスの数")]
        [Range(0, 5)] public int harmonySteps = 2;
        [Tooltip("何マスごとに報酬のマスを置くか")]
        [Range(1, 10)] public int rewardEvery = 3;
        [Tooltip("報酬のマスの中身（この順にくり返して置く）")]
        public FolkReward[] rewardCycle =
        {
            new FolkReward(FolkRewardType.Points, 30f),
            new FolkReward(FolkRewardType.Gauge, 10f),
            new FolkReward(FolkRewardType.DoubleNext, 0f),
        };
        [Tooltip("1曲完成した時の、ポイントの倍率")]
        [Range(1f, 5f)] public float completeMultiplier = 2f;
        [Tooltip("1曲完成した時に、ポイントが増える秒数")]
        [Range(1f, 60f)] public float completeSeconds = 10f;

        [Header("弦（盤面の手前のふち）")]
        [Tooltip("弦の数（ふちをこの数に分ける）")]
        [Range(3, 8)] public int stringCount = 6;
        [Tooltip("仮の弦の音の高さ（左から。Hz）。ギターの開放弦 E A D G B E")]
        public float[] stringFrequencies = { 82.41f, 110.00f, 146.83f, 196.00f, 246.94f, 329.63f };
        public Color stringColor = new Color(0.95f, 0.82f, 0.52f);
        public Color targetColor = new Color(0.35f, 1f, 0.45f);

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        [Tooltip("弦の音（左の弦から順に。足りない弦は仮の音）")]
        public AudioClip[] stringSounds = new AudioClip[0];
        [Tooltip("マスを進む音")]
        public AudioClip stepSound;
        [Tooltip("ハモリ！の音")]
        public AudioClip harmonySound;
        [Tooltip("報酬をもらった音")]
        public AudioClip rewardSound;
        [Tooltip("1曲完成！の音")]
        public AudioClip completeSound;
        [Tooltip("盤面の弦1本の Prefab（弦の数だけ置き、ふるえる動きはこのスクリプトが付ける）")]
        public GameObject stringPrefab;
        [Tooltip("モニターのコマの画像（空なら ♪ の丸）")]
        public Sprite pieceSprite;

        // ---- 状態 ----
        bool active;
        int songLength;
        int position;            // 今のマス（0 がスタート）
        float shownPosition;     // モニターのコマの位置（なめらかに追いかける）
        int songNumber;          // 何曲目
        int targetString;
        bool pendingDouble;      // 次に落ちた1枚のポイントが ×2
        float completeTimer;     // 1曲完成のポイント ×N の残り秒数
        int[] squareReward;      // マスごとの報酬（rewardCycle の番号。-1 は報酬なし）
        float[] squareHeight;    // マスごとの音符の高さ（五線の上の位置）
        readonly System.Random rng = new System.Random();

        // ---- 盤面 ----
        Transform stringRoot;
        readonly List<Transform> strings = new List<Transform>();
        readonly List<Material> stringMaterials = new List<Material>();
        float[] stringShake;     // ふるえの強さ（0〜1）
        float stringsShown;      // 弦の出し入れ（0〜1）
        float D => game.tuning.medalDiameter;

        // ---- 音 ----
        AudioSource audioSource;
        AudioClip[] madeStrings;
        AudioClip[] madeHarmonies;   // 弦ごとのハモリの和音
        AudioClip madeStep, madeReward, madeComplete;

        // ---- モニター ----
        MonitorDisplay monitor;
        RectTransform screen, sheet, piece;
        Text songText, progressText, statusText;
        readonly List<Image> noteHeads = new List<Image>();
        readonly List<Image> boxes = new List<Image>();
        readonly List<Text> boxLabels = new List<Text>();
        Image doubleBadge;
        float[] boxFlash;
        const float StaffTop = 108f;      // 五線の一番上の線（中身の座標）
        const float StaffGap = 24f;       // 線の間
        const float SquareStep = 72f;     // マスの間
        const float PieceX = 170f;        // コマを置く横の位置（楽譜の窓の中）
        const float ViewX = 20f, ViewW = 760f;
        static readonly Color ScreenBg = new Color32(0x23, 0x25, 0x2f, 0xff);
        static readonly Color Orange = new Color32(0xff, 0x7a, 0x1a, 0xff);
        static readonly Color Lime = new Color32(0x9c, 0xf0, 0x5a, 0xff);
        static readonly Color Cyan = new Color32(0x38, 0xc8, 0xff, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        /// <summary>シーンに無ければ、RecordEffectSystem の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<FolkRecord>() != null) return;
            var system = FindFirstObjectByType<RecordEffectSystem>();
            if (system != null) system.gameObject.AddComponent<FolkRecord>();
        }

        void Awake() => Instance = this;

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (effects == null) effects = FindFirstObjectByType<RecordEffectSystem>();
            if (game == null || effects == null)
            {
                Debug.LogWarning("FolkRecord: MedalPusherGame か RecordEffectSystem が見つからないため、フォークの効果は動きません。", this);
                enabled = false;
                return;
            }

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            effects.RecordPlayed += OnRecordPlayed;
            effects.RecordFinished += OnRecordFinished;
            game.MedalInserted += OnMedalInserted;
            game.MedalDropped += OnMedalDropped;

            BuildStrings();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (effects != null)
            {
                effects.RecordPlayed -= OnRecordPlayed;
                effects.RecordFinished -= OnRecordFinished;
            }
            if (game != null)
            {
                game.MedalInserted -= OnMedalInserted;
                game.MedalDropped -= OnMedalDropped;
            }
            if (stringRoot != null) Destroy(stringRoot.gameObject);
        }

        bool IsMine(RecordData r) => r != null && r.key == recordKey;
        bool IsScoring => game.State == GameState.Playing || game.State == GameState.Grace;

        // ================================================================
        // 始まり・終わり
        // ================================================================

        void OnRecordPlayed(RecordData r)
        {
            if (IsMine(r)) Begin();
        }

        void OnRecordFinished(RecordData r)
        {
            if (IsMine(r)) End();
        }

        void Begin()
        {
            active = true;
            songNumber = 1;
            songLength = Mathf.Clamp(baseSquares, 2, maxSquares);
            completeTimer = 0f;
            pendingDouble = false;
            NewSheet();
            targetString = rng.Next(StringCount);
            monitor = game.Monitor;
            BuildScreen();
        }

        void End()
        {
            active = false;
            completeTimer = 0f;
            pendingDouble = false;
            if (monitor != null) monitor.HideRecordScreen(recordKey);
            screen = null;
        }

        int StringCount => Mathf.Max(1, stringCount);

        /// <summary>新しい楽譜を作る（マスの報酬と、音符の高さ）。</summary>
        void NewSheet()
        {
            position = 0;
            shownPosition = 0f;
            squareReward = new int[songLength + 1];
            squareHeight = new float[songLength + 1];
            int cycle = 0;
            for (int i = 0; i <= songLength; i++)
            {
                bool reward = i > 0 && i < songLength && rewardCycle != null && rewardCycle.Length > 0 && i % Mathf.Max(1, rewardEvery) == 0;
                squareReward[i] = reward ? cycle++ % rewardCycle.Length : -1;
                // 音符の高さ：ゆるい波に少しばらつきを足す（五線の中、9段階）
                squareHeight[i] = Mathf.Clamp(Mathf.Round(4f + 3f * Mathf.Sin(i * 0.7f + songNumber) + rng.Next(-1, 2)), 0f, 8f);
            }
        }

        // ================================================================
        // ゲームからの通知
        // ================================================================

        void OnMedalInserted(Medal medal)
        {
            if (!active || !IsScoring) return;
            Advance(1);
        }

        void OnMedalDropped(Medal medal, DropZoneType type)
        {
            if (!active || type == DropZoneType.Lost || medal == null) return;

            int s = StringAt(medal.transform.position.x);
            Pluck(s);

            if (!IsScoring) return;

            // ポイントの上乗せ（次の1枚 ×2・1曲完成 ×N）。基本のポイントは MedalPusherGame が入れている
            int basePoints = type == DropZoneType.Center ? game.rules.centerPoints : game.rules.sidePoints;
            int extra = 0;
            if (pendingDouble && basePoints > 0)
            {
                extra += basePoints;
                pendingDouble = false;
                Pop("×2！", Orange, StringBoxCenter(s) + new Vector2(0f, -60f));
            }
            if (completeTimer > 0f) extra += Mathf.RoundToInt(basePoints * (completeMultiplier - 1f));
            if (extra > 0) effects.AddPoints(extra);

            // 狙いの弦ならハモリ
            if (s == targetString)
            {
                PlayHarmony(s);
                Pop("ハモリ！", Lime, StringBoxCenter(s) + new Vector2(0f, -40f));
                if (s < boxFlash.Length) boxFlash[s] = 1f;
                int next = targetString;
                if (StringCount > 1) while (next == targetString) next = rng.Next(StringCount);
                targetString = next;
                Advance(harmonySteps);
            }
        }

        /// <summary>落ちた横の位置から、何本目の弦か（左が 0）。</summary>
        int StringAt(float x)
        {
            float half = game.layout.boardWidth * 0.5f;
            float t = Mathf.InverseLerp(-half, half, x);
            return Mathf.Clamp(Mathf.FloorToInt(t * StringCount), 0, StringCount - 1);
        }

        // ================================================================
        // すごろく
        // ================================================================

        void Advance(int steps)
        {
            for (int k = 0; k < steps && active; k++)
            {
                position++;
                if (position >= songLength)
                {
                    Complete();
                    return;
                }
                int ri = squareReward[position];
                if (ri >= 0) Grant(rewardCycle[ri]);
            }
            PlaySound(stepSound, ref madeStep, () => MakePluck(659.25f, 0.25f, 0.35f, 99));
        }

        void Grant(FolkReward reward)
        {
            switch (reward.type)
            {
                case FolkRewardType.Points:
                    effects.AddPoints(Mathf.RoundToInt(reward.value));
                    break;
                case FolkRewardType.Gauge:
                    effects.AddGauge(reward.value);
                    break;
                case FolkRewardType.DoubleNext:
                    pendingDouble = true;
                    break;
            }
            PlaySound(rewardSound, ref madeReward, () => MakeChord(new[] { 523.25f, 659.25f, 783.99f }, 1.0f));
            Pop(reward.type == FolkRewardType.DoubleNext ? "次の1枚 ×2" : reward.Label, RewardColor(reward.type), new Vector2(PieceX + ViewX, 60f));
        }

        void Complete()
        {
            completeTimer = completeSeconds;
            PlaySound(completeSound, ref madeComplete, MakeStrum);
            Pop("1曲完成！", Orange, new Vector2(MonitorDisplay.RecordBodyW * 0.5f, 150f));
            for (int i = 0; i < stringShake.Length; i++) stringShake[i] = 1f;
            songNumber++;
            songLength = Mathf.Min(maxSquares, songLength + growPerSong);
            NewSheet();
            BuildSheet();
        }

        static Color RewardColor(FolkRewardType t) => t switch
        {
            FolkRewardType.Gauge => Cyan,
            FolkRewardType.DoubleNext => Orange,
            _ => new Color32(0xff, 0xd2, 0x3f, 0xff)
        };

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (game == null || effects == null) return;
            // 念のため：効果が別のレコードに替わっていたら終える
            if (active && !IsMine(effects.Current)) End();
            if (completeTimer > 0f) completeTimer -= Time.deltaTime;

            UpdateStrings();
            UpdateScreen();
        }

        // ================================================================
        // 盤面の弦
        // ================================================================

        void BuildStrings()
        {
            stringRoot = new GameObject("FolkStrings").transform;
            int n = StringCount;
            stringShake = new float[n];
            boxFlash = new float[n];
            float half = game.layout.boardWidth * 0.5f;
            float zone = game.layout.boardWidth / n;
            float z = game.layout.floorFrontZ - D * 0.05f;
            for (int i = 0; i < n; i++)
            {
                float cx = -half + (i + 0.5f) * zone;
                Transform t;
                Material m = null;
                if (stringPrefab != null)
                {
                    t = Instantiate(stringPrefab, stringRoot).transform;
                    t.position = new Vector3(cx, D * 0.06f, z);
                }
                else
                {
                    t = Cube($"String{i + 1}", new Vector3(cx, D * 0.06f, z), new Vector3(zone * 0.86f, D * 0.04f, D * 0.04f));
                    m = Glow(stringColor, 1f);
                    t.GetComponent<Renderer>().sharedMaterial = m;
                }
                strings.Add(t);
                stringMaterials.Add(m);

                // 弦と弦のあいだの柱（フレット）
                if (stringPrefab == null && i > 0)
                {
                    var post = Cube("Fret", new Vector3(-half + i * zone, D * 0.08f, z), new Vector3(D * 0.05f, D * 0.16f, D * 0.06f));
                    post.GetComponent<Renderer>().sharedMaterial = Glow(new Color(0.55f, 0.42f, 0.30f), 0.2f);
                }
            }
            stringRoot.gameObject.SetActive(false);
        }

        void UpdateStrings()
        {
            stringsShown = Mathf.MoveTowards(stringsShown, active ? 1f : 0f, Time.deltaTime * 3f);
            bool visible = stringsShown > 0f;
            if (stringRoot.gameObject.activeSelf != visible) stringRoot.gameObject.SetActive(visible);
            if (!visible) return;
            stringRoot.localScale = new Vector3(1f, stringsShown, 1f);

            float t = Time.time;
            for (int i = 0; i < strings.Count; i++)
            {
                stringShake[i] = Mathf.MoveTowards(stringShake[i], 0f, Time.deltaTime * 1.6f);
                var s = strings[i];
                var p = s.localPosition;
                p.y = D * 0.06f + Mathf.Sin(t * 70f + i) * stringShake[i] * D * 0.04f; // ふるえる
                s.localPosition = p;

                var m = stringMaterials[i];
                if (m == null) continue;
                bool target = active && i == targetString;
                float pulse = target ? 1.6f + 0.8f * Mathf.Sin(t * 6f) : 0.8f;
                Color c = target ? targetColor : stringColor;
                m.color = c;
                m.SetColor("_EmissionColor", c * (pulse + stringShake[i] * 3f));
            }
        }

        /// <summary>弦を鳴らす（光ってふるえ、音が鳴る）。</summary>
        void Pluck(int s)
        {
            if (s < 0 || s >= stringShake.Length) return;
            stringShake[s] = 1f;
            if (s < boxFlash.Length) boxFlash[s] = Mathf.Max(boxFlash[s], 0.6f);
            AudioClip clip = stringSounds != null && s < stringSounds.Length ? stringSounds[s] : null;
            if (clip == null)
            {
                if (madeStrings == null || madeStrings.Length != StringCount)
                {
                    madeStrings = new AudioClip[StringCount];
                    for (int i = 0; i < StringCount; i++) madeStrings[i] = MakePluck(Freq(i), 1.4f, 0.5f, i);
                }
                clip = madeStrings[s];
            }
            if (clip != null) audioSource.PlayOneShot(clip, volume);
        }

        /// <summary>ハモリの音（差し替えの音が無ければ、その弦の音に5度と1オクターブ上を重ねた和音）。</summary>
        void PlayHarmony(int s)
        {
            AudioClip clip = harmonySound;
            if (clip == null)
            {
                if (madeHarmonies == null || madeHarmonies.Length != StringCount) madeHarmonies = new AudioClip[StringCount];
                if (madeHarmonies[s] == null) madeHarmonies[s] = MakeChord(new[] { Freq(s), Freq(s) * 1.5f, Freq(s) * 2f }, 1.4f);
                clip = madeHarmonies[s];
            }
            audioSource.PlayOneShot(clip, volume);
        }

        float Freq(int s)
        {
            if (stringFrequencies != null && s < stringFrequencies.Length && stringFrequencies[s] > 0f) return stringFrequencies[s];
            return 82.41f * Mathf.Pow(2f, s * 5f / 12f); // 足りない弦は、4度ずつ上げる
        }

        Transform Cube(string name, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());   // 見た目だけ。メダルには当たらない
            go.transform.SetParent(stringRoot, false);
            go.transform.position = position;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        static Material Glow(Color color, float strength)
        {
            var m = ProtoAssets.MakeMaterial(color, 0f, 0.5f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * strength);
            return m;
        }

        // ================================================================
        // モニターの画面
        // ================================================================

        void BuildScreen()
        {
            if (monitor == null) return;
            screen = monitor.ShowRecordScreen(recordKey, "FOLK", "弾き語りの楽譜すごろく");
            if (screen == null) return;
            float W = MonitorDisplay.RecordBodyW, H = MonitorDisplay.RecordBodyH;

            songText = monitor.ScreenLabel(screen, "Song", 24f, 30f, 300f, 30f, "", 20, Ink, TextAnchor.MiddleLeft);
            progressText = monitor.ScreenLabel(screen, "Progress", W - 24f, 30f, 300f, 30f, "", 22, Ink, TextAnchor.MiddleRight, true);

            // 楽譜の窓（五線と、流れていく音符）
            var view = monitor.ScreenImage(screen, "StaffView", ViewX + ViewW * 0.5f, 160f, ViewW, 200f, ScreenBg);
            view.gameObject.AddComponent<RectMask2D>();
            for (int i = 0; i < 5; i++)
                monitor.ScreenImage(view.rectTransform, $"Line{i + 1}", ViewW * 0.5f, StaffTop - 60f + i * StaffGap, ViewW, 2f, new Color(1f, 1f, 1f, 0.3f));
            sheet = monitor.ScreenGroup(view.rectTransform, "Sheet", 0f, 0f, 0f, 0f);
            BuildSheet();

            // コマ（♪）
            if (pieceSprite != null)
            {
                piece = monitor.ScreenImage(view.rectTransform, "Piece", PieceX, 40f, 48f, 48f, Color.white).rectTransform;
                piece.GetComponent<Image>().sprite = pieceSprite;
            }
            else
            {
                piece = monitor.ScreenImage(view.rectTransform, "Piece", PieceX, 40f, 44f, 44f, Orange, MonitorDisplay.ScreenShape.Circle).rectTransform;
                monitor.ScreenLabel(piece, "Mark", 22f, 22f, 44f, 44f, "♪", 28, Color.white);
            }

            // 下：6本の弦（盤面と同じ並び）と、狙いの弦
            monitor.ScreenLabel(screen, "StringsTitle", 24f, 286f, 200f, 26f, "弦（盤面の手前のふち）", 16, Mute, TextAnchor.MiddleLeft);
            boxes.Clear();
            boxLabels.Clear();
            float bw = 62f, gap = 6f;
            for (int i = 0; i < StringCount; i++)
            {
                float cx = 24f + bw * 0.5f + i * (bw + gap);
                boxes.Add(monitor.ScreenImage(screen, $"Box{i + 1}", cx, 330f, bw, 50f, new Color(1f, 1f, 1f, 0.12f), MonitorDisplay.ScreenShape.Pill));
                boxLabels.Add(monitor.ScreenLabel(screen, $"BoxLabel{i + 1}", cx, 330f, bw, 50f, "", 14, Ink));
            }

            // 右下：今の状態（ルールの案内・1曲完成の残り秒数）と、次の1枚 ×2 の札
            statusText = monitor.ScreenLabel(screen, "Status", W - 24f, 312f, 330f, 40f, "", 18, Ink, TextAnchor.MiddleRight);
            doubleBadge = monitor.ScreenImage(screen, "Double", W - 24f - 80f, 352f, 160f, 30f, Orange, MonitorDisplay.ScreenShape.Pill);
            monitor.ScreenLabel(doubleBadge.rectTransform, "Text", 80f, 15f, 160f, 30f, "次の1枚 ×2", 16, Color.white);
        }

        /// <summary>楽譜の音符（マス）を作り直す。</summary>
        void BuildSheet()
        {
            if (sheet == null) return;
            for (int i = sheet.childCount - 1; i >= 0; i--) Destroy(sheet.GetChild(i).gameObject);
            noteHeads.Clear();
            for (int i = 0; i <= songLength; i++)
            {
                float x = PieceX + i * SquareStep;
                float y = NoteY(i);
                int ri = squareReward[i];
                if (i == songLength)
                {
                    // ゴール：終わりの二重線と FIN
                    monitor.ScreenImage(sheet, "End1", x - 6f, StaffTop - 60f + StaffGap * 2f, 3f, StaffGap * 4f, Ink);
                    monitor.ScreenImage(sheet, "End2", x + 4f, StaffTop - 60f + StaffGap * 2f, 8f, StaffGap * 4f, Ink);
                    monitor.ScreenTag(sheet, "Fin", x, 22f, 70f, 26f, "FIN", 16, Orange, Color.white);
                    noteHeads.Add(null);
                    continue;
                }
                Color color = ri >= 0 ? RewardColor(rewardCycle[ri].type) : Ink;
                float size = ri >= 0 ? 30f : 22f;
                var head = monitor.ScreenImage(sheet, $"Note{i}", x, y, size * 1.25f, size, color, MonitorDisplay.ScreenShape.Circle);
                head.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 20f);
                monitor.ScreenImage(sheet, $"Stem{i}", x + size * 0.55f, y - 28f, 3f, 52f, color);
                if (ri >= 0) monitor.ScreenLabel(sheet, $"Reward{i}", x, 186f, 80f, 24f, rewardCycle[ri].Label, 18, color, TextAnchor.MiddleCenter, true);
                noteHeads.Add(head);
            }
        }

        /// <summary>i 番目の音符の上下の位置（楽譜の窓の中）。</summary>
        float NoteY(int i)
        {
            float h = squareHeight != null && i < squareHeight.Length ? squareHeight[i] : 4f;
            return StaffTop - 60f + StaffGap * 4f - h * StaffGap * 0.5f; // 0 が一番下の線、8 が一番上の線
        }

        /// <summary>弦の箱の中心（モニターの中身の座標。ポップの位置に使う）。</summary>
        Vector2 StringBoxCenter(int s)
        {
            float bw = 62f, gap = 6f;
            return new Vector2(24f + bw * 0.5f + s * (bw + gap), 330f);
        }

        void UpdateScreen()
        {
            if (!active || screen == null) return;

            songText.text = $"<color=#FF7A1A>{songNumber}曲目</color>　楽譜 {songLength} マス";
            progressText.text = $"{Mathf.Min(position, songLength)} <size=16>/ {songLength}</size>";

            // コマをなめらかに進める（進む時は少し跳ねる）。楽譜はコマが同じ位置に見えるよう左へ流す
            float before = shownPosition;
            shownPosition = Mathf.MoveTowards(shownPosition, position, Time.deltaTime * 6f);
            if (shownPosition > position) shownPosition = position; // 1曲完成で 0 に戻った時
            if (position < before - 0.5f) shownPosition = position;
            sheet.anchoredPosition = new Vector2(-shownPosition * SquareStep, 0f);
            int lo = Mathf.FloorToInt(shownPosition), hi = Mathf.Min(lo + 1, songLength);
            float frac = shownPosition - lo;
            float y = Mathf.Lerp(NoteY(lo), NoteY(hi), frac) - 30f - Mathf.Sin(frac * Mathf.PI) * 14f;
            piece.anchoredPosition = new Vector2(PieceX, -y);

            // 通り過ぎた音符は薄く
            for (int i = 0; i < noteHeads.Count; i++)
            {
                var head = noteHeads[i];
                if (head == null) continue;
                var c = head.color;
                c.a = i <= position ? 0.3f : 1f;
                head.color = c;
            }

            // 弦の箱：狙いの弦はライム色で点滅、鳴った弦は光る
            for (int i = 0; i < boxes.Count; i++)
            {
                boxFlash[i] = Mathf.MoveTowards(boxFlash[i], 0f, Time.deltaTime * 2.5f);
                bool target = i == targetString;
                Color baseColor = target ? Color.Lerp(Lime, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.time * 6f)) : new Color(1f, 1f, 1f, 0.12f);
                boxes[i].color = Color.Lerp(baseColor, Color.white, boxFlash[i]);
                boxLabels[i].text = target ? "狙い！" : "";
                boxLabels[i].color = target ? new Color(0.1f, 0.15f, 0.1f) : Ink;
            }

            // 右下の案内
            if (completeTimer > 0f)
                statusText.text = $"<color=#FF7A1A>1曲完成！ ポイント ×{completeMultiplier:0.#}</color>　残り {Mathf.CeilToInt(completeTimer)}秒";
            else
                statusText.text = $"投入で1マス進む\n<color=#9CF05A>狙いの弦</color>に落とすとハモリ +{harmonySteps}マス";
            doubleBadge.gameObject.SetActive(pendingDouble);
        }

        void Pop(string text, Color color, Vector2 bodyPos)
        {
            if (monitor == null || screen == null) return;
            monitor.PopText(text, color, monitor.RecordBodyToCanvas(bodyPos));
        }

        // ================================================================
        // 仮の音（コードで作る）
        // ================================================================

        void PlaySound(AudioClip clip, ref AudioClip made, Func<AudioClip> make)
        {
            if (clip == null)
            {
                if (made == null) made = make();
                clip = made;
            }
            if (clip != null) audioSource.PlayOneShot(clip, volume);
        }

        /// <summary>弦をはじいた音（Karplus-Strong）。</summary>
        static AudioClip MakePluck(float frequency, float seconds, float gain, int seed)
        {
            var data = PluckData(frequency, seconds, gain, seed);
            var clip = AudioClip.Create($"FolkPluck_{frequency:0}", data.Length, 1, 44100, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float[] PluckData(float frequency, float seconds, float gain, int seed)
        {
            const int rate = 44100;
            int length = Mathf.Max(1, (int)(rate * seconds));
            int period = Mathf.Max(2, Mathf.RoundToInt(rate / frequency));
            var rand = new System.Random(1234 + seed);
            var buffer = new float[period];
            for (int i = 0; i < period; i++) buffer[i] = (float)(rand.NextDouble() * 2.0 - 1.0);
            var data = new float[length];
            int index = 0;
            for (int i = 0; i < length; i++)
            {
                int next = (index + 1) % period;
                float v = buffer[index];
                buffer[index] = 0.996f * 0.5f * (buffer[index] + buffer[next]);
                float fadeOut = i > length * 0.85f ? (length - i) / (length * 0.15f) : 1f;
                data[i] = v * gain * fadeOut;
                index = next;
            }
            return data;
        }

        /// <summary>いくつかの音を同時に鳴らした和音。</summary>
        static AudioClip MakeChord(float[] freqs, float seconds)
        {
            var mix = new float[(int)(44100 * seconds)];
            for (int k = 0; k < freqs.Length; k++)
            {
                var d = PluckData(freqs[k], seconds, 0.35f, 50 + k);
                for (int i = 0; i < mix.Length; i++) mix[i] += d[i];
            }
            var clip = AudioClip.Create("FolkChord", mix.Length, 1, 44100, false);
            clip.SetData(mix, 0);
            return clip;
        }

        /// <summary>6本の弦を上から順にかき鳴らした音（1曲完成）。</summary>
        AudioClip MakeStrum()
        {
            const int rate = 44100;
            float seconds = 2.2f;
            var mix = new float[(int)(rate * seconds)];
            for (int s = 0; s < StringCount; s++)
            {
                var d = PluckData(Freq(s) * 2f, seconds, 0.3f, 80 + s);
                int offset = (int)(rate * 0.035f * s);
                for (int i = 0; i + offset < mix.Length; i++) mix[i + offset] += d[i];
            }
            var clip = AudioClip.Create("FolkStrum", mix.Length, 1, rate, false);
            clip.SetData(mix, 0);
            return clip;
        }
    }
}
