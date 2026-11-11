#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define AM_NEW_INPUT
#endif
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if AM_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace MedalPusher
{
    /// <summary>
    /// エリアの始まりに流すムービー（今はエリア1：ボリューム・ゼロの「区画紹介」）。
    ///
    /// 【使い方】
    ///   MainScene の Hierarchy で右クリック → Create Empty → 名前を「AreaMovie」にして、このスクリプトを付ける。
    ///
    /// 【しくみ】
    ///   ・エリア1が始まった時に流す（約21秒）。MainScene に来た時と、エリア1からやり直した時。
    ///     続きからエリア2以降で始めた時は流れない
    ///   ・流している間は Time.timeScale を 0 にしてゲームを止める。
    ///     だから、もともとあるモニターのエリア演出は、このムービーが終わってから続きが流れる
    ///   ・素材は Resources/AreaMovies/Area1 から読む
    ///   ・Space かクリックでスキップ（1回目で案内が出て、2回目でスキップ）
    ///   ・ドットのフィルター：ArcadeCamera と同じように、ムービーをいったん低い解像度に描いてから
    ///     画面いっぱいに引きのばし、ArcadeScreen のシェーダーで仕上げる
    /// </summary>
    public class AreaMovieDirector : MonoBehaviour
    {
        [Header("■ 再生")]
        [Tooltip("どのエリアの始まりに流すか（0 がエリア1）")]
        public int areaIndex = 0;
        [Tooltip("素材を読む場所（Resources フォルダの中）")]
        public string resourceFolder = "AreaMovies/Area1";
        [Tooltip("流している間、ゲームを止める（もともとのモニターの演出は、ムービーの後に流れる）")]
        public bool pauseGame = true;
        [Tooltip("Space / クリックでスキップできるようにする")]
        public bool allowSkip = true;
        [Tooltip("画面の手前に出す順番（大きいほど手前）")]
        public int sortingOrder = 5000;

        [Header("■ 音を自動で読みこむ")]
        [Tooltip("オンにすると、曲・効果音の欄が空の時に、Resources の audioFolder から自動で読みこむ")]
        public bool autoLoadAudio = true;
        [Tooltip("自動で読みこむ場所（Resources フォルダの中）")]
        public string audioFolder = "AreaMovies/Area1/Audio/Intro";

        [Header("■ ドットのフィルター")]
        [Tooltip("オンにすると、ゲーム本編と同じようなドットの見た目になる")]
        public bool dotFilter = true;
        [Tooltip("オンにすると、ゲームのカメラの ArcadeCamera から、ドットの細かさとマテリアルを借りる（本編と同じ見た目になる）")]
        public bool useGameCameraSettings = true;
        [Tooltip("ムービーを描く縦のドット数。小さいほどドットが粗くなる（上がオンの時は、ゲームのカメラの値を使う）")]
        [Range(120, 1080)]
        public int pixelHeight = 550;
        [Tooltip("ArcadeScreen シェーダーを使ったマテリアル。空ならゲームのカメラの物を使う")]
        public Material screenMaterial;

        [Header("■ 音（空のままでも動く）")]
        [Tooltip("ムービーの曲と効果音")]
        public MovieSound sound = new MovieSound(new[]
        {
            ("風の音", 0f), ("ネオンの「ジジッ」", 3.5f), ("サーチライトの「ガチャン」", 6.2f),
            ("つまみが回る「カリカリ」", 12.5f), ("つまみが 0 になる「カチッ」", 14.3f), ("タイトルの「ドン」", 17.15f),
        });

        [Header("■ 動きの調整")]
        [Range(0f, 1f), Tooltip("画面がピカッと光る強さ")]
        public float flashStrength = 0.8f;
        [Tooltip("画面が揺れる強さ（ピクセル）")]
        public float shakeStrength = 22f;
        [Range(0f, 1f), Tooltip("ブラウン管の横線の濃さ")]
        public float scanlineAlpha = 0.3f;

        // =====================================================================
        // 入れ物
        // =====================================================================

        [System.Serializable]
        public class MovieSound
        {
            [Tooltip("ムービーの間ずっと流す曲（空なら流さない）")]
            public AudioClip bgm;
            [Range(0f, 1f)] public float bgmVolume = 0.8f;
            [Tooltip("効果音の予定表。clip に音を入れるだけで、その秒数に鳴る")]
            public List<OpeningSoundCue> cues = new List<OpeningSoundCue>();

            public MovieSound() { }
            public MovieSound((string memo, float time)[] defaults)
            {
                foreach (var d in defaults) cues.Add(new OpeningSoundCue { memo = d.memo, time = d.time });
            }
        }

        private class Layer
        {
            public RawImage img;
            public RectTransform rt;
        }

        private class Cut
        {
            public string name;
            public float start, end;
            public RectTransform root;
            public readonly Dictionary<string, Layer> layers = new Dictionary<string, Layer>();
            public float Duration => Mathf.Max(0.01f, end - start);
        }

        private struct Hit
        {
            public float time, flash, shake;
            public Hit(float t, float f, float s) { time = t; flash = f; shake = s; }
        }

        // =====================================================================
        // 中で使う値
        // =====================================================================

        private const float W = 1920f, H = 1080f;
        private readonly List<Cut> cuts = new List<Cut>();
        private readonly List<Hit> hits = new List<Hit>();
        private readonly List<float> dipTimes = new List<float>();
        private GameObject movieCanvasGo;     // ムービー本体（ドットのフィルターの時は、低い解像度に描かれる）
        private GameObject displayCanvasGo;   // 画面に出す板とスキップの文字
        private GameObject movieCameraGo;
        private Camera movieCamera;
        private RenderTexture renderTexture;
        private RawImage screenImage;
        private RectTransform stage;
        private RawImage flash, fade, scan;
        private Text skipText;
        private AudioSource bgmSource, seSource;
        private float time, prevTime, total;
        private bool playing, skipping;
        private float skipTimer, skipArmed;
        private float savedTimeScale = 1f;
        private bool paused;
        private int activeCut = -1;
        private int lastScreenW, lastScreenH;
        private MedalPusherGame game;
        private GameState lastState = (GameState)(-1);
        private bool startupChecked;

        // =====================================================================
        // 始まり・終わり
        // =====================================================================

        private void Awake()
        {
            game = FindFirstObjectByType<MedalPusherGame>();
            if (game != null) game.StateChanged += OnStateChanged;
            else PlayMovie();   // ゲームが無いシーンでは、すぐに流す
        }

        private void Start()
        {
            // シーンの始まりの合図は、聞きのがすことがあるので、1フレーム後に自分で確かめる
            if (game != null) StartCoroutine(StartupCheck());
        }

        /// <summary>
        /// シーンが始まって1フレーム後に、今エリア1の演出中かを確かめて、そうなら流す。
        /// （ゲームはシーンが始まったとたんに「演出中」の合図を出すので、順番によっては合図を聞きのがすため。
        ///   1フレーム待つのは、続きからの時にセーブの読みこみでエリアが変わるのを待つため）
        /// </summary>
        private System.Collections.IEnumerator StartupCheck()
        {
            yield return null;
            startupChecked = true;
            bool areaStart = game.State == GameState.Intro || game.State == GameState.Warmup;
            if (!playing && areaStart && game.AreaIndex == areaIndex) PlayMovie();
        }

        /// <summary>エリアが始まった時（演出中になった時）に、そのエリアなら流す</summary>
        private void OnStateChanged(GameState state)
        {
            GameState previous = lastState;
            lastState = state;
            if (playing || game == null) return;
            if (!startupChecked) return;   // 最初の1フレームは StartupCheck に任せる（2回流れないように）
            bool areaStart = state == GameState.Intro || (state == GameState.Warmup && previous != GameState.Intro);
            if (areaStart && game.AreaIndex == areaIndex) PlayMovie();
        }

        /// <summary>ムービーを流す</summary>
        public void PlayMovie()
        {
            if (playing) return;
            cuts.Clear(); hits.Clear(); dipTimes.Clear();
            activeCut = -1; skipping = false; skipArmed = 0f;

            // ゲームを止める（モニターの演出が先に進まないように）
            if (pauseGame)
            {
                savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
                paused = true;
            }

            if (dotFilter && useGameCameraSettings)
            {
                ArcadeCamera arcade = FindFirstObjectByType<ArcadeCamera>();
                if (arcade != null)
                {
                    pixelHeight = arcade.pixelHeight;
                    if (screenMaterial == null) screenMaterial = arcade.screenMaterial;
                }
            }
            if (dotFilter && screenMaterial == null)
            {
                Shader shader = Shader.Find("MedalGame/ArcadeScreen");
                if (shader != null) screenMaterial = new Material(shader) { name = "ArcadeScreen (ムービー用)" };
            }

            BuildCanvas();
            BuildMovie();
            total = cuts.Count > 0 ? cuts[cuts.Count - 1].end : 0f;
            LoadAutoAudio(sound, "se_");

            time = prevTime = 0f;
            playing = true;
            if (sound != null && sound.bgm != null)
            {
                bgmSource.clip = sound.bgm;
                bgmSource.volume = sound.bgmVolume;
                bgmSource.Play();
            }
            ApplyFrame();
        }

        private void Update()
        {
            if (!playing) return;
            float dt = Time.unscaledDeltaTime;
            if (dotFilter && (Screen.width != lastScreenW || Screen.height != lastScreenH)) MakeRenderTexture();

            if (skipping)
            {
                skipTimer += dt;
                ApplyFrame();
                if (skipTimer >= 0.35f) Finish();
                return;
            }

            if (allowSkip) HandleSkip(dt);
            if (skipping) return;

            prevTime = time;
            time += dt;
            if (time >= total)
            {
                Finish();
                return;
            }
            ApplyFrame();
        }

        private void HandleSkip(float dt)
        {
            if (skipArmed > 0f) skipArmed -= dt;
            if (PressedSkip())
            {
                if (skipArmed > 0f) { skipping = true; skipTimer = 0f; prevTime = time; }
                else skipArmed = 2f;
            }
            if (skipText != null)
            {
                skipText.text = skipArmed > 0f ? "もう一度押すとスキップ" : "Space / クリック：スキップ";
                Color c = skipText.color;
                c.a = skipArmed > 0f ? 0.95f : 0.4f;
                skipText.color = c;
            }
        }

        private static bool PressedSkip()
        {
#if AM_NEW_INPUT
            Keyboard k = Keyboard.current;
            bool key = k != null && k.spaceKey.wasPressedThisFrame;
#else
            bool key = Input.GetKeyDown(KeyCode.Space);
#endif
            return key || InputCompat.MouseDown();
        }

        private void Finish()
        {
            if (!playing) return;
            playing = false;
            if (bgmSource != null) bgmSource.Stop();
            CleanUp();
            RestoreTime();
        }

        private void CleanUp()
        {
            if (movieCamera != null) movieCamera.targetTexture = null;
            if (screenImage != null) screenImage.texture = null;
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
            if (movieCanvasGo != null) Destroy(movieCanvasGo);
            if (displayCanvasGo != null) Destroy(displayCanvasGo);
            if (movieCameraGo != null) Destroy(movieCameraGo);
            if (bgmSource != null) Destroy(bgmSource);
            if (seSource != null) Destroy(seSource);
        }

        private void RestoreTime()
        {
            if (!paused) return;
            paused = false;
            if (Time.timeScale == 0f) Time.timeScale = savedTimeScale;
        }

        private void OnDisable()
        {
            // 途中で消された時も、ゲームが止まったままにならないように
            RestoreTime();
        }

        private void OnDestroy()
        {
            if (game != null) game.StateChanged -= OnStateChanged;
            CleanUp();
        }

        // =====================================================================
        // 毎フレームの見た目
        // =====================================================================

        /// <summary>
        /// 曲・効果音の欄が空なら、Resources の audioFolder から自動で読みこむ。
        /// 曲は bgm、効果音は「sePrefix + 01」から、予定表の上から順番に対応する。自分で入れた音がある欄は、そちらを使う。
        /// </summary>
        private void LoadAutoAudio(AreaMovieDirector.MovieSound s, string sePrefix)
        {
            if (!autoLoadAudio || s == null) return;
            if (s.bgm == null) s.bgm = Resources.Load<AudioClip>(audioFolder + "/bgm");
            for (int i = 0; i < s.cues.Count; i++)
            {
                OpeningSoundCue cue = s.cues[i];
                if (cue != null && cue.clip == null) cue.clip = Resources.Load<AudioClip>($"{audioFolder}/{sePrefix}{i + 1:00}");
            }
        }

        private void ApplyFrame()
        {
            int index = cuts.Count - 1;
            for (int i = 0; i < cuts.Count; i++) if (time < cuts[i].end) { index = i; break; }
            if (index != activeCut)
            {
                for (int i = 0; i < cuts.Count; i++) cuts[i].root.gameObject.SetActive(i == index);
                activeCut = index;
            }

            if (index >= 0)
            {
                Cut c = cuts[index];
                float lt = time - c.start;
                float u = Mathf.Clamp01(lt / c.Duration);
                ResetCut(c);
                Animate(c, lt, u);
            }

            // 暗転：始まり・カットの切りかわり・終わり・スキップ
            float f = 1f - Smooth(time / 0.3f);
            foreach (float d in dipTimes) f = Mathf.Max(f, 1f - Mathf.Abs(time - d) / 0.15f);
            f = Mathf.Max(f, Smooth((time - (total - 0.5f)) / 0.5f));
            if (skipping) f = Mathf.Max(f, Mathf.Clamp01(skipTimer / 0.35f));
            SetAlpha(fade, Mathf.Clamp01(f));

            // 光と揺れ
            float fl = 0f, sh = 0f;
            foreach (Hit h in hits)
            {
                if (time < h.time) continue;
                float k = Mathf.Exp(-(time - h.time) * 5f);
                fl += h.flash * k;
                sh += h.shake * k;
            }
            SetAlpha(flash, Mathf.Clamp01(fl * flashStrength));
            float amount = sh * shakeStrength;
            stage.anchoredPosition = new Vector2((Mathf.PerlinNoise(time * 28f, 0.3f) - 0.5f) * 2f * amount,
                                                 (Mathf.PerlinNoise(0.7f, time * 28f) - 0.5f) * 2f * amount);
            SetAlpha(scan, scanlineAlpha);

            // 音
            if (sound != null && seSource != null && !skipping)
            {
                foreach (OpeningSoundCue cue in sound.cues)
                {
                    if (cue != null && cue.clip != null && prevTime < cue.time && cue.time <= time) seSource.PlayOneShot(cue.clip, cue.volume);
                }
            }
            if (bgmSource != null && sound != null)
            {
                float v = sound.bgmVolume * (1f - Smooth((time - (total - 0.8f)) / 0.8f));
                if (skipping) v *= 1f - Mathf.Clamp01(skipTimer / 0.35f);
                bgmSource.volume = v;
            }
        }

        // =====================================================================
        // ムービーの中身（区画紹介）
        // =====================================================================

        private void BuildMovie()
        {
            AddCut("street", 0f, 6f, "B/sky", "B/city_far", "B/city_near", "B/street", "B/signs", "B/label");
            AddCut("roof", 6f, 11f, "B/roof_bg", "B/light", "B/boss_small");
            AddCut("close", 11f, 17f, "B/close_bg", "B/boss_big", "B/knob_face", "B/knob_numbers", "B/knob_cap", "Common/boss_box", "Common/boss_text");
            AddCut("title", 17f, 21f, "Common/title_bg", "Common/title_text");
            dipTimes.Add(6f);
            dipTimes.Add(11f);
            hits.Add(new Hit(11f + 3.3f, 0.3f, 0.8f));   // つまみが 0 になった
            hits.Add(new Hit(17.15f, 0.9f, 1f));         // タイトル
            SetLayerRecursively(movieCanvasGo, LayerMask.NameToLayer("UI"));
        }

        private void Animate(Cut c, float lt, float u)
        {
            switch (c.name)
            {
                case "street":
                {
                    // 灰色の街を、奥ほどゆっくり、手前ほど速く横に流す
                    Move(L(c, "B/sky"), new Vector2(-(u - 0.5f) * 20f, 0f));
                    Move(L(c, "B/city_far"), new Vector2(-(u - 0.5f) * 50f, 0f));
                    Move(L(c, "B/city_near"), new Vector2(-(u - 0.5f) * 90f, 0f));
                    Move(L(c, "B/street"), new Vector2(-(u - 0.5f) * 120f, 0f));
                    // 消えたネオンが、ときどき一瞬だけ光りかける
                    Layer signs = L(c, "B/signs");
                    Move(signs, new Vector2(-(u - 0.5f) * 90f, 0f));
                    bool blink = Mathf.Repeat(lt * 0.9f, 1f) > 0.82f && Mathf.Repeat(lt * 13f, 1f) < 0.5f;
                    SetAlpha(signs, blink ? 0.55f : 0.04f);
                    Zoom(c, 1.08f, Vector2.zero);
                    // 左上の「AREA 1 音の消えた区画」
                    Layer label = L(c, "B/label");
                    float a = EaseOut((lt - 0.6f) / 0.5f);
                    Move(label, new Vector2((1f - a) * -60f, 0f));
                    SetAlpha(label, a);
                    break;
                }

                case "roof":
                {
                    // サーチライトがさまよって、屋上のボスを照らす
                    Layer light = L(c, "B/light");
                    float ang = Mathf.Lerp(-14f, 0f, EaseOut((lt - 0.3f) / 1.8f));
                    Pose(light, Vector2.zero, ang, 1f, new Vector2(-645f, -560f));
                    SetAlpha(light, Smooth((lt - 0.2f) / 0.3f));
                    Zoom(c, Mathf.Lerp(1f, 1.18f, Smooth(u)), new Vector2(220f, -60f));
                    break;
                }

                case "close":
                {
                    Pose(L(c, "B/boss_big"), Vector2.zero, 0f, Mathf.Lerp(1.05f, 1f, EaseOut(lt / 1.5f)), new Vector2(340f, -500f));
                    float faceA = Smooth((lt - 0.2f) / 0.4f);
                    SetAlpha(L(c, "B/knob_face"), faceA);
                    SetAlpha(L(c, "B/knob_numbers"), faceA);
                    // つまみが 10 から 0 まで回る
                    float v = lt < 1.5f ? 10f : Mathf.Lerp(10f, 0f, Smooth((lt - 1.5f) / 1.8f));
                    Layer cap = L(c, "B/knob_cap");
                    Pose(cap, Vector2.zero, -27f * v, 1f, new Vector2(-440f, 20f));
                    SetAlpha(cap, faceA);
                    CaptionIn(L(c, "Common/boss_box"), L(c, "Common/boss_text"), lt, 3.0f, 3.3f, 1.0f);
                    break;
                }

                case "title":
                {
                    // AREA 1 がドンと出る
                    Layer t = L(c, "Common/title_text");
                    float e = Mathf.Clamp01(lt / 0.35f);
                    Pose(t, Vector2.zero, 0f, Mathf.Lerp(2.2f, 1f, EaseBack(e)), Vector2.zero);
                    SetAlpha(t, Smooth(e / 0.4f));
                    Pose(L(c, "Common/title_bg"), new Vector2((1f - EaseOut(lt / 0.4f)) * -400f, 0f), 0f, 1.25f, Vector2.zero);
                    break;
                }
            }
        }

        /// <summary>せりふ：枠がふわっと出て、文字が左から順に見えていく</summary>
        private static void CaptionIn(Layer box, Layer text, float lt, float boxAt, float textAt, float textSeconds)
        {
            float b = Smooth((lt - boxAt) / 0.3f);
            Move(box, new Vector2(0f, (1f - b) * -24f));
            SetAlpha(box, b);
            float p = Mathf.Clamp01((lt - textAt) / Mathf.Max(0.01f, textSeconds));
            Reveal(text, 230f, 1730f, p);
        }

        // =====================================================================
        // 組み立て
        // =====================================================================

        private void BuildCanvas()
        {
            // ---------- ムービー本体の Canvas ----------
            movieCanvasGo = new GameObject("AreaMovieCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            movieCanvasGo.transform.SetParent(transform, false);
            Canvas movieCanvas = movieCanvasGo.GetComponent<Canvas>();
            CanvasScaler scaler = movieCanvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(W, H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (dotFilter)
            {
                // ムービー専用のカメラで、低い解像度の板（RenderTexture）に描く
                movieCameraGo = new GameObject("AreaMovieCamera");
                movieCameraGo.transform.SetParent(transform, false);
                movieCamera = movieCameraGo.AddComponent<Camera>();
                movieCamera.clearFlags = CameraClearFlags.SolidColor;
                movieCamera.backgroundColor = Color.black;
                movieCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
                movieCamera.orthographic = true;
                movieCamera.depth = -50;
                movieCamera.useOcclusionCulling = false;
                movieCamera.allowHDR = false;
                movieCamera.allowMSAA = false;
                movieCamera.nearClipPlane = 0.1f;
                movieCamera.farClipPlane = 10f;
                movieCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                movieCanvas.worldCamera = movieCamera;
                movieCanvas.planeDistance = 1f;
            }
            else
            {
                movieCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                movieCanvas.sortingOrder = sortingOrder;
            }
            RectTransform root = (RectTransform)movieCanvasGo.transform;

            RawImage back = MakeRaw(root, null, "Backdrop", Color.black);
            Stretch(back.rectTransform);

            GameObject st = new GameObject("Stage", typeof(RectTransform));
            st.transform.SetParent(root, false);
            stage = (RectTransform)st.transform;
            stage.sizeDelta = new Vector2(W, H);

            scan = MakeRaw(root, Load("Common/scanlines"), "Scanlines", Color.white);
            Stretch(scan.rectTransform);
            RawImage vignette = MakeRaw(root, Load("Common/vignette"), "Vignette", Color.white);
            Stretch(vignette.rectTransform);
            flash = MakeRaw(root, null, "Flash", new Color(1f, 1f, 1f, 0f));
            Stretch(flash.rectTransform);
            fade = MakeRaw(root, null, "Fade", Color.black);
            Stretch(fade.rectTransform);

            // ---------- 画面に出す Canvas（ドットの板と、スキップの文字） ----------
            displayCanvasGo = new GameObject("AreaMovieDisplay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            displayCanvasGo.transform.SetParent(transform, false);
            Canvas display = displayCanvasGo.GetComponent<Canvas>();
            display.renderMode = RenderMode.ScreenSpaceOverlay;
            display.sortingOrder = sortingOrder + 1;
            CanvasScaler dScaler = displayCanvasGo.GetComponent<CanvasScaler>();
            dScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            dScaler.referenceResolution = new Vector2(W, H);
            dScaler.matchWidthOrHeight = 0.5f;
            RectTransform droot = (RectTransform)displayCanvasGo.transform;

            // 画面いっぱいの板（クリックがゲームに届かないようにする役目もある）
            screenImage = MakeRaw(droot, null, "Screen", dotFilter ? Color.white : new Color(0f, 0f, 0f, 0f));
            Stretch(screenImage.rectTransform);
            screenImage.raycastTarget = true;
            if (dotFilter)
            {
                if (screenMaterial != null) screenImage.material = screenMaterial;
                MakeRenderTexture();
            }

            GameObject tGo = new GameObject("SkipText", typeof(RectTransform), typeof(Text));
            tGo.transform.SetParent(droot, false);
            skipText = tGo.GetComponent<Text>();
            skipText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            skipText.fontSize = 24;
            skipText.alignment = TextAnchor.LowerRight;
            skipText.color = new Color(1f, 1f, 1f, 0.4f);
            skipText.raycastTarget = false;
            skipText.text = allowSkip ? "Space / クリック：スキップ" : "";
            RectTransform srt = skipText.rectTransform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1f, 0f);
            srt.anchoredPosition = new Vector2(-40f, 24f);
            srt.sizeDelta = new Vector2(600f, 40f);

            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.playOnAwake = false;
            seSource = gameObject.AddComponent<AudioSource>();
            seSource.playOnAwake = false;
        }

        /// <summary>画面の縦横の比に合わせて、低い解像度の板を作り直す</summary>
        private void MakeRenderTexture()
        {
            lastScreenW = Screen.width;
            lastScreenH = Screen.height;
            if (movieCamera == null) return;

            movieCamera.targetTexture = null;
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
            int h = Mathf.Max(1, pixelHeight);
            int w = Mathf.Max(1, Mathf.RoundToInt(h * (float)Screen.width / Mathf.Max(1, Screen.height)));
            renderTexture = new RenderTexture(w, h, 24)
            {
                name = "AreaMovie",
                filterMode = FilterMode.Point,   // 引きのばす時にぼかさない（ドットに見える）
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
            };
            renderTexture.Create();
            movieCamera.targetTexture = renderTexture;
            screenImage.texture = renderTexture;
        }

        private Cut AddCut(string name, float start, float end, params string[] files)
        {
            GameObject go = new GameObject("Cut_" + name, typeof(RectTransform));
            go.transform.SetParent(stage, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(W, H);
            Cut c = new Cut { name = name, start = start, end = end, root = rt };
            foreach (string f in files)
            {
                RawImage img = MakeRaw(rt, Load(f), f, Color.white);
                img.rectTransform.sizeDelta = new Vector2(W, H);
                if (img.texture == null) img.enabled = false;
                c.layers[f] = new Layer { img = img, rt = img.rectTransform };
            }
            go.SetActive(false);
            cuts.Add(c);
            return c;
        }

        private Texture2D Load(string file)
        {
            Texture2D tex = Resources.Load<Texture2D>(resourceFolder + "/" + file);
            if (tex == null) Debug.LogWarning($"[エリアムービー] 素材が見つかりません：Resources/{resourceFolder}/{file}");
            return tex;
        }

        private static RawImage MakeRaw(RectTransform parent, Texture tex, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            RawImage img = go.GetComponent<RawImage>();
            img.texture = tex;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null || layer < 0) return;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        /// <summary>毎フレーム、カットの全レイヤーを「止まっている状態」に戻す</summary>
        private static void ResetCut(Cut c)
        {
            c.root.anchoredPosition = Vector2.zero;
            c.root.localScale = Vector3.one;
            foreach (Layer l in c.layers.Values)
            {
                l.rt.pivot = new Vector2(0.5f, 0.5f);
                l.rt.anchoredPosition = Vector2.zero;
                l.rt.sizeDelta = new Vector2(W, H);
                l.rt.localRotation = Quaternion.identity;
                l.rt.localScale = Vector3.one;
                l.img.uvRect = new Rect(0f, 0f, 1f, 1f);
                l.img.color = Color.white;
            }
        }

        // =====================================================================
        // 便利な道具（座標は、画面の真ん中が 0,0、上がプラス）
        // =====================================================================

        private static Layer L(Cut c, string name)
        {
            c.layers.TryGetValue(name, out Layer l);
            return l;
        }

        /// <summary>ずらす・回す・大きくする。pivot はその中心</summary>
        private static void Pose(Layer l, Vector2 offset, float angle, float scale, Vector2 pivot)
        {
            if (l == null) return;
            Quaternion rot = Quaternion.Euler(0f, 0f, angle);
            Vector2 center = pivot + (Vector2)(rot * (Vector3)(-pivot * scale)) + offset;
            l.rt.anchoredPosition = center;
            l.rt.localRotation = rot;
            l.rt.localScale = new Vector3(scale, scale, 1f);
        }

        private static void Move(Layer l, Vector2 offset) => Pose(l, offset, 0f, 1f, Vector2.zero);

        /// <summary>画像の x0〜x1（画面の左はしから数えたピクセル）の範囲を、左から p の割合だけ見せる</summary>
        private static void Reveal(Layer l, float x0, float x1, float p)
        {
            if (l == null) return;
            float edge = p >= 1f ? W : Mathf.Lerp(x0, x1, Mathf.Clamp01(p));
            float wShow = Mathf.Max(0f, edge);
            l.rt.pivot = new Vector2(0f, 0.5f);
            l.rt.anchoredPosition = new Vector2(-W * 0.5f, l.rt.anchoredPosition.y);
            l.rt.sizeDelta = new Vector2(wShow, H);
            l.img.uvRect = new Rect(0f, 0f, wShow / W, 1f);
            if (p <= 0f) SetAlpha(l, 0f);
        }

        private static void Zoom(Cut c, float scale, Vector2 focus)
        {
            c.root.localScale = new Vector3(scale, scale, 1f);
            c.root.anchoredPosition = focus * (1f - scale);
        }

        private static void SetAlpha(Layer l, float a)
        {
            if (l == null) return;
            SetAlpha(l.img, a);
        }

        private static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            Color c = g.color;
            c.a = Mathf.Clamp01(a);
            g.color = c;
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static float EaseOut(float x)
        {
            x = Mathf.Clamp01(x);
            float a = 1f - x;
            return 1f - a * a * a;
        }

        private static float EaseBack(float x)
        {
            x = Mathf.Clamp01(x);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float a = x - 1f;
            return 1f + c3 * a * a * a + c1 * a * a;
        }
    }
}
