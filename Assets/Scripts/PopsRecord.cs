using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N001 ポップス「コール&amp;レスポンス・ライブ」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：中央の穴を左・中・右の3レーンに分け、赤・黄・青のスポットライトを当てる。
    ///   手前のふちに、レーンの色の帯を出す。コールされたレーンは明るく点滅する。
    ///   盛り上がると、左右から紙吹雪のキャノンが飛ぶ
    /// ・モニター：ライブのステージ。アイドル（仮の絵）が「次は 赤！」と色をコールする。
    ///   上に盛り上がりメーター、下に3つのレーンと、ペンライトの客席
    /// ・ルール：
    ///   中央の穴の、コールの色のレーンに落ちると +hitBonus pt（ほかのレーンは +missBonus pt）。端の穴は 0pt のまま
    ///   コールは callSeconds 秒ごとか、成功するたびに変わる
    ///   成功でメーターが大きく、ほかのレーンでも少し上がる（何も入らないと少しずつ下がる）
    ///   メーターが満タンで「アンコール！」：encoreSeconds 秒のあいだ、どのレーンも成功あつかいで、
    ///   ポイントが ×encoreMultiplier（基本のポイントにも掛かる）
    /// ・ポイントが入るのは、ラウンドのプレイ中（判定待ちを含む）だけ
    ///
    /// 見た目と音はコードで作る仮のもの。Inspector の「差し替え用」に Prefab・画像・音を入れると、そちらを使う。
    /// モニターのアイドルは、idolPrefab の3Dモデルを専用のカメラで写して出す（空ならエディタでは Mascot_Neon）。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。
    /// CSV の「効果の型」は Scripted にしておく（前の効果と二重にならないように）。
    /// </summary>
    [DisallowMultipleComponent]
    public class PopsRecord : MonoBehaviour
    {
        /// <summary>シーンにあるポップスのスクリプト（RecordVisualEffects が、前の仮の演出を止めるのに使う）</summary>
        public static PopsRecord Instance { get; private set; }

        [Header("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        public RecordEffectSystem effects;

        [Header("レコード")]
        [Tooltip("このスクリプトが受け持つレコードのキー（英名）")]
        public string recordKey = "Pops";

        [Header("コール&レスポンス")]
        [Tooltip("コールの色のレーンに落ちた時に、上乗せするポイント")]
        [Range(0, 100)] public int hitBonus = 15;
        [Tooltip("ほかのレーンに落ちた時に、上乗せするポイント")]
        [Range(0, 100)] public int missBonus = 3;
        [Tooltip("コールが変わるまでの秒数（成功しても変わる）")]
        [Range(1f, 20f)] public float callSeconds = 5f;

        [Header("盛り上がりメーター（100 で満タン）")]
        [Tooltip("成功した時に上がる量")]
        [Range(0f, 100f)] public float meterOnHit = 18f;
        [Tooltip("ほかのレーンに落ちた時に上がる量")]
        [Range(0f, 100f)] public float meterOnMiss = 4f;
        [Tooltip("1秒ごとに下がる量")]
        [Range(0f, 20f)] public float meterDecay = 1f;

        [Header("アンコール（メーター満タン）")]
        [Range(1f, 60f)] public float encoreSeconds = 10f;
        [Tooltip("アンコール中のポイントの倍率")]
        [Range(1f, 5f)] public float encoreMultiplier = 2f;

        [Header("レーン（左・中・右）")]
        public Color[] laneColors =
        {
            new Color32(0xff, 0x4d, 0x6d, 0xff),
            new Color32(0xff, 0xd2, 0x3f, 0xff),
            new Color32(0x38, 0xa0, 0xff, 0xff),
        };
        public string[] laneNames = { "赤", "黄", "青" };

        [Header("盤面のスポットライト")]
        [Range(0f, 10f)] public float spotIntensity = 4f;
        [Range(10f, 80f)] public float spotAngle = 34f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        [Tooltip("コール成功の音")]
        public AudioClip hitSound;
        [Tooltip("ほかのレーンに落ちた音")]
        public AudioClip missSound;
        [Tooltip("コールが変わった音（アイドルの声など）")]
        public AudioClip callSound;
        [Tooltip("アンコール！の音")]
        public AudioClip encoreSound;
        [Tooltip("歓声（成功した時に小さく、アンコールで大きく鳴る）")]
        public AudioClip cheerSound;
        [Tooltip("紙吹雪のキャノンの Prefab（左右に置いて、撃つたびに出す。数秒で消す）")]
        public GameObject confettiPrefab;
        [Tooltip("スポットライト1つの Prefab（レーンの数だけ置く。色と明るさはこのスクリプトが付ける）")]
        public GameObject spotlightPrefab;
        [Tooltip("モニターのアイドルの3Dモデル（Prefab）。空の時、エディタでは Mascot_Neon を自動で使う。\nビルドで使う時は、ここに入れておく")]
        public GameObject idolPrefab;
        [Tooltip("モニターのアイドルの絵（3Dモデルが無い時に使う。これも空なら仮の絵）")]
        public Sprite idolSprite;

        [Header("アイドルの3Dモデルの写し方")]
        [Tooltip("アイドルのモデルを写す専用のレイヤー（ほかで使っていない番号。ショップは 27、前に出す演出は 28）")]
        [Range(8, 31)] public int idolLayer = 26;
        [Tooltip("カメラの画角（小さいほど大きく写る）")]
        [Range(10f, 60f)] public float idolCameraFov = 26f;
        [Tooltip("左右にゆっくり振り向く角度")]
        [Range(0f, 60f)] public float idolSwayAngle = 20f;
        [Tooltip("モデルを照らす明るさ")]
        [Range(0f, 3f)] public float idolLightIntensity = 1.1f;

        /// <summary>idolPrefab が空の時に、エディタで使うモデル</summary>
        const string DefaultIdolPath = "Assets/Prefab/Charactor/Mascot_Neon.prefab";

        const int LaneCount = 3;

        // ---- 状態 ----
        bool active;
        int call;                // コールしているレーン
        float callTimer;
        float meter;             // 0〜100
        float encoreTimer;
        bool halfFired;          // メーター半分のキャノンを撃ったか
        float callPulse;         // コールが変わった時のはずみ
        readonly float[] laneFlash = new float[LaneCount];
        readonly System.Random rng = new System.Random();
        bool Encore => encoreTimer > 0f;

        // ---- 盤面 ----
        Transform stageRoot;
        readonly List<Light> spots = new List<Light>();
        readonly List<Material> beamMaterials = new List<Material>();
        readonly List<Material> barMaterials = new List<Material>();
        ParticleSystem confettiLeft, confettiRight, burst;
        Material particleMaterial;
        float stageShown;
        float D => game.tuning.medalDiameter;

        // ---- 音 ----
        AudioSource audioSource;
        AudioClip madeHit, madeMiss, madeCall, madeEncore, madeCheer;

        // ---- モニター ----
        MonitorDisplay monitor;
        RectTransform screen, idol, bubble;
        Image meterFill, bubbleTimer, idolDress, idolAura;
        Text meterText, callText, callSmall, statusText;
        readonly List<Image> beams = new List<Image>();
        readonly List<Image> laneBoxes = new List<Image>();
        readonly List<Text> laneLabels = new List<Text>();
        readonly List<RectTransform> penlights = new List<RectTransform>();
        readonly List<Image> penlightImages = new List<Image>();
        float bodyW = 800f;
        float meterShown;
        // アイドルの3Dモデル（盤面から離れた所に置き、専用のカメラで RenderTexture に写す）
        Transform idolStage, idolModel;
        Camera idolCamera;
        RenderTexture idolTexture;
        float idolSpin;          // コールが変わった時に、くるっと回る（0〜1）
        float meterW;
        const float MeterLeft = 190f;
        const float StageTop = 64f;
        const float StageH = 380f;
        const float LaneY = 500f;
        const float LaneH = 76f;
        const float LaneGap = 16f;
        const float StatusY = 572f;
        static readonly Color StageBg = new Color32(0x16, 0x14, 0x22, 0xff);
        static readonly Color Orange = new Color32(0xff, 0x7a, 0x1a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Dark = new Color32(0x24, 0x22, 0x26, 0xff);
        static readonly Color Skin = new Color32(0xff, 0xe2, 0xcc, 0xff);
        static readonly Color Hair = new Color32(0x5a, 0x3c, 0x8c, 0xff);

        /// <summary>シーンに無ければ、RecordEffectSystem の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<PopsRecord>() != null) return;
            var system = FindFirstObjectByType<RecordEffectSystem>();
            if (system != null) system.gameObject.AddComponent<PopsRecord>();
        }

        void Awake() => Instance = this;

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (effects == null) effects = FindFirstObjectByType<RecordEffectSystem>();
            if (game == null || effects == null)
            {
                Debug.LogWarning("PopsRecord: MedalPusherGame か RecordEffectSystem が見つからないため、ポップスの効果は動きません。", this);
                enabled = false;
                return;
            }

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            effects.RecordPlayed += OnRecordPlayed;
            effects.RecordFinished += OnRecordFinished;
            game.MedalDropped += OnMedalDropped;

            BuildStage();
#if UNITY_EDITOR
            if (idolPrefab == null) idolPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DefaultIdolPath);
#endif
            if (idolPrefab != null) BuildIdolStage();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (effects != null)
            {
                effects.RecordPlayed -= OnRecordPlayed;
                effects.RecordFinished -= OnRecordFinished;
            }
            if (game != null) game.MedalDropped -= OnMedalDropped;
            if (stageRoot != null) Destroy(stageRoot.gameObject);
            if (idolStage != null) Destroy(idolStage.gameObject);
            if (idolTexture != null) idolTexture.Release();
        }

        bool IsMine(RecordData r) => r != null && r.key == recordKey;
        bool IsScoring => game.State == GameState.Playing || game.State == GameState.Grace;

        Color LaneColor(int i) => laneColors != null && i >= 0 && i < laneColors.Length ? laneColors[i] : Color.white;
        string LaneName(int i) => laneNames != null && i >= 0 && i < laneNames.Length ? laneNames[i] : $"{i + 1}";

        /// <summary>アンコール中は、レーンの色を順にまわした色</summary>
        Color RainbowColor(float offset)
        {
            float t = Mathf.Repeat(Time.time * 2.5f + offset, LaneCount);
            int a = Mathf.FloorToInt(t) % LaneCount;
            return Color.Lerp(LaneColor(a), LaneColor((a + 1) % LaneCount), t - Mathf.Floor(t));
        }

        // ================================================================
        // 始まり・終わり
        // ================================================================

        void OnRecordPlayed(RecordData r)
        {
            if (IsMine(r)) Begin();
        }

        void OnRecordFinished(RecordData r)
        {
            if (IsMine(r)) End();
        }

        void Begin()
        {
            active = true;
            meter = 0f;
            meterShown = 0f;
            encoreTimer = 0f;
            halfFired = false;
            call = rng.Next(LaneCount);
            callTimer = callSeconds;
            monitor = game.Monitor;
            BuildScreen();
            PlaySound(callSound, ref madeCall, MakeCall);
        }

        void End()
        {
            active = false;
            encoreTimer = 0f;
            if (monitor != null) monitor.HideRecordScreen(recordKey);
            screen = null;
        }

        void NextCall()
        {
            int next = call;
            while (next == call) next = rng.Next(LaneCount);
            call = next;
            callTimer = callSeconds;
            callPulse = 1f;
            idolSpin = 1f;
            PlaySound(callSound, ref madeCall, MakeCall);
        }

        void StartEncore()
        {
            encoreTimer = encoreSeconds;
            meter = 0f;
            halfFired = false;
            FireConfetti();
            PlaySound(encoreSound, ref madeEncore, MakeEncore);
            PlaySound(cheerSound, ref madeCheer, MakeCheer);
            Pop("アンコール！", Orange, new Vector2(bodyW * 0.5f, StageTop + 120f));
            if (SpeakerUnit.Main != null)
            {
                SpeakerUnit.Main.Pulse(1f);
                SpeakerUnit.Main.EmitNotes(16, Orange);
            }
        }

        // ================================================================
        // ゲームからの通知
        // ================================================================

        void OnMedalDropped(Medal medal, DropZoneType type)
        {
            if (!active || medal == null || type != DropZoneType.Center) return;
            if (!IsScoring) return;

            int lane = LaneAt(medal.transform.position.x);
            bool hit = Encore || lane == call;

            // ポイントの上乗せ。基本のポイント（中央 10pt）は MedalPusherGame が入れている
            int bonus = hit ? hitBonus : missBonus;
            int extra = bonus;
            if (Encore) extra += Mathf.RoundToInt((game.rules.centerPoints + bonus) * (encoreMultiplier - 1f));
            if (extra > 0) effects.AddPoints(extra);

            laneFlash[lane] = 1f;
            Burst(lane, hit);
            var popPos = LaneBoxCenter(lane) + new Vector2(0f, -70f);
            if (hit)
            {
                PlaySound(hitSound, ref madeHit, MakeHit);
                PlaySoundScaled(cheerSound, ref madeCheer, MakeCheer, 0.35f); // 小さめの歓声
                Pop(Encore ? $"+{extra}" : $"コール成功！ +{extra}", LaneColor(lane), popPos);
                if (SpeakerUnit.Main != null) SpeakerUnit.Main.Pulse(0.6f);
                if (!Encore)
                {
                    meter += meterOnHit;
                    NextCall();
                }
            }
            else
            {
                PlaySound(missSound, ref madeMiss, MakeMiss);
                Pop($"+{extra}", Ink, popPos);
                meter += meterOnMiss;
            }

            if (!Encore)
            {
                if (!halfFired && meter >= 50f)
                {
                    halfFired = true;
                    FireConfetti();
                }
                if (meter >= 100f) StartEncore();
            }
            meter = Mathf.Clamp(meter, 0f, 100f);
        }

        /// <summary>中央の穴の、何番目のレーンか（左が 0）。</summary>
        int LaneAt(float x)
        {
            float half = HoleHalf;
            float t = Mathf.InverseLerp(-half, half, x);
            return Mathf.Clamp(Mathf.FloorToInt(t * LaneCount), 0, LaneCount - 1);
        }

        float HoleHalf => Mathf.Clamp(game.layout.centerHoleWidth, 0.1f, game.layout.boardWidth) * 0.5f;
        float LaneX(int i) => -HoleHalf + (i + 0.5f) * (HoleHalf * 2f / LaneCount);

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (game == null || effects == null) return;
            // 念のため：効果が別のレコードに替わっていたら終える
            if (active && !IsMine(effects.Current)) End();

            if (active)
            {
                if (Encore)
                {
                    encoreTimer -= Time.deltaTime;
                    if (encoreTimer <= 0f)
                    {
                        encoreTimer = 0f;
                        NextCall();
                    }
                }
                else
                {
                    callTimer -= Time.deltaTime;
                    if (callTimer <= 0f) NextCall();
                    meter = Mathf.Max(0f, meter - meterDecay * Time.deltaTime);
                }
            }
            callPulse = Mathf.MoveTowards(callPulse, 0f, Time.deltaTime * 3f);
            for (int i = 0; i < LaneCount; i++) laneFlash[i] = Mathf.MoveTowards(laneFlash[i], 0f, Time.deltaTime * 3f);

            UpdateStage();
            UpdateIdol();
            UpdateScreen();
        }

        // ================================================================
        // 盤面：スポットライト・レーンの帯・紙吹雪
        // ================================================================

        void BuildStage()
        {
            stageRoot = new GameObject("PopsStage").transform;
            particleMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit"));

            float laneW = HoleHalf * 2f / LaneCount;
            float front = game.layout.floorFrontZ;
            for (int i = 0; i < LaneCount; i++)
            {
                float x = LaneX(i);
                var target = new Vector3(x, 0f, front + D * 0.6f);
                var from = new Vector3(x, game.layout.wallHeight * 1.05f, front + D * 3f);

                // スポットライト（本物のライト＋見える光の筋）
                Light light;
                if (spotlightPrefab != null)
                {
                    var go = Instantiate(spotlightPrefab, stageRoot);
                    go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                    light = go.GetComponentInChildren<Light>();
                    beamMaterials.Add(null);
                }
                else
                {
                    var go = new GameObject($"Spot{i + 1}");
                    go.transform.SetParent(stageRoot, false);
                    go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                    light = go.AddComponent<Light>();
                    beamMaterials.Add(BuildBeam(go.transform, Vector3.Distance(from, target), laneW * 0.45f));
                }
                if (light != null)
                {
                    light.type = LightType.Spot;
                    light.spotAngle = spotAngle;
                    light.range = Vector3.Distance(from, target) * 1.6f;
                    light.shadows = LightShadows.None;
                    light.intensity = 0f;
                }
                spots.Add(light);

                // 手前のふちの、レーンの色の帯
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = $"LaneBar{i + 1}";
                Destroy(bar.GetComponent<Collider>());   // 見た目だけ。メダルには当たらない
                bar.transform.SetParent(stageRoot, false);
                bar.transform.position = new Vector3(x, D * 0.06f, front - D * 0.05f);
                bar.transform.localScale = new Vector3(laneW * 0.9f, D * 0.05f, D * 0.06f);
                var r = bar.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                var m = ProtoAssets.MakeMaterial(LaneColor(i), 0f, 0.5f);
                m.EnableKeyword("_EMISSION");
                r.sharedMaterial = m;
                barMaterials.Add(m);
            }

            // 左右の紙吹雪のキャノン（盤面の左右の端から、上と内側へ）
            float side = game.layout.boardWidth * 0.5f - D * 0.3f;
            confettiLeft = BuildConfetti("ConfettiLeft", new Vector3(-side, D * 0.3f, front + D * 0.8f), new Vector3(0.45f, 1f, 0.25f));
            confettiRight = BuildConfetti("ConfettiRight", new Vector3(side, D * 0.3f, front + D * 0.8f), new Vector3(-0.45f, 1f, 0.25f));
            burst = BuildBurst();

            stageRoot.gameObject.SetActive(false);
        }

        /// <summary>見える光の筋（先が細く、床に向けて広がる円すい）。ライトの子にする。</summary>
        Material BuildBeam(Transform parent, float length, float radius)
        {
            const int seg = 20;
            var mesh = new Mesh { name = "PopsBeam" };
            var v = new Vector3[seg * 2];
            var c = new Color[seg * 2];
            var tri = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v[i] = dir * radius * 0.08f;
                v[seg + i] = dir * radius + new Vector3(0f, 0f, length);
                c[i] = new Color(1f, 1f, 1f, 0.55f);
                c[seg + i] = new Color(1f, 1f, 1f, 0.05f);
                int n = (i + 1) % seg;
                tri[i * 6 + 0] = i; tri[i * 6 + 1] = seg + i; tri[i * 6 + 2] = seg + n;
                tri[i * 6 + 3] = i; tri[i * 6 + 4] = seg + n; tri[i * 6 + 5] = n;
            }
            mesh.vertices = v;
            mesh.colors = c;
            mesh.triangles = tri;
            mesh.RecalculateBounds();

            var go = new GameObject("Beam");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var m = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent"));
            m.color = new Color(1f, 1f, 1f, 0f);
            r.sharedMaterial = m;
            return m;
        }

        ParticleSystem BuildConfetti(string name, Vector3 position, Vector3 direction)
        {
            var go = new GameObject(name);
            go.transform.SetParent(stageRoot, false);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(D * 5f, D * 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(D * 0.10f, D * 0.18f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var colors = new List<Color>(laneColors ?? new Color[0]) { Color.white, new Color(1f, 0.5f, 0.85f), new Color(0.5f, 1f, 0.6f) };
            main.startColor = new ParticleSystem.MinMaxGradient(RandomColors(colors.ToArray())) { mode = ParticleSystemGradientMode.RandomColor };

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = D * 0.1f;

            // ひらひら：空気でゆっくりになり、回る
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = D * 2.2f;
            limit.dampen = 0.08f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = D * 0.6f;
            noise.frequency = 0.6f;

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = particleMaterial;
            return ps;
        }

        /// <summary>落ちたレーンから、そのレーンの色の粒が飛ぶ。</summary>
        ParticleSystem BuildBurst()
        {
            var go = new GameObject("LaneBurst");
            go.transform.SetParent(stageRoot, false);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1f, -0.4f));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(D * 2f, D * 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(D * 0.08f, D * 0.14f);
            main.gravityModifier = 0.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;
            shape.radius = D * 0.3f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = particleMaterial;
            return ps;
        }

        void Burst(int lane, bool hit)
        {
            if (burst == null) return;
            burst.transform.position = new Vector3(LaneX(lane), D * 0.3f, game.layout.floorFrontZ);
            var p = new ParticleSystem.EmitParams { startColor = LaneColor(lane), applyShapeToPosition = true };
            burst.Emit(p, hit ? 26 : 8);
        }

        void FireConfetti()
        {
            if (confettiPrefab != null)
            {
                foreach (var ps in new[] { confettiLeft, confettiRight })
                {
                    if (ps == null) continue;
                    var fx = Instantiate(confettiPrefab, ps.transform.position, ps.transform.rotation, stageRoot);
                    Destroy(fx, 5f);
                }
                return;
            }
            if (confettiLeft != null) confettiLeft.Emit(90);
            if (confettiRight != null) confettiRight.Emit(90);
        }

        void UpdateStage()
        {
            if (stageRoot == null) return;
            stageShown = Mathf.MoveTowards(stageShown, active ? 1f : 0f, Time.deltaTime * 2.5f);
            // 紙吹雪が残っている間は消さない
            bool particles = (confettiLeft != null && confettiLeft.particleCount > 0) || (burst != null && burst.particleCount > 0);
            bool visible = stageShown > 0f || particles;
            if (stageRoot.gameObject.activeSelf != visible) stageRoot.gameObject.SetActive(visible);
            if (!visible) return;

            float t = Time.time;
            for (int i = 0; i < LaneCount; i++)
            {
                bool called = active && (Encore || i == call);
                Color c = Encore ? RainbowColor(i * 0.7f) : LaneColor(i);
                float blink = called ? 0.75f + 0.25f * Mathf.Sin(t * (Encore ? 14f : 8f)) : 0.25f;
                float level = Mathf.Clamp01(blink + laneFlash[i] * 0.6f) * stageShown;

                if (i < spots.Count && spots[i] != null)
                {
                    spots[i].color = c;
                    spots[i].intensity = spotIntensity * level;
                    spots[i].enabled = level > 0.001f;
                }
                if (i < beamMaterials.Count && beamMaterials[i] != null)
                    beamMaterials[i].color = new Color(c.r, c.g, c.b, 0.35f * level);
                if (i < barMaterials.Count && barMaterials[i] != null)
                {
                    barMaterials[i].color = c;
                    barMaterials[i].SetColor("_EmissionColor", c * (0.4f + 2.2f * level));
                }
            }
        }

        // ================================================================
        // アイドルの3Dモデル（モニターに写す）
        // ================================================================

        const float IdolViewW = 260f, IdolViewH = 320f;   // モニターに出す大きさ（中身の座標）
        static readonly Vector3 IdolStagePosition = new Vector3(0f, -260f, 0f); // 盤面から離れた所

        void BuildIdolStage()
        {
            int layer = Mathf.Clamp(idolLayer, 8, 31);
            idolStage = new GameObject("PopsIdolStage").transform;
            idolStage.position = IdolStagePosition;

            // モデル（高さを 1 にそろえ、足もとを y=0 に置く。正面は -Z 向き）
            idolModel = new GameObject("IdolHolder").transform;
            idolModel.SetParent(idolStage, false);
            var inst = Instantiate(idolPrefab, idolModel);
            inst.transform.localPosition = Vector3.zero;
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var rb in inst.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
            var renderers = inst.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                float height = Mathf.Max(0.001f, b.size.y);
                float scale = 1f / height;
                var center = b.center - idolModel.position;
                inst.transform.localPosition = new Vector3(-center.x, -(b.min.y - idolModel.position.y), -center.z);
                idolModel.localScale = Vector3.one * scale;
            }

            // カメラ（正面から全身。背景は透明にして、モニターのステージに重ねる）
            idolTexture = new RenderTexture(Mathf.RoundToInt(IdolViewW * 2f), Mathf.RoundToInt(IdolViewH * 2f), 24, RenderTextureFormat.ARGB32) { name = "PopsIdol" };
            idolTexture.Create();
            var camGo = new GameObject("IdolCamera");
            camGo.transform.SetParent(idolStage, false);
            float dist = 0.62f / Mathf.Tan(idolCameraFov * 0.5f * Mathf.Deg2Rad);
            camGo.transform.localPosition = new Vector3(0f, 0.55f, -dist);
            camGo.transform.LookAt(idolStage.TransformPoint(new Vector3(0f, 0.5f, 0f)));
            idolCamera = camGo.AddComponent<Camera>();
            idolCamera.fieldOfView = idolCameraFov;
            idolCamera.clearFlags = CameraClearFlags.SolidColor;
            idolCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            idolCamera.targetTexture = idolTexture;
            idolCamera.cullingMask = 1 << layer;
            idolCamera.nearClipPlane = 0.05f;
            idolCamera.farClipPlane = 20f;
            idolCamera.enabled = false;

            // 明かり（モデルだけを照らす）
            var key = new GameObject("IdolKeyLight").AddComponent<Light>();
            key.transform.SetParent(idolStage, false);
            key.transform.localRotation = Quaternion.Euler(30f, -25f, 0f);
            key.type = LightType.Directional;
            key.shadows = LightShadows.None;
            var fill = new GameObject("IdolFillLight").AddComponent<Light>();
            fill.transform.SetParent(idolStage, false);
            fill.transform.localRotation = Quaternion.Euler(10f, 140f, 0f);
            fill.type = LightType.Directional;
            fill.shadows = LightShadows.None;
            fill.color = new Color(1f, 0.75f, 0.95f);
            idolKey = key;
            idolFill = fill;

            SetLayer(idolStage, layer);
            key.cullingMask = 1 << layer;
            fill.cullingMask = 1 << layer;
            if (Camera.main != null) Camera.main.cullingMask &= ~(1 << layer);   // メインカメラには映さない
            ExcludeOtherLights();
        }

        Light idolKey, idolFill;

        /// <summary>盤面のライトが、アイドルのモデルを照らさないようにする。</summary>
        void ExcludeOtherLights()
        {
            int layer = Mathf.Clamp(idolLayer, 8, 31);
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (idolStage == null || !light.transform.IsChildOf(idolStage)) light.cullingMask &= ~(1 << layer);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        void UpdateIdol()
        {
            if (idolCamera == null) return;
            bool show = active && screen != null;
            if (show && !idolCamera.enabled) ExcludeOtherLights(); // あとから作られたライトも外す
            idolCamera.enabled = show;
            if (!show) return;

            float t = Time.time;
            idolSpin = Mathf.MoveTowards(idolSpin, 0f, Time.deltaTime * 2.2f);
            float spin = (1f - idolSpin) * 360f;            // コールが変わると、くるっと1回転
            if (idolSpin <= 0f) spin = 0f;
            float sway = Mathf.Sin(t * 1.6f) * idolSwayAngle;
            idolModel.localRotation = Quaternion.Euler(0f, sway + spin, 0f);   // 正面（-Z）をカメラに向けたまま振る
            idolKey.intensity = idolLightIntensity;
            idolFill.intensity = idolLightIntensity * 0.45f;
        }

        // ================================================================
        // モニターの画面
        // ================================================================

        void BuildScreen()
        {
            if (monitor == null) return;
            screen = monitor.ShowRecordScreen(recordKey, "POPS", "コール&レスポンス・ライブ");
            if (screen == null) return;
            float W = bodyW = monitor.RecordBodyW;

            // 上：盛り上がりメーター
            monitor.ScreenLabel(screen, "MeterLabel", 24f, 30f, 160f, 36f, "盛り上がり", 24, Ink, TextAnchor.MiddleLeft);
            meterW = W - MeterLeft - 200f;
            monitor.ScreenImage(screen, "MeterBg", MeterLeft + meterW * 0.5f, 30f, meterW, 26f, new Color(1f, 1f, 1f, 0.12f), MonitorDisplay.ScreenShape.Pill);
            meterFill = monitor.ScreenImage(screen, "MeterFill", MeterLeft + 4f, 30f, 0f, 18f, Orange, MonitorDisplay.ScreenShape.Pill);
            meterFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            meterFill.rectTransform.anchoredPosition = new Vector2(MeterLeft + 4f, -30f);
            meterText = monitor.ScreenLabel(screen, "MeterText", W - 24f, 30f, 170f, 44f, "", 30, Ink, TextAnchor.MiddleRight, true);

            // まん中：ライブのステージ
            float sw = W - 48f;
            var stage = monitor.ScreenImage(screen, "Stage", W * 0.5f, StageTop + StageH * 0.5f, sw, StageH, StageBg);
            stage.gameObject.AddComponent<RectMask2D>();
            var st = stage.rectTransform;
            float idolX = sw * 0.5f, idolY = StageH - 150f;

            // スポットライトの光の筋（上から、アイドルへ）
            beams.Clear();
            for (int i = 0; i < LaneCount; i++)
            {
                float sx = idolX + (i - 1) * Mathf.Min(420f, sw * 0.32f);
                var b = monitor.ScreenImage(st, $"Beam{i + 1}", sx, 0f, 150f, 560f, LaneColor(i));
                var rt = b.rectTransform;
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(sx, 20f);
                float dx = idolX - sx, dy = idolY + 20f;
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dx, dy) * Mathf.Rad2Deg);
                beams.Add(b);
            }

            // 床
            monitor.ScreenImage(st, "Floor", idolX, StageH - 52f, Mathf.Min(sw * 0.7f, 760f), 70f, new Color(1f, 1f, 1f, 0.08f), MonitorDisplay.ScreenShape.Circle);

            // アイドルの後ろの光（コールの色）
            idolAura = monitor.ScreenImage(st, "Aura", idolX, idolY + 10f, 300f, 300f, LaneColor(call), MonitorDisplay.ScreenShape.Circle);

            // アイドル（3Dモデル → 絵 → 仮の絵 の順に使う）
            idol = monitor.ScreenGroup(st, "Idol", idolX, idolY, 200f, 260f);
            idolDress = null;
            if (idolTexture != null)
            {
                var view = monitor.ScreenGroup(idol, "Model", 100f, 120f, IdolViewW, IdolViewH);
                var raw = view.gameObject.AddComponent<RawImage>();
                raw.texture = idolTexture;
                raw.raycastTarget = false;
            }
            else if (idolSprite != null)
            {
                var img = monitor.ScreenImage(idol, "Sprite", 100f, 130f, 200f, 260f, Color.white);
                img.sprite = idolSprite;
                img.preserveAspect = true;
            }
            else
            {
                idolDress = monitor.ScreenImage(idol, "Dress", 100f, 190f, 130f, 140f, LaneColor(call), MonitorDisplay.ScreenShape.Pill);
                monitor.ScreenImage(idol, "HairBack", 100f, 74f, 120f, 120f, Hair, MonitorDisplay.ScreenShape.Circle);
                monitor.ScreenImage(idol, "Head", 100f, 82f, 96f, 96f, Skin, MonitorDisplay.ScreenShape.Circle);
                monitor.ScreenImage(idol, "Bangs", 100f, 46f, 104f, 40f, Hair, MonitorDisplay.ScreenShape.Pill);
                monitor.ScreenImage(idol, "EyeL", 84f, 88f, 10f, 16f, Dark, MonitorDisplay.ScreenShape.Circle);
                monitor.ScreenImage(idol, "EyeR", 116f, 88f, 10f, 16f, Dark, MonitorDisplay.ScreenShape.Circle);
                monitor.ScreenImage(idol, "MicStick", 150f, 150f, 6f, 46f, Mute);
                monitor.ScreenImage(idol, "Mic", 150f, 124f, 22f, 22f, Dark, MonitorDisplay.ScreenShape.Circle);
            }

            // ふきだし：「次は 赤！」と、次のコールまでの時間
            float bx = Mathf.Min(idolX + 300f, sw - 170f);
            bubble = monitor.ScreenGroup(st, "Bubble", bx, idolY - 70f, 300f, 140f);
            var tail = monitor.ScreenImage(bubble, "Tail", 8f, 96f, 30f, 30f, Ink);
            tail.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            monitor.ScreenImage(bubble, "Bg", 150f, 70f, 300f, 140f, Ink, MonitorDisplay.ScreenShape.Pill);
            callSmall = monitor.ScreenLabel(bubble, "Small", 30f, 34f, 250f, 30f, "次は", 22, Dark, TextAnchor.MiddleLeft);
            callText = monitor.ScreenLabel(bubble, "Call", 150f, 78f, 280f, 70f, "", 56, Dark);
            monitor.ScreenImage(bubble, "TimerBg", 150f, 118f, 220f, 6f, new Color(0f, 0f, 0f, 0.15f));
            bubbleTimer = monitor.ScreenImage(bubble, "Timer", 150f, 118f, 220f, 6f, Orange);
            bubbleTimer.type = Image.Type.Filled;
            bubbleTimer.fillMethod = Image.FillMethod.Horizontal;

            // 客席のペンライト
            penlights.Clear();
            penlightImages.Clear();
            int count = Mathf.Max(8, Mathf.FloorToInt(sw / 38f));
            for (int i = 0; i < count; i++)
            {
                float x = (i + 0.5f) * sw / count;
                var p = monitor.ScreenImage(st, $"Penlight{i}", x, StageH - 18f, 10f, 46f, LaneColor(i % LaneCount), MonitorDisplay.ScreenShape.Pill);
                penlights.Add(p.rectTransform);
                penlightImages.Add(p);
            }

            // 下：3つのレーン（盤面の中央の穴と同じ並び）
            laneBoxes.Clear();
            laneLabels.Clear();
            for (int i = 0; i < LaneCount; i++)
            {
                var c = LaneBoxCenter(i);
                float lw = LaneWidth();
                laneBoxes.Add(monitor.ScreenImage(screen, $"Lane{i + 1}", c.x, c.y, lw, LaneH, LaneColor(i), MonitorDisplay.ScreenShape.Pill));
                laneLabels.Add(monitor.ScreenLabel(screen, $"LaneLabel{i + 1}", c.x, c.y, lw, LaneH, "", 26, Dark));
            }

            // 一番下：ルールの案内
            statusText = monitor.ScreenLabel(screen, "Status", 24f, StatusY, W - 48f, 40f, "", 22, Ink, TextAnchor.MiddleLeft);
        }

        float LaneWidth() => (bodyW - 48f - LaneGap * (LaneCount - 1)) / LaneCount;

        /// <summary>レーンの箱の中心（モニターの中身の座標。ポップの位置に使う）。</summary>
        Vector2 LaneBoxCenter(int i)
        {
            float lw = LaneWidth();
            return new Vector2(24f + lw * 0.5f + i * (lw + LaneGap), LaneY);
        }

        void UpdateScreen()
        {
            if (!active || screen == null) return;
            float t = Time.time;

            // メーター
            meterShown = Mathf.Lerp(meterShown, Encore ? encoreTimer / Mathf.Max(0.1f, encoreSeconds) * 100f : meter, 1f - Mathf.Exp(-8f * Time.deltaTime));
            meterFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, (meterW - 8f) * meterShown / 100f), 18f);
            meterFill.color = Encore ? RainbowColor(0f) : Color.Lerp(Orange, new Color32(0xff, 0xd2, 0x3f, 0xff), meterShown / 100f);
            meterText.text = Encore ? "<color=#FF7A1A>ENCORE</color>" : $"{Mathf.FloorToInt(meter)}<size=20>%</size>";

            // ふきだし
            Color callColor = Encore ? RainbowColor(0f) : LaneColor(call);
            if (Encore)
            {
                callSmall.text = $"アンコール！ 残り {Mathf.CeilToInt(encoreTimer)}秒";
                callText.text = "ぜんぶ！";
                bubbleTimer.fillAmount = encoreTimer / Mathf.Max(0.1f, encoreSeconds);
            }
            else
            {
                callSmall.text = "次は";
                callText.text = $"{LaneName(call)}！";
                bubbleTimer.fillAmount = callTimer / Mathf.Max(0.1f, callSeconds);
            }
            callText.color = Color.Lerp(callColor, Dark, 0.15f);
            float bs = 1f + 0.18f * callPulse * callPulse;
            bubble.localScale = new Vector3(bs, bs, 1f);

            // アイドル：リズムに合わせて跳ねる。服はコールの色
            float bounce = Mathf.Abs(Mathf.Sin(t * (Encore ? 9f : 6f))) * (Encore ? 14f : 7f);
            var ip = idol.anchoredPosition;
            idol.anchoredPosition = new Vector2(ip.x, -(StageH - 150f) + bounce);
            if (idolDress != null) idolDress.color = callColor;
            idolAura.color = new Color(callColor.r, callColor.g, callColor.b, 0.22f + 0.08f * Mathf.Sin(t * 6f));

            // 光の筋
            for (int i = 0; i < beams.Count; i++)
            {
                bool called = Encore || i == call;
                Color c = Encore ? RainbowColor(i * 0.7f) : LaneColor(i);
                float a = called ? 0.28f + 0.08f * Mathf.Sin(t * 8f) : 0.08f;
                beams[i].color = new Color(c.r, c.g, c.b, a + laneFlash[i] * 0.2f);
            }

            // ペンライト：盛り上がるほど大きく振る。色はコールの色
            float amp = 4f + 22f * Mathf.Clamp01(meterShown / 100f) + (Encore ? 10f : 0f);
            for (int i = 0; i < penlights.Count; i++)
            {
                float y = StageH - 18f - Mathf.Abs(Mathf.Sin(t * 6f + i * 0.55f)) * amp;
                var pp = penlights[i].anchoredPosition;
                penlights[i].anchoredPosition = new Vector2(pp.x, -y);
                penlights[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 6f + i * 0.55f) * 18f);
                Color pc = Encore ? RainbowColor(i * 0.3f) : Color.Lerp(LaneColor(i % LaneCount), callColor, 0.6f);
                penlightImages[i].color = pc;
            }

            // レーン：コールのレーンは明るく点滅、ほかは暗く。落ちると白く光る
            for (int i = 0; i < laneBoxes.Count; i++)
            {
                bool called = Encore || i == call;
                Color c = Encore ? RainbowColor(i * 0.7f) : LaneColor(i);
                float blink = 0.5f + 0.5f * Mathf.Sin(t * 8f);
                Color baseColor = called ? Color.Lerp(c, Color.white, 0.25f * blink) : new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 1f);
                laneBoxes[i].color = Color.Lerp(baseColor, Color.white, laneFlash[i]);
                int pts = (called ? hitBonus : missBonus);
                laneLabels[i].text = $"<size=30>{LaneName(i)}</size>　+{pts}";
                laneLabels[i].color = called ? Dark : Ink;
            }

            // 案内
            statusText.text = Encore
                ? $"<color=#FF7A1A>アンコール！</color>　どのレーンも成功・ポイント ×{encoreMultiplier:0.#}　残り {Mathf.CeilToInt(encoreTimer)}秒"
                : $"コールの色のレーンに落とすと +{hitBonus}pt・ほかのレーン +{missBonus}pt　メーター満タンでアンコール！";
        }

        void Pop(string text, Color color, Vector2 bodyPos)
        {
            if (monitor == null || screen == null) return;
            monitor.PopText(text, color, monitor.RecordBodyToCanvas(bodyPos));
        }

        // ================================================================
        // 音（差し替えの音が無ければ、コードで作る仮の音）
        // ================================================================

        void PlaySound(AudioClip clip, ref AudioClip made, Func<AudioClip> make) => PlaySoundScaled(clip, ref made, make, 1f);

        void PlaySoundScaled(AudioClip clip, ref AudioClip made, Func<AudioClip> make, float scale)
        {
            if (clip == null)
            {
                if (made == null) made = make();
                clip = made;
            }
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip, volume * scale);
        }

        static AudioClip MakeHit() => Arpeggio("PopsHit", new[] { 1046.5f, 1318.5f, 1568f, 2093f }, 0.06f, 0.35f, 0.35f);
        static AudioClip MakeMiss() => Arpeggio("PopsMiss", new[] { 659.25f }, 0.05f, 0.18f, 0.2f);
        static AudioClip MakeCall() => Arpeggio("PopsCall", new[] { 880f, 1174.7f }, 0.09f, 0.2f, 0.3f);
        static AudioClip MakeEncore() => Arpeggio("PopsEncore", new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f, 1568f, 2093f }, 0.07f, 0.8f, 0.35f);

        /// <summary>音を順に鳴らす（ピコピコした明るい音）。step は音と音の間、tail は最後の音の長さ。</summary>
        static AudioClip Arpeggio(string name, float[] freqs, float step, float tail, float gain)
        {
            const int rate = 44100;
            float seconds = step * (freqs.Length - 1) + tail;
            var data = new float[Mathf.Max(1, (int)(rate * seconds))];
            for (int k = 0; k < freqs.Length; k++)
            {
                int start = (int)(rate * step * k);
                float len = k == freqs.Length - 1 ? tail : step * 2.5f;
                int n = (int)(rate * len);
                for (int i = 0; i < n && start + i < data.Length; i++)
                {
                    float tt = i / (float)rate;
                    float env = Mathf.Exp(-tt * 6f / len) * Mathf.Min(1f, i / 200f);
                    float ph = 2f * Mathf.PI * freqs[k] * tt;
                    // 四角い波を少しまぜた、明るい音
                    float wave = Mathf.Sin(ph) * 0.7f + Mathf.Sign(Mathf.Sin(ph)) * 0.15f;
                    data[start + i] += wave * env * gain;
                }
            }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>歓声（ざわざわした音を、ふくらませて消す）。</summary>
        static AudioClip MakeCheer()
        {
            const int rate = 44100;
            float seconds = 1.6f;
            var data = new float[(int)(rate * seconds)];
            var rand = new System.Random(77);
            float low = 0f, wobble = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float tt = i / (float)rate;
                float noise = (float)(rand.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.18f;             // 高い所を削る
                wobble += ((float)rand.NextDouble() - wobble) * 0.0008f; // 人の声のような、ゆるい揺れ
                float env = Mathf.Min(1f, tt / 0.12f) * Mathf.Exp(-Mathf.Max(0f, tt - 0.25f) * 2.2f);
                data[i] = low * env * (0.6f + wobble) * 0.6f;
            }
            var clip = AudioClip.Create("PopsCheer", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static Gradient RandomColors(Color[] colors)
        {
            if (colors == null || colors.Length == 0) colors = new[] { Color.white };
            int n = Mathf.Min(colors.Length, 8);   // Gradient の色は8つまで
            var keys = new GradientColorKey[n];
            for (int i = 0; i < n; i++) keys[i] = new GradientColorKey(colors[i], n == 1 ? 0f : i / (float)(n - 1));
            var g = new Gradient { mode = GradientMode.Fixed };
            g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
