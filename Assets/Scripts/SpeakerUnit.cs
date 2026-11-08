using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 筐体の右の壁に付けるスピーカー。レコードの曲に合わせて振動し、演出の音や光をここから出せる。
    ///
    /// 置き方：上のメニュー「MedalPusher」→「スピーカーを置く」（SpeakerUnitEditor.cs）。
    ///   レコーダー（左の壁のターンテーブル）と左右反対の位置に、同じくらいの大きさで置く。
    ///
    /// 曲に合わせた動き（自動）：
    ///  ・ウーファー（大きいコーン）が低音で前後に動く。ツイーター（小さい方）は高い音で小さくふるえる
    ///  ・まわりのリングライトと、上のレベルメーターが音の大きさで光る
    ///  ・強い低音が来ると、本体が少しゆれて、音符の光の粒が飛び出す
    ///  ・レコードを流した瞬間に、少しはずんで音符が飛ぶ
    ///   （曲は RecordAudioPlayer の AudioSource から読み取る。ほかの音には反応しない）
    ///
    /// レコードの効果を発動している間の演出（自動）：
    ///  ・発動の瞬間：Q を押すと少し沈み（曲のブレーキ）、ドロップの瞬間に、ドンと大きくはずみ、
    ///    前に光の輪（衝撃波）が2つ広がり、音符と系統ごとの粒がたくさん飛ぶ（RecordAudioPlayer.Dropped）
    ///  ・リングライトとメーターが、そのレコードの系統の色になり、本体のゆれも大きくなる
    ///  ・系統ごとの粒が、ビートに合わせて出る
    ///      定番・加算 → きらきら / 倍率 → 火花 / 物理 → 衝撃波とけむり / 盤面・フィールド → 泡 /
    ///      経済 → コイン / モニター・条件 → 星 / メダル・変化 → 虹色
    ///    レコードごとに変えたい時は Style Overrides にキー（英名）と粒の種類を書く（例：SnowCountry → Snow）
    ///  ・メダルが穴に落ちるたび、音符と「+30」のような、その1枚のポイントの文字が飛ぶ
    ///
    /// 演出から呼ぶもの（例：SpeakerUnit.Main?.Pulse()）：
    ///  ・Pulse(強さ)：ドンとはずむ
    ///  ・EmitNotes(数, 色)：音符の光の粒を飛ばす
    ///  ・Flash(色, 秒)：リングライトとメーターを、しばらくその色にする
    ///  ・PlaySound(音, 音量)：スピーカーの位置から効果音を鳴らす（鳴らした音にも反応して動く）
    ///
    /// 見た目はすべてコードで作るので、モデルや画像はいらない（作った物はシーンに保存されない）。
    /// 本体の大きさ：高さ 1・幅 0.62・奥行き 0.42（Scale で大きさを変える）。原点は前の面の下のまん中。
    /// 前の面は -Z を向く（青い矢印 Z が壁の中を向くように置く）。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SpeakerUnit : MonoBehaviour
    {
        /// <summary>シーンにあるスピーカー（いちばん最初に有効になったもの）</summary>
        public static SpeakerUnit Main { get; private set; }

        [Header("つなぐ相手（空なら自動で探す）")]
        public RecordEffectSystem effectSystem;
        public RecordAudioPlayer audioPlayer;
        [Tooltip("ほかに反応させたい AudioSource があれば入れる")]
        public AudioSource[] extraSources = new AudioSource[0];

        [Header("音への反応")]
        [Tooltip("低音（ウーファーの動き）の感度")]
        [Range(0f, 40f)] public float bassGain = 8f;
        [Tooltip("高い音（ツイーターの動き）の感度")]
        [Range(0f, 80f)] public float trebleGain = 20f;
        [Tooltip("音の大きさ（光とメーター）の感度")]
        [Range(0f, 10f)] public float levelGain = 3f;
        [Tooltip("ウーファーが前に出る量（本体の高さに対する割合）")]
        [Range(0f, 0.1f)] public float wooferExcursion = 0.035f;
        [Tooltip("本体のゆれる量（本体の高さに対する割合）")]
        [Range(0f, 0.05f)] public float shakeAmount = 0.008f;
        [Tooltip("強い低音で音符の粒を出す")]
        public bool notesOnBeat = true;
        [Tooltip("音符の粒を出す低音の強さ（0〜1）")]
        [Range(0.1f, 1f)] public float beatThreshold = 0.6f;

        [Header("色")]
        public Color bodyColor = new Color32(0x1c, 0x1c, 0x21, 0xff);
        public Color baffleColor = new Color32(0x10, 0x10, 0x12, 0xff);
        public Color trimColor = new Color32(0xff, 0x7a, 0x1a, 0xff);
        public Color coneColor = new Color32(0x2a, 0x2a, 0x30, 0xff);
        public Color lightColor = new Color32(0xff, 0x7a, 0x1a, 0xff);
        public Color peakColor = new Color32(0xff, 0xd2, 0x3f, 0xff);
        public Color noteColor = new Color32(0xff, 0x9a, 0x3a, 0xff);

        [Header("レコードの効果の発動中")]
        [Tooltip("リングライトとメーターを、レコードの系統の色にする")]
        public bool colorWithRecord = true;
        [Tooltip("発動中の本体のゆれ・ウーファーの動きの倍率")]
        [Range(1f, 3f)] public float activeBoost = 1.6f;
        [Tooltip("系統ごとの粒をビートに合わせて出す")]
        public bool styleParticles = true;
        [Tooltip("系統ごとの粒の量（ビート1回あたり）")]
        [Range(1, 30)] public int styleBurst = 8;
        [Tooltip("メダルが落ちるたび、音符とポイントの文字を出す")]
        public bool notesOnPoint = true;
        [Tooltip("発動の瞬間に、前に光の輪を広げる")]
        public bool shockwaveOnActivate = true;
        [Tooltip("レコードごとの粒の種類（系統の決まりより優先）")]
        public StyleOverride[] styleOverrides = new StyleOverride[0];

        [Header("テスト")]
        [Tooltip("再生していない時の見た目（音の大きさ 0〜1）")]
        [Range(0f, 1f)] public float previewLevel = 0.4f;

        /// <summary>演出の粒の種類</summary>
        public enum FxStyle { Auto, Glitter, Sparks, Shock, Bubbles, Coins, Stars, Rainbow, Snow, Fireworks, None }

        /// <summary>レコード1枚の粒の種類の上書き（Inspector で設定する）</summary>
        [System.Serializable]
        public class StyleOverride
        {
            [Tooltip("レコードのキー（英名）。例：SnowCountry")]
            public string key;
            public FxStyle style = FxStyle.Glitter;
        }

        /// <summary>系統ごとの決まり（レコードの category）</summary>
        static readonly Dictionary<string, FxStyle> CategoryStyles = new Dictionary<string, FxStyle>
        {
            { "加算", FxStyle.Glitter }, { "定番", FxStyle.Glitter },
            { "倍率", FxStyle.Sparks },
            { "物理", FxStyle.Shock },
            { "フィールド", FxStyle.Bubbles }, { "盤面", FxStyle.Bubbles },
            { "経済", FxStyle.Coins },
            { "条件", FxStyle.Stars }, { "モニター", FxStyle.Stars },
            { "変化", FxStyle.Rainbow }, { "メダル", FxStyle.Rainbow },
        };

        /// <summary>レコードごとの決まり（系統より優先。Inspector の Style Overrides はさらに優先）</summary>
        static readonly Dictionary<string, FxStyle> KeyStyles = new Dictionary<string, FxStyle>
        {
            { "Rock", FxStyle.Sparks }, { "Metal", FxStyle.Sparks }, { "Hardcore", FxStyle.Sparks },
            { "SnowCountry", FxStyle.Snow }, { "Snow", FxStyle.Snow },
            { "Fireworks", FxStyle.Fireworks },
            { "GoldRush", FxStyle.Coins },
            { "Star", FxStyle.Stars }, { "Moon", FxStyle.Stars },
            { "Sea", FxStyle.Bubbles },
            { "Psychedelic", FxStyle.Rainbow },
        };

        // 形（本体の高さを 1 とした大きさ）
        const float W = 0.62f, H = 1f, D = 0.42f;
        const float WooferY = 0.36f, WooferR = 0.22f;
        const float TweeterY = 0.78f, TweeterR = 0.07f;
        const int MeterBars = 8;

        GameObject visual, body, woofer, tweeter, ringLight;
        readonly List<GameObject> bars = new List<GameObject>();
        readonly List<Material> barMats = new List<Material>();
        readonly List<Object> owned = new List<Object>();      // 作ったメッシュ・マテリアル（片づけ用）
        Material ringMat;
        ParticleSystem notes, fx;
        AudioSource sfx;
        readonly List<Transform> shockRings = new List<Transform>();
        readonly List<Material> shockMats = new List<Material>();
        readonly List<float> shockTimes = new List<float>();
        readonly List<Popup> popups = new List<Popup>();
        MedalPusherGame game;
        RecordData activeRecord;      // いま発動しているレコード（粒の種類を決め直すのに使う）
        FxStyle activeStyle = FxStyle.None;
        Color recordColor;
        float recordTint;             // 系統の色にどれだけ寄せるか（0〜1）
        float fxTimer;

        class Popup
        {
            public Transform t;
            public TextMesh text;
            public float time = -1f;   // 負なら使っていない
            public Color color;
            public float drift;
        }

        readonly float[] spectrum = new float[256];
        readonly float[] samples = new float[256];
        readonly List<AudioSource> sources = new List<AudioSource>();
        float sourceScanTimer;
        float level, bass, treble, prevBass;
        float pulse, flashTime, flashSeconds;
        Color flashColor;
        float noteCooldown;

        // ================================================================
        // 作る・片づける
        // ================================================================

        bool subscribed;
        RecordAudioPlayer dropListener;   // ドロップの知らせを受け取っている相手

        void OnEnable()
        {
            if (Main == null) Main = this;
            Build();
        }

        void OnDisable()
        {
            Subscribe(false);
            if (Main == this) Main = null;
            Cleanup();
        }

        /// <summary>レコードを流した時の知らせを受け取る（RecordEffectSystem が見つかるまで毎フレーム試す）。</summary>
        void Subscribe(bool on)
        {
            if (!Application.isPlaying) return;
            if (effectSystem == null) effectSystem = FindFirstObjectByType<RecordEffectSystem>();
            if (effectSystem == null) return;
            effectSystem.RecordPlayed -= OnRecordPlayed;
            effectSystem.RecordLoaded -= OnRecordLoaded;
            if (game != null) game.MedalDropped -= OnMedalDropped;
            if (audioPlayer == null) audioPlayer = FindFirstObjectByType<RecordAudioPlayer>();
            if (dropListener != null) dropListener.Dropped -= OnDropped;
            dropListener = null;
            if (on)
            {
                effectSystem.RecordPlayed += OnRecordPlayed;
                effectSystem.RecordLoaded += OnRecordLoaded;
                if (audioPlayer != null)
                {
                    audioPlayer.Dropped += OnDropped;
                    dropListener = audioPlayer;
                }
                game = effectSystem.game != null ? effectSystem.game : FindFirstObjectByType<MedalPusherGame>();
                if (game != null) game.MedalDropped += OnMedalDropped;
            }
            subscribed = on;
        }

        Color RecordColor(RecordData record)
        {
            if (record != null && RecordArtwork.HasCategoryColor(record.category)) return RecordArtwork.CategoryColor(record.category);
            return noteColor;
        }

        /// <summary>レコードを流した（曲だけ）：少しはずむ</summary>
        void OnRecordLoaded(RecordData record)
        {
            Pulse(0.8f);
            EmitNotes(6, RecordColor(record));
        }

        /// <summary>
        /// レコードの効果を発動した。曲のブレーキ中は、少し沈んで光を落とす。
        /// 大きな演出はドロップの瞬間（OnDropped）。曲の係（RecordAudioPlayer）がいなければ、すぐに出す。
        /// </summary>
        void OnRecordPlayed(RecordData record)
        {
            SetFxStyle(StyleOf(record));
            if (dropListener == null)
            {
                OnDropped(record);
                return;
            }
            Flash(Color.black, Mathf.Max(0.1f, dropListener.brakeSeconds));   // ブレーキ中は光が沈む
        }

        /// <summary>ドロップの瞬間：大きくはずんで、衝撃波と音符と系統ごとの粒</summary>
        void OnDropped(RecordData record)
        {
            Color c = RecordColor(record);
            Pulse(2.5f);
            EmitNotes(28, c);
            Flash(Color.white, 0.9f);
            if (shockwaveOnActivate) Shockwave(c);
            FxStyle style = StyleOf(record);
            SetFxStyle(style);
            EmitStyle(style, c, styleBurst * 3);
        }

        /// <summary>発動中にメダルが穴に落ちた：音符と、その1枚のポイントの文字</summary>
        void OnMedalDropped(Medal medal, DropZoneType type)
        {
            if (!notesOnPoint || type == DropZoneType.Lost || effectSystem == null || !effectSystem.IsActive) return;
            Color c = RecordColor(effectSystem.Current);
            EmitNotes(2, c);
            Pulse(0.6f);
            int pts = effectSystem.LastDropPoints;
            if (pts > 0) ShowPopup($"+{pts}", Color.Lerp(c, Color.white, 0.35f));
        }

        /// <summary>そのレコードの粒の種類（Inspector の上書き → レコードごとの決まり → 系統の決まり）</summary>
        public FxStyle StyleOf(RecordData record)
        {
            if (RecordData.IsNullOrEmpty(record)) return FxStyle.None;
            if (styleOverrides != null)
                foreach (var o in styleOverrides)
                    if (o != null && !string.IsNullOrWhiteSpace(o.key) && o.key.Trim() == record.key && o.style != FxStyle.Auto) return o.style;
            if (KeyStyles.TryGetValue(record.key, out FxStyle k)) return k;
            if (!string.IsNullOrEmpty(record.category) && CategoryStyles.TryGetValue(record.category, out FxStyle c)) return c;
            return FxStyle.Glitter;
        }

        void Build()
        {
            Cleanup();
            visual = Child("Visual", transform);

            // ---- 本体（動かない所） ----
            var b = new Builder();
            b.Box(new Vector3(0f, H * 0.5f, D * 0.5f), new Vector3(W, H, D), bodyColor);                    // 箱
            b.Box(new Vector3(0f, H * 0.5f, -0.004f), new Vector3(W - 0.06f, H - 0.06f, 0.008f), baffleColor); // 前の板
            b.Box(new Vector3(0f, H - 0.012f, -0.006f), new Vector3(W, 0.024f, 0.012f), trimColor);        // 上のふち
            b.Box(new Vector3(0f, 0.012f, -0.006f), new Vector3(W, 0.024f, 0.012f), trimColor);            // 下のふち
            b.RingWithSide(new Vector2(0f, WooferY), WooferR + 0.012f, WooferR + 0.03f, -0.012f, 0f, trimColor);   // ウーファーのふち
            b.RingWithSide(new Vector2(0f, TweeterY), TweeterR + 0.012f, TweeterR + 0.026f, -0.012f, 0f, trimColor); // ツイーターのふち
            foreach (float x in new[] { -0.2f, 0.2f })
            {
                b.Disc(new Vector3(x, 0.66f, -0.009f), 0.032f, Color.black);                                  // 低音の穴
                b.RingWithSide(new Vector2(x, 0.66f), 0.032f, 0.042f, -0.012f, 0f, bodyColor);
            }
            b.Box(new Vector3(-0.17f, 0.9f, -0.01f), new Vector3(0.14f, 0.04f, 0.006f), new Color(0.95f, 0.95f, 0.93f));  // 名前の札
            b.Box(new Vector3(-0.12f, 0.9f, -0.014f), new Vector3(0.03f, 0.04f, 0.004f), trimColor);
            foreach (float x in new[] { -W * 0.4f, W * 0.4f })
                foreach (float z in new[] { 0.06f, D - 0.06f })
                    b.Box(new Vector3(x, -0.015f, z), new Vector3(0.05f, 0.03f, 0.05f), Color.black);       // 足
            b.Box(new Vector3(0f, H + 0.012f, 0.06f), new Vector3(W - 0.1f, 0.024f, 0.08f), baffleColor);  // メーターの台
            body = MeshObject("Body", visual.transform, b);

            // ---- ウーファー（前後に動く） ----
            b = new Builder();
            b.RingWithSide(Vector2.zero, WooferR * 0.86f, WooferR + 0.012f, -0.016f, -0.008f, baffleColor); // ふちのゴム
            b.Cone(Vector2.zero, WooferR * 0.86f, -0.014f, WooferR * 0.32f, -0.03f, coneColor);             // コーン
            b.Dome(new Vector3(0f, 0f, -0.03f), WooferR * 0.32f, -0.02f, trimColor);                        // まん中（レコードのラベル風）
            woofer = MeshObject("Woofer", visual.transform, b);
            woofer.transform.localPosition = new Vector3(0f, WooferY, 0f);

            // ---- ツイーター ----
            b = new Builder();
            b.RingWithSide(Vector2.zero, TweeterR * 0.8f, TweeterR + 0.012f, -0.014f, -0.008f, baffleColor);
            b.Dome(new Vector3(0f, 0f, -0.012f), TweeterR * 0.8f, -0.03f, new Color(0.62f, 0.63f, 0.68f));
            tweeter = MeshObject("Tweeter", visual.transform, b);
            tweeter.transform.localPosition = new Vector3(0f, TweeterY, 0f);

            // ---- リングライト（光る） ----
            b = new Builder();
            b.RingWithSide(Vector2.zero, WooferR + 0.032f, WooferR + 0.046f, -0.016f, 0f, Color.white);
            ringMat = UnlitMaterial(lightColor);
            ringLight = MeshObject("RingLight", visual.transform, b, ringMat);
            ringLight.transform.localPosition = new Vector3(0f, WooferY, 0f);

            // ---- レベルメーター（上に並ぶ光る棒） ----
            var cube = new Builder();
            cube.Box(new Vector3(0f, 0.5f, 0f), Vector3.one, Color.white);
            var cubeMesh = cube.ToMesh("SpeakerBar");
            owned.Add(cubeMesh);
            float span = W - 0.16f;
            for (int i = 0; i < MeterBars; i++)
            {
                var mat = UnlitMaterial(lightColor);
                barMats.Add(mat);
                var go = Child($"Bar{i + 1}", visual.transform);
                go.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.transform.localPosition = new Vector3(-span / 2 + span * i / (MeterBars - 1), H + 0.024f, 0.06f);
                bars.Add(go);
            }

            // ---- 音符の光の粒 ----
            var pGo = Child("Notes", visual.transform);
            pGo.transform.localPosition = new Vector3(0f, WooferY, -0.02f);
            pGo.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);   // 前（-Z）へ飛ばす
            notes = pGo.AddComponent<ParticleSystem>();
            notes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = notes.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.gravityModifier = -0.15f;                                                 // ふわっと上がる
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 200;
            var emission = notes.emission;
            emission.rateOverTime = 0f;
            var shape = notes.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = WooferR * 0.6f;
            var col = notes.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var pr = pGo.GetComponent<ParticleSystemRenderer>();
            var pShader = Shader.Find("Particles/Standard Unlit");
            if (pShader == null) pShader = Shader.Find("Unlit/Color");
            var pMat = new Material(pShader) { name = "SpeakerNotes", hideFlags = HideFlags.DontSave };
            owned.Add(pMat);
            pr.sharedMaterial = pMat;

            // ---- 系統ごとの粒（発動中） ----
            var fGo = Child("Fx", visual.transform);
            fGo.transform.localPosition = new Vector3(0f, WooferY, -0.03f);
            fGo.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            fx = fGo.AddComponent<ParticleSystem>();
            fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var fMain = fx.main;
            fMain.playOnAwake = false;
            fMain.loop = false;
            fMain.simulationSpace = ParticleSystemSimulationSpace.World;
            fMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
            fMain.maxParticles = 400;
            var fEmission = fx.emission;
            fEmission.rateOverTime = 0f;
            var fShape = fx.shape;
            fShape.shapeType = ParticleSystemShapeType.Cone;
            fShape.radius = WooferR * 0.8f;
            var fCol = fx.colorOverLifetime;
            fCol.enabled = true;
            fCol.color = grad;
            var fSize = fx.sizeOverLifetime;
            fSize.enabled = true;
            fSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.3f));
            fGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = pMat;
            activeStyle = FxStyle.None;
            SetFxStyle(FxStyle.Glitter);

            // ---- 発動の瞬間の光の輪（衝撃波） ----
            var ringShader = Shader.Find("Sprites/Default");
            if (ringShader == null) ringShader = Shader.Find("Unlit/Color");
            for (int i = 0; i < 2; i++)
            {
                var rb = new Builder();
                rb.RingWithSide(Vector2.zero, WooferR * 0.92f, WooferR, -0.02f, -0.02f, Color.white, 48);
                var rMat = new Material(ringShader) { name = "SpeakerShock", color = new Color(1f, 1f, 1f, 0f), hideFlags = HideFlags.DontSave };
                owned.Add(rMat);
                var ring = MeshObject($"Shock{i + 1}", visual.transform, rb, rMat);
                var mesh = ring.GetComponent<MeshFilter>().sharedMesh;
                var white = new Color[mesh.vertexCount];
                for (int k = 0; k < white.Length; k++) white[k] = Color.white;
                mesh.colors = white;
                ring.transform.localPosition = new Vector3(0f, WooferY, -0.01f);
                ring.SetActive(false);
                shockRings.Add(ring.transform);
                shockMats.Add(rMat);
                shockTimes.Add(-1f);
            }

            // ---- ポイントの文字（「+30」など） ----
            if (Application.isPlaying)
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                for (int i = 0; i < 8; i++)
                {
                    var go = Child($"Popup{i + 1}", visual.transform);
                    var tm = go.AddComponent<TextMesh>();
                    tm.font = font;
                    tm.fontSize = 64;
                    tm.characterSize = 0.015f;
                    tm.anchor = TextAnchor.MiddleCenter;
                    tm.alignment = TextAlignment.Center;
                    tm.fontStyle = FontStyle.Bold;
                    var mr = go.GetComponent<MeshRenderer>();
                    if (font != null) mr.sharedMaterial = font.material;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    go.SetActive(false);
                    popups.Add(new Popup { t = go.transform, text = tm });
                }
            }

            // ---- 効果音を鳴らす所 ----
            sfx = GetComponent<AudioSource>();
            if (sfx == null && Application.isPlaying)
            {
                sfx = gameObject.AddComponent<AudioSource>();
                sfx.playOnAwake = false;
                sfx.spatialBlend = 0.6f;   // スピーカーの位置から聞こえる（少しだけ）
            }
        }

        void Cleanup()
        {
            bars.Clear();
            barMats.Clear();
            shockRings.Clear();
            shockMats.Clear();
            shockTimes.Clear();
            popups.Clear();
            fx = null;
            if (visual != null) Kill(visual);
            foreach (var o in owned) Kill(o);
            owned.Clear();
            visual = body = woofer = tweeter = ringLight = null;
            notes = null;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        GameObject Child(string name, Transform parent)
        {
            var go = new GameObject("Speaker_" + name) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            go.transform.SetParent(parent, false);
            return go;
        }

        GameObject MeshObject(string name, Transform parent, Builder b, Material single = null)
        {
            var go = Child(name, parent);
            var mesh = b.ToMesh("Speaker" + name);
            owned.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (single != null)
            {
                var mats = new Material[mesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = single;
                r.sharedMaterials = mats;
            }
            else
            {
                var mats = new Material[b.colors.Count];
                for (int i = 0; i < mats.Length; i++) mats[i] = LitMaterial(b.colors[i]);
                r.sharedMaterials = mats;
            }
            return go;
        }

        Material LitMaterial(Color c)
        {
            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = "Speaker", color = c, hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.35f);
            owned.Add(m);
            return m;
        }

        Material UnlitMaterial(Color c)
        {
            var m = new Material(Shader.Find("Unlit/Color")) { name = "SpeakerLight", color = c, hideFlags = HideFlags.DontSave };
            owned.Add(m);
            return m;
        }

        // ================================================================
        // 外から呼ぶもの
        // ================================================================

        /// <summary>ドンとはずむ（strength は 1 でいつもの強さ）。</summary>
        public void Pulse(float strength = 1f) => pulse = Mathf.Max(pulse, strength);

        /// <summary>音符の光の粒を飛ばす。</summary>
        public void EmitNotes(int count = 8, Color? color = null)
        {
            if (notes == null || !Application.isPlaying) return;
            var p = new ParticleSystem.EmitParams { startColor = color ?? noteColor, applyShapeToPosition = true };
            notes.Emit(p, count);
        }

        /// <summary>リングライトとメーターを、しばらくその色にする。</summary>
        public void Flash(Color color, float seconds = 0.5f)
        {
            flashColor = color;
            flashSeconds = Mathf.Max(0.05f, seconds);
            flashTime = flashSeconds;
        }

        /// <summary>前に光の輪（衝撃波）を広げる。</summary>
        public void Shockwave(Color color)
        {
            for (int i = 0; i < shockRings.Count; i++)
            {
                shockTimes[i] = -0.12f * i;   // 2つ目は少し遅れて広がる
                shockMats[i].color = new Color(color.r, color.g, color.b, 0f);
                shockRings[i].gameObject.SetActive(true);
            }
        }

        /// <summary>スピーカーの前に文字を浮かべる（「+30」など）。</summary>
        public void ShowPopup(string text, Color color)
        {
            if (!Application.isPlaying || popups.Count == 0) return;
            Popup p = popups[0];
            foreach (var q in popups)
            {
                if (q.time < 0f) { p = q; break; }
                if (q.time > p.time) p = q;   // 全部使っていたら、いちばん古いものを使い回す
            }
            p.time = 0f;
            p.color = color;
            p.drift = Random.Range(-0.12f, 0.12f);
            p.text.text = text;
            p.text.color = color;
            p.t.gameObject.SetActive(true);
        }

        /// <summary>系統ごとの粒を count 個出す（style が Auto なら、いま発動しているレコードの種類）。</summary>
        public void EmitStyle(FxStyle style, Color color, int count)
        {
            if (fx == null || !Application.isPlaying || count <= 0) return;
            if (style == FxStyle.Auto) style = activeStyle;
            if (style == FxStyle.None) return;
            if (style != activeStyle) SetFxStyle(style);

            for (int i = 0; i < count; i++)
            {
                var p = new ParticleSystem.EmitParams { applyShapeToPosition = true };
                Color c = color;
                switch (style)
                {
                    case FxStyle.Glitter: c = Color.Lerp(color, Color.white, Random.Range(0.3f, 0.8f)); break;
                    case FxStyle.Sparks: c = Color.Lerp(new Color(1f, 0.55f, 0.1f), new Color(1f, 0.95f, 0.5f), Random.value); break;
                    case FxStyle.Shock: c = Color.Lerp(new Color(0.55f, 0.5f, 0.45f), Color.white, Random.value * 0.4f); break;
                    case FxStyle.Bubbles: c = Color.Lerp(color, new Color(0.7f, 1f, 1f), 0.5f); c.a = 0.75f; break;
                    case FxStyle.Coins: c = Color.Lerp(new Color(0.95f, 0.72f, 0.2f), new Color(1f, 0.9f, 0.5f), Random.value); break;
                    case FxStyle.Stars: c = Color.Lerp(color, Color.white, Random.Range(0.2f, 0.6f)); break;
                    case FxStyle.Rainbow:
                    case FxStyle.Fireworks: c = Color.HSVToRGB(Random.value, 0.75f, 1f); break;
                    case FxStyle.Snow: c = new Color(0.92f, 0.97f, 1f); break;
                }
                p.startColor = c;
                if (style == FxStyle.Fireworks)
                {
                    // 前の少し上で、ぱっと全方向に開く
                    p.applyShapeToPosition = false;
                    p.position = fx.transform.TransformPoint(new Vector3(0f, -0.35f, 0.3f));
                    p.velocity = Random.onUnitSphere * Random.Range(0.8f, 1.4f) * transform.lossyScale.y;
                }
                fx.Emit(p, 1);
            }
        }

        /// <summary>粒の種類に合わせて、粒の速さ・重さ・大きさを変える。</summary>
        void SetFxStyle(FxStyle style)
        {
            if (fx == null || style == activeStyle) return;
            activeStyle = style;
            var main = fx.main;
            var shape = fx.shape;
            shape.angle = 35f;
            switch (style)
            {
                case FxStyle.Glitter:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
                    main.gravityModifier = 0.05f;
                    shape.angle = 55f;
                    break;
                case FxStyle.Sparks:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
                    main.gravityModifier = 1.2f;
                    shape.angle = 40f;
                    break;
                case FxStyle.Shock:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
                    main.gravityModifier = -0.05f;
                    shape.angle = 70f;
                    break;
                case FxStyle.Bubbles:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
                    main.gravityModifier = -0.25f;
                    break;
                case FxStyle.Coins:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 1.7f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.06f);
                    main.gravityModifier = 1.4f;
                    shape.angle = 25f;
                    break;
                case FxStyle.Stars:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
                    main.gravityModifier = -0.05f;
                    shape.angle = 60f;
                    break;
                case FxStyle.Rainbow:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
                    main.gravityModifier = -0.1f;
                    shape.angle = 50f;
                    break;
                case FxStyle.Snow:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
                    main.gravityModifier = 0.06f;
                    shape.angle = 80f;
                    break;
                case FxStyle.Fireworks:
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
                    main.gravityModifier = 0.4f;
                    break;
            }
        }

        /// <summary>スピーカーの位置から効果音を鳴らす。</summary>
        public void PlaySound(AudioClip clip, float volume = 1f)
        {
            if (clip == null || sfx == null) return;
            sfx.PlayOneShot(clip, volume);
            Pulse(0.8f);
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (visual == null) Build();
            if (visual == null) return;
            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;

            if (Application.isPlaying && !subscribed) Subscribe(true);
            if (Application.isPlaying) Analyze(dt);
            else
            {
                level = previewLevel;
                bass = previewLevel;
                treble = previewLevel * 0.6f;
            }

            // レコードの効果を発動しているか（発動中は系統の色になり、動きが大きくなる）
            RecordData active = Application.isPlaying && effectSystem != null ? effectSystem.Current : null;
            if (active != activeRecord)
            {
                activeRecord = active;
                if (active != null)
                {
                    recordColor = RecordColor(active);
                    SetFxStyle(StyleOf(active));
                }
            }
            bool isActive = active != null;
            recordTint = Mathf.MoveTowards(recordTint, isActive && colorWithRecord ? 1f : 0f, dt * 2f);
            float boost = isActive ? activeBoost : 1f;

            pulse = Mathf.MoveTowards(pulse, 0f, dt * 4f);
            flashTime = Mathf.Max(0f, flashTime - dt);
            float kick = Mathf.Clamp01((bass + pulse * 0.8f) * (isActive ? 1.15f : 1f));

            // ウーファーとツイーター
            woofer.transform.localPosition = new Vector3(0f, WooferY, -kick * wooferExcursion * boost);
            float ws = 1f + kick * 0.03f;
            woofer.transform.localScale = new Vector3(ws, ws, 1f);
            float tw = Application.isPlaying ? Mathf.Sin(Time.time * 60f) * treble : 0f;
            tweeter.transform.localPosition = new Vector3(0f, TweeterY, -tw * wooferExcursion * 0.25f);

            // 本体のゆれ（強い低音とはずみの時だけ）
            float shake = Mathf.Max(0f, kick - 0.35f) / 0.65f;
            if (Application.isPlaying && shake > 0f)
            {
                float t = Time.time * 40f;
                visual.transform.localPosition = new Vector3(Mathf.PerlinNoise(t, 0.1f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (2f * shakeAmount * shake * boost);
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(t, 3.3f) - 0.5f) * 2f * shake * boost);
            }
            else
            {
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
            }

            // 光
            Color idleCol = Color.Lerp(lightColor, recordColor, recordTint);
            Color baseCol = flashTime > 0f ? Color.Lerp(idleCol, flashColor, flashTime / flashSeconds) : idleCol;
            float glow = Mathf.Clamp01(0.25f + level * 0.9f + pulse * 0.5f);
            ringMat.color = Color.Lerp(baseCol * 0.25f, Color.Lerp(baseCol, Color.white, pulse * 0.3f), glow);
            for (int i = 0; i < bars.Count; i++)
            {
                float on = Mathf.Clamp01(level * MeterBars * 1.1f - i);
                float h = Mathf.Lerp(0.02f, 0.11f, on);
                bars[i].transform.localScale = new Vector3((W - 0.16f) / MeterBars * 0.7f, h, 0.05f);
                Color c = i >= MeterBars - 2 ? peakColor : baseCol;
                barMats[i].color = Color.Lerp(c * 0.2f, c, Mathf.Max(on, 0.15f));
            }

            // 強い低音で音符の粒（発動中は、系統ごとの粒も出す。物理は小さな衝撃波も）
            noteCooldown -= dt;
            float threshold = isActive ? beatThreshold * 0.8f : beatThreshold;
            if (Application.isPlaying && notesOnBeat && bass > threshold && prevBass <= threshold && noteCooldown <= 0f)
            {
                EmitNotes(3, baseCol);
                noteCooldown = 0.15f;
                if (isActive && styleParticles)
                {
                    EmitStyle(activeStyle, recordColor, activeStyle == FxStyle.Fireworks ? styleBurst * 3 : styleBurst);
                    if (activeStyle == FxStyle.Shock && shockwaveOnActivate) Shockwave(recordColor);
                }
            }
            prevBass = bass;

            // 曲の無いレコードでも、発動中は少しずつ粒を出す
            if (Application.isPlaying && isActive && styleParticles && level < 0.02f)
            {
                fxTimer -= dt;
                if (fxTimer <= 0f)
                {
                    fxTimer = 0.5f;
                    EmitStyle(activeStyle, recordColor, Mathf.Max(1, styleBurst / 2));
                    Pulse(0.5f);
                }
            }

            UpdateShockwaves(dt);
            UpdatePopups(dt);
        }

        /// <summary>光の輪を広げながら消す。</summary>
        void UpdateShockwaves(float dt)
        {
            for (int i = 0; i < shockRings.Count; i++)
            {
                if (!shockRings[i].gameObject.activeSelf) continue;
                shockTimes[i] += dt;
                float t = Mathf.Clamp01(shockTimes[i] / 0.6f);
                float s = 1f + t * 2.6f;
                shockRings[i].localScale = new Vector3(s, s, 1f);
                Color c = shockMats[i].color;
                c.a = shockTimes[i] < 0f ? 0f : (1f - t) * 0.9f;
                shockMats[i].color = c;
                if (t >= 1f) shockRings[i].gameObject.SetActive(false);
            }
        }

        /// <summary>ポイントの文字を上へ浮かべながら消す。</summary>
        void UpdatePopups(float dt)
        {
            foreach (var p in popups)
            {
                if (p.time < 0f) continue;
                p.time += dt;
                float t = p.time / 0.9f;
                if (t >= 1f)
                {
                    p.time = -1f;
                    p.t.gameObject.SetActive(false);
                    continue;
                }
                p.t.localPosition = new Vector3(p.drift, H + 0.12f + t * 0.35f, -0.08f);
                float pop = 1f + 0.4f * Mathf.Max(0f, 1f - t * 5f);
                p.t.localScale = new Vector3(pop, pop, 1f);
                Color c = p.color;
                c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                p.text.color = c;
            }
        }

        /// <summary>流れている曲の大きさ・低音・高音を読み取る。</summary>
        void Analyze(float dt)
        {
            sourceScanTimer -= dt;
            if (sourceScanTimer <= 0f)
            {
                sourceScanTimer = 1f;
                sources.Clear();
                if (audioPlayer == null) audioPlayer = FindFirstObjectByType<RecordAudioPlayer>();
                if (audioPlayer != null) sources.AddRange(audioPlayer.GetComponentsInChildren<AudioSource>());
                foreach (var s in extraSources) if (s != null) sources.Add(s);
                if (sfx != null) sources.Add(sfx);
            }

            float l = 0f, b = 0f, t = 0f;
            foreach (var s in sources)
            {
                if (s == null || !s.isPlaying) continue;
                s.GetOutputData(samples, 0);
                float sum = 0f;
                for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
                l = Mathf.Max(l, Mathf.Sqrt(sum / samples.Length) * s.volume);
                s.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);
                float sb = 0f, st = 0f;
                for (int i = 1; i <= 6; i++) sb += spectrum[i];
                for (int i = 20; i <= 80; i++) st += spectrum[i];
                b = Mathf.Max(b, sb);
                t = Mathf.Max(t, st);
            }
            // 上がる時はすぐ、下がる時はゆっくり
            level = Smooth(level, Mathf.Clamp01(l * levelGain), dt);
            bass = Smooth(bass, Mathf.Clamp01(b * bassGain), dt);
            treble = Smooth(treble, Mathf.Clamp01(t * trebleGain), dt);
        }

        static float Smooth(float current, float target, float dt)
        {
            float speed = target > current ? 30f : 6f;
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * dt));
        }

        // ================================================================
        // 形を作る道具（色ごとにサブメッシュを分ける）
        // ================================================================

        class Builder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<List<int>> subs = new List<List<int>>();
            public readonly List<Color> colors = new List<Color>();

            List<int> Sub(Color c)
            {
                int i = colors.IndexOf(c);
                if (i < 0)
                {
                    colors.Add(c);
                    subs.Add(new List<int>());
                    i = colors.Count - 1;
                }
                return subs[i];
            }

            /// <summary>三角形1つ。outward の向きから見えるように向きを合わせる。</summary>
            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color col)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);
                var list = Sub(col);
                int k = v.Count;
                Vector3 nrm = Vector3.Cross(b - a, c - a).normalized;
                v.Add(a); v.Add(b); v.Add(c);
                n.Add(nrm); n.Add(nrm); n.Add(nrm);
                list.Add(k); list.Add(k + 1); list.Add(k + 2);
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color col)
            {
                Tri(a, b, c, outward, col);
                Tri(a, c, d, outward, col);
            }

            public void Box(Vector3 c, Vector3 s, Color col)
            {
                Vector3 h = s * 0.5f;
                Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
                Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), Vector3.back, col);
                Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Vector3.forward, col);
                Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up, col);
                Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Vector3.down, col);
                Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), Vector3.left, col);
                Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.right, col);
            }

            /// <summary>前（-Z）を向いた円板。</summary>
            public void Disc(Vector3 c, float r, Color col, int seg = 20)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a0 = Mathf.PI * 2f * i / seg, a1 = Mathf.PI * 2f * (i + 1) / seg;
                    Tri(c, c + new Vector3(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r, 0f), c + new Vector3(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r, 0f), Vector3.back, col);
                }
            }

            /// <summary>前（-Z）を向いた輪（前の面は zFront、外側の側面は zBack まで）。</summary>
            public void RingWithSide(Vector2 c, float rIn, float rOut, float zFront, float zBack, Color col, int seg = 32)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a0 = Mathf.PI * 2f * i / seg, a1 = Mathf.PI * 2f * (i + 1) / seg;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f), d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                    Vector3 o = new Vector3(c.x, c.y, 0f);
                    Vector3 f = new Vector3(0f, 0f, zFront), bz = new Vector3(0f, 0f, zBack);
                    Quad(o + d0 * rIn + f, o + d0 * rOut + f, o + d1 * rOut + f, o + d1 * rIn + f, Vector3.back, col);
                    Quad(o + d0 * rOut + f, o + d0 * rOut + bz, o + d1 * rOut + bz, o + d1 * rOut + f, (d0 + d1) * 0.5f, col);
                    Quad(o + d0 * rIn + f, o + d0 * rIn + bz, o + d1 * rIn + bz, o + d1 * rIn + f, -(d0 + d1) * 0.5f, col);
                }
            }

            /// <summary>半径 r0（z0）から r1（z1）へのすり鉢（前から見える面）。</summary>
            public void Cone(Vector2 c, float r0, float z0, float r1, float z1, Color col, int seg = 32)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a0 = Mathf.PI * 2f * i / seg, a1 = Mathf.PI * 2f * (i + 1) / seg;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f), d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                    Vector3 o = new Vector3(c.x, c.y, 0f);
                    Quad(o + d0 * r0 + new Vector3(0, 0, z0), o + d1 * r0 + new Vector3(0, 0, z0),
                         o + d1 * r1 + new Vector3(0, 0, z1), o + d0 * r1 + new Vector3(0, 0, z1), Vector3.back, col);
                }
            }

            /// <summary>前にふくらむ丸い屋根（中心 c、半径 r、ふくらみ bulge。bulge が負なら前へ出る）。</summary>
            public void Dome(Vector3 c, float r, float bulge, Color col, int seg = 20, int rings = 4)
            {
                for (int j = 0; j < rings; j++)
                {
                    float t0 = (float)j / rings, t1 = (float)(j + 1) / rings;
                    float r0 = r * Mathf.Cos(t0 * Mathf.PI * 0.5f), r1 = r * Mathf.Cos(t1 * Mathf.PI * 0.5f);
                    float z0 = c.z - Mathf.Abs(bulge) * Mathf.Sin(t0 * Mathf.PI * 0.5f);
                    float z1 = c.z - Mathf.Abs(bulge) * Mathf.Sin(t1 * Mathf.PI * 0.5f);
                    for (int i = 0; i < seg; i++)
                    {
                        float a0 = Mathf.PI * 2f * i / seg, a1 = Mathf.PI * 2f * (i + 1) / seg;
                        Vector3 p00 = new Vector3(c.x + Mathf.Cos(a0) * r0, c.y + Mathf.Sin(a0) * r0, z0);
                        Vector3 p10 = new Vector3(c.x + Mathf.Cos(a1) * r0, c.y + Mathf.Sin(a1) * r0, z0);
                        Vector3 p11 = new Vector3(c.x + Mathf.Cos(a1) * r1, c.y + Mathf.Sin(a1) * r1, z1);
                        Vector3 p01 = new Vector3(c.x + Mathf.Cos(a0) * r1, c.y + Mathf.Sin(a0) * r1, z1);
                        Vector3 outward = ((p00 + p11) * 0.5f - new Vector3(c.x, c.y, c.z + Mathf.Abs(bulge))).normalized;
                        Quad(p00, p10, p11, p01, outward, col);
                    }
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                m.SetVertices(v);
                m.SetNormals(n);
                m.subMeshCount = subs.Count;
                for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
