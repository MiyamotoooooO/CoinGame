using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N006 ジャズ「ミッドナイト・セッション」。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・盤面：紫のムード照明。上から1本のスポットライトが、中央の穴の手前をゆっくり左右に動く（床に光の輪）。
    ///   倍率が最大になると、スポットライトが金色になる
    ///   奥の壁の左に、ネオンのサックス。管の目盛りが倍率に合わせて下から光っていき、最大で金色になる。横に今の倍率「×1.26」
    /// ・ルール：
    ///   中央の穴のポイントに、今の倍率が掛かる（はじめ ×1）。倍率は下がらない
    ///   中央の穴に落ちるたびに、倍率 +stepOutside。スポットライトの輪の中に落ちると「アドリブ成功」で +stepInside
    ///   倍率は最大 ×(1 + maxAdd)。最大になると「セッション最高潮」：輪の中に落ちるたびに +goldBonus pt
    /// ・動く光を見て、発射口（左・右）と投入のタイミングを合わせる遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class JazzRecord : RecordScriptBase
    {
        public static JazzRecord Instance { get; private set; }

        [Header("倍率")]
        [Tooltip("輪の外に落ちた時に上がる倍率（CSV の +0.02）")]
        [Range(0f, 0.5f)] public float stepOutside = 0.02f;
        [Tooltip("スポットライトの輪の中に落ちた時に上がる倍率")]
        [Range(0f, 0.5f)] public float stepInside = 0.06f;
        [Tooltip("倍率の上がる分の最大（1 で ×2 まで）")]
        [Range(0.1f, 5f)] public float maxAdd = 1f;
        [Tooltip("最大の時、輪の中に落ちたら足すポイント")]
        [Range(0, 100)] public int goldBonus = 10;

        [Header("スポットライト")]
        [Tooltip("輪の半径（メダルの直径に対する倍率）")]
        [Range(0.3f, 3f)] public float spotRadius = 0.9f;
        [Tooltip("左右に1往復する秒数")]
        [Range(1f, 20f)] public float sweepSeconds = 7f;
        [Range(0f, 10f)] public float spotIntensity = 5f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip adlibSound;
        public AudioClip dropSound;
        public AudioClip maxSound;

        protected override string DefaultKey => "Jazz";
        protected override string ScreenTitle => "JAZZ";
        protected override string ScreenSubtitle => "ミッドナイト・セッション";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<JazzRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Violet = new Color32(0xa0, 0x6c, 0xff, 0xff);
        static readonly Color Amber = new Color32(0xff, 0xb3, 0x3c, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd7, 0x4a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color NeonOff = new Color32(0x30, 0x24, 0x48, 0xff);

        float add;               // 倍率の上がった分
        float spotX, spotPulse, addShown;
        bool maxed;
        float Multiplier => 1f + add;

        Light moodLight, spot;
        GameObject ring;
        Material ringMaterial;
        ParticleSystem sparkle;

        const int Segments = 10;
        readonly List<Material> segMats = new List<Material>();
        Material saxMat;
        float meterPulse;
        BoardText multText;
        AudioClip madeAdlib, madeDrop, madeMax;

        Vector3 SaxBase => new Vector3(-HalfW * 0.62f, Layout.pusherHeight + D * 0.6f, BackZ - D * 0.25f);

        float SpotZ => FrontZ + D * 0.6f;

        protected override void BuildBoard()
        {
            moodLight = PointLight("Mood", Board, new Vector3(0f, Layout.wallHeight, (FrontZ + BackZ) * 0.5f), Violet, Layout.boardWidth * 1.4f);
            var spotGo = new GameObject("Spot");
            spotGo.transform.SetParent(Board, false);
            spot = spotGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 22f;
            spot.range = Layout.wallHeight * 3f;
            spot.shadows = LightShadows.None;
            spot.color = Amber;
            spot.intensity = 0f;
            ringMaterial = Glow(Amber, 1.2f);
            ring = Shape(PrimitiveType.Cylinder, "SpotRing", Board, Vector3.zero, new Vector3(D * spotRadius * 2f, D * 0.004f, D * spotRadius * 2f), ringMaterial);
            sparkle = Particles("Sparkle", Board, Vector3.up, D * 2f, D * 0.1f, 0.9f, 0.2f, 40f);
            BuildSax();
        }

        /// <summary>奥の壁の左のネオンのサックス：まっすぐな管（目盛り Segments 個）、下で曲がって上を向くベル、上の吹き口</summary>
        void BuildSax()
        {
            var root = new GameObject("NeonSax").transform;
            root.SetParent(Board, false);
            root.position = SaxBase;
            root.rotation = Quaternion.Euler(0f, 0f, -12f);   // 少し傾ける
            saxMat = Glow(Gold, 1f);
            float segH = D * 0.32f, w = D * 0.36f;
            for (int i = 0; i < Segments; i++)
            {
                var m = Glow(NeonOff, 0.2f);
                Cube($"Seg{i}", root, new Vector3(0f, D * 0.5f + segH * (i + 0.5f), 0f), new Vector3(w, segH * 0.8f, D * 0.08f), m);
                segMats.Add(m);
            }
            float top = D * 0.5f + segH * Segments;
            // 管のふち（金のネオンの線）
            Cube("EdgeL", root, new Vector3(-w * 0.62f, top * 0.5f + D * 0.25f, 0f), new Vector3(D * 0.04f, top - D * 0.5f, D * 0.04f), saxMat);
            Cube("EdgeR", root, new Vector3(w * 0.62f, top * 0.5f + D * 0.25f, 0f), new Vector3(D * 0.04f, top - D * 0.5f, D * 0.04f), saxMat);
            // 下で曲がって、右上を向くベル
            var bend = Shape(PrimitiveType.Cylinder, "Bend", root, new Vector3(w * 0.6f, D * 0.3f, 0f), new Vector3(w * 1.2f, D * 0.04f, w * 1.2f), saxMat);
            bend.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var bell = Shape(PrimitiveType.Cylinder, "Bell", root, new Vector3(w * 1.5f, D * 0.75f, 0f), new Vector3(w * 1.6f, D * 0.03f, w * 1.6f), saxMat);
            bell.transform.localRotation = Quaternion.Euler(0f, 0f, -20f);
            // 上の吹き口（左へ曲がる）
            var neck = Cube("Neck", root, new Vector3(-w * 0.4f, top + D * 0.15f, 0f), new Vector3(w * 1.1f, D * 0.06f, D * 0.06f), saxMat);
            neck.transform.localRotation = Quaternion.Euler(0f, 0f, 25f);
            multText = BoardLabel("Mult", Board, SaxBase + new Vector3(D * 2.6f, top * 0.55f, 0f), "", D * 0.9f, Ink);
        }

        protected override void OnBegin()
        {
            add = 0f;
            addShown = 0f;
            maxed = false;
        }

        protected override void Tick(float dt, bool running)
        {
            spotPulse = Mathf.MoveTowards(spotPulse, 0f, dt * 2.5f);
            float phase = Elapsed / Mathf.Max(0.5f, sweepSeconds) * Mathf.PI * 2f;
            spotX = Mathf.Sin(phase) * HoleHalf * 0.85f;
            addShown = Mathf.Lerp(addShown, add, 1f - Mathf.Exp(-8f * dt));
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            float x = medal.transform.position.x;
            bool inside = Mathf.Abs(x - spotX) <= D * spotRadius;

            // いまの倍率でポイントを足してから、倍率を上げる
            int extra = AddMultiplied(Multiplier);
            meterPulse = 1f;
            if (maxed && inside)
            {
                AddPoints(goldBonus);
                extra += goldBonus;
            }
            bool wasMax = maxed;
            add = Mathf.Min(maxAdd, add + (inside ? stepInside : stepOutside));
            maxed = add >= maxAdd - 0.0001f;

            var pos = new Vector3(x, D * 1.2f, SpotZ);
            if (inside)
            {
                spotPulse = 1f;
                Play(adlibSound, ref madeAdlib, () => RecordSynth.Notes("JazzAdlib", new[] { RecordSynth.Note(7), RecordSynth.Note(10), RecordSynth.Note(14), RecordSynth.Note(15) }, 0.07f, 0.4f, 0.3f, RecordSynth.Wave.Triangle));
                BoardPop(maxed ? $"アドリブ！ +{CenterPoints + extra}" : $"アドリブ！ ×+{stepInside:0.00}", Gold, pos, D * 0.7f);
                Emit(sparkle, new Vector3(x, D * 0.3f, SpotZ), Gold, 20);
            }
            else
            {
                Play(dropSound, ref madeDrop, () => RecordSynth.Notes("JazzDrop", new[] { RecordSynth.Note(-5) }, 0.05f, 0.25f, 0.25f, RecordSynth.Wave.Sine), 0.6f);
                BoardPop($"+{CenterPoints + extra}", Violet, pos, D * 0.6f);
            }
            if (maxed && !wasMax)
            {
                Play(maxSound, ref madeMax, () => RecordSynth.Chord("JazzMax", new[] { RecordSynth.Note(0), RecordSynth.Note(4), RecordSynth.Note(7), RecordSynth.Note(11), RecordSynth.Note(14) }, 1.6f, 0.35f, RecordSynth.Wave.Triangle));
                BoardPop("セッション最高潮！", Gold, new Vector3(0f, D * 2.5f, SpotZ + D * 1.5f), D * 1.1f);
                Speaker(1f, 12, Gold);
            }
        }

        protected override bool KeepBoardVisible() => sparkle != null && sparkle.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            Color c = maxed ? Gold : Amber;
            var target = new Vector3(spotX, 0f, SpotZ);
            var from = new Vector3(spotX * 0.6f, Layout.wallHeight * 1.1f, SpotZ + D * 2.5f);
            spot.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
            spot.color = c;
            spot.intensity = spotIntensity * BoardShown * (1f + 0.6f * spotPulse);
            spot.enabled = spot.intensity > 0.001f;
            moodLight.intensity = 1.5f * BoardShown;
            moodLight.enabled = moodLight.intensity > 0.001f;
            ring.transform.position = new Vector3(spotX, D * 0.01f, SpotZ);
            SetGlow(ringMaterial, c, (0.8f + 1.5f * spotPulse) * BoardShown);
            ring.SetActive(BoardShown > 0.01f);

            // ネオンのサックス：倍率に合わせて目盛りが下から光る
            meterPulse = Mathf.MoveTowards(meterPulse, 0f, dt * 2.5f);
            float fill = maxAdd > 0f ? Mathf.Clamp01(addShown / maxAdd) * Segments : 0f;
            for (int i = 0; i < segMats.Count; i++)
            {
                float k = Mathf.Clamp01(fill - i);
                Color sc = maxed ? Gold : Color.Lerp(Violet, Amber, i / (Segments - 1f));
                Color col = Color.Lerp(NeonOff, sc, k);
                float g = (0.2f + 2.2f * k + (i == Mathf.FloorToInt(fill) ? 1.5f * meterPulse : 0f)) * BoardShown;
                if (maxed) g *= 0.8f + 0.4f * Mathf.Sin(Time.time * 6f + i * 0.5f);
                SetGlow(segMats[i], col, g);
            }
            if (saxMat != null) SetGlow(saxMat, maxed ? Gold : Amber, (maxed ? 2.5f : 1.2f) * BoardShown);
            if (multText != null)
            {
                multText.gameObject.SetActive(Active);
                multText.text = $"×{1f + addShown:0.00}";
                multText.Color = maxed ? Gold : Ink;
                multText.Scale = 1f + 0.15f * meterPulse;
            }
        }
    }
}
