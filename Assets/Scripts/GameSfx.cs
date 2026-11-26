using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// ゲーム全体の効果音。音はすべてコードで作る（音のファイルはいらない）。
    /// 80 年代のゲーム機のような「ピコピコ」した音（ビット感のある音）にしている。
    ///   ・四角い波（矩形波）：明るい「ピッ」「ピロン」。太さ（デューティ比）12.5% / 25% / 50% で音色が変わる
    ///   ・三角の波（三角波）：やわらかい低い音。段々にして、昔の音源のざらっとした感じを出す
    ///   ・雑音（ノイズ）：「ジャッ」「カシャッ」。昔の音源と同じ作り方（LFSR）で、金属っぽい音も出せる
    ///   ・音の大きさも 16 段で変える（なめらかに変えない）ので、昔のゲームっぽくなる
    /// 鳴らす時：
    /// ・メダルを入れた時（コインの「ピロリン」）
    /// ・メダルが落ちて、ほかのメダルや床に当たった時（「チッ」。たくさん鳴りすぎないように数を制限）
    /// ・メダルが中央の穴に落ちた時（上がる「ピロリロン」）／端の穴に落ちた時（下がる「ボッ」）
    /// ・ラウンドの始まり・投入を使い切った時・ノルマ達成・ノルマ未達・ショップ
    /// ・レコードを付けかえた時、画面のボタンを押した時
    /// ほかのスクリプトからは GameSfx.Play(GameSfx.Sound.Click) のように鳴らせる。
    ///
    /// 音の大きさは、設定画面の「効果音」（GameAudioSettings）にも合わせる。
    ///
    /// 使い方：何もしなくても、MedalPusherGame があるシーンでは再生した時に自動で付く。
    /// 音の大きさを変えたい時は、シーンの好きな GameObject にこのコンポーネントを付けて Inspector で変える
    /// （付いていれば、自動では作らない）。
    /// </summary>
    [DisallowMultipleComponent]
    public class GameSfx : MonoBehaviour
    {
        public enum Sound
        {
            Insert,       // メダルを入れた
            Clink,        // メダル同士・床に当たった
            DropCenter,   // 中央の穴に落ちた
            DropSide,     // 端の穴に落ちた
            RoundStart,   // ラウンドの始まり
            OutOfMedals,  // 投入を使い切った
            Clear,        // ノルマ達成
            Fail,         // ノルマ未達
            Shop,         // ショップに入った
            Equip,        // レコードを付けかえた
            Click,        // ボタンを押した
        }

        [Header("音の大きさ（設定画面の「効果音」の大きさも、さらにかかる）")]
        [Tooltip("全体の大きさ。0 で効果音をすべて消す")]
        [Range(0f, 1f)] public float masterVolume = 0.7f;
        [Tooltip("メダルを入れた音")]
        [Range(0f, 1f)] public float insertVolume = 0.5f;
        [Tooltip("メダルが当たる音")]
        [Range(0f, 1f)] public float clinkVolume = 0.25f;
        [Tooltip("穴に落ちた音")]
        [Range(0f, 1f)] public float dropVolume = 0.55f;
        [Tooltip("ラウンドの始まり・達成・未達・ショップの音")]
        [Range(0f, 1f)] public float jingleVolume = 0.6f;
        [Tooltip("ボタン・レコードを付けかえた音")]
        [Range(0f, 1f)] public float uiVolume = 0.45f;

        [Header("メダルが当たる音")]
        [Tooltip("1 秒に鳴らすいちばん多い回数（多すぎるとうるさいので）")]
        [Range(1, 40)] public int clinksPerSecond = 14;
        [Tooltip("この速さ（メダルの直径の何倍／秒）より速く落ちてきて止まった時に鳴らす")]
        [Range(0.5f, 20f)] public float clinkSpeed = 3f;

        [Header("穴に落ちた音")]
        [Tooltip("同じ音を続けて鳴らす時の、いちばん短い間（秒）。たくさん落ちた時にうるさくならないように")]
        [Range(0f, 0.3f)] public float dropInterval = 0.06f;

        static GameSfx instance;
        const int Rate = 44100;

        AudioSource source;
        readonly Dictionary<Sound, AudioClip> clips = new Dictionary<Sound, AudioClip>();
        AudioClip[] clinkClips;   // メダルが当たる音は、同じ音のくり返しに聞こえないように何種類か作っておく
        readonly Dictionary<Sound, float> lastPlayed = new Dictionary<Sound, float>();
        readonly Dictionary<Medal, float> lastFallSpeed = new Dictionary<Medal, float>();
        readonly HashSet<Button> hookedButtons = new HashSet<Button>();
        MedalPusherGame game;
        EquipManager equip;
        float clinkBudget;
        float nextScan;

        // ───────── 自動で付ける ─────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AttachIfNeeded();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachIfNeeded();

        static void AttachIfNeeded()
        {
            if (FindObjectOfType<GameSfx>() != null) return;
            if (FindObjectOfType<MedalPusherGame>() == null) return;   // 台があるシーンだけ
            new GameObject("GameSfx（自動）").AddComponent<GameSfx>();
        }

        /// <summary>効果音を鳴らす。GameSfx がないシーンでは何もしない。</summary>
        public static void Play(Sound sound, float volume = 1f, float pitch = 1f)
        {
            if (instance != null) instance.PlayInternal(sound, volume, pitch);
        }

        // ───────── 準備 ─────────

        void Awake()
        {
            instance = this;
            source = MakeSource();
            MakeClips();
        }

        /// <summary>
        /// 効果音用の AudioSource を作る。設定画面の「BGM」の大きさ（AudioListener の音量）はかからないようにして、
        /// かわりに「効果音」の大きさを、鳴らすたびに自分でかける。
        /// </summary>
        AudioSource MakeSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;   // 2D（どこにいても同じ大きさ）
            s.ignoreListenerVolume = true;
            return s;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (game != null)
            {
                game.MedalInserted -= OnInserted;
                game.MedalDropped -= OnDropped;
                game.StateChanged -= OnStateChanged;
            }
            if (equip != null) equip.EquipmentChanged -= OnEquip;
        }

        void Start()
        {
            game = FindObjectOfType<MedalPusherGame>();
            if (game != null)
            {
                game.MedalInserted += OnInserted;
                game.MedalDropped += OnDropped;
                game.StateChanged += OnStateChanged;
            }
        }

        // ───────── ゲームの出来事 ─────────

        void OnInserted(Medal m) => PlayInternal(Sound.Insert, 1f, 1f);

        void OnDropped(Medal m, DropZoneType type)
        {
            if (type == DropZoneType.Center) PlayInternal(Sound.DropCenter, 1f, 1f);
            else if (type == DropZoneType.Side) PlayInternal(Sound.DropSide, 1f, 1f);
        }

        void OnStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.Playing: PlayInternal(Sound.RoundStart, 1f, 1f); break;
                case GameState.Grace: PlayInternal(Sound.OutOfMedals, 1f, 1f); break;
                case GameState.Cleared: PlayInternal(Sound.Clear, 1f, 1f); break;
                case GameState.Failed: PlayInternal(Sound.Fail, 1f, 1f); break;
                case GameState.Shop: PlayInternal(Sound.Shop, 1f, 1f); break;
            }
        }

        void OnEquip() => PlayInternal(Sound.Equip, 1f, 1f);

        void OnButtonClick() => PlayInternal(Sound.Click, 1f, 1f);

        void Update()
        {
            // 後から出てくるボタンやレコード置き場にも音を付けるため、ときどき探す
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1f;
                ScanHooks();
            }
        }

        void ScanHooks()
        {
            if (equip == null)
            {
                equip = FindObjectOfType<EquipManager>();
                if (equip != null) equip.EquipmentChanged += OnEquip;
            }
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (b == null || hookedButtons.Contains(b)) continue;
                hookedButtons.Add(b);
                b.onClick.AddListener(OnButtonClick);
            }
        }

        /// <summary>落ちてきたメダルが急に止まったら「チッ」と鳴らす（当たった音のかわり）。</summary>
        void FixedUpdate()
        {
            if (game == null || game.Pool == null) return;
            clinkBudget = Mathf.Min(clinksPerSecond * 0.25f, clinkBudget + clinksPerSecond * Time.fixedDeltaTime);
            float diameter = Mathf.Max(0.01f, game.tuning.medalDiameter);
            float threshold = clinkSpeed * diameter;
            var active = game.Pool.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var m = active[i];
                if (m == null) continue;
                var rb = m.Body;
                if (rb.IsSleeping()) { lastFallSpeed.Remove(m); continue; }
                float fall = -rb.velocity.y;   // 下向きの速さ
                if (lastFallSpeed.TryGetValue(m, out float before) && before > threshold && fall < before * 0.4f)
                {
                    if (clinkBudget >= 1f)
                    {
                        clinkBudget -= 1f;
                        float strength = Mathf.Clamp01((before - threshold) / (threshold * 2f) + 0.35f);
                        // 昔の音源は音の高さが半音ずつしか変わらないので、ずらす時も半音単位にする
                        float pitch = Mathf.Pow(2f, Random.Range(-2, 4) / 12f);
                        PlayInternal(Sound.Clink, strength, pitch);
                    }
                }
                lastFallSpeed[m] = fall;
            }
            // 使われなくなったメダルの記録は、ときどき捨てる
            if (lastFallSpeed.Count > active.Count * 2 + 64) lastFallSpeed.Clear();
        }

        // ───────── 鳴らす ─────────

        void PlayInternal(Sound sound, float volume, float pitch)
        {
            if (source == null || masterVolume <= 0f) return;
            AudioClip clip;
            if (sound == Sound.Clink && clinkClips != null && clinkClips.Length > 0) clip = clinkClips[Random.Range(0, clinkClips.Length)];
            else if (!clips.TryGetValue(sound, out clip)) return;
            if (clip == null) return;

            // 穴に落ちた音は、続けて鳴りすぎないようにする
            if (sound == Sound.DropCenter || sound == Sound.DropSide)
            {
                if (lastPlayed.TryGetValue(sound, out float t) && Time.unscaledTime - t < dropInterval) return;
            }
            lastPlayed[sound] = Time.unscaledTime;

            // 設定画面の「全体」×「効果音」の大きさもかける
            float vol = masterVolume * CategoryVolume(sound) * volume * GameAudioSettings.SfxGain;
            if (vol <= 0f) return;

            // PlayOneShot は音の高さを AudioSource ごとにしか変えられないので、高さを変える時は使い捨ての音源を使う
            if (Mathf.Abs(pitch - 1f) < 0.001f)
            {
                source.PlayOneShot(clip, vol);
            }
            else
            {
                var s = GetVoice();
                s.pitch = pitch;
                s.volume = vol;
                s.clip = clip;
                s.Play();
            }
        }

        // 高さを変えて鳴らす用の音源（使い回す）
        readonly List<AudioSource> voices = new List<AudioSource>();
        int nextVoice;
        const int VoiceCount = 12;

        AudioSource GetVoice()
        {
            if (voices.Count < VoiceCount)
            {
                var s = MakeSource();
                voices.Add(s);
                return s;
            }
            // 鳴っていない音源があればそれを、なければいちばん古いものを使う
            for (int i = 0; i < voices.Count; i++)
                if (!voices[i].isPlaying) return voices[i];
            nextVoice = (nextVoice + 1) % voices.Count;
            return voices[nextVoice];
        }

        float CategoryVolume(Sound sound)
        {
            switch (sound)
            {
                case Sound.Insert: return insertVolume;
                case Sound.Clink: return clinkVolume;
                case Sound.DropCenter:
                case Sound.DropSide: return dropVolume;
                case Sound.Equip:
                case Sound.Click: return uiVolume;
                default: return jingleVolume;
            }
        }

        // ───────── 音を作る ─────────

        void MakeClips()
        {
            // メダルを入れた：コインの「ピロリン」（短い高い音のあと、もっと高い音をのばす）
            {
                var d = Buffer(0.42f);
                Noise(d, 0f, 0.025f, 9000f, true, 0.25f, 0f);
                Tone(d, 0f, 0.06f, M(83), M(83), Wave.Pulse50, 0.55f, 0f);
                Tone(d, 0.06f, 0.36f, M(88), M(88), Wave.Pulse50, 0.55f, 7f);
                clips[Sound.Insert] = Make("Sfx_Insert", d);
            }

            // メダルが当たった：金属っぽい短い雑音と、高い「チッ」（4 種類）
            clinkClips = new AudioClip[4];
            for (int v = 0; v < clinkClips.Length; v++)
            {
                var rng = new System.Random(100 + v);
                var d = Buffer(0.09f);
                Noise(d, 0f, RandRange(rng, 0.035f, 0.07f), RandRange(rng, 7000f, 14000f), true, 0.55f, 55f);
                float f = M(94 + rng.Next(0, 8));
                Tone(d, 0f, 0.03f, f, f * 0.94f, Wave.Pulse12, 0.35f, 70f);
                clinkClips[v] = Make("Sfx_Clink" + v, d);
            }
            clips[Sound.Clink] = clinkClips[0];

            // 中央の穴：上がっていく「ピロリロン」と、小さなこだま
            {
                var d = Buffer(0.62f);
                int[] up = { 84, 88, 91, 96 };
                for (int k = 0; k < 2; k++)   // k = 1 がこだま（少し遅れて、小さく）
                {
                    float start = k * 0.13f;
                    float gain = k == 0 ? 0.55f : 0.2f;
                    for (int i = 0; i < up.Length; i++)
                    {
                        bool last = i == up.Length - 1;
                        Tone(d, start + i * 0.035f, last ? 0.36f : 0.035f, M(up[i]), M(up[i]), Wave.Pulse25, gain, last ? 9f : 0f);
                    }
                }
                clips[Sound.DropCenter] = Make("Sfx_DropCenter", d);
            }

            // 端の穴：下がっていく、にぶい「ボッ」
            {
                var d = Buffer(0.2f);
                Tone(d, 0f, 0.16f, 220f, 55f, Wave.Triangle, 0.9f, 14f);
                Noise(d, 0f, 0.06f, 1800f, false, 0.3f, 40f);
                clips[Sound.DropSide] = Make("Sfx_DropSide", d);
            }

            // ラウンドの始まり：上がっていく 4 音のあと、高い音をのばす。下に三角の波でベース
            {
                var d = Buffer(0.75f);
                int[] up = { 79, 84, 88, 91 };
                for (int i = 0; i < up.Length; i++) Tone(d, i * 0.08f, 0.08f, M(up[i]), M(up[i]), Wave.Pulse12, 0.5f, 0f);
                Tone(d, 0.32f, 0.43f, M(96), M(96), Wave.Pulse12, 0.5f, 6f);
                Tone(d, 0f, 0.32f, M(48), M(48), Wave.Triangle, 0.7f, 0f);
                Tone(d, 0.32f, 0.43f, M(60), M(60), Wave.Triangle, 0.7f, 5f);
                clips[Sound.RoundStart] = Make("Sfx_RoundStart", d);
            }

            // 投入を使い切った：「ポン ポン」（下がる 2 音）
            {
                var d = Buffer(0.45f);
                Tone(d, 0f, 0.15f, M(79), M(79), Wave.Pulse50, 0.5f, 14f);
                Tone(d, 0.2f, 0.25f, M(76), M(76), Wave.Pulse50, 0.5f, 11f);
                clips[Sound.OutOfMedals] = Make("Sfx_OutOfMedals", d);
            }

            // ノルマ達成：ファンファーレ（上がる 4 音のあと、和音を「ピロピロ」と速く切りかえてのばす）
            {
                var d = Buffer(1.4f);
                int[] up = { 72, 76, 79, 84 };
                for (int i = 0; i < up.Length; i++) Tone(d, i * 0.09f, 0.09f, M(up[i]), M(up[i]), Wave.Pulse25, 0.5f, 0f);
                Arpeggio(d, 0.36f, 1.0f, new[] { 84, 88, 91 }, 1f / 40f, Wave.Pulse25, 0.5f, 2.6f);
                Tone(d, 0f, 0.36f, M(48), M(48), Wave.Triangle, 0.7f, 0f);
                Tone(d, 0.36f, 1.0f, M(48), M(48), Wave.Triangle, 0.7f, 2.4f);
                Noise(d, 0.36f, 0.35f, 6000f, false, 0.25f, 9f);   // シャーン
                clips[Sound.Clear] = Make("Sfx_Clear", d);
            }

            // ノルマ未達：下がっていく 4 音。最後の音はゆれながら消える
            {
                var d = Buffer(1.15f);
                int[] down = { 67, 66, 65, 64 };
                for (int i = 0; i < down.Length; i++)
                {
                    bool last = i == down.Length - 1;
                    Tone(d, i * 0.2f, last ? 0.55f : 0.2f, M(down[i]), M(down[i]), Wave.Pulse50, 0.45f, last ? 4f : 0f, last ? 0.025f : 0f);
                    Tone(d, i * 0.2f, last ? 0.55f : 0.2f, M(down[i] - 12), M(down[i] - 12), Wave.Triangle, 0.5f, last ? 4f : 0f);
                }
                clips[Sound.Fail] = Make("Sfx_Fail", d);
            }

            // ショップ：お店のドアの「ピンポーン」と、小さなこだま
            {
                var d = Buffer(1.3f);
                for (int k = 0; k < 2; k++)
                {
                    float start = k * 0.16f;
                    float gain = k == 0 ? 0.5f : 0.18f;
                    Tone(d, start, 0.32f, M(88), M(88), Wave.Pulse25, gain, 5f);
                    Tone(d, start + 0.35f, 0.75f, M(84), M(84), Wave.Pulse25, gain, 3.5f);
                }
                clips[Sound.Shop] = Make("Sfx_Shop", d);
            }

            // レコードを付けかえた：「カシャッ」と上がる「ピッ」
            {
                var d = Buffer(0.14f);
                Noise(d, 0f, 0.035f, 11000f, true, 0.4f, 60f);
                Tone(d, 0f, 0.06f, 600f, 1400f, Wave.Pulse50, 0.45f, 25f);
                Tone(d, 0.07f, 0.05f, M(88), M(88), Wave.Pulse50, 0.35f, 40f);
                clips[Sound.Equip] = Make("Sfx_Equip", d);
            }

            // ボタン：短い「ピッ」
            {
                var d = Buffer(0.05f);
                Tone(d, 0f, 0.04f, 1200f, 1500f, Wave.Pulse50, 0.5f, 45f);
                clips[Sound.Click] = Make("Sfx_Click", d);
            }
        }

        // ───────── 昔のゲーム機のような音を作る道具 ─────────

        enum Wave { Pulse12, Pulse25, Pulse50, Triangle }

        /// <summary>MIDI のノート番号（60 がド）から、音の高さ（Hz）</summary>
        static float M(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

        static float RandRange(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static float[] Buffer(float seconds) => new float[Mathf.Max(1, (int)(Rate * seconds))];

        /// <summary>音の大きさを 16 段にする（昔の音源と同じで、なめらかに変わらない）</summary>
        static float Steps16(float v) => Mathf.Round(Mathf.Clamp01(v) * 15f) / 15f;

        /// <summary>波の形（phase は 0〜1）</summary>
        static float WaveAt(Wave w, float phase)
        {
            switch (w)
            {
                case Wave.Pulse12: return phase < 0.125f ? 1f : -1f;
                case Wave.Pulse25: return phase < 0.25f ? 1f : -1f;
                case Wave.Pulse50: return phase < 0.5f ? 1f : -1f;
                default:
                    // 三角の波を 16 段の階段にする（昔の音源の三角波のざらっとした感じ）
                    float tri = 1f - 4f * Mathf.Abs(phase - 0.5f);
                    return Mathf.Round(tri * 7.5f) / 7.5f;
            }
        }

        /// <summary>
        /// 1 つの音を data の start 秒から足す。高さは f0 から f1 へなめらかに変わる（同じなら変わらない）。
        /// decay：消える速さ（0 なら同じ大きさのまま）。vibrato：音の高さのゆれの深さ（0.02 で 2%）。
        /// </summary>
        static void Tone(float[] data, float start, float seconds, float f0, float f1, Wave wave, float gain, float decay, float vibrato = 0f)
        {
            int begin = Mathf.Clamp((int)(start * Rate), 0, data.Length);
            int end = Mathf.Clamp((int)((start + seconds) * Rate), 0, data.Length);
            float phase = 0f;
            for (int i = begin; i < end; i++)
            {
                float t = (i - begin) / (float)Rate;
                float k = seconds > 0f ? t / seconds : 0f;
                float f = f0 * Mathf.Pow(f1 / f0, k);                         // 高さが変わる音（スイープ）
                if (vibrato > 0f) f *= 1f + vibrato * Mathf.Sin(2f * Mathf.PI * 6f * t);
                phase = Mathf.Repeat(phase + f / Rate, 1f);
                float env = Steps16(decay > 0f ? Mathf.Exp(-t * decay) : 1f);
                data[i] += WaveAt(wave, phase) * env * gain;
            }
        }

        /// <summary>和音の音を、stepSeconds ごとに速く切りかえて鳴らす（昔のゲームの和音の出し方）</summary>
        static void Arpeggio(float[] data, float start, float seconds, int[] notes, float stepSeconds, Wave wave, float gain, float decay)
        {
            int begin = Mathf.Clamp((int)(start * Rate), 0, data.Length);
            int end = Mathf.Clamp((int)((start + seconds) * Rate), 0, data.Length);
            float phase = 0f;
            for (int i = begin; i < end; i++)
            {
                float t = (i - begin) / (float)Rate;
                int n = notes[(int)(t / stepSeconds) % notes.Length];
                phase = Mathf.Repeat(phase + M(n) / Rate, 1f);
                float env = Steps16(Mathf.Exp(-t * decay));
                data[i] += WaveAt(wave, phase) * env * gain;
            }
        }

        /// <summary>
        /// 昔の音源と同じ作り方の雑音（15 ビットの LFSR）。clockHz が高いほど「シャー」と高い音になる。
        /// metallic を true にすると、短いくり返しの雑音になって「キン」とした金属っぽい音になる。
        /// </summary>
        static void Noise(float[] data, float start, float seconds, float clockHz, bool metallic, float gain, float decay)
        {
            int begin = Mathf.Clamp((int)(start * Rate), 0, data.Length);
            int end = Mathf.Clamp((int)((start + seconds) * Rate), 0, data.Length);
            int reg = 1;
            float phase = 0f;
            float value = 1f;
            for (int i = begin; i < end; i++)
            {
                float t = (i - begin) / (float)Rate;
                phase += clockHz / Rate;
                while (phase >= 1f)
                {
                    phase -= 1f;
                    int bit = (reg ^ (reg >> (metallic ? 6 : 1))) & 1;
                    reg = (reg >> 1) | (bit << 14);
                    value = (reg & 1) != 0 ? 1f : -1f;
                }
                float env = Steps16(decay > 0f ? Mathf.Exp(-t * decay) : 1f);
                data[i] += value * env * gain;
            }
        }

        /// <summary>
        /// 作った音のデータを、いちばん大きい所が 0.8 になるようにそろえ、
        /// 音の細かさを 6 ビット（64 段）に落として AudioClip にする。最初と最後は少しだけなめらかにして「プツッ」を防ぐ。
        /// </summary>
        static AudioClip Make(string name, float[] data)
        {
            float peak = 1e-6f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float scale = 0.8f / peak;
            int fade = Mathf.Min(data.Length / 4, Rate / 1000);   // 1 ミリ秒
            for (int i = 0; i < data.Length; i++)
            {
                float v = Mathf.Round(data[i] * scale * 31f) / 31f;   // 6 ビット
                if (i < fade) v *= i / (float)fade;
                if (i > data.Length - 1 - fade) v *= (data.Length - 1 - i) / (float)fade;
                data[i] = v;
            }
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
