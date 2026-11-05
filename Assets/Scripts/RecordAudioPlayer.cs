using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 装着しているレコードの曲を流す。RecordEffectSystem と同じ GameObject に付けて使う。
    ///
    /// ・装着しているレコードのうち曲があるものを、枠の順に BGM として流し続ける（1曲終わったら次の枠へ）
    /// ・レコードを新しく装着したら、そのレコードの曲に切り替える（曲の頭から流す）
    /// ・再生型のレコードを再生したとき、その曲があれば効果時間のあいだその曲に切り替え、
    ///   終わったら元の曲の続きに戻る
    /// ・曲の切り替えはフェードでつなぐ
    ///
    /// 曲とレコードは「ファイル名 = レコードのキー（英名）」で紐づける（Pops.mp3 → ポップス）。
    /// 曲は Inspector の Clips にまとめてドラッグする。Resources/RecordAudio/ に置いた曲も、同じ名前で自動で探す。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecordAudioPlayer : MonoBehaviour
    {
        [Header("つなぐ相手（空なら自動で探す）")]
        public RecordEffectSystem effectSystem;
        public EquipManager equipManager;

        [Header("曲")]
        [Tooltip("レコードの曲。ファイル名をレコードのキー（英名）にする。Project ウィンドウから複数まとめてドラッグできる")]
        public AudioClip[] clips = new AudioClip[0];

        [Header("鳴らし方")]
        [Range(0f, 1f)] public float volume = 0.6f;
        [Tooltip("曲を切り替えるときのフェードの秒数")]
        [Range(0f, 3f)] public float fadeSeconds = 0.8f;
        [Tooltip("装着しているレコードの曲を BGM として流す")]
        public bool playEquippedAsBgm = true;
        [Tooltip("画面の左に、いま流している曲を表示する（確認用）")]
        public bool showNowPlaying = true;

        /// <summary>いま流している曲のレコード（流していなければ null）</summary>
        public RecordData NowPlaying { get; private set; }

        AudioSource source;
        readonly Dictionary<string, AudioClip> clipByKey = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, AudioClip> resourceCache = new Dictionary<string, AudioClip>();
        readonly Dictionary<AudioClip, float> resumeTime = new Dictionary<AudioClip, float>();
        string bgmKey;          // BGM として流しているレコードのキー
        bool advanceBgm;        // BGM の曲が終わったので、次の枠へ進む
        RecordData overrideRecord;
        float overrideRemaining;
        AudioClip nextClip;
        RecordData nextRecord;
        bool fadingOut;
        float currentVolume;
        bool subscribed;
        readonly HashSet<string> lastEquippedKeys = new HashSet<string>();
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

        void OnDestroy()
        {
            if (effectSystem != null) effectSystem.RecordPlayed -= OnRecordPlayed;
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

        /// <summary>つなぐ相手を探す。RecordEffectSystem の準備が終わってから使えるように、毎フレーム確かめる。</summary>
        bool Connect()
        {
            if (effectSystem == null) effectSystem = FindFirstObjectByType<RecordEffectSystem>();
            if (effectSystem == null) return false;
            if (equipManager == null) equipManager = effectSystem.equipManager;
            if (equipManager == null) equipManager = FindFirstObjectByType<EquipManager>();
            if (equipManager == null) return false;
            if (!subscribed)
            {
                effectSystem.RecordPlayed += OnRecordPlayed;
                subscribed = true;
            }
            return true;
        }

        void OnRecordPlayed(RecordData record)
        {
            AudioClip clip = FindClip(record);
            if (clip == null) return;
            // 効果時間は RecordEffectSystem の調整（倍率・上書き）込みの値を使う
            float duration = effectSystem != null ? effectSystem.GetDuration(record) : record.duration;
            overrideRecord = record;
            overrideRemaining = duration > 0f ? duration : clip.length;
        }

        void Update()
        {
            if (!Connect()) return;

            if (overrideRecord != null)
            {
                overrideRemaining -= Time.deltaTime;
                if (overrideRemaining <= 0f) overrideRecord = null;
            }

            // 新しく装着されたレコードがあれば、その曲に切り替える（付け替えたらすぐ聞こえるように）
            RecordData newlyEquipped = null;
            foreach (RecordData r in equipManager.Equipped)
            {
                if (!lastEquippedKeys.Contains(r.key) && FindClip(r) != null)
                {
                    newlyEquipped = r;
                    break;
                }
            }
            lastEquippedKeys.Clear();
            foreach (RecordData r in equipManager.Equipped) lastEquippedKeys.Add(r.key);
            if (newlyEquipped != null)
            {
                bgmKey = newlyEquipped.key;
                advanceBgm = false;
                resumeTime.Remove(FindClip(newlyEquipped));   // 新しく付けた曲は頭から流す
            }

            // BGM の曲が最後まで流れたら、次の枠のレコードへ進む
            if (source.clip != null && !source.isPlaying && !fadingOut && overrideRecord == null && Application.isFocused)
            {
                resumeTime.Remove(source.clip);
                source.clip = null;
                NowPlaying = null;
                advanceBgm = true;
            }

            RecordData desired = DesiredRecord();
            AudioClip desiredClip = FindClip(desired);

            // 流したい曲が変わったら、フェードアウトしてから切り替える
            if (desiredClip != source.clip && !fadingOut)
            {
                fadingOut = true;
                nextClip = desiredClip;
                nextRecord = desired;
            }
            else if (fadingOut)
            {
                nextClip = desiredClip;
                nextRecord = desired;
            }

            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            if (fadingOut)
            {
                currentVolume = Mathf.MoveTowards(currentVolume, 0f, step);
                if (currentVolume <= 0f || source.clip == null)
                {
                    SwitchTo(nextClip, nextRecord);
                    fadingOut = false;
                }
            }
            else
            {
                currentVolume = Mathf.MoveTowards(currentVolume, 1f, step);
            }
            source.volume = currentVolume * volume;
        }

        void SwitchTo(AudioClip clip, RecordData record)
        {
            // 切り替える前の曲の位置を覚えておく（再生型の曲のあと、続きから戻すため）
            if (source.clip != null && source.isPlaying) resumeTime[source.clip] = source.time;

            source.Stop();
            source.clip = clip;
            NowPlaying = clip != null ? record : null;
            currentVolume = 0f;
            if (clip == null) return;

            float t = 0f;
            if (resumeTime.TryGetValue(clip, out float saved) && saved < clip.length - 1f) t = saved;
            source.time = t;
            source.Play();
        }

        void OnGUI()
        {
            if (!showNowPlaying) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft, fontSize = 14 };
                labelStyle.normal.textColor = Color.white;
            }
            string text = NowPlaying != null && source.clip != null
                ? $"♪ {NowPlaying.name}（{source.clip.name}）  {source.time:0}/{source.clip.length:0}秒"
                : "♪ 曲なし（装着しているレコードに曲がありません）";
            GUI.Box(new Rect(10f, Screen.height * 0.45f - 30f, 440f, 26f), text, labelStyle);
        }

        /// <summary>いま流すべきレコード。再生型の効果中ならその曲、それ以外は装着しているレコードを枠の順に。</summary>
        RecordData DesiredRecord()
        {
            if (overrideRecord != null) return overrideRecord;
            if (!playEquippedAsBgm) return null;

            var withClip = new List<RecordData>();
            foreach (RecordData r in equipManager.Equipped)
            {
                if (FindClip(r) != null) withClip.Add(r);
            }
            if (withClip.Count == 0)
            {
                bgmKey = null;
                return null;
            }

            // いまの BGM のレコードが装着中のままなら、それを続ける（外されていたら先頭から）
            int index = withClip.FindIndex(r => r.key == bgmKey);
            if (index < 0)
            {
                index = 0;
                advanceBgm = false;
            }
            else if (advanceBgm)
            {
                index = (index + 1) % withClip.Count;
                advanceBgm = false;
            }
            bgmKey = withClip[index].key;
            return withClip[index];
        }
    }
}
