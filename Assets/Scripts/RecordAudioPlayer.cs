using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 流しているレコードの曲を鳴らす。RecordEffectSystem と同じシーンに置く（同じ GameObject がわかりやすい）。
    ///
    /// 2026-11-08 の仕様変更（「流す」と「発動」を分けた）：
    /// ・レコードを流すと（ゲージはいらない）、その曲をくり返し流す。効果が無い間は「おとなしい音」で流れる
    /// ・Q キーで効果を発動すると、曲は止めずに、続きから「盛り上がった音」に変わる
    ///   効果は発動した時から曲1曲ぶんの秒数だけ続く（RecordEffectSystem.GetDuration を参照）
    /// ・効果が終わると、そのまま「おとなしい音」に戻って流れ続ける（無音にはならない）
    /// ・別のレコードに切り替えたときや止めたときは、フェードアウトしてから切り替える
    /// ・曲が無いレコードは無音（ターンテーブルは回る。効果は CSV の効果時間の秒数）
    ///
    /// 発動の瞬間（ブレーキ → ドロップ）：
    ///  Q を押すと、レコードにブレーキをかけたように音が下がり（Brake Seconds）、
    ///  そのあと「ドン」と低音も高音も全部そろった盛り上がった音になる（ドロップ）。
    ///  ドロップの瞬間に Dropped が呼ばれる（BoostPresentation とスピーカーが、ここで大きな演出を出す）
    ///
    /// おとなしい音の作り方は2つ：
    ///  ① 何もしなければ、同じ曲の低い音と高い音を削って（ラジオのような軽い音）、小さくする
    ///     （Calm Low Pass Hz / Calm High Pass Hz / Calm Level）
    ///  ② 曲を編集して、おとなしい版を別に用意した時は、ファイル名を「キー + _Calm」にして Clips に入れる
    ///     （例：Pops.mp3 と Pops_Calm.mp3）。ふだんは _Calm の曲、発動中は元の曲が流れる
    ///     切りかえる時は、同じ秒数の所から続きを流す（2つの曲は、長さとテンポをそろえて作る）
    ///
    /// 曲とレコードは「ファイル名 = レコードのキー（英名）」で紐づける（Pops.mp3 → ポップス）。
    /// 曲は Inspector の Clips にまとめてドラッグする。Resources/RecordAudio/ に置いた曲も、同じ名前で自動で探す。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecordAudioPlayer : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public RecordEffectSystem effectSystem;

        [Header("曲")]
        [Tooltip("レコードの曲。ファイル名をレコードのキー（英名）にする。おとなしい版は「キー_Calm」。Project ウィンドウから複数まとめてドラッグできる")]
        public AudioClip[] clips = new AudioClip[0];
        [Tooltip("おとなしい版の曲のファイル名の終わり")]
        public string calmSuffix = "_Calm";

        [Header("鳴らし方")]
        [Range(0f, 1f)] public float volume = 0.6f;
        [Tooltip("曲を切り替える・止めるときのフェードアウトの秒数")]
        [Range(0f, 3f)] public float fadeSeconds = 0.3f;

        [Header("おとなしい音（効果を発動していない間）")]
        [Tooltip("高い音を削る境目（Hz）。小さいほどこもった音になる。22000 で削らない")]
        [Range(200f, 22000f)] public float calmLowPassHz = 2600f;
        [Tooltip("低い音を削る境目（Hz）。大きいほど低音が抜けて、ラジオのような軽い音になる。10 で削らない")]
        [Range(10f, 2000f)] public float calmHighPassHz = 450f;
        [Tooltip("音量の倍率")]
        [Range(0f, 1f)] public float calmLevel = 0.45f;
        [Tooltip("効果が終わって、おとなしい音に戻る秒数")]
        [Range(0.05f, 5f)] public float calmReturnSeconds = 1.5f;

        [Header("発動の瞬間（ブレーキ → ドロップ）")]
        [Tooltip("発動した時に、レコードにブレーキをかけたように音を下げてから、盛り上がった音にする")]
        public bool brakeOnActivate = true;
        [Tooltip("ブレーキの秒数（この秒数のあとにドロップ）")]
        [Range(0.05f, 1f)] public float brakeSeconds = 0.35f;
        [Tooltip("ブレーキで下げる音の高さ（1 = 下げない）")]
        [Range(0.2f, 1f)] public float brakePitch = 0.45f;
        [Tooltip("ドロップで盛り上がった音になるまでの秒数（短いほど「ドン」となる）")]
        [Range(0.01f, 1f)] public float dropSeconds = 0.05f;

        [Tooltip("画面の左に、いま流している曲を表示する（確認用）")]
        public bool showNowPlaying = true;

        /// <summary>いま鳴らしている曲のレコード（鳴らしていなければ null）</summary>
        public RecordData NowPlaying { get; private set; }

        /// <summary>いま盛り上がった音（効果の発動中）か。0 = おとなしい、1 = 盛り上がった</summary>
        public float Hype { get; private set; }

        /// <summary>ブレーキ中か（発動してからドロップまで）</summary>
        public bool IsBraking => brakeTimer > 0f;

        /// <summary>
        /// 発動のドロップの瞬間（ブレーキが終わって、盛り上がった音になった瞬間）に呼ばれる。
        /// 曲が無い時やブレーキを使わない時は、発動と同時に呼ばれる。
        /// </summary>
        public event System.Action<RecordData> Dropped;

        /// <summary>
        /// いまターンテーブルに載せるレコード（= 流しているレコード。効果を発動していなくても入る）。曲が無いレコードも含む。
        /// TurntableController が見る。
        /// </summary>
        public RecordData CurrentRecord => effectSystem != null ? effectSystem.Loaded : null;

        AudioSource source;
        AudioLowPassFilter lowPass;
        AudioHighPassFilter highPass;
        float brakeTimer;
        RecordData dropRecord;
        readonly Dictionary<string, AudioClip> clipByKey = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, AudioClip> resourceCache = new Dictionary<string, AudioClip>();
        int lastLoadCount, lastPlayCount;
        bool wasActive;
        bool switching;            // フェードアウト中（終わったら pendingClip に切り替える）
        AudioClip pendingClip;
        RecordData pendingRecord;
        float currentVolume;
        GUIStyle labelStyle;

        void Awake()
        {
            // 曲用の AudioSource は子に作る（音を削るフィルターが、同じ GameObject のほかの音にかからないように）
            var musicGo = new GameObject("RecordMusic");
            musicGo.transform.SetParent(transform, false);
            source = musicGo.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;         // 効果が終わっても流れ続ける
            source.spatialBlend = 0f;   // 2D の音（カメラの位置に関係なく同じ大きさ）
            source.volume = 0f;
            lowPass = musicGo.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = calmLowPassHz;
            highPass = musicGo.AddComponent<AudioHighPassFilter>();
            highPass.cutoffFrequency = calmHighPassHz;
            BuildClipTable();
        }

        void OnValidate()
        {
            if (Application.isPlaying) BuildClipTable();
        }

        void BuildClipTable()
        {
            clipByKey.Clear();
            if (clips == null) return;
            foreach (AudioClip clip in clips)
            {
                if (clip != null) clipByKey[clip.name] = clip;
            }
        }

        /// <summary>レコードの曲（盛り上がった版 = 元の曲）を探す。Inspector の Clips → Resources/RecordAudio/ の順に探す。無ければ null。</summary>
        public AudioClip FindClip(RecordData record)
        {
            if (RecordData.IsNullOrEmpty(record)) return null;
            return FindByName(record.key);
        }

        /// <summary>おとなしい版の曲を探す。無ければ元の曲を返す（高い音を削って使う）。</summary>
        public AudioClip FindCalmClip(RecordData record)
        {
            if (RecordData.IsNullOrEmpty(record)) return null;
            AudioClip calm = string.IsNullOrEmpty(calmSuffix) ? null : FindByName(record.key + calmSuffix);
            return calm != null ? calm : FindClip(record);
        }

        /// <summary>おとなしい版の曲が別にあるか</summary>
        public bool HasCalmClip(RecordData record) =>
            !RecordData.IsNullOrEmpty(record) && !string.IsNullOrEmpty(calmSuffix) && FindByName(record.key + calmSuffix) != null;

        AudioClip FindByName(string name)
        {
            if (clipByKey.TryGetValue(name, out AudioClip clip)) return clip;
            if (!resourceCache.TryGetValue(name, out clip))
            {
                clip = Resources.Load<AudioClip>("RecordAudio/" + name);
                resourceCache[name] = clip;   // 見つからなかったことも覚えて、毎回探さないようにする
            }
            return clip;
        }

        void Update()
        {
            if (effectSystem == null) effectSystem = FindFirstObjectByType<RecordEffectSystem>();
            if (effectSystem == null) return;

            RecordData loaded = effectSystem.Loaded;
            bool active = effectSystem.IsActive;

            if (effectSystem.PlayCount != lastPlayCount)
            {
                // 効果を発動した：曲は止めずに、続きから盛り上がった音にする
                lastPlayCount = effectSystem.PlayCount;
                lastLoadCount = effectSystem.LoadCount;
                AudioClip main = FindClip(effectSystem.Current);
                bool wasSounding = main != null && !switching && source.isPlaying;
                if (main == null || switching || !source.isPlaying)
                    BeginSwitch(main, effectSystem.Current);            // 何も鳴っていない時だけ、頭から流す
                else if (source.clip != main)
                    SwapKeepingTime(main, effectSystem.Current);        // おとなしい版 → 元の曲（同じ秒数から）
                // 同じ曲が鳴っている時は、そのまま（下で音だけが盛り上がる）

                // ブレーキ → ドロップ（鳴っていない時は、すぐにドロップ）
                dropRecord = effectSystem.Current;
                if (brakeOnActivate && wasSounding) brakeTimer = brakeSeconds;
                else Drop();
            }
            else if (effectSystem.LoadCount != lastLoadCount)
            {
                // 新しいレコードを流した（曲だけ）：おとなしい版で流す
                lastLoadCount = effectSystem.LoadCount;
                BeginSwitch(FindCalmClip(loaded), loaded);
            }
            else if (loaded == null && NowPlaying != null && !switching)
            {
                // 止められた
                BeginSwitch(null, null);
            }
            else if (wasActive && !active && loaded != null && HasCalmClip(loaded) && !switching)
            {
                // 効果が終わった：おとなしい版の曲に、同じ秒数から切り替える（別の曲が無ければ、同じ曲のまま音だけ変わる）
                SwapKeepingTime(FindCalmClip(loaded), loaded);
            }
            wasActive = active;

            float step = fadeSeconds > 0f ? Time.unscaledDeltaTime / fadeSeconds : 1f;
            if (switching)
            {
                currentVolume = Mathf.MoveTowards(currentVolume, 0f, step);
                if (currentVolume <= 0f || !source.isPlaying)
                {
                    StartClip(pendingClip, pendingRecord);
                    switching = false;
                }
            }
            else
            {
                currentVolume = Mathf.MoveTowards(currentVolume, 1f, step);
            }

            // ブレーキ：音の高さを下げていき、終わったらドロップ
            if (brakeTimer > 0f)
            {
                brakeTimer -= Time.unscaledDeltaTime;
                if (!active) brakeTimer = 0f;   // ブレーキ中に止められた
                float t = 1f - Mathf.Clamp01(brakeTimer / Mathf.Max(0.01f, brakeSeconds));
                source.pitch = Mathf.Lerp(1f, brakePitch, t * t);
                if (brakeTimer <= 0f)
                {
                    brakeTimer = 0f;
                    source.pitch = 1f;
                    if (active) Drop();
                }
            }
            else if (source.pitch != 1f) source.pitch = 1f;

            // おとなしい音 ⇔ 盛り上がった音（ドロップは一気に、戻る時はゆっくり）
            float target = active && brakeTimer <= 0f ? 1f : 0f;
            float seconds = target > Hype ? dropSeconds : calmReturnSeconds;
            Hype = Mathf.MoveTowards(Hype, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds));
            bool separateCalm = !active && HasCalmClip(loaded);   // 別のおとなしい版を流している時は、音を削らない
            float calmAmount = separateCalm ? 0f : 1f - Hype;
            // 高さの感じ方に合わせて、Hz は掛け算で変える
            lowPass.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(Mathf.Max(200f, calmLowPassHz)), calmAmount));
            highPass.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(10f), Mathf.Log(Mathf.Max(10f, calmHighPassHz)), calmAmount));
            float mood = Mathf.Lerp(1f, calmLevel, calmAmount);
            source.volume = currentVolume * volume * mood;
        }

        /// <summary>ドロップ：盛り上がった音にして、Dropped を知らせる</summary>
        void Drop()
        {
            brakeTimer = 0f;
            if (source != null) source.pitch = 1f;
            Dropped?.Invoke(dropRecord);
        }

        /// <summary>フェードアウトしてから clip に切り替える（null なら止める）</summary>
        void BeginSwitch(AudioClip clip, RecordData record)
        {
            pendingClip = clip;
            pendingRecord = record;
            switching = true;
        }

        /// <summary>
        /// 鳴っている曲を、止めずに別の曲（おとなしい版 ⇔ 元の曲）に切り替える。いまの秒数の所から続きを流す。
        /// </summary>
        void SwapKeepingTime(AudioClip clip, RecordData record)
        {
            if (clip == null || !source.isPlaying || source.clip == null)
            {
                BeginSwitch(clip, record);
                return;
            }
            float t = source.time;
            source.clip = clip;
            NowPlaying = record;
            source.Play();
            source.time = clip.length > 0f ? Mathf.Repeat(t, clip.length) : 0f;
        }

        void StartClip(AudioClip clip, RecordData record)
        {
            source.Stop();
            source.clip = clip;
            NowPlaying = clip != null ? record : null;
            if (clip == null) return;
            source.time = 0f;
            source.Play();
            currentVolume = 1f;   // 流し始めは、すぐに聞こえるようにする
        }

        void OnGUI()
        {
            if (!showNowPlaying || !DebugHud.Visible) return;   // F2 で消せる
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft, fontSize = 14 };
                labelStyle.normal.textColor = Color.white;
            }
            string text;
            string mood = IsBraking ? "ブレーキ中" : effectSystem != null && effectSystem.IsActive ? "盛り上がった音" : "おとなしい音";
            if (NowPlaying != null && source.clip != null) text = $"♪ {NowPlaying.name}（{source.clip.name}・{mood}）  {source.time:0}/{source.clip.length:0}秒";
            else if (CurrentRecord != null) text = $"♪ {CurrentRecord.name}（曲なし）";
            else text = "♪ なし";
            GUI.Box(new Rect(10f, Screen.height * 0.45f - 30f, 520f, 26f), text, labelStyle);
        }
    }
}
