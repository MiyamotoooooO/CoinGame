using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// 設定画面。全体・BGM・効果音の音の大きさを変えられる（値は GameAudioSettings に保存される）。
    ///
    /// 開き方：
    /// ・タイトル画面の「設定」ボタン（TitleMenu が SettingsMenu.Open() を呼ぶ）
    /// ・ゲーム中に Esc キー（レコード一覧やクリアの確認などが開いていない時だけ）
    /// 閉じ方：「閉じる」ボタンか Esc キー。
    ///
    /// ゲーム中に開いた時は、ゲームを止める（Time.timeScale を 0 にして、メダルの発射も止める）。
    /// シーンに置く必要はない（ゲームを始めると自動で作られ、シーンが変わっても残る）。
    /// 画面の部品は、すべてコードで作る（画像はいらない）。
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class SettingsMenu : MonoBehaviour
    {
        static SettingsMenu instance;

        /// <summary>設定画面が開いているか</summary>
        public static bool IsOpen => instance != null && instance.open;

        /// <summary>設定画面を開く</summary>
        public static void Open()
        {
            Ensure();
            instance.SetOpen(true);
        }

        /// <summary>設定画面を閉じる</summary>
        public static void Close()
        {
            if (instance != null) instance.SetOpen(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate() => Ensure();

        static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("SettingsMenu（自動）");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SettingsMenu>();
        }

        // ───────── 色（タイトル画面に合わせる） ─────────
        static readonly Color Navy = new Color(0.07f, 0.05f, 0.17f, 0.97f);
        static readonly Color NavyLight = new Color(0.17f, 0.13f, 0.30f, 1f);
        static readonly Color Yellow = new Color(1f, 0.82f, 0.25f, 1f);
        static readonly Color Pink = new Color(1f, 0.24f, 0.59f, 1f);
        static readonly Color Cyan = new Color(0.16f, 0.85f, 1f, 1f);
        static readonly Color Cream = new Color(0.95f, 0.93f, 0.99f, 1f);
        static readonly Color Gray = new Color(0.62f, 0.60f, 0.72f, 1f);

        // スライダーの段の数（20 段 = 5 ずつ）
        const float Steps = 20f;

        bool open;
        GameObject root;
        Font font;
        Slider masterSlider, bgmSlider, sfxSlider;
        Text masterValue, bgmValue, sfxValue;
        bool refreshing;

        // テスト音（効果音の大きさを変えた時に鳴らす）
        AudioSource testSource;
        AudioClip testClip;
        float lastTest;

        // ゲームを止める
        float savedTimeScale = -1f;
        readonly List<MedalLauncher> stoppedLaunchers = new List<MedalLauncher>();
        bool waitMouseRelease;

        // ほかの画面が Esc を使ったフレームに、設定が開かないようにする
        RecordRack rack;
        RecordCollectionScreen collection;
        RoundClearBar clearBar;
        bool otherWasOpen;
        float nextFind;

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            MakeTestSound();
            Build();
            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        // ================================================================
        // 開く・閉じる
        // ================================================================

        void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            root.SetActive(value);

            if (value)
            {
                Refresh();
                EnsureEventSystem();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(masterSlider.gameObject);

                // ゲーム中なら、ゲームを止める
                if (FindFirstObjectByType<MedalPusherGame>() != null)
                {
                    if (Time.timeScale > 0f)
                    {
                        savedTimeScale = Time.timeScale;
                        Time.timeScale = 0f;
                    }
                    StopLaunchers();
                }
            }
            else
            {
                GameAudioSettings.Save();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                if (savedTimeScale >= 0f && Time.timeScale == 0f) Time.timeScale = savedTimeScale;
                savedTimeScale = -1f;
                waitMouseRelease = true;   // 「閉じる」を押したクリックで、メダルが出ないように
            }
        }

        /// <summary>メダルの発射を止める（止めている間に、クリックやキーでメダルが出ないように）</summary>
        void StopLaunchers()
        {
            foreach (var l in FindObjectsByType<MedalLauncher>(FindObjectsSortMode.None))
            {
                if (l == null || !l.enabled) continue;
                l.enabled = false;
                stoppedLaunchers.Add(l);
            }
            waitMouseRelease = false;
        }

        void RestartLaunchers()
        {
            foreach (var l in stoppedLaunchers) if (l != null) l.enabled = true;
            stoppedLaunchers.Clear();
        }

        void Update()
        {
            if (open)
            {
                if (InputCompat.Down(GameKey.Cancel)) SetOpen(false);
                return;
            }

            // 閉じたあと、マウスのボタンをはなしてから発射をもどす
            if (waitMouseRelease && !InputCompat.MouseHeld() && !InputCompat.MouseRightHeld())
            {
                waitMouseRelease = false;
                RestartLaunchers();
            }

            // ゲーム中は Esc で開く
            if (InputCompat.Down(GameKey.Cancel) && CanOpenInGame()) SetOpen(true);
        }

        void LateUpdate()
        {
            otherWasOpen = OtherScreenOpen();
        }

        bool CanOpenInGame()
        {
            if (FindFirstObjectByType<MedalPusherGame>() == null) return false;   // タイトルなどは、設定ボタンから開く
            if (Time.timeScale <= 0f) return false;                               // ムービーを流している間など
            if (otherWasOpen || OtherScreenOpen()) return false;                  // Esc でほかの画面を閉じた時
            if (clearBar != null && clearBar.ConfirmClosedFrame == Time.frameCount) return false;
            return true;
        }

        /// <summary>Esc で閉じるほかの画面（レコード一覧・コレクション・クリアの確認）が開いているか</summary>
        bool OtherScreenOpen()
        {
            // 見つかっていない物は、1 秒に 1 回だけ探す（毎フレーム探すと重いので）
            if ((rack == null || collection == null || clearBar == null) && Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 1f;
                if (rack == null) rack = FindFirstObjectByType<RecordRack>();
                if (collection == null) collection = FindFirstObjectByType<RecordCollectionScreen>();
                if (clearBar == null) clearBar = FindFirstObjectByType<RoundClearBar>();
            }
            return (rack != null && rack.IsOpen)
                || (collection != null && collection.IsOpen)
                || (clearBar != null && clearBar.IsConfirmShowing);
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // ================================================================
        // 値
        // ================================================================

        /// <summary>保存されている値を、スライダーに入れる</summary>
        void Refresh()
        {
            refreshing = true;
            masterSlider.value = Mathf.Round(GameAudioSettings.Master * Steps);
            bgmSlider.value = Mathf.Round(GameAudioSettings.Bgm * Steps);
            sfxSlider.value = Mathf.Round(GameAudioSettings.Sfx * Steps);
            refreshing = false;
            UpdateLabels();
        }

        void UpdateLabels()
        {
            masterValue.text = Percent(masterSlider.value);
            bgmValue.text = Percent(bgmSlider.value);
            sfxValue.text = Percent(sfxSlider.value);
        }

        /// <summary>スライダーの値（0〜20）を、0〜100 の数字にする</summary>
        static string Percent(float v) => Mathf.RoundToInt(v / Steps * 100f).ToString();

        void OnMasterChanged(float v)
        {
            if (refreshing) return;
            GameAudioSettings.Master = v / Steps;
            UpdateLabels();
            PlayTest();
        }

        void OnBgmChanged(float v)
        {
            if (refreshing) return;
            GameAudioSettings.Bgm = v / Steps;   // 流れている曲の大きさが、すぐに変わる
            UpdateLabels();
        }

        void OnSfxChanged(float v)
        {
            if (refreshing) return;
            GameAudioSettings.Sfx = v / Steps;
            UpdateLabels();
            PlayTest();
        }

        void OnResetClicked()
        {
            GameAudioSettings.ResetToDefault();
            Refresh();
            PlayTest();
        }

        // ================================================================
        // テスト音
        // ================================================================

        void MakeTestSound()
        {
            testSource = gameObject.AddComponent<AudioSource>();
            testSource.playOnAwake = false;
            testSource.spatialBlend = 0f;
            testSource.ignoreListenerVolume = true;   // 効果音の大きさは自分でかける

            // 短い「ピッ」（四角い波）
            const int rate = 44100;
            int n = (int)(rate * 0.06f);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float f = 1200f * Mathf.Pow(1.25f, t / 0.06f);
                phase = Mathf.Repeat(phase + f / rate, 1f);
                float env = Mathf.Round(Mathf.Exp(-t * 40f) * 15f) / 15f;
                d[i] = (phase < 0.5f ? 0.5f : -0.5f) * env;
            }
            testClip = AudioClip.Create("Sfx_SettingsTest", n, 1, rate, false);
            testClip.SetData(d, 0);
        }

        void PlayTest()
        {
            if (Time.unscaledTime - lastTest < 0.08f) return;   // スライダーを動かしている間に鳴りすぎないように
            lastTest = Time.unscaledTime;
            testSource.PlayOneShot(testClip, 0.6f * GameAudioSettings.SfxGain);
        }

        // ================================================================
        // 画面を組み立てる
        // ================================================================

        void Build()
        {
            var canvasGo = new GameObject("SettingsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;   // いちばん手前
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root = canvasGo;
            var rootRt = (RectTransform)canvasGo.transform;

            // 後ろを暗くする（後ろのボタンなどを押せないようにする）
            var dim = MakeImage("Dim", rootRt, new Color(0f, 0f, 0f, 0.62f), true);
            Stretch(dim.rectTransform);

            // 外の黄色いふちと、中の紺のパネル
            var border = MakeImage("Border", rootRt, Yellow, true);
            Center(border.rectTransform, new Vector2(0f, 0f), new Vector2(820f, 600f));
            var panel = MakeImage("Panel", border.rectTransform, Navy, true);
            Center(panel.rectTransform, Vector2.zero, new Vector2(808f, 588f));
            var p = panel.rectTransform;

            // 見出し
            var title = MakeText("Title", p, "設定", 52, Cream, TextAnchor.MiddleLeft);
            TopLeft(title.rectTransform, new Vector2(60f, -34f), new Vector2(200f, 70f));
            var sub = MakeText("Sub", p, "SETTINGS", 22, Yellow, TextAnchor.MiddleLeft);
            TopLeft(sub.rectTransform, new Vector2(180f, -52f), new Vector2(200f, 40f));
            var line = MakeImage("Line", p, Pink, false);
            TopLeft(line.rectTransform, new Vector2(60f, -116f), new Vector2(688f, 4f));
            var line2 = MakeImage("Line2", p, Cyan, false);
            TopLeft(line2.rectTransform, new Vector2(60f, -122f), new Vector2(240f, 3f));

            // 音の大きさ
            var sound = MakeText("SoundHeader", p, "サウンド", 24, Gray, TextAnchor.MiddleLeft);
            TopLeft(sound.rectTransform, new Vector2(60f, -142f), new Vector2(300f, 36f));
            masterSlider = MakeRow(p, -200f, "全体の音量", out masterValue);
            bgmSlider = MakeRow(p, -284f, "BGM（曲）", out bgmValue);
            sfxSlider = MakeRow(p, -368f, "効果音", out sfxValue);
            masterSlider.onValueChanged.AddListener(OnMasterChanged);
            bgmSlider.onValueChanged.AddListener(OnBgmChanged);
            sfxSlider.onValueChanged.AddListener(OnSfxChanged);

            // ボタン
            var reset = MakeButton(p, "初期値にもどす", new Vector2(60f, -476f), new Vector2(300f, 70f), NavyLight, Yellow);
            reset.onClick.AddListener(OnResetClicked);
            var close = MakeButton(p, "閉じる", new Vector2(448f, -476f), new Vector2(300f, 70f), Yellow, new Color(0.07f, 0.05f, 0.17f));
            close.onClick.AddListener(() => SetOpen(false));

            var hint = MakeText("Hint", p, "Esc キーでも閉じます　／　←→ キーでも音量を変えられます", 18, Gray, TextAnchor.MiddleCenter);
            TopLeft(hint.rectTransform, new Vector2(0f, -552f), new Vector2(808f, 30f));

            // キーの ↑↓ で、スライダーとボタンを順に動けるようにする
            Chain(masterSlider, bgmSlider, sfxSlider, reset, close);
        }

        /// <summary>1 行（名前・スライダー・数字）を作る</summary>
        Slider MakeRow(RectTransform parent, float y, string label, out Text valueText)
        {
            var nameText = MakeText("Label_" + label, parent, label, 30, Cream, TextAnchor.MiddleLeft);
            TopLeft(nameText.rectTransform, new Vector2(60f, y), new Vector2(240f, 56f));

            var go = new GameObject("Slider_" + label, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            TopLeft(rt, new Vector2(300f, y), new Vector2(340f, 56f));
            var slider = go.GetComponent<Slider>();

            // みぞ
            var back = MakeImage("Background", rt, NavyLight, true);
            back.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            back.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            back.rectTransform.sizeDelta = new Vector2(0f, 16f);

            // 入っている所（黄色）
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(rt, false);
            var fa = (RectTransform)fillArea.transform;
            fa.anchorMin = new Vector2(0f, 0.5f);
            fa.anchorMax = new Vector2(1f, 0.5f);
            fa.sizeDelta = new Vector2(0f, 16f);
            var fill = MakeImage("Fill", fa, Yellow, false);
            fill.rectTransform.sizeDelta = Vector2.zero;

            // つまみ（白い四角）
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(rt, false);
            var ha = (RectTransform)handleArea.transform;
            ha.anchorMin = Vector2.zero;
            ha.anchorMax = Vector2.one;
            ha.offsetMin = new Vector2(12f, 0f);
            ha.offsetMax = new Vector2(-12f, 0f);
            var handle = MakeImage("Handle", ha, Cream, true);
            handle.rectTransform.sizeDelta = new Vector2(24f, 0f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = Steps;   // 5 ずつ変わる（←→ キー 1 回で 5）
            slider.wholeNumbers = true;
            var cb = slider.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = Pink;
            cb.selectedColor = Pink;
            cb.pressedColor = Cyan;
            slider.colors = cb;

            valueText = MakeText("Value_" + label, parent, "80", 30, Yellow, TextAnchor.MiddleRight);
            TopLeft(valueText.rectTransform, new Vector2(660f, y), new Vector2(88f, 56f));
            return slider;
        }

        Button MakeButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Color back, Color textColor)
        {
            var img = MakeImage("Button_" + label, parent, back, true);
            TopLeft(img.rectTransform, pos, size);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1f, 0.75f, 0.85f);
            cb.selectedColor = new Color(1f, 0.75f, 0.85f);
            cb.pressedColor = new Color(0.7f, 0.95f, 1f);
            b.colors = cb;
            var t = MakeText("Text", img.rectTransform, label, 30, textColor, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform);
            return b;
        }

        /// <summary>↑↓ キーで動く順番を決める</summary>
        static void Chain(params Selectable[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnUp = items[(i - 1 + items.Length) % items.Length];
                nav.selectOnDown = items[(i + 1) % items.Length];
                if (items[i] is Button && i + 1 < items.Length && items[i + 1] is Button) nav.selectOnRight = items[i + 1];
                if (items[i] is Button && i - 1 >= 0 && items[i - 1] is Button) nav.selectOnLeft = items[i - 1];
                items[i].navigation = nav;
            }
        }

        Image MakeImage(string name, RectTransform parent, Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        Text MakeText(string name, RectTransform parent, string text, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void Center(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>親の左上を基準に置く（下はマイナス）</summary>
        static void TopLeft(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
