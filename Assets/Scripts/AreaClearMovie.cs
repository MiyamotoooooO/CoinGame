#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define ACM_NEW_INPUT
#endif
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ACM_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace MedalPusher
{
    /// <summary>
    /// エリアのボスを倒した時に流すムービー（今はエリア1：ボリューム・ゼロ）。
    ///
    /// 【使い方】
    ///   MainScene の Hierarchy で右クリック → Create Empty → 名前を「AreaClearMovie」にして、このスクリプトを付ける。
    ///   Inspector のチェックボックスで、流すムービーと、ボスのせりふを1つずつ選ぶ（それぞれ2つ以上は選べない）。
    ///
    /// 【しくみ】
    ///   ・ボスのラウンドでノルマを達成した（Cleared になった）時に流す
    ///   ・流している間は Time.timeScale を 0 にしてゲームを止める。クリアの帯などは、ムービーの後に出る
    ///   ・素材は Resources/AreaMovies/Area1 から読む
    ///   ・Space かクリックでスキップ（1回目で案内が出て、2回目でスキップ）
    ///   ・ドットのフィルターは、エリアの始まりのムービーと同じ（ArcadeCamera と同じ見た目）
    ///   ・確認用：再生中に、このコンポーネントの右上の「︙」→「テスト再生」でいつでも流せる
    /// </summary>
    public class AreaClearMovie : MonoBehaviour
    {
        [Header("■ 流すムービー（1つだけチェックできる。新しくチェックした方が残る）")]
        [Tooltip("案1：色が戻る（約18秒）。つまみが 0→10 に回り、ボスがノイズになって崩れ、灰色の街に色が戻る")]
        public bool movie1_ColorReturns = true;
        [Tooltip("案2：放送終了（約11秒）。ボスの放送が SIGNAL LOST で切れ、ヴィニーが割りこむ")]
        public bool movie2_SignalLost = false;
        [Tooltip("案3：短いクリア演出（約6.5秒）。つまみが 10 まで回って AREA 1 CLEAR")]
        public bool movie3_Short = false;
        [SerializeField, HideInInspector] private int lastMovie = 0;

        [Header("■ ボスのやられた時のせりふ（1つだけチェックできる。案3では出ない）")]
        [Tooltip("a：「音量が……戻って……いく……」（静かに負けを認める）")]
        public bool lineA = true;
        [Tooltip("b：「ボリュームを……上げるなァ……！」（悔しがる）")]
        public bool lineB = false;
        [Tooltip("c：「……この区画の静けさも、ここまでか。」（まだ上がいる感じ）")]
        public bool lineC = false;
        [SerializeField, HideInInspector] private int lastLine = 0;

        [Header("■ デバッグ用")]
        [Tooltip("チェックを入れると、このスクリプトを付けたシーンに来た時に、ボスを倒していなくてもムービーを流す（本番では外しておく）")]
        public bool debugPlayOnSceneStart = false;

        [Header("■ 再生")]
        [Tooltip("どのエリアのボスを倒した時に流すか（0 がエリア1）")]
        public int areaIndex = 0;
        [Tooltip("素材を読む場所（Resources フォルダの中）")]
        public string resourceFolder = "AreaMovies/Area1";
        [Tooltip("流している間、ゲームを止める（クリアの帯などは、ムービーの後に出る）")]
        public bool pauseGame = true;
        [Tooltip("Space / クリックでスキップできるようにする")]
        public bool allowSkip = true;
        [Tooltip("画面の手前に出す順番（大きいほど手前）")]
        public int sortingOrder = 5000;

        [Header("■ 音を自動で読みこむ")]
        [Tooltip("オンにすると、曲・効果音の欄が空の時に、Resources の audioFolder から自動で読みこむ")]
        public bool autoLoadAudio = true;
        [Tooltip("自動で読みこむ場所（Resources フォルダの中）")]
        public string audioFolder = "AreaMovies/Area1/Audio/Clear";

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

        [Header("■ 音（空のままでも動く）")]
        [Tooltip("案1 の曲と効果音")]
        public AreaMovieDirector.MovieSound sound1 = new AreaMovieDirector.MovieSound(new[]
        {
            ("つまみが回る「ギュイーン」", 0.8f), ("音が戻る「ドン」", 2.0f), ("ボスが崩れるノイズ", 4.2f),
            ("テレビが消える「プツン」", 5.6f), ("街に色が戻る「キラーン」", 6.5f), ("タイトルの「ドン」", 14.15f),
        });
        [Tooltip("案2 の曲と効果音")]
        public AreaMovieDirector.MovieSound sound2 = new AreaMovieDirector.MovieSound(new[]
        {
            ("電波の乱れ", 0.6f), ("テレビが消える「プツン」", 4.3f), ("砂嵐のザー", 4.5f),
            ("ヴィニー登場の「ドン」", 6.5f), ("タイトルの「ドン」", 8.95f),
        });
        [Tooltip("案3 の曲と効果音")]
        public AreaMovieDirector.MovieSound sound3 = new AreaMovieDirector.MovieSound(new[]
        {
            ("つまみが回る「ギュイーン」", 0.4f), ("音が戻る「ドン」", 1.6f), ("タイトルの「ドン」", 3.15f),
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
            public RawImage edge;   // 色が戻る時の白い線
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
        private MedalPusherGame game;
        private int movie = -1;      // 0=案1 1=案2 2=案3
        private string lineFile = "Clear/line_a";
        private readonly List<Cut> cuts = new List<Cut>();
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

        // =====================================================================
        // Inspector：それぞれ、チェックは1つだけ
        // =====================================================================

        private void OnValidate()
        {
            bool[] m = { movie1_ColorReturns, movie2_SignalLost, movie3_Short };
            int pick = KeepOne(m, lastMovie);
            movie1_ColorReturns = pick == 0; movie2_SignalLost = pick == 1; movie3_Short = pick == 2;
            lastMovie = pick;

            bool[] l = { lineA, lineB, lineC };
            int pickLine = KeepOne(l, lastLine);
            lineA = pickLine == 0; lineB = pickLine == 1; lineC = pickLine == 2;
            lastLine = pickLine;
        }

        /// <summary>2つ以上チェックされていたら、新しくチェックされた方だけ残す。何も無ければ -1</summary>
        private static int KeepOne(bool[] on, int last)
        {
            int count = 0, first = -1;
            for (int i = 0; i < on.Length; i++) if (on[i]) { count++; if (first < 0) first = i; }
            if (count == 0) return -1;
            if (count == 1) return first;
            for (int i = 0; i < on.Length; i++) if (on[i] && i != last) return i;
            return last;
        }

        // =====================================================================
        // ゲームとのつながり
        // =====================================================================

        private void Start()
        {
            game = FindFirstObjectByType<MedalPusherGame>();
            if (game != null) game.StateChanged += OnStateChanged;
            else Debug.LogWarning("[クリアムービー] MedalPusherGame が見つかりません。", this);

            // デバッグ用：シーンに来たら、強制的にムービーを流す
            if (debugPlayOnSceneStart) StartCoroutine(DebugPlayRoutine());
        }

        /// <summary>
        /// デバッグ用。エリアの始まりのムービーなどでゲームが止まっている間は待って、
        /// 止まりが解けたらすぐに流す（2つのムービーが重ならないように）
        /// </summary>
        private System.Collections.IEnumerator DebugPlayRoutine()
        {
            yield return null;                              // ほかのスクリプトの準備を1フレーム待つ
            while (Time.timeScale == 0f) yield return null; // ほかのムービーが終わるまで待つ
            Play();
        }

        private void OnStateChanged(GameState state)
        {
            // ボスのラウンドで、ノルマを達成した時
            if (state == GameState.Cleared && game != null && game.IsBossRound && game.AreaIndex == areaIndex && !playing)
            {
                Play();
            }
        }

        /// <summary>ムービーを流す（確認用に、Inspector の「︙」からも呼べる）</summary>
        [ContextMenu("テスト再生")]
        public void Play()
        {
            if (playing || !Application.isPlaying) return;
            movie = movie1_ColorReturns ? 0 : movie2_SignalLost ? 1 : movie3_Short ? 2 : -1;
            if (movie < 0) return;
            lineFile = lineB ? "Clear/line_b" : lineC ? "Clear/line_c" : "Clear/line_a";

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
            if (movie == 0) BuildMovie1();
            else if (movie == 1) BuildMovie2();
            else BuildMovie3();
            SetLayerRecursively(movieCanvasGo, LayerMask.NameToLayer("UI"));
            sound = movie == 0 ? sound1 : movie == 1 ? sound2 : sound3;
            LoadAutoAudio(sound, $"m{movie + 1}_se_");
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
#if ACM_NEW_INPUT
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
                if (movie == 0) Animate1(c, lt, u);
                else if (movie == 1) Animate2(c, lt, u);
                else Animate3(c, lt, u);
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
        // 案1：色が戻る
        // =====================================================================

        private void BuildMovie1()
        {
            AddCut("close", 0f, 6f, "B/close_bg", "B/boss_big", "Clear/boss_glitch", "B/knob_face", "B/knob_numbers", "B/knob_cap", "Common/boss_box", lineFile);
            Cut color = AddCut("color", 6f, 14f, "B/sky", "B/city_far", "B/city_near", "B/street",
                               "Clear/sky_c", "Clear/city_far_c", "Clear/city_near_c", "Clear/street_c", "B/signs", "Clear/notes");
            color.edge = MakeRaw(color.root, null, "Edge", new Color(1f, 1f, 1f, 0f));
            color.edge.rectTransform.sizeDelta = new Vector2(16f, H);
            AddCut("title", 14f, 18f, "Common/title_bg", "Clear/title_text");
            dipTimes.Add(6f);
            dipTimes.Add(14f);
            hits.Add(new Hit(2.0f, 0.9f, 1f));      // つまみが 10 まで回った（音が戻る）
            hits.Add(new Hit(5.6f, 0.7f, 0.4f));    // ボスが消える
            hits.Add(new Hit(14.15f, 0.9f, 1f));    // タイトル
        }

        private void Animate1(Cut c, float lt, float u)
        {
            switch (c.name)
            {
                case "close":
                {
                    // つまみ：0 から 10 まで一気に回る
                    float faceA = Smooth(lt / 0.4f);
                    SetAlpha(L(c, "B/knob_face"), faceA);
                    SetAlpha(L(c, "B/knob_numbers"), faceA);
                    float v = lt < 0.8f ? 0f : Mathf.Lerp(0f, 10f, EaseIn((lt - 0.8f) / 1.2f));
                    if (lt > 2.0f) v = 10f + Mathf.Sin((lt - 2f) * 40f) * Mathf.Exp(-(lt - 2f) * 6f) * 0.4f;
                    Layer cap = L(c, "B/knob_cap");
                    Pose(cap, Vector2.zero, -27f * v, 1f, new Vector2(-440f, 20f));
                    SetAlpha(cap, faceA);

                    // ボス：音が戻るとノイズが走り、最後はテレビが消えるように縦につぶれて消える
                    Layer boss = L(c, "B/boss_big");
                    Layer glitch = L(c, "Clear/boss_glitch");
                    Vector2 feet = new Vector2(340f, -500f);
                    float shake = lt > 2.0f ? Mathf.Clamp01((lt - 2.0f) / 2.2f) : 0f;
                    float jx = (Mathf.PerlinNoise(lt * 30f, 1.3f) - 0.5f) * 80f * shake;
                    bool showGlitch = shake > 0f && Mathf.Repeat(lt * (6f + shake * 14f), 1f) < 0.3f + shake * 0.4f;
                    float off = Smooth((lt - 4.6f) / 1.0f);   // 0〜1 で消えていく
                    float sx = 1f + off * 0.4f;
                    float sy = Mathf.Lerp(1f, 0.02f, off * off);
                    PoseXY(boss, new Vector2(jx, 0f), sx, sy, new Vector2(340f, -50f));
                    PoseXY(glitch, new Vector2(-jx, 0f), sx, sy, new Vector2(340f, -50f));
                    SetAlpha(boss, (showGlitch ? 0f : 1f) * (1f - Smooth((lt - 5.4f) / 0.2f)));
                    SetAlpha(glitch, (showGlitch ? 1f : 0f) * (1f - Smooth((lt - 5.4f) / 0.2f)));
                    if (lt < 2.0f) Pose(boss, Vector2.zero, 0f, 1f, feet);

                    // やられた時のせりふ
                    CaptionIn(L(c, "Common/boss_box"), L(c, lineFile), lt, 2.4f, 2.7f, 1.2f);
                    break;
                }

                case "color":
                {
                    // 灰色の街に、右から色が戻っていく
                    float p = Smooth((lt - 0.5f) / 3.5f);
                    foreach (string n in new[] { "Clear/sky_c", "Clear/city_far_c", "Clear/city_near_c", "Clear/street_c" })
                    {
                        RevealFromRight(L(c, n), p);
                    }
                    float edgeX = W * 0.5f - W * p;
                    c.edge.rectTransform.anchoredPosition = new Vector2(edgeX, 0f);
                    SetAlpha(c.edge, p > 0.001f && p < 0.999f ? 0.9f : 0f);

                    // ネオンの看板：色が通りすぎた所から、チカチカして光る
                    Layer signs = L(c, "B/signs");
                    float on = Smooth((p - 0.55f) / 0.3f);
                    float flick = lt < 4.6f && Mathf.Repeat(lt * 11f, 1f) < 0.4f ? 0.4f : 1f;
                    SetAlpha(signs, on * flick);

                    // 音符がふわふわ上がってくる
                    Layer notes = L(c, "Clear/notes");
                    float na = Smooth((lt - 3.8f) / 0.8f);
                    Move(notes, new Vector2(Mathf.Sin(lt * 1.4f) * 12f, (lt - 3.8f) * 14f - 40f * (1f - na)));
                    SetAlpha(notes, na);
                    Zoom(c, Mathf.Lerp(1.04f, 1.12f, Smooth(u)), new Vector2(0f, 60f));
                    break;
                }

                case "title":
                    Title(c, lt);
                    break;
            }
        }

        // =====================================================================
        // 案2：放送終了
        // =====================================================================

        private void BuildMovie2()
        {
            AddCut("broadcast", 0f, 4.5f, "Clear/studio_bg", "Clear/boss_tv", "Clear/noise", "Common/boss_box", lineFile);
            AddCut("lost", 4.5f, 6.5f, "Clear/noise", "Clear/signal_lost");
            AddCut("vinny", 6.5f, 8.8f, "Clear/vinny_bg", "Clear/vinny", "Clear/vinny_box", "Clear/vinny_text");
            AddCut("title", 8.8f, 11.5f, "Common/title_bg", "Clear/title_text");
            hits.Add(new Hit(4.3f, 0.8f, 0.6f));    // 放送が切れる
            hits.Add(new Hit(6.5f, 0.6f, 0.4f));    // ヴィニー
            hits.Add(new Hit(8.95f, 0.9f, 1f));     // タイトル
        }

        private void Animate2(Cut c, float lt, float u)
        {
            switch (c.name)
            {
                case "broadcast":
                {
                    Layer boss = L(c, "Clear/boss_tv");
                    float g = Smooth((lt - 0.6f) / 2.6f);   // 乱れの強さ
                    float jx = (Mathf.PerlinNoise(lt * 30f, 2.1f) - 0.5f) * 120f * g;
                    float off = Smooth((lt - 3.9f) / 0.4f);
                    PoseXY(boss, new Vector2(jx, 0f), 1f + off * 0.3f, Mathf.Lerp(1f, 0.02f, off), new Vector2(0f, 0f));
                    SetAlpha(boss, 1f - Smooth((lt - 4.25f) / 0.1f));
                    Zoom(c, Mathf.Lerp(1f, 1.06f, Smooth(u)), new Vector2(0f, 150f));
                    Layer noise = L(c, "Clear/noise");
                    Noise(noise, g > 0.5f);
                    SetAlpha(noise, g * (Mathf.Repeat(lt * 9f, 1f) < 0.5f ? 0.55f : 0.15f));
                    CaptionIn(L(c, "Common/boss_box"), L(c, lineFile), lt, 0.8f, 1.1f, 1.2f);
                    break;
                }

                case "lost":
                {
                    Noise(L(c, "Clear/noise"), false);
                    Layer t = L(c, "Clear/signal_lost");
                    float a = Mathf.Repeat(lt * 3f, 1f) < 0.7f ? 1f : 0.3f;
                    Move(t, new Vector2(Mathf.Repeat(lt * 5f, 1f) < 0.08f ? 20f : 0f, 0f));
                    SetAlpha(t, a);
                    break;
                }

                case "vinny":
                {
                    Layer v = L(c, "Clear/vinny");
                    float hop = lt > 0.5f ? Mathf.Abs(Mathf.Sin(lt * 2f * Mathf.PI)) * 14f : 0f;
                    Pose(v, new Vector2(Mathf.Lerp(-700f, 0f, EaseBack(lt / 0.5f)), hop), 0f, 1f, new Vector2(-200f, -290f));
                    CaptionIn(L(c, "Clear/vinny_box"), L(c, "Clear/vinny_text"), lt, 0.4f, 0.6f, 0.6f);
                    break;
                }

                case "title":
                    Title(c, lt);
                    break;
            }
        }

        // =====================================================================
        // 案3：短いクリア演出
        // =====================================================================

        private void BuildMovie3()
        {
            AddCut("knob", 0f, 3f, "B/close_bg", "B/knob_face", "B/knob_numbers", "B/knob_cap");
            AddCut("title", 3f, 6.5f, "Common/title_bg", "Clear/title_text");
            hits.Add(new Hit(1.6f, 1f, 1f));
            hits.Add(new Hit(3.15f, 0.9f, 1f));
        }

        private void Animate3(Cut c, float lt, float u)
        {
            if (c.name == "title") { Title(c, lt); return; }
            float v = lt < 0.4f ? 0f : Mathf.Lerp(0f, 10f, EaseIn((lt - 0.4f) / 1.2f));
            if (lt > 1.6f) v = 10f + Mathf.Sin((lt - 1.6f) * 40f) * Mathf.Exp(-(lt - 1.6f) * 6f) * 0.4f;
            Pose(L(c, "B/knob_cap"), Vector2.zero, -27f * v, 1f, new Vector2(-440f, 20f));
            Zoom(c, Mathf.Lerp(1f, 1.15f, Smooth(u)), new Vector2(-440f, 20f));
        }

        // =====================================================================
        // 共通の動き
        // =====================================================================

        private void Title(Cut c, float lt)
        {
            Layer t = L(c, "Clear/title_text");
            float e = Mathf.Clamp01(lt / 0.35f);
            Pose(t, Vector2.zero, 0f, Mathf.Lerp(2.2f, 1f, EaseBack(e)), Vector2.zero);
            SetAlpha(t, Smooth(e / 0.4f));
            Pose(L(c, "Common/title_bg"), new Vector2((1f - EaseOut(lt / 0.4f)) * -400f, 0f), 0f, 1.25f, Vector2.zero);
        }

        private static void CaptionIn(Layer box, Layer text, float lt, float boxAt, float textAt, float textSeconds)
        {
            float b = Smooth((lt - boxAt) / 0.3f);
            Move(box, new Vector2(0f, (1f - b) * -24f));
            SetAlpha(box, b);
            float p = Mathf.Clamp01((lt - textAt) / Mathf.Max(0.01f, textSeconds));
            Reveal(text, 230f, 1730f, p);
        }

        private static void Noise(Layer n, bool strong)
        {
            if (n == null) return;
            n.img.uvRect = new Rect(Random.value, Random.value, W / 512f, H / 512f);
            if (strong) Move(n, new Vector2((Random.value - 0.5f) * 80f, 0f));
        }

        // =====================================================================
        // 組み立て
        // =====================================================================

        private void BuildCanvas()
        {
            movieCanvasGo = new GameObject("AreaClearMovieCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            movieCanvasGo.transform.SetParent(transform, false);
            Canvas movieCanvas = movieCanvasGo.GetComponent<Canvas>();
            CanvasScaler scaler = movieCanvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(W, H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (dotFilter)
            {
                movieCameraGo = new GameObject("AreaClearMovieCamera");
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

            displayCanvasGo = new GameObject("AreaClearMovieDisplay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
                name = "AreaClearMovie",
                filterMode = FilterMode.Point,
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
                else if (f.EndsWith("noise"))
                {
                    img.texture.wrapMode = TextureWrapMode.Repeat;
                    img.texture.filterMode = FilterMode.Point;
                }
                c.layers[f] = new Layer { img = img, rt = img.rectTransform };
            }
            go.SetActive(false);
            cuts.Add(c);
            return c;
        }

        private Texture2D Load(string file)
        {
            Texture2D tex = Resources.Load<Texture2D>(resourceFolder + "/" + file);
            if (tex == null) Debug.LogWarning($"[クリアムービー] 素材が見つかりません：Resources/{resourceFolder}/{file}");
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

        private static void Pose(Layer l, Vector2 offset, float angle, float scale, Vector2 pivot)
        {
            if (l == null) return;
            Quaternion rot = Quaternion.Euler(0f, 0f, angle);
            Vector2 center = pivot + (Vector2)(rot * (Vector3)(-pivot * scale)) + offset;
            l.rt.anchoredPosition = center;
            l.rt.localRotation = rot;
            l.rt.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>横と縦で別々に大きさを変える（テレビが消える時のように、縦につぶす）</summary>
        private static void PoseXY(Layer l, Vector2 offset, float sx, float sy, Vector2 pivot)
        {
            if (l == null) return;
            Vector2 center = pivot + new Vector2(-pivot.x * sx, -pivot.y * sy) + offset;
            l.rt.anchoredPosition = center;
            l.rt.localRotation = Quaternion.identity;
            l.rt.localScale = new Vector3(sx, sy, 1f);
        }

        private static void Move(Layer l, Vector2 offset) => Pose(l, offset, 0f, 1f, Vector2.zero);

        /// <summary>左から p の割合だけ見せる（せりふの文字用）</summary>
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

        /// <summary>右から p の割合だけ見せる（色が戻る所用）</summary>
        private static void RevealFromRight(Layer l, float p)
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

        private static float EaseIn(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x;
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
