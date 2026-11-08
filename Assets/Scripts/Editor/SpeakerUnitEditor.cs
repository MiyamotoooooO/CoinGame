using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// スピーカーをボタン1つで置くためのエディター用スクリプト（Editor フォルダに置く。ゲームには入らない）。
    ///
    /// ・上のメニュー「MedalPusher」→「スピーカーを置く」：SpeakerUnit を作り、レコーダーと左右反対の壁に置く
    ///   （もうシーンにある時は、それを置き直す）
    /// ・SpeakerUnit の Inspector の「レコーダーの反対側に置き直す」ボタン：同じことを、選んでいるスピーカーにする
    /// ・「テスト：はずませる」ボタン（再生中だけ）：ドンとはずんで音符を飛ばす
    ///
    /// 置き方：筐体（Cabinet）のまん中を境に、レコーダー（TurntableController）を左右反対に写した場所の壁に、
    /// 前の面が盤面とプレイヤーの方を少し向くように置く。大きさはレコーダーに合わせる。
    /// </summary>
    [CustomEditor(typeof(SpeakerUnit))]
    [CanEditMultipleObjects]
    public class SpeakerUnitEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("レコーダーの反対側に置き直す", GUILayout.Height(30)))
                foreach (var t in targets) SpeakerPlacer.PlaceOppositeRecorder((SpeakerUnit)t);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("テスト：はずませる（再生中だけ）"))
                    foreach (var t in targets)
                    {
                        var s = (SpeakerUnit)t;
                        s.Pulse(1.5f);
                        s.EmitNotes(12);
                    }
            }
            EditorGUILayout.HelpBox("位置・向き・大きさを自動で合わせます。そのあと Transform で細かく直せます。", MessageType.None);
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        [MenuItem("MedalPusher/スピーカーを置く")]
        [MenuItem("GameObject/MedalPusher/スピーカーを置く", false, 11)]
        static void CreateSpeaker()
        {
            var speaker = Object.FindFirstObjectByType<SpeakerUnit>();
            if (speaker == null)
            {
                var go = new GameObject("Speaker");
                Undo.RegisterCreatedObjectUndo(go, "スピーカーを置く");
                speaker = Undo.AddComponent<SpeakerUnit>(go);
            }
            SpeakerPlacer.PlaceOppositeRecorder(speaker);
            Selection.activeGameObject = speaker.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }

    static class SpeakerPlacer
    {
        const float ModelDepth = 0.42f;   // SpeakerUnit の奥行き（高さ 1 に対して）

        public static void PlaceOppositeRecorder(SpeakerUnit speaker)
        {
            var tt = Object.FindFirstObjectByType<TurntableController>();
            Undo.RecordObject(speaker.transform, "スピーカーを置き直す");

            if (tt == null)
            {
                var view = SceneView.lastActiveSceneView;
                speaker.transform.position = view != null ? view.pivot : Vector3.zero;
                EditorUtility.DisplayDialog("スピーカー",
                    "レコーダー（TurntableController の付いた物）が見つからないので、シーンビューの中心に置きました。手で動かしてください。", "OK");
                MarkDirty(speaker);
                return;
            }

            // レコーダーの大きさ
            var renderers = tt.GetComponentsInChildren<Renderer>();
            Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(tt.transform.position, Vector3.one * 0.5f);
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            // 左右反対にする境目（筐体のまん中。見つからなければ x = 0）
            float mirrorX = 0f;
            var cabinet = GameObject.Find("Cabinet");
            if (cabinet != null)
            {
                var cr = cabinet.GetComponentsInChildren<Renderer>();
                if (cr.Length > 0)
                {
                    Bounds cb = cr[0].bounds;
                    foreach (var r in cr) cb.Encapsulate(r.bounds);
                    mirrorX = cb.center.x;
                }
            }

            // レコーダーの壁の向き（上 Y が壁から盤面の方を向いている）を左右反対にする
            Vector3 n = tt.transform.up.normalized;
            Vector3 normal = new Vector3(-n.x, n.y, n.z).normalized;
            Vector3 center = new Vector3(2f * mirrorX - b.center.x, b.center.y, b.center.z);
            float eNormal = Extent(b, n);
            Vector3 wallPoint = center - normal * eNormal;                       // 壁の面

            // 前の面を、盤面とプレイヤーの方へ少し向ける
            Vector3 facing = normal;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 toCam = (cam.transform.position - wallPoint).normalized;
                facing = Vector3.Slerp(normal, toCam, 0.3f).normalized;
            }
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, facing).normalized;
            if (up == Vector3.zero) up = Vector3.up;

            // 大きさ：レコーダーの、壁に沿った長い方の長さに合わせる
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, normal).normalized;
            Vector3 side = Vector3.Cross(normal, down).normalized;
            float height = Mathf.Max(0.1f, Mathf.Max(Extent(b, down), Extent(b, side)) * 2f * 1.05f);

            // 原点は前の面の下のまん中。背中が壁に付き、上下のまん中がレコーダーの高さにそろうように置く
            Vector3 pos = wallPoint + facing * (ModelDepth * height) - up * (height * 0.5f);

            speaker.transform.SetParent(null, true);
            speaker.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-facing, up));   // 青い矢印（Z）は壁の中へ
            speaker.transform.localScale = Vector3.one * height;
            MarkDirty(speaker);
        }

        static float Extent(Bounds b, Vector3 dir)
        {
            var e = b.extents;
            return Mathf.Abs(e.x * dir.x) + Mathf.Abs(e.y * dir.y) + Mathf.Abs(e.z * dir.z);
        }

        static void MarkDirty(SpeakerUnit speaker)
        {
            EditorUtility.SetDirty(speaker);
            EditorUtility.SetDirty(speaker.transform);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(speaker.gameObject.scene);
        }
    }
}
