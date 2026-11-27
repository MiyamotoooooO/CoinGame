using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// チュートリアルの間だけ、ほかの画面の操作を止めるための合図（TutorialGuide が出す）。
    /// ・BlockProceed：クリア後の帯の「閉じる」・Enter キー・ショップを閉じる を止める（RoundClearBar.IsConfirmOpen に入る）
    /// ・ShopFocusCard：0 以上の間、ショップではこの番号のカードしか押せない（ほかを押すと ShopLockMessage を出す）
    /// ・PointerOverPanel：カーソルが説明の枠の上にある（枠のボタンを押した時に、後ろのショップまで押さないように）
    /// </summary>
    public static class TutorialGate
    {
        public static bool BlockProceed;
        public static int ShopFocusCard = -1;
        public static string ShopLockMessage = "";
        public static bool PointerOverPanel;

        public static void Clear()
        {
            BlockProceed = false;
            ShopFocusCard = -1;
            ShopLockMessage = "";
            PointerOverPanel = false;
        }
    }

    /// <summary>
    /// エリア1のラウンド1で出すチュートリアル。画面の上に説明の枠を出し、矢印で場所を指す。
    ///
    /// 流れ
    ///  1. メダルの打ち方（何枚か打つと次へ）
    ///  2. 中央の穴とノルマ
    ///  3. グルーヴゲージ
    ///  4. 補充スロット
    ///  （そのまま遊んで、ラウンドをクリアするまで待つ）
    ///  4. クリアしたら「ショップへ入る」を押してもらう（「閉じる」と Enter は止める）
    ///  5. ショップの説明
    ///  6. 射的のレコードを買ってもらう（1 回目は 0pt。ほかのカード・リロール・閉じるは止める）
    ///  7. ショップを閉じてもらう
    ///  8. 次のラウンドで、レコードの棚を開いてもらう
    ///  9. 射的のレコードをプレーヤーへドラッグして流してもらう
    /// 10. Q で発動することの説明 → おわり
    ///
    /// ・「スキップ」でいつでもやめられる。最後まで見るかスキップすると、次からは出さない（PlayerPrefs に記録）
    /// ・もう一度見たい時は、上のメニュー「MedalPusher」→「チュートリアルをもう一度見る」（エディター）
    /// ・何もしなくても、MedalPusherGame があるシーンでは再生した時に自動で付く
    /// ・MedalPusherGame は変えない（通知と公開されている値だけを使う）
    /// </summary>
    [DisallowMultipleComponent]
    public class TutorialGuide : MonoBehaviour
    {
        public const string DoneKey = "MedalPusher.TutorialDone";

        [Tooltip("オンにすると、見終わっていてもエリア1のラウンド1で毎回出す（確かめる用）")]
        public bool showEveryTime = false;
        [Tooltip("打ち方の説明で、何枚打ったら次へ進むか")]
        [Range(1, 20)] public int shotsToContinue = 5;
        [Tooltip("ショップで買ってもらうレコードのキー（RecordData.csv の英名）")]
        public string tutorialRecordKey = "Shooting";

        enum Step
        {
            None, Shoot, Hole, Gauge, Slot, WaitClear,
            GoShop, ShopIntro, BuyRecord, CloseShop,
            OpenRack, LoadRecord, Activate, Done
        }

        Step step = Step.None;
        int shotsInStep;

        MedalPusherGame game;
        RecordEffectSystem effects;
        RecordRack rack;
        GrooveGauge gauge;
        SlotRefill slot;
        bool launcherBlocked;

        // 画面
        RectTransform canvasRect, panel, arrowA, arrowB, divider, dotsRoot;
        CanvasGroup group;
        Text subText, titleText, bodyText, hintText;
        RectTransform nextButton, skipButton;
        Text nextLabel;
        Image[] dots;
        float panelAlpha;
        Sprite arrowSprite, frameSprite, buttonSprite, skipSprite;
        Font font;
        string hintBase = "";

        const float PanelW = 780f;      // 説明の枠の幅
        const float Pad = 28f;          // 枠の内側の余白
        const int StepCount = 11;

        // 色（盤面の説明の枠に近い、こい紺と明るいグレーの縁）
        static readonly Color FrameLight = new Color(0.86f, 0.88f, 0.93f);
        static readonly Color FrameDark = new Color(0.04f, 0.05f, 0.09f);
        static readonly Color FrameFill = new Color(0.10f, 0.12f, 0.19f, 0.97f);
        static readonly Color SubColor = new Color(0.62f, 0.86f, 0.82f);
        static readonly Color BodyColor = new Color(0.92f, 0.92f, 0.95f);
        static readonly Color HintColor = new Color(0.78f, 1f, 0.35f);
        static readonly Color DividerColor = new Color(0.40f, 0.44f, 0.55f, 0.8f);
        static readonly Color NextFill = new Color(1f, 0.82f, 0.28f);
        static readonly Color NextText = new Color(0.14f, 0.10f, 0.04f);
        static readonly Color SkipText = new Color(0.68f, 0.70f, 0.78f);
        static readonly Color DotDone = new Color(0.30f, 0.80f, 0.74f);
        static readonly Color DotNow = new Color(1f, 0.82f, 0.28f);
        static readonly Color DotTodo = new Color(0.30f, 0.33f, 0.42f);
        static readonly Color ArrowColor = new Color(1f, 0.85f, 0.25f);

        // 本文の中で、キーや大事な言葉に付ける色
        const string Key = "<color=#FFD447>";
        const string Em = "<color=#7FE3D6>";
        const string End = "</color>";

        // ───────── 自動で付ける ─────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            SceneManager.sceneLoaded += (scene, mode) => AttachIfNeeded();
            AttachIfNeeded();
        }

        static void AttachIfNeeded()
        {
            if (FindObjectOfType<TutorialGuide>() != null) return;
            if (FindObjectOfType<MedalPusherGame>() == null) return;   // 台があるシーンだけ
            new GameObject("TutorialGuide（自動）").AddComponent<TutorialGuide>();
        }

        bool AlreadyDone => !showEveryTime && PlayerPrefs.GetInt(DoneKey, 0) == 1;

        // ───────── 準備 ─────────

        void Start()
        {
            TutorialGate.Clear();
            game = FindObjectOfType<MedalPusherGame>();
            if (game == null || AlreadyDone)
            {
                enabled = false;
                return;
            }
            game.StateChanged += OnStateChanged;
            game.MedalInserted += OnInserted;
            game.GameReset += OnGameReset;
            Build();
            // すでにラウンド1が始まっていた時
            OnStateChanged(game.State);
        }

        void OnDestroy()
        {
            if (game != null)
            {
                game.StateChanged -= OnStateChanged;
                game.MedalInserted -= OnInserted;
                game.GameReset -= OnGameReset;
            }
            TutorialGate.Clear();
            if (game != null && game.Shop != null) ClearShopOverrides();
            if (launcherBlocked && game != null && game.Launcher != null) game.Launcher.enabled = true;
        }

        bool IsFirstRound => game.AreaIndex == 0 && game.RoundIndex == 0;

        // ───────── ゲームの出来事 ─────────

        void OnStateChanged(GameState state)
        {
            if (step == Step.Done) return;
            switch (state)
            {
                case GameState.Playing:
                    if (step == Step.None && IsFirstRound) SetStep(Step.Shoot);
                    else if (step == Step.CloseShop || step == Step.GoShop || step == Step.ShopIntro || step == Step.BuyRecord)
                        SetStep(Step.OpenRack);   // ショップを閉じて、次のラウンドが始まった
                    break;
                case GameState.Cleared:
                    if (IsFirstRound && step >= Step.Shoot && step <= Step.WaitClear) SetStep(Step.GoShop);
                    break;
                case GameState.Shop:
                    if (step == Step.GoShop) SetStep(Step.ShopIntro);
                    break;
            }
        }

        void OnInserted(Medal m)
        {
            if (step != Step.Shoot) return;
            shotsInStep++;
            if (shotsInStep >= shotsToContinue) SetStep(Step.Hole);
            else SetHint($"{shotsToContinue} 枚打つと次へ　{shotsInStep} / {shotsToContinue}");
        }

        void OnGameReset()
        {
            // やり直したら、最初から（ラウンド1が始まった時にもう一度出す）
            if (step == Step.Done) return;
            TutorialGate.Clear();
            ClearShopOverrides();
            step = Step.None;
            ShowPanel(false);
        }

        // ───────── 進め方 ─────────

        void SetStep(Step next)
        {
            step = next;
            shotsInStep = 0;
            TutorialGate.BlockProceed = next == Step.GoShop || next == Step.ShopIntro || next == Step.BuyRecord;
            TutorialGate.ShopFocusCard = -1;
            TutorialGate.ShopLockMessage = "";
            if (next != Step.None && next != Step.WaitClear && next != Step.Done) GameSfx.Play(GameSfx.Sound.Click);

            switch (next)
            {
                case Step.Shoot:
                    Show("操作", "メダルを打とう",
                        $"{Key}左クリック{End}（{Key}A{End}／{Key}←{End}）で左の発射口、\n" +
                        $"{Key}右クリック{End}（{Key}D{End}／{Key}→{End}）で右の発射口からメダルが出ます。\n" +
                        $"{Key}Space{End} で左右交互。押しっぱなしで続けて打てます。",
                        null, $"{shotsToContinue} 枚打つと次へ　0 / {shotsToContinue}");
                    break;
                case Step.Hole:
                    Show("ルール", "中央の穴をねらおう",
                        $"プッシャーがメダルを手前へ押し出します。\n" +
                        $"{Em}中央の穴{End}に落ちると {Key}10pt{End} ＋ メダルが 1 枚戻ります。{Em}端の穴{End}は 0pt。\n" +
                        $"ポイントが{Em}ノルマ{End}をこえると、ラウンドクリア！",
                        "次へ");
                    break;
                case Step.Gauge:
                    Show("ルール", "グルーヴゲージ",
                        $"メダルが穴に落ちるたびに、この{Em}ゲージ{End}がたまります。\n" +
                        $"たまったゲージで、流している{Em}レコードの効果{End}を発動できます。\n" +
                        $"レコードはショップで手に入ります。",
                        "次へ");
                    break;
                case Step.Slot:
                    Show("ルール", "補充スロット",
                        $"メダルが{Em}中央の穴{End}に入るたびに、{Em}スロット{End}のリールが\n" +
                        $"左から 1 本ずつ止まります。3 つそろうと{Key}あたり{End}！\n" +
                        $"出目ごとに、メダルが出たりゲージが増えたりします。まずはノルマをめざそう！",
                        "次へ");
                    break;
                case Step.WaitClear:
                    ShowPanel(false);
                    break;
                case Step.GoShop:
                    PrepareShop();
                    Show("ショップ", "ラウンドクリア！",
                        $"クリアすると、{Em}ショップ{End}に入れます。\n下の「{Key}ショップへ入る{End}」を押してみよう。",
                        null, "「ショップへ入る」を押そう");
                    break;
                case Step.ShopIntro:
                    Show("ショップ", "ショップへようこそ",
                        $"{Em}エリアのポイント{End}を使って、レコードや投入上限アップを買えます。\n" +
                        $"ポイントは次のエリアに持っていけないので、エリアの中で使い切ろう。",
                        "次へ");
                    break;
                case Step.BuyRecord:
                    if (!SetupBuyFocus())
                    {
                        SetStep(Step.CloseShop);   // もう持っている時などは、買う所をとばす
                        return;
                    }
                    Show("ショップ", "射的のレコードを買おう",
                        $"光っている「{Key}射的{End}」のレコードをクリックしよう。\n" +
                        $"はじめてのお買い物なので {Key}0pt{End} で買えます！",
                        null, "光っているカードをクリック");
                    break;
                case Step.CloseShop:
                    ClearShopOverrides();
                    Show("ショップ", "レコードを手に入れた！",
                        $"買ったレコードは、画面の右はしの{Em}レコード棚{End}に入ります。\n" +
                        $"左上の「{Key}戻る{End}」でショップを閉じて、次のラウンドへ進もう。",
                        null, "「戻る」でショップを閉じよう");
                    break;
                case Step.OpenRack:
                    Show("レコード", "レコード棚を開こう",
                        $"{Key}E{End} キーか、画面の右はしの「{Key}RECORD{End}」のつまみを押すと、\n" +
                        $"持っているレコードの棚が開きます。",
                        null, "棚を開こう");
                    break;
                case Step.LoadRecord:
                    Show("レコード", "プレーヤーにセット",
                        $"棚の「{Key}射的{End}」のレコードを、左の{Em}プレーヤーの欄{End}へドラッグしよう。\n" +
                        $"セットすると曲が流れ始めます。",
                        null, "レコードをドラッグ");
                    break;
                case Step.Activate:
                    Show("レコード", "Q で発動！",
                        $"グルーヴゲージが{Em}矢印の所{End}までたまったら、{Key}Q{End} キー\n" +
                        $"（またはプレーヤーの盤をクリック）で効果を発動できます。\n" +
                        $"効果は曲が終わるまで続きます。これでチュートリアルはおしまい！",
                        "おわる");
                    break;
                case Step.Done:
                    Finish();
                    break;
            }
        }

        /// <summary>「次へ」ボタン。</summary>
        void OnNext()
        {
            switch (step)
            {
                case Step.Hole: SetStep(Step.Gauge); break;
                case Step.Gauge: SetStep(Step.Slot); break;
                case Step.Slot: SetStep(Step.WaitClear); break;
                case Step.ShopIntro: SetStep(Step.BuyRecord); break;
                case Step.Activate: SetStep(Step.Done); break;
            }
        }

        void OnSkip() => SetStep(Step.Done);

        void Finish()
        {
            step = Step.Done;
            TutorialGate.Clear();
            ClearShopOverrides();
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();
            ShowPanel(false);
            if (arrowA != null) arrowA.gameObject.SetActive(false);
            if (arrowB != null) arrowB.gameObject.SetActive(false);
        }

        // ───────── ショップ ─────────

        /// <summary>ショップを開く前に、チュートリアルのレコードを並べ、0pt にしておく。</summary>
        void PrepareShop()
        {
            var shop = game.Shop;
            if (shop == null || shop.Inventory == null || shop.Inventory.Has(tutorialRecordKey)) return;
            shop.pinnedRecordKey = tutorialRecordKey;
            shop.freeRecordKey = tutorialRecordKey;
        }

        bool SetupBuyFocus()
        {
            var shop = game.Shop;
            if (shop == null || shop.Inventory == null || shop.Inventory.Has(tutorialRecordKey)) return false;
            int index = shop.IndexOfOffer(tutorialRecordKey);
            if (index < 0) return false;
            TutorialGate.ShopFocusCard = index;
            TutorialGate.ShopLockMessage = "まずは光っている「射的」のレコードを買ってみよう";
            TutorialGate.BlockProceed = true;
            return true;
        }

        void ClearShopOverrides()
        {
            if (game == null || game.Shop == null) return;
            game.Shop.pinnedRecordKey = "";
            game.Shop.freeRecordKey = "";
        }

        // ───────── 毎フレーム ─────────

        void Update()
        {
            if (game == null || group == null) return;

            // 待っている操作ができたら、次へ
            switch (step)
            {
                case Step.BuyRecord:
                    if (game.Shop != null && game.Shop.Inventory != null && game.Shop.Inventory.Has(tutorialRecordKey))
                        SetStep(Step.CloseShop);
                    break;
                case Step.OpenRack:
                    if (rack == null) rack = FindFirstObjectByType<RecordRack>();
                    if (EffectsLoaded()) SetStep(Step.Activate);
                    else if (rack != null && rack.IsOpen) SetStep(Step.LoadRecord);
                    break;
                case Step.LoadRecord:
                    if (EffectsLoaded()) SetStep(Step.Activate);
                    break;
            }

            bool visible = step != Step.None && step != Step.WaitClear && step != Step.Done;
            // エリアの始まりの演出などの間は隠す
            if (game.State == GameState.Intro) visible = false;
            panelAlpha = Mathf.MoveTowards(panelAlpha, visible ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            group.alpha = panelAlpha;
            group.blocksRaycasts = visible;
            group.interactable = visible;

            // 待っている時の「▶ 〇〇しよう」を点滅させる
            if (hintText != null && hintText.gameObject.activeSelf)
            {
                bool on = Mathf.Repeat(Time.unscaledTime * 1.6f, 1f) < 0.7f;
                hintText.text = (on ? "▶ " : "　 ") + hintBase;
            }

            UpdateArrows(visible);
            UpdateLauncherBlock(visible);
        }

        bool EffectsLoaded()
        {
            if (effects == null) effects = FindFirstObjectByType<RecordEffectSystem>();
            return effects != null && effects.Loaded != null;
        }

        // ───────── 矢印 ─────────

        void UpdateArrows(bool visible)
        {
            Vector2 a = default, b = default;
            bool hasA = false, hasB = false;
            Vector2 dirA = Vector2.down, dirB = Vector2.down;   // 矢印の向き（指す向き）

            if (visible)
            {
                switch (step)
                {
                    case Step.Shoot:
                        if (game.Launcher != null)
                        {
                            hasA = WorldToCanvas(game.Launcher.MuzzlePosition(-1), out a);
                            hasB = WorldToCanvas(game.Launcher.MuzzlePosition(1), out b);
                        }
                        break;
                    case Step.Hole:
                        var hole = GameObject.Find("Hole_Center");
                        if (hole != null)
                        {
                            var col = hole.GetComponentInChildren<Collider>();
                            hasA = WorldToCanvas(col != null ? col.bounds.center : hole.transform.position, out a);
                        }
                        break;
                    case Step.Gauge:
                        if (gauge == null) gauge = FindFirstObjectByType<GrooveGauge>();
                        if (gauge != null) hasA = WorldToCanvas(GaugeCenter(gauge), out a);
                        break;
                    case Step.Slot:
                        if (slot == null) slot = FindFirstObjectByType<SlotRefill>();
                        if (slot != null && slot.Machine != null) hasA = WorldToCanvas(BoundsCenter(slot.Machine), out a);
                        break;
                    case Step.GoShop:
                        if (game.ClearBar != null && game.ClearBar.ShopButtonRect != null)
                            hasA = UiToCanvas(game.ClearBar.ShopButtonRect, out a);
                        break;
                    case Step.OpenRack:
                        if (rack == null) rack = FindFirstObjectByType<RecordRack>();
                        if (rack != null && rack.TabRect != null)
                        {
                            hasA = UiToCanvas(rack.TabRect, out a);
                            dirA = Vector2.right;
                        }
                        break;
                }
            }
            PlaceArrow(arrowA, hasA, a, dirA);
            PlaceArrow(arrowB, hasB, b, dirB);
        }

        /// <summary>ゲージの見た目のまん中（見た目をまとめた箱の中心。無ければ置いた場所）。</summary>
        static Vector3 GaugeCenter(GrooveGauge g) => BoundsCenter(g.transform);

        /// <summary>物の見た目のまん中（見た目をまとめた箱の中心。無ければ置いた場所）。</summary>
        static Vector3 BoundsCenter(Transform tr)
        {
            var rs = tr.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return tr.position;
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b.center;
        }

        /// <summary>矢印を、target を指すように置く。少しはずませる。</summary>
        void PlaceArrow(RectTransform arrow, bool show, Vector2 target, Vector2 dir)
        {
            if (arrow == null) return;
            arrow.gameObject.SetActive(show);
            if (!show) return;
            float bounce = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f)) * 16f;
            arrow.anchoredPosition = target - dir * (46f + bounce);
            // 矢印の絵は下向きなので、dir の向きに回す
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f;
            arrow.localEulerAngles = new Vector3(0f, 0f, angle);
        }

        /// <summary>
        /// 盤面の場所を、このキャンバスの座標（左下が 0）に直す。カメラの後ろなら false。
        /// 画面はゆがませて（LensDistortion）映しているので、そのぶんも計算に入れる（HoverTooltip の逆の計算）。
        /// </summary>
        bool WorldToCanvas(Vector3 world, out Vector2 pos)
        {
            pos = default;
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 v = cam.WorldToViewportPoint(world);
            if (v.z <= 0f) return false;
            Vector2 uv = new Vector2(v.x, v.y);

            var lens = cam.GetComponent<LensDistortion>();
            if (lens != null && lens.isActiveAndEnabled && lens.strength > 0f)
            {
                // 画面の位置 s → カメラの位置 c の式：c = s × (1 + k r²) / (1 + k R²)（中心からの距離で、横は画面の比をかける）
                // c がわかっているので、s をくり返し計算で求める
                float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
                float k = lens.strength;
                float cornerR2 = 0.25f * (aspect * aspect + 1f);
                var c = uv - new Vector2(0.5f, 0.5f);
                c.x *= aspect;
                var s = c;
                for (int i = 0; i < 8; i++)
                    s = c * (1f + k * cornerR2) / (1f + k * Vector2.Dot(s, s));
                s.x /= aspect;
                uv = s + new Vector2(0.5f, 0.5f);
            }

            var size = canvasRect.rect.size;
            pos = new Vector2(uv.x * size.x, uv.y * size.y);
            return true;
        }

        /// <summary>ほかの画面（オーバーレイの Canvas）の UI の真ん中を、このキャンバスの座標に直す。</summary>
        bool UiToCanvas(RectTransform target, out Vector2 pos)
        {
            pos = default;
            if (target == null || !target.gameObject.activeInHierarchy) return false;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 screen = (corners[0] + corners[2]) * 0.5f;
            var size = canvasRect.rect.size;
            pos = new Vector2(screen.x / Mathf.Max(1f, Screen.width) * size.x, screen.y / Mathf.Max(1f, Screen.height) * size.y);
            return true;
        }

        // ───────── クリックでメダルが出ないように ─────────

        void UpdateLauncherBlock(bool visible)
        {
            bool over = visible && panelAlpha > 0.5f &&
                        RectTransformUtility.RectangleContainsScreenPoint(panel, InputCompat.MousePosition(), null);
            TutorialGate.PointerOverPanel = over;
            if (game.Launcher == null) return;
            if (over && !launcherBlocked && game.Launcher.enabled)
            {
                game.Launcher.enabled = false;
                launcherBlocked = true;
            }
            else if (!over && launcherBlocked)
            {
                game.Launcher.enabled = true;
                launcherBlocked = false;
            }
        }

        // ───────── 説明の枠に書く ─────────

        /// <summary>
        /// 説明の枠を出す。sub＝小さい見出し、title＝見出し、body＝本文。
        /// next に文字を入れると「次へ」ボタンを出す。hint に文字を入れると、ボタンの代わりに「▶ 〇〇しよう」を出す。
        /// </summary>
        void Show(string sub, string title, string body, string next, string hint = null)
        {
            subText.text = sub;
            titleText.text = title;
            bodyText.text = body;

            bool hasNext = !string.IsNullOrEmpty(next);
            nextButton.gameObject.SetActive(hasNext);
            if (hasNext) nextLabel.text = next + "  ▶";
            hintText.gameObject.SetActive(!hasNext && !string.IsNullOrEmpty(hint));
            hintBase = hint ?? "";

            UpdateDots();
            Layout();
            ShowPanel(true);
        }

        void SetHint(string hint)
        {
            hintBase = hint ?? "";
        }

        /// <summary>中身の量に合わせて、枠の高さと、下の段（ボタン・ヒント）の位置を決める。</summary>
        void Layout()
        {
            float w = PanelW - Pad * 2f;
            float y = -24f;
            Place(subText.rectTransform, w, 22f, ref y, 4f);
            Place(titleText.rectTransform, w, 40f, ref y, 10f);
            divider.anchoredPosition = new Vector2(Pad, y);
            divider.sizeDelta = new Vector2(w, 2f);
            y -= 14f;
            bodyText.rectTransform.sizeDelta = new Vector2(w, 10f);
            float bodyH = Mathf.Max(30f, bodyText.preferredHeight);
            Place(bodyText.rectTransform, w, bodyH, ref y, 18f);

            // 下の段
            float rowH = 50f;
            float rowCenter = y - rowH * 0.5f;
            nextButton.anchoredPosition = new Vector2(PanelW - Pad - nextButton.sizeDelta.x * 0.5f, rowCenter);
            hintText.rectTransform.anchoredPosition = new Vector2(PanelW - Pad, rowCenter);
            skipButton.anchoredPosition = new Vector2(Pad + skipButton.sizeDelta.x * 0.5f, rowCenter);
            y -= rowH + 22f;

            panel.sizeDelta = new Vector2(PanelW, -y);
        }

        static void Place(RectTransform rt, float w, float h, ref float y, float gap)
        {
            rt.anchoredPosition = new Vector2(Pad, y);
            rt.sizeDelta = new Vector2(w, h);
            y -= h + gap;
        }

        void UpdateDots()
        {
            int n = StepIndex();
            for (int i = 0; i < dots.Length; i++)
                dots[i].color = i < n - 1 ? DotDone : (i == n - 1 ? DotNow : DotTodo);
        }

        int StepIndex()
        {
            switch (step)
            {
                case Step.Shoot: return 1;
                case Step.Hole: return 2;
                case Step.Gauge: return 3;
                case Step.Slot: return 4;
                case Step.GoShop: return 5;
                case Step.ShopIntro: return 6;
                case Step.BuyRecord: return 7;
                case Step.CloseShop: return 8;
                case Step.OpenRack: return 9;
                case Step.LoadRecord: return 10;
                case Step.Activate: return 11;
                default: return 0;
            }
        }

        void ShowPanel(bool show)
        {
            if (panel != null) panel.gameObject.SetActive(show);
        }

        // ───────── 画面を作る ─────────

        void Build()
        {
            font = Resources.Load<Font>(MonitorDisplay.FontFolder + "/Body");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("TutorialCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;   // クリア後の帯（60）より上、エリアのムービー（1000〜）より下
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            group = canvasGo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            canvasRect = (RectTransform)canvasGo.transform;

            frameSprite = MakeFrameSprite("TutorialFrame", FrameLight, FrameDark, FrameFill);
            buttonSprite = MakeFrameSprite("TutorialButton", FrameDark, FrameDark, Color.white);
            skipSprite = MakeFrameSprite("TutorialSkip", new Color(0.45f, 0.48f, 0.58f), FrameDark, new Color(0.16f, 0.18f, 0.26f));

            // 説明の枠（画面の上のまん中。中は左上を (0, 0) にして並べる）
            panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(canvasRect, false);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -22f);
            panel.sizeDelta = new Vector2(PanelW, 220f);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = frameSprite;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 0.5f;   // 縁を 2 倍の太さで、ドットのまま描く

            subText = NewText("Sub", panel, 17, SubColor, FontStyle.Bold, TextAnchor.UpperLeft);
            titleText = NewText("Title", panel, 30, Color.white, FontStyle.Bold, TextAnchor.UpperLeft);
            bodyText = NewText("Body", panel, 21, BodyColor, FontStyle.Normal, TextAnchor.UpperLeft);
            bodyText.lineSpacing = 1.2f;

            divider = NewTopLeft("Divider", panel);
            var dimg = divider.gameObject.AddComponent<Image>();
            dimg.color = DividerColor;
            dimg.raycastTarget = false;

            // 進み具合の四角（右上）
            dotsRoot = NewTopLeft("Dots", panel);
            dots = new Image[StepCount];
            const float dot = 10f, gapDot = 5f;
            float dotsW = StepCount * dot + (StepCount - 1) * gapDot;
            dotsRoot.anchoredPosition = new Vector2(PanelW - Pad - dotsW, -30f);
            for (int i = 0; i < StepCount; i++)
            {
                var d = NewTopLeft("Dot" + i, dotsRoot);
                d.sizeDelta = new Vector2(dot, dot);
                d.anchoredPosition = new Vector2(i * (dot + gapDot), 0f);
                dots[i] = d.gameObject.AddComponent<Image>();
                dots[i].raycastTarget = false;
            }

            // 次へ（黄色）・スキップ（小さく）・待っている時のヒント
            nextButton = NewButton("Next", buttonSprite, NextFill, new Vector2(190f, 50f), 22, NextText, OnNext, out nextLabel);
            skipButton = NewButton("Skip", skipSprite, Color.white, new Vector2(120f, 36f), 16, SkipText, OnSkip, out var skipLabel);
            skipLabel.text = "スキップ";
            hintText = NewText("Hint", panel, 20, HintColor, FontStyle.Bold, TextAnchor.MiddleRight);
            var hrt = hintText.rectTransform;
            hrt.pivot = new Vector2(1f, 0.5f);
            hrt.sizeDelta = new Vector2(PanelW - Pad * 2f - 140f, 40f);
            panel.gameObject.SetActive(false);

            // 矢印（2 つまで。発射口の左右を同時に指す時に使う）
            arrowSprite = MakeArrowSprite();
            arrowA = NewArrow("ArrowA");
            arrowB = NewArrow("ArrowB");
        }

        RectTransform NewTopLeft(string name, RectTransform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            return rt;
        }

        Text NewText(string name, RectTransform parent, int size, Color color, FontStyle style, TextAnchor align)
        {
            var rt = NewTopLeft(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        RectTransform NewButton(string name, Sprite sprite, Color fill, Vector2 size, int fontSize, Color textColor,
            UnityEngine.Events.UnityAction onClick, out Text label)
        {
            var rt = NewTopLeft(name, panel);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 0.5f;
            img.color = fill;
            var button = rt.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.normalColor = new Color(0.92f, 0.92f, 0.92f, 1f);   // カーソルを合わせると少し明るく
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            button.colors = colors;
            button.onClick.AddListener(onClick);

            label = NewText("Label", rt, fontSize, textColor, FontStyle.Bold, TextAnchor.MiddleCenter);
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            return rt;
        }

        RectTransform NewArrow(string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(canvasRect, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(56f, 56f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = arrowSprite;
            img.color = Color.white;
            img.raycastTarget = false;
            rt.gameObject.SetActive(false);
            return rt;
        }

        /// <summary>
        /// 角を 1 段ずつ欠いた、ドットの枠の絵（9 分割で伸ばす）。
        /// 外から：明るい縁 2 ドット → 暗い線 1 ドット → 中の色。
        /// </summary>
        static Sprite MakeFrameSprite(string name, Color light, Color dark, Color fill)
        {
            const int S = 24, Cut = 3, Border = 8;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int dx = Mathf.Min(x, S - 1 - x);
                    int dy = Mathf.Min(y, S - 1 - y);
                    int inset = Mathf.Min(Mathf.Min(dx, dy), dx + dy - Cut);   // 角は斜めに欠く
                    Color c;
                    if (inset < 0) c = Color.clear;
                    else if (inset < 2) c = light;
                    else if (inset < 3) c = dark;
                    else c = fill;
                    px[y * S + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
        }

        /// <summary>下向きの矢印の絵（黄色の三角に、こい縁）。ドットで描く。</summary>
        static Sprite MakeArrowSprite()
        {
            const int S = 28;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "TutorialArrow", filterMode = FilterMode.Point };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                // 下（y=0）がとがった先。上へいくほど広い
                float half = (y + 1f) * 0.5f;
                for (int x = 0; x < S; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - S * 0.5f);
                    Color c = Color.clear;
                    if (dx <= half && y < S - 1) c = (dx > half - 2f || y < 2 || y >= S - 3) ? FrameDark : ArrowColor;
                    px[y * S + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }
    }
}
