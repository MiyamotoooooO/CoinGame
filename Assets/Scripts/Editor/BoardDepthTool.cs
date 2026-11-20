using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 盤面の奥行き（前後の長さ）を変えるためのエディター用の窓（Editor フォルダに置く。ゲームには入らない）。
    ///
    /// ・上のメニュー「MedalPusher」→「盤面の奥行きを変える」で窓を開く
    /// ・「倍率」を入れて「この倍率で伸ばす」を押すと、いま開いているシーンの盤面を、ワールドの Z=0 を中心に前後へ伸ばす
    ///   盤面のモデル（StageVer2）：大きさの Z を倍率倍（床・筐体・プッシャー・穴・モニターがいっしょに伸びる。幅と高さはそのまま）
    ///   MedalPusherGame の設定：手前のふち・プッシャーの奥行き・いちばん奥の位置・奥のかべまで・穴の奥行き・プッシャーの動く長さを倍率倍
    ///   ほかのシーンの物（スピーカー・グルーヴゲージ・レコーダーなど）：場所の Z を倍率倍（形はそのまま）
    ///     見た目のない当たり判定だけの物（Trigger など）は、大きさの Z も倍率倍
    ///   カメラ（「カメラを合わせる」が入っている時）：手前のふちが動いた分だけ、カメラも手前へ下げる（手前のふちの見え方を今と同じにする）
    /// ・伸ばしたあとに Ctrl+Z を押すと、まとめて元に戻る。シーンを保存するまでは、何回でも試せる
    /// ・倍率は「いまの形から」かける。続けて押すと重なる（1.3 を2回で 1.69 倍）。元の長さに戻す時は「元に戻す倍率」の値で伸ばす
    /// ・MainScene と Area2 は別々に直す（同じ倍率を、両方のシーンで1回ずつ押す）
    /// ・再生中は使えない
    /// </summary>
    public class BoardDepthTool : EditorWindow
    {
        float factor = 1.3f;
        bool moveCamera = true;
        string boardRootName = "StageVer2";

        [MenuItem("MedalPusher/盤面の奥行きを変える")]
        static void Open() => GetWindow<BoardDepthTool>("盤面の奥行き");

        void OnGUI()
        {
            var game = Object.FindFirstObjectByType<MedalPusherGame>();
            EditorGUILayout.LabelField("盤面を、ワールドの Z=0 を中心に前後へ伸ばします。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            if (game == null)
            {
                EditorGUILayout.HelpBox("このシーンに MedalPusherGame がありません。", MessageType.Warning);
                return;
            }

            var l = game.layout;
            float d = game.tuning.medalDiameter;
            float lower = l.pusherRetractZ - game.tuning.pusherStroke - l.floorFrontZ;
            EditorGUILayout.LabelField("いまの盤面", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"手前のふち Z：{l.floorFrontZ:0.###}　奥のかべ Z：{game.BackWallFrontZ:0.###}");
            EditorGUILayout.LabelField($"下段（プッシャーの前の床）の長さ：{lower:0.###}（メダル {lower / Mathf.Max(0.0001f, d):0.0} 枚分）");
            var root = FindRoot(boardRootName);
            EditorGUILayout.LabelField(root != null ? $"{boardRootName} の大きさ Z：{root.localScale.z:0.###}" : $"{boardRootName} が見つかりません");
            EditorGUILayout.Space();

            boardRootName = EditorGUILayout.TextField("盤面のモデルの名前", boardRootName);
            factor = EditorGUILayout.Slider("倍率", factor, 0.5f, 2f);
            EditorGUILayout.LabelField($"伸ばした後の下段：メダル {lower * factor / Mathf.Max(0.0001f, d):0.0} 枚分　／　元に戻す倍率：{1f / factor:0.####}");
            moveCamera = EditorGUILayout.Toggle("カメラを合わせる", moveCamera);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || root == null || Mathf.Approximately(factor, 1f)))
            {
                if (GUILayout.Button("この倍率で伸ばす", GUILayout.Height(28f))) Stretch(game, root, factor, moveCamera);
            }
            if (EditorApplication.isPlaying) EditorGUILayout.HelpBox("再生中は使えません。止めてから押してください。", MessageType.Info);
            EditorGUILayout.HelpBox("押したあと Ctrl+Z で元に戻せます。よければシーンを保存してください。MainScene と Area2 は、それぞれ開いて同じ倍率で押します。", MessageType.None);
        }

        static Transform FindRoot(string name)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == name) return go.transform;
            return null;
        }

        static void Stretch(MedalPusherGame game, Transform board, float k, bool moveCamera)
        {
            Undo.SetCurrentGroupName("盤面の奥行きを変える");
            int group = Undo.GetCurrentGroup();

            // 設定の値（前後の長さ・位置）
            Undo.RecordObject(game, "盤面の奥行き");
            var l = game.layout;
            float oldFront = l.floorFrontZ;
            l.floorFrontZ *= k;
            l.pusherDepth *= k;
            l.pusherRetractZ *= k;
            l.backWallOffset *= k;
            l.holeDepth *= k;
            game.tuning.pusherStroke *= k;
            if (moveCamera)
            {
                var cp = l.cameraPosition;
                cp.z += l.floorFrontZ - oldFront;
                l.cameraPosition = cp;
            }
            EditorUtility.SetDirty(game);

            // 盤面のモデル
            Undo.RecordObject(board, "盤面の奥行き");
            board.localScale = new Vector3(board.localScale.x, board.localScale.y, board.localScale.z * k);
            board.localPosition = new Vector3(board.localPosition.x, board.localPosition.y, board.localPosition.z * k);

            // ほかのシーンの物（ルートだけ。子はルートといっしょに動く）
            var moved = new List<string>();
            foreach (var go in board.gameObject.scene.GetRootGameObjects())
            {
                var t = go.transform;
                if (t == board || go == game.gameObject) continue;
                if (go.GetComponentInChildren<Camera>(true) != null || go.GetComponentInChildren<Light>(true) != null) continue;
                if (Mathf.Abs(t.position.z) < 0.0001f) continue;
                Undo.RecordObject(t, "盤面の奥行き");
                t.position = new Vector3(t.position.x, t.position.y, t.position.z * k);
                // 見た目のない当たり判定だけの物は、大きさも伸ばす
                bool onlyCollider = go.GetComponentInChildren<Renderer>(true) == null && go.GetComponentInChildren<Collider>(true) != null;
                if (onlyCollider) t.localScale = new Vector3(t.localScale.x, t.localScale.y, t.localScale.z * k);
                moved.Add(onlyCollider ? go.name + "（大きさも）" : go.name);
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
            Debug.Log($"[盤面の奥行き] {k:0.###} 倍に伸ばしました。手前のふち Z：{oldFront:0.###} → {l.floorFrontZ:0.###}。" +
                      $"場所を動かした物：{(moved.Count > 0 ? string.Join("、", moved) : "なし")}");
        }
    }
}
