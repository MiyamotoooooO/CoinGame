#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define MP_NEW_INPUT
#endif
using UnityEngine;
#if MP_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace MedalPusher
{
    /// <summary>
    /// デバッグ用：F6 キーで、レコード N001〜N010 を所持する・手放すを切りかえる（演出の確認用）。
    ///
    /// ・1つでも持っていない物があれば、F6 で N001〜N010 をぜんぶ持つ
    /// ・ぜんぶ持っている時に F6 を押すと、N001〜N010 をぜんぶ手放す
    /// ・押した時は、画面のまん中の上に2秒だけ結果を出す
    /// ・ids を変えると、ほかのレコードも同じように切りかえられる
    /// ・F3 は図鑑（RecordDexHotkey）の「全40枚を入手」、F4 は演出のプレビュー（RecordSpectacleDirector）に使われているので、F6 にした
    ///
    /// シーンに置かなくても、RecordInventory があれば再生時に自動で付く。
    /// </summary>
    [DisallowMultipleComponent]
    public class DebugRecordHotkey : MonoBehaviour
    {
        [Tooltip("F6 で切りかえるレコードの ID")]
        public string[] ids = { "N001", "N002", "N003", "N004", "N005", "N006", "N007", "N008", "N009", "N010" };

        RecordInventory inventory;
        string message;
        float messageTime = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<DebugRecordHotkey>() != null) return;
            var inv = FindFirstObjectByType<RecordInventory>();
            if (inv != null) inv.gameObject.AddComponent<DebugRecordHotkey>();
        }

        void Awake()
        {
            inventory = GetComponent<RecordInventory>();
            if (inventory == null) inventory = FindFirstObjectByType<RecordInventory>();
        }

        static bool KeyDown()
        {
#if MP_NEW_INPUT
            var k = Keyboard.current;
            return k != null && k.f6Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F6);
#endif
        }

        void Update()
        {
            if (messageTime > 0f) messageTime -= Time.unscaledDeltaTime;
            if (inventory == null || !KeyDown()) return;

            // ID からキー（英名）を引く
            var all = RecordDatabase.LoadFromResources();
            var keys = new System.Collections.Generic.List<string>();
            foreach (var id in ids)
            {
                var r = all.Find(x => x.id == id);
                if (r != null) keys.Add(r.key);
                else Debug.LogWarning($"DebugRecordHotkey: ID「{id}」のレコードが CSV にありません。");
            }
            if (keys.Count == 0) return;

            bool ownAll = true;
            foreach (var k in keys) if (!inventory.Has(k)) { ownAll = false; break; }

            int changed = 0;
            foreach (var k in keys)
            {
                if (ownAll) { if (inventory.Remove(k)) changed++; }
                else { if (inventory.Add(k)) changed++; }
            }
            message = ownAll ? $"デバッグ：{ids[0]}〜{ids[ids.Length - 1]} を手放しました（{changed}枚）"
                             : $"デバッグ：{ids[0]}〜{ids[ids.Length - 1]} を所持しました（{changed}枚）";
            messageTime = 2f;
            Debug.Log(message);
        }

        void OnGUI()
        {
            if (messageTime <= 0f) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
            float w = 640f, h = 44f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, 60f, w, h), message, style);
        }
    }
}
