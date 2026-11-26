using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 音を「昔のゲーム機」のようにする（ビットクラッシャー）。AudioSource と同じ GameObject に付ける。
    /// GameAudioMixer が、効果音だけの GameObject に自動で付ける（自分で付ける必要はない）。
    ///
    /// ・音の細かさ（ビット数）を落とす：小さいほど「ジリジリ」した音になる
    /// ・1 秒あたりの音の数（サンプリングレート）を落とす：小さいほど「こもった、ザラザラ」した音になる
    /// 同じ GameObject のすべての AudioSource にかかる。
    /// </summary>
    public class ChipCrusher : MonoBehaviour
    {
        /// <summary>false にすると、すべての ChipCrusher が何もしなくなる（聞き比べ用）</summary>
        public static bool Enabled = true;

        [Tooltip("音の細かさ（ビット数）。昔のゲーム機は 4〜8 くらい")]
        [Range(2, 16)] public int bits = 6;
        [Tooltip("1 秒あたりの音の数。小さいほどザラザラする（ふつうは 44100 か 48000）")]
        [Range(2000f, 44100f)] public float sampleRate = 16000f;
        [Tooltip("かける強さ。0 で元の音、1 で全部かかった音")]
        [Range(0f, 1f)] public float mix = 1f;

        int outputRate = 48000;
        float[] held = new float[8];
        float phase;

        void Awake()
        {
            outputRate = AudioSettings.outputSampleRate;   // 音を作るスレッドからは読めないので、ここで覚えておく
        }

        // 音を作るスレッドで、音のかたまりが来るたびに呼ばれる
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!Enabled || mix <= 0f || channels <= 0) return;
            if (held.Length < channels) held = new float[channels];

            float step = Mathf.Clamp01(sampleRate / Mathf.Max(1, outputRate));
            float levels = (1 << (Mathf.Clamp(bits, 2, 16) - 1)) - 1;   // 6 ビットなら 31 段（+ と - で 63 段）

            for (int i = 0; i < data.Length; i += channels)
            {
                phase += step;
                bool take = phase >= 1f;
                if (take) phase -= 1f;
                for (int c = 0; c < channels; c++)
                {
                    float x = data[i + c];
                    if (take) held[c] = Mathf.Round(Mathf.Clamp(x, -1f, 1f) * levels) / levels;
                    data[i + c] = x + (held[c] - x) * mix;
                }
            }
        }
    }
}
