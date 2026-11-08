using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 端の穴の表示（SideHoleMarker）を、上のメニューから置くためのエディター用スクリプト（Editor フォルダに置く。ゲームには入らない）。
    ///
    /// ・上のメニュー「MedalPusher」→「端の穴の表示を置く」：
    ///   SideHoleMarker の付いた GameObject を作り、フォントにモニターと同じフォント（Resources/MonitorFonts/Body）を入れる。
    ///   もうシーンにある時は、それを選んでフォントだけ入れ直す。
    /// ・置いたあとは、シーンを保存して再生すると、盤面の手前のふちに帯と文字が出る（帯は再生中だけ出る）。
    /// </summary>
    public static class SideHoleMarkerEditor
    {
        [MenuItem("MedalPusher/端の穴の表示を置く")]
        [MenuItem("GameObject/MedalPusher/端の穴の表示を置く", false, 12)]
        static void CreateMarker()
        {
            var marker = Object.FindFirstObjectByType<SideHoleMarker>();
            bool created = false;
            if (marker == null)
            {
                var go = new GameObject("SideHoleMarker");
                Undo.RegisterCreatedObjectUndo(go, "端の穴の表示を置く");
                marker = Undo.AddComponent<SideHoleMarker>(go);
                created = true;
            }

            if (marker.font == null)
            {
                var font = Resources.Load<Font>(MonitorDisplay.FontFolder + "/Body");
                if (font != null)
                {
                    Undo.RecordObject(marker, "端の穴の表示のフォント");
                    marker.font = font;
                }
            }
            if (marker.game == null)
            {
                var game = Object.FindFirstObjectByType<MedalPusherGame>();
                if (game != null)
                {
                    Undo.RecordObject(marker, "端の穴の表示のつなぎ先");
                    marker.game = game;
                }
            }

            EditorUtility.SetDirty(marker);
            EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
            Selection.activeGameObject = marker.gameObject;

            string fontNote = marker.font != null ? $"フォント：{marker.font.name}" : "フォント：見つからなかったので、Unity に入っているフォントを使います";
            EditorUtility.DisplayDialog("端の穴の表示",
                (created ? "SideHoleMarker を置きました。" : "SideHoleMarker はもう置いてあったので、選びました。") +
                $"\n{fontNote}\n\nシーンを保存（Ctrl + S）してから再生すると、盤面の手前のふちに帯と文字が出ます。", "OK");
        }
    }
}
