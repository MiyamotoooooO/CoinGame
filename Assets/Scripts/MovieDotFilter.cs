using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// Canvas で作ったムービー（オープニングなど）に、ArcadeCamera と同じドットのフィルターをかける。
    ///
    /// 【使い方】
    ///   Canvas の付いたオブジェクト（またはその親）に付けるだけ。
    ///   Add Component した時に、子の Canvas と Assets/ArcadeFX/ArcadeScreen.mat を自動で入れる。
    ///
    /// 【しくみ】
    ///   1. その Canvas を、専用のカメラで低い解像度の板（RenderTexture）に描く
    ///   2. その板を、画面いっぱいの RawImage に ArcadeScreen シェーダーで貼る
    ///      （減色・ディザ・ふちどり・走査線は、ゲーム本編と同じシェーダーがかける）
    /// </summary>
    [DisallowMultipleComponent]
    public class MovieDotFilter : MonoBehaviour
    {
        [Tooltip("ドットにする Canvas。空なら子の中から探す")]
        public Canvas targetCanvas;

        [Tooltip("オフにすると、ふつうの画面に戻す（くらべる時に使う）")]
        public bool effectEnabled = true;

        [Tooltip("同じシーンに ArcadeCamera があれば、そのドットの細かさとマテリアルを使う")]
        public bool useGameCameraSettings = true;

        [Tooltip("縦のドット数。小さいほどドットが粗くなる（ゲーム本編は 550）")]
        [Range(120, 1080)]
        public int pixelHeight = 550;

        [Tooltip("ArcadeScreen シェーダーを使ったマテリアル（ふつうは Assets/ArcadeFX/ArcadeScreen.mat）")]
        public Material screenMaterial;

        private GameObject cameraGo;
        private Camera filterCamera;
        private GameObject displayGo;
        private RawImage screenImage;
        private RenderTexture renderTexture;
        private bool applied;
        private int lastW, lastH, lastPixelHeight;
        private RenderMode originalMode;
        private Camera originalCamera;
        private int originalSorting;

        private void Reset()
        {
            targetCanvas = GetComponentInChildren<Canvas>(true);
#if UNITY_EDITOR
            screenMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/ArcadeFX/ArcadeScreen.mat");
#endif
        }

        private void OnEnable()
        {
            if (effectEnabled) Apply();
        }

        private void OnDisable()
        {
            Remove();
        }

        private void LateUpdate()
        {
            if (!applied)
            {
                if (effectEnabled) Apply();
                return;
            }
            if (!effectEnabled)
            {
                Remove();
                return;
            }
            if (Screen.width != lastW || Screen.height != lastH || pixelHeight != lastPixelHeight) MakeRenderTexture();
        }

        /// <summary>フィルターをかける</summary>
        private void Apply()
        {
            if (applied) return;
            if (targetCanvas == null) targetCanvas = GetComponentInChildren<Canvas>(true);
            if (targetCanvas == null)
            {
                Debug.LogWarning("[MovieDotFilter] Canvas が見つかりません。", this);
                return;
            }

            if (useGameCameraSettings)
            {
                ArcadeCamera arcade = FindFirstObjectByType<ArcadeCamera>();
                if (arcade != null)
                {
                    pixelHeight = arcade.pixelHeight;
                    if (screenMaterial == null) screenMaterial = arcade.screenMaterial;
                }
            }
            if (screenMaterial == null)
            {
                Shader shader = Shader.Find("MedalGame/ArcadeScreen");
                if (shader != null) screenMaterial = new Material(shader) { name = "ArcadeScreen (MovieDotFilter)" };
            }

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer < 0) uiLayer = 5;

            // ムービーを低い解像度に描く、専用のカメラ
            cameraGo = new GameObject("MovieDotFilter_Camera");
            cameraGo.hideFlags = HideFlags.DontSave;
            cameraGo.transform.SetParent(transform, false);
            filterCamera = cameraGo.AddComponent<Camera>();
            filterCamera.clearFlags = CameraClearFlags.SolidColor;
            filterCamera.backgroundColor = Color.black;
            filterCamera.cullingMask = 1 << uiLayer;
            filterCamera.orthographic = true;
            filterCamera.depth = -50;
            filterCamera.nearClipPlane = 0.1f;
            filterCamera.farClipPlane = 10f;
            filterCamera.useOcclusionCulling = false;
            filterCamera.allowHDR = false;
            filterCamera.allowMSAA = false;

            // ムービーの Canvas を、そのカメラで描くように切りかえる
            originalMode = targetCanvas.renderMode;
            originalCamera = targetCanvas.worldCamera;
            originalSorting = targetCanvas.sortingOrder;
            targetCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            targetCanvas.worldCamera = filterCamera;
            targetCanvas.planeDistance = 1f;
            foreach (Transform t in targetCanvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = uiLayer;

            // 画面いっぱいに貼る板
            displayGo = new GameObject("MovieDotFilter_Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            displayGo.hideFlags = HideFlags.DontSave;
            displayGo.transform.SetParent(transform, false);
            Canvas display = displayGo.GetComponent<Canvas>();
            display.renderMode = RenderMode.ScreenSpaceOverlay;
            display.sortingOrder = originalSorting;
            GameObject img = new GameObject("Screen", typeof(RectTransform), typeof(RawImage));
            img.transform.SetParent(displayGo.transform, false);
            screenImage = img.GetComponent<RawImage>();
            screenImage.raycastTarget = false;
            if (screenMaterial != null) screenImage.material = screenMaterial;
            RectTransform rt = screenImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            applied = true;
            MakeRenderTexture();
        }

        /// <summary>フィルターを外して、もとの Canvas に戻す</summary>
        private void Remove()
        {
            if (!applied) return;
            applied = false;
            if (targetCanvas != null)
            {
                targetCanvas.renderMode = originalMode;
                targetCanvas.worldCamera = originalCamera;
                targetCanvas.sortingOrder = originalSorting;
            }
            if (filterCamera != null) filterCamera.targetTexture = null;
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
            if (cameraGo != null) Destroy(cameraGo);
            if (displayGo != null) Destroy(displayGo);
        }

        /// <summary>画面の縦横の比に合わせて、低い解像度の板を作り直す</summary>
        private void MakeRenderTexture()
        {
            lastW = Screen.width;
            lastH = Screen.height;
            lastPixelHeight = pixelHeight;
            if (filterCamera == null) return;

            filterCamera.targetTexture = null;
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
            int h = Mathf.Max(1, pixelHeight);
            int w = Mathf.Max(1, Mathf.RoundToInt(h * (float)Screen.width / Mathf.Max(1, Screen.height)));
            renderTexture = new RenderTexture(w, h, 24)
            {
                name = "MovieDotFilter",
                filterMode = FilterMode.Point,   // 引きのばす時にぼかさない（ドットに見える）
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
            };
            renderTexture.Create();
            filterCamera.targetTexture = renderTexture;
            if (screenImage != null) screenImage.texture = renderTexture;
        }
    }
}
