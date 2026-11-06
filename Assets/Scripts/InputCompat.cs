#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define MP_NEW_INPUT
#endif
using UnityEngine;
#if MP_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace MedalPusher
{
    public enum GameKey
    {
        Reset,        // R
        NextRound,    // Enter / N
        AddMedals,    // F1
        TogglePanel,  // Tab
        ToggleCamera, // C
        PausePusher,  // P
        Cancel        // Esc
    }

    /// <summary>
    /// 旧Input Manager / 新Input System のどちらの設定でも動くようにする薄いラッパー。
    /// （Project Settings > Player > Active Input Handling に合わせて自動で切り替わる）
    /// </summary>
    public static class InputCompat
    {
        /// <summary>左の発射口（A / ←）</summary>
        public static bool LeftFireHeld()
        {
#if MP_NEW_INPUT
            var k = Keyboard.current;
            return k != null && (k.aKey.isPressed || k.leftArrowKey.isPressed);
#else
            return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
#endif
        }

        /// <summary>右の発射口（D / →）</summary>
        public static bool RightFireHeld()
        {
#if MP_NEW_INPUT
            var k = Keyboard.current;
            return k != null && (k.dKey.isPressed || k.rightArrowKey.isPressed);
#else
            return Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
#endif
        }

        public static Vector2 MousePosition()
        {
#if MP_NEW_INPUT
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        /// <summary>左クリックを押した瞬間</summary>
        public static bool MouseDown()
        {
#if MP_NEW_INPUT
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        public static bool MouseHeld()
        {
#if MP_NEW_INPUT
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

        public static bool MouseRightHeld()
        {
#if MP_NEW_INPUT
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton(1);
#endif
        }

        /// <summary>左右交互に発射（Space）</summary>
        public static bool AlternateFireHeld()
        {
#if MP_NEW_INPUT
            return Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
#else
            return Input.GetKey(KeyCode.Space);
#endif
        }

        public static bool Down(GameKey key)
        {
#if MP_NEW_INPUT
            var k = Keyboard.current;
            if (k == null) return false;
            switch (key)
            {
                case GameKey.Reset: return k.rKey.wasPressedThisFrame;
                case GameKey.NextRound: return k.enterKey.wasPressedThisFrame || k.nKey.wasPressedThisFrame;
                case GameKey.AddMedals: return k.f1Key.wasPressedThisFrame;
                case GameKey.TogglePanel: return k.tabKey.wasPressedThisFrame;
                case GameKey.ToggleCamera: return k.cKey.wasPressedThisFrame;
                case GameKey.PausePusher: return k.pKey.wasPressedThisFrame;
                case GameKey.Cancel: return k.escapeKey.wasPressedThisFrame;
            }
            return false;
#else
            switch (key)
            {
                case GameKey.Reset: return Input.GetKeyDown(KeyCode.R);
                case GameKey.NextRound: return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.N);
                case GameKey.AddMedals: return Input.GetKeyDown(KeyCode.F1);
                case GameKey.TogglePanel: return Input.GetKeyDown(KeyCode.Tab);
                case GameKey.ToggleCamera: return Input.GetKeyDown(KeyCode.C);
                case GameKey.PausePusher: return Input.GetKeyDown(KeyCode.P);
                case GameKey.Cancel: return Input.GetKeyDown(KeyCode.Escape);
            }
            return false;
#endif
        }
    }
}
