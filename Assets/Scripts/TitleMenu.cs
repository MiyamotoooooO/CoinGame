#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define TM_NEW_INPUT
#endif
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
#if TM_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace MedalPusher
{
    /// <summary>
    /// タイトル画面のメニュー。
    /// 組み立ては メニュー「RECORD DROP → タイトル画面を組み立てる」で自動で行う。
    ///
    /// ・マウスでも、キーボード（↑↓ / W S で選ぶ、Enter / Space で決定）でも操作できる
    /// ・「初めから」「続きから」は今までどおり TitleManager の処理を呼ぶ
    /// ・セーブが無い時は「続きから」を押せないようにする
    /// ・画面の右半分は映像を流すため、UI は左側だけに置いている
    /// </summary>
    public class TitleMenu : MonoBehaviour
    {
        /// <summary>ボタンを押した時にすること</summary>
        public enum MenuAction { NewGame, Continue, Profile, Exit, Settings }

        [Header("■ つなぐ先")]
        [Tooltip("「初めから」「続きから」を頼む相手。空ならシーンから自動で探す")]
        public TitleManager titleManager;
        [Tooltip("「初めから」で読み込むシーン。空なら今までどおり TitleManager の newGame を呼ぶ（MainScene へ行く）。オープニングを流す時は OpeningScene と入れる")]
        public string newGameScene = "";
        [Tooltip("プロフィールを押した時にすること。何もつながっていなければ「準備中です」と出る")]
        public UnityEvent onProfile = new UnityEvent();
        [Tooltip("設定を押した時にすること。何もつながっていなければ「準備中です」と出る")]
        public UnityEvent onSettings = new UnityEvent();

        [Header("■ 動き")]
        [Tooltip("画面が出てから、パネルが動き始めるまでの秒数")]
        public float introDelay = 0.2f;
        [Tooltip("パネルが左からスライドして入ってくる秒数")]
        public float introSeconds = 0.5f;
        [Tooltip("メニュー項目が1つずつ出てくる時の、ずらす秒数")]
        public float itemStagger = 0.07f;
        [Tooltip("えらぶ項目を変えた時に、帯が動く速さ（大きいほど速い）")]
        public float selectSpeed = 14f;
        [Tooltip("えらんでいる項目の、小さなレコードが回る速さ（度/秒）")]
        public float recordSpinSpeed = 180f;
        [Tooltip("決定してから画面が暗くなるまでの秒数")]
        public float confirmFadeSeconds = 0.4f;
        [Tooltip("「準備中です」を出しておく秒数")]
        public float toastSeconds = 1.6f;

        [Header("■ 色")]
        [Tooltip("ふつうの時の文字の色")]
        public Color normalTextColor = new Color(0.92f, 0.91f, 0.96f);
        [Tooltip("えらんでいる時の文字の色（帯の上に乗るので暗い色）")]
        public Color selectedTextColor = new Color(0.055f, 0.043f, 0.14f);
        [Tooltip("押せない時の文字の色")]
        public Color disabledTextColor = new Color(0.45f, 0.43f, 0.55f);
        [Tooltip("番号と英語の文字の色")]
        public Color subTextColor = new Color(0.16f, 0.9f, 1f);

        [Header("■ 自動で入る（組み立てメニューが入れるので、触らなくてよい）")]
        public RectTransform panel;
        public float panelWidth = 760f;
        public RectTransform logo;
        public CanvasGroup logoGroup;
        public List<TitleMenuItem> items = new List<TitleMenuItem>();
        public TextMeshProUGUI saveInfo;
        public Button settingsButton;
        public CanvasGroup bottomGroup;
        public CanvasGroup toastGroup;
        public TextMeshProUGUI toastText;
        public Image fadeImage;

        // ---------- 中で使う値 ----------
        private int selected;
        private float clock;
        private bool confirming;
        private bool actionDone;
        private float confirmTimer;
        private MenuAction pendingAction;
        private float toastTimer;
        private SaveData save;
        private bool hasSave;
        private Vector2 logoBasePos;

        private void Awake()
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;
                items[i].owner = this;
                items[i].index = i;
            }
            if (settingsButton != null) settingsButton.onClick.AddListener(() => DoAction(MenuAction.Settings));
            if (logo != null) logoBasePos = logo.anchoredPosition;
        }

        private void Start()
        {
            if (titleManager == null) titleManager = FindFirstObjectByType<TitleManager>();
            LoadSave();

            foreach (TitleMenuItem item in items)
            {
                if (item == null) continue;
                item.usable = !(item.action == MenuAction.Continue && !hasSave);
            }
            selected = FindUsable(0, 1);
            if (selected >= 0 && selected < items.Count) items[selected].selectAmount = 1f;

            if (saveInfo != null)
            {
                saveInfo.text = hasSave
                    ? $"続きから：AREA {save.area + 1}  ROUND {save.round + 1}  ・  MEDAL {save.medal}"
                    : "セーブがありません";
            }
            SetFade(0f, false);
            if (toastGroup != null) toastGroup.alpha = 0f;
            ApplyVisuals(0f);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            clock += dt;

            bool ready = clock > introDelay + 0.2f;
            if (!confirming && ready)
            {
                if (PressedUp()) Move(-1);
                if (PressedDown()) Move(1);
                if (PressedConfirm()) Confirm();
            }

            if (confirming)
            {
                confirmTimer += dt;
                SetFade(Mathf.Clamp01(confirmTimer / Mathf.Max(0.01f, confirmFadeSeconds)), true);
                if (!actionDone && confirmTimer >= confirmFadeSeconds)
                {
                    actionDone = true;
                    RunAction(pendingAction);
                }
            }

            if (toastTimer > 0f) toastTimer -= dt;
            if (toastGroup != null)
            {
                float t = Mathf.Max(0f, toastTimer);
                toastGroup.alpha = t <= 0f ? 0f : Mathf.Min(1f, t / 0.3f, (toastSeconds - t) / 0.15f);
            }

            ApplyVisuals(dt);
        }

        // =====================================================================
        // 見た目
        // =====================================================================

        private void ApplyVisuals(float dt)
        {
            // パネルが左からスライドして入ってくる
            float panelIn = EaseOut((clock - introDelay) / Mathf.Max(0.01f, introSeconds));
            if (panel != null) panel.anchoredPosition = new Vector2(Mathf.Lerp(-panelWidth, 0f, panelIn), panel.anchoredPosition.y);

            float logoIn = EaseOut((clock - introDelay - 0.15f) / 0.5f);
            if (logoGroup != null) logoGroup.alpha = logoIn;
            if (logo != null) logo.anchoredPosition = logoBasePos + new Vector2(0f, (1f - logoIn) * 30f);
            if (bottomGroup != null) bottomGroup.alpha = EaseOut((clock - introDelay - 0.5f) / 0.4f);

            float blend = 1f - Mathf.Exp(-selectSpeed * dt);
            for (int i = 0; i < items.Count; i++)
            {
                TitleMenuItem it = items[i];
                if (it == null) continue;

                // 1つずつ左から出てくる
                float appear = EaseOut((clock - introDelay - 0.2f - i * itemStagger) / 0.35f);
                if (it.group != null) it.group.alpha = appear;

                // えらばれ具合をなめらかに変える
                float target = i == selected ? 1f : 0f;
                it.selectAmount = dt <= 0f ? target : Mathf.Lerp(it.selectAmount, target, blend);
                float s = it.selectAmount;

                // 決定した瞬間、帯がチカチカ光る
                float flash = (confirming && i == selected) ? (Mathf.Repeat(confirmTimer * 12f, 1f) < 0.5f ? 1f : 0.6f) : 1f;

                if (it.content != null) it.content.anchoredPosition = new Vector2((1f - appear) * -80f + s * 18f, 0f);
                if (it.highlight != null)
                {
                    it.highlight.color = new Color(flash, flash, flash, s);
                    it.highlight.rectTransform.localScale = new Vector3(Mathf.Lerp(0.7f, 1f, s), 1f, 1f);
                }

                Color label = it.usable ? Color.Lerp(normalTextColor, selectedTextColor, s) : disabledTextColor;
                Color sub = it.usable ? Color.Lerp(subTextColor, selectedTextColor, s) : disabledTextColor;
                if (it.labelText != null) it.labelText.color = label;
                if (it.indexText != null) it.indexText.color = sub;
                if (it.subText != null) it.subText.color = sub;

                if (it.miniRecord != null)
                {
                    it.miniRecord.color = new Color(1f, 1f, 1f, s);
                    it.miniRecord.rectTransform.Rotate(0f, 0f, -recordSpinSpeed * dt);
                }
            }

            // セーブの情報：「続きから」をえらんでいる時は、はっきり見せる
            if (saveInfo != null)
            {
                bool onContinue = selected >= 0 && selected < items.Count && items[selected] != null && items[selected].action == MenuAction.Continue;
                Color c = onContinue ? subTextColor : normalTextColor;
                c.a = onContinue ? 1f : 0.55f;
                saveInfo.color = c;
                saveInfo.alpha = c.a * EaseOut((clock - introDelay - 0.6f) / 0.4f);
            }
        }

        private void SetFade(float alpha, bool block)
        {
            if (fadeImage == null) return;
            Color c = fadeImage.color;
            c.a = alpha;
            fadeImage.color = c;
            fadeImage.raycastTarget = block;
        }

        private void ShowToast(string message)
        {
            if (toastText != null) toastText.text = message;
            toastTimer = toastSeconds;
        }

        // =====================================================================
        // 操作
        // =====================================================================

        /// <summary>項目をえらぶ（押せない項目はえらばない）</summary>
        public void Select(int index)
        {
            if (confirming) return;
            if (index < 0 || index >= items.Count || items[index] == null || !items[index].usable) return;
            selected = index;
        }

        /// <summary>今えらんでいる項目を決定する</summary>
        public void Confirm()
        {
            if (confirming || selected < 0 || selected >= items.Count) return;
            TitleMenuItem it = items[selected];
            if (it == null || !it.usable) return;
            DoAction(it.action);
        }

        private void Move(int dir)
        {
            int next = FindUsable(selected + dir, dir);
            if (next >= 0) selected = next;
        }

        /// <summary>start から dir の向きに、押せる項目を探す（はしまで行ったら反対側へ）</summary>
        private int FindUsable(int start, int dir)
        {
            int n = items.Count;
            if (n == 0) return -1;
            for (int k = 0; k < n; k++)
            {
                int i = ((start + dir * k) % n + n) % n;
                if (items[i] != null && items[i].usable) return i;
            }
            return -1;
        }

        private void DoAction(MenuAction action)
        {
            if (confirming) return;
            switch (action)
            {
                case MenuAction.Profile:
                    if (onProfile.GetPersistentEventCount() > 0) onProfile.Invoke();
                    else ShowToast("プロフィールは準備中です");
                    break;
                case MenuAction.Settings:
                    if (onSettings.GetPersistentEventCount() > 0) onSettings.Invoke();
                    else ShowToast("設定は準備中です");
                    break;
                default:
                    // 初めから・続きから・終了は、画面を暗くしてから行う
                    if (action == MenuAction.NewGame && !string.IsNullOrEmpty(newGameScene) && !Application.CanStreamedLevelBeLoaded(newGameScene))
                    {
                        Debug.LogWarning($"[タイトル] シーン「{newGameScene}」が Build Settings に入っていません。File → Build Settings に追加してください。");
                        ShowToast("シーンが見つかりません");
                        return;
                    }
                    confirming = true;
                    actionDone = false;
                    confirmTimer = 0f;
                    pendingAction = action;
                    break;
            }
        }

        private void RunAction(MenuAction action)
        {
            switch (action)
            {
                case MenuAction.NewGame:
                    if (!string.IsNullOrEmpty(newGameScene))
                    {
                        if (titleManager != null && titleManager.saveManager != null) titleManager.saveManager.DeleteSave();
                        SceneManager.LoadScene(newGameScene);
                    }
                    else if (titleManager != null) titleManager.newGame();
                    else Debug.LogWarning("[タイトル] TitleManager が見つかりません。");
                    break;
                case MenuAction.Continue:
                    if (titleManager != null) titleManager.ContinueGame();
                    else Debug.LogWarning("[タイトル] TitleManager が見つかりません。");
                    break;
                case MenuAction.Exit:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
            }
        }

        // =====================================================================
        // セーブを読む（読むだけ。書きかえはしない）
        // =====================================================================

        private void LoadSave()
        {
            hasSave = false;
            save = null;
            string path = Application.persistentDataPath + "/save.json";
            try
            {
                if (File.Exists(path))
                {
                    save = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                    hasSave = save != null;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[タイトル] セーブを読めませんでした：" + e.Message);
            }
        }

        // =====================================================================
        // キー入力（旧 Input Manager と 新 Input System の両方で動く）
        // =====================================================================

        private static bool PressedUp()
        {
#if TM_NEW_INPUT
            Keyboard k = Keyboard.current;
            return k != null && (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
#endif
        }

        private static bool PressedDown()
        {
#if TM_NEW_INPUT
            Keyboard k = Keyboard.current;
            return k != null && (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
#endif
        }

        private static bool PressedConfirm()
        {
#if TM_NEW_INPUT
            Keyboard k = Keyboard.current;
            return k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
#endif
        }

        /// <summary>速く始まって、ゆっくり止まる（0〜1）</summary>
        private static float EaseOut(float x)
        {
            x = Mathf.Clamp01(x);
            float a = 1f - x;
            return 1f - a * a * a;
        }
    }
}
