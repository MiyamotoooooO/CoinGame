#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define MP_NEW_INPUT
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
#if MP_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace MedalPusher
{
    /// <summary>
    /// レコード図鑑（RecordDex シーン）へ飛ぶデバッグ用のキーと、図鑑の記録。
    ///
    /// ・P キーを押すと、どのシーンからでも図鑑のシーンへ飛ぶ（エディタと Development Build の時だけ）
    ///   図鑑で P か Esc を押すと、元のシーンへもどる（RecordDexScreen が受け持つ）
    ///   ※ P はゲームの「プッシャーを止める」と同じキー。飛ぶ前に一瞬止まるが、シーンが替わるので気にしなくてよい
    /// ・F3 キーを押すと、40枚のレコードを全部入手する（所持一覧に入れて、図鑑も全部「発見」にする）。デバッグ用。
    ///   エディタと Development Build の時だけ。図鑑の画面で押すと、図鑑の「発見」だけ全部つける
    /// ・ゲーム中にレコードを流した回数・発動した回数を、この PC に保存する（PlayerPrefs）。図鑑の「発見」に使う
    /// ・シーンに置かなくても、ゲームを始めると自動で作られる（シーンが替わっても消えない）
    /// </summary>
    public class RecordDexHotkey : MonoBehaviour
    {
        /// <summary>図鑑のシーンの名前（Build Settings に入れておく。エディタでは自動で入る）</summary>
        public const string DexSceneName = "RecordDex";

        /// <summary>図鑑に飛ぶ前のシーンの名前（もどる時に使う）</summary>
        public static string ReturnSceneName { get; set; }

        static RecordDexHotkey instance;
        RecordEffectSystem watched;
        string notice;
        float noticeUntil;
        GUIStyle noticeStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            if (instance != null) return;
            var go = new GameObject("RecordDexHotkey");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<RecordDexHotkey>();
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => watched = null;

        void Update()
        {
            WatchRecords();

            bool debugAllowed = Application.isEditor || Debug.isDebugBuild;
            if (!debugAllowed) return;
            if (Application.isEditor && DexKeys.Pressed(DexKeys.Key.F3)) UnlockAll();   // F3（全部入手）はエディタだけ。ビルドでは Development Build でも効かない
            if (SceneManager.GetActiveScene().name == DexSceneName) return;   // 図鑑の中では RecordDexScreen が P を受け持つ
            if (DexKeys.Pressed(DexKeys.Key.P)) OpenDex();
        }

        /// <summary>
        /// デバッグ用：レコードを全部入手する（F3）。
        /// ゲームのシーンでは所持一覧（RecordInventory）に全部入れ、図鑑の「発見」も全部つける。
        /// </summary>
        public void UnlockAll()
        {
            var records = RecordDatabase.LoadFromResources();
            if (records == null || records.Count == 0)
            {
                ShowNotice("レコードの CSV が読めませんでした");
                return;
            }

            int added = 0;
            var inventory = FindFirstObjectByType<RecordInventory>();
            if (inventory != null)
                foreach (var r in records)
                    if (!RecordData.IsNullOrEmpty(r) && inventory.Add(r)) added++;

            RecordDexProgress.DiscoverAll(records);

            var dex = FindFirstObjectByType<RecordDexScreen>();
            if (dex != null) dex.SendMessage("RefreshDetail", SendMessageOptions.DontRequireReceiver);

            string msg = inventory != null
                ? $"F3：全レコードを入手しました（所持に +{added} 枚、図鑑は全部発見）"
                : "F3：図鑑のレコードを全部「発見」にしました（所持はゲームの画面で F3）";
            ShowNotice(msg);
            Debug.Log("RecordDexHotkey: " + msg);
        }

        void ShowNotice(string text)
        {
            notice = text;
            noticeUntil = Time.unscaledTime + 3f;
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(notice) || Time.unscaledTime > noticeUntil) return;
            if (noticeStyle == null)
            {
                noticeStyle = new GUIStyle(GUI.skin.box) { fontSize = Mathf.Max(14, Screen.height / 40), alignment = TextAnchor.MiddleCenter, wordWrap = true };
                noticeStyle.normal.textColor = Color.white;
            }
            float w = Mathf.Min(Screen.width - 40f, Screen.height * 1.1f), h = noticeStyle.fontSize * 2.6f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.12f, w, h), notice, noticeStyle);
        }

        /// <summary>図鑑のシーンを開く（今のシーンを覚えておく）</summary>
        public static void OpenDex()
        {
            if (!Application.CanStreamedLevelBeLoaded(DexSceneName))
            {
                Debug.LogWarning($"RecordDexHotkey: シーン「{DexSceneName}」が Build Settings に入っていません。File > Build Settings に Assets/Scenes/{DexSceneName}.unity を足してください。");
                return;
            }
            ReturnSceneName = SceneManager.GetActiveScene().name;
            Time.timeScale = 1f;            // ヒットストップ中などに飛んでも、図鑑が止まらないように
            AudioListener.pause = false;
            SceneManager.LoadScene(DexSceneName);
        }

        /// <summary>ゲームのシーンの RecordEffectSystem を見つけて、流した・発動したを記録する</summary>
        void WatchRecords()
        {
            if (watched != null) return;
            var fx = FindFirstObjectByType<RecordEffectSystem>();
            if (fx == null) return;
            watched = fx;
            fx.RecordLoaded += r => RecordDexProgress.AddHeard(r);
            fx.RecordPlayed += r => RecordDexProgress.AddActivated(r);
        }
    }

    /// <summary>
    /// 図鑑の記録（この PC に保存）。レコードを1回でも流すと「発見」になる。
    /// </summary>
    public static class RecordDexProgress
    {
        const string HeardPrefix = "RecordDex.Heard.";
        const string ActivatedPrefix = "RecordDex.Activated.";

        public static int Heard(string key) => string.IsNullOrEmpty(key) ? 0 : PlayerPrefs.GetInt(HeardPrefix + key, 0);
        public static int Activated(string key) => string.IsNullOrEmpty(key) ? 0 : PlayerPrefs.GetInt(ActivatedPrefix + key, 0);
        public static bool IsDiscovered(string key) => Heard(key) > 0 || Activated(key) > 0;

        public static void AddHeard(RecordData r)
        {
            if (RecordData.IsNullOrEmpty(r)) return;
            PlayerPrefs.SetInt(HeardPrefix + r.key, Heard(r.key) + 1);
            PlayerPrefs.Save();
        }

        public static void AddActivated(RecordData r)
        {
            if (RecordData.IsNullOrEmpty(r)) return;
            PlayerPrefs.SetInt(ActivatedPrefix + r.key, Activated(r.key) + 1);
            PlayerPrefs.Save();
        }

        /// <summary>まだ発見していないレコードを、全部「発見」にする（F3 で呼ぶ。デバッグ用）</summary>
        public static void DiscoverAll(System.Collections.Generic.IEnumerable<RecordData> records)
        {
            foreach (var r in records)
            {
                if (RecordData.IsNullOrEmpty(r) || IsDiscovered(r.key)) continue;
                PlayerPrefs.SetInt(HeardPrefix + r.key, 1);
            }
            PlayerPrefs.Save();
        }

        /// <summary>記録を消す（図鑑で F9 を押すと呼ぶ。デバッグ用）</summary>
        public static void Clear(System.Collections.Generic.IEnumerable<RecordData> records)
        {
            foreach (var r in records)
            {
                if (RecordData.IsNullOrEmpty(r)) continue;
                PlayerPrefs.DeleteKey(HeardPrefix + r.key);
                PlayerPrefs.DeleteKey(ActivatedPrefix + r.key);
            }
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 図鑑で使うキー入力（旧 Input Manager と新 Input System のどちらでも動く。InputCompat と同じ切りかえ方）
    /// </summary>
    public static class DexKeys
    {
        public enum Key { P, Escape, Backspace, Left, Right, Up, Down, Tab, Q, E, Space, Enter, F3, F4, F5, F9, Shift }

        /// <summary>押した瞬間</summary>
        public static bool Pressed(Key key)
        {
#if MP_NEW_INPUT
            var k = Keyboard.current;
            if (k == null) return false;
            switch (key)
            {
                case Key.P: return k.pKey.wasPressedThisFrame;
                case Key.Escape: return k.escapeKey.wasPressedThisFrame;
                case Key.Backspace: return k.backspaceKey.wasPressedThisFrame;
                case Key.Left: return k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame;
                case Key.Right: return k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame;
                case Key.Up: return k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame;
                case Key.Down: return k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame;
                case Key.Tab: return k.tabKey.wasPressedThisFrame;
                case Key.Q: return k.qKey.wasPressedThisFrame;
                case Key.E: return k.eKey.wasPressedThisFrame;
                case Key.Space: return k.spaceKey.wasPressedThisFrame;
                case Key.Enter: return k.enterKey.wasPressedThisFrame;
                case Key.F3: return k.f3Key.wasPressedThisFrame;
                case Key.F4: return k.f4Key.wasPressedThisFrame;
                case Key.F5: return k.f5Key.wasPressedThisFrame;
                case Key.F9: return k.f9Key.wasPressedThisFrame;
                case Key.Shift: return k.leftShiftKey.wasPressedThisFrame || k.rightShiftKey.wasPressedThisFrame;
            }
            return false;
#else
            switch (key)
            {
                case Key.P: return Input.GetKeyDown(KeyCode.P);
                case Key.Escape: return Input.GetKeyDown(KeyCode.Escape);
                case Key.Backspace: return Input.GetKeyDown(KeyCode.Backspace);
                case Key.Left: return Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
                case Key.Right: return Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
                case Key.Up: return Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
                case Key.Down: return Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
                case Key.Tab: return Input.GetKeyDown(KeyCode.Tab);
                case Key.Q: return Input.GetKeyDown(KeyCode.Q);
                case Key.E: return Input.GetKeyDown(KeyCode.E);
                case Key.Space: return Input.GetKeyDown(KeyCode.Space);
                case Key.Enter: return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
                case Key.F3: return Input.GetKeyDown(KeyCode.F3);
                case Key.F4: return Input.GetKeyDown(KeyCode.F4);
                case Key.F5: return Input.GetKeyDown(KeyCode.F5);
                case Key.F9: return Input.GetKeyDown(KeyCode.F9);
                case Key.Shift: return Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
            }
            return false;
#endif
        }

        /// <summary>Shift を押しているか</summary>
        public static bool ShiftHeld()
        {
#if MP_NEW_INPUT
            var k = Keyboard.current;
            return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
#else
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        }

        /// <summary>マウスホイールの回した量（上が +）</summary>
        public static float Scroll()
        {
#if MP_NEW_INPUT
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
            return Input.mouseScrollDelta.y;
#endif
        }
    }
}
