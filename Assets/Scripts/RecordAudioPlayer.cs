using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 流しているレコードの曲を鳴らす。RecordEffectSystem と同じシーンに置く（同じ GameObject がわかりやすい）。
    ///
    /// ・RecordEffectSystem でレコードを流すと、そのレコードの曲を頭から1回流す（くり返さない）
    /// ・曲が終わると効果も終わる（効果の長さ = 曲の長さ。RecordEffectSystem.GetDuration を参照）
    /// ・別のレコードに切り替えたときや止めたときは、フェードアウトしてから切り替える
    /// ・曲が無いレコードは無音（ターンテーブルは回る。効果は CSV の効果時間の秒数）
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
        [Tooltip("レコードの曲。ファイル名をレコードのキー（英名）にする。Project ウィンドウから複数まとめてドラッグできる")]
        public AudioClip[] clips = new AudioClip[0];

        [Header("鳴らし方")]
        [Range(0f, 1f)] public float volume = 0.6f;
        [Tooltip("曲を切り替える・止めるときのフェードアウトの秒数")]
        [Range(0f, 3f)] public float fadeSeconds = 0.3f;
        [Tooltip("画面の左に、いま流している曲を表示する（確認用）")]
        public bool showNowPlaying = true;

        /// <summary>いま鳴らしている曲のレコード（鳴らしていなければ null）</summary>
        public RecordData NowPlaying { get; private set; }

        /// <summary>
        /// いまターンテーブルに載せるレコード（= 流しているレコード）。曲が無いレコードも含む。
        /// TurntableController が見る。
        /// </summary>
        public RecordData CurrentRecord => effectSystem != null ? effectSystem.Current : null;

        AudioSource source;
        readonly Dictionary<string, AudioClip> clipByKey = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, AudioClip> resourceCache = new Dictionary<string, AudioClip>();
        int lastPlayCount;
        bool switching;            // フェードアウト中（終わったら pendingClip に切り替える）
        AudioClip pendingClip;
        RecordData pendingRecord;
        float currentVolume;
        GUIStyle labelStyle;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;   // 2D の音（カメラの位置に関係なく同じ大きさ）
            source.volume = 0f;
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

        /// <summary>レコードの曲を探す。Inspector の Clips → Resources/RecordAudio/ の順に探す。無ければ null。</summary>
        public AudioClip FindClip(RecordData record)
        {
            if (RecordData.IsNullOrEmpty(record)) return null;
            if (clipByKey.TryGetValue(record.key, out AudioClip clip)) return clip;
            if (!resourceCache.TryGetValue(record.key, out clip))
            {
                clip = Resources.Load<AudioClip>("RecordAudio/" + record.key);
                resourceCache[record.key] = clip;   // 見つからなかったことも覚えて、毎回探さないようにする
            }
            return clip;
        }

        void Update()
        {
            if (effectSystem == null) effectSystem = FindFirstObjectByType<RecordEffectSystem>();
            if (effectSystem == null) return;

            RecordData current = effectSystem.Current;

            // 新しくレコードが流された（同じレコードを流し直したときも PlayCount が増える）
            if (effectSystem.PlayCount != lastPlayCount)
            {
                lastPlayCount = effectSystem.PlayCount;
                BeginSwitch(FindClip(current), current);
            }
            // 効果が終わった・止められたのに、まだ鳴っている
            else if (current == null && NowPlaying != null && !switching)
            {
                BeginSwitch(null, null);
            }

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
            source.volume = currentVolume * volume;

            // 曲が最後まで流れた
            if (!switching && NowPlaying != null && !source.isPlaying && !AudioListener.pause && Application.isFocused)
            {
                NowPlaying = null;
                source.clip = null;
            }
        }

        /// <summary>フェードアウトしてから clip に切り替える（null なら止める）</summary>
        void BeginSwitch(AudioClip clip, RecordData record)
        {
            pendingClip = clip;
            pendingRecord = record;
            switching = true;
        }

        void StartClip(AudioClip clip, RecordData record)
        {
            source.Stop();
            source.clip = clip;
            NowPlaying = clip != null ? record : null;
            if (clip == null) return;
            source.time = 0f;
            source.Play();
            currentVolume = 1f;   // 曲の頭から、すぐに聞こえるようにする
        }

        void OnGUI()
        {
            if (!showNowPlaying) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft, fontSize = 14 };
                labelStyle.normal.textColor = Color.white;
            }
            string text;
            if (NowPlaying != null && source.clip != null) text = $"♪ {NowPlaying.name}（{source.clip.name}）  {source.time:0}/{source.clip.length:0}秒";
            else if (CurrentRecord != null) text = $"♪ {CurrentRecord.name}（曲なし）";
            else text = "♪ なし";
            GUI.Box(new Rect(10f, Screen.height * 0.45f - 30f, 440f, 26f), text, labelStyle);
        }
    }
}
