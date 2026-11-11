#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define AMB_NEW_INPUT
#endif
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if AMB_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace MedalPusher
{
    /// <summary>
    /// エリアのムービー（始まり・ボス撃破）の共通の土台。エリア2から使う。
    /// 中身（どの絵をどう動かすか）は、これを受けついだスクリプト（Area2IntroMovie など）に書く。
    ///
    /// この土台がやること：
    ///   ・ゲームの状態を見て、決まった時にムービーを流す（いつ流すかは ShouldPlay で決める）
    ///   ・流している間は Time.timeScale を 0 にしてゲームを止める
    ///   ・ドットのフィルター（ArcadeCamera と同じ見た目）
    ///   ・Space / クリックでスキップ（1回目で案内、2回目でスキップ）
    ///   ・曲と効果音を Resources から自動で読みこんで鳴らす
    ///   ・暗転・白い光・画面の揺れ
    /// </summary>
    public abstract class AreaMovieBase : MonoBehaviour
    {
        [Header("■ デバッグ用")]
        [Tooltip("チェックを入れると、このスクリプトを付けたシーンに来た時に、条件に関係なくムービーを流す（本番では外しておく）")]
        public bool debugPlayOnSceneStart = false;

        [Header("■ 再生")]
        [Tooltip("どのエリアで流すか（0 がエリア1、1 がエリア2）。-1 なら決まった値を使う")]
        public int areaIndex = -1;
        [Tooltip("素材を読む場所（Resources フォルダの中）。空なら決まった場所")]
        public string resourceFolder = "";
        [Tooltip("流している間、ゲームを止める（もともとの演出や帯は、ムービーの後に出る）")]
        public bool pauseGame = true;
        [Tooltip("Space / クリックでスキップできるようにする")]
        public bool allowSkip = true;
        [Tooltip("画面の手前に出す順番（大きいほど手前）")]
        public int sortingOrder = 5000;

        [Header("■ 音を自動で読みこむ")]
        [Tooltip("オンにすると、曲・効果音の欄が空の時に、Resources の audioFolder から自動で読みこむ")]
        public bool autoLoadAudio = true;
        [Tooltip("自動で読みこむ場所（Resources フォルダの中）。空なら決まった場所")]
        public string audioFolder = "";

        [Header("■ ドットのフィルター")]
        [Tooltip("オンにすると、ゲーム本編と同じようなドットの見た目になる")]
        public bool dotFilter = true;
        [Tooltip("オンにすると、ゲームのカメラの ArcadeCamera から、ドットの細かさとマテリアルを借りる")]
        public bool useGameCameraSettings = true;
        [Tooltip("ムービーを描く縦のドット数（上がオンの時は、ゲームのカメラの値を使う）")]
        [Range(120, 1080)]
        public int pixelHeight = 550;
        [Tooltip("ArcadeScreen シェーダーを使ったマテリアル。空ならゲームのカメラの物を使う")]
        public Material screenMaterial;

        [Header("■ 動きの調整")]
        [Range(0f, 1f), Tooltip("画面がピカッと光る強さ")]
        public float flashStrength = 0.8f;
        [Tooltip("画面が揺れる強さ（ピクセル）")]
        public float shakeStrength = 22f;
        [Range(0f, 1f), Tooltip("ブラウン管の横線の濃さ")]
        public float scanlineAlpha = 0.3f;

        // =====================================================================
        // 受けついだスクリプトが決めること
        // =====================================================================

        /// <summary>areaIndex が -1 の時に使うエリア</summary>
        protected abstract int DefaultAreaIndex { get; }
        /// <summary>resourceFolder が空の時に使う場所</summary>
        protected abstract string DefaultResourceFolder { get; }
        /// <summary>audioFolder が空の時に使う場所</summary>
        protected abstract string DefaultAudioFolder { get; }
        /// <summary>ゲームの状態が変わった時、ムービーを流すかどうか</summary>
        protected abstract bool ShouldPlay(GameState state, GameState previous);
        /// <summary>流す前に、チェックボックスなどから中身を決める。流さない時は false</summary>
        protected virtual bool PrepareSelection() => true;
        /// <summary>カットを並べる（AddCut・AddHit・AddDip を使う）</summary>
        protected abstract void BuildMovie();
        /// <summary>毎フレーム、今のカットを動かす（lt：カットが始まってからの秒数、u：0〜1）</summary>
        protected abstract void Animate(Cut c, float lt, float u);
        /// <summary>今のムービーの音</summary>
        protected abstract AreaMovieDirector.MovieSound CurrentSound { get; }
        /// <summary>効果音のファイル名の頭（se_01 なら "se_"）</summary>
        protected virtual string SePrefix => "se_";

        protected int Area => areaIndex >= 0 ? areaIndex : DefaultAreaIndex;
        protected string Folder => string.IsNullOrEmpty(resourceFolder) ? DefaultResourceFolder : resourceFolder;
        protected string AudioFolderPath => string.IsNullOrEmpty(audioFolder) ? DefaultAudioFolder : audioFolder;

        // =====================================================================
        // 入れ物
        // =====================================================================

        protected class Layer
        {
            public RawImage img;
            public RectTransform rt;
        }

        protected class Cut
        {
            public string name;
            public float start, end;
            public RectTransform root;
            public readonly Dictionary<string, Layer> layers = new Dictionary<string, Layer>();
            public readonly List<Layer> extras = new List<Layer>();   // スクリプトで作った板（音の波の棒など）
            public RawImage edge;                                      // 色が戻る時の白い線
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

        protected const float W = 1920f, H = 1080f;
        protected MedalPusherGame game;
        protected readonly List<Cut> cuts = new List<Cut>();
        private readonly List<Hit> hits = new List<Hit>();
        private readonly List<float> dipTimes = new List<float>();
        private GameObject movieCanvasGo, displayCanvasGo, movieCameraGo;
        private Camera movieCamera;
        private RenderTexture renderTexture;
        private RawImage screenImage;
        private RectTransform stage;
        private RawImage flash, fade, scan;
        private Text skipText;
        private AudioSource bgmSource, seSource;
        private AreaMovieDirector.MovieSound sound;
        private float time, prevTime, total;
        private bool playing, skipping;
        private float skipTimer, skipArmed;
        private float savedTimeScale = 1f;
        private bool paused;
        private int activeCut = -1;
        private int lastScreenW, lastScreenH;
        private GameState lastState = (GameState)(-1);

        /// <summary>流している最中か</summary>
        public bool IsPlaying => playing;

        // =====================================================================
        // ゲームとのつながり
        // =====================================================================

        protected virtual void Awake()
        {
            game = FindFirstObjectByType<MedalPusherGame>();
            if (game != null) game.StateChanged += OnStateChanged;
        }

        protected virtual void Start()
        {
            if (debugPlayOnSceneStart) StartCoroutine(DebugPlayRoutine());
        }

        private IEnumerator DebugPlayRoutine()
        {
            yield return null;                              // ほかのスクリプトの準備を1フレーム待つ
            while (Time.timeScale == 0f) yield return null; // ほかのムービーが終わるまで待つ
            Play();
        }

        private void OnStateChanged(GameState state)
        {
            GameState previous = lastState;
            lastState = state;
            if (playing || game == null) return;
            if (ShouldPlay(state, previous)) Play();
        }

        /// <summary>ムービーを流す（確認用に、Inspector の「︙」からも呼べる）</summary>
        [ContextMenu("テスト再生")]
        public void Play()
        {
            if (playing || !Application.isPlaying) return;
            if (!PrepareSelection()) return;

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

            cuts.Clear(); hits.Clear(); dipTimes.Clear();
            activeCut = -1;
            BuildCanvas();
            BuildMovie();
            SetLayerRecursively(movieCanvasGo, LayerMask.NameToLayer("UI"));
            sound = CurrentSound;
            LoadAutoAudio(sound);
            total = cuts.Count > 0 ? cuts[cuts.Count - 1].end : 0f;

            time = prevTime = 0f;
            skipping = false; skipArmed = 0f;
            playing = true;
            if (sound != null && sound.bgm != null)
            {
                bgmSource.clip = sound.bgm;
                bgmSource.volume = sound.bgmVolume;
                bgmSource.Play();
            }
            ApplyFrame();
        }

        protected virtual void Update()
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
#if AMB_NEW_INPUT
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

        protected virtual void OnDisable()
        {
            RestoreTime();
        }

        protected virtual void OnDestroy()
        {
            if (game != null) game.StateChanged -= OnStateChanged;
            CleanUp();
        }

        /// <summary>曲・効果音の欄が空なら、Resources から自動で読みこむ（効果音は予定表の上から順番）</summary>
        private void LoadAutoAudio(AreaMovieDirector.MovieSound s)
        {
            if (!autoLoadAudio || s == null) return;
            string folder = AudioFolderPath;
            if (s.bgm == null) s.bgm = Resources.Load<AudioClip>(folder + "/bgm");
            for (int i = 0; i < s.cues.Count; i++)
            {
                OpeningSoundCue cue = s.cues[i];
                if (cue != null && cue.clip == null) cue.clip = Resources.Load<AudioClip>($"{folder}/{SePrefix}{i + 1:00}");
            }
        }

        // =====================================================================
        // 毎フレームの見た目
        // =====================================================================

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

            float f = 1f - Smooth(time / 0.3f);
            foreach (float d in dipTimes) f = Mathf.Max(f, 1f - Mathf.Abs(time - d) / 0.15f);
            f = Mathf.Max(f, Smooth((time - (total - 0.5f)) / 0.5f));
            if (skipping) f = Mathf.Max(f, Mathf.Clamp01(skipTimer / 0.35f));
            SetAlpha(fade, Mathf.Clamp01(f));

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
        // 組み立て
        // =====================================================================

        private void BuildCanvas()
        {
            movieCanvasGo = new GameObject(GetType().Name + "Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            movieCanvasGo.transform.SetParent(transform, false);
            Canvas movieCanvas = movieCanvasGo.GetComponent<Canvas>();
            CanvasScaler scaler = movieCanvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(W, H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (dotFilter)
            {
                movieCameraGo = new GameObject(GetType().Name + "Camera");
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

            displayCanvasGo = new GameObject(GetType().Name + "Display", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            displayCanvasGo.transform.SetParent(transform, false);
            Canvas display = displayCanvasGo.GetComponent<Canvas>();
            display.renderMode = RenderMode.ScreenSpaceOverlay;
            display.sortingOrder = sortingOrder + 1;
            CanvasScaler dScaler = displayCanvasGo.GetComponent<CanvasScaler>();
            dScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            dScaler.referenceResolution = new Vector2(W, H);
            dScaler.matchWidthOrHeight = 0.5f;
            RectTransform droot = (RectTransform)displayCanvasGo.transform;

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
                name = GetType().Name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
            };
            renderTexture.Create();
            movieCamera.targetTexture = renderTexture;
            screenImage.texture = renderTexture;
        }

        // =====================================================================
        // 受けついだスクリプトが使う道具（座標は、画面の真ん中が 0,0、上がプラス）
        // =====================================================================

        /// <summary>カットを1つ作る。files は Resources/（素材の場所）/ からの名前</summary>
        protected Cut AddCut(string name, float start, float end, params string[] files)
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
                else if (f.EndsWith("noise")) PrepareNoise(img.texture);
                c.layers[f] = new Layer { img = img, rt = img.rectTransform };
            }
            go.SetActive(false);
            cuts.Add(c);
            return c;
        }

        /// <summary>カットに、スクリプトで動かす板を足す（tex が null なら白い板）</summary>
        protected Layer AddExtra(Cut c, string name, Texture tex)
        {
            RawImage img = MakeRaw(c.root, tex, name, Color.white);
            if (tex != null && name.Contains("noise")) PrepareNoise(tex);
            Layer l = new Layer { img = img, rt = img.rectTransform };
            c.extras.Add(l);
            return l;
        }

        /// <summary>カットに、色が戻る時の白い線を足す</summary>
        protected void AddEdge(Cut c)
        {
            c.edge = MakeRaw(c.root, null, "Edge", new Color(1f, 1f, 1f, 0f));
            c.edge.rectTransform.sizeDelta = new Vector2(16f, H);
        }

        protected void AddHit(float time, float flashAmount, float shakeAmount) => hits.Add(new Hit(time, flashAmount, shakeAmount));
        protected void AddDip(float time) => dipTimes.Add(time);

        protected Texture2D Load(string file)
        {
            Texture2D tex = Resources.Load<Texture2D>(Folder + "/" + file);
            if (tex == null) Debug.LogWarning($"[{GetType().Name}] 素材が見つかりません：Resources/{Folder}/{file}");
            return tex;
        }

        private static void PrepareNoise(Texture tex)
        {
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
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

        protected static Layer L(Cut c, string name)
        {
            c.layers.TryGetValue(name, out Layer l);
            return l;
        }

        /// <summary>ずらす・回す・大きくする。pivot はその中心</summary>
        protected static void Pose(Layer l, Vector2 offset, float angle, float scale, Vector2 pivot)
        {
            if (l == null) return;
            Quaternion rot = Quaternion.Euler(0f, 0f, angle);
            Vector2 center = pivot + (Vector2)(rot * (Vector3)(-pivot * scale)) + offset;
            l.rt.anchoredPosition = center;
            l.rt.localRotation = rot;
            l.rt.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>横と縦で別々に大きさを変える（テレビが消える時のように、縦につぶす）</summary>
        protected static void PoseXY(Layer l, Vector2 offset, float sx, float sy, Vector2 pivot)
        {
            if (l == null) return;
            Vector2 center = pivot + new Vector2(-pivot.x * sx, -pivot.y * sy) + offset;
            l.rt.anchoredPosition = center;
            l.rt.localRotation = Quaternion.identity;
            l.rt.localScale = new Vector3(sx, sy, 1f);
        }

        protected static void Move(Layer l, Vector2 offset) => Pose(l, offset, 0f, 1f, Vector2.zero);

        /// <summary>左から p の割合だけ見せる（せりふの文字用。x0〜x1 は画面の左はしからのピクセル）</summary>
        protected static void Reveal(Layer l, float x0, float x1, float p)
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

        /// <summary>右から p の割合だけ見せる（色が戻る所用）</summary>
        protected static void RevealFromRight(Layer l, float p)
        {
            if (l == null) return;
            p = Mathf.Clamp01(p);
            float wShow = W * p;
            l.rt.pivot = new Vector2(1f, 0.5f);
            l.rt.anchoredPosition = new Vector2(W * 0.5f, 0f);
            l.rt.sizeDelta = new Vector2(wShow, H);
            l.img.uvRect = new Rect(1f - p, 0f, p, 1f);
            if (p <= 0f) SetAlpha(l, 0f);
        }

        /// <summary>色が戻る時の白い線を、p の位置に置く</summary>
        protected static void PlaceEdge(Cut c, float p)
        {
            if (c.edge == null) return;
            c.edge.rectTransform.anchoredPosition = new Vector2(W * 0.5f - W * p, 0f);
            SetAlpha(c.edge, p > 0.001f && p < 0.999f ? 0.9f : 0f);
        }

        /// <summary>せりふ：枠がふわっと出て、文字が左から順に見えていく</summary>
        protected static void CaptionIn(Layer box, Layer text, float lt, float boxAt, float textAt, float textSeconds)
        {
            float b = Smooth((lt - boxAt) / 0.3f);
            Move(box, new Vector2(0f, (1f - b) * -24f));
            SetAlpha(box, b);
            float p = Mathf.Clamp01((lt - textAt) / Mathf.Max(0.01f, textSeconds));
            Reveal(text, 230f, 1730f, p);
        }

        /// <summary>タイトルカード：大きな文字がドンと出る</summary>
        protected static void TitleIn(Layer bg, Layer text, float lt)
        {
            float e = Mathf.Clamp01(lt / 0.35f);
            Pose(text, Vector2.zero, 0f, Mathf.Lerp(2.2f, 1f, EaseBack(e)), Vector2.zero);
            SetAlpha(text, Smooth(e / 0.4f));
            Pose(bg, new Vector2((1f - EaseOut(lt / 0.4f)) * -400f, 0f), 0f, 1.25f, Vector2.zero);
        }

        /// <summary>砂嵐：毎フレーム模様をずらす</summary>
        protected static void Noise(Layer n, bool strong)
        {
            if (n == null) return;
            n.img.uvRect = new Rect(Random.value, Random.value, Mathf.Max(0.5f, n.rt.rect.width / 512f), Mathf.Max(0.5f, n.rt.rect.height / 512f));
            if (strong) n.rt.anchoredPosition += new Vector2((Random.value - 0.5f) * 80f, 0f);
        }

        protected static void Zoom(Cut c, float scale, Vector2 focus)
        {
            c.root.localScale = new Vector3(scale, scale, 1f);
            c.root.anchoredPosition = focus * (1f - scale);
        }

        protected static void SetAlpha(Layer l, float a)
        {
            if (l == null) return;
            SetAlpha(l.img, a);
        }

        protected static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            Color c = g.color;
            c.a = Mathf.Clamp01(a);
            g.color = c;
        }

        /// <summary>
        /// 音の波を映す画面の棒を動かす。keep は波の大きさ（1 でいつもどおり、0 で平ら）
        /// 画面の中（x 140〜800、まん中の高さ y=520）に 48 本並べる
        /// </summary>
        protected static void WaveformBars(List<Layer> bars, float lt, float keep, float alpha)
        {
            for (int i = 0; i < bars.Count; i++)
            {
                Layer b = bars[i];
                float xImg = 140f + i * 13.75f + 4.5f;
                float amp = 40f + 170f * Mathf.Abs(Mathf.Sin(i * 0.37f + lt * 7f) * Mathf.Sin(i * 0.11f + lt * 3.3f));
                float h = Mathf.Max(4f, amp * 2f * keep);
                b.rt.pivot = new Vector2(0.5f, 0.5f);
                b.rt.localScale = Vector3.one;
                b.rt.sizeDelta = new Vector2(9f, h);
                b.rt.anchoredPosition = new Vector2(xImg - W * 0.5f, H * 0.5f - 520f);
                Color col = Color.Lerp(new Color(1f, 0.3f, 0.4f), new Color(0.3f, 0.95f, 1f), Mathf.Clamp01(keep * 1.5f));
                col.a = alpha;
                b.img.color = col;
            }
        }

        /// <summary>チェックボックスを1つだけにする（新しくチェックされた方を残す）。何も無ければ -1</summary>
        protected static int KeepOne(bool[] on, int last)
        {
            int count = 0, first = -1;
            for (int i = 0; i < on.Length; i++) if (on[i]) { count++; if (first < 0) first = i; }
            if (count == 0) return -1;
            if (count == 1) return first;
            for (int i = 0; i < on.Length; i++) if (on[i] && i != last) return i;
            return last;
        }

        protected static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        protected static float EaseOut(float x)
        {
            x = Mathf.Clamp01(x);
            float a = 1f - x;
            return 1f - a * a * a;
        }

        protected static float EaseIn(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x;
        }

        protected static float EaseBack(float x)
        {
            x = Mathf.Clamp01(x);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float a = x - 1f;
            return 1f + c3 * a * a * a + c1 * a * a;
        }
    }
}
