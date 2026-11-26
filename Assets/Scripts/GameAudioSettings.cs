using System;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 音の大きさの設定（全体・BGM・効果音）。設定画面（SettingsMenu）で変える。
    /// 値はパソコンに保存される（PlayerPrefs）ので、ゲームを閉じても残る。
    ///
    /// ほかのスクリプトから使う時：
    ///   GameAudioSettings.SfxGain … 効果音にかける大きさ（全体 × 効果音）
    ///   GameAudioSettings.BgmGain … BGM にかける大きさ（全体 × BGM）
    /// </summary>
    public static class GameAudioSettings
    {
        const string KeyMaster = "RecordDrop_Volume_Master";
        const string KeyBgm = "RecordDrop_Volume_Bgm";
        const string KeySfx = "RecordDrop_Volume_Sfx";

        public const float DefaultMaster = 0.8f;
        public const float DefaultBgm = 0.8f;
        public const float DefaultSfx = 0.8f;

        static bool loaded;
        static float master, bgm, sfx;

        /// <summary>値が変わった時に呼ばれる</summary>
        public static event Action Changed;

        /// <summary>全体の大きさ（0〜1）</summary>
        public static float Master { get { Load(); return master; } set { Load(); master = Mathf.Clamp01(value); Changed?.Invoke(); } }

        /// <summary>BGM（レコードの曲・ムービーの曲）の大きさ（0〜1）</summary>
        public static float Bgm { get { Load(); return bgm; } set { Load(); bgm = Mathf.Clamp01(value); Changed?.Invoke(); } }

        /// <summary>効果音の大きさ（0〜1）</summary>
        public static float Sfx { get { Load(); return sfx; } set { Load(); sfx = Mathf.Clamp01(value); Changed?.Invoke(); } }

        /// <summary>効果音にかける大きさ（全体 × 効果音）</summary>
        public static float SfxGain => Master * Sfx;

        /// <summary>BGM にかける大きさ（全体 × BGM）</summary>
        public static float BgmGain => Master * Bgm;

        /// <summary>最初の値にもどす</summary>
        public static void ResetToDefault()
        {
            Load();
            master = DefaultMaster;
            bgm = DefaultBgm;
            sfx = DefaultSfx;
            Changed?.Invoke();
        }

        /// <summary>パソコンに保存する（設定画面を閉じた時に呼ぶ）</summary>
        public static void Save()
        {
            Load();
            PlayerPrefs.SetFloat(KeyMaster, master);
            PlayerPrefs.SetFloat(KeyBgm, bgm);
            PlayerPrefs.SetFloat(KeySfx, sfx);
            PlayerPrefs.Save();
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            master = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMaster, DefaultMaster));
            bgm = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyBgm, DefaultBgm));
            sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(KeySfx, DefaultSfx));
        }
    }
}
