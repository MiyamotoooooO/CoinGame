using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 補充スロットの効果音（SlotRefill の続き）。音はすべて SfxSynth でコードで作る。
    /// ・リールを回す・止める・リーチ・あたり（ファンファーレ＋メダルのジャラジャラ）・777（大きいファンファーレ）
    /// ・出目ごとの演出の音：爆発・導火線・落ちる笛の音・ドスン・雷・ゴロゴロ・メダル（鉄の音）・きらきら・
    ///   貯金箱のカラカラ・宝箱のふたのギーッ・レコードの風を切る音・ダイヤのキラーン
    /// ・Play は使い捨てではなく使い回す音源（Voices）で鳴らす。音の高さを変えても、ほかの鳴っている音に影響しない
    ///   音源は 1 つずつ別の GameObject に置く（GameAudioMixer が音をざらざらにする ChipCrusher を付けるが、
    ///   ChipCrusher は 1 つの GameObject に AudioSource が 1 つの時しか使えないため）
    ///   使い回す音源は、設定の「効果音」の大きさを自分でかけ、もとの音源に ChipCrusher があれば同じように付ける
    /// </summary>
    public partial class SlotRefill
    {
        AudioClip sfxReach, sfxCoinPour, sfxRattle, sfxCreak, sfxWhoosh, sfxSparkle;

        readonly List<AudioSource> voices = new List<AudioSource>();
        int nextVoice;
        const int VoiceCount = 10;

        /// <summary>スロットのすべての音を作る（前の仮の音と入れかえる）。</summary>
        void MakeRichSounds()
        {
            var rng = new System.Random(2026);
            float[] d;

            // リールが回り出す：レバーのカチカチ（だんだん間があく）と、ばねの「ビヨン」
            d = SfxSynth.Buffer(0.32f);
            for (int i = 0; i < 7; i++)
            {
                float t = 0.012f * i * (1f + i * 0.35f);
                SfxSynth.Noise(d, t, 0.02f, 0.6f, rng, 0.6f, true, 0.001f, 160f);
                SfxSynth.MetalHit(d, t, 900f + i * 40f, 0.25f, rng, 0.25f);
            }
            SfxSynth.Tone(d, 0.04f, 0.26f, 220f, 160f, 0.3f, SfxSynth.Wave.Triangle, 0.005f, 12f, 18f, 0.06f);
            sfxSpin = SfxSynth.Make("Slot_Spin", d, 0.7f);

            // リールが止まる：「ガチャッ」
            d = SfxSynth.Buffer(0.16f);
            SfxSynth.Noise(d, 0f, 0.06f, 0.9f, rng, 0.35f, false, 0.001f, 70f);
            SfxSynth.MetalHit(d, 0f, 760f, 0.7f, rng, 0.35f);
            SfxSynth.MetalHit(d, 0.025f, 1150f, 0.35f, rng, 0.3f);
            sfxStop = SfxSynth.Make("Slot_Stop", d, 0.75f);

            // リーチ：上がっていく音のくり返しで、ドキドキさせる
            d = SfxSynth.Buffer(1.0f);
            float[] reachNotes = { 523f, 659f, 784f, 659f, 784f, 988f, 784f, 988f, 1175f, 1319f };
            for (int i = 0; i < reachNotes.Length; i++)
                SfxSynth.Tone(d, i * 0.085f, 0.11f, reachNotes[i], reachNotes[i], 0.35f, SfxSynth.Wave.Square, 0.003f, 14f);
            SfxSynth.Tone(d, 0f, 1.0f, 110f, 165f, 0.25f, SfxSynth.Wave.Saw, 0.05f, 1.5f, 12f, 0.03f);
            sfxReach = SfxSynth.Make("Slot_Reach", d, 0.7f);

            // あたり：短いファンファーレと、メダルのジャラジャラ
            d = SfxSynth.Buffer(1.5f);
            float[] win = { 784f, 988f, 1175f, 1568f };
            for (int i = 0; i < win.Length; i++)
                SfxSynth.Tone(d, i * 0.08f, 0.12f, win[i], win[i], 0.32f, SfxSynth.Wave.Square, 0.003f, 8f);
            foreach (var f in new[] { 1568f, 1976f, 2349f })
                SfxSynth.Tone(d, 0.32f, 0.8f, f, f, 0.18f, SfxSynth.Wave.Triangle, 0.005f, 3.5f, 6f, 0.008f);
            SfxSynth.CoinPour(d, 0.3f, 1.0f, 26, 0.35f, rng);
            sfxWin = SfxSynth.Make("Slot_Win", d, 0.85f);

            // 777：太鼓つきの大きいファンファーレと、たくさんのメダル
            d = SfxSynth.Buffer(2.4f);
            float[] big = { 523f, 523f, 523f, 659f, 784f, 659f, 784f, 1047f };
            float[] bigLen = { 0.1f, 0.1f, 0.1f, 0.2f, 0.2f, 0.1f, 0.1f, 0.5f };
            float bt = 0f;
            for (int i = 0; i < big.Length; i++)
            {
                SfxSynth.Tone(d, bt, bigLen[i] + 0.04f, big[i], big[i], 0.3f, SfxSynth.Wave.Square, 0.003f, 3f);
                SfxSynth.Tone(d, bt, bigLen[i] + 0.04f, big[i] * 0.5f, big[i] * 0.5f, 0.2f, SfxSynth.Wave.Triangle, 0.003f, 3f);
                SfxSynth.Noise(d, bt, 0.12f, 0.35f, rng, 0.5f, false, 0.001f, 30f);   // 太鼓
                SfxSynth.Tone(d, bt, 0.15f, 120f, 60f, 0.5f, SfxSynth.Wave.Sine, 0.001f, 20f);
                bt += bigLen[i];
            }
            foreach (var f in new[] { 1047f, 1319f, 1568f, 2093f })
                SfxSynth.Tone(d, bt - 0.5f, 1.3f, f, f, 0.14f, SfxSynth.Wave.Triangle, 0.01f, 2f, 6f, 0.01f);
            SfxSynth.CoinPour(d, bt - 0.4f, 1.4f, 60, 0.4f, rng);
            sfxBig = SfxSynth.Make("Slot_Big", d, 0.9f);

            // ポン（何かが出る・はねる）
            d = SfxSynth.Buffer(0.3f);
            SfxSynth.Tone(d, 0f, 0.25f, 420f, 140f, 0.8f, SfxSynth.Wave.Sine, 0.002f, 14f);
            SfxSynth.Noise(d, 0f, 0.12f, 0.35f, rng, 0.25f, false, 0.002f, 30f);
            sfxPop = SfxSynth.Make("Slot_Pop", d, 0.75f);

            // きらきら（ベル）：高い音を重ねて、少しずつずらす
            d = SfxSynth.Buffer(1.1f);
            float[] bell = { 2093f, 2637f, 3136f, 4186f, 3520f };
            for (int i = 0; i < bell.Length; i++)
            {
                SfxSynth.Tone(d, i * 0.045f, 0.9f, bell[i], bell[i], 0.3f, SfxSynth.Wave.Sine, 0.002f, 5f);
                SfxSynth.Tone(d, i * 0.045f, 0.6f, bell[i] * 2.01f, bell[i] * 2.01f, 0.08f, SfxSynth.Wave.Sine, 0.002f, 9f);
            }
            sfxBell = SfxSynth.Make("Slot_Bell", d, 0.65f);

            // ドスン：重い物が落ちた
            d = SfxSynth.Buffer(0.5f);
            SfxSynth.Tone(d, 0f, 0.45f, 90f, 38f, 1f, SfxSynth.Wave.Sine, 0.001f, 9f);
            SfxSynth.Noise(d, 0f, 0.2f, 0.6f, rng, 0.12f, false, 0.001f, 18f);
            SfxSynth.Tone(d, 0f, 0.08f, 190f, 150f, 0.4f, SfxSynth.Wave.Triangle, 0.001f, 40f);   // 木の「コン」
            sfxThud = SfxSynth.Make("Slot_Thud", d, 0.9f);

            // レコードのスクラッチ：「キュキュッ」
            d = SfxSynth.Buffer(0.5f);
            SfxSynth.Tone(d, 0f, 0.16f, 260f, 900f, 0.35f, SfxSynth.Wave.Saw, 0.005f, 2f);
            SfxSynth.Tone(d, 0.18f, 0.2f, 900f, 220f, 0.35f, SfxSynth.Wave.Saw, 0.005f, 2f);
            SfxSynth.Noise(d, 0f, 0.4f, 0.4f, rng, 0.5f, true, 0.005f, 4f);
            sfxScratch = SfxSynth.Make("Slot_Scratch", d, 0.6f);

            // 爆発：「ドカーン」（低いドン＋こもった雑音＋パチパチ）
            d = SfxSynth.Buffer(1.6f);
            SfxSynth.Tone(d, 0f, 1.2f, 110f, 32f, 1f, SfxSynth.Wave.Sine, 0.002f, 3.5f);
            SfxSynth.Noise(d, 0f, 1.4f, 1.1f, rng, 0.08f, false, 0.002f, 3.2f);
            SfxSynth.Noise(d, 0f, 0.25f, 0.5f, rng, 0.6f, false, 0.001f, 14f);
            SfxSynth.Noise(d, 0.15f, 1.2f, 0.35f, rng, 0.7f, true, 0.01f, 3f, 0.18f);
            sfxBoom = SfxSynth.Make("Slot_Boom", d, 0.95f);

            // 導火線：「シュシュッ」（高い雑音がパチパチとぎれる）
            d = SfxSynth.Buffer(0.3f);
            SfxSynth.Noise(d, 0f, 0.3f, 0.6f, rng, 0.9f, true, 0.01f, 0f, 0.5f);
            sfxSizzle = SfxSynth.Make("Slot_Sizzle", d, 0.45f);

            // 落ちてくる：「ヒューーッ」
            d = SfxSynth.Buffer(0.7f);
            SfxSynth.Tone(d, 0f, 0.7f, 1900f, 550f, 0.4f, SfxSynth.Wave.Sine, 0.03f, 0.8f, 7f, 0.015f);
            SfxSynth.Noise(d, 0f, 0.7f, 0.08f, rng, 0.3f, false, 0.1f, 1f);
            sfxFall = SfxSynth.Make("Slot_Fall", d, 0.55f);

            // 雷：「バリッ」（鋭い雑音）
            d = SfxSynth.Buffer(0.9f);
            SfxSynth.Noise(d, 0f, 0.15f, 1f, rng, 1f, true, 0.001f, 18f, 0.6f);
            SfxSynth.Noise(d, 0.02f, 0.85f, 0.8f, rng, 0.15f, false, 0.003f, 4f);
            sfxCrash = SfxSynth.Make("Slot_Crash", d, 0.9f);

            // ゴロゴロ：こもった雑音がゆれる
            d = SfxSynth.Buffer(1.5f);
            SfxSynth.Noise(d, 0f, 1.5f, 1f, rng, 0.03f, false, 0.2f, 1.2f, 0.25f);
            SfxSynth.Tone(d, 0f, 1.5f, 50f, 42f, 0.4f, SfxSynth.Wave.Sine, 0.2f, 1.3f, 3f, 0.1f);
            sfxRumble = SfxSynth.Make("Slot_Rumble", d, 0.8f);

            // メダル 1 枚：鉄の「チャリン」
            d = SfxSynth.Buffer(0.35f);
            SfxSynth.MetalHit(d, 0f, 1950f, 1f, rng);
            SfxSynth.MetalHit(d, 0.07f, 2200f, 0.35f, rng, 0.7f);
            sfxClink = SfxSynth.Make("Slot_Clink", d, 0.7f);

            // メダルがたくさんこぼれる：「ジャラジャラ」
            d = SfxSynth.Buffer(1.3f);
            SfxSynth.CoinPour(d, 0f, 1.0f, 34, 0.6f, rng);
            sfxCoinPour = SfxSynth.Make("Slot_CoinPour", d, 0.8f);

            // 貯金箱をふる：「カラカラッ」（中のメダルと、陶器の音）
            d = SfxSynth.Buffer(0.28f);
            for (int i = 0; i < 5; i++)
            {
                float t = i * 0.035f + SfxSynth.Rand(rng, 0f, 0.01f);
                SfxSynth.MetalHit(d, t, SfxSynth.Rand(rng, 1250f, 1650f), SfxSynth.Rand(rng, 0.4f, 0.8f), rng, 0.4f);
            }
            SfxSynth.Noise(d, 0f, 0.05f, 0.5f, rng, 0.3f, false, 0.001f, 60f);
            SfxSynth.Tone(d, 0f, 0.1f, 620f, 560f, 0.25f, SfxSynth.Wave.Triangle, 0.001f, 35f);
            sfxRattle = SfxSynth.Make("Slot_Rattle", d, 0.7f);

            // 宝箱のふた：「ギーッ」（ひっかかりながら回る低い音）
            d = SfxSynth.Buffer(0.55f);
            SfxSynth.Tone(d, 0f, 0.5f, 95f, 150f, 0.6f, SfxSynth.Wave.Saw, 0.03f, 1.5f, 23f, 0.18f);
            SfxSynth.Noise(d, 0f, 0.5f, 0.2f, rng, 0.25f, false, 0.03f, 2f, 0.4f);
            sfxCreak = SfxSynth.Make("Slot_Creak", d, 0.55f);

            // レコードが飛ぶ：「ヒュン」
            d = SfxSynth.Buffer(0.4f);
            SfxSynth.Noise(d, 0f, 0.4f, 1f, rng, 0.25f, false, 0.15f, 9f);
            SfxSynth.Tone(d, 0f, 0.35f, 500f, 1300f, 0.2f, SfxSynth.Wave.Sine, 0.1f, 6f);
            sfxWhoosh = SfxSynth.Make("Slot_Whoosh", d, 0.6f);

            // ダイヤ：「キラーン」
            d = SfxSynth.Buffer(1.3f);
            SfxSynth.Tone(d, 0f, 1.2f, 2400f, 3300f, 0.35f, SfxSynth.Wave.Sine, 0.005f, 2.6f, 9f, 0.006f);
            SfxSynth.Tone(d, 0.05f, 1.1f, 3600f, 4900f, 0.18f, SfxSynth.Wave.Sine, 0.005f, 3f, 11f, 0.006f);
            for (int i = 0; i < 6; i++)
                SfxSynth.Tone(d, 0.1f + i * 0.07f, 0.3f, 4186f + i * 300f, 4186f + i * 300f, 0.08f, SfxSynth.Wave.Sine, 0.002f, 14f);
            sfxSparkle = SfxSynth.Make("Slot_Sparkle", d, 0.6f);
        }

        /// <summary>使い回す音源で鳴らす（音の高さを変える時用）。</summary>
        void PlayVoice(AudioClip clip, float vol, float pitch)
        {
            var v = GetVoice();
            // もとの音源と同じ音（ざらざら）にする
            if (audioSource != null && audioSource.GetComponent<ChipCrusher>() != null && v.GetComponent<ChipCrusher>() == null)
                v.gameObject.AddComponent<ChipCrusher>();
            v.pitch = pitch;
            v.volume = vol * GameAudioSettings.SfxGain;
            v.clip = clip;
            v.Play();
        }

        /// <summary>使い回す音源を 1 つ返す（鳴っていない物か、いちばん古い物）。</summary>
        AudioSource GetVoice()
        {
            if (voices.Count < VoiceCount)
            {
                var host = new GameObject("SlotVoice" + voices.Count);
                host.transform.SetParent(audioSource != null ? audioSource.transform : transform, false);
                var s = host.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.ignoreListenerVolume = true;   // 大きさは Play で自分でかける（GameAudioMixer には任せない）
                voices.Add(s);
                return s;
            }
            for (int i = 0; i < voices.Count; i++)
                if (!voices[i].isPlaying) return voices[i];
            nextVoice = (nextVoice + 1) % voices.Count;
            return voices[nextVoice];
        }
    }
}
