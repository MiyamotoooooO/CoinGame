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
    ///  ・スピーカーの色が、そのレコードの色になる（ジャケットの絵から、いちばん目立つ色と2番目の色を取り出す。
    ///    ジャケットが無ければ系統の色）。ふち・本体・コーン・リングライト・メーターの色が変わり、本体のゆれも大きくなる
    ///  ・系統ごとの粒が、ビートに合わせて出る
    ///      定番・加算 → きらきら / 倍率 → 火花 / 物理 → 衝撃波とけむり / 盤面・フィールド → 泡 /
    ///      経済 → コイン / モニター・条件 → 星 / メダル・変化 → 虹色
    ///    レコードごとに変えたい時は Style Overrides にキー（英名）と粒の種類を書く（例：SnowCountry → Snow）
    ///  ・メダルが穴に落ちるたび、レコードの種類（粒の種類）ごとにちがう演出と「+30」のような文字が出る
    ///      きらきら → 光の粒がはじけ、文字はふわっと上がる
    ///      火花     → 火花が飛び、ウーファーが強くはね、文字はふるえる
    ///      衝撃波   → 小さな光の輪が広がり、文字はドンと大きく出て縮む
    ///      泡       → 泡がのぼり、文字はゆらゆら上がる
    ///      コイン   → コインが飛び出し、文字ははねる
    ///      星       → 星がまたたき、ライトが2番目の色に光り、文字は2色でまたたく
    ///      虹色     → 虹色の粒が出て、文字の色が虹色に変わり続ける
    ///      雪       → 雪が舞い、文字はふわっと上がる
    ///      花火     → 小さな花火が開き、文字はドンと出る
    ///    中央の穴の時は、端の穴より大きく出る
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
        [Tooltip("メダルが落ちるたび、レコードの種類ごとの演出とポイントの文字を出す")]
        public bool notesOnPoint = true;
        [Tooltip("本体・ふち・コーンの色も、レコードの色に変える（オフならリングライトとメーターだけ）")]
        public bool recolorBody = true;
        [Tooltip("ジャケットの絵から色を取り出す（オフなら系統の色だけ使う）")]
        public bool colorFromJacket = true;
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
        Color recordColor, recordColor2;   // レコードの色（いちばん目立つ色・2番目の色）
        float recordTint;             // レコードの色にどれだけ寄せるか（0〜1）
        float appliedBodyTint = -1f;  // 本体の色を最後に変えた時の recordTint
        float fxTimer;
        RecordArtwork artwork;
        readonly Dictionary<string, Color[]> paletteCache = new Dictionary<string, Color[]>();
        readonly List<BodyMat> bodyMats = new List<BodyMat>();
        readonly List<float> shockSizes = new List<float>();

        /// <summary>ポイントの文字の動き方</summary>
        enum PopMotion { Float, Shake, Slam, Sway, Bounce, Twinkle, Rainbow }

        class Popup
        {
            public Transform t;
            public TextMesh text;
            public float time = -1f;   // 負なら使っていない
            public Color color, color2;
            public float drift;
            public PopMotion motion;
        }

        /// <summary>本体の色を変えるマテリアル（role：1 ふち / 2 箱 / 3 前の板 / 4 コーン）</summary>
        struct BodyMat
        {
            public Material mat;
            public Color baseColor;
            public int role;
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

        Color RecordColor(RecordData record) => PaletteOf(record)[0];

        /// <summary>
        /// レコードの色（[0] いちばん目立つ色、[1] 2番目の色）。
        /// ジャケットの絵があれば、そこから鮮やかな色を2つ取り出す。無い時・白黒の絵の時は系統の色から作る。
        /// </summary>
        public Color[] PaletteOf(RecordData record)
        {
            if (RecordData.IsNullOrEmpty(record)) return new[] { noteColor, peakColor };
            if (paletteCache.TryGetValue(record.key, out Color[] cached)) return cached;

            Color cat = RecordArtwork.HasCategoryColor(record.category) ? RecordArtwork.CategoryColor(record.category) : noteColor;
            Color[] pal = null;
            if (colorFromJacket && Application.isPlaying)
            {
                if (artwork == null) artwork = FindFirstObjectByType<RecordArtwork>();
                Texture2D tex = artwork != null ? artwork.GetJacket(record) : null;
                if (tex != null) pal = ExtractPalette(tex);
            }
            if (pal == null)
            {
                Color.RGBToHSV(cat, out float h, out float sat, out float v);
                pal = new[] { Vivid(cat), Color.HSVToRGB(Mathf.Repeat(h + 0.12f, 1f), Mathf.Clamp01(sat * 0.7f), 1f) };
            }
            paletteCache[record.key] = pal;
            return pal;
        }

        /// <summary>絵を 16×16 に縮めて、色あい（12区分）ごとに鮮やかさで重みを付けて数え、多い色を2つ選ぶ。</summary>
        static Color[] ExtractPalette(Texture2D tex)
        {
            const int N = 16;
            var rt = RenderTexture.GetTemporary(N, N, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);   // 読み取り禁止の画像でも読めるように、一度描いてから読む
            RenderTexture.active = rt;
            var small = new Texture2D(N, N, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, N, N), 0, 0);
            small.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color[] px = small.GetPixels();
            Destroy(small);

            const int Bins = 12;
            var weight = new float[Bins];
            var sum = new Vector3[Bins];
            foreach (Color p in px)
            {
                Color.RGBToHSV(p, out float h, out float sat, out float v);
                if (sat < 0.2f || v < 0.2f) continue;   // 白・黒・灰色は数えない
                float w = sat * sat * v;
                int bin = Mathf.Clamp((int)(h * Bins), 0, Bins - 1);
                weight[bin] += w;
                sum[bin] += new Vector3(p.r, p.g, p.b) * w;
            }
            int b1 = 0;
            for (int i = 1; i < Bins; i++) if (weight[i] > weight[b1]) b1 = i;
            if (weight[b1] < 0.5f) return null;   // ほとんど白黒の絵

            int b2 = -1;
            for (int i = 0; i < Bins; i++)
            {
                int dist = Mathf.Min(Mathf.Abs(i - b1), Bins - Mathf.Abs(i - b1));
                if (dist < 2 || weight[i] < weight[b1] * 0.12f) continue;
                if (b2 < 0 || weight[i] > weight[b2]) b2 = i;
            }
            Color c1 = Vivid(ToColor(sum[b1] / weight[b1]));
            Color c2;
            if (b2 >= 0) c2 = Vivid(ToColor(sum[b2] / weight[b2]));
            else
            {
                Color.RGBToHSV(c1, out float h, out float sat, out _);
                c2 = Color.HSVToRGB(Mathf.Repeat(h + 0.12f, 1f), sat * 0.7f, 1f);
            }
            return new[] { c1, c2 };
        }

        static Color ToColor(Vector3 v) => new Color(v.x, v.y, v.z, 1f);

        /// <summary>光らせても映えるように、鮮やかさと明るさを少し上げる</summary>
        static Color Vivid(Color c)
        {
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            return Color.HSVToRGB(h, Mathf.Max(sat, 0.55f), Mathf.Max(v, 0.9f));
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

        /// <summary>発動中にメダルが穴に落ちた：レコードの種類ごとの演出と、その1枚のポイントの文字</summary>
        void OnMedalDropped(Medal medal, DropZoneType type)
        {
            if (!notesOnPoint || type == DropZoneType.Lost || effectSystem == null || !effectSystem.IsActive) return;
            Color[] pal = PaletteOf(effectSystem.Current);
            Color c1 = pal[0], c2 = pal[1];
            float big = type == DropZoneType.Center ? 1f : 0.6f;   // 中央の穴は大きく
            int n(int count) => Mathf.Max(1, Mathf.RoundToInt(count * big));
            int pts = effectSystem.LastDropPoints;
            string text = pts > 0 ? $"+{pts}" : "";
            Color light = Color.Lerp(c1, Color.white, 0.35f);

            switch (activeStyle)
            {
                case FxStyle.Glitter:     // きらきら
                    EmitStyle(FxStyle.Glitter, c1, n(8));
                    EmitNotes(n(2), c2);
                    Pulse(0.6f * big);
                    ShowPopup(text, light, PopMotion.Float, c2);
                    break;
                case FxStyle.Sparks:      // 火花
                    EmitStyle(FxStyle.Sparks, c1, n(14));
                    Pulse(1.1f * big);
                    Flash(c2, 0.12f);
                    ShowPopup(text, light, PopMotion.Shake, c2);
                    break;
                case FxStyle.Shock:       // 衝撃波
                    Shockwave(c1, 0.35f + 0.25f * big);
                    EmitStyle(FxStyle.Shock, c2, n(5));
                    Pulse(1.3f * big);
                    ShowPopup(text, light, PopMotion.Slam, c2);
                    break;
                case FxStyle.Bubbles:     // 泡
                    EmitStyle(FxStyle.Bubbles, c1, n(7));
                    EmitStyle(FxStyle.Bubbles, c2, n(3));
                    Pulse(0.4f * big);
                    ShowPopup(text, light, PopMotion.Sway, c2);
                    break;
                case FxStyle.Coins:       // コイン
                    EmitStyle(FxStyle.Coins, c1, n(10));
                    Pulse(0.8f * big);
                    ShowPopup(text, Color.Lerp(c1, new Color(1f, 0.85f, 0.3f), 0.5f), PopMotion.Bounce, c2);
                    break;
                case FxStyle.Stars:       // 星
                    EmitStyle(FxStyle.Stars, c1, n(6));
                    EmitStyle(FxStyle.Stars, c2, n(3));
                    Flash(c2, 0.25f);
                    Pulse(0.5f * big);
                    ShowPopup(text, light, PopMotion.Twinkle, c2);
                    break;
                case FxStyle.Rainbow:     // 虹色
                    EmitStyle(FxStyle.Rainbow, c1, n(9));
                    Pulse(0.7f * big);
                    ShowPopup(text, light, PopMotion.Rainbow, c2);
                    break;
                case FxStyle.Snow:        // 雪
                    EmitStyle(FxStyle.Snow, c1, n(8));
                    EmitNotes(n(1), c2);
                    Pulse(0.4f * big);
                    ShowPopup(text, Color.Lerp(c1, Color.white, 0.6f), PopMotion.Float, c2);
                    break;
                case FxStyle.Fireworks:   // 花火
                    EmitStyle(FxStyle.Fireworks, c1, n(18));
                    Pulse(1.0f * big);
                    Flash(c2, 0.15f);
                    ShowPopup(text, light, PopMotion.Slam, c2);
                    break;
                default:
                    EmitNotes(n(2), c1);
                    Pulse(0.6f * big);
                    ShowPopup(text, light, PopMotion.Float, c2);
                    break;
            }
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
                shockSizes.Add(1f);
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
            shockSizes.Clear();
            popups.Clear();
            bodyMats.Clear();
            appliedBodyTint = -1f;
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
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = LitMaterial(b.colors[i]);
                    int role = RoleOf(b.colors[i]);
                    if (role > 0) bodyMats.Add(new BodyMat { mat = mats[i], baseColor = b.colors[i], role = role });
                }
                r.sharedMaterials = mats;
            }
            return go;
        }

        /// <summary>色から、本体のどの部分か調べる（レコードの色に変える部分だけ 1〜4。ほかは 0）</summary>
        int RoleOf(Color c)
        {
            if (Same(c, trimColor)) return 1;
            if (Same(c, bodyColor)) return 2;
            if (Same(c, baffleColor)) return 3;
            if (Same(c, coneColor)) return 4;
            return 0;
        }

        static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.01f;

        /// <summary>本体・ふち・コーンの色を、レコードの色に寄せる（recordTint：0 = 元の色、1 = レコードの色）</summary>
        void ApplyBodyColors()
        {
            float tint = recolorBody ? recordTint : 0f;
            if (Mathf.Abs(tint - appliedBodyTint) < 0.002f) return;
            appliedBodyTint = tint;
            foreach (var m in bodyMats)
            {
                if (m.mat == null) continue;
                Color target;
                switch (m.role)
                {
                    case 1: target = recordColor; break;                                                     // ふち：レコードの色
                    case 2: target = Color.Lerp(m.baseColor, recordColor * 0.45f, 0.45f); break;             // 箱：暗いレコードの色
                    case 3: target = Color.Lerp(m.baseColor, recordColor2 * 0.3f, 0.3f); break;              // 前の板：ほんのり2番目の色
                    default: target = Color.Lerp(m.baseColor, recordColor2 * 0.6f, 0.5f); break;             // コーン：2番目の色
                }
                target.a = 1f;
                m.mat.color = Color.Lerp(m.baseColor, target, tint);
            }
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

        /// <summary>前に光の輪（衝撃波）を広げる。size は広がる大きさの倍率（1 でいつもの大きさ）。</summary>
        public void Shockwave(Color color, float size = 1f)
        {
            for (int i = 0; i < shockRings.Count; i++)
            {
                shockTimes[i] = -0.12f * i * size;   // 2つ目は少し遅れて広がる
                shockSizes[i] = size;
                shockMats[i].color = new Color(color.r, color.g, color.b, 0f);
                shockRings[i].gameObject.SetActive(true);
            }
        }

        /// <summary>スピーカーの前に文字を浮かべる（「+30」など）。</summary>
        public void ShowPopup(string text, Color color) => ShowPopup(text, color, PopMotion.Float, color);

        void ShowPopup(string text, Color color, PopMotion motion, Color color2)
        {
            if (!Application.isPlaying || popups.Count == 0 || string.IsNullOrEmpty(text)) return;
            Popup p = popups[0];
            foreach (var q in popups)
            {
                if (q.time < 0f) { p = q; break; }
                if (q.time > p.time) p = q;   // 全部使っていたら、いちばん古いものを使い回す
            }
            p.time = 0f;
            p.color = color;
            p.color2 = color2;
            p.motion = motion;
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
                    Color[] pal = PaletteOf(active);
                    recordColor = pal[0];
                    recordColor2 = pal[1];
                    appliedBodyTint = -1f;   // 色が変わったので、本体の色を塗り直す
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
                Color c = i >= MeterBars - 2 ? Color.Lerp(peakColor, recordColor2, recordTint) : baseCol;
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

            ApplyBodyColors();
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
                float s = 1f + t * 2.6f * shockSizes[i];
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
                float life = p.motion == PopMotion.Slam ? 0.75f : p.motion == PopMotion.Sway ? 1.2f : 0.9f;
                float t = p.time / life;
                if (t >= 1f)
                {
                    p.time = -1f;
                    p.t.gameObject.SetActive(false);
                    continue;
                }
                Vector3 pos = new Vector3(p.drift, H + 0.12f + t * 0.35f, -0.08f);
                float pop = 1f + 0.4f * Mathf.Max(0f, 1f - t * 5f);
                Color c = p.color;
                switch (p.motion)
                {
                    case PopMotion.Shake:     // ふるえる
                        pos.x += Mathf.Sin(p.time * 90f) * 0.025f * (1f - t);
                        pop = 1f + 0.6f * Mathf.Max(0f, 1f - t * 4f);
                        break;
                    case PopMotion.Slam:      // ドンと大きく出て縮む
                        pop = Mathf.Lerp(2.2f, 1f, 1f - (1f - Mathf.Clamp01(t * 5f)) * (1f - Mathf.Clamp01(t * 5f)));
                        pos.y = H + 0.18f + t * 0.12f;
                        break;
                    case PopMotion.Sway:      // ゆらゆら
                        pos.x += Mathf.Sin(p.time * 6f) * 0.06f;
                        pos.y = H + 0.1f + t * 0.3f;
                        break;
                    case PopMotion.Bounce:    // はねる
                        pos.y = H + 0.12f + Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2.5f)) * 0.2f * (1f - t) + t * 0.08f;
                        break;
                    case PopMotion.Twinkle:   // 2色でまたたく
                        pop += 0.2f * Mathf.Sin(p.time * 25f) * (1f - t);
                        c = Color.Lerp(p.color, p.color2, 0.5f + 0.5f * Mathf.Sin(p.time * 20f));
                        break;
                    case PopMotion.Rainbow:   // 虹色に変わり続ける
                        c = Color.HSVToRGB(Mathf.Repeat(p.time * 1.5f + p.drift * 3f, 1f), 0.65f, 1f);
                        break;
                }
                p.t.localPosition = pos;
                p.t.localScale = new Vector3(pop, pop, 1f);
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
