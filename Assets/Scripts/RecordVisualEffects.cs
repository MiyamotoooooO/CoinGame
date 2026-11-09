using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedalPusher
{
    /// <summary>
    /// レコードを流している間の「盤面・モニターに見えるもの」（RecordData.csv の同じ名前の列）を出す。
    ///
    /// いま出すもの（N001〜N004）：
    ///   ポップス     ：中央の穴に落ちるたび、穴から色とりどりの音符が飛び出し、モニターに「+3」が弾ける
    ///   フォーク     ：端の穴のふちが緑に光る。端の穴に落ちると、ギターの弦が鳴って光がふちを走る
    ///   ブルース     ：盤面の照明が青く沈む。残り投入数が5以下になると、モニターに「LAST 5」からカウントダウン
    ///   シティポップ ：盤面の奥にネオンの街並みが立ち上がり、10秒ごとにビルの窓の明かりが増える（明かりの数 = 今の加算）
    ///
    /// 見た目はすべて仮。演出ができたら、Inspector の「差し替え用」の欄に Prefab や音を入れると、そちらが使われる。
    /// （空の欄は仮の見た目のまま）
    ///
    /// 置き方：シーンに置かなくても、RecordEffectSystem があれば再生時に自動で同じ GameObject に付く。
    /// Prefab を設定したい時は、RecordEffectSystem の GameObject にこのコンポーネントを付けておく。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecordVisualEffects : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        public RecordEffectSystem effects;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.7f;

        [Header("N001 ポップス")]
        public PopsVisual pops = new PopsVisual();
        [Header("N002 フォーク")]
        public FolkVisual folk = new FolkVisual();
        [Header("N003 ブルース")]
        public BluesVisual blues = new BluesVisual();
        [Header("N004 シティポップ")]
        public CityPopVisual cityPop = new CityPopVisual();

        // ================================================================
        // 設定
        // ================================================================

        [Serializable]
        public class PopsVisual
        {
            public string recordKey = "Pops";
            [Tooltip("差し替え用：中央の穴に落ちるたびに、穴の位置に出す演出の Prefab（空なら仮の音符のパーティクル）")]
            public GameObject dropEffectPrefab;
            [Tooltip("差し替え用の Prefab を消すまでの秒数")]
            public float prefabLifetime = 2f;
            [Tooltip("差し替え用：落ちた時に鳴らす音（空なら鳴らさない）")]
            public AudioClip dropSound;

            [Header("仮の音符")]
            [Range(1, 30)] public int noteCount = 8;
            [Tooltip("飛び出す速さ（メダルの直径に対する倍率）")]
            public float noteSpeed = 9f;
            [Tooltip("音符の大きさ（メダルの直径に対する倍率）")]
            public float noteSize = 0.8f;
            public Color[] noteColors =
            {
                new Color(1f, 0.35f, 0.35f), new Color(1f, 0.8f, 0.2f), new Color(0.4f, 0.9f, 0.4f),
                new Color(0.35f, 0.7f, 1f), new Color(0.85f, 0.45f, 1f)
            };

            [Header("モニター")]
            [Tooltip("モニターに「+3」を出す（数字はレコードの値1）")]
            public bool monitorPop = true;
        }

        [Serializable]
        public class FolkVisual
        {
            public string recordKey = "Folk";
            [Tooltip("差し替え用：流している間、左右の端の穴のふちに置く Prefab（空なら仮の光る棒）")]
            public GameObject rimPrefab;
            [Tooltip("差し替え用：端の穴に落ちた時に、その側のふちに出す Prefab（空なら仮の走る光）")]
            public GameObject runPrefab;
            [Tooltip("差し替え用の Prefab（走る光）を消すまでの秒数")]
            public float runPrefabLifetime = 1.5f;
            [Tooltip("差し替え用：弦の音（複数入れるとランダム。空なら仮の音をその場で作る）")]
            public AudioClip[] strumSounds = new AudioClip[0];

            [Header("仮の見た目")]
            public Color rimColor = new Color(0.25f, 1f, 0.35f);
            [Range(0f, 8f)] public float rimGlow = 2f;
            [Tooltip("光が端から中央側までふちを走る秒数")]
            public float runSeconds = 0.35f;
        }

        [Serializable]
        public class BluesVisual
        {
            public string recordKey = "Blues";
            [Header("照明")]
            [Tooltip("照明をこの色に寄せる")]
            public Color lightColor = new Color(0.35f, 0.5f, 1f);
            [Tooltip("照明の明るさの倍率（小さいほど沈む）")]
            [Range(0f, 1f)] public float lightDim = 0.55f;
            [Tooltip("環境光（影の部分の明るさ）にかける色")]
            public Color ambientTint = new Color(0.45f, 0.55f, 1f);
            [Tooltip("照明が切り替わる秒数")]
            public float fadeSeconds = 0.8f;

            [Header("モニターのカウントダウン")]
            [Tooltip("{0} に残り投入数が入る。出し始める数はレコードの値2（5）")]
            public string countdownFormat = "LAST {0}";
            public Color countdownColor = new Color(0.55f, 0.75f, 1f);
            [Tooltip("残りが0の時の色")]
            public Color countdownZeroColor = new Color(1f, 0.4f, 0.4f);
        }

        [Serializable]
        public class CityPopVisual
        {
            public string recordKey = "CityPop";
            [Tooltip("差し替え用：街並みの Prefab（空なら仮の街並み）。" +
                     "子の中で名前が「Window」で始まる物を窓の明かりとして、名前の順に表示・非表示を切り替える")]
            public GameObject skylinePrefab;

            [Header("置き場所（盤面の奥の壁の前、プッシャーの上面の高さが基準）")]
            public Vector3 offset = Vector3.zero;
            [Tooltip("街並みの幅（盤面の幅に対する割合）")]
            [Range(0.2f, 1.2f)] public float widthRatio = 0.9f;
            [Tooltip("一番高いビルの高さ（盤面の幅に対する割合）")]
            [Range(0.02f, 0.5f)] public float heightRatio = 0.12f;
            [Tooltip("立ち上がる・沈む秒数")]
            public float riseSeconds = 0.8f;

            [Header("窓の明かり（明かりの数 = 今の加算）")]
            [Tooltip("加算の上限（窓の数）。RecordEffectSystem の上限と同じにする")]
            public int maxAdd = 12;
            [Tooltip("加算が増える間隔（秒）。RecordEffectSystem と同じにする")]
            public float stepSeconds = 10f;

            [Header("仮の見た目")]
            [Range(3, 12)] public int buildingCount = 7;
            public Color buildingColor = new Color(0.10f, 0.08f, 0.22f);
            public Color[] neonColors = { new Color(1f, 0.3f, 0.75f), new Color(0.3f, 0.9f, 1f), new Color(0.75f, 0.4f, 1f) };
            [Range(0f, 8f)] public float neonGlow = 2.5f;
            public Color windowOnColor = new Color(1f, 0.85f, 0.4f);
            public Color windowOffColor = new Color(0.05f, 0.05f, 0.1f);
            [Range(0f, 8f)] public float windowGlow = 2f;
        }

        // ================================================================
        // 中身
        // ================================================================

        Transform root;
        AudioSource audioSource;
        float D => game.tuning.medalDiameter;   // 大きさの目安

        // ポップス
        ParticleSystem noteParticles;
        Material noteMaterial;

        // フォーク
        readonly GameObject[] folkRims = new GameObject[2];
        readonly Material[] folkRimMaterials = new Material[2];
        readonly float[] folkFlash = new float[2];
        float folkWeight;
        readonly List<Runner> runners = new List<Runner>();
        Material runnerMaterial;
        AudioClip[] placeholderStrums;

        class Runner
        {
            public GameObject go;
            public float fromX, toX, age;
        }

        // ブルース
        class SavedLight
        {
            public Light light;
            public Color color;
            public float intensity;
        }
        readonly List<SavedLight> savedLights = new List<SavedLight>();
        bool lightsSaved;
        Color savedSky, savedEquator, savedGround;
        float bluesWeight;
        bool bannerShown;

        // シティポップ
        Transform skyline;
        readonly List<GameObject> windowsLit = new List<GameObject>();   // 明かり（名前・順番どおり）
        readonly List<Transform> windowPop = new List<Transform>();
        readonly List<float> windowPopTime = new List<float>();
        float skylineRise;
        int skylineLit = -1;

        /// <summary>シーンに無ければ、RecordEffectSystem の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<RecordVisualEffects>() != null) return;
            var system = FindFirstObjectByType<RecordEffectSystem>();
            if (system != null) system.gameObject.AddComponent<RecordVisualEffects>();
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (effects == null) effects = FindFirstObjectByType<RecordEffectSystem>();
            if (game == null || effects == null)
            {
                Debug.LogWarning("RecordVisualEffects: MedalPusherGame か RecordEffectSystem が見つからないため、レコードの演出は出ません。", this);
                enabled = false;
                return;
            }

            root = new GameObject("RecordVisualEffects").transform;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            game.MedalDropped += OnMedalDropped;
        }

        void OnDestroy()
        {
            if (game != null) game.MedalDropped -= OnMedalDropped;
            RestoreLights();
            if (root != null) Destroy(root.gameObject);
        }

        bool IsPlaying(string key) => effects.Current != null && effects.Current.key == key;
        bool IsScoring => game.State == GameState.Playing || game.State == GameState.Grace;

        void OnMedalDropped(Medal medal, DropZoneType type)
        {
            if (!IsScoring || effects.Current == null) return;
            Vector3 p = medal != null ? medal.transform.position : Vector3.zero;

            // ポップスは PopsRecord（コール&レスポンス・ライブ）が受け持つので、PopsRecord がある時は前の仮の演出を出さない
            if (type == DropZoneType.Center && IsPlaying(pops.recordKey) && PopsRecord.Instance == null) PopsDrop(p, effects.Current);
            // フォークは FolkRecord（楽譜すごろく）が受け持つので、FolkRecord がある時は前の仮の演出を出さない
            if (type == DropZoneType.Side && IsPlaying(folk.recordKey) && FolkRecord.Instance == null) FolkDrop(p);
        }

        void Update()
        {
            if (game == null || effects == null) return;
            UpdateFolk();
            UpdateBlues();
            UpdateCityPop();
        }

        void PlaySound(AudioClip clip)
        {
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip, volume);
        }

        // ================================================================
        // ポップス
        // ================================================================

        void PopsDrop(Vector3 medalPos, RecordData record)
        {
            // 中央の穴のふち（盤面の手前の端）の、落ちた所の真上から出す
            float half = Mathf.Clamp(game.layout.centerHoleWidth, 0.1f, game.layout.boardWidth) * 0.5f;
            var pos = new Vector3(Mathf.Clamp(medalPos.x, -half, half), D * 0.3f, game.layout.floorFrontZ);

            if (pops.dropEffectPrefab != null)
            {
                var fx = Instantiate(pops.dropEffectPrefab, pos, Quaternion.identity, root);
                Destroy(fx, Mathf.Max(0.1f, pops.prefabLifetime));
            }
            else
            {
                EnsureNoteParticles();
                noteParticles.transform.position = pos;
                noteParticles.Emit(pops.noteCount);
            }
            PlaySound(pops.dropSound);

            if (pops.monitorPop && game.Monitor != null)
            {
                var colors = pops.noteColors;
                var c = colors != null && colors.Length > 0 ? colors[UnityEngine.Random.Range(0, colors.Length)] : Color.yellow;
                game.Monitor.PopText($"+{Mathf.RoundToInt(record.value1)}", c);
            }
        }

        /// <summary>仮の音符：コードで作るパーティクル。</summary>
        void EnsureNoteParticles()
        {
            if (noteParticles != null) return;
            var go = new GameObject("Pops_Notes");
            go.transform.SetParent(root, false);
            // 上向き・少し手前（カメラ側）に向けて飛ばす
            go.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1f, -0.45f));

            noteParticles = go.AddComponent<ParticleSystem>();
            noteParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = noteParticles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(pops.noteSpeed * D * 0.6f, pops.noteSpeed * D);
            main.startSize = new ParticleSystem.MinMaxCurve(pops.noteSize * D * 0.8f, pops.noteSize * D * 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            main.gravityModifier = 0.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            main.startColor = new ParticleSystem.MinMaxGradient(MakeRandomColors(pops.noteColors)) { mode = ParticleSystemGradientMode.RandomColor };

            var emission = noteParticles.emission;
            emission.rateOverTime = 0f;   // Emit() で出す時だけ

            var shape = noteParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;
            shape.radius = D * 0.4f;

            var fade = noteParticles.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            noteMaterial = new Material(FindTransparentShader()) { mainTexture = MakeNoteTexture() };
            renderer.sharedMaterial = noteMaterial;
        }

        static Gradient MakeRandomColors(Color[] colors)
        {
            if (colors == null || colors.Length == 0) colors = new[] { Color.white };
            int n = Mathf.Min(colors.Length, 8);   // Gradient の色は8つまで
            var keys = new GradientColorKey[n];
            for (int i = 0; i < n; i++) keys[i] = new GradientColorKey(colors[i], n == 1 ? 0f : i / (float)(n - 1));
            var g = new Gradient { mode = GradientMode.Fixed };
            g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        static Shader FindTransparentShader()
        {
            return Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Unlit/Transparent");
        }

        /// <summary>仮の音符（♪）の絵。白で描き、色はパーティクルの色で付ける。</summary>
        static Texture2D MakeNoteTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float x = (i + 0.5f) / size, y = (j + 0.5f) / size;
                    // 玉（少し傾いた楕円）
                    float hx = x - 0.38f, hy = y - 0.26f;
                    float rx = hx * 0.94f + hy * 0.34f, ry = -hx * 0.34f + hy * 0.94f;
                    bool head = rx * rx / (0.2f * 0.2f) + ry * ry / (0.14f * 0.14f) <= 1f;
                    // 棒
                    bool stem = x >= 0.53f && x <= 0.6f && y >= 0.28f && y <= 0.9f;
                    // 旗
                    float fy = 0.9f - (x - 0.6f) * 0.9f;
                    bool flag = x >= 0.6f && x <= 0.82f && y <= fy && y >= fy - 0.14f;
                    px[j * size + i] = head || stem || flag ? Color.white : new Color(1f, 1f, 1f, 0f);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ================================================================
        // フォーク
        // ================================================================

        /// <summary>端の穴の、ふちの中心（side = 0 が左、1 が右）と長さ。</summary>
        void SideRim(int side, out Vector3 center, out float length, out float innerX, out float outerX)
        {
            float half = game.layout.boardWidth * 0.5f;
            float inner = Mathf.Clamp(game.layout.centerHoleWidth, 0.1f, game.layout.boardWidth) * 0.5f;
            float sign = side == 0 ? -1f : 1f;
            innerX = sign * inner;
            outerX = sign * half;
            length = half - inner;
            center = new Vector3(sign * (half + inner) * 0.5f, D * 0.04f, game.layout.floorFrontZ - D * 0.06f);
        }

        void UpdateFolk()
        {
            bool on = IsPlaying(folk.recordKey) && FolkRecord.Instance == null;
            folkWeight = Mathf.MoveTowards(folkWeight, on ? 1f : 0f, Time.deltaTime * 4f);
            if (folkWeight > 0f) EnsureFolkRims();

            for (int s = 0; s < 2; s++)
            {
                folkFlash[s] = Mathf.MoveTowards(folkFlash[s], 0f, Time.deltaTime * 3f);
                if (folkRims[s] == null) continue;
                folkRims[s].SetActive(folkWeight > 0f);
                if (folkRimMaterials[s] != null)   // 仮の棒：ゆっくり脈打ち、落ちた時は強く光る
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
                    float glow = folk.rimGlow * (pulse + folkFlash[s] * 2f) * folkWeight;
                    folkRimMaterials[s].color = Color.Lerp(Color.black, folk.rimColor, folkWeight);
                    folkRimMaterials[s].SetColor("_EmissionColor", folk.rimColor * glow);
                }
            }

            for (int i = runners.Count - 1; i >= 0; i--)
            {
                var r = runners[i];
                r.age += Time.deltaTime;
                float t = r.age / Mathf.Max(0.05f, folk.runSeconds);
                if (t >= 1f)
                {
                    Destroy(r.go);
                    runners.RemoveAt(i);
                    continue;
                }
                var p = r.go.transform.position;
                p.x = Mathf.Lerp(r.fromX, r.toX, 1f - (1f - t) * (1f - t));
                r.go.transform.position = p;
                r.go.transform.localScale = new Vector3(D * 0.6f * (1f - t * 0.5f), D * 0.1f, D * 0.14f);
            }
        }

        void EnsureFolkRims()
        {
            for (int s = 0; s < 2; s++)
            {
                if (folkRims[s] != null) continue;
                SideRim(s, out var center, out float length, out _, out _);
                if (folk.rimPrefab != null)
                {
                    folkRims[s] = Instantiate(folk.rimPrefab, center, Quaternion.identity, root);
                }
                else
                {
                    folkRims[s] = MakeCube($"Folk_Rim_{(s == 0 ? "L" : "R")}", center, new Vector3(length, D * 0.06f, D * 0.1f));
                    folkRimMaterials[s] = MakeGlowMaterial(folk.rimColor, folk.rimGlow);
                    folkRims[s].GetComponent<Renderer>().sharedMaterial = folkRimMaterials[s];
                }
            }
        }

        void FolkDrop(Vector3 medalPos)
        {
            int s = medalPos.x < 0f ? 0 : 1;
            folkFlash[s] = 1f;
            SideRim(s, out var center, out _, out float innerX, out float outerX);

            if (folk.runPrefab != null)
            {
                var fx = Instantiate(folk.runPrefab, center, Quaternion.identity, root);
                Destroy(fx, Mathf.Max(0.1f, folk.runPrefabLifetime));
            }
            else
            {
                // 仮の走る光：外側の端から中央側の端までふちを走る
                if (runnerMaterial == null) runnerMaterial = MakeGlowMaterial(Color.white, 4f);
                var go = MakeCube("Folk_Run", new Vector3(outerX, center.y + D * 0.02f, center.z), Vector3.one * D * 0.1f);
                go.GetComponent<Renderer>().sharedMaterial = runnerMaterial;
                runners.Add(new Runner { go = go, fromX = outerX, toX = innerX });
            }

            if (folk.strumSounds != null && folk.strumSounds.Length > 0)
            {
                PlaySound(folk.strumSounds[UnityEngine.Random.Range(0, folk.strumSounds.Length)]);
            }
            else
            {
                if (placeholderStrums == null)
                {
                    // 仮の弦の音（E マイナーの和音の音から選ぶ）
                    float[] freqs = { 164.81f, 196.00f, 246.94f, 329.63f };
                    placeholderStrums = new AudioClip[freqs.Length];
                    for (int i = 0; i < freqs.Length; i++) placeholderStrums[i] = MakePluck(freqs[i], i);
                }
                PlaySound(placeholderStrums[UnityEngine.Random.Range(0, placeholderStrums.Length)]);
            }
        }

        /// <summary>弦をはじいた音を作る（Karplus-Strong）。</summary>
        static AudioClip MakePluck(float frequency, int seed)
        {
            const int rate = 44100;
            int length = (int)(rate * 1.2f);
            int period = Mathf.Max(2, Mathf.RoundToInt(rate / frequency));
            var rng = new System.Random(1234 + seed);
            var buffer = new float[period];
            for (int i = 0; i < period; i++) buffer[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

            var data = new float[length];
            int index = 0;
            for (int i = 0; i < length; i++)
            {
                int next = (index + 1) % period;
                float v = buffer[index];
                buffer[index] = 0.996f * 0.5f * (buffer[index] + buffer[next]);
                float fadeOut = i > length * 0.85f ? (length - i) / (length * 0.15f) : 1f;
                data[i] = v * 0.45f * fadeOut;
                index = next;
            }
            var clip = AudioClip.Create($"Pluck_{frequency:0}", length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ================================================================
        // ブルース
        // ================================================================

        void UpdateBlues()
        {
            RecordData r = effects.Current;
            bool on = r != null && r.key == blues.recordKey;

            // 照明
            bluesWeight = Mathf.MoveTowards(bluesWeight, on ? 1f : 0f, Time.deltaTime / Mathf.Max(0.05f, blues.fadeSeconds));
            if (bluesWeight > 0f)
            {
                if (!lightsSaved) SaveLights();
                ApplyLights(bluesWeight);
            }
            else if (lightsSaved)
            {
                RestoreLights();
            }

            // モニターのカウントダウン（残り投入数が値2以下の時）
            bool showCountdown = false;
            // BluesRecord（ソロ）がある時は、カウントダウンを BluesRecord の画面に出すので、ここでは出さない（照明はそのまま）
            if (on && IsScoring && game.Monitor != null && BluesRecord.Instance == null)
            {
                int left = Mathf.Max(0, game.InsertLimit - game.InsertedThisRound);
                int from = r.value2 > 0f ? Mathf.RoundToInt(r.value2) : 5;
                if (left <= from)
                {
                    showCountdown = true;
                    game.Monitor.SetBanner(string.Format(blues.countdownFormat, left), left > 0 ? blues.countdownColor : blues.countdownZeroColor);
                }
            }
            if (!showCountdown && bannerShown && game.Monitor != null) game.Monitor.ClearBanner();
            bannerShown = showCountdown;
        }

        /// <summary>今の照明の色と明るさを覚えておく（元に戻すため）。</summary>
        void SaveLights()
        {
            savedLights.Clear();
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                savedLights.Add(new SavedLight { light = light, color = light.color, intensity = light.intensity });
            savedSky = RenderSettings.ambientSkyColor;
            savedEquator = RenderSettings.ambientEquatorColor;
            savedGround = RenderSettings.ambientGroundColor;
            lightsSaved = true;
        }

        void ApplyLights(float w)
        {
            foreach (var s in savedLights)
            {
                if (s.light == null) continue;   // CabinetLighting が作り直した時など
                s.light.color = Color.Lerp(s.color, blues.lightColor, w);
                s.light.intensity = s.intensity * Mathf.Lerp(1f, blues.lightDim, w);
            }
            Color tint = blues.ambientTint * blues.lightDim;
            RenderSettings.ambientSkyColor = Color.Lerp(savedSky, savedSky * tint, w);
            RenderSettings.ambientEquatorColor = Color.Lerp(savedEquator, savedEquator * tint, w);
            RenderSettings.ambientGroundColor = Color.Lerp(savedGround, savedGround * tint, w);
        }

        void RestoreLights()
        {
            if (!lightsSaved) return;
            foreach (var s in savedLights)
            {
                if (s.light == null) continue;
                s.light.color = s.color;
                s.light.intensity = s.intensity;
            }
            RenderSettings.ambientSkyColor = savedSky;
            RenderSettings.ambientEquatorColor = savedEquator;
            RenderSettings.ambientGroundColor = savedGround;
            savedLights.Clear();
            lightsSaved = false;
        }

        // ================================================================
        // シティポップ
        // ================================================================

        void UpdateCityPop()
        {
            RecordData r = effects.Current;
            bool on = r != null && r.key == cityPop.recordKey;

            skylineRise = Mathf.MoveTowards(skylineRise, on ? 1f : 0f, Time.deltaTime / Mathf.Max(0.05f, cityPop.riseSeconds));
            if (skylineRise <= 0f)
            {
                if (skyline != null) skyline.gameObject.SetActive(false);
                skylineLit = -1;
                return;
            }

            EnsureSkyline();
            skyline.gameObject.SetActive(true);
            float e = 1f - (1f - skylineRise) * (1f - skylineRise);
            skyline.localScale = new Vector3(1f, Mathf.Max(0.001f, e), 1f);   // 下から立ち上がる

            // 明かりの数 = 今の加算（RecordEffectSystem の シティポップ と同じ計算）
            if (on)
            {
                float elapsed = effects.CurrentDuration - effects.Remaining;
                int add = Mathf.Min(cityPop.maxAdd,
                    Mathf.RoundToInt(r.value1 + r.value2 * Mathf.Floor(elapsed / Mathf.Max(0.1f, cityPop.stepSeconds))));
                SetLitWindows(add);
            }

            // 明かりが付いた窓を少しはずませる
            for (int i = windowPop.Count - 1; i >= 0; i--)
            {
                windowPopTime[i] += Time.deltaTime;
                float t = windowPopTime[i] / 0.4f;
                var w = windowPop[i];
                if (w == null || t >= 1f)
                {
                    if (w != null) w.localScale = Vector3.one;
                    windowPop.RemoveAt(i);
                    windowPopTime.RemoveAt(i);
                    continue;
                }
                float s = 1f + 0.6f * (1f - t);
                w.localScale = new Vector3(s, s, 1f);
            }
        }

        void SetLitWindows(int count)
        {
            count = Mathf.Clamp(count, 0, windowsLit.Count);
            if (count == skylineLit) return;
            for (int i = 0; i < windowsLit.Count; i++)
            {
                bool lit = i < count;
                if (windowsLit[i].activeSelf == lit) continue;
                windowsLit[i].SetActive(lit);
                if (lit && skylineLit >= 0)   // 増えた窓（流し始めの分ははずませない）
                {
                    windowPop.Add(windowsLit[i].transform);
                    windowPopTime.Add(0f);
                }
            }
            skylineLit = count;
        }

        void EnsureSkyline()
        {
            if (skyline != null) return;
            var layout = game.layout;
            var anchor = new Vector3(0f, layout.pusherHeight, game.BackWallFrontZ - D * 0.05f) + cityPop.offset;

            skyline = new GameObject("CityPop_Skyline").transform;
            skyline.SetParent(root, false);
            skyline.position = anchor;
            windowsLit.Clear();

            if (cityPop.skylinePrefab != null)
            {
                var inst = Instantiate(cityPop.skylinePrefab, skyline);
                inst.transform.localPosition = Vector3.zero;
                var found = new List<Transform>();
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    if (t != inst.transform && t.name.StartsWith("Window")) found.Add(t);
                found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                foreach (var t in found) windowsLit.Add(t.gameObject);
                return;
            }

            BuildPlaceholderSkyline(layout);
        }

        /// <summary>仮の街並み：高さの違うビル、屋上のネオン、窓（明かりは maxAdd 個）。</summary>
        void BuildPlaceholderSkyline(BoardLayout layout)
        {
            var rng = new System.Random(4);
            float width = layout.boardWidth * cityPop.widthRatio;
            float maxH = layout.boardWidth * cityPop.heightRatio;
            float depth = D * 0.3f;
            int n = Mathf.Max(1, cityPop.buildingCount);

            var bodyMat = MakeGlowMaterial(cityPop.buildingColor, 0.3f);
            var offMat = MakeGlowMaterial(cityPop.windowOffColor, 0f);
            var onMat = MakeGlowMaterial(cityPop.windowOnColor, cityPop.windowGlow);

            // ビルの幅を少しずつ変える
            var widths = new float[n];
            float sum = 0f;
            for (int i = 0; i < n; i++) { widths[i] = 0.7f + (float)rng.NextDouble() * 0.6f; sum += widths[i]; }

            // 窓を各ビルに配る（高いビルほど多め）
            var heights = new float[n];
            for (int i = 0; i < n; i++) heights[i] = maxH * (0.45f + (float)rng.NextDouble() * 0.55f);
            var perBuilding = new int[n];
            for (int k = 0; k < cityPop.maxAdd; k++)
            {
                int best = 0;
                float bestScore = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    float score = heights[i] / (perBuilding[i] + 1);
                    if (score > bestScore) { bestScore = score; best = i; }
                }
                perBuilding[best]++;
            }

            var allWindows = new List<GameObject>();
            float x = -width * 0.5f;
            float gap = width * 0.01f;
            for (int i = 0; i < n; i++)
            {
                float w = width * widths[i] / sum;
                float h = heights[i];
                float cx = x + w * 0.5f;
                var body = MakeCube($"Building_{i + 1}", Vector3.zero, new Vector3(w - gap, h, depth));
                body.transform.SetParent(skyline, false);
                body.transform.localPosition = new Vector3(cx, h * 0.5f, 0f);
                body.GetComponent<Renderer>().sharedMaterial = bodyMat;

                // 屋上のネオン
                var neonColor = cityPop.neonColors != null && cityPop.neonColors.Length > 0
                    ? cityPop.neonColors[i % cityPop.neonColors.Length] : Color.magenta;
                var neon = MakeCube($"Neon_{i + 1}", Vector3.zero, new Vector3(w - gap, D * 0.06f, depth * 1.05f));
                neon.transform.SetParent(skyline, false);
                neon.transform.localPosition = new Vector3(cx, h, 0f);
                neon.GetComponent<Renderer>().sharedMaterial = MakeGlowMaterial(neonColor, cityPop.neonGlow);

                // 窓（消えている窓の上に、明かりを重ねる）
                int count = perBuilding[i];
                int cols = Mathf.Max(1, Mathf.Min(count, Mathf.FloorToInt((w - gap) / (D * 0.35f))));
                int rows = Mathf.CeilToInt(count / (float)cols);
                float ws = Mathf.Min(D * 0.22f, (w - gap) / (cols * 1.6f), h / (rows * 1.8f + 1f));
                for (int k = 0; k < count; k++)
                {
                    int c = k % cols, row = k / cols;
                    float wx = cx + (c - (cols - 1) * 0.5f) * ws * 1.6f;
                    float wy = h - ws * 1.6f * (row + 1);
                    var off = MakeCube("WindowOff", Vector3.zero, new Vector3(ws, ws, D * 0.02f));
                    off.transform.SetParent(skyline, false);
                    off.transform.localPosition = new Vector3(wx, wy, -depth * 0.5f - D * 0.01f);
                    off.GetComponent<Renderer>().sharedMaterial = offMat;

                    var on = MakeCube("Window", Vector3.zero, new Vector3(ws, ws, D * 0.02f));
                    on.transform.SetParent(off.transform, true);
                    on.transform.localPosition = new Vector3(0f, 0f, -0.6f);
                    on.GetComponent<Renderer>().sharedMaterial = onMat;
                    on.SetActive(false);
                    allWindows.Add(on);
                }
                x += w;
            }

            // 明かりの付く順番はばらばらにする
            for (int i = allWindows.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (allWindows[i], allWindows[j]) = (allWindows[j], allWindows[i]);
            }
            windowsLit.AddRange(allWindows);
        }

        // ================================================================
        // 小さな道具
        // ================================================================

        GameObject MakeCube(string name, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = size;
            Destroy(go.GetComponent<Collider>());   // 見た目だけ。メダルには当たらない
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static Material MakeGlowMaterial(Color color, float glow)
        {
            var m = ProtoAssets.MakeMaterial(color, 0f, 0.5f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * glow);
            return m;
        }
    }
}
