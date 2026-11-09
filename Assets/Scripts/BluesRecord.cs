using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N003 ブルース「哀愁をためて、ソロで返す」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：照明が青く暗くなる（RecordVisualEffects の青い照明をそのまま使う）。奥の壁の向こうに雨が降る。
    ///   端の穴の手前のふちが青く光り、端の穴に落ちると強く光る
    /// ・モニター：夜の酒場。カウンターのグラスに、青いメダル（ブルーノート）がたまっていく。
    ///   ソロの時は、右側がギターソロの画面になり、グラスのメダルが1枚ずつ音になって払い出される
    /// ・ルール：
    ///   端の穴に落ちた時と、idleSeconds 秒のあいだどの穴にも落ちなかった時に、ブルーノートが1枚たまる（最大 maxNotes）
    ///   次のどれかで「ソロ」：残り投入数が lastInserts 以下（LAST 5）・グラスがいっぱい・曲の残りが endSeconds 秒以下
    ///   ソロの間は、soloInterval 秒ごとに1枚ずつ +pointsPerNote pt を払い出す（1枚ごとにギターの音が鳴る）
    ///   LAST 5 と曲の終わりの間は、ソロが続く（たまったそばから払い出す）
    ///   曲が終わった時にグラスに残っていた分は、まとめて払い出す
    /// ・ポイントが入るのは、ラウンドのプレイ中（判定待ちを含む）だけ
    ///
    /// 見た目と音はコードで作る仮のもの。Inspector の「差し替え用」に Prefab・画像・音を入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。
    /// CSV の「効果の型」は Scripted にしておく（前の効果と二重にならないように）。
    /// </summary>
    [DisallowMultipleComponent]
    public class BluesRecord : MonoBehaviour
    {
        /// <summary>シーンにあるブルースのスクリプト（RecordVisualEffects が、前の仮の演出を止めるのに使う）</summary>
        public static BluesRecord Instance { get; private set; }

        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        public RecordEffectSystem effects;

        [Header("レコード")]
        [Tooltip("このスクリプトが受け持つレコードのキー（英名）")]
        public string recordKey = "Blues";

        [Header("ブルーノート（グラスにたまる青いメダル）")]
        [Tooltip("グラスにたまる最大の数（いっぱいになるとソロ）")]
        [Range(1, 40)] public int maxNotes = 20;
        [Tooltip("この秒数のあいだ、どの穴にも落ちなかったら1枚たまる")]
        [Range(1f, 30f)] public float idleSeconds = 5f;

        [Header("ソロ")]
        [Tooltip("1枚ごとに払い出すポイント")]
        [Range(1, 200)] public int pointsPerNote = 20;
        [Tooltip("1枚払い出す間かく（秒）")]
        [Range(0.05f, 2f)] public float soloInterval = 0.3f;
        [Tooltip("残り投入数がこの数以下になるとソロ（LAST 5）")]
        [Range(0, 30)] public int lastInserts = 5;
        [Tooltip("曲の残りがこの秒数以下になるとソロ")]
        [Range(0f, 60f)] public float endSeconds = 10f;

        [Header("盤面")]
        public Color glowColor = new Color(0.3f, 0.5f, 1f);
        [Tooltip("雨の量（1秒あたりの粒の数）")]
        [Range(0f, 600f)] public float rainAmount = 160f;
        public Color rainColor = new Color(0.65f, 0.78f, 1f, 0.35f);

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("雨の音の大きさ")]
        [Range(0f, 1f)] public float rainVolume = 0.12f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        [Tooltip("ブルーノートがたまった音（グラスに落ちる音）")]
        public AudioClip noteSound;
        [Tooltip("ソロが始まった音")]
        public AudioClip soloStartSound;
        [Tooltip("ソロで1枚払い出すたびの音（順にくり返す。空なら仮のギターの音）")]
        public AudioClip[] soloNoteSounds = new AudioClip[0];
        [Tooltip("雨の音（くり返し流す）")]
        public AudioClip rainSound;
        [Tooltip("雨の Prefab（空なら仮のパーティクル）。盤面の奥の壁の向こうに置く")]
        public GameObject rainPrefab;
        [Tooltip("ソロの画面のギターの絵（空なら仮の絵）")]
        public Sprite guitarSprite;

        // ---- 状態 ----
        bool active;
        int notes;
        float idleTimer;
        bool soloing;
        float soloTimer;
        int soloStep;            // ギターのフレーズの何音目か
        int paidThisSong;
        float glowFlash;
        readonly System.Random rng = new System.Random();

        // ---- 盤面 ----
        Transform boardRoot;
        readonly List<Material> glowMaterials = new List<Material>();
        ParticleSystem rain;
        GameObject rainInstance;
        float boardShown;
        float D => game.tuning.medalDiameter;

        // ---- 音 ----
        AudioSource audioSource, rainSource;
        AudioClip madeNote, madeSoloStart, madeRain;
        AudioClip[] madeSoloNotes;
        // A のブルーススケール（A C D E♭ E G）を、2オクターブ上がって下りるフレーズ
        static readonly float[] SoloScale =
        {
            220.00f, 261.63f, 293.66f, 311.13f, 329.63f, 392.00f,
            440.00f, 523.25f, 587.33f, 622.25f, 659.26f, 783.99f, 880.00f
        };

        // ---- モニター ----
        MonitorDisplay monitor;
        RectTransform screen, room, soloPanel;
        CanvasGroup soloGroup;
        Text countText, lastText, timeText, statusText, soloTitle;
        readonly List<Image> glassNotes = new List<Image>();
        readonly List<RectTransform> rainLines = new List<RectTransform>();
        readonly List<Image> chips = new List<Image>();
        readonly List<Text> chipTexts = new List<Text>();
        readonly List<RectTransform> fretStrings = new List<RectTransform>();
        Image signGlow, glassFill;
        float bodyW = 800f;
        float soloShown;
        float roomW;
        float windowW = 400f;
        // 払い出した音符（フレットの上を上がって消える）
        readonly List<RectTransform> flyNotes = new List<RectTransform>();
        readonly List<float> flyAge = new List<float>();
        const float RoomTop = 64f;
        const float RoomH = 390f;
        const float GlassX = 200f, GlassBottom = 312f, GlassW = 150f, GlassH = 210f;
        const float NoteH = 9f;
        const float SoloLeft = 380f;     // ソロの画面の左（部屋の座標）
        const float ChipY = 492f, ChipH = 60f, ChipGap = 16f;
        const float StatusY = 566f;
        static readonly Color RoomBg = new Color32(0x10, 0x14, 0x26, 0xff);
        static readonly Color Wood = new Color32(0x4a, 0x2e, 0x22, 0xff);
        static readonly Color WoodLight = new Color32(0x6b, 0x44, 0x30, 0xff);
        static readonly Color NoteBlue = new Color32(0x3c, 0x7c, 0xff, 0xff);
        static readonly Color NoteLight = new Color32(0x9c, 0xc4, 0xff, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Dark = new Color32(0x0a, 0x0c, 0x16, 0xff);

        /// <summary>シーンに無ければ、RecordEffectSystem の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<BluesRecord>() != null) return;
            var system = FindFirstObjectByType<RecordEffectSystem>();
            if (system != null) system.gameObject.AddComponent<BluesRecord>();
        }

        void Awake() => Instance = this;

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (effects == null) effects = FindFirstObjectByType<RecordEffectSystem>();
            if (game == null || effects == null)
            {
                Debug.LogWarning("BluesRecord: MedalPusherGame か RecordEffectSystem が見つからないため、ブルースの効果は動きません。", this);
                enabled = false;
                return;
            }

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            rainSource = gameObject.AddComponent<AudioSource>();
            rainSource.playOnAwake = false;
            rainSource.spatialBlend = 0f;
            rainSource.loop = true;
            rainSource.volume = 0f;

            effects.RecordPlayed += OnRecordPlayed;
            effects.RecordFinished += OnRecordFinished;
            game.MedalDropped += OnMedalDropped;

            BuildBoard();
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
            if (boardRoot != null) Destroy(boardRoot.gameObject);
        }

        bool IsMine(RecordData r) => r != null && r.key == recordKey;
        bool IsScoring => game.State == GameState.Playing || game.State == GameState.Grace;
        int InsertsLeft => Mathf.Max(0, game.InsertLimit - game.InsertedThisRound);
        bool LastZone => IsScoring && InsertsLeft <= lastInserts;
        bool EndZone => effects.CurrentDuration > 0f && effects.Remaining <= endSeconds;
        bool Full => notes >= maxNotes;

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
            notes = 0;
            idleTimer = 0f;
            soloing = false;
            soloShown = 0f;
            soloStep = 0;
            paidThisSong = 0;
            monitor = game.Monitor;
            BuildScreen();
        }

        void End()
        {
            // グラスに残っていた分は、まとめて払い出す
            if (active && notes > 0 && IsScoring)
            {
                int pts = notes * pointsPerNote;
                effects.AddPoints(pts);
                Pop($"ラストソロ +{pts}", NoteLight, new Vector2(GlassX + 24f, RoomTop + GlassBottom - GlassH - 30f));
            }
            notes = 0;
            active = false;
            soloing = false;
            if (monitor != null) monitor.HideRecordScreen(recordKey);
            screen = null;
        }

        // ================================================================
        // ゲームからの通知
        // ================================================================

        void OnMedalDropped(Medal medal, DropZoneType type)
        {
            if (!active || type == DropZoneType.Lost) return;
            idleTimer = 0f;   // どこかの穴に落ちた
            if (type == DropZoneType.Side && IsScoring)
            {
                glowFlash = 1f;
                AddNote("端の穴…");
            }
        }

        void AddNote(string reason)
        {
            if (notes >= maxNotes) return;
            notes++;
            PlaySound(noteSound, ref madeNote, MakeNoteSound);
            Pop($"{reason} ♪+1", NoteLight, new Vector2(GlassX + 24f, RoomTop + GlassBottom - GlassH - 10f));
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
                // しばらくどの穴にも落ちないと、1枚たまる
                idleTimer += Time.deltaTime;
                if (idleTimer >= idleSeconds)
                {
                    idleTimer = 0f;
                    AddNote("沈黙…");
                }

                // ソロ：始まり・払い出し・終わり
                bool want = notes > 0 && (Full || LastZone || EndZone);
                if (!soloing && want) StartSolo();
                if (soloing)
                {
                    soloTimer -= Time.deltaTime;
                    if (soloTimer <= 0f && notes > 0)
                    {
                        soloTimer = soloInterval;
                        PayNote();
                    }
                    if (notes <= 0 && soloTimer <= 0f) soloing = false;
                }
            }
            glowFlash = Mathf.MoveTowards(glowFlash, 0f, Time.deltaTime * 2f);

            UpdateBoard();
            UpdateScreen();
        }

        void StartSolo()
        {
            soloing = true;
            soloTimer = 0.6f;    // 最初の1音の前に、少しためる
            PlaySound(soloStartSound, ref madeSoloStart, MakeSoloStart);
            Pop("SOLO!", NoteLight, new Vector2(SoloLeft + (roomW - SoloLeft) * 0.5f + 24f, RoomTop + 70f));
            if (SpeakerUnit.Main != null) SpeakerUnit.Main.Pulse(1f);
        }

        void PayNote()
        {
            notes--;
            effects.AddPoints(pointsPerNote);
            paidThisSong += pointsPerNote;
            PlaySoloNote();
            if (SpeakerUnit.Main != null) SpeakerUnit.Main.Pulse(0.5f);
            SpawnFlyNote();
        }

        // ================================================================
        // 盤面：端の穴のふちの光・雨
        // ================================================================

        void BuildBoard()
        {
            boardRoot = new GameObject("BluesBoard").transform;

            // 端の穴の手前のふち（左右）
            float half = game.layout.boardWidth * 0.5f;
            float hole = Mathf.Clamp(game.layout.centerHoleWidth, 0.1f, game.layout.boardWidth) * 0.5f;
            float w = Mathf.Max(0.05f, half - hole);
            float z = game.layout.floorFrontZ - D * 0.05f;
            foreach (float side in new[] { -1f, 1f })
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = side < 0f ? "SideGlowLeft" : "SideGlowRight";
                Destroy(go.GetComponent<Collider>());   // 見た目だけ。メダルには当たらない
                go.transform.SetParent(boardRoot, false);
                go.transform.position = new Vector3(side * (hole + w * 0.5f), D * 0.06f, z);
                go.transform.localScale = new Vector3(w * 0.92f, D * 0.05f, D * 0.06f);
                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                var m = ProtoAssets.MakeMaterial(glowColor, 0f, 0.5f);
                m.EnableKeyword("_EMISSION");
                r.sharedMaterial = m;
                glowMaterials.Add(m);
            }

            // 雨（奥の壁の向こう。ふつうのカメラで描くので、筐体やモニターの後ろにかくれる）
            var rainPos = new Vector3(0f, game.layout.wallHeight * 2.2f, game.BackWallFrontZ + D * 1.2f);
            if (rainPrefab != null)
            {
                rainInstance = Instantiate(rainPrefab, rainPos, Quaternion.identity, boardRoot);
            }
            else
            {
                rain = BuildRain(rainPos);
            }

            boardRoot.gameObject.SetActive(false);
        }

        ParticleSystem BuildRain(Vector3 position)
        {
            var go = new GameObject("BluesRain");
            go.transform.SetParent(boardRoot, false);
            go.transform.position = position;
            // パーティクルを前に出す仕組み（レイヤー 0 だけを動かす）に乗らないように、TransparentFX にしておく
            go.layer = 1;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float fall = game.layout.wallHeight * 1.3f;   // 壁の高さあたりまで落ちたら消える
            float speed = D * 9f;
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = fall / speed;
            main.startSpeed = 0f;
            main.startSize = D * 0.03f;
            main.startColor = rainColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;

            var emission = ps.emission;
            emission.rateOverTime = rainAmount;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(game.layout.boardWidth * 1.6f, 0.1f, D * 1.5f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-D * 0.6f, -D * 0.4f);   // 少しななめに
            vel.y = new ParticleSystem.MinMaxCurve(-speed * 1.1f, -speed * 0.9f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 2f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit"));
            return ps;
        }

        void UpdateBoard()
        {
            if (boardRoot == null) return;
            boardShown = Mathf.MoveTowards(boardShown, active ? 1f : 0f, Time.deltaTime * 2f);
            bool visible = boardShown > 0f;
            if (boardRoot.gameObject.activeSelf != visible) boardRoot.gameObject.SetActive(visible);

            // 雨（音も）
            if (rain != null)
            {
                if (active && !rain.isEmitting) rain.Play();
                if (!active && rain.isEmitting) rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                var emission = rain.emission;
                emission.rateOverTime = rainAmount;
            }
            if (rainInstance != null && rainInstance.activeSelf != active) rainInstance.SetActive(active);
            UpdateRainSound();
            if (!visible) return;

            float t = Time.time;
            float pulse = soloing ? 1.6f + 0.8f * Mathf.Sin(t * 10f) : 0.7f + 0.25f * Mathf.Sin(t * 1.5f);
            foreach (var m in glowMaterials)
            {
                m.color = glowColor;
                m.SetColor("_EmissionColor", glowColor * (pulse + glowFlash * 3f) * boardShown);
            }
        }

        void UpdateRainSound()
        {
            if (rainSource == null) return;
            float target = active ? rainVolume : 0f;
            if (target > 0f && !rainSource.isPlaying)
            {
                if (rainSound == null && madeRain == null) madeRain = MakeRain();
                rainSource.clip = rainSound != null ? rainSound : madeRain;
                rainSource.Play();
            }
            rainSource.volume = Mathf.MoveTowards(rainSource.volume, target, Time.deltaTime * 0.3f);
            if (rainSource.isPlaying && rainSource.volume <= 0f && target <= 0f) rainSource.Stop();
        }

        // ================================================================
        // モニターの画面
        // ================================================================

        void BuildScreen()
        {
            if (monitor == null) return;
            screen = monitor.ShowRecordScreen(recordKey, "BLUES", "哀愁をためて、ソロで返す");
            if (screen == null) return;
            float W = bodyW = monitor.RecordBodyW;
            roomW = W - 48f;

            // 上：ブルーノートの数・LAST・曲の残り
            monitor.ScreenLabel(screen, "CountLabel", 24f, 30f, 180f, 36f, "ブルーノート", 24, Ink, TextAnchor.MiddleLeft);
            countText = monitor.ScreenLabel(screen, "Count", 200f, 30f, 200f, 44f, "", 34, NoteLight, TextAnchor.MiddleLeft, true);
            timeText = monitor.ScreenLabel(screen, "Time", W - 24f, 30f, 260f, 40f, "", 24, Mute, TextAnchor.MiddleRight);
            lastText = monitor.ScreenLabel(screen, "Last", W - 300f, 30f, 260f, 44f, "", 34, NoteLight, TextAnchor.MiddleRight, true);

            // まん中：夜の酒場
            var roomImg = monitor.ScreenImage(screen, "Room", W * 0.5f, RoomTop + RoomH * 0.5f, roomW, RoomH, RoomBg);
            roomImg.gameObject.AddComponent<RectMask2D>();
            room = roomImg.rectTransform;

            // 窓と雨
            float winL = SoloLeft + 40f, winR = Mathf.Min(roomW - 300f, SoloLeft + 420f);
            float winW = Mathf.Max(160f, winR - winL), winCX = winL + winW * 0.5f;
            windowW = winW;
            monitor.ScreenImage(room, "WindowFrame", winCX, 130f, winW + 16f, 196f, Wood);
            var win = monitor.ScreenImage(room, "Window", winCX, 130f, winW, 180f, new Color32(0x1c, 0x2a, 0x4c, 0xff));
            win.gameObject.AddComponent<RectMask2D>();
            rainLines.Clear();
            for (int i = 0; i < 26; i++)
            {
                float x = (float)rng.NextDouble() * winW;
                var line = monitor.ScreenImage(win.rectTransform, $"Rain{i}", x, (float)rng.NextDouble() * 180f, 2f, 26f, new Color(0.7f, 0.82f, 1f, 0.45f));
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f);
                rainLines.Add(line.rectTransform);
            }
            monitor.ScreenImage(room, "WindowBarV", winCX, 130f, 6f, 180f, Wood);
            monitor.ScreenImage(room, "WindowBarH", winCX, 130f, winW, 6f, Wood);

            // 酒びんの棚
            float shelfL = winL + winW + 40f, shelfR = roomW - 30f;
            if (shelfR - shelfL > 80f)
            {
                monitor.ScreenImage(room, "Shelf", (shelfL + shelfR) * 0.5f, 200f, shelfR - shelfL, 10f, WoodLight);
                int n = Mathf.FloorToInt((shelfR - shelfL) / 44f);
                Color[] bottle = { new Color32(0x2f, 0x6b, 0x4a, 0xff), new Color32(0x7a, 0x3a, 0x22, 0xff), new Color32(0x3a, 0x4c, 0x8c, 0xff), new Color32(0x8c, 0x7a, 0x3a, 0xff) };
                for (int i = 0; i < n; i++)
                {
                    float x = shelfL + 22f + i * 44f;
                    float h = 70f + (i * 37 % 30);
                    monitor.ScreenImage(room, $"Bottle{i}", x, 195f - h * 0.5f, 26f, h, bottle[i % bottle.Length], MonitorDisplay.ScreenShape.Pill);
                    monitor.ScreenImage(room, $"Neck{i}", x, 195f - h - 10f, 10f, 24f, bottle[i % bottle.Length]);
                }
            }

            // ネオンの看板
            signGlow = monitor.ScreenImage(room, "SignGlow", 190f, 56f, 280f, 70f, new Color(0.25f, 0.45f, 1f, 0.25f), MonitorDisplay.ScreenShape.Pill);
            monitor.ScreenLabel(room, "Sign", 190f, 56f, 280f, 60f, "BLUE NOTE", 40, NoteLight, TextAnchor.MiddleCenter, true);

            // カウンター
            monitor.ScreenImage(room, "Counter", roomW * 0.5f, GlassBottom + 34f, roomW, 68f, Wood);
            monitor.ScreenImage(room, "CounterTop", roomW * 0.5f, GlassBottom + 2f, roomW, 6f, WoodLight);

            // グラス（青いメダルが下から積もる）
            monitor.ScreenImage(room, "GlassBack", GlassX, GlassBottom - GlassH * 0.5f, GlassW, GlassH, new Color(1f, 1f, 1f, 0.06f));
            glassFill = monitor.ScreenImage(room, "GlassFill", GlassX, GlassBottom - GlassH * 0.5f, GlassW - 10f, GlassH - 10f, new Color(0.3f, 0.5f, 1f, 0.12f));
            glassNotes.Clear();
            int max = Mathf.Max(1, maxNotes);
            float step = Mathf.Min(NoteH, (GlassH - 16f) / max);
            for (int i = 0; i < max; i++)
            {
                float x = GlassX + ((i % 3) - 1) * 6f;
                float y = GlassBottom - 10f - i * step;
                var n = monitor.ScreenImage(room, $"Note{i}", x, y, GlassW - 34f, Mathf.Max(5f, step + 1f), NoteBlue, MonitorDisplay.ScreenShape.Pill);
                n.enabled = false;
                glassNotes.Add(n);
            }
            monitor.ScreenImage(room, "GlassL", GlassX - GlassW * 0.5f, GlassBottom - GlassH * 0.5f, 4f, GlassH, new Color(1f, 1f, 1f, 0.5f));
            monitor.ScreenImage(room, "GlassR", GlassX + GlassW * 0.5f, GlassBottom - GlassH * 0.5f, 4f, GlassH, new Color(1f, 1f, 1f, 0.5f));
            monitor.ScreenImage(room, "GlassB", GlassX, GlassBottom, GlassW + 4f, 6f, new Color(1f, 1f, 1f, 0.5f));

            // ソロの画面（部屋の右側にかぶせる）
            float sw = roomW - SoloLeft;
            soloPanel = monitor.ScreenGroup(room, "Solo", SoloLeft + sw * 0.5f, RoomH * 0.5f, sw, RoomH);
            soloGroup = soloPanel.gameObject.AddComponent<CanvasGroup>();
            soloGroup.alpha = 0f;
            monitor.ScreenImage(soloPanel, "Bg", sw * 0.5f, RoomH * 0.5f, sw, RoomH, Dark);
            monitor.ScreenImage(soloPanel, "Spot", sw * 0.5f, RoomH * 0.55f, Mathf.Min(sw, 520f), 340f, new Color(0.3f, 0.5f, 1f, 0.18f), MonitorDisplay.ScreenShape.Circle);
            // フレット（6本の弦。払い出した音が上を流れる）
            fretStrings.Clear();
            float fretL = 40f, fretR = sw - 40f;
            monitor.ScreenImage(soloPanel, "Neck", (fretL + fretR) * 0.5f, 250f, fretR - fretL, 120f, new Color32(0x2a, 0x1a, 0x12, 0xff));
            for (int i = 0; i < 6; i++)
            {
                var s = monitor.ScreenImage(soloPanel, $"String{i}", (fretL + fretR) * 0.5f, 200f + i * 20f, fretR - fretL, 2f + i * 0.4f, new Color(0.85f, 0.85f, 0.9f, 0.8f));
                fretStrings.Add(s.rectTransform);
            }
            for (float x = fretL + 60f; x < fretR; x += 70f)
                monitor.ScreenImage(soloPanel, "Fret", x, 250f, 3f, 120f, new Color(0.75f, 0.7f, 0.55f, 0.7f));
            // ギター（仮の絵、または差し替えの絵）
            if (guitarSprite != null)
            {
                var g = monitor.ScreenImage(soloPanel, "Guitar", sw - 120f, 110f, 180f, 180f, Color.white);
                g.sprite = guitarSprite;
                g.preserveAspect = true;
            }
            else
            {
                monitor.ScreenImage(soloPanel, "Body1", sw - 110f, 120f, 120f, 120f, new Color32(0x2a, 0x5c, 0xc8, 0xff), MonitorDisplay.ScreenShape.Circle);
                monitor.ScreenImage(soloPanel, "Body2", sw - 110f, 60f, 90f, 90f, new Color32(0x2a, 0x5c, 0xc8, 0xff), MonitorDisplay.ScreenShape.Circle);
                monitor.ScreenImage(soloPanel, "Hole", sw - 110f, 105f, 34f, 34f, Dark, MonitorDisplay.ScreenShape.Circle);
            }
            soloTitle = monitor.ScreenLabel(soloPanel, "Title", 40f, 80f, 400f, 100f, "SOLO", 84, NoteLight, TextAnchor.MiddleLeft, true);

            // 下：ソロになる3つの条件
            chips.Clear();
            chipTexts.Clear();
            float cw = (W - 48f - ChipGap * 2f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float cx = 24f + cw * 0.5f + i * (cw + ChipGap);
                chips.Add(monitor.ScreenImage(screen, $"Chip{i}", cx, ChipY, cw, ChipH, new Color(1f, 1f, 1f, 0.1f), MonitorDisplay.ScreenShape.Pill));
                chipTexts.Add(monitor.ScreenLabel(screen, $"ChipText{i}", cx, ChipY, cw - 20f, ChipH, "", 22, Ink));
            }

            // 一番下：ルールの案内
            statusText = monitor.ScreenLabel(screen, "Status", 24f, StatusY, W - 48f, 40f, "", 21, Ink, TextAnchor.MiddleLeft);
        }

        void SpawnFlyNote()
        {
            if (soloPanel == null) return;
            float sw = roomW - SoloLeft;
            int s = rng.Next(6);
            float x = 60f + (float)rng.NextDouble() * (sw - 120f);
            var n = monitor.ScreenImage(soloPanel, "PaidNote", x, 200f + s * 20f, 30f, 30f, NoteBlue, MonitorDisplay.ScreenShape.Circle);
            monitor.ScreenLabel(n.rectTransform, "Mark", 15f, 15f, 30f, 30f, "♪", 22, Color.white);
            flyNotes.Add(n.rectTransform);
            flyAge.Add(0f);
            if (s < fretStrings.Count) fretStrings[s].localScale = new Vector3(1f, 3f, 1f);   // 弦がふるえる
            Pop($"+{pointsPerNote}", Gold, new Vector2(24f + SoloLeft + x, RoomTop + 200f + s * 20f - 40f));
        }

        void UpdateScreen()
        {
            if (!active || screen == null) return;
            float t = Time.time;

            // 上の段
            countText.text = $"{notes}<size=22>/{maxNotes}</size>";
            int left = InsertsLeft;
            lastText.text = LastZone ? $"LAST {left}" : "";
            lastText.color = left > 0 ? NoteLight : new Color(1f, 0.4f, 0.4f);
            if (effects.CurrentDuration > 0f)
            {
                int sec = Mathf.CeilToInt(Mathf.Max(0f, effects.Remaining));
                timeText.text = $"曲の残り {sec / 60}:{sec % 60:00}";
            }
            else timeText.text = "";

            // グラス
            for (int i = 0; i < glassNotes.Count; i++)
            {
                bool on = i < notes;
                if (glassNotes[i].enabled != on) glassNotes[i].enabled = on;
                if (on) glassNotes[i].color = Color.Lerp(NoteBlue, NoteLight, 0.25f + 0.25f * Mathf.Sin(t * 2f + i * 0.6f));
            }
            glassFill.color = new Color(0.3f, 0.5f, 1f, Full ? 0.25f + 0.15f * Mathf.Sin(t * 8f) : 0.12f);
            signGlow.color = new Color(0.25f, 0.45f, 1f, 0.18f + 0.1f * Mathf.Sin(t * 3f) + (Mathf.Sin(t * 23f) > 0.97f ? -0.15f : 0f));

            // 窓の雨
            foreach (var r in rainLines)
            {
                var p = r.anchoredPosition;
                p.y -= 420f * Time.deltaTime;   // 下へ（y はマイナスが下）
                p.x -= 90f * Time.deltaTime;
                if (p.y < -200f) { p.y = 10f; p.x = (float)rng.NextDouble() * windowW; }
                if (p.x < -10f) p.x += windowW;
                r.anchoredPosition = p;
            }

            // ソロの画面
            soloShown = Mathf.MoveTowards(soloShown, soloing ? 1f : 0f, Time.deltaTime * 4f);
            soloGroup.alpha = soloShown;
            soloTitle.text = soloing ? "SOLO" : "";
            soloTitle.rectTransform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(t * 8f));
            foreach (var s in fretStrings)
            {
                var sc = s.localScale;
                sc.y = Mathf.MoveTowards(sc.y, 1f, Time.deltaTime * 8f);
                s.localScale = sc;
            }
            for (int i = flyNotes.Count - 1; i >= 0; i--)
            {
                flyAge[i] += Time.deltaTime;
                var n = flyNotes[i];
                if (n == null || flyAge[i] > 1.2f)
                {
                    if (n != null) Destroy(n.gameObject);
                    flyNotes.RemoveAt(i);
                    flyAge.RemoveAt(i);
                    continue;
                }
                var p = n.anchoredPosition;
                p.y += 120f * Time.deltaTime;
                n.anchoredPosition = p;
                var img = n.GetComponent<Image>();
                img.color = new Color(NoteBlue.r, NoteBlue.g, NoteBlue.b, 1f - flyAge[i] / 1.2f);
            }

            // ソロになる3つの条件
            SetChip(0, LastZone, $"残り投入 {lastInserts} 以下<size=18>（いま {left}）</size>");
            SetChip(1, Full, $"グラスがいっぱい<size=18>（{notes}/{maxNotes}）</size>");
            int remain = Mathf.CeilToInt(Mathf.Max(0f, effects.Remaining));
            SetChip(2, EndZone, $"曲の残り {endSeconds:0} 秒<size=18>（いま {remain}秒）</size>");

            statusText.text = soloing
                ? $"<color=#9CC4FF>ソロ！</color>　グラスのブルーノートを 1枚ずつ +{pointsPerNote}pt で払い出し中　（この曲で +{paidThisSong}）"
                : $"端の穴に落ちる・{idleSeconds:0}秒どこにも落ちない → ブルーノートが1枚たまる　下のどれかでソロ（1枚 +{pointsPerNote}pt）";
        }

        void SetChip(int i, bool on, string text)
        {
            if (i >= chips.Count) return;
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            chips[i].color = on ? Color.Lerp(NoteBlue, NoteLight, 0.3f * blink) : new Color(1f, 1f, 1f, 0.1f);
            chipTexts[i].text = text;
            chipTexts[i].color = on ? Color.white : Mute;
        }

        void Pop(string text, Color color, Vector2 bodyPos)
        {
            if (monitor == null || screen == null) return;
            monitor.PopText(text, color, monitor.RecordBodyToCanvas(bodyPos));
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

        /// <summary>ソロの1音（差し替えの音を順に。無ければブルーススケールを上がって下りるギターの音）。</summary>
        void PlaySoloNote()
        {
            AudioClip clip = null;
            if (soloNoteSounds != null && soloNoteSounds.Length > 0) clip = soloNoteSounds[soloStep % soloNoteSounds.Length];
            if (clip == null)
            {
                if (madeSoloNotes == null)
                {
                    madeSoloNotes = new AudioClip[SoloScale.Length];
                    for (int i = 0; i < SoloScale.Length; i++) madeSoloNotes[i] = MakeGuitar(SoloScale[i], 0.9f, 0.5f, i);
                }
                // 上がって下りる（0,1,2,…,12,11,…,1,0,1,…）
                int span = SoloScale.Length - 1;
                int k = soloStep % (span * 2);
                int idx = k <= span ? k : span * 2 - k;
                clip = madeSoloNotes[idx];
            }
            soloStep++;
            if (clip != null) audioSource.PlayOneShot(clip, volume);
        }

        /// <summary>ブルーノートがたまった音（グラスの「チン」と、低い E♭ の弦の音）。</summary>
        static AudioClip MakeNoteSound()
        {
            const int rate = 44100;
            var pluck = PluckData(155.56f, 1.2f, 0.4f, 7);
            var data = new float[pluck.Length];
            for (int i = 0; i < data.Length; i++)
            {
                float tt = i / (float)rate;
                float clink = Mathf.Sin(2f * Mathf.PI * 2637f * tt) * Mathf.Exp(-tt * 18f) * 0.18f
                            + Mathf.Sin(2f * Mathf.PI * 3951f * tt) * Mathf.Exp(-tt * 25f) * 0.08f;
                data[i] = clink + (tt > 0.05f ? pluck[i - (int)(rate * 0.05f)] : 0f);
            }
            var clip = AudioClip.Create("BluesNote", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>ソロの始まり（A の和音を、ひずませてジャーン）。</summary>
        static AudioClip MakeSoloStart()
        {
            const int rate = 44100;
            float seconds = 1.8f;
            var mix = new float[(int)(rate * seconds)];
            float[] chord = { 110.00f, 164.81f, 220.00f, 277.18f, 329.63f };
            for (int k = 0; k < chord.Length; k++)
            {
                var d = PluckData(chord[k], seconds, 0.35f, 40 + k);
                int offset = (int)(rate * 0.02f * k);
                for (int i = 0; i + offset < mix.Length; i++) mix[i + offset] += d[i];
            }
            for (int i = 0; i < mix.Length; i++) mix[i] = Overdrive(mix[i], 2.5f) * 0.5f;
            var clip = AudioClip.Create("BluesSoloStart", mix.Length, 1, rate, false);
            clip.SetData(mix, 0);
            return clip;
        }

        /// <summary>少しひずんだギターの1音。</summary>
        static AudioClip MakeGuitar(float frequency, float seconds, float gain, int seed)
        {
            var data = PluckData(frequency, seconds, gain, 300 + seed);
            for (int i = 0; i < data.Length; i++) data[i] = Overdrive(data[i], 3f) * 0.45f;
            var clip = AudioClip.Create($"BluesSolo_{frequency:0}", data.Length, 1, 44100, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Overdrive(float x, float drive)
        {
            float y = x * drive;
            return y / (1f + Mathf.Abs(y));
        }

        /// <summary>雨の音（ざーっという音。くり返しても目立たないように、はじめと終わりをつなぐ）。</summary>
        static AudioClip MakeRain()
        {
            const int rate = 44100;
            int length = rate * 3;
            var data = new float[length];
            var rand = new System.Random(5);
            float low = 0f;
            for (int i = 0; i < length; i++)
            {
                float noise = (float)(rand.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.35f;
                float drop = rand.NextDouble() < 0.0006 ? 0.8f : 0f;   // ときどき、ぽつっ
                data[i] = (low * 0.5f + drop * noise) * 0.6f;
            }
            // つなぎ目：終わりの 0.2 秒を、はじめに重ねる
            int fade = rate / 5;
            for (int i = 0; i < fade; i++)
            {
                float a = i / (float)fade;
                data[i] = data[i] * a + data[length - fade + i] * (1f - a);
            }
            Array.Resize(ref data, length - fade);
            var clip = AudioClip.Create("BluesRain", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>弦をはじいた音（Karplus-Strong）。</summary>
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
    }
}
