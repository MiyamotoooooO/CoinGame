using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace MedalPusher
{
    /// <summary>
    /// オープニングムービー（約60秒）を流すスクリプト。
    ///
    /// 【組み立て方】
    ///   メニューの「RECORD DROP → オープニングを組み立てる」を押すと、
    ///   opening_cuts.json を読んで、10カット分の画像とこのスクリプトを自動でシーンに並べる。
    ///
    /// 【しくみ】
    ///   「今ムービーの何秒目か」だけを数えて、その秒数での見た目（位置・回転・濃さ）を
    ///   毎フレーム計算し直している。だから途中の秒数から再生しても、スキップしても崩れない。
    ///   動きを変えたい時は、下の「Cut1 〜 Cut10 の動き」の所を書きかえる。
    /// </summary>
    public class OpeningMoviePlayer : MonoBehaviour
    {
        // 画面の大きさ（素材と同じ 1920×1080）
        public const float ScreenW = 1920f;
        public const float ScreenH = 1080f;
        // Cut4 の灰色の範囲を、画面の右はしより少し外側から始めるための余白
        public const float WipeOverhang = 100f;

        [Header("■ 再生")]
        [Tooltip("シーンが始まったら自動で再生する")]
        public bool autoPlay = true;
        [Tooltip("終わった後に読み込むシーンの名前。空にすると、ムービーを消すだけになる")]
        public string nextSceneName = "MainScene";
        [Tooltip("確認用：この秒数から再生を始める（例：38 にすると Cut8 から）。本番は 0 にしておく")]
        public float startFromSeconds = 0f;
        [Tooltip("Enter / Esc / クリックでスキップできるようにする（1回目で案内が出て、2回目でスキップ）")]
        public bool allowSkip = true;
        [Tooltip("ムービーが終わった時（スキップした時も）に呼ばれる")]
        public UnityEvent onFinished = new UnityEvent();

        [Header("■ 曲")]
        [Tooltip("オープニング曲。空のままでも動く（その場合は無音）")]
        public AudioClip bgmClip;
        [Range(0f, 1f), Tooltip("曲の音量")]
        public float bgmVolume = 0.8f;
        [Tooltip("曲が鳴り始める秒数（Cut1 で針が落ちた少し後）")]
        public float bgmStartTime = 1.4f;
        [Tooltip("Cut4 が始まってから何秒後に、曲にブレーキがかかり始めるか")]
        public float brakeStart = 0.3f;
        [Tooltip("ブレーキがかかってから無音になるまでの秒数")]
        public float brakeSeconds = 1.5f;
        [Tooltip("Cut8 の「ドン！」で曲が戻る時、曲のどこ（秒）から流すか。サビの頭に合わせると気持ちいい")]
        public float bgmDropTime = 0f;
        [Tooltip("曲のテンポ（BPM）。キャラが跳ねるリズムに使う")]
        public float bpm = 120f;

        [Header("■ 効果音")]
        [Tooltip("効果音の予定表。clip に音を入れるだけで、その秒数に鳴る。memo は何の音かのメモ")]
        public List<OpeningSoundCue> soundCues = new List<OpeningSoundCue>();
        [Tooltip("せりふの文字が1文字出るたびに鳴る「ピッ」という音（空なら鳴らない）")]
        public AudioClip typeBlipClip;
        [Range(0f, 1f), Tooltip("「ピッ」の音量")]
        public float typeBlipVolume = 0.35f;

        [Header("■ せりふ")]
        [Tooltip("1文字出るまでの間隔（秒）")]
        public float charInterval = 0.07f;
        [Tooltip("1カットにせりふが2つ以上ある時、1つを何秒見せるか")]
        public float captionLineSeconds = 2.4f;
        [Tooltip("話している人の名前の色")]
        public Color speakerColor = new Color(1f, 0.35f, 0.75f);
        [Tooltip("吹き出し（Cut8）の中の文字の色")]
        public Color bubbleTextColor = new Color(0.1f, 0.06f, 0.16f);

        [Header("■ 動きの調整")]
        [Tooltip("Cut1 レコードの回転の中心（画面の真ん中から何ピクセルずれているか）")]
        public Vector2 recordCenter = new Vector2(-120f, -147f);
        [Tooltip("Cut1 レコードが回る速さ（度/秒）")]
        public float recordSpinSpeed = 150f;
        [Tooltip("Cut1 トーンアーム（針の腕）の付け根の位置（画面の真ん中から何ピクセルずれているか）")]
        public Vector2 tonearmPivot = new Vector2(745f, 475f);
        [Tooltip("Cut1 トーンアームの最初の角度。ここから 0 度まで動いて、針がレコードに乗る")]
        public float tonearmStartAngle = 16f;
        [Tooltip("Cut1 が始まってから何秒後に針が落ちるか")]
        public float needleTime = 1.2f;
        [Tooltip("Cut4 が始まってから、灰色が右から広がり始めるまでの秒数")]
        public float wipeDelay = 0.4f;
        [Tooltip("Cut4 の灰色が画面全体に広がるまでの秒数")]
        public float wipeSeconds = 4.2f;
        [Tooltip("Cut7 が始まってから何秒後に布が落ちるか")]
        public float clothTime = 1.2f;
        [Tooltip("Cut7 が始まってから何秒後に蛍光灯がつくか")]
        public float lightTime = 3.0f;
        [Tooltip("Cut8 が始まってから何秒後に「ドン！」で曲が戻るか")]
        public float dropTime = 2.4f;
        [Tooltip("Cut10 が始まってから何秒後にロゴが落ちてくるか")]
        public float logoTime = 1.0f;
        [Tooltip("ロゴが落ちてきて止まるまでの秒数")]
        public float logoSlamSeconds = 0.25f;
        [Tooltip("Cut10 が始まってから何秒後に、下の文字が点滅し始めるか")]
        public float roundTextTime = 2.6f;
        [Range(0f, 1f), Tooltip("画面がピカッと白く光る強さ（0 で光らない）")]
        public float flashStrength = 0.85f;
        [Tooltip("画面が揺れる強さ（ピクセル）")]
        public float shakeStrength = 18f;
        [Tooltip("カットが切り替わる時の暗転の長さ（秒）。0 で暗転しない")]
        public float cutDipSeconds = 0.18f;
        [Range(0f, 1f), Tooltip("ブラウン管の横線の濃さ")]
        public float scanlineAlpha = 0.35f;
        [Range(0f, 1f), Tooltip("画面の四すみを暗くする濃さ")]
        public float vignetteAlpha = 1f;

        [Header("■ 自動で入る（組み立てメニューが入れるので、触らなくてよい）")]
        public List<OpeningCutView> cuts = new List<OpeningCutView>();
        public RectTransform stageRoot;
        public RectTransform captionHome;
        public RectTransform captionRoot;
        public Image captionFallbackBox;
        public TextMeshProUGUI captionSpeaker;
        public TextMeshProUGUI captionBody;
        public RectTransform wipeMask;
        public Image wipeEdge;
        public Image vignette;
        public Image scanlines;
        public Image flashImage;
        public Image fadeImage;
        public TextMeshProUGUI skipLabel;
        public AudioSource bgmSource;
        public AudioSource seSource;

        // ---------- 中で使う値 ----------
        private const float StartFadeSeconds = 0.8f;   // 最初の暗転から明るくなる時間
        private const float EndFadeSeconds = 1.2f;     // 最後に暗くなる時間
        private const float SkipFadeSeconds = 0.4f;    // スキップした時に暗くなる時間
        private const float SkipArmSeconds = 2.0f;     // 「もう一度押すとスキップ」が出ている時間
        private const string SkipNormalText = "Enter / クリック：スキップ";
        private const string SkipArmedText = "もう一度押すとスキップ";

        private float time;          // 今ムービーの何秒目か
        private float prevTime;      // 1フレーム前の秒数（効果音を鳴らす判定に使う）
        private float playClock;     // 再生ボタンを押してから何秒たったか
        private bool playing;
        private bool skipping;
        private float skipTimer;
        private float skipArmedTimer;
        private int activeCut = -1;
        private int bgmPhase = -1;
        private int captionKey = -1;
        private int lastVisibleChars = -1;

        /// <summary>ムービー全体の長さ（秒）</summary>
        public float TotalSeconds => cuts.Count == 0 ? 0f : cuts[cuts.Count - 1].end;

        /// <summary>再生中かどうか</summary>
        public bool IsPlaying => playing;

        // =====================================================================
        // 始まり・終わり
        // =====================================================================

        private void Awake()
        {
            foreach (OpeningCutView cut in cuts)
            {
                if (cut == null) continue;
                cut.BuildLookup();
                if (cut.root != null) cut.root.gameObject.SetActive(false);
            }
            if (captionRoot != null) captionRoot.gameObject.SetActive(false);
            if (skipLabel != null) skipLabel.gameObject.SetActive(false);
            SetImageAlpha(flashImage, 0f);
            SetImageAlpha(fadeImage, autoPlay ? 1f : 0f);
        }

        private void Start()
        {
            if (autoPlay) Play();
        }

        /// <summary>ムービーを最初（または startFromSeconds）から再生する</summary>
        public void Play()
        {
            if (cuts.Count == 0)
            {
                Debug.LogWarning("[オープニング] カットが入っていません。メニューの「RECORD DROP → オープニングを組み立てる」を押してください。");
                return;
            }
            gameObject.SetActive(true);
            time = Mathf.Clamp(startFromSeconds, 0f, TotalSeconds);
            prevTime = time;
            playClock = 0f;
            playing = true;
            skipping = false;
            skipTimer = 0f;
            skipArmedTimer = 0f;
            activeCut = -1;
            bgmPhase = -1;
            captionKey = -1;
            lastVisibleChars = -1;
            ApplyFrame();
        }

        /// <summary>スキップする（暗くなってから終わる）</summary>
        public void Skip()
        {
            if (!playing || skipping) return;
            skipping = true;
            skipTimer = 0f;
            prevTime = time;
        }

        private void Update()
        {
            if (!playing) return;
            float dt = Time.deltaTime;
            playClock += dt;

            if (skipping)
            {
                skipTimer += dt;
                ApplyFrame();
                if (skipTimer >= SkipFadeSeconds) Finish();
                return;
            }

            if (allowSkip) HandleSkipInput(dt);
            if (skipping) return;

            prevTime = time;
            time += dt;
            if (time >= TotalSeconds)
            {
                time = TotalSeconds;
                ApplyFrame();
                Finish();
                return;
            }
            ApplyFrame();
        }

        private void HandleSkipInput(float dt)
        {
            if (skipArmedTimer > 0f) skipArmedTimer -= dt;

            bool pressed = InputCompat.MouseDown()
                        || InputCompat.Down(GameKey.Cancel)
                        || InputCompat.Down(GameKey.NextRound);
            if (pressed)
            {
                // 1回目は案内を出すだけ。案内が出ている間にもう一度押したらスキップ
                if (skipArmedTimer > 0f)
                {
                    Skip();
                    return;
                }
                skipArmedTimer = SkipArmSeconds;
            }

            if (skipLabel != null)
            {
                if (!skipLabel.gameObject.activeSelf) skipLabel.gameObject.SetActive(true);
                bool armed = skipArmedTimer > 0f;
                skipLabel.text = SafeText(armed ? SkipArmedText : SkipNormalText, skipLabel.font);
                skipLabel.alpha = armed ? 0.95f : 0.4f;
            }
        }

        private void Finish()
        {
            if (!playing) return;
            playing = false;
            skipping = false;
            if (bgmSource != null) bgmSource.Stop();
            if (onFinished != null) onFinished.Invoke();

            if (!string.IsNullOrEmpty(nextSceneName))
            {
                if (Application.CanStreamedLevelBeLoaded(nextSceneName))
                {
                    SceneManager.LoadScene(nextSceneName);
                    return;
                }
                Debug.LogWarning($"[オープニング] シーン「{nextSceneName}」が Build Settings に入っていないので読み込めません。File → Build Settings に追加してください。");
            }
            gameObject.SetActive(false);
        }

        // =====================================================================
        // 毎フレームの見た目
        // =====================================================================

        private void ApplyFrame()
        {
            int index = FindCutIndex(time);
            if (index != activeCut)
            {
                for (int i = 0; i < cuts.Count; i++)
                {
                    if (cuts[i] != null && cuts[i].root != null) cuts[i].root.gameObject.SetActive(i == index);
                }
                activeCut = index;
                captionKey = -1;
                lastVisibleChars = -1;
            }

            OpeningCutView cut = index >= 0 ? cuts[index] : null;
            if (cut != null && cut.root != null)
            {
                float lt = time - cut.start;               // カットが始まってから何秒か
                float u = Mathf.Clamp01(lt / cut.Duration); // カットの中で 0〜1 のどこか
                ResetCut(cut);
                AnimateCut(cut, lt, u);
                AnimateCaptionBox(cut, lt);
                UpdateCaption(cut, lt);
            }
            else
            {
                UpdateCaption(null, 0f);
            }

            UpdateScreenEffects();
            UpdateBgm();
            FireSoundCues();
        }

        private int FindCutIndex(float t)
        {
            for (int i = 0; i < cuts.Count; i++)
            {
                if (cuts[i] != null && t < cuts[i].end) return i;
            }
            return cuts.Count - 1;
        }

        private OpeningCutView CutByNumber(int number)
        {
            foreach (OpeningCutView cut in cuts)
            {
                if (cut != null && cut.number == number) return cut;
            }
            return null;
        }

        /// <summary>「CutN が始まってから○秒後」を、ムービー全体の秒数にする。カットが無ければ ずっと先</summary>
        private float AbsTime(int cutNumber, float localSeconds)
        {
            OpeningCutView cut = CutByNumber(cutNumber);
            return cut == null ? float.PositiveInfinity : cut.start + localSeconds;
        }

        /// <summary>毎フレーム、カットの全レイヤーを「止まっている状態」に戻す</summary>
        private static void ResetCut(OpeningCutView cut)
        {
            cut.root.anchoredPosition = Vector2.zero;
            cut.root.localScale = Vector3.one;
            cut.root.localRotation = Quaternion.identity;
            foreach (OpeningLayerView layer in cut.layers)
            {
                if (layer == null || layer.rect == null) continue;
                layer.rect.anchoredPosition = layer.basePos;
                layer.rect.localRotation = Quaternion.identity;
                layer.rect.localScale = Vector3.one;
                if (layer.image != null) layer.image.color = Color.white;
            }
        }

        private void AnimateCut(OpeningCutView c, float lt, float u)
        {
            switch (c.number)
            {
                case 1: AnimateCut1(c, lt, u); break;
                case 2: AnimateCut2(c, lt, u); break;
                case 3: AnimateCut3(c, lt, u); break;
                case 4: AnimateCut4(c, lt, u); break;
                case 5: AnimateCut5(c, lt, u); break;
                case 6: AnimateCut6(c, lt, u); break;
                case 7: AnimateCut7(c, lt, u); break;
                case 8: AnimateCut8(c, lt, u); break;
                case 9: AnimateCut9(c, lt, u); break;
                case 10: AnimateCut10(c, lt, u); break;
            }
        }

        // =====================================================================
        // Cut1 〜 Cut10 の動き
        //   lt … カットが始まってから何秒か
        //   u  … カットの中で 0（始まり）〜 1（終わり）のどこか
        // =====================================================================

        // Cut1：真っ暗な中、レコードに針が落ちる。「198X年 ネオン通り」
        private void AnimateCut1(OpeningCutView c, float lt, float u)
        {
            // レコードはずっと回る
            Pose(c.Find("record"), Vector2.zero, -lt * recordSpinSpeed, 1f, recordCenter);

            // トーンアームが付け根を中心に動いて、針がレコードに乗る（乗った瞬間に少しはねる）
            float armMove = Smooth((lt - (needleTime - 1.0f)) / 0.9f);
            float armAngle = Mathf.Lerp(tonearmStartAngle, 0f, armMove);
            if (lt > needleTime)
            {
                float after = lt - needleTime;
                armAngle += Mathf.Sin(after * 30f) * Mathf.Exp(-after * 8f) * 1.2f;
            }
            Pose(c.Find("tonearm"), Vector2.zero, armAngle, 1f, tonearmPivot);

            // 針が乗った瞬間に火花がパッと光る
            OpeningLayerView spark = c.Find("spark");
            if (spark != null)
            {
                float e = lt - needleTime;
                float a = e < 0f ? 0f : Mathf.Exp(-e * 3.5f);
                float s = Mathf.Lerp(0.6f, 1.25f, EaseOut(e / 0.4f));
                Pose(spark, Vector2.zero, Mathf.Max(0f, e) * 90f, s, spark.boundsCenter);
                SetColor(spark, 1f, a);
            }

            // 「198X年 ネオン通り」がふわっと出て、最後に消える
            OpeningLayerView title = c.Find("title_text");
            if (title != null)
            {
                float appear = Clamp01((lt - 1.8f) / 0.6f);
                float a = Smooth(appear) * (1f - Smooth((lt - 3.4f) / 0.5f));
                Move(title, new Vector2(0f, -24f * (1f - EaseOut(appear))));
                SetColor(title, 1f, a);
            }

            // カメラがゆっくりレコードに寄る
            Zoom(c, Mathf.Lerp(1f, 1.06f, Smooth(u)), recordCenter);
        }

        // Cut2：ネオンがきらめく夜の街。ネオンが踊っている
        private void AnimateCut2(OpeningCutView c, float lt, float u)
        {
            float beat = Beat(c.start + lt);

            // 奥の物ほどゆっくり、手前の物ほど速く横に流す（奥行きが出る）
            foreach (OpeningLayerView layer in c.layers)
            {
                if (layer == null || layer.name == "neon" || layer.name == "notes") continue;
                Move(layer, new Vector2(Parallax(layer.name, u), 0f));
            }

            // ネオンはリズムに合わせて跳ねて、左右にゆれる
            OpeningLayerView neon = c.Find("neon");
            if (neon != null)
            {
                float hop = Bounce(beat);
                Pose(neon, new Vector2(Parallax("neon", u), hop * 22f), Mathf.Sin(beat * Mathf.PI) * 3f, 1f, Feet(neon));
            }

            // 音符はふわふわ上がりながら、明るくなったり暗くなったりする
            OpeningLayerView notes = c.Find("notes");
            if (notes != null)
            {
                Move(notes, new Vector2(Parallax("notes", u), Mathf.Sin(lt * 2f) * 10f + lt * 10f));
                SetColor(notes, 1f, 0.75f + 0.25f * Mathf.Sin(lt * 5f));
            }

            // お店のネオン看板がチラチラする
            OpeningLayerView shops = c.Find("shops");
            if (shops != null)
            {
                float flicker = 0.93f + 0.07f * Mathf.Sin(lt * 23f) * Mathf.Sin(lt * 7f);
                SetColor(shops, flicker, 1f);
            }

            // 横に流した時に画面のはしが見えないよう、少し大きくしておく
            Zoom(c, 1.1f, Vector2.zero);
        }

        // Cut3：空にレコード盤の形の黒い飛行船が現れる
        private void AnimateCut3(OpeningCutView c, float lt, float u)
        {
            foreach (OpeningLayerView layer in c.layers)
            {
                if (layer == null) continue;
                if (layer.name == "airship" || layer.name == "searchlights" || layer.name == "notes" || layer.name == "darken") continue;
                Move(layer, new Vector2(Parallax(layer.name, u) * 0.6f, 0f));
            }

            // 空がだんだん暗くなる
            SetColor(c.Find("darken"), 1f, Smooth(lt / 2f));

            // 飛行船が上から降りてきて、ふわふわ浮かぶ
            OpeningLayerView airship = c.Find("airship");
            if (airship != null)
            {
                float arrive = EaseOut(lt / 3f);
                float y = Mathf.Lerp(420f, 0f, arrive) + Mathf.Sin(lt * 1.6f) * 6f;
                float s = Mathf.Lerp(0.8f, 1f, arrive);
                Pose(airship, new Vector2(0f, y), Mathf.Sin(lt * 1.1f) * 1.5f, s, airship.boundsCenter);
            }

            // サーチライトがついて、左右にゆれる
            OpeningLayerView lights = c.Find("searchlights");
            if (lights != null)
            {
                float a = Smooth((lt - 0.8f) / 0.8f) * (0.85f + 0.15f * Mathf.Sin(lt * 8f));
                Pose(lights, Vector2.zero, Mathf.Sin(lt * 1.3f) * 4f, 1f, new Vector2(0f, -600f));
                SetColor(lights, 1f, a);
            }

            // 音符が落ちながら消えていく
            OpeningLayerView notes = c.Find("notes");
            if (notes != null)
            {
                Move(notes, new Vector2(0f, -lt * 40f));
                SetColor(notes, 1f, 1f - Smooth(lt / 2.2f));
            }

            Zoom(c, Mathf.Lerp(1.1f, 1.16f, Smooth(u)), new Vector2(0f, 250f));
        }

        // Cut4：レコードが吸い上げられ、街の色が右から灰色に消えていく
        private void AnimateCut4(OpeningCutView c, float lt, float u)
        {
            // 灰色の範囲を右から広げる
            float p = Smooth((lt - wipeDelay) / Mathf.Max(0.01f, wipeSeconds));
            float width = p * (ScreenW + WipeOverhang * 2f);
            if (wipeMask != null) wipeMask.sizeDelta = new Vector2(width, 0f);
            if (wipeEdge != null)
            {
                float leftEdge = ScreenW * 0.5f + WipeOverhang - width;
                wipeEdge.rectTransform.anchoredPosition = new Vector2(leftEdge + 40f, 0f);
                SetImageAlpha(wipeEdge, (p > 0.001f && p < 0.999f) ? 0.85f : 0f);
            }

            // 色ありと灰色の2枚を、同じように動かす
            for (int i = 0; i < 2; i++)
            {
                string suffix = i == 0 ? "" : "_gray";

                OpeningLayerView beam = c.Find("beam" + suffix);
                if (beam != null)
                {
                    float pulse = Mathf.Sin(lt * 9f);
                    Pose(beam, Vector2.zero, 0f, 1f + 0.015f * pulse, Top(beam));
                    SetColor(beam, 1f, 0.8f + 0.2f * pulse);
                }

                OpeningLayerView records = c.Find("flying_records" + suffix);
                if (records != null)
                {
                    Pose(records, new Vector2(0f, lt * 35f), Mathf.Sin(lt * 2f) * 2f, 1f + lt * 0.02f, records.boundsCenter);
                }

                Move(c.Find("airship" + suffix), new Vector2(0f, Mathf.Sin(lt * 1.6f) * 6f));
            }

            Zoom(c, Mathf.Lerp(1.04f, 1.1f, Smooth(u)), new Vector2(0f, 200f));
        }

        // Cut5：タワーの巨大モニターに、ミスター・ミュートのシルエット
        private void AnimateCut5(OpeningCutView c, float lt, float u)
        {
            OpeningLayerView mute = c.Find("mr_mute");
            if (mute != null)
            {
                // 映りの悪いモニターのように、チカチカしながら映る
                float a = Smooth((lt - 0.4f) / 0.8f);
                if (lt < 1.6f && Mathf.Repeat(lt * 11f, 1f) < 0.35f) a *= 0.35f;
                Move(mute, new Vector2(0f, Mathf.Sin(lt * 1.5f) * 3f));
                SetColor(mute, 1f, a);
            }

            Zoom(c, Mathf.Lerp(1f, 1.12f, Smooth(u)), new Vector2(0f, 180f));
        }

        // Cut6：灰色のレコード屋。コバンが最後の金のメダルを差し出す
        private void AnimateCut6(OpeningCutView c, float lt, float u)
        {
            OpeningLayerView koban = c.Find("koban");
            if (koban != null)
            {
                float x = Mathf.Lerp(50f, 0f, EaseOut(lt / 1f));
                Pose(koban, new Vector2(x, 0f), 0f, 1f + 0.012f * Mathf.Sin(lt * 2.2f), Feet(koban));
            }

            // メダルだけ色があって、差し出されてキラキラ光る
            OpeningLayerView medal = c.Find("medal");
            Vector2 focus = Vector2.zero;
            if (medal != null)
            {
                float y = Mathf.Lerp(-40f, 0f, EaseBack((lt - 0.3f) / 0.9f));
                Pose(medal, new Vector2(0f, y), Mathf.Sin(lt * 1.7f) * 4f, 1f + 0.04f * Mathf.Sin(lt * 4f), medal.boundsCenter);
                focus = medal.boundsCenter;
            }

            Zoom(c, Mathf.Lerp(1f, 1.08f, Smooth(u)), focus);
        }

        // Cut7：ゲームセンターの地下。筐体の布が落ち、頭の止まったヴィニーが見える
        private void AnimateCut7(OpeningCutView c, float lt, float u)
        {
            // 蛍光灯：つく前は暗い → チカチカ → つく
            float lightOn;
            if (lt < lightTime) lightOn = 0f;
            else if (lt < lightTime + 0.45f) lightOn = Mathf.Repeat(lt * 14f, 1f) < 0.5f ? 1f : 0.2f;
            else lightOn = 1f;
            float bright = Mathf.Lerp(0.45f, 1f, lightOn);

            foreach (OpeningLayerView layer in c.layers) SetColor(layer, bright, 1f);
            SetColor(c.Find("lamp"), 1f, Mathf.Lerp(0.2f, 1f, lightOn));

            // かぶっていた布がずり落ちる
            OpeningLayerView clothBefore = c.Find("cloth_before");
            if (clothBefore != null)
            {
                float f = Clamp01((lt - clothTime) / 0.8f);
                Pose(clothBefore, new Vector2(0f, -EaseIn(f) * 650f), -f * 5f, 1f, clothBefore.boundsCenter);
                SetColor(clothBefore, bright, 1f - Smooth((f - 0.55f) / 0.45f));
            }

            // 落ちた後の布が床に現れる
            OpeningLayerView cloth = c.Find("cloth");
            if (cloth != null)
            {
                float a = Smooth((lt - clothTime - 0.45f) / 0.35f);
                Move(cloth, new Vector2(0f, (1f - a) * 80f));
                SetColor(cloth, bright, a);
            }

            // ほこりが舞い上がって、ゆっくり消える
            OpeningLayerView dust = c.Find("dust");
            if (dust != null)
            {
                float a = Smooth((lt - clothTime - 0.4f) / 0.4f) * (1f - Smooth((lt - 4.5f) / 1.2f));
                Move(dust, new Vector2(0f, Mathf.Max(0f, lt - clothTime) * 14f));
                SetColor(dust, 1f, a);
            }

            // 最後にヴィニーへぐっと寄る
            OpeningLayerView vinny = c.Find("vinny_stopped");
            Vector2 focus = vinny != null ? vinny.boundsCenter : Vector2.zero;
            Zoom(c, Mathf.Lerp(1f, 1.3f, EaseIn((lt - 4f) / 2f)), focus);
        }

        // Cut8：メダルを入れると、メダリオが転がり出て、ヴィニーの頭が回り「ドン！」と曲が戻る
        private void AnimateCut8(OpeningCutView c, float lt, float u)
        {
            float after = lt - dropTime;   // 「ドン！」から何秒か（マイナスならまだ前）
            bool dropped = after >= 0f;
            float beat = Beat(Mathf.Max(0f, after));
            float bright = dropped ? 1f : 0.5f;

            foreach (OpeningLayerView layer in c.layers) SetColor(layer, bright, 1f);

            // 背景の集中線がゆっくり回る（集中線が別の画像になっている時だけ）
            OpeningLayerView burst = c.Find("burst_lines");
            if (burst != null)
            {
                Pose(burst, Vector2.zero, -lt * 25f, 2.3f, Vector2.zero);
                SetColor(burst, bright, dropped ? 1f : 0.3f);
            }

            // 後ろの光：「ドン！」の後、リズムに合わせて光る
            OpeningLayerView glow = c.Find("glow");
            if (glow != null)
            {
                float hop = Bounce(beat);
                Pose(glow, Vector2.zero, 0f, 1f + 0.05f * hop, glow.boundsCenter);
                SetColor(glow, 1f, dropped ? 0.65f + 0.35f * hop : 0f);
            }

            // ヴィニー：「ドン！」の後、リズムに合わせて跳ねる
            OpeningLayerView vinny = c.Find("vinny");
            if (vinny != null && dropped)
            {
                float hop = Bounce(beat);
                Pose(vinny, new Vector2(0f, hop * 14f), 0f, 1f + 0.03f * hop, Feet(vinny));
            }

            // 頭が回っている線：「ドン！」の少し前から出て、くるくる回る
            OpeningLayerView spin = c.Find("spin_lines");
            if (spin != null)
            {
                Pose(spin, Vector2.zero, -lt * 540f, 1f, spin.boundsCenter);
                SetColor(spin, 1f, Smooth((lt - (dropTime - 0.6f)) / 0.3f));
            }

            // メダルを持った手：入ってきて、メダルを入れたら下がる
            OpeningLayerView hand = c.Find("hand_medal");
            if (hand != null)
            {
                float into = EaseOut(lt / 0.7f);
                float away = Smooth((lt - 1.0f) / 0.5f);
                Move(hand, new Vector2(Mathf.Lerp(-320f, 0f, into) - away * 200f, -away * 120f));
                SetColor(hand, bright, 1f - away);
            }

            // メダリオ：右から転がってきて、「ドン！」の後は跳ねる
            OpeningLayerView medario = c.Find("medario");
            if (medario != null)
            {
                float f = Clamp01((lt - 1.3f) / 0.9f);
                float x = Mathf.Lerp(520f, 0f, EaseOut(f));
                float angle = (1f - EaseOut(f)) * 720f;
                float y = dropped ? Bounce(beat + 0.5f) * 10f : 0f;
                Pose(medario, new Vector2(x, y), angle, 1f, medario.boundsCenter);
                SetColor(medario, bright, lt < 1.3f ? 0f : 1f);
            }

            // 「DROP」の文字：大きい所からドンと落ちてくる
            OpeningLayerView dropText = c.Find("drop_text");
            if (dropText != null)
            {
                float e = Clamp01(after / 0.3f);
                Pose(dropText, Vector2.zero, 0f, Mathf.Lerp(2.6f, 1f, EaseBack(e)), dropText.boundsCenter);
                SetColor(dropText, 1f, dropped ? Smooth(after / 0.1f) : 0f);
            }

            // 吹き出し：ポンと出る
            OpeningLayerView bubble = c.Find("bubble");
            if (bubble != null)
            {
                float e = Clamp01((lt - (c.captionStart - 0.3f)) / 0.3f);
                Pose(bubble, Vector2.zero, 0f, Mathf.Max(0.001f, EaseBack(e)), bubble.boundsCenter);
                SetColor(bubble, 1f, e > 0f ? 1f : 0f);
            }

            Zoom(c, dropped ? 1f + 0.02f * Bounce(beat) : 1f, Vector2.zero);
        }

        // Cut9：筐体から色が広がる。エリアの幹部たちのシルエットが並ぶ
        private void AnimateCut9(OpeningCutView c, float lt, float u)
        {
            // 背景がだんだん明るくなる
            float bright = Mathf.Lerp(0.35f, 1f, Smooth(lt / 1.2f));
            SetColor(c.Find("sky"), bright, 1f);
            SetColor(c.Find("city"), bright, 1f);
            SetColor(c.Find("floor"), bright, 1f);

            // 色の輪が何度も広がる
            OpeningLayerView ring = c.Find("color_ring");
            if (ring != null)
            {
                float phase = Mathf.Repeat(lt / 1.8f, 1f);
                Pose(ring, Vector2.zero, 0f, Mathf.Lerp(0.25f, 2.4f, EaseOut(phase)), CircleCenter(ring));
                SetColor(ring, 1f, 1f - phase);
            }

            // 幹部たちが1人ずつドンと出てくる
            for (int i = 1; i <= 4; i++)
            {
                OpeningLayerView boss = c.Find("boss" + i);
                if (boss == null) continue;
                float t0 = 1.0f + 0.35f * (i - 1);
                float e = Clamp01((lt - t0) / 0.45f);
                float breathe = e >= 1f ? 1f + 0.01f * Mathf.Sin(lt * 2f + i) : 1f;
                Pose(boss, new Vector2(0f, Mathf.Lerp(-90f, 0f, EaseBack(e))), 0f, breathe, Feet(boss));
                SetColor(boss, 1f, Smooth(e / 0.4f));
            }

            // 音符がふわふわ上がる
            OpeningLayerView notes = c.Find("notes");
            if (notes != null)
            {
                Move(notes, new Vector2(Mathf.Sin(lt * 1.4f) * 12f, lt * 12f));
                SetColor(notes, 1f, Smooth(lt / 1f));
            }

            Zoom(c, Mathf.Lerp(1f, 1.06f, Smooth(u)), Vector2.zero);
        }

        // Cut10：ロゴが登場し、エリア1の最初のラウンドへ
        private void AnimateCut10(OpeningCutView c, float lt, float u)
        {
            float impact = logoTime + logoSlamSeconds;
            float beat = Beat(c.start + lt);

            SetColor(c.Find("sky"), 0.85f, 1f);
            SetColor(c.Find("floor"), 0.85f, 1f);
            SetColor(c.Find("darken"), 1f, Mathf.Lerp(1f, 0.45f, Smooth(lt / 1f)));

            // 後ろの光の筋：ロゴが落ちたら出て、ゆっくり回る
            OpeningLayerView rays = c.Find("rays");
            if (rays != null)
            {
                float a = Smooth((lt - impact) / 0.3f) * (0.8f + 0.2f * Mathf.Sin(lt * 3f));
                Pose(rays, Vector2.zero, lt * 5f + Mathf.Sin(lt * 0.8f) * 4f, 1.5f + 0.03f * Bounce(beat), new Vector2(0f, 40f));
                SetColor(rays, 1f, a);
            }

            // ロゴ：大きい所からドンと落ちて、少しはねてから、ゆっくり呼吸する
            OpeningLayerView logo = c.Find("logo") ?? c.Find("logo_temp");
            if (logo != null)
            {
                float e = Clamp01((lt - logoTime) / Mathf.Max(0.01f, logoSlamSeconds));
                float afterImpact = lt - impact;
                float s = afterImpact < 0f
                    ? Mathf.Lerp(3f, 1f, e * e)
                    : 1f + Mathf.Sin(afterImpact * 18f) * Mathf.Exp(-afterImpact * 7f) * 0.06f + 0.012f * Mathf.Sin(lt * 3f);
                Pose(logo, Vector2.zero, 0f, s, logo.boundsCenter);
                SetColor(logo, 1f, lt < logoTime ? 0f : Smooth(e / 0.3f));
            }

            // 下の文字：点滅する
            OpeningLayerView roundText = c.Find("round_text");
            if (roundText != null)
            {
                float a = 0f;
                if (lt >= roundTextTime) a = Mathf.Repeat((lt - roundTextTime) * 1.4f, 1f) < 0.72f ? 1f : 0.25f;
                SetColor(roundText, 1f, a);
            }
        }

        // =====================================================================
        // せりふ
        // =====================================================================

        /// <summary>せりふの枠（caption の画像）を、せりふの少し前にふわっと出す</summary>
        private static void AnimateCaptionBox(OpeningCutView c, float lt)
        {
            OpeningLayerView box = c.Find("caption");
            if (box == null) return;
            float a = Smooth((lt - (c.captionStart - 0.35f)) / 0.3f);
            Move(box, new Vector2(0f, -(1f - a) * 24f));
            Color col = box.image != null ? box.image.color : Color.white;
            SetColor(box, col.r, a);
        }

        private void UpdateCaption(OpeningCutView c, float lt)
        {
            if (captionRoot == null) return;

            bool show = c != null && c.captions != null && c.captions.Count > 0 && lt >= c.captionStart;
            if (!show)
            {
                if (captionRoot.gameObject.activeSelf) captionRoot.gameObject.SetActive(false);
                captionKey = -1;
                lastVisibleChars = -1;
                return;
            }

            float lineSeconds = Mathf.Max(0.1f, captionLineSeconds);
            int index = Mathf.Min(c.captions.Count - 1, Mathf.FloorToInt((lt - c.captionStart) / lineSeconds));
            float lineStart = c.captionStart + index * lineSeconds;

            int key = c.number * 100 + index;
            if (key != captionKey)
            {
                captionKey = key;
                LayoutCaption(c, c.captions[index]);
                lastVisibleChars = -1;
            }
            if (!captionRoot.gameObject.activeSelf) captionRoot.gameObject.SetActive(true);

            // 1文字ずつ出す
            if (captionBody != null)
            {
                int total = captionBody.text.Length;
                int visible = Mathf.Clamp(Mathf.FloorToInt((lt - lineStart) / Mathf.Max(0.01f, charInterval)) + 1, 0, total);
                captionBody.maxVisibleCharacters = visible;
                if (visible > lastVisibleChars && lastVisibleChars >= 0 && typeBlipClip != null && seSource != null && !skipping)
                {
                    seSource.PlayOneShot(typeBlipClip, typeBlipVolume);
                }
                lastVisibleChars = visible;
            }
        }

        /// <summary>
        /// せりふの文字を置く場所を決める。
        ///   せりふ枠の画像（caption）があるカット → その枠の中
        ///   吹き出し（bubble）があるカット       → 吹き出しの中
        ///   どちらも無いカット                   → 画面の下に、自前の黒い枠を出す
        /// 文字は枠の画像の子にするので、カメラが寄っても枠と一緒に動く。
        /// </summary>
        private void LayoutCaption(OpeningCutView c, OpeningCaptionLine line)
        {
            OpeningLayerView anchor = c.Find("caption");
            int mode = 0;
            if (anchor == null)
            {
                anchor = c.Find("bubble");
                mode = anchor != null ? 1 : 2;
            }

            captionRoot.anchorMin = captionRoot.anchorMax = captionRoot.pivot = new Vector2(0.5f, 0.5f);
            if (mode < 2)
            {
                captionRoot.SetParent(anchor.rect, false);
                captionRoot.anchoredPosition = anchor.boundsCenter - anchor.restCenter + (mode == 1 ? new Vector2(0f, 8f) : Vector2.zero);
                captionRoot.sizeDelta = mode == 1 ? anchor.boundsSize * 0.86f : anchor.boundsSize;
            }
            else
            {
                captionRoot.SetParent(captionHome != null ? captionHome : stageRoot, false);
                captionRoot.anchoredPosition = new Vector2(0f, -392f);
                captionRoot.sizeDelta = new Vector2(1540f, 180f);
            }
            captionRoot.localRotation = Quaternion.identity;
            captionRoot.localScale = Vector3.one;
            captionRoot.SetAsLastSibling();

            if (captionFallbackBox != null) captionFallbackBox.gameObject.SetActive(mode == 2);

            bool showSpeaker = mode != 1 && !string.IsNullOrEmpty(line.speaker);
            if (captionSpeaker != null)
            {
                captionSpeaker.gameObject.SetActive(showSpeaker);
                captionSpeaker.text = SafeText(line.speaker, captionSpeaker.font);
                captionSpeaker.color = speakerColor;
                captionSpeaker.fontSize = 30f;
                captionSpeaker.alignment = TextAlignmentOptions.TopLeft;
                RectTransform rt = captionSpeaker.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(48f, -16f);
                rt.sizeDelta = new Vector2(-96f, 44f);
            }

            if (captionBody != null)
            {
                captionBody.text = SafeText(line.text, captionBody.font);
                captionBody.maxVisibleCharacters = 0;
                captionBody.enableWordWrapping = true;
                captionBody.fontSize = mode == 1 ? 40f : 46f;
                captionBody.alignment = mode == 1 ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
                captionBody.color = mode == 1 ? bubbleTextColor : Color.white;
                RectTransform rt = captionBody.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = mode == 1 ? new Vector2(24f, 16f) : new Vector2(56f, 18f);
                rt.offsetMax = mode == 1 ? new Vector2(-24f, -16f) : new Vector2(-56f, showSpeaker ? -58f : -18f);
            }
        }

        /// <summary>
        /// フォントに無い文字を、近い文字に置きかえる（無い文字は □ になってしまうため）。
        /// 例：“ ” → 「 」、… → ...
        /// </summary>
        private static string SafeText(string s, TMP_FontAsset font)
        {
            if (string.IsNullOrEmpty(s) || font == null) return s ?? "";

            if (!font.HasCharacter('“') || !font.HasCharacter('”'))
            {
                s = s.Replace('“', '「').Replace('”', '」');
            }

            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (ch == '\n' || ch == ' ' || font.HasCharacter(ch))
                {
                    sb.Append(ch);
                    continue;
                }
                switch (ch)
                {
                    case '…': sb.Append("..."); break;
                    case '！': sb.Append('!'); break;
                    case '？': sb.Append('?'); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        // =====================================================================
        // 画面全体の効果（暗転・白い光・揺れ・ブラウン管）
        // =====================================================================

        private void UpdateScreenEffects()
        {
            float total = TotalSeconds;

            // 暗転：再生の始まり、カットの切りかわり、最後、スキップした時
            float fade = 1f - Smooth(playClock / StartFadeSeconds);
            if (cutDipSeconds > 0f)
            {
                for (int i = 1; i < cuts.Count; i++)
                {
                    if (cuts[i] == null) continue;
                    fade = Mathf.Max(fade, 1f - Mathf.Abs(time - cuts[i].start) / cutDipSeconds);
                }
            }
            fade = Mathf.Max(fade, Smooth((time - (total - EndFadeSeconds)) / EndFadeSeconds));
            if (skipping) fade = Mathf.Max(fade, Clamp01(skipTimer / SkipFadeSeconds));
            SetImageAlpha(fadeImage, Clamp01(fade));

            // 白い光：針が落ちた時（弱く）、「ドン！」の時、ロゴが落ちた時
            float dropAt = AbsTime(8, dropTime);
            float logoAt = AbsTime(10, logoTime + logoSlamSeconds);
            float needleAt = AbsTime(1, needleTime);
            float brakeAt = AbsTime(4, brakeStart);
            float flash = Impulse(time, dropAt, 5f) + Impulse(time, logoAt, 5f) + 0.3f * Impulse(time, needleAt, 8f);
            SetImageAlpha(flashImage, Clamp01(flash * flashStrength));

            // 揺れ
            float shake = Impulse(time, dropAt, 4f) + Impulse(time, logoAt, 5f) + 0.35f * Impulse(time, brakeAt, 2.5f);
            if (stageRoot != null)
            {
                float amount = shake * shakeStrength;
                float x = (Mathf.PerlinNoise(time * 28f, 0.3f) - 0.5f) * 2f * amount;
                float y = (Mathf.PerlinNoise(0.7f, time * 28f) - 0.5f) * 2f * amount;
                stageRoot.anchoredPosition = new Vector2(x, y);
            }

            SetImageAlpha(vignette, vignetteAlpha);
            SetImageAlpha(scanlines, scanlineAlpha);
        }

        // =====================================================================
        // 音
        // =====================================================================

        /// <summary>
        /// 曲の流れ：
        ///   針が落ちる → 曲スタート → Cut3 で音程が下がる → Cut4 でブレーキ → 無音
        ///   → Cut8 の「ドン！」で曲が戻る → 最後にだんだん小さくなる
        /// </summary>
        private void UpdateBgm()
        {
            if (bgmSource == null || bgmClip == null) return;

            float total = TotalSeconds;
            float brakeAt = AbsTime(4, brakeStart);
            float silentAt = brakeAt + Mathf.Max(0.01f, brakeSeconds);
            float dropAt = AbsTime(8, dropTime);
            float maxTime = Mathf.Max(0f, bgmClip.length - 0.05f);

            int phase;
            if (time < bgmStartTime) phase = 0;       // まだ鳴っていない
            else if (time < silentAt) phase = 1;      // 最初の曲
            else if (time < dropAt) phase = 2;        // 無音
            else phase = 3;                           // 曲が戻った後

            if (phase != bgmPhase)
            {
                bgmPhase = phase;
                if (phase == 1)
                {
                    bgmSource.clip = bgmClip;
                    bgmSource.time = Mathf.Clamp(time - bgmStartTime, 0f, maxTime);
                    bgmSource.Play();
                }
                else if (phase == 3)
                {
                    bgmSource.clip = bgmClip;
                    bgmSource.time = Mathf.Clamp(bgmDropTime + (time - dropAt), 0f, maxTime);
                    bgmSource.Play();
                }
                else
                {
                    bgmSource.Stop();
                }
            }

            float pitch = 1f;
            float volume = 1f;
            if (phase == 1)
            {
                OpeningCutView cut3 = CutByNumber(3);
                if (cut3 != null && time > cut3.start)
                {
                    pitch = Mathf.Lerp(1f, 0.85f, Smooth((time - cut3.start) / cut3.Duration));
                }
                if (time > brakeAt)
                {
                    float b = Clamp01((time - brakeAt) / Mathf.Max(0.01f, brakeSeconds));
                    pitch = Mathf.Lerp(pitch, 0.05f, b * b);
                    volume = 1f - b;
                }
            }
            else if (phase == 3)
            {
                volume = 1f - Smooth((time - (total - 2f)) / 2f);
            }
            if (skipping) volume *= 1f - Clamp01(skipTimer / SkipFadeSeconds);

            bgmSource.pitch = Mathf.Max(0.05f, pitch);
            bgmSource.volume = bgmVolume * volume;
        }

        /// <summary>予定表の秒数を通り過ぎた効果音を鳴らす</summary>
        private void FireSoundCues()
        {
            if (seSource == null || skipping) return;
            foreach (OpeningSoundCue cue in soundCues)
            {
                if (cue == null || cue.clip == null) continue;
                if (prevTime < cue.time && cue.time <= time)
                {
                    seSource.PlayOneShot(cue.clip, cue.volume);
                }
            }
        }

        // =====================================================================
        // 便利な道具
        // =====================================================================

        /// <summary>
        /// レイヤーを動かす。
        ///   offset … ずらす量（ピクセル）
        ///   angle  … 回す角度（度。プラスで左回り）
        ///   scale  … 大きさ（1 がそのまま）
        ///   pivot  … 回したり大きくしたりする時の中心（画面の真ん中が 0,0）
        /// </summary>
        private static void Pose(OpeningLayerView l, Vector2 offset, float angle, float scale, Vector2 pivot)
        {
            if (l == null || l.rect == null) return;
            Quaternion rot = Quaternion.Euler(0f, 0f, angle);
            Vector2 fromPivot = (l.restCenter - pivot) * scale;
            Vector2 newCenter = pivot + (Vector2)(rot * (Vector3)fromPivot) + offset;
            l.rect.anchoredPosition = l.basePos + (newCenter - l.restCenter);
            l.rect.localRotation = rot;
            l.rect.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>レイヤーをずらすだけ</summary>
        private static void Move(OpeningLayerView l, Vector2 offset)
        {
            if (l == null) return;
            Pose(l, offset, 0f, 1f, l.restCenter);
        }

        /// <summary>明るさ（0〜1）と濃さ（0〜1）を変える</summary>
        private static void SetColor(OpeningLayerView l, float bright, float alpha)
        {
            if (l == null || l.image == null) return;
            l.image.color = new Color(bright, bright, bright, Clamp01(alpha));
        }

        private static void SetImageAlpha(Graphic g, float alpha)
        {
            if (g == null) return;
            Color c = g.color;
            c.a = alpha;
            g.color = c;
        }

        /// <summary>カメラが寄る（focus の場所を止めたまま、カット全体を大きくする）</summary>
        private static void Zoom(OpeningCutView c, float scale, Vector2 focus)
        {
            c.root.localScale = new Vector3(scale, scale, 1f);
            c.root.anchoredPosition = focus * (1f - scale);
        }

        /// <summary>奥行き用：名前ごとに、横に流れる量（ピクセル）</summary>
        private static float Parallax(string layerName, float u)
        {
            float depth;
            switch (layerName)
            {
                case "sky": depth = 12f; break;
                case "city_far": depth = 25f; break;
                case "city_near": depth = 45f; break;
                case "searchlights": depth = 45f; break;
                case "floor":
                case "shops":
                case "neon": depth = 60f; break;
                case "notes": depth = 75f; break;
                default: depth = 0f; break;
            }
            return -(u - 0.5f) * 2f * depth;
        }

        /// <summary>絵の足もと（下のまん中）</summary>
        private static Vector2 Feet(OpeningLayerView l) => new Vector2(l.boundsCenter.x, l.boundsCenter.y - l.boundsSize.y * 0.5f);

        /// <summary>絵の上のまん中</summary>
        private static Vector2 Top(OpeningLayerView l) => new Vector2(l.boundsCenter.x, l.boundsCenter.y + l.boundsSize.y * 0.5f);

        /// <summary>丸い絵の中心（下が画面からはみ出していても、横はばから計算する）</summary>
        private static Vector2 CircleCenter(OpeningLayerView l) => new Vector2(l.boundsCenter.x, l.boundsCenter.y + l.boundsSize.y * 0.5f - l.boundsSize.x * 0.5f);

        private float Beat(float seconds) => seconds * bpm / 60f;

        /// <summary>拍に合わせて 0 → 1 → 0 と跳ねる値</summary>
        private static float Bounce(float beat) => Mathf.Abs(Mathf.Sin(beat * Mathf.PI));

        /// <summary>at の瞬間に 1 になって、だんだん 0 に戻る値</summary>
        private static float Impulse(float t, float at, float speed) => t < at ? 0f : Mathf.Exp(-(t - at) * speed);

        private static float Clamp01(float x) => Mathf.Clamp01(x);

        /// <summary>ゆっくり始まって、ゆっくり止まる</summary>
        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>速く始まって、ゆっくり止まる</summary>
        private static float EaseOut(float x)
        {
            x = Mathf.Clamp01(x);
            float a = 1f - x;
            return 1f - a * a * a;
        }

        /// <summary>ゆっくり始まって、速くなる</summary>
        private static float EaseIn(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x;
        }

        /// <summary>少し行き過ぎてから戻る（ポンと出る感じ）</summary>
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
