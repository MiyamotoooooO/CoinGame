using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N004 シティポップ「ミッドナイト・ドライブ」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：奥の壁の前に、ネオンのビルが8棟せり上がる。ビルの窓の明かりの数が、そのまま今の加算。
    ///   ビルの前の道を、車のヘッドライトとテールランプの光が流れる（街が明るいほど車が多い）。
    ///   消灯が近づくと、一番上の明かりがチカチカする。消灯すると、火花が散って明かりが1つ消える。
    ///   「CITY LIGHTS」の間は、窓も屋上のネオンもピンクと水色に光り、車の光が速くなる
    /// ・モニター：夕焼けから夜へ変わっていく空と、街のシルエット。左上の時計が 19:00 から 24:00 へ進む
    ///   （曲の進み具合）。空の右上に、大きく今の加算「+8」。下にネオンのメーター（明かり）、
    ///   「次の明かり」と「消灯まで」の2本のバー
    /// ・ルール：
    ///   中央の穴に落ちると、1枚ごとに +加算 pt（はじめは +startAdd）。端の穴は 0pt のまま
    ///   stepSeconds 秒ごとに、加算が +stepAdd ずつ増える（最大 +maxAdd）
    ///   消灯：idleSeconds 秒どの穴（中央・端）にも落ちないと、明かりが blackoutLoss 段消える（+minAdd より下にはならない）。
    ///         消灯すると「次の明かり」も 0 からやり直し。投入し続けるか、手持ちを温存するかの判断になる
    ///   加算が最大になると「CITY LIGHTS」：lightsSeconds 秒のあいだ、中央の穴のポイントが ×lightsMultiplier
    ///   （基本のポイントにも掛かる）。この間は消灯しない。終わると加算は +afterLightsAdd に戻り、また上っていく
    /// ・ポイントが入るのは、ラウンドのプレイ中（判定待ちを含む）だけ。明かりが増える・消灯するのも、その間だけ
    ///
    /// 見た目と音はコードで作る仮のもの。Inspector の「差し替え用」に Prefab・音を入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。
    /// CSV の「効果の型」は Scripted にしておく（前の効果 AddPerDropGrowing と二重にならないように）。
    /// </summary>
    [DisallowMultipleComponent]
    public class CityPopRecord : MonoBehaviour
    {
        /// <summary>シーンにあるシティポップのスクリプト（RecordVisualEffects が、前の仮の演出を止めるのに使う）</summary>
        public static CityPopRecord Instance { get; private set; }

        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        public RecordEffectSystem effects;

        [Header("レコード")]
        [Tooltip("このスクリプトが受け持つレコードのキー（英名）")]
        public string recordKey = "CityPop";

        [Header("街の明かり（明かりの数 = 1枚ごとの上乗せ pt）")]
        [Tooltip("発動した時の上乗せ（中央の穴に落ちた1枚ごと）")]
        [Range(0, 20)] public int startAdd = 2;
        [Tooltip("明かりが増える間かく（秒）")]
        [Range(1f, 60f)] public float stepSeconds = 10f;
        [Tooltip("1回で増える明かりの数")]
        [Range(1, 5)] public int stepAdd = 1;
        [Tooltip("明かりの最大（盤面の窓の数）。ここまで行くと CITY LIGHTS")]
        [Range(2, 30)] public int maxAdd = 12;

        [Header("消灯（メダルが落ちない時）")]
        [Tooltip("この秒数どの穴にも落ちないと、明かりが消える")]
        [Range(1f, 30f)] public float idleSeconds = 6f;
        [Tooltip("消える前に、明かりがチカチカする秒数")]
        [Range(0f, 5f)] public float warnSeconds = 2f;
        [Tooltip("1回の消灯で消える明かりの数")]
        [Range(1, 5)] public int blackoutLoss = 1;
        [Tooltip("これより下には消えない")]
        [Range(0, 20)] public int minAdd = 1;

        [Header("CITY LIGHTS（明かりが最大）")]
        [Range(1f, 60f)] public float lightsSeconds = 10f;
        [Tooltip("CITY LIGHTS 中のポイントの倍率（中央の穴）")]
        [Range(1f, 5f)] public float lightsMultiplier = 2f;
        [Tooltip("CITY LIGHTS が終わった後の明かりの数")]
        [Range(0, 30)] public int afterLightsAdd = 6;

        [Header("盤面の街並み")]
        [Tooltip("ビルの数")]
        [Range(3, 16)] public int buildingCount = 8;
        [Tooltip("街並みの幅（盤面の幅に対する割合）")]
        [Range(0.2f, 1.2f)] public float widthRatio = 0.9f;
        [Tooltip("一番高いビルの高さ（盤面の幅に対する割合）")]
        [Range(0.02f, 0.5f)] public float heightRatio = 0.12f;
        [Tooltip("置き場所のずれ（奥の壁の前、プッシャーの上面の高さが基準）")]
        public Vector3 offset = Vector3.zero;
        [Tooltip("街の明かりが盤面を照らす明るさ")]
        [Range(0f, 10f)] public float glowIntensity = 3f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        [Tooltip("明かりが増えた音（エレピの和音など）")]
        public AudioClip stepSound;
        [Tooltip("明かりがチカチカし始めた音")]
        public AudioClip warnSound;
        [Tooltip("消灯した音")]
        public AudioClip blackoutSound;
        [Tooltip("CITY LIGHTS の音")]
        public AudioClip lightsSound;
        [Tooltip("中央の穴に落ちた時の音（空なら鳴らさない）")]
        public AudioClip dropSound;
        [Tooltip("街並みの Prefab（空なら仮の街並み）。子の中で名前が「Window」で始まる物を、明かりとして名前の順に点ける")]
        public GameObject skylinePrefab;

        // ---- 色 ----
        static readonly Color Pink = new Color32(0xff, 0x4f, 0xa3, 0xff);
        static readonly Color Cyan = new Color32(0x3f, 0xe0, 0xff, 0xff);
        static readonly Color Sunset = new Color32(0xff, 0x8a, 0x3d, 0xff);
        static readonly Color Purple = new Color32(0x7b, 0x4d, 0xff, 0xff);
        static readonly Color WindowYellow = new Color32(0xff, 0xd9, 0x6a, 0xff);
        static readonly Color Warn = new Color32(0xff, 0x4d, 0x4d, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Dark = new Color32(0x24, 0x22, 0x26, 0xff);
        static readonly Color BuildingColor = new Color32(0x16, 0x13, 0x2a, 0xff);

        // ---- 状態 ----
        bool active;
        int add;                 // 今の上乗せ（= 明かりの数）
        float growTimer;         // 次の明かりまで
        float idleTimer;         // 最後にメダルが落ちてから
        float lightsTimer;       // CITY LIGHTS の残り
        bool warned;             // チカチカの音を鳴らしたか
        float stepPulse;         // 明かりが増えた時のはずみ
        float blackoutPulse;     // 消灯した時のはずみ
        readonly System.Random rng = new System.Random();
        bool Lights => lightsTimer > 0f;
        bool Warning => active && !Lights && add > minAdd && idleTimer > idleSeconds - warnSeconds;

        // ---- 盤面 ----
        Transform stageRoot, buildingsRoot;
        readonly List<GameObject> windows = new List<GameObject>();       // 明かり（点ける順）
        readonly List<Material> windowMaterials = new List<Material>();   // 明かりの色（仮の街並みの時だけ）
        readonly List<Material> neonMaterials = new List<Material>();
        readonly List<Color> neonColors = new List<Color>();
        readonly List<Transform> cars = new List<Transform>();
        readonly List<float> carSpeeds = new List<float>();
        readonly List<float> carThresholds = new List<float>();
        Material headMaterial, tailMaterial;
        Light glow;
        ParticleSystem sparks;
        Material particleMaterial;
        float stageShown;
        float streetHalf;
        float D => game.tuning.medalDiameter;

        // ---- 音 ----
        AudioSource audioSource;
        AudioClip madeStep, madeWarn, madeBlackout, madeLights;

        // ---- モニター ----
        MonitorDisplay monitor;
        RectTransform screen, sun, lightsTag;
        Text clockText, addText, addSmall, statusText, nextLabel, idleLabel, lightsText;
        Image nextFill, idleFill;
        readonly List<Image> skyBands = new List<Image>();
        readonly List<Image> screenWindows = new List<Image>();          // 明かり（盤面と同じ順）
        readonly List<Image> decoWindows = new List<Image>();            // 飾りの窓
        readonly List<float> decoThresholds = new List<float>();
        readonly List<Image> meterCells = new List<Image>();
        readonly List<RectTransform> screenCars = new List<RectTransform>();
        readonly List<float> screenCarSpeeds = new List<float>();
        float bodyW = 800f;
        float skyW;
        const float SkyTop = 64f;
        const float SkyH = 380f;
        const float MeterY = 478f;
        const float BarY = 532f;
        const float StatusY = 576f;
        const float BarLeft = 150f;
        static readonly Color[] DayBands =
        {
            new Color32(0xff, 0x7a, 0x3a, 0xff), new Color32(0xff, 0x5a, 0x6e, 0xff), new Color32(0xe0, 0x4a, 0x9a, 0xff),
            new Color32(0xa4, 0x45, 0xb0, 0xff), new Color32(0x6a, 0x3c, 0xa8, 0xff), new Color32(0x3a, 0x2c, 0x7a, 0xff),
        };
        static readonly Color[] NightBands =
        {
            new Color32(0x0c, 0x0b, 0x22, 0xff), new Color32(0x12, 0x10, 0x30, 0xff), new Color32(0x1a, 0x14, 0x3e, 0xff),
            new Color32(0x26, 0x18, 0x4c, 0xff), new Color32(0x3a, 0x1c, 0x58, 0xff), new Color32(0x5a, 0x22, 0x60, 0xff),
        };

        /// <summary>シーンに無ければ、RecordEffectSystem の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<CityPopRecord>() != null) return;
            var system = FindFirstObjectByType<RecordEffectSystem>();
            if (system != null) system.gameObject.AddComponent<CityPopRecord>();
        }

        void Awake() => Instance = this;

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (effects == null) effects = FindFirstObjectByType<RecordEffectSystem>();
            if (game == null || effects == null)
            {
                Debug.LogWarning("CityPopRecord: MedalPusherGame か RecordEffectSystem が見つからないため、シティポップの効果は動きません。", this);
                enabled = false;
                return;
            }

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            effects.RecordPlayed += OnRecordPlayed;
            effects.RecordFinished += OnRecordFinished;
            game.MedalDropped += OnMedalDropped;

            BuildStage();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (effects != null)
            {
                effects.RecordPlayed -= OnRecordPlayed;
                effects.RecordFinished -= OnRecordFinished;
            }
            if (game != null) game.MedalDropped -= OnMedalDropped;
            if (stageRoot != null) Destroy(stageRoot.gameObject);
        }

        bool IsMine(RecordData r) => r != null && r.key == recordKey;
        bool IsScoring => game.State == GameState.Playing || game.State == GameState.Grace;
        int MaxAdd => Mathf.Max(1, maxAdd);

        /// <summary>ピンクと水色を行き来する色（CITY LIGHTS 用）</summary>
        static Color NeonCycle(float offset)
        {
            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f + offset);
            return Color.Lerp(Pink, Cyan, t);
        }

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
            add = Mathf.Clamp(startAdd, 0, MaxAdd);
            growTimer = 0f;
            idleTimer = 0f;
            lightsTimer = 0f;
            warned = false;
            stepPulse = 1f;
            blackoutPulse = 0f;
            monitor = game.Monitor;
            BuildScreen();
        }

        void End()
        {
            active = false;
            lightsTimer = 0f;
            if (monitor != null) monitor.HideRecordScreen(recordKey);
            screen = null;
        }

        /// <summary>明かりが増える</summary>
        void StepUp()
        {
            int before = add;
            add = Mathf.Min(MaxAdd, add + stepAdd);
            if (add > before)
            {
                stepPulse = 1f;
                PlaySound(stepSound, ref madeStep, MakeStep);
                Pop($"明かり +{add - before}", WindowYellow, new Vector2(skyW * 0.5f + 24f, SkyTop + 90f));
                if (SpeakerUnit.Main != null) SpeakerUnit.Main.Pulse(0.4f);
            }
            if (add >= MaxAdd) StartLights();
        }

        /// <summary>消灯：明かりが消える</summary>
        void Blackout()
        {
            idleTimer = 0f;
            warned = false;
            if (add <= minAdd) return;
            add = Mathf.Max(minAdd, add - blackoutLoss);
            growTimer = 0f;
            blackoutPulse = 1f;
            PlaySound(blackoutSound, ref madeBlackout, MakeBlackout);
            EmitSparks(add);
            Pop("消灯…", Warn, new Vector2(skyW * 0.5f + 24f, SkyTop + 90f));
        }

        void StartLights()
        {
            lightsTimer = lightsSeconds;
            idleTimer = 0f;
            warned = false;
            PlaySound(lightsSound, ref madeLights, MakeLights);
            Pop("CITY LIGHTS！", Pink, new Vector2(bodyW * 0.5f, SkyTop + 140f));
            if (SpeakerUnit.Main != null)
            {
                SpeakerUnit.Main.Pulse(1f);
                SpeakerUnit.Main.EmitNotes(16, Pink);
            }
        }

        void EndLights()
        {
            lightsTimer = 0f;
            add = Mathf.Clamp(afterLightsAdd, minAdd, MaxAdd - 1);
            growTimer = 0f;
            idleTimer = 0f;
        }

        // ================================================================
        // ゲームからの通知
        // ================================================================

        void OnMedalDropped(Medal medal, DropZoneType type)
        {
            if (!active || medal == null || type == DropZoneType.Lost) return;
            if (!IsScoring) return;

            // どの穴でも、落ちれば消灯までの時間は元に戻る
            idleTimer = 0f;
            warned = false;
            if (type != DropZoneType.Center) return;

            // ポイントの上乗せ。基本のポイント（中央 10pt）は MedalPusherGame が入れている
            int extra = add;
            if (Lights) extra += Mathf.RoundToInt((game.rules.centerPoints + add) * (lightsMultiplier - 1f));
            if (extra > 0) effects.AddPoints(extra);

            if (dropSound != null && audioSource != null) audioSource.PlayOneShot(dropSound, volume * 0.6f);
            float x = 24f + skyW * (0.15f + 0.7f * (float)rng.NextDouble());
            Pop($"+{extra}", Lights ? NeonCycle(x) : WindowYellow, new Vector2(x, SkyTop + SkyH - 150f));
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (game == null || effects == null) return;
            // 念のため：効果が別のレコードに替わっていたら終える
            if (active && !IsMine(effects.Current)) End();

            if (active && IsScoring)
            {
                if (Lights)
                {
                    lightsTimer -= Time.deltaTime;
                    if (lightsTimer <= 0f) EndLights();
                }
                else
                {
                    growTimer += Time.deltaTime;
                    if (growTimer >= stepSeconds)
                    {
                        growTimer -= stepSeconds;
                        StepUp();
                    }

                    idleTimer += Time.deltaTime;
                    if (Warning && !warned)
                    {
                        warned = true;
                        PlaySound(warnSound, ref madeWarn, MakeWarn);
                    }
                    if (idleTimer >= idleSeconds) Blackout();
                }
            }
            stepPulse = Mathf.MoveTowards(stepPulse, 0f, Time.deltaTime * 2.5f);
            blackoutPulse = Mathf.MoveTowards(blackoutPulse, 0f, Time.deltaTime * 2f);

            UpdateStage();
            UpdateScreen();
        }

        /// <summary>消灯が近い一番上の明かりを、チカチカさせるか（今のフレームで消して見せるか）</summary>
        bool FlickerOff()
        {
            if (!Warning) return false;
            float speed = Mathf.Lerp(6f, 22f, Mathf.InverseLerp(idleSeconds - warnSeconds, idleSeconds, idleTimer));
            return Mathf.PerlinNoise(Time.time * speed, 0.37f) < 0.45f;
        }

        // ================================================================
        // 盤面：ネオンの街並み・車の光・火花
        // ================================================================

        void BuildStage()
        {
            var layout = game.layout;
            stageRoot = new GameObject("CityPopStage").transform;
            stageRoot.position = new Vector3(0f, layout.pusherHeight, game.BackWallFrontZ - D * 0.05f) + offset;
            buildingsRoot = new GameObject("Buildings").transform;
            buildingsRoot.SetParent(stageRoot, false);
            particleMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit"));

            if (skylinePrefab != null)
            {
                var inst = Instantiate(skylinePrefab, buildingsRoot);
                inst.transform.localPosition = Vector3.zero;
                var found = new List<Transform>();
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    if (t != inst.transform && t.name.StartsWith("Window")) found.Add(t);
                found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                foreach (var t in found) windows.Add(t.gameObject);
            }
            else
            {
                BuildPlaceholderSkyline(layout);
            }

            BuildCars(layout);

            // 街の明かりが盤面を照らす（ピンクの光）
            var glowGo = new GameObject("CityGlow");
            glowGo.transform.SetParent(stageRoot, false);
            glowGo.transform.localPosition = new Vector3(0f, layout.boardWidth * heightRatio * 0.6f, -D * 1.5f);
            glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = layout.boardWidth * 0.9f;
            glow.shadows = LightShadows.None;
            glow.color = Pink;
            glow.intensity = 0f;

            sparks = BuildSparks();
            stageRoot.gameObject.SetActive(false);
        }

        /// <summary>仮の街並み：高さの違うビル、屋上のネオン、窓（明かりは maxAdd 個）。</summary>
        void BuildPlaceholderSkyline(BoardLayout layout)
        {
            var rand = new System.Random(4);
            int n = Mathf.Max(1, buildingCount);
            float width = layout.boardWidth * widthRatio;
            float maxH = layout.boardWidth * heightRatio;
            float depth = D * 0.3f;
            float gap = width * 0.01f;

            var bodyMat = MakeGlowMaterial(BuildingColor, 0.3f);
            var offMat = MakeGlowMaterial(new Color(0.12f, 0.11f, 0.2f), 0f);

            var widths = new float[n];
            var heights = new float[n];
            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                widths[i] = 0.7f + (float)rand.NextDouble() * 0.6f;
                heights[i] = maxH * (0.45f + (float)rand.NextDouble() * 0.55f);
                sum += widths[i];
            }

            // 明かりを各ビルに配る（高いビルほど多め）
            var perBuilding = new int[n];
            for (int k = 0; k < MaxAdd; k++)
            {
                int best = 0;
                float bestScore = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    float score = heights[i] / (perBuilding[i] + 1);
                    if (score > bestScore) { bestScore = score; best = i; }
                }
                perBuilding[best]++;
            }

            var byBuilding = new List<List<GameObject>>();
            var matsByBuilding = new List<List<Material>>();
            float x = -width * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float w = width * widths[i] / sum;
                float h = heights[i];
                float cx = x + w * 0.5f;
                var body = MakeCube($"Building_{i + 1}", buildingsRoot, new Vector3(cx, h * 0.5f, 0f), new Vector3(w - gap, h, depth));
                body.GetComponent<Renderer>().sharedMaterial = bodyMat;

                // 屋上のネオン
                Color nc = i % 3 == 0 ? Pink : i % 3 == 1 ? Cyan : Purple;
                var neon = MakeCube($"Neon_{i + 1}", buildingsRoot, new Vector3(cx, h, 0f), new Vector3(w - gap, D * 0.06f, depth * 1.05f));
                var nm = MakeGlowMaterial(nc, 1.5f);
                neon.GetComponent<Renderer>().sharedMaterial = nm;
                neonMaterials.Add(nm);
                neonColors.Add(nc);

                // 窓（消えている窓の上に、明かりを重ねる）
                var list = new List<GameObject>();
                var mats = new List<Material>();
                int count = perBuilding[i];
                int cols = Mathf.Max(1, Mathf.Min(count, Mathf.FloorToInt((w - gap) / (D * 0.35f))));
                int rows = Mathf.CeilToInt(count / (float)cols);
                float ws = Mathf.Min(D * 0.22f, (w - gap) / (cols * 1.6f), h / (rows * 1.8f + 1f));
                for (int k = 0; k < count; k++)
                {
                    int c = k % cols, row = k / cols;
                    float wx = cx + (c - (cols - 1) * 0.5f) * ws * 1.6f;
                    float wy = ws * 1.6f * (row + 1);   // 下の階から点いていく
                    var off = MakeCube("WindowOff", buildingsRoot, new Vector3(wx, wy, -depth * 0.5f - D * 0.01f), new Vector3(ws, ws, D * 0.02f));
                    off.GetComponent<Renderer>().sharedMaterial = offMat;
                    var on = MakeCube("Window", buildingsRoot, new Vector3(wx, wy, -depth * 0.5f - D * 0.02f), new Vector3(ws, ws, D * 0.02f));
                    var m = MakeGlowMaterial(WindowYellow, 2.5f);
                    on.GetComponent<Renderer>().sharedMaterial = m;
                    on.SetActive(false);
                    list.Add(on);
                    mats.Add(m);
                }
                byBuilding.Add(list);
                matsByBuilding.Add(mats);
                x += w;
            }

            // 点ける順：ビルを順番にまわる（街ぜんたいに少しずつ明かりが広がる）
            int start = n / 2;
            int stride = Stride(n);
            bool any = true;
            for (int level = 0; any; level++)
            {
                any = false;
                for (int j = 0; j < n; j++)
                {
                    int b = (start + j * stride) % n;   // となりのビルばかりにならないように飛ばす
                    if (level < byBuilding[b].Count)
                    {
                        windows.Add(byBuilding[b][level]);
                        windowMaterials.Add(matsByBuilding[b][level]);
                        any = true;
                    }
                }
            }
        }

        /// <summary>ビルを飛ばしてまわる時の間かく（3つ飛ばし。ビルの数が3で割り切れる時は1つずつ）</summary>
        static int Stride(int n) => n % 3 == 0 ? 1 : 3;

        /// <summary>ビルの前の道を流れる、車のヘッドライト（右へ）とテールランプ（左へ）。</summary>
        void BuildCars(BoardLayout layout)
        {
            streetHalf = layout.boardWidth * widthRatio * 0.5f;
            headMaterial = MakeGlowMaterial(new Color(1f, 0.95f, 0.8f), 3f);
            tailMaterial = MakeGlowMaterial(new Color(1f, 0.2f, 0.3f), 3f);
            var rand = new System.Random(11);
            const int perLane = 7;
            for (int lane = 0; lane < 2; lane++)
            {
                for (int i = 0; i < perLane; i++)
                {
                    float z = -D * (0.3f + 0.12f * lane);
                    float y = D * 0.04f;
                    var car = MakeCube(lane == 0 ? "Headlight" : "Taillight", stageRoot,
                                       new Vector3(-streetHalf + streetHalf * 2f * i / perLane, y, z),
                                       new Vector3(D * 0.35f, D * 0.03f, D * 0.03f));
                    car.GetComponent<Renderer>().sharedMaterial = lane == 0 ? headMaterial : tailMaterial;
                    cars.Add(car.transform);
                    float speed = D * (1.2f + (float)rand.NextDouble() * 0.8f) * (lane == 0 ? 1f : -1f);
                    carSpeeds.Add(speed);
                    carThresholds.Add((float)rand.NextDouble());   // 街が明るいほど、車が多く出る
                }
            }
        }

        ParticleSystem BuildSparks()
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(stageRoot, false);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1f, -0.5f));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(D * 1.5f, D * 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(D * 0.04f, D * 0.09f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 50f;
            shape.radius = D * 0.05f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = particleMaterial;
            return ps;
        }

        /// <summary>消えた明かりの所から、火花を散らす。</summary>
        void EmitSparks(int windowIndex)
        {
            if (sparks == null) return;
            Vector3 pos = stageRoot.position + new Vector3(0f, D * 0.5f, -D * 0.3f);
            if (windowIndex >= 0 && windowIndex < windows.Count && windows[windowIndex] != null)
                pos = windows[windowIndex].transform.position + new Vector3(0f, 0f, -D * 0.05f);
            sparks.transform.position = pos;
            var p = new ParticleSystem.EmitParams { startColor = Sunset, applyShapeToPosition = true };
            sparks.Emit(p, 24);
            var p2 = new ParticleSystem.EmitParams { startColor = WindowYellow, applyShapeToPosition = true };
            sparks.Emit(p2, 10);
        }

        void UpdateStage()
        {
            if (stageRoot == null) return;
            stageShown = Mathf.MoveTowards(stageShown, active ? 1f : 0f, Time.deltaTime * 1.25f);
            bool particles = sparks != null && sparks.particleCount > 0;
            bool visible = stageShown > 0f || particles;
            if (stageRoot.gameObject.activeSelf != visible) stageRoot.gameObject.SetActive(visible);
            if (!visible) return;

            // 下から立ち上がる
            float e = 1f - (1f - stageShown) * (1f - stageShown);
            buildingsRoot.localScale = new Vector3(1f, Mathf.Max(0.001f, e), 1f);

            float t = Time.time;
            bool flickerOff = FlickerOff();
            float level = Mathf.Clamp01(add / (float)MaxAdd);

            // 窓の明かり
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] == null) continue;
                bool lit = active && i < add;
                if (lit && i == add - 1 && flickerOff) lit = false;
                if (windows[i].activeSelf != lit) windows[i].SetActive(lit);
                if (!lit || i >= windowMaterials.Count || windowMaterials[i] == null) continue;

                Color c = Lights ? NeonCycle(i * 0.9f) : WindowYellow;
                float g = 2.5f + (i == add - 1 ? stepPulse * 4f : 0f);
                windowMaterials[i].color = c;
                windowMaterials[i].SetColor("_EmissionColor", c * g);
            }

            // 屋上のネオン：街が明るいほど強く。CITY LIGHTS ではピンクと水色にまわる
            for (int i = 0; i < neonMaterials.Count; i++)
            {
                Color c = Lights ? NeonCycle(i * 1.3f) : neonColors[i];
                float g = 0.6f + 2.4f * level + (Lights ? 1.5f + Mathf.Sin(t * 10f + i) : 0f);
                if (blackoutPulse > 0f) g *= 1f - 0.7f * blackoutPulse;
                neonMaterials[i].color = c;
                neonMaterials[i].SetColor("_EmissionColor", c * g);
            }

            // 車の光：街が明るいほど多く。CITY LIGHTS では速い
            float speedScale = Lights ? 2.6f : 1f;
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                bool show = active && carThresholds[i] < 0.25f + 0.75f * level;
                if (car.gameObject.activeSelf != show) car.gameObject.SetActive(show);
                var p = car.localPosition;
                p.x += carSpeeds[i] * speedScale * Time.deltaTime;
                if (p.x > streetHalf) p.x -= streetHalf * 2f;
                if (p.x < -streetHalf) p.x += streetHalf * 2f;
                car.localPosition = p;
            }

            // 盤面を照らす光
            if (glow != null)
            {
                glow.color = Lights ? NeonCycle(0f) : Color.Lerp(Sunset, Pink, level);
                float flicker = flickerOff ? 0.6f : 1f;
                glow.intensity = glowIntensity * stageShown * (0.25f + 0.75f * level) * flicker * (Lights ? 1.4f : 1f);
                glow.enabled = glow.intensity > 0.001f;
            }
        }

        // ================================================================
        // モニターの画面
        // ================================================================

        void BuildScreen()
        {
            skyBands.Clear();
            screenWindows.Clear();
            decoWindows.Clear();
            decoThresholds.Clear();
            meterCells.Clear();
            screenCars.Clear();
            screenCarSpeeds.Clear();
            if (monitor == null) return;
            screen = monitor.ShowRecordScreen(recordKey, "CITY POP", "ミッドナイト・ドライブ");
            if (screen == null) return;
            float W = bodyW = monitor.RecordBodyW;
            skyW = W - 48f;

            // 上：時計（曲の進み具合）
            monitor.ScreenTag(screen, "ClockTag", 70f, 30f, 96f, 28f, "TIME", 16, Pink, Dark);
            clockText = monitor.ScreenLabel(screen, "Clock", 132f, 30f, 200f, 50f, "19:00", 40, Ink, TextAnchor.MiddleLeft, true);
            monitor.ScreenLabel(screen, "Route", W - 24f, 30f, 420f, 40f, "ROUTE 198X ─ NEON AVENUE", 20, Mute, TextAnchor.MiddleRight, true);

            // まん中：夕焼けの空と街並み
            var skyImg = monitor.ScreenImage(screen, "Sky", W * 0.5f, SkyTop + SkyH * 0.5f, skyW, SkyH, NightBands[0]);
            skyImg.gameObject.AddComponent<RectMask2D>();
            var sky = skyImg.rectTransform;

            int bandCount = DayBands.Length;
            float bandH = SkyH * 0.72f / bandCount;
            for (int i = 0; i < bandCount; i++)
                skyBands.Add(monitor.ScreenImage(sky, $"Band{i}", skyW * 0.5f, bandH * (i + 0.5f), skyW, bandH + 1f, DayBands[i]));
            // 一番下の帯の下（街の後ろ）
            skyBands.Add(monitor.ScreenImage(sky, "BandLow", skyW * 0.5f, SkyH * 0.72f + SkyH * 0.14f, skyW, SkyH * 0.28f + 1f, DayBands[bandCount - 1]));

            // 沈んでいく太陽
            var sunImg = monitor.ScreenImage(sky, "Sun", skyW * 0.32f, SkyH * 0.55f, 150f, 150f, new Color(1f, 0.85f, 0.5f, 0.9f), MonitorDisplay.ScreenShape.Circle);
            sun = sunImg.rectTransform;

            BuildScreenSkyline(sky);

            // 道と車の光
            monitor.ScreenImage(sky, "Road", skyW * 0.5f, SkyH - 14f, skyW, 28f, new Color32(0x0a, 0x09, 0x14, 0xff));
            for (int i = 0; i < 10; i++)
            {
                bool head = i % 2 == 0;
                var c = monitor.ScreenImage(sky, head ? "Head" : "Tail", skyW * i / 10f, SkyH - (head ? 20f : 8f), 34f, 4f,
                                            head ? new Color(1f, 0.95f, 0.8f) : new Color(1f, 0.25f, 0.35f), MonitorDisplay.ScreenShape.Pill);
                screenCars.Add(c.rectTransform);
                screenCarSpeeds.Add((head ? 1f : -1f) * (120f + 60f * (float)rng.NextDouble()));
            }

            // 今の加算（空の右上に大きく）
            addText = monitor.ScreenLabel(sky, "Add", skyW - 30f, 92f, 320f, 130f, "", 110, Ink, TextAnchor.MiddleRight, true);
            var outline = addText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
            outline.effectDistance = new Vector2(3f, -3f);
            addSmall = monitor.ScreenLabel(sky, "AddSmall", skyW - 34f, 160f, 320f, 30f, "pt ／ 中央の穴 1枚", 18, Ink, TextAnchor.MiddleRight);

            // CITY LIGHTS の札
            lightsTag = monitor.ScreenGroup(sky, "LightsTag", skyW * 0.5f, 60f, 420f, 70f);
            monitor.ScreenTag(lightsTag, "Bg", 210f, 35f, 420f, 64f, "", 10, Pink, Dark);
            lightsText = monitor.ScreenLabel(lightsTag, "Text", 210f, 35f, 420f, 64f, "", 36, Dark, TextAnchor.MiddleCenter, true);
            lightsTag.gameObject.SetActive(false);

            // 下：ネオンのメーター（明かりの数）
            monitor.ScreenLabel(screen, "MeterLabel", 24f, MeterY, 120f, 36f, "ネオン", 22, Ink, TextAnchor.MiddleLeft);
            float cellsW = W - BarLeft - 24f;
            float cellGap = 6f;
            float cellW = (cellsW - cellGap * (MaxAdd - 1)) / MaxAdd;
            for (int i = 0; i < MaxAdd; i++)
            {
                float cx = BarLeft + cellW * 0.5f + i * (cellW + cellGap);
                meterCells.Add(monitor.ScreenImage(screen, $"Cell{i}", cx, MeterY, cellW, 26f, new Color(1f, 1f, 1f, 0.1f)));
            }

            // 次の明かり・消灯まで
            float half = (W - BarLeft - 24f - 30f) * 0.5f;
            nextLabel = monitor.ScreenLabel(screen, "NextLabel", 24f, BarY, 120f, 30f, "次の明かり", 18, Mute, TextAnchor.MiddleLeft);
            monitor.ScreenImage(screen, "NextBg", BarLeft + half * 0.5f, BarY, half, 8f, new Color(1f, 1f, 1f, 0.12f));
            nextFill = monitor.ScreenImage(screen, "NextFill", BarLeft + half * 0.5f, BarY, half, 8f, Cyan);
            nextFill.type = Image.Type.Filled;
            nextFill.fillMethod = Image.FillMethod.Horizontal;

            float idleX = BarLeft + half + 30f;
            idleLabel = monitor.ScreenLabel(screen, "IdleLabel", idleX, BarY - 18f, half, 24f, "消灯まで", 16, Mute, TextAnchor.MiddleLeft);
            monitor.ScreenImage(screen, "IdleBg", idleX + half * 0.5f, BarY + 4f, half, 8f, new Color(1f, 1f, 1f, 0.12f));
            idleFill = monitor.ScreenImage(screen, "IdleFill", idleX + half * 0.5f, BarY + 4f, half, 8f, WindowYellow);
            idleFill.type = Image.Type.Filled;
            idleFill.fillMethod = Image.FillMethod.Horizontal;

            // 一番下：ルールの案内
            statusText = monitor.ScreenLabel(screen, "Status", 24f, StatusY, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        /// <summary>モニターの街並み：ビルのシルエットと窓（明かりの窓は盤面と同じ数）。</summary>
        void BuildScreenSkyline(RectTransform sky)
        {
            var rand = new System.Random(4);
            int n = Mathf.Max(6, Mathf.FloorToInt(skyW / 70f));
            var widths = new float[n];
            float sum = 0f;
            for (int i = 0; i < n; i++) { widths[i] = 0.6f + (float)rand.NextDouble() * 0.8f; sum += widths[i]; }

            var levelSlots = new List<List<Image>>();
            float x = 0f;
            float groundY = SkyH - 28f;
            for (int i = 0; i < n; i++)
            {
                float w = skyW * widths[i] / sum;
                float h = 110f + (float)rand.NextDouble() * 150f;
                float cx = x + w * 0.5f;
                monitor.ScreenImage(sky, $"B{i}", cx, groundY - h * 0.5f, w - 4f, h, BuildingColor);

                // 窓：小さな四角を並べる。各ビルの何個かは「明かり」の窓（大きめ）にする
                var slots = new List<Image>();
                int cols = Mathf.Max(1, Mathf.FloorToInt((w - 14f) / 16f));
                int rows = Mathf.Max(1, Mathf.FloorToInt((h - 20f) / 20f));
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        float wx = cx + (c - (cols - 1) * 0.5f) * 16f;
                        float wy = groundY - h + 18f + r * 20f;
                        var img = monitor.ScreenImage(sky, "W", wx, wy, 8f, 9f, new Color(1f, 1f, 1f, 0.06f));
                        if ((r + c * 2 + i) % 5 == 0) slots.Add(img);
                        else
                        {
                            decoWindows.Add(img);
                            decoThresholds.Add((float)rand.NextDouble());
                        }
                    }
                }
                levelSlots.Add(slots);
                x += w;
            }

            // 明かりの窓を、ビルを順にまわって maxAdd 個えらぶ（盤面と同じように、街ぜんたいに広がる）
            for (int level = 0; screenWindows.Count < MaxAdd; level++)
            {
                bool any = false;
                for (int j = 0; j < n && screenWindows.Count < MaxAdd; j++)
                {
                    int b = (n / 2 + j * Stride(n)) % n;
                    var slots = levelSlots[b];
                    int idx = slots.Count - 1 - level;   // 下の階から
                    if (idx < 0) continue;
                    var img = slots[idx];
                    if (screenWindows.Contains(img)) continue;
                    img.rectTransform.sizeDelta = new Vector2(11f, 12f);
                    screenWindows.Add(img);
                    any = true;
                }
                if (!any && level > 40) break;   // 窓が足りない時（とても細い画面）
            }
        }

        void UpdateScreen()
        {
            if (!active || screen == null) return;
            float t = Time.time;
            float level = Mathf.Clamp01(add / (float)MaxAdd);
            bool flickerOff = FlickerOff();

            // 時計：曲の始まりが 19:00、終わりが 24:00
            float progress = effects.CurrentDuration > 0f ? Mathf.Clamp01(1f - effects.Remaining / effects.CurrentDuration) : 0f;
            int minutes = 19 * 60 + Mathf.FloorToInt(progress * 300f);
            clockText.text = minutes >= 24 * 60 ? "24:00" : $"{minutes / 60}:{minutes % 60:00}";

            // 空：夕焼けから夜へ。CITY LIGHTS ではネオンの色にまわる
            for (int i = 0; i < skyBands.Count; i++)
            {
                int k = Mathf.Min(i, DayBands.Length - 1);
                Color c = Color.Lerp(DayBands[k], NightBands[k], progress);
                if (Lights) c = Color.Lerp(c, NeonCycle(i * 0.6f), 0.55f);
                skyBands[i].color = c;
            }
            float sunY = Mathf.Lerp(SkyH * 0.45f, SkyH + 60f, progress);
            sun.anchoredPosition = new Vector2(sun.anchoredPosition.x, -sunY);

            // 窓
            for (int i = 0; i < screenWindows.Count; i++)
            {
                bool lit = i < add && !(i == add - 1 && flickerOff);
                Color c = Lights ? NeonCycle(i * 0.9f) : WindowYellow;
                if (lit && i == add - 1) c = Color.Lerp(c, Color.white, stepPulse * 0.7f);
                screenWindows[i].color = lit ? c : new Color(1f, 1f, 1f, 0.1f);
            }
            for (int i = 0; i < decoWindows.Count; i++)
            {
                bool lit = decoThresholds[i] < level * 0.6f;
                Color c = Lights ? NeonCycle(i * 0.3f) : new Color(1f, 0.85f, 0.45f, 0.55f);
                decoWindows[i].color = lit ? c : new Color(1f, 1f, 1f, 0.05f);
            }

            // 車の光
            float speedScale = Lights ? 2.6f : 1f;
            for (int i = 0; i < screenCars.Count; i++)
            {
                var p = screenCars[i].anchoredPosition;
                p.x = Mathf.Repeat(p.x + screenCarSpeeds[i] * speedScale * Time.deltaTime + 40f, skyW + 80f) - 40f;
                screenCars[i].anchoredPosition = p;
            }

            // 今の加算
            Color addColor = Lights ? NeonCycle(0f) : Warning ? Color.Lerp(Ink, Warn, 0.5f + 0.5f * Mathf.Sin(t * 14f)) : Ink;
            addText.text = $"+{add}";
            addText.color = addColor;
            float s = 1f + 0.25f * stepPulse * stepPulse - 0.12f * blackoutPulse;
            addText.rectTransform.localScale = new Vector3(s, s, 1f);
            addSmall.text = Lights ? $"pt ／ 1枚　×{lightsMultiplier:0.#}" : "pt ／ 中央の穴 1枚";

            // CITY LIGHTS の札
            if (lightsTag.gameObject.activeSelf != Lights) lightsTag.gameObject.SetActive(Lights);
            if (Lights)
            {
                lightsText.text = $"CITY LIGHTS ×{lightsMultiplier:0.#}  {Mathf.CeilToInt(lightsTimer)}";
                float ls = 1f + 0.05f * Mathf.Sin(t * 8f);
                lightsTag.localScale = new Vector3(ls, ls, 1f);
            }

            // ネオンのメーター
            for (int i = 0; i < meterCells.Count; i++)
            {
                Color c;
                if (i < add)
                {
                    bool off = i == add - 1 && flickerOff;
                    c = Lights ? NeonCycle(i * 0.5f) : Color.Lerp(Pink, Cyan, i / (float)Mathf.Max(1, meterCells.Count - 1));
                    if (off) c = new Color(c.r, c.g, c.b, 0.25f);
                }
                else if (i == add && !Lights)
                {
                    float f = growTimer / Mathf.Max(0.1f, stepSeconds);
                    c = new Color(1f, 1f, 1f, 0.1f + 0.25f * f);
                }
                else c = new Color(1f, 1f, 1f, 0.1f);
                meterCells[i].color = c;
            }

            // 次の明かり・消灯まで
            nextFill.fillAmount = Lights ? lightsTimer / Mathf.Max(0.1f, lightsSeconds) : growTimer / Mathf.Max(0.1f, stepSeconds);
            nextFill.color = Lights ? NeonCycle(0f) : Cyan;
            nextLabel.text = Lights ? "CITY LIGHTS" : add >= MaxAdd ? "満タン" : "次の明かり";

            float idleLeft = Mathf.Max(0f, idleSeconds - idleTimer);
            idleFill.fillAmount = Lights ? 1f : idleLeft / Mathf.Max(0.1f, idleSeconds);
            idleFill.color = Warning ? Color.Lerp(Warn, Ink, 0.5f + 0.5f * Mathf.Sin(t * 14f)) : WindowYellow;
            idleLabel.text = Lights ? "消灯しない" : add <= minAdd ? "消灯まで（これ以上は消えない）" : $"消灯まで {idleLeft:0.0}秒";
            idleLabel.color = Warning ? Warn : Mute;

            // 案内
            statusText.text = Lights
                ? $"<color=#FF4FA3>CITY LIGHTS！</color>　中央の穴のポイント ×{lightsMultiplier:0.#}　残り {Mathf.CeilToInt(lightsTimer)}秒"
                : Warning
                    ? "<color=#FF4D4D>ネオンが切れそう！</color>　メダルを落として明かりを守ろう"
                    : $"{stepSeconds:0}秒ごとに明かりが増える（最大 +{MaxAdd}）　{idleSeconds:0}秒落ちないと消灯";
        }

        void Pop(string text, Color color, Vector2 bodyPos)
        {
            if (monitor == null || screen == null) return;
            monitor.PopText(text, color, monitor.RecordBodyToCanvas(bodyPos));
        }

        // ================================================================
        // 道具
        // ================================================================

        static GameObject MakeCube(string name, Transform parent, Vector3 localPosition, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());   // 見た目だけ。メダルには当たらない
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static Material MakeGlowMaterial(Color color, float glow)
        {
            var m = ProtoAssets.MakeMaterial(color, 0f, 0.5f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * glow);
            return m;
        }

        // ================================================================
        // 音（差し替えの音が無ければ、コードで作る仮の音）
        // ================================================================

        void PlaySound(AudioClip clip, ref AudioClip made, Func<AudioClip> make)
        {
            if (clip == null)
            {
                if (made == null) made = make();
                clip = made;
            }
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip, volume);
        }

        // エレピの和音（F メジャー7）をころころと
        static AudioClip MakeStep() => EPiano("CityPopStep", new[] { 349.23f, 440f, 523.25f, 659.25f }, 0.05f, 0.9f, 0.22f);
        // CITY LIGHTS：高い和音を大きく
        static AudioClip MakeLights() => EPiano("CityPopLights", new[] { 349.23f, 440f, 523.25f, 659.25f, 698.46f, 880f, 1046.5f, 1318.5f }, 0.06f, 1.6f, 0.2f);

        /// <summary>エレピのような丸い音を順に鳴らす。step は音と音の間、tail は最後の音の長さ。</summary>
        static AudioClip EPiano(string name, float[] freqs, float step, float tail, float gain)
        {
            const int rate = 44100;
            float seconds = step * (freqs.Length - 1) + tail;
            var data = new float[Mathf.Max(1, (int)(rate * seconds))];
            for (int k = 0; k < freqs.Length; k++)
            {
                int start = (int)(rate * step * k);
                int n = data.Length - start;
                for (int i = 0; i < n; i++)
                {
                    float tt = i / (float)rate;
                    float env = Mathf.Exp(-tt * 3.2f) * Mathf.Min(1f, i / 120f);
                    float ph = 2f * Mathf.PI * freqs[k] * tt;
                    // 少し金属っぽい倍音と、ゆれ（トレモロ）
                    float wave = Mathf.Sin(ph + 0.6f * Mathf.Sin(ph * 2f) * Mathf.Exp(-tt * 6f));
                    float trem = 1f - 0.15f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 5.5f * tt));
                    data[start + i] += wave * env * trem * gain;
                }
            }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>ネオン管がジジッと鳴る音（チカチカし始めた時）。</summary>
        static AudioClip MakeWarn()
        {
            const int rate = 44100;
            var data = new float[(int)(rate * 0.5f)];
            for (int i = 0; i < data.Length; i++)
            {
                float tt = i / (float)rate;
                float saw = Mathf.Repeat(tt * 120f, 1f) * 2f - 1f;
                float chop = Mathf.PerlinNoise(tt * 40f, 0.5f) > 0.5f ? 1f : 0.15f;   // 途切れ途切れ
                data[i] = saw * chop * 0.18f * Mathf.Min(1f, (0.5f - tt) * 10f);
            }
            var clip = AudioClip.Create("CityPopWarn", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>明かりが消える音（下がっていく音と、バチッという音）。</summary>
        static AudioClip MakeBlackout()
        {
            const int rate = 44100;
            var data = new float[(int)(rate * 0.6f)];
            var rand = new System.Random(5);
            float phase = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float tt = i / (float)rate;
                float freq = Mathf.Lerp(900f, 90f, tt / 0.6f);
                phase += 2f * Mathf.PI * freq / rate;
                float tone = Mathf.Sin(phase) * Mathf.Exp(-tt * 4f) * 0.3f;
                float crack = tt < 0.05f ? (float)(rand.NextDouble() * 2.0 - 1.0) * (1f - tt / 0.05f) * 0.5f : 0f;
                data[i] = tone + crack;
            }
            var clip = AudioClip.Create("CityPopBlackout", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
