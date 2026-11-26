using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MedalPusher
{
    /// <summary>
    /// メニュー「RECORD DROP/レコーダー・スピーカー・ゲージの位置を合わせる」。
    /// 今開いているシーン（MainScene）の recorder・Speaker・GrooveGauge を、決めた位置・向き・大きさにする。
    /// （再生中に動かして決めた値。再生を止めると戻ってしまうので、このメニューでシーンに入れる）
    ///
    /// 押したあとは Ctrl + S でシーンを保存する。Ctrl + Z で元にもどせる。
    /// スロット（SlotRefill）は、ゲームを始めた時にコードで作られるので、SlotRefill.cs の値で決めている。
    /// </summary>
    public static class PhotoPoseTool
    {
        struct Pose
        {
            public string name;
            public Vector3 position, rotation, scale;
            public Pose(string name, Vector3 position, Vector3 rotation, Vector3 scale)
            {
                this.name = name;
                this.position = position;
                this.rotation = rotation;
                this.scale = scale;
            }
        }

        // 位置・向き（度）・大きさ。Inspector の Transform の値と同じ
        static readonly Pose[] Poses =
        {
            new Pose("recorder",    new Vector3(-1.593f, 1.438f, 0.598f),  new Vector3(-82.31f, -75.41f, 0f),  new Vector3(4.95f, 4.95f, 4.95f)),
            new Pose("Speaker",     new Vector3(-1.95f, 0.639f, -0.721f),  new Vector3(5.5f, -57f, 0f),       new Vector3(1.42f, 1.43352f, 1.43352f)),
            new Pose("GrooveGauge", new Vector3(-1.534f, 0.431f, -0.545f), new Vector3(7.69f, -82.038f, 0f),  new Vector3(1.66f, 1.66f, 1.66f)),
        };

        [MenuItem("RECORD DROP/レコーダー・スピーカー・ゲージの位置を合わせる")]
        static void Apply()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("位置を合わせる", "再生中は使えません。再生を止めてから押してください。", "OK");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            var done = new List<string>();
            var missing = new List<string>();
            foreach (var pose in Poses)
            {
                Transform t = FindInScene(scene, pose.name);
                if (t == null)
                {
                    missing.Add(pose.name);
                    continue;
                }
                Undo.RecordObject(t, "位置を合わせる");
                t.localPosition = pose.position;
                t.localEulerAngles = pose.rotation;
                t.localScale = pose.scale;
                EditorUtility.SetDirty(t);
                done.Add(pose.name);
            }

            if (done.Count > 0) EditorSceneManager.MarkSceneDirty(scene);

            string msg = done.Count > 0 ? "合わせました：" + string.Join("、", done) + "\nCtrl + S でシーンを保存してください。" : "合わせた物はありません。";
            if (missing.Count > 0) msg += "\n\nシーン「" + scene.name + "」に見つかりませんでした：" + string.Join("、", missing) + "\n（MainScene を開いてから押してください）";
            EditorUtility.DisplayDialog("位置を合わせる", msg, "OK");
        }

        /// <summary>シーンの一番上の階層から、名前で探す（見つからなければ、子の中も探す）</summary>
        static Transform FindInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == name) return root.transform;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            return null;
        }
    }
}
