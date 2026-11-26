using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MedalPusher
{
    /// <summary>
    /// 設定の音の大きさを、ゲームの中のすべての音にかける（シーンに置く必要はない。ゲームを始めると自動で作られる）。
    ///
    /// しくみ：
    /// ・BGM：AudioListener（耳）の音量を「全体 × BGM」にする。曲を流している AudioSource はそのまま
    /// ・効果音：効果音の AudioSource は耳の音量を無視する設定（ignoreListenerVolume）にして、
    ///   その AudioSource の音量に「全体 × 効果音」をかける
    /// ・どちらの音かは、AudioSource が入っている変数の名前などで見分ける（下の IsMusic）
    ///   名前に bgm / music / beat が入っていれば BGM、それ以外は効果音
    /// ・効果音だけの GameObject には、ビット感を出す ChipCrusher を付ける（コードで作った効果音も、昔のゲーム機のような音になる）
    ///
    /// 自分で音量を管理しているスクリプト（GameSfx など。ignoreListenerVolume をオンにしている物）には、何もしない。
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class GameAudioMixer : MonoBehaviour
    {
        [Tooltip("効果音だけの GameObject に、ビット感を出す ChipCrusher を付ける")]
        public bool addChipCrusher = true;

        static GameAudioMixer instance;

        readonly HashSet<int> seen = new HashSet<int>();
        readonly Dictionary<AudioSource, float> sfxSources = new Dictionary<AudioSource, float>();   // 効果音の AudioSource と、もとの音量
        readonly List<AudioSource> deadList = new List<AudioSource>();
        float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            if (instance != null) return;
            var go = new GameObject("GameAudioMixer（自動）");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameAudioMixer>();
        }

        void Awake()
        {
            GameAudioSettings.Changed += ApplySfx;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            GameAudioSettings.Changed -= ApplySfx;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (instance == this) instance = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            nextScan = 0f;   // 新しいシーンの音を、すぐに見分ける
        }

        void Update()
        {
            AudioListener.volume = GameAudioSettings.BgmGain;

            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.25f;
                Scan();
            }
        }

        /// <summary>新しくできた AudioSource を見つけて、BGM か効果音かを決める</summary>
        void Scan()
        {
            foreach (var s in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (s == null || !seen.Add(s.GetInstanceID())) continue;
                if (s.ignoreListenerVolume) continue;   // 自分で音量を管理している（GameSfx・設定画面のテスト音など）
                if (IsMusic(s)) continue;               // BGM は耳の音量（全体 × BGM）だけで変わる

                sfxSources[s] = s.volume;
                s.ignoreListenerVolume = true;
                s.volume = sfxSources[s] * GameAudioSettings.SfxGain;
                if (addChipCrusher) AddCrusher(s.gameObject);
            }

            // 消えた AudioSource は忘れる
            deadList.Clear();
            foreach (var kv in sfxSources) if (kv.Key == null) deadList.Add(kv.Key);
            foreach (var d in deadList) sfxSources.Remove(d);
        }

        /// <summary>設定が変わった時：効果音の AudioSource の音量をかけ直す</summary>
        void ApplySfx()
        {
            float g = GameAudioSettings.SfxGain;
            foreach (var kv in sfxSources)
                if (kv.Key != null) kv.Key.volume = kv.Value * g;
        }

        /// <summary>
        /// BGM の AudioSource か。
        /// ・GameObject の名前に Music / BGM が入っている（RecordAudioPlayer の曲は「RecordMusic」）
        /// ・同じ GameObject のスクリプトの、この AudioSource が入っている変数の名前に bgm / music / beat が入っている
        /// ・レコード図鑑の試聴（RecordDexScreen）
        /// ・くり返し鳴らす長い音（5 秒より長い）
        /// </summary>
        static bool IsMusic(AudioSource s)
        {
            string goName = s.gameObject.name.ToLowerInvariant();
            if (goName.Contains("music") || goName.Contains("bgm")) return true;

            foreach (var mb in s.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                if (mb is RecordDexScreen) return true;
                for (var t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType != typeof(AudioSource)) continue;
                        if (!ReferenceEquals(f.GetValue(mb), s)) continue;
                        string n = f.Name.ToLowerInvariant();
                        if (n.Contains("bgm") || n.Contains("music") || n.Contains("beat")) return true;
                    }
                }
            }

            return s.loop && s.clip != null && s.clip.length > 5f;
        }

        /// <summary>
        /// 効果音だけの GameObject に ChipCrusher を付ける。
        /// （フィルターは同じ GameObject のすべての音にかかるので、BGM がいっしょにある所には付けない）
        /// </summary>
        static void AddCrusher(GameObject go)
        {
            if (go.GetComponent<ChipCrusher>() != null) return;
            if (go.GetComponent<AudioListener>() != null) return;
            foreach (var other in go.GetComponents<AudioSource>())
                if (other.ignoreListenerVolume == false && IsMusic(other)) return;
            go.AddComponent<ChipCrusher>();
        }
    }
}
