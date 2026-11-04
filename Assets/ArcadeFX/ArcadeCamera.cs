using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ゲーム画面にアーケード風の効果をかける。Main Camera に付けて使う。
///
/// しくみ：
/// 1. このカメラの映像を、低い解像度の RenderTexture に描く（ドット化）
/// 2. その RenderTexture を、画面いっぱいの RawImage に ArcadeScreen シェーダーで貼る
///    （減色、ディザ、ふちどり、走査線、周辺減光はシェーダーがかける）
///
/// HUD の Canvas は、ここで作る画面用の Canvas より Sort Order を大きくすれば、
/// 効果がかからずにくっきり表示される。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class ArcadeCamera : MonoBehaviour
{
    [Header("ドット化")]
    [Tooltip("ゲーム画面を描く縦のドット数。小さいほどドットが粗くなる（270 で 1080p の4分の1）")]
    [Range(120, 1080)]
    public int pixelHeight = 270;

    [Header("画面の効果")]
    [Tooltip("ArcadeScreen シェーダーを使ったマテリアル")]
    public Material screenMaterial;

    [Tooltip("画面用の Canvas の Sort Order。HUD の Canvas はこれより大きくする")]
    public int sortingOrder = -100;

    [Tooltip("オフにすると効果を外し、ふつうの画面に戻す（比較用）")]
    public bool effectEnabled = true;

    Camera _camera;
    RenderTexture _renderTexture;
    GameObject _canvasObject;
    GameObject _clearCameraObject;
    RawImage _screenImage;
    int _lastScreenWidth;
    int _lastScreenHeight;
    int _lastPixelHeight;
    bool _lastEffectEnabled;

    /// <summary>今ゲーム画面を描いている RenderTexture（効果がオフなら null）</summary>
    public RenderTexture RenderTexture => _renderTexture;

    void OnEnable()
    {
        _camera = GetComponent<Camera>();
        if (screenMaterial == null)
        {
            // マテリアルが未設定なら、シェーダーから作る（ArcadeScreen.mat を設定しておくのがおすすめ）
            var shader = Shader.Find("MedalGame/ArcadeScreen");
            if (shader != null)
            {
                screenMaterial = new Material(shader) { name = "ArcadeScreen (自動作成)" };
            }
            else
            {
                Debug.LogWarning("ArcadeCamera: ArcadeScreen シェーダーが見つかりません。Screen Material を設定してください。", this);
            }
        }
        BuildScreen();
        Rebuild();
    }

    void OnDisable()
    {
        if (_camera != null)
        {
            _camera.targetTexture = null;
        }
        ReleaseRenderTexture();
        if (_canvasObject != null)
        {
            Destroy(_canvasObject);
        }
        if (_clearCameraObject != null)
        {
            Destroy(_clearCameraObject);
        }
    }

    void LateUpdate()
    {
        bool changed = Screen.width != _lastScreenWidth
                       || Screen.height != _lastScreenHeight
                       || pixelHeight != _lastPixelHeight
                       || effectEnabled != _lastEffectEnabled;
        if (changed)
        {
            Rebuild();
        }
        if (_screenImage != null && _screenImage.material != screenMaterial)
        {
            _screenImage.material = screenMaterial;
        }
    }

    /// <summary>
    /// 画面上の位置（マウスの位置など）から、盤面へ向かう Ray を作る。
    /// このカメラは RenderTexture に描いているので、Camera.ScreenPointToRay ではなくこちらを使う。
    /// </summary>
    public Ray ScreenPointToRay(Vector3 screenPosition)
    {
        var viewport = new Vector3(screenPosition.x / Screen.width, screenPosition.y / Screen.height, 0f);
        return _camera.ViewportPointToRay(viewport);
    }

    void BuildScreen()
    {
        // 画面用の Canvas の後ろを黒く塗るだけのカメラ（何も映さない）
        _clearCameraObject = new GameObject("ArcadeScreen_Clear");
        _clearCameraObject.hideFlags = HideFlags.DontSave;
        var clearCamera = _clearCameraObject.AddComponent<Camera>();
        clearCamera.cullingMask = 0;
        clearCamera.clearFlags = CameraClearFlags.SolidColor;
        clearCamera.backgroundColor = Color.black;
        clearCamera.depth = _camera.depth - 1;
        clearCamera.useOcclusionCulling = false;

        // ゲーム画面を貼る Canvas と RawImage
        _canvasObject = new GameObject("ArcadeScreen_Canvas");
        _canvasObject.hideFlags = HideFlags.DontSave;
        var canvas = _canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var imageObject = new GameObject("Screen");
        imageObject.transform.SetParent(_canvasObject.transform, false);
        _screenImage = imageObject.AddComponent<RawImage>();
        _screenImage.raycastTarget = false;
        _screenImage.material = screenMaterial;

        var rect = _screenImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void Rebuild()
    {
        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;
        _lastPixelHeight = pixelHeight;
        _lastEffectEnabled = effectEnabled;

        _camera.targetTexture = null;
        ReleaseRenderTexture();

        if (!effectEnabled)
        {
            // 効果を外したときは、カメラがそのまま画面に描く
            _canvasObject.SetActive(false);
            _clearCameraObject.SetActive(false);
            return;
        }

        int height = Mathf.Max(1, pixelHeight);
        int width = Mathf.Max(1, Mathf.RoundToInt(height * (float)Screen.width / Screen.height));

        _renderTexture = new RenderTexture(width, height, 24)
        {
            name = "ArcadeScreen",
            filterMode = FilterMode.Point,     // 引き伸ばすときにぼかさない
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1,
        };
        _renderTexture.Create();

        _camera.targetTexture = _renderTexture;
        _screenImage.texture = _renderTexture;
        _canvasObject.SetActive(true);
        _clearCameraObject.SetActive(true);
    }

    void ReleaseRenderTexture()
    {
        if (_renderTexture == null)
        {
            return;
        }
        if (_screenImage != null)
        {
            _screenImage.texture = null;
        }
        _renderTexture.Release();
        Destroy(_renderTexture);
        _renderTexture = null;
    }
}
