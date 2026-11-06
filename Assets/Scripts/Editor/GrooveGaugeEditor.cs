using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// グルーヴゲージをボタン1つで置くためのエディター用スクリプト（Editor フォルダに置く。ゲームには入らない）。
    ///
    /// ・上のメニュー「MedalPusher」→「グルーヴゲージを置く」：GrooveGauge を作り、レコーダーの下の壁に、壁に向けて置く
    ///   （もうシーンにある時は、それを置き直す）
    /// ・GrooveGauge の Inspector の「レコーダーの下に置き直す」ボタン：同じことを、選んでいるゲージにする
    /// ・GrooveGauge を選ぶと、シーンビューにケーブルを動かす点が出る
    ///     「ケーブルの始まり」「さし口」と書いた所の矢印（赤・緑・青）：ケーブルの両はしを動かす
    ///     黄色い点：ケーブルの曲がり（ドラッグすると Custom Curve がオンになる）
    ///
    /// 置き方：レコーダー（TurntableController）の向きから壁の向きを求め、
    /// レコーダーのすぐ下の壁に、ゲージの表が盤面の方を向くように置く。ケーブルはレコーダーの下のはしから出す。
    /// </summary>
    [CustomEditor(typeof(GrooveGauge))]
    [CanEditMultipleObjects]
    public class GrooveGaugeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("レコーダーの下に置き直す", GUILayout.Height(30)))
            {
                foreach (var t in targets) GrooveGaugePlacer.PlaceUnderRecorder((GrooveGauge)t);
            }
            if (GUILayout.Button("ケーブルの曲がりを自動に戻す"))
            {
                foreach (var t in targets)
                {
                    var g = (GrooveGauge)t;
                    Undo.RecordObject(g, "ケーブルの曲がりを自動に戻す");
                    g.customCurve = false;
                    g.MarkDirty();
                    EditorUtility.SetDirty(g);
                }
            }
            EditorGUILayout.HelpBox("ケーブルは、シーンビューの点をドラッグして動かせます。\n" +
                                    "「ケーブルの始まり」「さし口」の矢印：両はしを動かす　黄色い点：曲がりを変える",
                MessageType.None);
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        /// <summary>シーンビューに、ケーブルを動かす点を出す。</summary>
        void OnSceneGUI()
        {
            var g = (GrooveGauge)target;
            var st = g.StartTransform;
            Vector3 p0 = g.StartPoint, p3 = g.EndPoint;
            g.GetBendPoints(out var p1, out var p2);

            // 曲がりの点とのつながりを点線で
            Handles.color = new Color(1f, 0.85f, 0.25f, 0.8f);
            Handles.DrawDottedLine(p0, p1, 4f);
            Handles.DrawDottedLine(p3, p2, 4f);

            // 始まり（レコーダー側）
            if (st != null)
            {
                Handles.Label(p0, "  ケーブルの始まり");
                EditorGUI.BeginChangeCheck();
                Vector3 n0 = Handles.PositionHandle(p0, st.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(g, "ケーブルの始まりを動かす");
                    g.cableStartOffset = st.InverseTransformPoint(n0);
                    Changed(g);
                }
            }

            // さし口（ゲージ側）：ゲージの面に沿って動かす
            Handles.Label(p3, "  さし口");
            EditorGUI.BeginChangeCheck();
            Vector3 n3 = Handles.PositionHandle(p3, g.transform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(g, "ケーブルのさし口を動かす");
                Vector3 local = g.transform.InverseTransformPoint(n3);
                float r = Mathf.Max(0.0001f, g.outerRadius);
                g.plugPosition = new Vector2(local.x / r, (local.y - g.cableRadius) / r);
                Changed(g);
            }

            // 曲がりの点（2つ）
            float size1 = HandleUtility.GetHandleSize(p1) * 0.08f;
            float size2 = HandleUtility.GetHandleSize(p2) * 0.08f;
            Handles.color = new Color(1f, 0.85f, 0.25f);
            EditorGUI.BeginChangeCheck();
            Vector3 m1 = Handles.FreeMoveHandle(p1, size1, Vector3.zero, Handles.SphereHandleCap);
            Vector3 m2 = Handles.FreeMoveHandle(p2, size2, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(g, "ケーブルの曲がりを動かす");
                g.customCurve = true;
                g.startBend = m1 - p0;
                g.endBend = g.transform.InverseTransformVector(m2 - p3);
                Changed(g);
            }
        }

        static void Changed(GrooveGauge g)
        {
            g.MarkDirty();
            EditorUtility.SetDirty(g);
            EditorApplication.QueuePlayerLoopUpdate(); // 止まっている時もケーブルをすぐ作り直す
            SceneView.RepaintAll();
        }

        [MenuItem("MedalPusher/グルーヴゲージを置く")]
        [MenuItem("GameObject/MedalPusher/グルーヴゲージを置く", false, 10)]
        static void CreateGauge()
        {
            var gauge = Object.FindFirstObjectByType<GrooveGauge>();
            if (gauge == null)
            {
                var go = new GameObject("GrooveGauge");
                Undo.RegisterCreatedObjectUndo(go, "グルーヴゲージを置く");
                gauge = Undo.AddComponent<GrooveGauge>(go);
            }
            GrooveGaugePlacer.PlaceUnderRecorder(gauge);
            Selection.activeGameObject = gauge.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }

    static class GrooveGaugePlacer
    {
        /// <summary>ゲージをレコーダーの下の壁に置く。レコーダーが無ければ、シーンビューの中心に置く。</summary>
        public static void PlaceUnderRecorder(GrooveGauge gauge)
        {
            var tt = Object.FindFirstObjectByType<TurntableController>();
            Undo.RecordObject(gauge.transform, "グルーヴゲージを置き直す");
            Undo.RecordObject(gauge, "グルーヴゲージを置き直す");

            if (tt == null)
            {
                var view = SceneView.lastActiveSceneView;
                gauge.transform.position = view != null ? view.pivot : Vector3.zero;
                EditorUtility.DisplayDialog("グルーヴゲージ",
                    "レコーダー（TurntableController の付いた物）が見つからないので、シーンビューの中心に置きました。手で動かしてください。", "OK");
                MarkDirty(gauge);
                return;
            }

            // レコーダーの大きさ（子のモデルをまとめた箱）
            var renderers = tt.GetComponentsInChildren<Renderer>();
            Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(tt.transform.position, Vector3.one * 0.5f);
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            // 壁の向き：レコーダーの上（Y）が壁から盤面の方を向いている
            Vector3 normal = tt.transform.up.normalized;
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, normal).normalized;   // 壁に沿って下
            if (down == Vector3.zero) down = -tt.transform.forward;
            Vector3 side = Vector3.Cross(normal, down).normalized;                    // 壁に沿って横

            float eDown = Extent(b, down);
            float eSide = Extent(b, side);
            float eNormal = Extent(b, normal);
            Vector3 wallPoint = b.center - normal * eNormal;                          // レコーダーの裏（壁の面）

            float radius = Mathf.Max(0.05f, eSide * 0.75f);
            Vector3 pos = wallPoint + down * (eDown + radius * 1.35f) + normal * 0.01f;

            // 壁に当たり判定があれば、壁の面にぴったり付ける
            if (Physics.Raycast(pos + normal * 0.5f, -normal, out var hit, 1.5f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(tt.transform))
                pos = hit.point + normal * 0.01f;

            gauge.transform.SetParent(null, true);
            gauge.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-normal, -down)); // 青い矢印（Z）は壁の中へ
            gauge.transform.localScale = Vector3.one;
            gauge.outerRadius = radius;
            gauge.cableRadius = Mathf.Max(0.004f, radius * 0.065f);
            gauge.cableStart = tt.transform;
            // ケーブルはレコーダーの下のはし（壁の少し手前）から出す
            gauge.cableStartOffset = tt.transform.InverseTransformPoint(wallPoint + down * eDown * 0.85f + normal * radius * 0.08f);

            MarkDirty(gauge);
        }

        /// <summary>箱の、ある向きへの半分の長さ。</summary>
        static float Extent(Bounds b, Vector3 dir)
        {
            var e = b.extents;
            return Mathf.Abs(e.x * dir.x) + Mathf.Abs(e.y * dir.y) + Mathf.Abs(e.z * dir.z);
        }

        static void MarkDirty(GrooveGauge gauge)
        {
            EditorUtility.SetDirty(gauge);
            EditorUtility.SetDirty(gauge.transform);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(gauge.gameObject.scene);
        }
    }
}
