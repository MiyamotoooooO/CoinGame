using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 試作用の画面表示（IMGUI）。左上: 状況と負荷、右: 物理の調整パネル（Tabで開閉）。
    /// UIパッケージに依存しないよう OnGUI で描いている。本番UIを作る時に差し替える前提。
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        MedalPusherGame game;
        bool showPanel = true;
        Vector2 scroll;
        Rect statsRect = new Rect(10, 10, 330, 215);
        Rect panelRect;

        float fps = 60f;
        float awakeTimer;
        int awakeCount;
        int rareCount;
        bool changed;

        GUIStyle labelStyle, bigStyle, smallStyle;

        static readonly int[] HzOptions = { 50, 60, 100, 120 };
        static readonly string[] HzLabels = { "50", "60", "100", "120" };

        public void Init(MedalPusherGame game) => this.game = game;

        /// <summary>マウスがHUDの上にあるか（その時はクリックで投入しない）。screenPos は左下原点。</summary>
        public bool IsPointerOverPanel(Vector2 screenPos)
        {
            var p = new Vector2(screenPos.x, Screen.height - screenPos.y);
            return statsRect.Contains(p) || (showPanel && panelRect.Contains(p));
        }

        void Update()
        {
            if (game == null) return;
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) fps = Mathf.Lerp(fps, 1f / dt, 0.05f);

            awakeTimer -= dt;
            if (awakeTimer <= 0f)
            {
                awakeTimer = 0.25f;
                awakeCount = game.Pool.CountAwake();
                rareCount = game.Pool.CountRare();
            }

            if (InputCompat.Down(GameKey.TogglePanel)) showPanel = !showPanel;
        }

        void EnsureStyles()
        {
            if (labelStyle != null) return;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true, wordWrap = true };
            bigStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 40, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true
            };
        }

        void OnGUI()
        {
            if (game == null) return;
            EnsureStyles();

            // 演出中は画面をすっきりさせ、スキップの案内だけ出す
            if (game.State == GameState.Intro)
            {
                GUI.Label(new Rect(10, Screen.height - 30, Screen.width - 20, 26), "Enter: 演出をスキップ", smallStyle);
                return;
            }
            // ショップ中はモニターが画面いっぱいになるので、案内だけ出す
            if (game.State == GameState.Shop)
            {
                GUI.Label(new Rect(10, Screen.height - 30, Screen.width - 20, 26), "クリック: 購入・リロール ／ 閉じる（または Enter）: 盤面に戻って次のラウンドへ", smallStyle);
                return;
            }

            DrawStats();
            DrawCenterMessage();
            DrawHelp();
            if (showPanel) DrawTuningPanel();
        }

        void DrawStats()
        {
            var r = game.rules;
            string state = game.State switch
            {
                GameState.Intro => "<color=#aaaaaa>演出中</color>",
                GameState.Warmup => "<color=#aaaaaa>準備中</color>",
                GameState.Playing => "<color=#88ff88>プレイ中</color>",
                GameState.Grace => "<color=#ffdd66>判定待ち</color>",
                GameState.Cleared => "<color=#66ffff>クリア</color>",
                GameState.Shop => "<color=#ffcc66>ショップ（仮）</color>",
                GameState.Finished => "<color=#66ffff>おしまい（仮）</color>",
                _ => "<color=#ff6666>失敗</color>"
            };
            if (r.freePlay) state += " <color=#ff99ff>[フリープレイ]</color>";

            int ms = Mathf.RoundToInt(1000f / Mathf.Max(1f, fps));
            string fpsColor = fps >= 55f ? "#88ff88" : fps >= 30f ? "#ffdd66" : "#ff6666";

            GUI.Box(statsRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(statsRect.x + 10, statsRect.y + 6, statsRect.width - 20, statsRect.height - 12));
            int roundCount = game.CurrentArea.rounds.Length;
            string boss = game.IsBossRound ? " <color=#ff7777>BOSS</color>" : "";
            GUILayout.Label($"エリア {game.AreaIndex + 1}  ラウンド {game.RoundIndex + 1}/{roundCount}{boss}   {state}", labelStyle);
            GUILayout.Label($"所持メダル: <b>{game.OwnedMedals}</b>", labelStyle);
            GUILayout.Label($"投入: <b>{game.InsertedThisRound}</b> / {game.InsertLimit}", labelStyle);
            if (game.IsBossRound)
                GUILayout.Label($"ボスHP: <b>{game.BossHp}</b> / {game.CurrentQuota}   （ポイント {game.Points}）", labelStyle);
            else
                GUILayout.Label($"ポイント: <b>{game.Points}</b> / {game.CurrentQuota}", labelStyle);
            GUILayout.Label($"落下  <color=#66ee88>中央 {game.CenterDrops}</color>   <color=#ff7777>端 {game.SideDrops}</color>   ロスト {game.LostDrops}", labelStyle);
            GUILayout.Label($"盤面メダル: <b>{game.Pool.ActiveCount}</b>  <color=#c8c8ff>レア {rareCount}</color>  （動いている: {awakeCount}）", labelStyle);
            GUILayout.Label($"<color={fpsColor}>FPS {fps:0}  ({ms} ms)</color>   物理 {game.tuning.physicsHz}Hz", labelStyle);
            GUILayout.EndArea();
        }

        void DrawCenterMessage()
        {
            string msg = null;
            switch (game.State)
            {
                case GameState.Warmup: msg = "<size=28>メダルを並べています…</size>"; break;
                case GameState.Cleared:
                    msg = game.IsLastRoundOfArea ? "<color=#66ffff>AREA CLEAR!</color>" : "<color=#66ffff>CLEAR!</color>";
                    break;
                case GameState.Finished: msg = "<size=30>用意したエリアはここまで（仮）</size>\n<size=20>R で最初から</size>"; break;
                case GameState.Failed: msg = "<color=#ff6666>FAILED</color>\n<size=20>R でリセット</size>"; break;
                case GameState.Grace:
                    float left = Mathf.Max(0f, game.rules.graceSeconds - game.StateTime);
                    msg = $"<size=26>判定まで {left:0.0} 秒</size>"; break;
            }
            if (msg == null) return;
            var rect = new Rect(0, Screen.height * 0.25f, Screen.width, 120);
            var shadow = new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height);
            var prev = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.Label(shadow, StripColor(msg), bigStyle);
            GUI.color = prev;
            GUI.Label(rect, msg, bigStyle);
        }

        static string StripColor(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s, "</?color[^>]*>", "");

        void DrawHelp()
        {
            const string help =
                "左クリック/A/←: 左から発射   右クリック/D/→: 右から発射   Space: 左右交互（長押しで連続）   R: リセット   Enter: 次へ\n" +
                "F1: メダル+50（負荷テスト）   P: プッシャー停止   C: カメラ切替   Tab: 調整パネル";
            GUI.Label(new Rect(10, Screen.height - 46, Screen.width - 20, 44), help, smallStyle);
        }

        void DrawTuningPanel()
        {
            float w = 380f;
            panelRect = new Rect(Screen.width - w - 10, 10, w, Mathf.Min(Screen.height - 70, 720));
            GUI.Box(panelRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(panelRect.x + 8, panelRect.y + 6, panelRect.width - 16, panelRect.height - 12));
            scroll = GUILayout.BeginScrollView(scroll);

            var t = game.tuning;
            var r = game.rules;
            changed = false;

            Header("プッシャー");
            t.pusherCycleSeconds = Slider("1往復の秒数", t.pusherCycleSeconds, 1f, 10f, "0.0");
            t.pusherStroke = Slider("ストローク", t.pusherStroke, 0.1f, 3f, "0.00");

            Header("メダル");
            t.medalMass = Slider("重さ", t.medalMass, 0.1f, 5f, "0.00");
            t.medalDynamicFriction = Slider("摩擦（動）", t.medalDynamicFriction, 0f, 1f, "0.00");
            t.medalStaticFriction = Slider("摩擦（静）", t.medalStaticFriction, 0f, 1f, "0.00");
            t.medalBounciness = Slider("反発", t.medalBounciness, 0f, 0.6f, "0.00");
            t.medalDrag = Slider("空気抵抗", t.medalDrag, 0f, 2f, "0.00");
            t.medalAngularDrag = Slider("回転抵抗", t.medalAngularDrag, 0f, 5f, "0.00");
            t.antiStandAngle = Slider("立ち防止の角度", t.antiStandAngle, 20f, 90f, "0");
            t.antiStandTorque = Slider("立ち防止の強さ", t.antiStandTorque, 0f, 200f, "0");

            Header("盤面・プッシャー");
            t.boardDynamicFriction = Slider("摩擦（動）", t.boardDynamicFriction, 0f, 1f, "0.00");
            t.boardStaticFriction = Slider("摩擦（静）", t.boardStaticFriction, 0f, 1f, "0.00");

            Header("ワールド");
            t.gravityScale = Slider("重力倍率", t.gravityScale, 0.5f, 4f, "0.00");
            GUILayout.BeginHorizontal();
            GUILayout.Label("物理Hz", GUILayout.Width(120));
            int hzIndex = System.Array.IndexOf(HzOptions, t.physicsHz);
            int newHz = GUILayout.Toolbar(hzIndex, HzLabels);
            GUILayout.EndHorizontal();
            if (newHz != hzIndex && newHz >= 0) { t.physicsHz = HzOptions[newHz]; changed = true; }
            t.solverIterations = Mathf.RoundToInt(Slider("ソルバー反復", t.solverIterations, 1, 30, "0"));
            t.sleepThreshold = Slider("スリープ閾値", t.sleepThreshold, 0.001f, 0.1f, "0.000");
            t.contactOffset = Slider("接触オフセット", t.contactOffset, 0.001f, 0.02f, "0.000");
            t.maxDepenetrationVelocity = Slider("めり込み解消速度", t.maxDepenetrationVelocity, 0.5f, 10f, "0.0");
            bool interp = GUILayout.Toggle(t.interpolateMedals, " メダルの補間（なめらか表示・少し重い）");
            if (interp != t.interpolateMedals) { t.interpolateMedals = interp; changed = true; }

            Header("カメラ（正面視点）");
            var l = game.layout;
            bool camChanged = false;
            float fov = Slider("視野角", l.cameraFieldOfView, 30f, 100f, "0");
            float pitch = Slider("見下ろす角度", l.cameraRotation.x, 0f, 80f, "0");
            float camY = Slider("高さ", l.cameraPosition.y, 0.5f, 6f, "0.00");
            float camZ = Slider("前後", l.cameraPosition.z, -6f, 1f, "0.00");
            float lens = Slider("レンズのゆがみ", l.cameraLensDistortion, 0f, 1f, "0.00");
            if (!Mathf.Approximately(fov, l.cameraFieldOfView) || !Mathf.Approximately(pitch, l.cameraRotation.x) ||
                !Mathf.Approximately(camY, l.cameraPosition.y) || !Mathf.Approximately(camZ, l.cameraPosition.z) ||
                !Mathf.Approximately(lens, l.cameraLensDistortion))
            {
                l.cameraFieldOfView = fov;
                l.cameraRotation = new Vector3(pitch, l.cameraRotation.y, l.cameraRotation.z);
                l.cameraPosition = new Vector3(l.cameraPosition.x, camY, camZ);
                l.cameraLensDistortion = lens;
                camChanged = true;
            }
            if (camChanged) game.ApplyCameraPreset();
            var m = game.monitor;
            m.monitorPixelHeight = Mathf.RoundToInt(Slider("モニターのドット(縦)", m.monitorPixelHeight, 120f, 1440f, "0"));

            Header("投入・テスト");
            r.insertInterval = Slider("発射間隔(秒)", r.insertInterval, 0.05f, 1f, "0.00");
            r.launchSpeed = Slider("発射の強さ", r.launchSpeed, 0.5f, 10f, "0.0");
            r.launchSpread = Slider("強さのばらつき", r.launchSpread, 0f, 0.5f, "0.00");
            r.freePlay = GUILayout.Toggle(r.freePlay, " フリープレイ（投入制限なし）");

            if (changed) game.ApplyTuning();

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("リセット (R)")) game.ResetGame();
            if (GUILayout.Button("メダル+50 (F1)")) game.AddTestMedals(50);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("現在の調整値をログ出力＋コピー"))
            {
                string json = JsonUtility.ToJson(t, true);
                GUIUtility.systemCopyBuffer = json;
                Debug.Log("[MedalPusher] PhysicsTuning\n" + json);
            }
            GUILayout.Label("※再生を止めると値は戻ります。残したい時はInspectorでコンポーネントを右クリック→Copy Component、停止後にPaste Component Values。", smallStyle);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Header(string text)
        {
            GUILayout.Space(6);
            GUILayout.Label($"<b>{text}</b>", labelStyle);
        }

        float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(170));
            GUILayout.Label(v.ToString(format), GUILayout.Width(50));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(v, value)) changed = true;
            return v;
        }
    }
}
