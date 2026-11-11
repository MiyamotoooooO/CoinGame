using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// Canvas に付けると、その UI にも ArcadeCamera のドットのフィルターがかかるようにする。
    ///
    /// しくみ：
    ///   ゲームが始まった時に、Canvas を「ゲームのカメラの中に描く（Screen Space - Camera）」に切りかえる。
    ///   ゲームのカメラは ArcadeCamera で低い解像度に描いているので、UI も 3D と一緒にドットになり、
    ///   ArcadeScreen のシェーダー（減色・ふちどり・走査線）がかかる。
    ///
    /// マウスについて：
    ///   低い解像度で描いているので、そのままだとマウスの位置とボタンの位置がずれる。
    ///   そこで、マウスの位置を直してくれる ArcadeUIRaycaster をいっしょに付ける。
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class ArcadeUICanvas : MonoBehaviour
    {
        [Tooltip("UI を描いてもらうカメラ。空なら ArcadeCamera が付いたカメラ（無ければ Main Camera）を使う")]
        public Camera targetCamera;

        [Tooltip("カメラから UI までの距離。小さいほど 3D の物に隠されにくい（カメラの Near より少しだけ大きくする）")]
        public float planeDistance = 0.31f;

        private void Awake()
        {
            Apply();
        }

        /// <summary>Canvas をゲームのカメラの中に描く設定にする</summary>
        public void Apply()
        {
            Camera cam = targetCamera;
            if (cam == null)
            {
                ArcadeCamera arcade = FindFirstObjectByType<ArcadeCamera>();
                cam = arcade != null ? arcade.GetComponent<Camera>() : Camera.main;
            }
            if (cam == null)
            {
                Debug.LogWarning("[ArcadeUICanvas] カメラが見つからないので、ドットのフィルターをかけられません。", this);
                return;
            }

            Canvas canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = Mathf.Max(cam.nearClipPlane + 0.01f, planeDistance);

            // もとのマウス判定を止めて、位置を直してくれる物に入れかえる
            foreach (GraphicRaycaster gr in GetComponents<GraphicRaycaster>())
            {
                if (!(gr is ArcadeUIRaycaster)) gr.enabled = false;
            }
            if (GetComponent<ArcadeUIRaycaster>() == null) gameObject.AddComponent<ArcadeUIRaycaster>();
        }
    }
}
