using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 効果音をコードで作るための道具（音のファイルはいらない）。
    /// 音のデータ（float の配列）を用意して、そこに「たたいた金属」「雑音」「音程のある音」などを足していき、
    /// 最後に Make で AudioClip にする。スロットの音（SlotRefillSfx.cs）が使う。
    /// </summary>
    public static class SfxSynth
    {
        public const int Rate = 44100;

        public enum Wave { Sine, Square, Triangle, Saw }

        /// <summary>seconds 秒ぶんの、空の音のデータ。</summary>
        public static float[] Buffer(float seconds) => new float[Mathf.Max(1, (int)(Rate * seconds))];

        public static float Rand(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static float Osc(Wave wave, float phase)
        {
            float p = phase - Mathf.Floor(phase);   // 0〜1
            switch (wave)
            {
                case Wave.Square: return p < 0.5f ? 0.8f : -0.8f;
                case Wave.Triangle: return 1f - 4f * Mathf.Abs(p - 0.5f);
                case Wave.Saw: return 2f * p - 1f;
                default: return Mathf.Sin(2f * Mathf.PI * p);
            }
        }

        static int Index(float seconds) => Mathf.Max(0, (int)(seconds * Rate));

        /// <summary>
        /// 音程のある音を足す。周波数は f0 から f1 へ（同じならそのまま）。
        /// attack 秒でふくらみ、decay（大きいほど早く消える）で消えていく。vibrato はゆれ（Hz）。
        /// </summary>
        public static void Tone(float[] d, float start, float seconds, float f0, float f1, float gain, Wave wave = Wave.Sine,
            float attack = 0.005f, float decay = 0f, float vibrato = 0f, float vibratoDepth = 0f)
        {
            int a = Index(start), b = Mathf.Min(d.Length, Index(start + seconds));
            float phase = 0f;
            for (int i = a; i < b; i++)
            {
                float t = (i - a) / (float)Rate;
                float k = t / Mathf.Max(0.0001f, seconds);
                float f = Mathf.Lerp(f0, f1, k);
                if (vibrato > 0f) f *= 1f + vibratoDepth * Mathf.Sin(2f * Mathf.PI * vibrato * t);
                phase += f / Rate;
                float env = Mathf.Clamp01(t / Mathf.Max(0.0001f, attack)) * Mathf.Exp(-t * decay);
                float tail = Mathf.Clamp01((seconds - t) / 0.01f);   // 終わりでプツッといわないように
                d[i] += Osc(wave, phase) * gain * env * tail;
            }
        }

        /// <summary>
        /// 雑音を足す。lowpass（0〜1。小さいほどこもった低い音、1 でそのまま）、highpass で高い音だけにもできる。
        /// crackle（0〜1）を上げると、パチパチととぎれる。
        /// </summary>
        public static void Noise(float[] d, float start, float seconds, float gain, System.Random rng,
            float lowpass = 1f, bool highpass = false, float attack = 0.002f, float decay = 0f, float crackle = 0f)
        {
            int a = Index(start), b = Mathf.Min(d.Length, Index(start + seconds));
            float lp = 0f, prev = 0f, crack = 1f;
            for (int i = a; i < b; i++)
            {
                float t = (i - a) / (float)Rate;
                float n = Rand(rng, -1f, 1f);
                lp += (n - lp) * Mathf.Clamp01(lowpass);
                float v = lp;
                if (highpass) { v = lp - prev; prev = lp; }
                if (crackle > 0f && (i & 63) == 0) crack = rng.NextDouble() < crackle ? Rand(rng, 0.4f, 1.6f) : 0.1f;
                if (crackle > 0f) v *= crack;
                float env = Mathf.Clamp01(t / Mathf.Max(0.0001f, attack)) * Mathf.Exp(-t * decay);
                float tail = Mathf.Clamp01((seconds - t) / 0.01f);
                d[i] += v * gain * env * tail;
            }
        }

        // 鉄の円板（メダル）をたたいた時の響き：倍音が整数倍からずれているので、金属らしいにごった音になる
        static readonly float[] MetalRatio = { 1.00f, 1.71f, 2.33f, 2.95f, 3.62f };
        static readonly float[] MetalAmp = { 0.50f, 0.35f, 0.28f, 0.20f, 0.12f };
        static readonly float[] MetalDecay = { 22f, 30f, 38f, 48f, 60f };

        /// <summary>鉄のメダルを 1 回たたいた音を足す（「チャリン」の 1 粒）。ring を大きくすると長く響く。</summary>
        public static void MetalHit(float[] d, float start, float f0, float gain, System.Random rng, float ring = 1f)
        {
            int a = Index(start);
            if (a >= d.Length) return;
            int parts = MetalRatio.Length;
            var detune = new float[parts];
            var pa = new float[parts];
            var pb = new float[parts];
            for (int k = 0; k < parts; k++)
            {
                detune[k] = Rand(rng, 3f, 9f);
                pa[k] = Rand(rng, 0f, 6.28f);
                pb[k] = Rand(rng, 0f, 6.28f);
            }
            int b = Mathf.Min(d.Length, a + (int)(Rate * 0.3f * ring));   // 0.3 秒でほぼ消える
            float prevNoise = 0f;
            for (int i = a; i < b; i++)
            {
                float t = (i - a) / (float)Rate;
                float v = 0f;
                for (int k = 0; k < parts; k++)
                {
                    float f = f0 * MetalRatio[k];
                    v += MetalAmp[k] * 0.5f * (Mathf.Sin(2f * Mathf.PI * f * t + pa[k]) + Mathf.Sin(2f * Mathf.PI * (f + detune[k]) * t + pb[k]))
                         * Mathf.Exp(-t * MetalDecay[k] / ring);
                }
                float n = Rand(rng, -1f, 1f);
                v += (n - prevNoise * 0.9f) * Mathf.Exp(-t * 250f) * 0.35f;   // たたいた瞬間の「チッ」
                prevNoise = n;
                d[i] += v * gain;
            }
        }

        /// <summary>
        /// メダルがたくさんこぼれる「ジャラジャラ」を足す。hits 枚が seconds 秒の間に、はじめ多く・だんだん少なく落ちる。
        /// </summary>
        public static void CoinPour(float[] d, float start, float seconds, int hits, float gain, System.Random rng,
            float fMin = 1500f, float fMax = 2500f)
        {
            for (int h = 0; h < hits; h++)
            {
                float u = (float)rng.NextDouble();
                float when = start + seconds * u * u;   // はじめの方に多く
                float g = gain * Rand(rng, 0.35f, 1f) * (1f - 0.6f * u);
                MetalHit(d, when, Rand(rng, fMin, fMax), g, rng);
            }
        }

        /// <summary>いちばん大きい所が peak になるようにそろえて、AudioClip にする。</summary>
        public static AudioClip Make(string name, float[] d, float peak = 0.85f)
        {
            float max = 1e-6f;
            for (int i = 0; i < d.Length; i++) max = Mathf.Max(max, Mathf.Abs(d[i]));
            float scale = peak / max;
            int fade = Mathf.Min(d.Length / 4, Rate / 500);
            for (int i = 0; i < d.Length; i++)
            {
                float v = d[i] * scale;
                if (i < fade) v *= i / (float)fade;
                if (i > d.Length - 1 - fade) v *= (d.Length - 1 - i) / (float)fade;
                d[i] = v;
            }
            var clip = AudioClip.Create(name, d.Length, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
