using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// ゲーム全体の効果音。音はすべてコードで作る（音のファイルはいらない）。
    /// 鳴らす時：
    /// ・メダルを入れた時（チャリン）
    /// ・メダルが落ちて、ほかのメダルや床に当たった時（ジャラッ。鉄のメダルがぶつかる音。たくさん鳴りすぎないように数を制限）
    /// ・メダルが中央の穴に落ちた時（ピロン）／端の穴に落ちた時（ポトッ）
    /// ・ラウンドの始まり・投入を使い切った時・ノルマ達成・ノルマ未達・ショップ
    /// ・レコードを付けかえた時、画面のボタンを押した時
    /// ほかのスクリプトからは GameSfx.Play(GameSfx.Sound.Click) のように鳴らせる。
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
            Clink,        // メダル同士・床に当たった（ジャラッ）
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

        [Header("音の大きさ")]
        [Tooltip("全体の大きさ。0 で効果音をすべて消す")]
        [Range(0f, 1f)] public float masterVolume = 0.7f;
        [Tooltip("メダルを入れた音")]
        [Range(0f, 1f)] public float insertVolume = 0.6f;
        [Tooltip("メダルが当たる音（ジャラッ）")]
        [Range(0f, 1f)] public float clinkVolume = 0.35f;
        [Tooltip("穴に落ちた音")]
        [Range(0f, 1f)] public float dropVolume = 0.6f;
        [Tooltip("ラウンドの始まり・達成・未達・ショップの音")]
        [Range(0f, 1f)] public float jingleVolume = 0.6f;
        [Tooltip("ボタン・レコードを付けかえた音")]
        [Range(0f, 1f)] public float uiVolume = 0.5f;

        [Header("メダルが当たる音")]
        [Tooltip("1 秒に鳴らすいちばん多い回数（多すぎるとうるさいので）")]
        [Range(1, 40)] public int clinksPerSecond = 18;
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
            SceneManager.sceneLoaded += (scene, mode) => AttachIfNeeded();
            AttachIfNeeded();
        }

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
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // 2D（どこにいても同じ大きさ）
            MakeClips();
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

        void OnInserted(Medal m) => PlayInternal(Sound.Insert, 1f, Random.Range(0.95f, 1.05f));

        void OnDropped(Medal m, DropZoneType type)
        {
            if (type == DropZoneType.Center) PlayInternal(Sound.DropCenter, 1f, Random.Range(0.98f, 1.04f));
            else if (type == DropZoneType.Side) PlayInternal(Sound.DropSide, 1f, Random.Range(0.9f, 1.1f));
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

        /// <summary>落ちてきたメダルが急に止まったら「ジャラッ」と鳴らす（当たった音のかわり）。</summary>
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
                        PlayInternal(Sound.Clink, strength, Random.Range(0.85f, 1.25f));
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

            // PlayOneShot は音の高さを AudioSource ごとにしか変えられないので、高さを変える時は使い捨ての音源を使う
            float vol = masterVolume * CategoryVolume(sound) * volume;
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
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
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
            // メダルを入れた：鉄のメダルが 3 回はねる「チャリン」と、転がる「ジャー」
            {
                var rng = new System.Random(7);
                var d = new float[(int)(Rate * 0.45f)];
                MetalHit(d, 1900f, 0f, 1f, rng);
                MetalHit(d, 2300f, 0.09f, 0.6f, rng);
                MetalHit(d, 2100f, 0.15f, 0.35f, rng);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    d[i] += Noise() * (0.5f + 0.5f * Sin(t, 38f)) * Mathf.Exp(-Mathf.Abs(t - 0.2f) * 12f) * 0.12f;
                }
                clips[Sound.Insert] = Make("Sfx_Insert", d);
            }

            // メダルが当たった：鉄のメダルが 2〜4 枚ぶつかる「ジャラッ」（4 種類）
            clinkClips = new AudioClip[4];
            for (int v = 0; v < clinkClips.Length; v++)
            {
                var rng = new System.Random(100 + v);
                var d = new float[(int)(Rate * 0.32f)];
                int hits = 2 + v % 3;
                for (int h = 0; h < hits; h++)
                {
                    float f0 = RandRange(rng, 1500f, 2400f);
                    float start = h == 0 ? 0f : RandRange(rng, 0.02f, 0.11f);
                    float gain = h == 0 ? 1f : RandRange(rng, 0.4f, 0.8f);
                    MetalHit(d, f0, start, gain, rng);
                }
                clinkClips[v] = Make("Sfx_Clink" + v, d);
            }
            clips[Sound.Clink] = clinkClips[0];

            // 中央の穴：明るい「ピロン」
            clips[Sound.DropCenter] = Tone("Sfx_DropCenter", 0.42f, t =>
            {
                float f = Note(t, 0.06f, 1047f, 1568f);
                float env = t < 0.06f ? 1f : Mathf.Exp(-(t - 0.06f) * 9f);
                return (Sin(t, f) * 0.55f + Sin(t, f * 2f) * 0.15f) * env * 0.8f;
            });

            // 端の穴：にぶい「ポトッ」
            clips[Sound.DropSide] = Tone("Sfx_DropSide", 0.2f, t =>
                (Sin(t, 150f - 220f * t) * 0.8f + Noise() * 0.15f) * Mathf.Exp(-t * 22f));

            // ラウンドの始まり：上がっていく 4 音
            clips[Sound.RoundStart] = Tone("Sfx_RoundStart", 0.6f, t =>
            {
                float f = Note(t, 0.08f, 523f, 659f, 784f, 1047f);
                float env = t < 0.32f ? 1f : Mathf.Exp(-(t - 0.32f) * 10f);
                return (Tri(t, f) * 0.5f + Sin(t, f * 2f) * 0.12f) * env * 0.6f;
            });

            // 投入を使い切った：やさしい 2 回の「ポン ポン」
            clips[Sound.OutOfMedals] = Tone("Sfx_OutOfMedals", 0.42f, t =>
            {
                float local = t < 0.2f ? t : t - 0.2f;
                return Sin(t, t < 0.2f ? 784f : 659f) * Mathf.Exp(-local * 14f) * 0.5f;
            });

            // ノルマ達成：ファンファーレ（上がる 4 音のあと、和音をのばす）
            clips[Sound.Clear] = Tone("Sfx_Clear", 1.3f, t =>
            {
                if (t < 0.4f)
                {
                    float f = Note(t, 0.1f, 523f, 659f, 784f, 1047f);
                    return (Sq(t, f) * 0.18f + Sin(t, f) * 0.3f) * 0.8f;
                }
                float k = t - 0.4f;
                float env = Mathf.Exp(-k * 2.5f);
                return (Tri(t, 1047f) * 0.3f + Tri(t, 1319f) * 0.25f + Tri(t, 1568f) * 0.22f + Sq(t, 523f) * 0.06f) * env * 0.75f;
            });

            // ノルマ未達：下がっていく 4 音
            clips[Sound.Fail] = Tone("Sfx_Fail", 1.1f, t =>
            {
                float f = Note(t, 0.2f, 392f, 349f, 311f, 262f);
                float env = t < 0.6f ? 1f : Mathf.Exp(-(t - 0.6f) * 5f);
                float wob = 1f + 0.01f * Mathf.Sin(t * 30f);
                return Tri(t, f * wob) * 0.5f * env;
            });

            // ショップ：お店のドアの「ピンポーン」
            clips[Sound.Shop] = Tone("Sfx_Shop", 1.2f, t =>
            {
                if (t < 0.35f) return (Sin(t, 1319f) * 0.5f + Sin(t, 2638f) * 0.1f) * Mathf.Exp(-t * 6f);
                float k = t - 0.35f;
                return (Sin(t, 1047f) * 0.5f + Sin(t, 2094f) * 0.1f) * Mathf.Exp(-k * 3.5f);
            });

            // レコードを付けかえた：「カチャッ」
            clips[Sound.Equip] = Tone("Sfx_Equip", 0.16f, t =>
                Noise() * Mathf.Exp(-t * 70f) * 0.45f
                + Sin(t, 1250f) * Mathf.Exp(-t * 35f) * 0.35f
                + (t > 0.05f ? Noise() * Mathf.Exp(-(t - 0.05f) * 90f) * 0.3f : 0f));

            // ボタン：短い「ポッ」
            clips[Sound.Click] = Tone("Sfx_Click", 0.07f, t =>
                (Sin(t, 950f) * 0.6f + Sin(t, 1900f) * 0.15f) * Mathf.Exp(-t * 60f));
        }

        static readonly System.Random noiseRng = new System.Random(11);
        static float Noise() => (float)(noiseRng.NextDouble() * 2.0 - 1.0);
        static float Sin(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t);
        static float Sq(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t) >= 0f ? 1f : -1f;
        static float Tri(float t, float f) => 1f - 4f * Mathf.Abs(Mathf.Repeat(f * t + 0.25f, 1f) - 0.5f);
        static float Note(float t, float step, params float[] notes) => notes[Mathf.Clamp((int)(t / step), 0, notes.Length - 1)];

        // 鉄の円板（メダル）をたたいた時の響き方：音の高さの比・大きさ・消える速さ
        // 比が整数でない（倍音がずれている）ので、ガラスやベルではなく金属らしいにごった音になる
        static readonly float[] MetalRatio = { 1.00f, 1.71f, 2.33f, 2.95f, 3.62f };
        static readonly float[] MetalAmp = { 0.50f, 0.35f, 0.28f, 0.20f, 0.12f };
        static readonly float[] MetalDecay = { 22f, 30f, 38f, 48f, 60f };

        static float RandRange(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>
        /// 鉄のメダルを 1 回たたいた音を、data の start 秒の所から足す。
        /// 少しだけずらした 2 つの音を重ねて「うなり」を作り、たたいた瞬間の「チッ」という雑音も足す。
        /// </summary>
        static void MetalHit(float[] data, float f0, float start, float gain, System.Random rng)
        {
            int begin = Mathf.Clamp((int)(start * Rate), 0, data.Length);
            int parts = MetalRatio.Length;
            var detune = new float[parts];
            var phaseA = new float[parts];
            var phaseB = new float[parts];
            for (int k = 0; k < parts; k++)
            {
                detune[k] = RandRange(rng, 3f, 9f);
                phaseA[k] = RandRange(rng, 0f, 6.28f);
                phaseB[k] = RandRange(rng, 0f, 6.28f);
            }
            float prevNoise = 0f;
            for (int i = begin; i < data.Length; i++)
            {
                float t = (i - begin) / (float)Rate;
                float v = 0f;
                for (int k = 0; k < parts; k++)
                {
                    float f = f0 * MetalRatio[k];
                    v += MetalAmp[k] * 0.5f * (Mathf.Sin(2f * Mathf.PI * f * t + phaseA[k]) + Mathf.Sin(2f * Mathf.PI * (f + detune[k]) * t + phaseB[k]))
                         * Mathf.Exp(-t * MetalDecay[k]);
                }
                // 高い音だけの雑音（たたいた瞬間の「チッ」）
                float n = RandRange(rng, -1f, 1f);
                v += (n - prevNoise * 0.9f) * Mathf.Exp(-t * 250f) * 0.35f;
                prevNoise = n;
                data[i] += v * gain;
            }
        }

        /// <summary>作った音のデータを、いちばん大きい所が 0.8 になるようにそろえて AudioClip にする。</summary>
        static AudioClip Make(string name, float[] data)
        {
            float peak = 1e-6f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float scale = 0.8f / peak;
            int fade = Mathf.Min(data.Length / 4, Rate / 500);
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i] * scale;
                if (i < fade) v *= i / (float)fade;
                if (i > data.Length - 1 - fade) v *= (data.Length - 1 - i) / (float)fade;
                data[i] = v;
            }
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>関数 f（時間 → -1〜1）から音を作る。最初と最後は少しだけなめらかにして「プツッ」を防ぐ。</summary>
        static AudioClip Tone(string name, float seconds, System.Func<float, float> f)
        {
            int n = Mathf.Max(1, (int)(Rate * seconds));
            var d = new float[n];
            int fade = Mathf.Min(n / 4, Rate / 500);   // 2 ミリ秒
            for (int i = 0; i < n; i++)
            {
                float v = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
                if (i < fade) v *= i / (float)fade;
                if (i > n - 1 - fade) v *= (n - 1 - i) / (float)fade;
                d[i] = v;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
