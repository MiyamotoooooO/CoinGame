using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class OpeningMovieManager : MonoBehaviour
{
    // ゲーム起動中に一度だけ再生するための静的フラグ
    private static bool hasPlayedMovieInSession = false;

    [Header("■ 基本・シーン・スタート設定")]
    [Tooltip("ムービーを流す対象のScene名（空欄の場合はこのスクリプトがあるSceneでそのまま動作します）")]
    [SerializeField] private string targetSceneName = "";

    [Tooltip("ムービーを開始するスタートボタン（未設定の場合はシーン開始時に自動スタートも可能）")]
    [SerializeField] private Button startButton;

    [Tooltip("スタートボタンが押される前に表示しておくタイトル画面などの親オブジェクト（任意）")]
    [SerializeField] private GameObject startScreenRoot;

    [Tooltip("ムービー中のみ操作を無効化し、ムービー終了後に有効化するゲームプレイ用オブジェクト群")]
    [SerializeField] private List<GameObject> gameplayObjectsToEnableAfterMovie = new List<GameObject>();

    [Header("■ フェード演出設定")]
    [Tooltip("かっこいいフェードアウト・フェードインにかかる時間（秒）")]
    [SerializeField] private float coolFadeDuration = 0.8f;

    [Header("■ シーン切り替え用オブジェクト (Object 1 〜 10)")]
    [SerializeField] private GameObject object1;
    [SerializeField] private GameObject object2;
    [SerializeField] private GameObject object3;
    [Tooltip("UFOが元の位置に戻った際に表示されるオブジェクト（灰色化開始トリガー）")]
    [SerializeField] private GameObject object4;
    [SerializeField] private GameObject object5;
    [SerializeField] private GameObject object6;
    [SerializeField] private GameObject object7;
    [SerializeField] private GameObject object8;
    [SerializeField] private GameObject object9;
    [SerializeField] private GameObject object10;

    [Header("■ Object1 の演出設定")]
    [Tooltip("常に360度回転し続けるUIオブジェクト")]
    [SerializeField] private RectTransform alwaysRotatingUI;
    [Tooltip("常時回転の速度（度/秒）")]
    [SerializeField] private float alwaysRotateSpeed = 90f;

    [Tooltip("フェードイン1秒後に現在の角度からZ軸が0へ徐々に戻るUIオブジェクト")]
    [SerializeField] private RectTransform zAngleToZeroUI;
    [Tooltip("Z軸が0になるまでの時間（秒）")]
    [SerializeField] private float zAngleRotateDuration = 1.2f;

    [Tooltip("角度が0になった時に『キラーン』と表示されるUIオブジェクト")]
    [SerializeField] private RectTransform kiraanUI;
    [Tooltip("キラーンUIが表示されてからフェードアウトし始めるまでの待機時間（秒）")]
    [SerializeField] private float kiraanHoldDuration = 1.0f;
    [Tooltip("キラーンUIがフェードアウトして消える時間（秒）")]
    [SerializeField] private float kiraanFadeOutDuration = 0.6f;

    [Header("■ BGM設定（Object1の角度0で再生 〜 Object2終了時にフェードアウト）")]
    [Tooltip("BGMを流すAudioSource（Object1が非表示になっても消えないよう独立して管理されます）")]
    [SerializeField] private AudioSource bgmAudioSource;
    [Tooltip("再生するBGMクリップ（AudioSource側に設定済みなら空欄でも可）")]
    [SerializeField] private AudioClip bgmClip;

    [Header("■ Object3 & Object4（UFO・CD竜巻・右から灰色化）設定")]
    [Tooltip("UFOのオブジェクト（最初はぐらぐら揺れ、その後3秒間動き回る）")]
    [SerializeField] private Transform ufoObject;
    [Tooltip("UFOの通常のぐらぐら揺れる幅")]
    [SerializeField] private float ufoIdleWobbleAmount = 8f;
    [Tooltip("UFOが3秒間動き回る際のX軸・Z軸の最大移動幅（UIの場合はXを中心に移動）")]
    [SerializeField] private Vector3 ufoMoveRange = new Vector3(250f, 25f, 150f);

    [Tooltip("竜巻のように上空へ上がっていく複数種類のCDのUIオブジェクト（Prefabまたは元オブジェクト）")]
    [SerializeField] private List<RectTransform> cdUIPrefabs = new List<RectTransform>();
    [Tooltip("CDが発生する開始ポジション1")]
    [SerializeField] private Transform cdSpawnPos1;
    [Tooltip("CDが発生する開始ポジション2")]
    [SerializeField] private Transform cdSpawnPos2;
    [Tooltip("CDがめがけて上がっていき、到達したら削除される上空の目標ポジション")]
    [SerializeField] private Transform cdAbsorbTargetPos;
    [Tooltip("CDが竜巻状に発生する総時間（画面が灰色になりきるまでの時間）")]
    [SerializeField] private float grayscaleAndTornadoDuration = 4.5f;

    [Header("■ グレースケール除外設定")]
    [Tooltip("画面が灰色になっても絶対に灰色にならないようにするUIオブジェクト群")]
    [SerializeField] private List<GameObject> neverGrayscaleUIObjects = new List<GameObject>();

    [Header("■ Object5 のテキスト設定")]
    [Tooltip("Object5で1文字ずつ表示するテキスト（Text または TextMeshProUGUI がついたオブジェクト）")]
    [SerializeField] private GameObject textObject5;
    [TextArea(2, 4), Tooltip("表示する文章（空欄の場合はテキストコンポーネントに元々入っている文字を使用）")]
    [SerializeField] private string overrideString5 = "";

    [Header("■ Object6 のテキスト設定")]
    [Tooltip("Object6で1文字ずつ表示するテキスト")]
    [SerializeField] private GameObject textObject6;
    [TextArea(2, 4)]
    [SerializeField] private string overrideString6 = "";

    [Header("■ Object7 のふわっと移動 ＆ ズーム設定")]
    [Tooltip("フェードイン後に特定の位置や角度へふわっと移動するオブジェクト")]
    [SerializeField] private Transform floatMoveObject7;
    [Tooltip("移動先の目標Transform（設定されている場合はこちらを優先）")]
    [SerializeField] private Transform floatMoveTargetTransform7;
    [Tooltip("移動先の位置（TargetTransformが未設定の場合に使用）")]
    [SerializeField] private Vector3 floatMoveTargetPosition7;
    [Tooltip("移動先の角度（TargetTransformが未設定の場合に使用）")]
    [SerializeField] private Vector3 floatMoveTargetEulerAngles7;
    [Tooltip("ふわっと移動するのにかかる時間（秒）")]
    [SerializeField] private float floatMoveDuration7 = 1.5f;

    [Tooltip("0.5秒後にズームしながらフェードアウトする際のズーム対象ルート（Object7自身やカメラ等）")]
    [SerializeField] private Transform zoomTargetRoot7;
    [Tooltip("ズームでめがける特定のPosition（Transform指定）")]
    [SerializeField] private Transform zoomFocusPoint7;
    [Tooltip("ズーム倍率")]
    [SerializeField] private float zoomScaleMultiplier7 = 2.2f;

    [Header("■ Object8 のテキスト ＆ 回転設定（ここから通常色に戻る）")]
    [Tooltip("Object8で1文字ずつ表示するテキスト")]
    [SerializeField] private GameObject textObject8;
    [TextArea(2, 4)]
    [SerializeField] private string overrideString8 = "";
    [Tooltip("テキスト表示中にゆっくり360度回転するオブジェクト")]
    [SerializeField] private Transform slowRotateObject8;
    [SerializeField] private float slowRotateSpeed8 = 45f;

    [Header("■ Object9 のテキスト設定")]
    [Tooltip("Object9で1文字ずつ表示するテキスト")]
    [SerializeField] private GameObject textObject9;
    [TextArea(2, 4)]
    [SerializeField] private string overrideString9 = "";

    [Header("■ Object10 の叩きつけUI・回転UI・テキスト設定")]
    [Tooltip("フェードイン1秒後に叩きつけるように表示されるUIオブジェクト")]
    [SerializeField] private RectTransform slamUIObject10;
    [Tooltip("叩きつけUIが表示される目標位置（Transformで指定する場合）")]
    [SerializeField] private Transform slamTargetPosTransform10;
    [Tooltip("叩きつけUIが表示される目標AnchoredPosition（Transform未設定時に使用）")]
    [SerializeField] private Vector2 slamTargetAnchoredPos10;
    [Tooltip("叩きつけ演出後に表示され、ゆっくり360度回転する別のUIオブジェクト")]
    [SerializeField] private RectTransform slowRotateUI10;
    [SerializeField] private float slowRotateSpeed10 = 45f;
    [Tooltip("叩きつけUIの1文字ずつ表示するテキスト")]
    [SerializeField] private GameObject textObject10;
    [TextArea(2, 4)]
    [SerializeField] private string overrideString10 = "";

    [Header("■ テキスト表示速度設定")]
    [Tooltip("1文字表示するごとの間隔（秒）")]
    [SerializeField] private float charTypeInterval = 0.06f;

    // 内部制御用変数
    private Canvas movieOverlayCanvas;
    private CanvasGroup fadeCanvasGroup;
    private RectTransform topLetterbox;
    private RectTransform bottomLetterbox;

    // グレースケール演出用
    private GameObject grayscaleOverlayRoot;
    private Material grayscaleWipeMaterial;
    private float currentGrayscaleProgress = 0f;
    private bool isGrayscaleActive = false;

    // 常時回転・状態フラグ
    private bool isMovieRunning = false;
    private bool isUfoIdleWobbling = false;
    private bool isSlowRotating8 = false;
    private bool isSlowRotating10 = false;
    private bool isTornadoSpawning = false;

    private Vector3 ufoInitialLocalPos;
    private Quaternion ufoInitialLocalRot;

    private void Awake()
    {
        // 指定シーン名があり、現在のシーンと異なる場合は何もしない
        if (!string.IsNullOrEmpty(targetSceneName) && SceneManager.GetActiveScene().name != targetSceneName)
        {
            return;
        }

        // すでに今回のゲーム起動中に1度ムービーが流れていた場合はスキップして通常ゲーム状態にする
        if (hasPlayedMovieInSession)
        {
            SetAllMovieObjectsActive(false);
            if (startScreenRoot != null) startScreenRoot.SetActive(false);
            SetGameplayObjectsActive(true);
            enabled = false;
            return;
        }

        // 初期状態としてムービー用オブジェクトを非表示に整理
        InitializeMovieObjectsState();
        CreateMovieOverlayUI();

        if (startButton != null)
        {
            startButton.onClick.RemoveListener(OnStartButtonClicked);
            startButton.onClick.AddListener(OnStartButtonClicked);
        }
    }

    private void Start()
    {
        if (!enabled) return;

        // もしスタートボタンが設定されていない場合は、シーン到達時に自動でムービーを開始する
        if (startButton == null && !isMovieRunning)
        {
            OnStartButtonClicked();
        }
    }

    private void Update()
    {
        if (!isMovieRunning) return;

        // 1. Object1〜常時360度回転するUI
        if (alwaysRotatingUI != null && alwaysRotatingUI.gameObject.activeInHierarchy)
        {
            alwaysRotatingUI.Rotate(0f, 0f, -alwaysRotateSpeed * Time.deltaTime);
        }

        // 2. UFOの待機中ぐらぐら揺れ演出
        if (isUfoIdleWobbling && ufoObject != null && ufoObject.gameObject.activeInHierarchy)
        {
            float offsetX = Mathf.Sin(Time.time * 4.5f) * ufoIdleWobbleAmount;
            float offsetY = Mathf.Cos(Time.time * 3.2f) * (ufoIdleWobbleAmount * 0.4f);
            float tiltZ = Mathf.Sin(Time.time * 5.0f) * 3.5f;
            ufoObject.localPosition = ufoInitialLocalPos + new Vector3(offsetX, offsetY, 0f);
            ufoObject.localRotation = ufoInitialLocalRot * Quaternion.Euler(0f, 0f, tiltZ);
        }

        // 3. Object8のゆっくり360度回転
        if (isSlowRotating8 && slowRotateObject8 != null && slowRotateObject8.gameObject.activeInHierarchy)
        {
            slowRotateObject8.Rotate(0f, 0f, -slowRotateSpeed8 * Time.deltaTime);
        }

        // 4. Object10のゆっくり360度回転UI
        if (isSlowRotating10 && slowRotateUI10 != null && slowRotateUI10.gameObject.activeInHierarchy)
        {
            slowRotateUI10.Rotate(0f, 0f, -slowRotateSpeed10 * Time.deltaTime);
        }
    }

    /// <summary>
    /// スタートボタンが押された時に呼ばれる開始メソッド
    /// </summary>
    public void OnStartButtonClicked()
    {
        if (isMovieRunning || hasPlayedMovieInSession) return;

        hasPlayedMovieInSession = true;
        isMovieRunning = true;

        if (startButton != null)
        {
            startButton.interactable = false;
        }

        SetGameplayObjectsActive(false);
        StartCoroutine(PlayFullMovieRoutine());
    }

    /// <summary>
    /// ムービー全体のメインコルーチン
    /// </summary>
    private IEnumerator PlayFullMovieRoutine()
    {
        // =========================================================================
        // 【1】スタートボタン押下 ➔ かっこいいフェードアウト ➔ Object1表示 ➔ 1.5秒後フェードイン
        // =========================================================================
        yield return StartCoroutine(CoolFadeOut(coolFadeDuration));

        if (startScreenRoot != null) startScreenRoot.SetActive(false);
        if (object1 != null) object1.SetActive(true);
        if (kiraanUI != null) kiraanUI.gameObject.SetActive(false);

        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeIn(coolFadeDuration));

        // フェードインし終わってから1秒後に別のUIのZ軸角度を現在角度から0へ徐々に戻す
        yield return new WaitForSeconds(1.0f);

        if (zAngleToZeroUI != null)
        {
            Quaternion startRot = zAngleToZeroUI.localRotation;
            Vector3 targetEuler = zAngleToZeroUI.localEulerAngles;
            targetEuler.z = 0f;
            Quaternion endRot = Quaternion.Euler(targetEuler);

            float elapsed = 0f;
            while (elapsed < zAngleRotateDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / zAngleRotateDuration));
                zAngleToZeroUI.localRotation = Quaternion.Slerp(startRot, endRot, t);
                yield return null;
            }
            zAngleToZeroUI.localRotation = endRot;
        }

        // 角度が0になったらBGMを再生開始（Object1が非表示になっても流れ続けるよう独立再生）
        PlayPersistentBGM();

        // 角度が0になったら別のUIオブジェクトを「キラーン」というエフェクトで表示し、少ししてからフェードアウトで非表示
        if (kiraanUI != null)
        {
            yield return StartCoroutine(PlayKiraanEffectAndFadeOut(kiraanUI, kiraanHoldDuration, kiraanFadeOutDuration));
        }

        // キラーンUIが非表示になってから1.5秒後にかっこいい感じでフェードアウト
        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeOut(coolFadeDuration));

        // =========================================================================
        // 【2】Object1非表示 ＆ Object2表示（BGMは継続）➔ 1.5秒後フェードイン ➔ 3秒後BGM減衰＆フェードアウト
        // =========================================================================
        if (object1 != null) object1.SetActive(false);
        if (object2 != null) object2.SetActive(true);

        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeIn(coolFadeDuration));

        // フェードイン完了から3秒待機
        yield return new WaitForSeconds(3.0f);

        // フェードアウトと同時にBGMの音量を徐々に下げて停止させる
        yield return StartCoroutine(CoolFadeOutWithBGMFade(coolFadeDuration));

        // =========================================================================
        // 【3】Object2非表示 ＆ Object3表示（UFO登場・CD竜巻・右から灰色化）
        // =========================================================================
        if (object2 != null) object2.SetActive(false);
        if (object4 != null) object4.SetActive(false); // 最初は非表示の状態から始まる
        if (object3 != null) object3.SetActive(true);

        if (ufoObject != null)
        {
            ufoInitialLocalPos = ufoObject.localPosition;
            ufoInitialLocalRot = ufoObject.localRotation;
            isUfoIdleWobbling = true; // 最初はぐらぐら少しだけ動いている状態
        }

        // フェードアウトしてから1.5秒後にかっこいい感じでフェードイン
        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeIn(coolFadeDuration));

        // フェードインし終わってから1秒後にUFOがあらゆる場所に3秒間動き回る（Y軸は少しだけ、主にX軸とZ軸が変動）
        yield return new WaitForSeconds(1.0f);
        isUfoIdleWobbling = false;

        if (ufoObject != null)
        {
            yield return StartCoroutine(MoveUfoAroundRoutine(3.0f));
        }
        else
        {
            yield return new WaitForSeconds(3.0f);
        }

        // 3秒たったらUFOが元の位置に戻って、最初非表示だったオブジェクト（Object4）が表示される
        isUfoIdleWobbling = true;
        if (object4 != null)
        {
            object4.SetActive(true);
        }

        // UFOとObject4がCDよりも手前のレイヤーになるよう設定し、CDの竜巻吸引と右からの灰色化を同時開始
        EnsureUfoAndObjectInFront();
        isTornadoSpawning = true;
        Coroutine tornadoCoroutine = StartCoroutine(SpawnTornadoCDsRoutine());

        // 右から徐々に灰色になっていく演出
        yield return StartCoroutine(AnimateRightToLeftGrayscaleRoutine(grayscaleAndTornadoDuration));

        // 灰色になりきったらCDの竜巻生成を終了し、1秒後にかっこいい感じにフェードアウト
        isTornadoSpawning = false;
        yield return new WaitForSeconds(1.0f);
        yield return StartCoroutine(CoolFadeOut(coolFadeDuration));

        isUfoIdleWobbling = false;
        if (tornadoCoroutine != null) StopCoroutine(tornadoCoroutine);

        // =========================================================================
        // 【4】Object3(＆4)非表示 ＆ Object5表示 ➔ 1.5秒後フェードイン ➔ 1秒後テキスト1文字ずつ表示
        // =========================================================================
        if (object3 != null) object3.SetActive(false);
        if (object4 != null) object4.SetActive(false);
        if (object5 != null) object5.SetActive(true);

        PrepareTextForTypewriter(textObject5, overrideString5, out string fullText5);

        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeIn(coolFadeDuration));

        yield return new WaitForSeconds(1.0f);
        yield return StartCoroutine(TypewriterTextRoutine(textObject5, fullText5));

        // テキスト表示完了から1.5秒後にかっこいい感じでフェードアウト
        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeOut(coolFadeDuration));

        // =========================================================================
        // 【5】Object5非表示 ＆ Object6表示 ➔ 1.5秒後フェードイン ➔ 1秒後テキスト ➔ 1秒後フェードアウト
        // =========================================================================
        if (object5 != null) object5.SetActive(false);
        if (object6 != null) object6.SetActive(true);

        PrepareTextForTypewriter(textObject6, overrideString6, out string fullText6);

        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeIn(coolFadeDuration));

        yield return new WaitForSeconds(1.0f);
        yield return StartCoroutine(TypewriterTextRoutine(textObject6, fullText6));

        yield return new WaitForSeconds(1.0f);
        yield return StartCoroutine(CoolFadeOut(coolFadeDuration));

        // =========================================================================
        // 【6】Object6非表示 ＆ Object7表示 ➔ 1.5秒後フェードイン ➔ ふわっと移動 ➔ 0.5秒後ズーム＆フェードアウト
        // =========================================================================
        if (object6 != null) object6.SetActive(false);
        if (object7 != null) object7.SetActive(true);

        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeIn(coolFadeDuration));

        // とあるオブジェクトを特定の位置や角度にふわっと移動
        if (floatMoveObject7 != null)
        {
            Vector3 startPos = floatMoveObject7.position;
            Quaternion startRot = floatMoveObject7.rotation;

            Vector3 endPos = floatMoveTargetTransform7 != null ? floatMoveTargetTransform7.position : floatMoveTargetPosition7;
            Quaternion endRot = floatMoveTargetTransform7 != null ? floatMoveTargetTransform7.rotation : Quaternion.Euler(floatMoveTargetEulerAngles7);

            float elapsed = 0f;
            while (elapsed < floatMoveDuration7)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / floatMoveDuration7));
                floatMoveObject7.position = Vector3.Lerp(startPos, endPos, t);
                floatMoveObject7.rotation = Quaternion.Slerp(startRot, endRot, t);
                yield return null;
            }
            floatMoveObject7.position = endPos;
            floatMoveObject7.rotation = endRot;
        }

        // 移動し終わってから0.5秒後に特定のPositionにズームしながらかっこいい感じにフェードアウト
        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(ZoomAndCoolFadeOutRoutine(coolFadeDuration));

        // =========================================================================
        // 【7】Object7非表示 ＆ Object8表示（ここから通常の色に戻す）➔ 1.5秒後フェードイン ➔ 1秒後テキスト＆360度回転
        // =========================================================================
        SetGrayscaleActive(false); // Object8からは通常通りの色で表示

        if (object7 != null) object7.SetActive(false);
        if (object8 != null) object8.SetActive(true);

        PrepareTextForTypewriter(textObject8, overrideString8, out string fullText8);

        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeIn(coolFadeDuration));

        yield return new WaitForSeconds(1.0f);
        isSlowRotating8 = true; // とあるオブジェクトがゆっくり360度回転
        yield return StartCoroutine(TypewriterTextRoutine(textObject8, fullText8));

        // テキストが表示され終わってから1秒後にかっこいい感じでフェードアウト
        yield return new WaitForSeconds(1.0f);
        yield return StartCoroutine(CoolFadeOut(coolFadeDuration));
        isSlowRotating8 = false;

        // =========================================================================
        // 【8】Object8非表示 ＆ Object9表示 ➔ 1.5秒後フェードイン ➔ 1秒後テキスト ➔ 1秒後フェードアウト
        // =========================================================================
        if (object8 != null) object8.SetActive(false);
        if (object9 != null) object9.SetActive(true);

        PrepareTextForTypewriter(textObject9, overrideString9, out string fullText9);

        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeIn(coolFadeDuration));

        yield return new WaitForSeconds(1.0f);
        yield return StartCoroutine(TypewriterTextRoutine(textObject9, fullText9));

        yield return new WaitForSeconds(1.0f);
        yield return StartCoroutine(CoolFadeOut(coolFadeDuration));

        // =========================================================================
        // 【9】Object9非表示 ＆ Object10表示 ➔ 1.5秒後フェードイン ➔ 1秒後UI叩きつけ ➔ 回転UI＆テキスト
        // =========================================================================
        if (object9 != null) object9.SetActive(false);
        if (object10 != null) object10.SetActive(true);

        if (slamUIObject10 != null) slamUIObject10.gameObject.SetActive(false);
        if (slowRotateUI10 != null) slowRotateUI10.gameObject.SetActive(false);
        PrepareTextForTypewriter(textObject10, overrideString10, out string fullText10);

        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeIn(coolFadeDuration));

        // フェードイン完了から1秒後にUIオブジェクトを叩きつけるように表示
        yield return new WaitForSeconds(1.0f);
        if (slamUIObject10 != null)
        {
            yield return StartCoroutine(SlamUIEffectRoutine(slamUIObject10));
        }

        // 叩きつけ演出が終わったら別のUIが表示されてゆっくり360度回転開始 ＆ テキストを1文字ずつ表示
        if (slowRotateUI10 != null)
        {
            slowRotateUI10.gameObject.SetActive(true);
            isSlowRotating10 = true;
        }

        yield return StartCoroutine(TypewriterTextRoutine(textObject10, fullText10));

        // テキストが表示され終わってから1秒後にかっこいい感じでフェードアウト
        yield return new WaitForSeconds(1.0f);
        yield return StartCoroutine(CoolFadeOut(coolFadeDuration));
        isSlowRotating10 = false;

        // =========================================================================
        // 【10】Object10を非表示 ➔ 1.5秒後にフェードイン ➔ ムービー終了・通常ゲーム開始
        // =========================================================================
        if (object10 != null) object10.SetActive(false);
        SetGameplayObjectsActive(true);

        yield return new WaitForSeconds(1.5f);
        yield return StartCoroutine(CoolFadeIn(coolFadeDuration));

        isMovieRunning = false;
        if (movieOverlayCanvas != null)
        {
            movieOverlayCanvas.gameObject.SetActive(false);
        }
    }

    // =========================================================================
    // 個別演出・補助コルーチン群
    // =========================================================================

    /// <summary>
    /// シネマティックな黒帯＋滑らかな暗転による「かっこいいフェードアウト」
    /// </summary>
    private IEnumerator CoolFadeOut(float duration)
    {
        if (fadeCanvasGroup == null) yield break;

        fadeCanvasGroup.blocksRaycasts = true;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = t * t * (3f - 2f * t);

            fadeCanvasGroup.alpha = smooth;
            UpdateLetterboxHeight(smooth * 120f);
            yield return null;
        }
        fadeCanvasGroup.alpha = 1f;
        UpdateLetterboxHeight(120f);
    }

    /// <summary>
    /// シネマティックな黒帯解除＋滑らかな明転による「かっこいいフェードイン」
    /// </summary>
    private IEnumerator CoolFadeIn(float duration)
    {
        if (fadeCanvasGroup == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = t * t * (3f - 2f * t);

            fadeCanvasGroup.alpha = 1f - smooth;
            UpdateLetterboxHeight((1f - smooth) * 120f);
            yield return null;
        }
        fadeCanvasGroup.alpha = 0f;
        UpdateLetterboxHeight(0f);
        fadeCanvasGroup.blocksRaycasts = false;
    }

    /// <summary>
    /// BGMの音量を徐々に0に下げながら同時にかっこいいフェードアウトを行う
    /// </summary>
    private IEnumerator CoolFadeOutWithBGMFade(float duration)
    {
        float startVolume = bgmAudioSource != null ? bgmAudioSource.volume : 1f;
        if (fadeCanvasGroup != null) fadeCanvasGroup.blocksRaycasts = true;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = t * t * (3f - 2f * t);

            if (fadeCanvasGroup != null)
            {
                fadeCanvasGroup.alpha = smooth;
                UpdateLetterboxHeight(smooth * 120f);
            }

            if (bgmAudioSource != null)
            {
                bgmAudioSource.volume = Mathf.Lerp(startVolume, 0f, t);
            }

            yield return null;
        }

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 1f;
            UpdateLetterboxHeight(120f);
        }

        if (bgmAudioSource != null)
        {
            bgmAudioSource.Stop();
            bgmAudioSource.volume = startVolume;
        }
    }

    /// <summary>
    /// 「キラーン」というエフェクトでUIを表示し、少し待機してからフェードアウトで非表示にする
    /// </summary>
    private IEnumerator PlayKiraanEffectAndFadeOut(RectTransform targetUI, float holdTime, float fadeOutTime)
    {
        targetUI.gameObject.SetActive(true);
        CanvasGroup cg = targetUI.GetComponent<CanvasGroup>();
        if (cg == null) cg = targetUI.gameObject.AddComponent<CanvasGroup>();

        Vector3 originalScale = targetUI.localScale;
        if (originalScale == Vector3.zero) originalScale = Vector3.one;

        cg.alpha = 0f;
        targetUI.localScale = Vector3.zero;

        // キラーン登場（スケールが弾けて回転しながら輝く）
        float appearDuration = 0.45f;
        float elapsed = 0f;
        while (elapsed < appearDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / appearDuration);
            // オーバーシュートイージング（1.25倍まで膨らんで1倍に収束）
            float scaleCurve = Mathf.Sin(t * Mathf.PI * 0.8f) * 1.2f;
            if (t > 0.8f) scaleCurve = Mathf.Lerp(1.2f, 1.0f, (t - 0.8f) / 0.2f);

            targetUI.localScale = originalScale * scaleCurve;
            cg.alpha = Mathf.Clamp01(t * 2.5f);
            yield return null;
        }
        targetUI.localScale = originalScale;
        cg.alpha = 1f;

        // 少しの間表示を維持
        yield return new WaitForSeconds(holdTime);

        // フェードアウトして非表示
        elapsed = 0f;
        while (elapsed < fadeOutTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeOutTime);
            cg.alpha = 1f - t;
            targetUI.localScale = Vector3.Lerp(originalScale, originalScale * 1.15f, t);
            yield return null;
        }

        cg.alpha = 0f;
        targetUI.localScale = originalScale;
        targetUI.gameObject.SetActive(false);
    }

    /// <summary>
    /// UFOが3秒間あらゆる場所に動き回り（Y軸は少しだけ、主にX軸とZ軸が変動）、最後に元の位置へ戻る
    /// </summary>
    private IEnumerator MoveUfoAroundRoutine(float duration)
    {
        float moveTime = Mathf.Max(0.5f, duration - 0.45f);
        float returnTime = duration - moveTime;

        int waypointsCount = 5;
        float stepDuration = moveTime / waypointsCount;

        for (int i = 0; i < waypointsCount; i++)
        {
            Vector3 startPos = ufoObject.localPosition;
            Quaternion startRot = ufoObject.localRotation;

            // X軸とZ軸を大きく変動させ、Y軸はわずかに変動させる
            float randX = UnityEngine.Random.Range(-ufoMoveRange.x, ufoMoveRange.x);
            float randY = UnityEngine.Random.Range(-ufoMoveRange.y, ufoMoveRange.y);
            float randZ = UnityEngine.Random.Range(-ufoMoveRange.z, ufoMoveRange.z);

            Vector3 targetPos = ufoInitialLocalPos + new Vector3(randX, randY, randZ);
            Quaternion targetRot = ufoInitialLocalRot * Quaternion.Euler(
                UnityEngine.Random.Range(-8f, 8f),
                0f,
                Mathf.Clamp(-randX * 0.08f, -18f, 18f)
            );

            float elapsed = 0f;
            while (elapsed < stepDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / stepDuration));
                ufoObject.localPosition = Vector3.Lerp(startPos, targetPos, t);
                ufoObject.localRotation = Quaternion.Slerp(startRot, targetRot, t);
                yield return null;
            }
        }

        // 元の位置へスムーズに戻る
        Vector3 lastPos = ufoObject.localPosition;
        Quaternion lastRot = ufoObject.localRotation;
        float retElapsed = 0f;
        while (retElapsed < returnTime)
        {
            retElapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(retElapsed / returnTime));
            ufoObject.localPosition = Vector3.Lerp(lastPos, ufoInitialLocalPos, t);
            ufoObject.localRotation = Quaternion.Slerp(lastRot, ufoInitialLocalRot, t);
            yield return null;
        }

        ufoObject.localPosition = ufoInitialLocalPos;
        ufoObject.localRotation = ufoInitialLocalRot;
    }

    /// <summary>
    /// 2つのPositionから複数種類のCDのUIオブジェクトを竜巻状にぐるぐる上空の目標Positionへ吸い上げ、到達したら削除する
    /// </summary>
    private IEnumerator SpawnTornadoCDsRoutine()
    {
        if (cdUIPrefabs == null || cdUIPrefabs.Count == 0 || cdAbsorbTargetPos == null)
        {
            yield break;
        }

        // CDを生成する専用コンテナを作成し、UFOやObject4よりも必ず背面のレイヤー（奥側）に配置する
        Transform parentTransform = ufoObject != null && ufoObject.parent != null ? ufoObject.parent : transform;
        GameObject cdContainer = new GameObject("CD_Tornado_Container", typeof(RectTransform));
        cdContainer.transform.SetParent(parentTransform, false);
        cdContainer.transform.SetAsFirstSibling(); // 一番背面へ配置

        EnsureUfoAndObjectInFront();

        while (isTornadoSpawning)
        {
            if (cdSpawnPos1 != null) SpawnSingleTornadoCD(cdSpawnPos1.position, cdContainer.transform);
            if (cdSpawnPos2 != null) SpawnSingleTornadoCD(cdSpawnPos2.position, cdContainer.transform);

            yield return new WaitForSeconds(0.08f);
        }
    }

    private void SpawnSingleTornadoCD(Vector3 startWorldPos, Transform container)
    {
        RectTransform prefab = cdUIPrefabs[UnityEngine.Random.Range(0, cdUIPrefabs.Count)];
        if (prefab == null) return;

        RectTransform cdInstance = Instantiate(prefab, container);
        cdInstance.gameObject.SetActive(true);
        cdInstance.position = startWorldPos;

        StartCoroutine(AnimateSingleCDTornado(cdInstance, startWorldPos));
    }

    private IEnumerator AnimateSingleCDTornado(RectTransform cdRect, Vector3 startPos)
    {
        float duration = UnityEngine.Random.Range(1.1f, 1.7f);
        float elapsed = 0f;
        float spiralFrequency = UnityEngine.Random.Range(8f, 14f);
        float maxSpiralRadius = UnityEngine.Random.Range(35f, 85f);
        float phaseOffset = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float selfSpinSpeed = UnityEngine.Random.Range(180f, 540f);

        Vector3 initialScale = cdRect.localScale;

        while (elapsed < duration && cdRect != null && cdAbsorbTargetPos != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            Vector3 targetPos = cdAbsorbTargetPos.position;
            Vector3 basePos = Vector3.Lerp(startPos, targetPos, t);

            // 竜巻の螺旋横揺れ（目標地点に近づくにつれて収束して吸い込まれる）
            float radiusEnvelope = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.7f);
            float swirlX = Mathf.Cos(t * spiralFrequency + phaseOffset) * maxSpiralRadius * radiusEnvelope;
            float swirlY = Mathf.Sin(t * spiralFrequency + phaseOffset) * (maxSpiralRadius * 0.25f) * radiusEnvelope;

            cdRect.position = basePos + new Vector3(swirlX, swirlY, 0f);
            cdRect.Rotate(0f, 0f, selfSpinSpeed * Time.deltaTime);

            // 目標Position直前で縮小しながら吸い込まれる
            if (t > 0.75f)
            {
                float shrink = 1f - ((t - 0.75f) / 0.25f);
                cdRect.localScale = initialScale * Mathf.Max(0.05f, shrink);
            }

            yield return null;
        }

        // 目標Positionまで上がりきったら削除
        if (cdRect != null)
        {
            Destroy(cdRect.gameObject);
        }
    }

    /// <summary>
    /// UFOとObject4をCDよりも手前のレイヤー（描画順）に保つ
    /// </summary>
    private void EnsureUfoAndObjectInFront()
    {
        if (ufoObject != null) ufoObject.SetAsLastSibling();
        if (object4 != null) object4.transform.SetAsLastSibling();
    }

    /// <summary>
    /// 画面の右から斜めの白い境界線を伴って徐々に灰色（グレースケール）にしていく
    /// </summary>
    private IEnumerator AnimateRightToLeftGrayscaleRoutine(float duration)
    {
        SetGrayscaleActive(true);
        EnsureNeverGrayscaleObjectsInFront();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            currentGrayscaleProgress = Mathf.Clamp01(elapsed / duration);
            if (grayscaleWipeMaterial != null)
            {
                grayscaleWipeMaterial.SetFloat("_Progress", currentGrayscaleProgress);
            }
            yield return null;
        }

        currentGrayscaleProgress = 1f;
        if (grayscaleWipeMaterial != null)
        {
            grayscaleWipeMaterial.SetFloat("_Progress", 1f);
        }
    }

    /// <summary>
    /// Object7の最後で特定のPositionへズームしながらかっこいい感じにフェードアウトする
    /// </summary>
    private IEnumerator ZoomAndCoolFadeOutRoutine(float duration)
    {
        Transform targetRoot = zoomTargetRoot7 != null ? zoomTargetRoot7 : (object7 != null ? object7.transform : null);
        Vector3 origScale = targetRoot != null ? targetRoot.localScale : Vector3.one;
        Vector3 origPos = targetRoot != null ? targetRoot.position : Vector3.zero;

        Vector3 focusWorldPos = zoomFocusPoint7 != null ? zoomFocusPoint7.position : origPos;

        if (fadeCanvasGroup != null) fadeCanvasGroup.blocksRaycasts = true;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = t * t * (3f - 2f * t);

            if (targetRoot != null)
            {
                float currentZoom = Mathf.Lerp(1f, zoomScaleMultiplier7, smooth);
                targetRoot.localScale = origScale * currentZoom;
                targetRoot.position = Vector3.Lerp(origPos, origPos + (origPos - focusWorldPos) * (currentZoom - 1f), smooth);
            }

            if (fadeCanvasGroup != null)
            {
                fadeCanvasGroup.alpha = smooth;
                UpdateLetterboxHeight(smooth * 120f);
            }

            yield return null;
        }

        // フェードアウト完了後にスケールと位置を元に戻しておく
        if (targetRoot != null)
        {
            targetRoot.localScale = origScale;
            targetRoot.position = origPos;
        }

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 1f;
            UpdateLetterboxHeight(120f);
        }
    }

    /// <summary>
    /// Object10でUIオブジェクトを指定位置へ叩きつけるように表示する
    /// </summary>
    private IEnumerator SlamUIEffectRoutine(RectTransform slamUI)
    {
        slamUI.gameObject.SetActive(true);

        if (slamTargetPosTransform10 != null)
        {
            slamUI.position = slamTargetPosTransform10.position;
        }
        else if (slamTargetAnchoredPos10 != Vector2.zero)
        {
            slamUI.anchoredPosition = slamTargetAnchoredPos10;
        }

        Vector3 targetScale = slamUI.localScale;
        if (targetScale == Vector3.zero) targetScale = Vector3.one;

        CanvasGroup cg = slamUI.GetComponent<CanvasGroup>();
        if (cg == null) cg = slamUI.gameObject.AddComponent<CanvasGroup>();

        // 上空（巨大スケール）から一気に指定位置へ叩きつける
        float slamTime = 0.22f;
        float elapsed = 0f;
        while (elapsed < slamTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / slamTime);
            float curve = t * t;
            slamUI.localScale = Vector3.Lerp(targetScale * 3.8f, targetScale * 0.9f, curve);
            cg.alpha = Mathf.Clamp01(t * 2f);
            yield return null;
        }

        // 叩きつけた瞬間のバウンスと画面シェイク感
        Vector3 basePos = slamUI.localPosition;
        float shakeTime = 0.25f;
        elapsed = 0f;
        while (elapsed < shakeTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / shakeTime);
            float damper = 1f - t;
            slamUI.localScale = Vector3.Lerp(targetScale * 0.9f, targetScale, t);
            slamUI.localPosition = basePos + (Vector3)UnityEngine.Random.insideUnitCircle * (18f * damper);
            yield return null;
        }

        slamUI.localScale = targetScale;
        slamUI.localPosition = basePos;
        cg.alpha = 1f;
    }

    /// <summary>
    /// テキストの1文字ずつ表示準備
    /// </summary>
    private void PrepareTextForTypewriter(GameObject textObj, string overrideStr, out string fullText)
    {
        fullText = "";
        if (textObj == null) return;

        TMP_Text tmp = textObj.GetComponent<TMP_Text>();
        if (tmp == null) tmp = textObj.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
        {
            fullText = !string.IsNullOrEmpty(overrideStr) ? overrideStr : tmp.text;
            tmp.text = "";
            return;
        }

        Text legacyText = textObj.GetComponent<Text>();
        if (legacyText == null) legacyText = textObj.GetComponentInChildren<Text>(true);
        if (legacyText != null)
        {
            fullText = !string.IsNullOrEmpty(overrideStr) ? overrideStr : legacyText.text;
            legacyText.text = "";
        }
    }

    /// <summary>
    /// テキストを順々に1文字ずつ表示するコルーチン
    /// </summary>
    private IEnumerator TypewriterTextRoutine(GameObject textObj, string fullText)
    {
        if (textObj == null || string.IsNullOrEmpty(fullText)) yield break;

        textObj.SetActive(true);
        TMP_Text tmp = textObj.GetComponent<TMP_Text>();
        if (tmp == null) tmp = textObj.GetComponentInChildren<TMP_Text>(true);

        Text legacyText = textObj.GetComponent<Text>();
        if (legacyText == null) legacyText = textObj.GetComponentInChildren<Text>(true);

        for (int i = 1; i <= fullText.Length; i++)
        {
            string currentSub = fullText.Substring(0, i);
            if (tmp != null) tmp.text = currentSub;
            if (legacyText != null) legacyText.text = currentSub;

            yield return new WaitForSeconds(charTypeInterval);
        }
    }

    /// <summary>
    /// Object1が非表示になっても途切れず流れ続けるようにBGMを再生する
    /// </summary>
    private void PlayPersistentBGM()
    {
        if (bgmAudioSource == null)
        {
            bgmAudioSource = gameObject.GetComponent<AudioSource>();
            if (bgmAudioSource == null)
            {
                bgmAudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        // BGMのAudioSourceがObject1の子になっている場合、Object1非表示で止まらないよう親をこのマネージャーに移す
        if (object1 != null && bgmAudioSource.transform.IsChildOf(object1.transform))
        {
            bgmAudioSource.transform.SetParent(transform);
        }

        if (bgmClip != null)
        {
            bgmAudioSource.clip = bgmClip;
        }

        bgmAudioSource.loop = true;
        bgmAudioSource.Play();
    }

    // =========================================================================
    // 初期化・オーバーレイ・グレースケール構築
    // =========================================================================

    private void InitializeMovieObjectsState()
    {
        SetAllMovieObjectsActive(false);
    }

    private void SetAllMovieObjectsActive(bool state)
    {
        if (object1 != null) object1.SetActive(state);
        if (object2 != null) object2.SetActive(state);
        if (object3 != null) object3.SetActive(state);
        if (object4 != null) object4.SetActive(state);
        if (object5 != null) object5.SetActive(state);
        if (object6 != null) object6.SetActive(state);
        if (object7 != null) object7.SetActive(state);
        if (object8 != null) object8.SetActive(state);
        if (object9 != null) object9.SetActive(state);
        if (object10 != null) object10.SetActive(state);
    }

    private void SetGameplayObjectsActive(bool state)
    {
        if (gameplayObjectsToEnableAfterMovie == null) return;
        for (int i = 0; i < gameplayObjectsToEnableAfterMovie.Count; i++)
        {
            if (gameplayObjectsToEnableAfterMovie[i] != null)
            {
                gameplayObjectsToEnableAfterMovie[i].SetActive(state);
            }
        }
    }

    /// <summary>
    /// 絶対に灰色にならないUIオブジェクト群をグレースケールエフェクトよりも前面のSortingOrderに配置する
    /// </summary>
    private void EnsureNeverGrayscaleObjectsInFront()
    {
        if (neverGrayscaleUIObjects == null) return;

        for (int i = 0; i < neverGrayscaleUIObjects.Count; i++)
        {
            GameObject uiObj = neverGrayscaleUIObjects[i];
            if (uiObj == null) continue;

            Canvas c = uiObj.GetComponent<Canvas>();
            if (c == null) c = uiObj.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = 29500; // グレースケール(29000)より手前、暗転フェード(30000)より奥

            if (uiObj.GetComponent<GraphicRaycaster>() == null)
            {
                uiObj.AddComponent<GraphicRaycaster>();
            }
        }
    }

    private void SetGrayscaleActive(bool active)
    {
        isGrayscaleActive = active;
        if (grayscaleOverlayRoot != null)
        {
            grayscaleOverlayRoot.SetActive(active);
        }
        if (!active && grayscaleWipeMaterial != null)
        {
            grayscaleWipeMaterial.SetFloat("_Progress", 0f);
        }
    }

    private void UpdateLetterboxHeight(float height)
    {
        if (topLetterbox != null) topLetterbox.sizeDelta = new Vector2(0f, height);
        if (bottomLetterbox != null) bottomLetterbox.sizeDelta = new Vector2(0f, height);
    }

    /// <summary>
    /// シネマティックフェードおよび右からの斜めグレースケールワイプ用のUIを自動構築する
    /// </summary>
    private void CreateMovieOverlayUI()
    {
        // 1. グレースケールワイプ用Canvas（SortingOrder: 29000）
        grayscaleOverlayRoot = new GameObject("MovieGrayscaleOverlayCanvas", typeof(Canvas), typeof(CanvasScaler));
        grayscaleOverlayRoot.transform.SetParent(transform, false);
        Canvas grayCanvas = grayscaleOverlayRoot.GetComponent<Canvas>();
        grayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        grayCanvas.sortingOrder = 29000;

        GameObject grayPanel = new GameObject("RightToLeftGrayscaleWipe", typeof(RectTransform), typeof(RawImage));
        grayPanel.transform.SetParent(grayscaleOverlayRoot.transform, false);
        RectTransform grayRect = grayPanel.GetComponent<RectTransform>();
        grayRect.anchorMin = Vector2.zero;
        grayRect.anchorMax = Vector2.one;
        grayRect.sizeDelta = Vector2.zero;

        RawImage grayRawImg = grayPanel.GetComponent<RawImage>();
        grayRawImg.raycastTarget = false;

        // 右から斜めの白い境界線付きでグレースケール化するシェーダー・テクスチャを設定
        Shader wipeShader = Shader.Find("UI/Default");
        grayscaleWipeMaterial = new Material(wipeShader);
        grayRawImg.material = grayscaleWipeMaterial;
        StartCoroutine(UpdateGrayscaleWipeVisualRoutine(grayRawImg));
        grayscaleOverlayRoot.SetActive(false);

        // 2. かっこいいフェード用Canvas（SortingOrder: 30000）
        GameObject canvasObj = new GameObject("MovieCoolFadeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObj.transform.SetParent(transform, false);
        movieOverlayCanvas = canvasObj.GetComponent<Canvas>();
        movieOverlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        movieOverlayCanvas.sortingOrder = 30000;

        // 上下シネマティック黒帯（レターボックス）
        topLetterbox = CreateBar(canvasObj.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        bottomLetterbox = CreateBar(canvasObj.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));

        // 全面フェードパネル
        GameObject fadePanel = new GameObject("FadePanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        fadePanel.transform.SetParent(canvasObj.transform, false);
        RectTransform fadeRect = fadePanel.GetComponent<RectTransform>();
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.sizeDelta = Vector2.zero;

        Image fadeImg = fadePanel.GetComponent<Image>();
        fadeImg.color = Color.black;

        fadeCanvasGroup = fadePanel.GetComponent<CanvasGroup>();
        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.blocksRaycasts = false;
    }

    private RectTransform CreateBar(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        GameObject bar = new GameObject("CinematicBar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(parent, false);
        RectTransform rt = bar.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = Vector2.zero;
        Image img = bar.GetComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        return rt;
    }

    /// <summary>
    /// 添付画像のように右から斜めの白い境界線を伴って画面が灰色になっていくテクスチャ演出を更新する
    /// </summary>
    private IEnumerator UpdateGrayscaleWipeVisualRoutine(RawImage targetRawImage)
    {
        int width = 256;
        int height = 144;
        Texture2D wipeTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        wipeTex.wrapMode = TextureWrapMode.Clamp;
        targetRawImage.texture = wipeTex;

        Color[] pixels = new Color[width * height];

        while (true)
        {
            if (isGrayscaleActive)
            {
                // 斜めの傾き（画像と同じく上が少し右、下が少し左に傾くライン）
                float slant = 0.14f;
                float edgeX = Mathf.Lerp(1.1f, -0.15f, currentGrayscaleProgress);

                for (int y = 0; y < height; y++)
                {
                    float ny = (float)y / height;
                    float linePos = edgeX + (ny - 0.5f) * slant;

                    for (int x = 0; x < width; x++)
                    {
                        float nx = (float)x / width;
                        float distFromLine = nx - linePos;

                        if (Mathf.Abs(distFromLine) < 0.012f)
                        {
                            // 斜めの白い光る境界ライン
                            float glow = 1f - (Mathf.Abs(distFromLine) / 0.012f);
                            pixels[y * width + x] = new Color(0.95f, 0.95f, 1f, glow * 0.9f);
                        }
                        else if (distFromLine > 0f)
                        {
                            // 境界線より右側のグレースケール（灰色）領域
                            pixels[y * width + x] = new Color(0.22f, 0.22f, 0.24f, 0.82f);
                        }
                        else
                        {
                            // 境界線より左側の通常色領域（透明）
                            pixels[y * width + x] = Color.clear;
                        }
                    }
                }

                wipeTex.SetPixels(pixels);
                wipeTex.Apply();
            }
            yield return null;
        }
    }
}