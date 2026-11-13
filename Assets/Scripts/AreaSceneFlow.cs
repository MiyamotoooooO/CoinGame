using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MedalPusher
{
    /// <summary>
    /// エリアが変わる時に、シーンを切り替える（エリア1 = MainScene、エリア2 = Area2 …）。
    ///
    /// 流れ
    ///   1. ボスを倒して次のエリアへ進むと（MedalPusherGame がエリアを1つ進めた時）、画面を暗くして、音を小さくする
    ///   2. 切り替えのムービーを流す（Transition Movies にそのエリアの動画があればそれ。無ければ仮の「AREA 2」の文字）
    ///   3. 次のエリアのシーン（Area Scenes）を読みこむ
    ///   4. 新しいシーンで、エリアの番号・通算のラウンド数・特殊ポイントを引きつぎ、画面を明るく戻す
    ///      （レコード・所持メダル・ポイント・投入上限アップ・ゲージは、引きつがない＝はじめの状態）
    ///      エリアの始まりの演出（Area2IntroMovie など）は、新しいシーンでいつもどおり流れる
    ///
    /// ・Area Scenes にシーン名が無いエリアは、今までどおり同じシーンのまま進む
    /// ・シーンは File → Build Settings の「Scenes In Build」に入れておく（入っていないと切り替えない）
    /// ・Area2 などのシーンを直接再生した時（引きつぐ物が無い時）は、そのシーンのエリアから始める（確かめる用）
    /// ・MedalPusherGame は変えない。特殊ポイントと通算のラウンド数は、外から書きかえる口が無いので、リフレクションで書きかえる
    /// ・シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    /// ・シーンを切り替えた時は、ほかの自動で付くしくみ（PointsHud・BossBattle・レコードの演出など）も、新しいシーンで付け直す
    /// </summary>
    [DisallowMultipleComponent]
    public class AreaSceneFlow : MonoBehaviour
    {
        [Tooltip("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;

        [Tooltip("エリアごとのシーン名（0 = エリア1）。空のエリアは、シーンを切り替えずに進む")]
        public string[] areaScenes = { "MainScene", "Area2", "", "", "", "" };

        [Header("切り替えの演出")]
        [Tooltip("エリアごとの切り替えムービー（0 = エリア1 に入る時）。空なら、仮の暗転と「AREA ○」の文字")]
        public VideoClip[] transitionMovies = new VideoClip[6];
        [Tooltip("暗くなる・明るくなるのにかかる秒数")]
        [Range(0.05f, 3f)] public float fadeSeconds = 0.5f;
        [Tooltip("仮の演出（ムービーが無い時）で、「AREA ○」を出しておく秒数")]
        [Range(0f, 10f)] public float placeholderSeconds = 1.5f;

        /// <summary>シーンをまたいで引きつぐ物（新しいシーンで使ったら消す）。</summary>
        static class Carry
        {
            public static bool pending;
            public static int area;
            public static int roundNumber;
            public static int specialPoints;
        }

        static bool transitioning;
        int lastArea;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            RunAutoAttachers();
            Attach();
        }

        /// <summary>
        /// 自動で付くしくみ（[RuntimeInitializeOnLoadMethod(AfterSceneLoad)] の付いた AutoAttach など）は、
        /// Unity では最初のシーンを読みこんだ時に1回しか呼ばれない。シーンを切り替えた時も付くように、ここで呼び直す。
        /// （どれも「もう付いていたら何もしない」作りなので、何回呼んでもよい）
        /// </summary>
        static void RunAutoAttachers()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var type in typeof(AreaSceneFlow).Assembly.GetTypes())
            {
                if (type == typeof(AreaSceneFlow) || type.Namespace != "MedalPusher") continue;
                foreach (var method in type.GetMethods(flags))
                {
                    var attr = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                    if (attr == null || attr.loadType != RuntimeInitializeLoadType.AfterSceneLoad) continue;
                    if (method.GetParameters().Length != 0 || method.ContainsGenericParameters) continue;
                    try { method.Invoke(null, null); }
                    catch (System.Exception e) { Debug.LogWarning($"AreaSceneFlow: {type.Name}.{method.Name} を呼べませんでした：{e.InnerException?.Message ?? e.Message}"); }
                }
            }
        }

        static void Attach()
        {
            if (FindFirstObjectByType<AreaSceneFlow>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<AreaSceneFlow>();
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null)
            {
                enabled = false;
                return;
            }
            Time.timeScale = 1f;            // 前のシーンのムービーで止まっていたら戻す
            AudioListener.volume = 1f;
            lastArea = game.AreaIndex;
            game.StateChanged += OnStateChanged;
            StartCoroutine(ApplyCarryLater());
        }

        void OnDestroy()
        {
            if (game != null) game.StateChanged -= OnStateChanged;
        }

        // ================================================================
        // 次のエリアへ進んだ時
        // ================================================================

        void OnStateChanged(GameState state)
        {
            int area = game.AreaIndex;
            bool advanced = area > lastArea;
            lastArea = area;
            if (!advanced || transitioning) return;
            string scene = SceneFor(area);
            if (string.IsNullOrEmpty(scene) || scene == SceneManager.GetActiveScene().name) return;   // 同じシーンのまま進む
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogWarning($"AreaSceneFlow: シーン「{scene}」が Build Settings に入っていないので、切り替えません。File → Build Settings の Scenes In Build に入れてください。", this);
                return;
            }

            Carry.pending = true;
            Carry.area = area;
            Carry.roundNumber = game.RoundNumber;
            Carry.specialPoints = game.SpecialPoints;
            StartTransition(scene, area);
        }

        string SceneFor(int area) => areaScenes != null && area >= 0 && area < areaScenes.Length ? areaScenes[area] : null;

        // ================================================================
        // 新しいシーンで、引きついだ物を入れる
        // ================================================================

        /// <summary>
        /// シーンの最初のフレームの終わりに入れる（セーブの読みこみ GameManager などの後、エリアのムービーが
        /// 1フレーム後に「今どのエリアか」を確かめる前）。
        /// </summary>
        IEnumerator ApplyCarryLater()
        {
            yield return new WaitForEndOfFrame();
            if (Carry.pending)
            {
                Carry.pending = false;
                Apply(Carry.area, Carry.roundNumber, Carry.specialPoints);
            }
            else
            {
                // このシーンを直接再生した時：シーンのエリアから始める（確かめる用）
                int area = System.Array.IndexOf(areaScenes ?? new string[0], SceneManager.GetActiveScene().name);
                if (area > 0 && game.AreaIndex != area) Apply(area, game.RoundNumber, game.SpecialPoints);
            }
            lastArea = game.AreaIndex;
        }

        void Apply(int area, int roundNumber, int specialPoints)
        {
            game.LoadProgress(area, 0, game.rules.startMedals, 0);   // エリアの1ラウンド目から。所持メダルははじめの枚数、ポイントは 0
            SetPrivate("RoundNumber", Mathf.Max(1, roundNumber));
            SetPrivate("SpecialPoints", specialPoints);
            Debug.Log($"AreaSceneFlow: エリア {area + 1} を始めました（特殊ポイント {specialPoints} を引きつぎ）。");
        }

        /// <summary>MedalPusherGame の、外から書きかえられない数を書きかえる。</summary>
        void SetPrivate(string property, int value)
        {
            var p = typeof(MedalPusherGame).GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var setter = p != null ? p.GetSetMethod(true) : null;
            if (setter == null)
            {
                Debug.LogWarning($"AreaSceneFlow: MedalPusherGame の {property} を書きかえられませんでした。", this);
                return;
            }
            setter.Invoke(game, new object[] { value });
        }

        // ================================================================
        // 切り替えの演出（シーンをまたいで残る）
        // ================================================================

        void StartTransition(string scene, int area)
        {
            transitioning = true;
            var go = new GameObject("AreaTransition");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<TransitionRunner>();
            runner.Run(scene, area, area < transitionMovies.Length ? transitionMovies[area] : null, fadeSeconds, placeholderSeconds);
        }

        /// <summary>暗転 → ムービー（無ければ仮の文字）→ シーンの読みこみ → 明るく戻す、を受けもつ（シーンをまたいで動く）。</summary>
        class TransitionRunner : MonoBehaviour
        {
            CanvasGroup group;
            Image black;
            Text label;
            RawImage movieImage;

            public void Run(string scene, int area, VideoClip movie, float fade, float hold)
            {
                Build(area);
                StartCoroutine(Flow(scene, movie, fade, hold));
            }

            void Build(int area)
            {
                var canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000;   // いちばん上
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                gameObject.AddComponent<GraphicRaycaster>();   // 切り替え中は、うしろの UI を押せないように
                group = gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;

                black = Full("Black").gameObject.AddComponent<Image>();
                black.color = Color.black;
                movieImage = Full("Movie").gameObject.AddComponent<RawImage>();
                movieImage.color = Color.white;
                movieImage.enabled = false;

                var labelRt = Full("Label");
                label = labelRt.gameObject.AddComponent<Text>();
                var font = Resources.Load<Font>(MonitorDisplay.FontFolder + "/Display");
                if (font == null) font = Resources.Load<Font>(MonitorDisplay.FontFolder + "/Body");
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.font = font;
                label.fontSize = 120;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.text = $"AREA {area + 1}";
                label.raycastTarget = false;
                label.enabled = false;
            }

            RectTransform Full(string name)
            {
                var go = new GameObject(name, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                return rt;
            }

            IEnumerator Flow(string scene, VideoClip movie, float fade, float hold)
            {
                // 1. 暗くして、音を小さく（時間が止まっていても動くように、unscaled の時間で）
                float startVolume = AudioListener.volume;
                for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
                {
                    float k = t / fade;
                    group.alpha = k;
                    AudioListener.volume = Mathf.Lerp(startVolume, 0f, k);
                    yield return null;
                }
                group.alpha = 1f;
                AudioListener.volume = 0f;

                // 2. ムービー（無ければ仮の文字）。読みこみは裏で先に始めておく
                var load = SceneManager.LoadSceneAsync(scene);
                load.allowSceneActivation = false;
                if (movie != null) yield return PlayMovie(movie);
                else
                {
                    label.enabled = true;
                    for (float t = 0f; t < hold; t += Time.unscaledDeltaTime) yield return null;
                    label.enabled = false;
                }

                // 3. シーンを切り替える
                AudioListener.volume = 1f;
                load.allowSceneActivation = true;
                while (!load.isDone) yield return null;
                Time.timeScale = 1f;
                yield return null;
                yield return null;   // 新しいシーンで、引きついだ物が入るのを待つ

                // 4. 明るく戻す
                for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
                {
                    group.alpha = 1f - t / fade;
                    yield return null;
                }
                transitioning = false;
                Destroy(gameObject);
            }

            IEnumerator PlayMovie(VideoClip clip)
            {
                var rt = new RenderTexture(Mathf.Max(16, (int)clip.width), Mathf.Max(16, (int)clip.height), 0);
                var player = gameObject.AddComponent<VideoPlayer>();
                player.playOnAwake = false;
                player.isLooping = false;
                player.clip = clip;
                player.renderMode = VideoRenderMode.RenderTexture;
                player.targetTexture = rt;
                player.audioOutputMode = VideoAudioOutputMode.Direct;
                player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
                player.Prepare();
                while (!player.isPrepared) yield return null;
                movieImage.texture = rt;
                movieImage.enabled = true;
                AudioListener.volume = 1f;   // ムービーの音は鳴らす
                player.Play();
                bool ended = false;
                player.loopPointReached += _ => ended = true;
                float limit = (float)clip.length + 2f;
                for (float t = 0f; !ended && t < limit; t += Time.unscaledDeltaTime) yield return null;
                AudioListener.volume = 0f;
                player.Stop();
                movieImage.enabled = false;
                rt.Release();
            }

            void OnDestroy()
            {
                transitioning = false;
                AudioListener.volume = 1f;
            }
        }
    }
}
