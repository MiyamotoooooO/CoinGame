using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// レコードの「大きな演出」の監督。RecordEffectSystem の GameObject に自動で付く（シーンに置かなくてよい）。
    ///
    /// ・レコードを発動すると、そのレコードの演出（RecordSpectacle を受けついだクラス。キーで探す）を始める
    ///   イントロ：主役のモデルが大きく登場 → カメラが寄る → 左下にタイトルカード（ジャケット・名前） → 右の定位置へ
    ///   発動中：定位置で動き、決まった間隔で「見せ場」。メダルが穴に落ちると反応する
    ///   曲が終わる：フィナーレ（まん中で決めて、上へ去る）
    /// ・モデルは専用のカメラで描き、ゲームの画面とモニターの画面の上に重ねる（盤面やモニターに隠れない。ゲームの画面と同じ粗さのドットになる）
    /// ・カメラの動きは、そのフレームの間だけずらして、次のフレームの最初に元に戻す（ゲームの処理には影響しない）
    /// ・効果のルール（ポイントなど）には一切さわらない。見た目と音だけ
    ///
    /// Inspector で、オン・オフ、カメラの強さ、モデルの置き場所・大きさを変えられる。
    ///
    /// 盤面に置く（Board Mode。2026-11-16 の変更）：
    ///   主役のモデルを、盤面の下の段の奥（左右のどちらか。レコードごとに Board Spots で変えられる）に立てる。
    ///   ゲームの画面と同じカメラで描くので、メダルの奥・手前もそのまま見え、メダルが当たる（じゃまになる）。
    ///   画面のまん中に大きく出る演出（登場・見せ場・フィナーレ）、カメラの動き、大きな文字、暗転、集中線、
    ///   画面のフラッシュ、紙吹雪、スローは出さない。代わりに、盤面の上で
    ///   　発動：モデルがぽんと生えてくる（足もとにキラキラと煙）
    ///   　見せ場：モデルがぷるっとはずんで、まわりがキラキラ
    ///   　終わり：モデルが小さくなって消える
    ///   の、うるさすぎない演出にする。効果音も少し小さくする。左下のタイトルカードは出す
    ///
    /// デバッグ用（エディタと Development Build の時だけ）：
    ///   F4 … 次のレコードの演出だけを見る（効果は発動しない。12秒でフィナーレ）。Shift+F4 で前のレコード
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public class RecordSpectacleDirector : MonoBehaviour
    {
        public static RecordSpectacleDirector Instance { get; private set; }

        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        public RecordEffectSystem effects;
        public RecordArtwork artwork;

        [Header("演出")]
        [Tooltip("オフにすると、大きな演出を出さない")]
        public bool spectacleEnabled = true;
        [Tooltip("発動の瞬間などに、カメラを動かす")]
        public bool moveCamera = true;
        [Tooltip("カメラの動きの強さ")]
        [Range(0f, 2f)] public float cameraStrength = 1f;
        [Tooltip("見せ場で一瞬スローにする")]
        public bool slowMotion = true;
        [Tooltip("発動の時に、左下にタイトルカード（ジャケット・名前）を出す")]
        public bool titleCard = true;
        [Tooltip("演出の効果音の大きさ")]
        [Range(0f, 1f)] public float volume = 0.7f;

        [Header("モデルの置き場所（画面の割合。左下が (0,0)、右上が (1,1)）")]
        [Tooltip("発動中に主役のモデルがいる場所")]
        public Vector2 residentViewport = new Vector2(0.76f, 0.56f);
        [Tooltip("定位置のモデルの大きさ（画面の高さに対する割合。モデルのいちばん長い辺がこの大きさになる）")]
        [Range(0.1f, 0.8f)] public float residentHeight = 0.24f;
        [Tooltip("登場・見せ場・フィナーレで、まん中に出る時の場所")]
        public Vector2 focusViewport = new Vector2(0.5f, 0.56f);
        [Tooltip("まん中に出る時の大きさ（画面の高さに対する割合）")]
        [Range(0.2f, 1f)] public float focusHeight = 0.72f;
        [Tooltip("見せ場で出てくる場所（盤面をふさがないよう、まん中の上）")]
        public Vector2 beatViewport = new Vector2(0.5f, 0.66f);
        [Tooltip("見せ場の時の大きさ（画面の高さに対する割合）")]
        [Range(0.1f, 1f)] public float beatHeight = 0.5f;

        [Header("盤面に置く（主役のモデルを、盤面の上に立てる）")]
        [Tooltip("オン：主役のモデルを盤面の下の段の奥に立てて、メダルが当たる。画面のまん中に大きく出る演出・カメラの動き・大きな文字などは出さない")]
        public bool boardMode = true;
        [Tooltip("盤面に置くモデルの高さ（メダルの直径を 1 とした数。いちばん長い辺がこの大きさ）")]
        [Range(1f, 8f)] public float boardHeight = 3.2f;
        [Tooltip("盤面に置いたモデルに、メダルが当たる")]
        public bool boardColliders = true;
        [Tooltip("レコードごとの置き場所。無いレコードは、左右の奥のどちらか（キーで決まる）")]
        public List<BoardSpot> boardSpots = new List<BoardSpot>();
        [Tooltip("この演出（主役のモデル・タイトルカード）を出さないレコードのキー。レコードのスクリプトが自分で主役と盤面の演出を出すレコード")]
        public List<string> skipKeys = new List<string> { "Techno", "Sea", "Shooting", "Train" };

        /// <summary>盤面に置く場所（レコードごと）</summary>
        [Serializable]
        public class BoardSpot
        {
            [Tooltip("レコードのキー（英名。Pops など）")]
            public string key = "";
            [Tooltip("左右（-1 左のはし、0 まん中、1 右のはし）")]
            [Range(-1f, 1f)] public float x = 0.72f;
            [Tooltip("前後（0 手前のふち、1 プッシャーがいちばん出た時の前）")]
            [Range(0f, 1f)] public float depth = 0.78f;
            [Tooltip("高さ（メダルの直径を 1 とした数。0 なら Board Height）")]
            [Range(0f, 8f)] public float height = 0f;
            [Tooltip("向き（度。0 でカメラの方を向く）")]
            [Range(-180f, 180f)] public float yaw = 0f;
        }

        [Header("画面の文字")]
        [Tooltip("空ならモニターと同じ本文のフォント")]
        public Font bodyFont;
        [Tooltip("空ならモニターと同じ数字のフォント")]
        public Font titleFont;
        [Tooltip("この演出の Canvas の Sort Order（BoostPresentation の 100 より小さくすると、あちらのカットインが上に出る）")]
        public int sortingOrder = 90;

        [Header("描き方")]
        [Tooltip("モデルを描くレイヤー。-1 なら空いているレイヤーを自動でえらぶ（モニターが使う 28・29 はさける）")]
        public int layer = -1;
        [Tooltip("モデルの絵を画面に重ねる Canvas の Sort Order の、ArcadeCamera の画面からの差（モニターの画面が +1 なので、+2 でモニターより上）")]
        public int screenOrderOffset = 3;
        [Tooltip("モデルと粒に、ゲームの画面と同じドットのフィルター（減色・ディザ・ふちどり・走査線）をかける")]
        public bool dotFilter = true;
        [Tooltip("モデルを描く細かさ（ゲームの画面のドットの何倍か）。ドットのフィルターがオンなら 1 がおすすめ")]
        [Range(1, 4)] public int modelResolution = 1;
        [Tooltip("モデルのふちのギザギザを減らす（アンチエイリアス）。ドットのフィルターがオンならオフがおすすめ")]
        public bool modelAntiAliasing = false;
        [Tooltip("発動の時、ゲームの画面を暗くしてモデルを目立たせる強さ")]
        [Range(0f, 1f)] public float introDim = 0.5f;
        [Tooltip("発動の時、画面のまん中から放射状の集中線を出す")]
        public bool speedLines = true;
        [Tooltip("モデルの部品どうしで影を落とす（立体感が出る）")]
        public bool modelShadows = true;

        // ================================================================
        // 外から読むもの
        // ================================================================

        public const float HalfH = 5f;
        const float StageDistance = 10f;

        public int Layer { get; private set; } = 31;
        public Transform Stage { get; private set; }
        public float HalfW { get; private set; } = 8.9f;
        /// <summary>Stage の 1 が、ワールドで何メートルか</summary>
        public float StageScale { get; private set; } = 1f;
        public Vector3 ResidentPos => ViewportToStage(residentViewport);
        public float ResidentSize => residentHeight * HalfH * 2f;
        public Vector3 FocusPos => ViewportToStage(focusViewport);
        public float FocusSize => focusHeight * HalfH * 2f;
        public Vector3 BeatPos => ViewportToStage(beatViewport);
        public float BeatSize => beatHeight * HalfH * 2f;

        /// <summary>盤面に置いたモデルを入れる入れ物（ワールド）</summary>
        public Transform BoardRoot
        {
            get
            {
                if (boardRoot == null) boardRoot = new GameObject("RecordSpectacleBoard").transform;
                return boardRoot;
            }
        }
        Transform boardRoot;

        /// <summary>
        /// 盤面に置く時の、モデルの足もと（ワールド）・高さ（ワールドの長さ）・向き（度）。
        /// 場所は盤面の下の段（手前のふちから、プッシャーがいちばん出た時の前面まで）の中。
        /// </summary>
        public bool TryGetBoardPose(string key, out Vector3 basePos, out float height, out float yaw)
        {
            basePos = Vector3.zero;
            height = 1f;
            yaw = 0f;
            if (game == null) return false;
            float d = game.tuning.medalDiameter;
            float half = game.BoardHalfWidth;
            float front = game.layout.floorFrontZ;
            float back = game.layout.pusherRetractZ - (game.Pusher != null ? game.Pusher.stroke : game.tuning.pusherStroke);
            BoardSpot spot = null;
            if (boardSpots != null)
                foreach (var s in boardSpots)
                    if (s != null && s.key == key) { spot = s; break; }
            float x = spot != null ? spot.x : DefaultSide(key) * 0.72f;
            float depth = spot != null ? spot.depth : 0.78f;
            height = (spot != null && spot.height > 0f ? spot.height : boardHeight) * d;
            yaw = spot != null ? spot.yaw : 0f;
            float wx = Mathf.Clamp(x, -1f, 1f) * Mathf.Max(0f, half - d * 0.8f);
            float wz = Mathf.Lerp(front + d * 0.8f, Mathf.Max(front + d, back - d * 1.1f), Mathf.Clamp01(depth));
            basePos = new Vector3(wx, FloorY(wx, wz), wz);
            return true;
        }

        /// <summary>置き場所の左右（キーで決める。-1 左、1 右）</summary>
        static float DefaultSide(string key)
        {
            int h = 17;
            if (key != null) foreach (char c in key) h = h * 31 + c;
            return (h & 1) == 0 ? 1f : -1f;
        }

        /// <summary>その場所の床の高さ（上から下へ調べる。メダル・動く物・盤面に置いたモデルは数えない）</summary>
        float FloorY(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 5f, z), Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<Medal>() != null) continue;
                var rb = h.collider.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;
                if (boardRoot != null && h.collider.transform.IsChildOf(boardRoot)) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? 0f : best;
        }

        // ================================================================
        // 中身
        // ================================================================

        readonly Dictionary<string, Type> spectacleTypes = new Dictionary<string, Type>();
        readonly Dictionary<string, RecordSpectacle> spectacles = new Dictionary<string, RecordSpectacle>();
        readonly List<RecordSpectacle> showing = new List<RecordSpectacle>();
        RecordSpectacle current;
        RecordData currentRecord;

        Transform rig;
        Camera overlay;
        RenderTexture overlayTexture;
        Canvas screenCanvas, dimCanvas;
        RawImage screenImage;
        Image dimImage;
        readonly List<RectTransform> lines = new List<RectTransform>();
        readonly List<Image> lineImages = new List<Image>();
        float dimLevel, dimTarget, linesTime = -1f, linesLength;
        Color linesColor = Color.white;
        Material dotMaterial, plainMaterial;
        Light keyLight, rimLight, fillLight;
        Camera mainCam;
        AudioSource audioSource;
        readonly Dictionary<SpectacleSfx, AudioClip> sfxClips = new Dictionary<SpectacleSfx, AudioClip>();
        bool hooked;
        float cameraMaskTimer;
        List<RecordData> previewRecords;
        int previewIndex = -1;
        RecordData previewRecord;
        float previewTimer = -1f;

        // ---- カメラ ----
        Vector3 basePos;
        Quaternion baseRot;
        float baseFov;
        bool camApplied;
        float introCam = -1f, finaleCam = -1f;
        float kick;
        float shakeTime = -1f, shakeSeconds, shakeStrength;

        // ---- スロー ----
        float slowStart = -1f, slowLength, slowScale, slowSet;

        // ---- 画面の部品 ----
        Canvas canvas;
        Font font, numFont;
        RectTransform barTop, barBottom;
        Image flashImage;
        RectTransform cardRoot, cardBand, cardJacketRoot;
        Image cardBandImage, cardStripe;
        RawImage cardJacket;
        Text cardTitle, cardSub, cardBadge;
        RectTransform bannerRoot;
        Text bannerText;
        Image bannerBand;
        float cardTime = -1f, barTime = -1f, flashAlpha;
        Color flashColor;
        float bannerTime = -1f, bannerDelay;
        float slamTime = -1f;
        Text slamText;
        Color bannerColor = Color.white;
        Image bannerEdgeTop, bannerEdgeBottom;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<RecordSpectacleDirector>() != null) return;
            var system = FindFirstObjectByType<RecordEffectSystem>();
            if (system != null) system.gameObject.AddComponent<RecordSpectacleDirector>();
        }

        // ================================================================
        // 初期化
        // ================================================================

        void Awake()
        {
            Instance = this;
            foreach (var t in typeof(RecordSpectacle).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(RecordSpectacle).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
                try
                {
                    var sp = (RecordSpectacle)Activator.CreateInstance(t);
                    if (!string.IsNullOrEmpty(sp.Key)) spectacleTypes[sp.Key] = t;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"RecordSpectacleDirector: {t.Name} を作れませんでした：{e.Message}");
                }
            }
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (effects == null) effects = GetComponent<RecordEffectSystem>();
            if (effects == null) effects = FindFirstObjectByType<RecordEffectSystem>();
            if (artwork == null) artwork = GetComponent<RecordArtwork>();
            if (artwork == null) artwork = FindFirstObjectByType<RecordArtwork>();
            if (effects == null)
            {
                Debug.LogWarning("RecordSpectacleDirector: RecordEffectSystem が見つからないので、大きな演出は出ません。", this);
                enabled = false;
                return;
            }

            Layer = PickLayer();
            BuildRig();
            BuildCanvas();
            var audioGo = new GameObject("SpectacleAudio");
            audioGo.transform.SetParent(transform, false);
            audioSource = audioGo.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            effects.RecordPlayed += OnPlayed;
            effects.RecordFinished += OnFinished;
            if (game != null)
            {
                game.MedalDropped += OnDropped;
                game.GameReset += HideAll;
            }
            hooked = true;
            gameObject.AddComponent<RecordSpectacleCameraRestore>().director = this;
        }

        void OnDestroy()
        {
            if (hooked)
            {
                if (effects != null)
                {
                    effects.RecordPlayed -= OnPlayed;
                    effects.RecordFinished -= OnFinished;
                }
                if (game != null)
                {
                    game.MedalDropped -= OnDropped;
                    game.GameReset -= HideAll;
                }
            }
            if (rig != null) Destroy(rig.gameObject);
            if (boardRoot != null) Destroy(boardRoot.gameObject);
            if (canvas != null) Destroy(canvas.gameObject);
            if (screenCanvas != null) Destroy(screenCanvas.gameObject);
            if (overlayTexture != null) { overlayTexture.Release(); Destroy(overlayTexture); }
            if (Instance == this) Instance = null;
        }

        void OnDisable()
        {
            RestoreCamera();
            EndSlowMo(true);
        }

        int PickLayer()
        {
            if (layer >= 8 && layer < 32) return layer;
            int named = LayerMask.NameToLayer("RecordSpectacle");
            if (named >= 0) return named;
            int skipA = game != null ? game.monitor.overlayLayer : 29, skipB = game != null ? game.monitor.frontEffectsLayer : 28;
            for (int i = 31; i >= 8; i--)
                if (i != skipA && i != skipB && string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return 31;
        }

        void BuildRig()
        {
            rig = new GameObject("RecordSpectacleRig").transform;
            var stageGo = new GameObject("Stage") { layer = Layer };
            Stage = stageGo.transform;
            Stage.SetParent(rig, false);
            Stage.localPosition = new Vector3(0f, 0f, StageDistance);

            var camGo = new GameObject("RecordSpectacleCamera");
            camGo.transform.SetParent(rig, false);
            overlay = camGo.AddComponent<Camera>();
            overlay.clearFlags = CameraClearFlags.Depth;
            overlay.cullingMask = 1 << Layer;
            overlay.nearClipPlane = 0.05f;
            overlay.farClipPlane = 200f;
            overlay.allowHDR = false;
            overlay.allowMSAA = false;
            overlay.useOcclusionCulling = false;
            overlay.enabled = false;

            keyLight = MakeLight("SpectacleKey", new Vector3(28f, -32f, 0f), new Color(1f, 0.96f, 0.9f), 1.15f);
            rimLight = MakeLight("SpectacleRim", new Vector3(-15f, 160f, 0f), Color.white, 0.9f);
            fillLight = MakeLight("SpectacleFill", new Vector3(-30f, 55f, 0f), new Color(0.55f, 0.65f, 1f), 0.35f);
            if (modelShadows)
            {
                keyLight.shadows = LightShadows.Soft;
                keyLight.shadowStrength = 0.55f;
                keyLight.shadowBias = 0.02f;
                keyLight.shadowNormalBias = 0.2f;
            }

            // ゲームの画面がドット化されている時（ArcadeCamera）は、モデルを別の絵に描いて、モニターの画面より上に重ねる
            var cgo = new GameObject("RecordSpectacleScreen");
            screenCanvas = cgo.AddComponent<Canvas>();
            screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var igo = new GameObject("Models");
            igo.transform.SetParent(cgo.transform, false);
            screenImage = igo.AddComponent<RawImage>();
            screenImage.raycastTarget = false;
            var premultiplied = Shader.Find("Hidden/MedalPusher/OverlayPremultiplied");
            if (premultiplied != null) plainMaterial = new Material(premultiplied);
            var dots = Shader.Find("MedalGame/SpectacleDots");
            if (dots == null) dots = Resources.Load<Shader>("SpectacleDots");
            if (dots != null) dotMaterial = new Material(dots);
            else Debug.LogWarning("RecordSpectacleDirector: SpectacleDots シェーダーが見つからないので、演出にドットのフィルターをかけられません。");
            screenImage.material = dotFilter && dotMaterial != null ? dotMaterial : plainMaterial;
            var irt = screenImage.rectTransform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
            cgo.SetActive(false);

            // モデルの後ろ（ゲームの画面・モニターの上）：画面を暗くする幕と、集中線
            var dgo = new GameObject("RecordSpectacleDim");
            dimCanvas = dgo.AddComponent<Canvas>();
            dimCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var dscaler = dgo.AddComponent<CanvasScaler>();
            dscaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            dscaler.referenceResolution = new Vector2(1920f, 1080f);
            dscaler.matchWidthOrHeight = 1f;
            var dim = Rect(dgo.transform, "Dim", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.color = Color.clear;
            dimImage.raycastTarget = false;
            for (int i = 0; i < 48; i++)
            {
                var l = Rect(dgo.transform, "SpeedLine", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(600f, 6f));
                l.localRotation = Quaternion.Euler(0f, 0f, i * 7.5f + UnityEngine.Random.Range(-3f, 3f));
                var img = l.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                img.color = Color.clear;
                lines.Add(l);
                lineImages.Add(img);
            }
            dgo.SetActive(false);
        }

        /// <summary>ゲームの画面を暗くする（level 0〜1。モデルの後ろだけ暗くなる）</summary>
        public void Dim(float level) => dimTarget = boardMode ? 0f : Mathf.Clamp01(level) * introDim;

        /// <summary>画面のまん中から放射状の集中線を seconds 秒出す</summary>
        public void SpeedLines(float seconds, Color color)
        {
            if (!speedLines || boardMode) return;
            linesTime = 0f;
            linesLength = Mathf.Max(0.1f, seconds);
            linesColor = color;
        }

        void UpdateDimAndLines(float dt)
        {
            if (dimCanvas == null) return;
            dimLevel = Mathf.MoveTowards(dimLevel, dimTarget, dt * 2.5f);
            bool linesOn = linesTime >= 0f;
            bool show = (dimLevel > 0.001f || linesOn) && screenCanvas != null && screenCanvas.gameObject.activeSelf;
            if (dimCanvas.gameObject.activeSelf != show) dimCanvas.gameObject.SetActive(show);
            if (!show) return;
            dimCanvas.sortingOrder = screenCanvas.sortingOrder - 1;
            dimImage.color = new Color(0f, 0f, 0.02f, dimLevel);
            if (!linesOn) { foreach (var img in lineImages) img.color = Color.clear; return; }
            linesTime += dt;
            float env = Mathf.Clamp01(linesTime / 0.15f) * Mathf.Clamp01((linesLength - linesTime) / 0.4f);
            for (int i = 0; i < lines.Count; i++)
            {
                // 内側の端が中心のまわりで出たり入ったりして、外へ流れて見える
                float phase = Mathf.Repeat(linesTime * 3.2f + i * 0.37f, 1f);
                float inner = Mathf.Lerp(260f, 520f, phase);
                lines[i].anchoredPosition = (Vector2)(lines[i].localRotation * Vector3.right) * inner;
                lines[i].sizeDelta = new Vector2(1400f, Mathf.Lerp(10f, 3f, phase) * (1f + (i % 3)));
                Color c = i % 4 == 0 ? linesColor : Color.white;
                lineImages[i].color = new Color(c.r, c.g, c.b, 0.55f * env * (1f - phase * 0.6f));
            }
            if (linesTime >= linesLength) linesTime = -1f;
        }

        /// <summary>モデルを描く先を決める（ドット化されている時は専用の絵、そうでなければ画面に直接）</summary>
        void RouteOverlay(bool any)
        {
            RenderTexture mainTarget = mainCam.targetTexture;
            bool own = any && mainTarget != null;
            if (own)
            {
                int scale = Mathf.Clamp(modelResolution, 1, 4);
                int h = Mathf.Min(mainTarget.height * scale, Mathf.Max(mainTarget.height, UnityEngine.Screen.height));
                int w = Mathf.Max(1, Mathf.RoundToInt(h * mainTarget.width / (float)Mathf.Max(1, mainTarget.height)));
                int aa = modelAntiAliasing ? 4 : 1;
                if (overlayTexture == null || overlayTexture.width != w || overlayTexture.height != h || overlayTexture.antiAliasing != aa)
                {
                    if (overlayTexture != null) { overlayTexture.Release(); Destroy(overlayTexture); }
                    overlayTexture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
                    {
                        name = "RecordSpectacle",
                        filterMode = scale > 1 ? FilterMode.Bilinear : FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        antiAliasing = aa,
                    };
                    overlayTexture.Create();
                }
                overlay.targetTexture = overlayTexture;
                overlay.allowMSAA = aa > 1;
                overlay.clearFlags = CameraClearFlags.SolidColor;
                overlay.backgroundColor = new Color(0f, 0f, 0f, 0f);
                screenImage.texture = overlayTexture;
                var arcade = mainCam.GetComponent<ArcadeCamera>();
                screenCanvas.sortingOrder = (arcade != null ? arcade.sortingOrder : -100) + screenOrderOffset;
                var mat = dotFilter && dotMaterial != null ? dotMaterial : plainMaterial;
                if (screenImage.material != mat) screenImage.material = mat;
                if (mat == dotMaterial && dotMaterial != null)
                {
                    // ドットの数はゲームの画面と同じ。色の段階・ディザ・ふちどりなどは、ゲームの画面のマテリアルからまねる
                    dotMaterial.SetVector("_DotCount", new Vector4(mainTarget.width, mainTarget.height, 0f, 0f));
                    var src = arcade != null ? arcade.screenMaterial : null;
                    if (src != null)
                        foreach (var prop in new[] { "_ColorLevels", "_DitherStrength", "_OutlineStrength", "_OutlineThreshold", "_ScanlineStrength" })
                            if (src.HasProperty(prop)) dotMaterial.SetFloat(prop, src.GetFloat(prop));
                    if (src != null && src.HasProperty("_OutlineColor")) dotMaterial.SetColor("_OutlineColor", src.GetColor("_OutlineColor"));
                }
            }
            else
            {
                overlay.targetTexture = null;
                overlay.clearFlags = CameraClearFlags.Depth;
            }
            if (screenCanvas.gameObject.activeSelf != own) screenCanvas.gameObject.SetActive(own);
        }

        Light MakeLight(string name, Vector3 euler, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(rig, false);
            go.transform.localRotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << Layer;
            l.enabled = false;
            return l;
        }

        /// <summary>モデルを後ろから照らす光の色</summary>
        public void SetRim(Color color, float intensity)
        {
            if (rimLight == null) return;
            rimLight.color = color;
            rimLight.intensity = intensity;
            if (fillLight != null) fillLight.color = Color.Lerp(new Color(0.55f, 0.65f, 1f), color, 0.4f);
        }

        // ================================================================
        // イベント
        // ================================================================

        void OnPlayed(RecordData record)
        {
            if (!spectacleEnabled || RecordData.IsNullOrEmpty(record)) return;
            if (record != previewRecord) { previewRecord = null; previewTimer = -1f; }
            HideAll();
            if (skipKeys != null && skipKeys.Contains(record.key))
            {
                current = null;
                currentRecord = null;
                return;
            }
            var sp = GetSpectacle(record.key);
            current = sp;
            currentRecord = record;
            SetRim(Color.Lerp(sp.Theme, Color.white, 0.3f), 1f);
            sp.Begin(this, record);
            if (!showing.Contains(sp)) showing.Add(sp);
            introCam = boardMode ? -1f : 0f;   // 盤面に置く時は、カメラの動き・帯・大きな文字を出さない
            finaleCam = -1f;
            barTime = boardMode ? -1f : 0f;
            slamTime = boardMode ? -1f : 0f;
            slamText.text = sp.Title;
            slamText.color = Color.Lerp(sp.Theme, Color.white, 0.15f);
            if (titleCard) ShowCard(record, sp);
        }

        void OnFinished(RecordData record)
        {
            if (previewRecord != null && record == previewRecord) previewTimer = -1f;
            if (current == null || currentRecord == null || record == null || record.key != currentRecord.key) return;
            current.Finale();
            finaleCam = boardMode ? -1f : 0f;
            introCam = -1f;
            current = null;
            currentRecord = null;
        }

        void OnDropped(Medal medal, DropZoneType type)
        {
            if (current == null || medal == null || type == DropZoneType.Lost) return;
            if (game != null && game.State != GameState.Playing && game.State != GameState.Grace) return;
            current.Drop(type, WorldToStage(medal.transform.position));
        }

        void HideAll()
        {
            foreach (var sp in showing) sp.Hide();
            showing.Clear();
            current = null;
            currentRecord = null;
            introCam = finaleCam = -1f;
            cardTime = barTime = bannerTime = slamTime = -1f;
            dimTarget = 0f;
            linesTime = -1f;
        }

        // ================================================================
        // デバッグ：演出だけを見る（F4）
        // ================================================================

        void UpdatePreview(float dt)
        {
            if (previewTimer >= 0f)
            {
                previewTimer -= dt;
                if (previewTimer < 0f && previewRecord != null) OnFinished(previewRecord);
            }
            if (!(Application.isEditor || Debug.isDebugBuild) || !DexKeys.Pressed(DexKeys.Key.F4)) return;
            if (previewRecords == null) previewRecords = RecordDatabase.LoadFromResources();
            if (previewRecords == null || previewRecords.Count == 0) return;
            previewIndex = (previewIndex + (DexKeys.ShiftHeld() ? -1 : 1) + previewRecords.Count) % previewRecords.Count;
            previewRecord = previewRecords[previewIndex];
            OnPlayed(previewRecord);
            previewTimer = 12f;
            Debug.Log($"RecordSpectacleDirector: 演出のプレビュー {previewRecord.id} {previewRecord.name}（F4 で次、Shift+F4 で前）");
        }

        RecordSpectacle GetSpectacle(string key)
        {
            if (spectacles.TryGetValue(key, out var sp)) return sp;
            if (spectacleTypes.TryGetValue(key, out var type)) sp = (RecordSpectacle)Activator.CreateInstance(type);
            else sp = new GenericSpectacle(key);
            spectacles[key] = sp;
            return sp;
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            float dt = Time.deltaTime;
            UpdatePreview(dt);
            for (int i = showing.Count - 1; i >= 0; i--)
            {
                var sp = showing[i];
                sp.Frame(dt);
                if (!sp.Visible) showing.RemoveAt(i);
            }
            UpdateSlowMo();
            UpdateUi(Time.unscaledDeltaTime, dt);
            UpdateDimAndLines(dt);
        }

        void LateUpdate()
        {
            mainCam = Camera.main;
            if (mainCam == null || rig == null) return;

            // ほかのカメラには、このレイヤーを描かせない（1秒に1回だけ確かめる）
            cameraMaskTimer -= Time.unscaledDeltaTime;
            if (cameraMaskTimer <= 0f)
            {
                cameraMaskTimer = 1f;
                int bit = 1 << Layer;
                foreach (var c in Camera.allCameras)
                    if (c != overlay && (c.cullingMask & bit) != 0) c.cullingMask &= ~bit;
            }

            RestoreCamera();
            basePos = mainCam.transform.position;
            baseRot = mainCam.transform.rotation;
            baseFov = mainCam.fieldOfView;

            // 舞台：カメラの前に置き、画面の高さが 10 になる大きさにする
            rig.SetPositionAndRotation(basePos, baseRot);
            float aspect = mainCam.targetTexture != null ? mainCam.targetTexture.width / (float)Mathf.Max(1, mainCam.targetTexture.height) : mainCam.aspect;
            HalfW = HalfH * Mathf.Max(0.3f, aspect);
            StageScale = 2f * StageDistance * Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) / (HalfH * 2f);
            Stage.localScale = Vector3.one * StageScale;

            ApplyCamera(Time.deltaTime);

            bool any = showing.Count > 0;
            overlay.enabled = any;
            keyLight.enabled = any;
            rimLight.enabled = any;
            fillLight.enabled = any;
            RouteOverlay(any);
            if (any)
            {
                overlay.depth = mainCam.depth + 0.5f;
                overlay.fieldOfView = mainCam.fieldOfView;
                overlay.transform.SetPositionAndRotation(mainCam.transform.position, mainCam.transform.rotation);
            }
        }

        // ================================================================
        // カメラ
        // ================================================================

        /// <summary>カメラを元に戻す（RecordSpectacleCameraRestore がフレームの最初に呼ぶ）</summary>
        public void RestoreCamera()
        {
            if (!camApplied) return;
            camApplied = false;
            if (mainCam == null) return;
            mainCam.transform.SetPositionAndRotation(basePos, baseRot);
            mainCam.fieldOfView = baseFov;
        }

        void ApplyCamera(float dt)
        {
            Vector3 move = Vector3.zero, euler = Vector3.zero;
            float fov = 0f;
            float unit = game != null ? Mathf.Max(0.2f, game.layout.boardWidth / 8f) : 1f;

            if (introCam >= 0f)
            {
                introCam += dt;
                float t = introCam;
                float w = t < 0.35f ? 0f : t < 1.2f ? Ease.OutCubic((t - 0.35f) / 0.85f) : t < 2.5f ? 1f : 1f - Ease.InOutCubic((t - 2.5f) / 0.9f);
                if (t > 3.4f) { introCam = -1f; w = 0f; }
                move += new Vector3(0f, 0.07f, 0.28f) * unit * w;
                euler += new Vector3(-2.5f * w, 1.2f * Mathf.Sin(t * 1.4f) * w, 1.4f * Mathf.Sin(t * 1.1f) * w);
                fov -= 5f * w;
            }
            if (finaleCam >= 0f)
            {
                finaleCam += dt;
                float t = finaleCam;
                float w = t < 0.5f ? Ease.OutCubic(t / 0.5f) : t < 1.9f ? 1f : 1f - Ease.InOutCubic((t - 1.9f) / 0.8f);
                if (t > 2.7f) { finaleCam = -1f; w = 0f; }
                move += new Vector3(0f, 0.05f, 0.18f) * unit * w;
                euler += new Vector3(-1.8f * w, 0f, -1.5f * Mathf.Sin(t * 2f) * w);
                fov -= 4f * w;
            }

            kick *= Mathf.Exp(-dt * 6f);
            if (kick > 0.001f)
            {
                fov += 7f * kick;
                move += new Vector3(0f, 0f, -0.06f) * unit * kick;
            }

            if (shakeTime >= 0f)
            {
                shakeTime += dt;
                float k = 1f - Mathf.Clamp01(shakeTime / Mathf.Max(0.01f, shakeSeconds));
                if (k <= 0f) shakeTime = -1f;
                else
                {
                    float n = Time.unscaledTime * 28f;
                    move += new Vector3(Mathf.PerlinNoise(n, 0.3f) - 0.5f, Mathf.PerlinNoise(1.7f, n) - 0.5f, 0f) * 2f * shakeStrength * k * unit;
                    euler.z += (Mathf.PerlinNoise(n * 0.7f, 5.1f) - 0.5f) * 3f * shakeStrength * k * 10f;
                }
            }

            if (!moveCamera) return;
            float s = cameraStrength;
            if (move == Vector3.zero && euler == Vector3.zero && Mathf.Approximately(fov, 0f)) return;
            mainCam.transform.SetPositionAndRotation(basePos + baseRot * (move * s), baseRot * Quaternion.Euler(euler * s));
            mainCam.fieldOfView = Mathf.Clamp(baseFov + fov * s, 10f, 120f);
            camApplied = true;
        }

        /// <summary>カメラをどんっと押す（画角が一瞬広がる）</summary>
        public void Kick(float strength) { if (!boardMode) kick = Mathf.Max(kick, strength); }

        /// <summary>画面を揺らす</summary>
        public void Shake(float strength, float seconds)
        {
            if (boardMode) return;
            shakeStrength = Mathf.Max(shakeTime >= 0f ? shakeStrength : 0f, strength);
            shakeSeconds = Mathf.Max(0.05f, seconds);
            shakeTime = 0f;
        }

        // ================================================================
        // スロー
        // ================================================================

        /// <summary>一瞬スローにする（ほかの所が時間を変えている時は何もしない）</summary>
        public void SlowMo(float scale, float seconds)
        {
            if (!slowMotion || boardMode || slowStart >= 0f || !Mathf.Approximately(Time.timeScale, 1f)) return;
            slowScale = Mathf.Clamp(scale, 0.05f, 1f);
            slowLength = Mathf.Max(0.05f, seconds);
            slowStart = Time.unscaledTime;
            slowSet = slowScale;
            Time.timeScale = slowSet;
        }

        void UpdateSlowMo()
        {
            if (slowStart < 0f) return;
            if (!Mathf.Approximately(Time.timeScale, slowSet)) { slowStart = -1f; return; }   // ほかの所が変えた
            float k = (Time.unscaledTime - slowStart) / slowLength;
            if (k >= 1f) { EndSlowMo(false); return; }
            slowSet = k < 0.6f ? slowScale : Mathf.Lerp(slowScale, 1f, Ease.InOutCubic((k - 0.6f) / 0.4f));
            Time.timeScale = slowSet;
        }

        void EndSlowMo(bool force)
        {
            if (slowStart < 0f) return;
            if (force || Mathf.Approximately(Time.timeScale, slowSet)) Time.timeScale = 1f;
            slowStart = -1f;
        }

        // ================================================================
        // 音
        // ================================================================

        public void Sfx(SpectacleSfx sfx, float vol, float pitch)
        {
            if (audioSource == null || volume <= 0f) return;
            if (boardMode) vol *= 0.6f;   // 盤面に置く時は、少し小さく
            if (!sfxClips.TryGetValue(sfx, out var clip) || clip == null)
            {
                clip = MakeSfx(sfx);
                sfxClips[sfx] = clip;
            }
            if (clip == null) return;
            audioSource.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
            audioSource.PlayOneShot(clip, volume * vol);
        }

        static AudioClip MakeSfx(SpectacleSfx sfx)
        {
            float N(int s) => RecordSynth.Note(s);
            switch (sfx)
            {
                case SpectacleSfx.Boom: return RecordSynth.Thud("SpBoom", 46f, 1.3f, 0.95f);
                case SpectacleSfx.Crash: return RecordSynth.Noise("SpCrash", 1.4f, 0.6f, 0.004f, 0.35f, 0.65f, 7);
                case SpectacleSfx.Whoosh: return RecordSynth.Noise("SpWhoosh", 0.9f, 0.12f, 0.45f, 0.22f, 0.55f, 11);
                case SpectacleSfx.Rise: return RecordSynth.Sweep("SpRise", 160f, 980f, 1.0f, 0.25f, RecordSynth.Wave.Saw);
                case SpectacleSfx.Zap: return RecordSynth.Sweep("SpZap", 1500f, 180f, 0.25f, 0.35f, RecordSynth.Wave.Square);
                case SpectacleSfx.Chime: return RecordSynth.Notes("SpChime", new[] { N(24), N(28), N(31), N(36) }, 0.07f, 0.9f, 0.35f, RecordSynth.Wave.Bell);
                case SpectacleSfx.Sparkle: return RecordSynth.Notes("SpSparkle", new[] { N(31), N(35), N(38), N(43), N(47) }, 0.045f, 0.6f, 0.25f, RecordSynth.Wave.Bell);
                case SpectacleSfx.Fanfare: return RecordSynth.Notes("SpFanfare", new[] { N(12), N(16), N(19), N(24), N(19), N(24) }, 0.11f, 1.0f, 0.3f, RecordSynth.Wave.Square);
                case SpectacleSfx.Bell: return RecordSynth.Notes("SpBell", new[] { N(21) }, 0.1f, 1.6f, 0.45f, RecordSynth.Wave.Bell);
                case SpectacleSfx.Drum: return RecordSynth.Thud("SpDrum", 85f, 0.45f, 0.95f);
                case SpectacleSfx.Splash: return RecordSynth.Noise("SpSplash", 1.1f, 0.35f, 0.01f, 0.45f, 0.6f, 5);
                case SpectacleSfx.Horn: return RecordSynth.Chord("SpHorn", new[] { N(-3), N(1), N(4) }, 1.3f, 0.45f, RecordSynth.Wave.Saw);
                case SpectacleSfx.Laser: return RecordSynth.Sweep("SpLaser", 2400f, 380f, 0.32f, 0.3f, RecordSynth.Wave.Square);
                case SpectacleSfx.Coin: return RecordSynth.Notes("SpCoin", new[] { N(35), N(40) }, 0.07f, 0.35f, 0.3f, RecordSynth.Wave.Square);
            }
            return null;
        }

        // ================================================================
        // 座標
        // ================================================================

        /// <summary>画面の割合（左下 0,0）を Stage の座標にする</summary>
        public Vector3 ViewportToStage(Vector2 vp) => new Vector3((vp.x - 0.5f) * 2f * HalfW, (vp.y - 0.5f) * 2f * HalfH, 0f);

        /// <summary>ワールドの点を Stage の座標にする（画面のどこに見えるか）</summary>
        public Vector3 WorldToStage(Vector3 world)
        {
            var cam = mainCam != null ? mainCam : Camera.main;
            if (cam == null) return Vector3.zero;
            Vector3 vp = camApplied ? WorldToViewportBase(cam, world) : cam.WorldToViewportPoint(world);
            return ViewportToStage(new Vector2(vp.x, vp.y));
        }

        Vector3 WorldToViewportBase(Camera cam, Vector3 world)
        {
            Vector3 local = Quaternion.Inverse(baseRot) * (world - basePos);
            if (local.z <= 0.001f) return new Vector3(0.5f, 0.5f, 0f);
            float h = Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) * local.z;
            float w = h * (HalfW / HalfH);
            return new Vector3(0.5f + local.x / w * 0.5f, 0.5f + local.y / h * 0.5f, local.z);
        }

        // ================================================================
        // 画面の文字（帯・タイトルカード・見出し）
        // ================================================================

        void BuildCanvas()
        {
            font = bodyFont != null ? bodyFont : Resources.Load<Font>(MonitorDisplay.FontFolder + "/Body");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            numFont = titleFont != null ? titleFont : Resources.Load<Font>(MonitorDisplay.FontFolder + "/Display");
            if (numFont == null) numFont = font;

            var go = new GameObject("RecordSpectacleCanvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var root = go.transform;

            // 上下の黒い帯（映画のように）
            barTop = Rect(root, "BarTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 0f));
            barTop.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);
            barBottom = Rect(root, "BarBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 0f));
            barBottom.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);

            // 光
            var f = Rect(root, "Flash", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            flashImage = f.gameObject.AddComponent<Image>();
            flashImage.color = Color.clear;
            flashImage.raycastTarget = false;

            // タイトルカード（左下）
            cardRoot = Rect(root, "Card", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0f, 150f), new Vector2(1500f, 260f));
            cardBand = Rect(cardRoot, "Band", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(-40f, 40f), new Vector2(1400f, 170f));
            cardBand.localRotation = Quaternion.Euler(0f, 0f, 3f);
            cardBandImage = cardBand.gameObject.AddComponent<Image>();
            var stripe = Rect(cardBand, "Stripe", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, 14f));
            cardStripe = stripe.gameObject.AddComponent<Image>();
            cardJacketRoot = Rect(cardRoot, "JacketRoot", Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(190f, 140f), new Vector2(236f, 236f));
            cardJacketRoot.gameObject.AddComponent<Image>().color = Color.white;
            var jr = Rect(cardJacketRoot, "Jacket", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-16f, -16f));
            cardJacket = jr.gameObject.AddComponent<RawImage>();
            cardTitle = MakeText(cardRoot, "Title", new Vector2(330f, 120f), new Vector2(1150f, 130f), numFont, 104, TextAnchor.LowerLeft);
            cardSub = MakeText(cardRoot, "Sub", new Vector2(336f, 58f), new Vector2(1150f, 56f), font, 36, TextAnchor.MiddleLeft);
            cardBadge = MakeText(cardRoot, "Badge", new Vector2(336f, 226f), new Vector2(600f, 40f), numFont, 30, TextAnchor.MiddleLeft);
            foreach (var t in new[] { cardTitle, cardSub, cardBadge })
            {
                var o = t.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.75f);
                o.effectDistance = new Vector2(3f, -3f);
            }
            cardRoot.gameObject.SetActive(false);

            // 見出し（まん中の上）
            bannerRoot = Rect(root, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(1600f, 190f));
            var bb = Rect(bannerRoot, "Band", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 120f));
            bannerBand = bb.gameObject.AddComponent<Image>();
            var et = Rect(bb, "EdgeTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 6f));
            bannerEdgeTop = et.gameObject.AddComponent<Image>();
            var eb = Rect(bb, "EdgeBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 6f));
            bannerEdgeBottom = eb.gameObject.AddComponent<Image>();
            bannerText = MakeText(bannerRoot, "Text", Vector2.zero, new Vector2(1600f, 190f), numFont, 128, TextAnchor.MiddleCenter);
            bannerText.rectTransform.anchorMin = bannerText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            bannerText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            bannerText.rectTransform.anchoredPosition = Vector2.zero;
            var bo = bannerText.gameObject.AddComponent<Outline>();
            bo.effectColor = Color.white;
            bo.effectDistance = new Vector2(4f, -4f);
            var bs = bannerText.gameObject.AddComponent<Shadow>();
            bs.effectColor = new Color(0f, 0f, 0f, 0.8f);
            bs.effectDistance = new Vector2(8f, -8f);
            bannerRoot.gameObject.SetActive(false);

            // タイトルをまん中にたたきつける（カードに入る前に）
            slamText = MakeText(root, "Slam", Vector2.zero, new Vector2(1900f, 260f), numFont, 190, TextAnchor.MiddleCenter);
            var srt = slamText.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            var so = slamText.gameObject.AddComponent<Outline>();
            so.effectColor = Color.white;
            so.effectDistance = new Vector2(6f, -6f);
            var ss = slamText.gameObject.AddComponent<Shadow>();
            ss.effectColor = new Color(0f, 0f, 0f, 0.85f);
            ss.effectDistance = new Vector2(12f, -12f);
            slamText.gameObject.SetActive(false);

            foreach (var g in canvas.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        }

        static RectTransform Rect(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Text MakeText(Transform parent, string name, Vector2 pos, Vector2 size, Font f, int fontSize, TextAnchor align)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = f;
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = Color.white;
            return t;
        }

        void ShowCard(RecordData record, RecordSpectacle sp)
        {
            Color theme = sp.Theme;
            cardBandImage.color = new Color(theme.r * 0.55f, theme.g * 0.55f, theme.b * 0.55f, 0.92f);
            cardStripe.color = Color.Lerp(theme, Color.white, 0.35f);
            var tex = artwork != null ? artwork.GetJacket(record) : null;
            cardJacket.texture = tex;
            cardJacket.color = tex != null ? Color.white : RecordArtwork.CategoryColor(record.category);
            cardTitle.text = sp.Title;
            cardTitle.color = Color.white;
            string catchLine = string.IsNullOrEmpty(sp.Catch) ? "" : "　" + sp.Catch;
            cardSub.text = $"{record.name}{catchLine}";
            cardBadge.text = $"{record.id}  ·  {record.category}  ·  {RarityStars(record.rarity)}";
            cardBadge.color = Color.Lerp(theme, Color.white, 0.55f);
            cardTime = 0f;
        }

        static string RarityStars(string rarity)
        {
            switch (rarity)
            {
                case "コモン": return "★";
                case "レア": return "★★";
                case "エピック": return "★★★";
                case "レジェンド": return "★★★★";
            }
            return rarity ?? "";
        }

        /// <summary>まん中の上に大きな文字を出す（delay 秒あとに）</summary>
        public void Banner(string text, Color color, float delay)
        {
            if (bannerText == null || string.IsNullOrEmpty(text) || boardMode) return;
            bannerText.text = text;
            bannerText.color = color;
            var o = bannerText.GetComponent<Outline>();
            if (o != null) o.effectColor = Color.Lerp(color, Color.white, 0.75f);
            bannerBand.color = new Color(0f, 0f, 0f, 0.55f);
            bannerColor = color;
            bannerTime = 0f;
            bannerDelay = delay;
        }

        /// <summary>画面を光らせる</summary>
        public void Flash(Color color, float alpha)
        {
            if (boardMode) return;
            flashColor = color;
            flashAlpha = Mathf.Max(flashAlpha, Mathf.Clamp01(alpha));
        }

        void UpdateUi(float udt, float dt)
        {
            if (canvas == null) return;

            // 光
            flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, udt * 2.2f);
            flashImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashAlpha);

            // 上下の帯
            float bar = 0f;
            if (barTime >= 0f)
            {
                barTime += dt;
                float t = barTime;
                bar = t < 0.35f ? 0f : t < 0.7f ? Ease.OutCubic((t - 0.35f) / 0.35f) : t < 3.0f ? 1f : 1f - Ease.InOutCubic((t - 3.0f) / 0.45f);
                if (t > 3.45f) { barTime = -1f; bar = 0f; }
            }
            float barH = 92f * bar;
            barTop.sizeDelta = new Vector2(0f, barH);
            barBottom.sizeDelta = new Vector2(0f, barH);

            // タイトルをたたきつける（ブーストのカットインが終わった直後。0.55 秒）
            if (slamTime >= 0f)
            {
                slamTime += dt;
                float t = slamTime - 1.8f;
                bool on = t >= 0f && t < 0.6f;
                if (slamText.gameObject.activeSelf != on) slamText.gameObject.SetActive(on);
                if (on)
                {
                    float inK = Ease.OutBack(Mathf.Clamp01(t / 0.14f));
                    float outK = Ease.InCubic(Mathf.Clamp01((t - 0.42f) / 0.18f));
                    float s = Mathf.Lerp(3.2f, 1f, inK) * (1f - 0.5f * outK);
                    var rt = slamText.rectTransform;
                    rt.localScale = new Vector3(s, s, 1f);
                    rt.localRotation = Quaternion.Euler(0f, 0f, -4f + 3f * Mathf.Sin(t * 40f) * (1f - inK));
                    rt.anchoredPosition = Vector2.Lerp(new Vector2(UnityEngine.Random.Range(-8f, 8f), 60f + UnityEngine.Random.Range(-8f, 8f)), new Vector2(-560f, -330f), outK);
                    var c = slamText.color; c.a = 1f - outK; slamText.color = c;
                    if (t < dt * 1.5f) { Flash(Color.white, 0.55f); Kick(0.8f); Shake(0.12f, 0.35f); Sfx(SpectacleSfx.Boom, 0.8f, 1.3f); }
                }
                if (t >= 0.6f) slamTime = -1f;
            }

            // タイトルカード（たたきつけたタイトルが、左下のカードに入る）
            if (cardTime >= 0f)
            {
                cardTime += dt;
                float t = cardTime - 2.35f;
                bool on = t >= 0f && t < 2.5f;
                if (cardRoot.gameObject.activeSelf != on) cardRoot.gameObject.SetActive(on);
                if (on)
                {
                    float inK = Ease.OutCubic(t / 0.35f), outK = Ease.InCubic(Mathf.Clamp01((t - 2.1f) / 0.4f));
                    cardBand.anchoredPosition = new Vector2(Mathf.Lerp(-1500f, -40f, inK) - 1500f * outK, 40f);
                    float j = Ease.OutBack(Mathf.Clamp01((t - 0.12f) / 0.4f));
                    cardJacketRoot.localScale = Vector3.one * Mathf.Max(0f, j) * (1f - outK);
                    cardJacketRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-30f, -5f, j) + 2f * Mathf.Sin(t * 3f));
                    float tk = Ease.OutCubic(Mathf.Clamp01((t - 0.2f) / 0.35f));
                    cardTitle.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(600f, 330f, tk) - 1200f * outK, 120f);
                    var c = cardTitle.color; c.a = tk * (1f - outK); cardTitle.color = c;
                    float sk = Ease.OutCubic(Mathf.Clamp01((t - 0.32f) / 0.35f));
                    cardSub.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(700f, 336f, sk) - 1200f * outK, 58f);
                    c = cardSub.color; c.a = sk * (1f - outK); cardSub.color = c;
                    c = cardBadge.color; c.a = sk * (1f - outK); cardBadge.color = c;
                }
                if (t >= 2.5f) cardTime = -1f;
            }

            // 見出し
            if (bannerTime >= 0f)
            {
                bannerTime += udt;
                float t = bannerTime - bannerDelay;
                bool on = t >= 0f && t < 1.6f;
                if (bannerRoot.gameObject.activeSelf != on) bannerRoot.gameObject.SetActive(on);
                if (on)
                {
                    float s = t < 0.22f ? Mathf.Lerp(1.8f, 1f, Ease.OutBack(t / 0.22f)) : 1f + 0.02f * Mathf.Sin(t * 12f);
                    float a = t < 1.25f ? 1f : 1f - (t - 1.25f) / 0.35f;
                    bannerRoot.localScale = new Vector3(s, s, 1f);
                    var c = bannerText.color; c.a = a; bannerText.color = c;
                    float bw = Ease.OutCubic(Mathf.Clamp01(t / 0.25f));
                    bannerBand.rectTransform.localScale = new Vector3(bw, 1f, 1f);
                    bannerBand.color = new Color(0f, 0f, 0f, 0.55f * a);
                    var ec = new Color(bannerColor.r, bannerColor.g, bannerColor.b, a);
                    bannerEdgeTop.color = ec;
                    bannerEdgeBottom.color = ec;
                }
                if (t >= 1.6f) bannerTime = -1f;
            }
        }
    }

    /// <summary>
    /// フレームの最初に、カメラを元の位置に戻す（RecordSpectacleDirector が自動で付ける）。
    /// ゲームの処理（マウスの当たり判定など）は、いつも元のカメラの位置で動く。
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class RecordSpectacleCameraRestore : MonoBehaviour
    {
        [HideInInspector] public RecordSpectacleDirector director;
        void Update() { if (director != null) director.RestoreCamera(); }
    }

    /// <summary>専用の演出が無いレコード用：大きなレコード盤が回る</summary>
    public class GenericSpectacle : RecordSpectacle
    {
        readonly string key;
        Transform disc;
        ParticleSystem notes;
        public GenericSpectacle() : this("") { }
        public GenericSpectacle(string key) { this.key = key; }
        public override string Key => key;
        public override string Title => string.IsNullOrEmpty(key) ? "RECORD" : key.ToUpperInvariant();
        public override Color Theme => C("#f2a33a");

        protected override void Build()
        {
            disc = Group(Hero, "Disc", Vector3.zero, new Vector3(90f, 0f, 0f));
            Cyl(disc, Vector3.zero, 1f, 0.04f, Mat(C("#141418"), 0.3f, 0.85f));
            for (int i = 1; i <= 4; i++) Ring(disc, new Vector3(0f, 0.022f, 0f), 0.2f + i * 0.07f, 0.004f, Mat(C("#2a2a30"), 0.2f, 0.9f));
            Cyl(disc, new Vector3(0f, 0.025f, 0f), 0.34f, 0.01f, Glow(Theme, 1.2f));
            notes = Fx(0.08f, 1.4f, -0.05f, Hero);
        }

        protected override void Idle(float t, float dt)
        {
            disc.Rotate(0f, 200f * dt, 0f, Space.Self);
            Drip(notes, new Vector3(0f, 0.3f, 0f), Theme, 6f, 0.6f, Vector3.up, 0.6f, dt);
        }
    }
}
