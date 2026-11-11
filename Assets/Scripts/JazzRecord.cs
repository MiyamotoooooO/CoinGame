using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N006 ジャズ「ミッドナイト・セッション」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：紫のムード照明。上から1本のスポットライトが、中央の穴の手前をゆっくり左右に動く（床に光の輪）。
    ///   倍率が最大になると、スポットライトが金色になる
    /// ・モニター：左にサックスのメーター（半円の目盛りと針。落ちるたびに上がり、最大で金色）、右に大きく今の倍率「×1.26」。
    ///   下に中央の穴の横はばの帯と、いまのスポットライトの位置
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<JazzRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Violet = new Color32(0xa0, 0x6c, 0xff, 0xff);
        static readonly Color Amber = new Color32(0xff, 0xb3, 0x3c, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd7, 0x4a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        float add;               // 倍率の上がった分
        float spotX, spotPulse, addShown;
        bool maxed;
        float Multiplier => 1f + add;

        Light moodLight, spot;
        GameObject ring;
        Material ringMaterial;
        ParticleSystem sparkle;

        readonly List<Image> ticks = new List<Image>();
        RectTransform needle, spotMarker;
        Text multText, multSub, statusText;
        Image spotMarkerImg;
        float laneLeft, laneW;
        AudioClip madeAdlib, madeDrop, madeMax;

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
            if (maxed && inside)
            {
                AddPoints(goldBonus);
                extra += goldBonus;
            }
            bool wasMax = maxed;
            add = Mathf.Min(maxAdd, add + (inside ? stepInside : stepOutside));
            maxed = add >= maxAdd - 0.0001f;

            Vector2 pos = new Vector2(BodyW * 0.72f, 320f);
            if (inside)
            {
                spotPulse = 1f;
                Play(adlibSound, ref madeAdlib, () => RecordSynth.Notes("JazzAdlib", new[] { RecordSynth.Note(7), RecordSynth.Note(10), RecordSynth.Note(14), RecordSynth.Note(15) }, 0.07f, 0.4f, 0.3f, RecordSynth.Wave.Triangle));
                Pop(maxed ? $"アドリブ！ +{CenterPoints + extra}" : $"アドリブ！ +{stepInside:0.00}", Gold, pos);
                Emit(sparkle, new Vector3(x, D * 0.3f, SpotZ), Gold, 20);
            }
            else
            {
                Play(dropSound, ref madeDrop, () => RecordSynth.Notes("JazzDrop", new[] { RecordSynth.Note(-5) }, 0.05f, 0.25f, 0.25f, RecordSynth.Wave.Sine), 0.6f);
                Pop($"+{stepOutside:0.00}", Violet, pos);
            }
            if (maxed && !wasMax)
            {
                Play(maxSound, ref madeMax, () => RecordSynth.Chord("JazzMax", new[] { RecordSynth.Note(0), RecordSynth.Note(4), RecordSynth.Note(7), RecordSynth.Note(11), RecordSynth.Note(14) }, 1.6f, 0.35f, RecordSynth.Wave.Triangle));
                PopCenter("セッション最高潮！", Gold);
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
        }

        protected override void BuildScreen()
        {
            ticks.Clear();
            float W = BodyW;
            Tag(Screen, "Badge", 70f, 24f, 100f, 26f, "JAZZ", 14, Violet, new Color32(0x16, 0x12, 0x22, 0xff));

            // 左：半円のメーター
            float cx = W * 0.3f, cy = 330f, r = 170f;
            const int count = 21;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.Lerp(180f, 0f, i / (float)(count - 1)) * Mathf.Deg2Rad;
                var tick = Img(Screen, $"Tick{i}", cx + Mathf.Cos(a) * r, cy - Mathf.Sin(a) * r, 10f, 34f, new Color(1f, 1f, 1f, 0.15f));
                tick.rectTransform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f);
                ticks.Add(tick);
            }
            var n = Img(Screen, "Needle", cx, cy, 6f, r * 0.9f, Ink);
            needle = n.rectTransform;
            needle.pivot = new Vector2(0.5f, 0f);
            Place(needle, cx, cy);
            Img(Screen, "Hub", cx, cy, 30f, 30f, Ink, MonitorDisplay.ScreenShape.Circle);

            // 右：倍率
            multText = Lbl(Screen, "Mult", W * 0.72f, 220f, 460f, 140f, "", 110, Gold, TextAnchor.MiddleCenter, true);
            multSub = Lbl(Screen, "MultSub", W * 0.72f, 300f, 460f, 32f, "", 20, Ink);

            // 下：中央の穴の帯とスポットライトの位置
            laneLeft = W * 0.2f;
            laneW = W * 0.6f;
            Lbl(Screen, "LaneLabel", 24f, 470f, 160f, 30f, "中央の穴", 18, Mute, TextAnchor.MiddleLeft);
            Img(Screen, "Lane", laneLeft + laneW * 0.5f, 470f, laneW, 30f, new Color(1f, 1f, 1f, 0.1f), MonitorDisplay.ScreenShape.Pill);
            spotMarkerImg = Img(Screen, "Spot", laneLeft + laneW * 0.5f, 470f, laneW * spotRadius * D / Mathf.Max(0.01f, HoleHalf), 30f, Amber, MonitorDisplay.ScreenShape.Pill);
            spotMarker = spotMarkerImg.rectTransform;
            Lbl(Screen, "LeftLabel", laneLeft - 10f, 470f, 60f, 30f, "左", 16, Mute, TextAnchor.MiddleRight);
            Lbl(Screen, "RightLabel", laneLeft + laneW + 10f, 470f, 60f, 30f, "右", 16, Mute, TextAnchor.MiddleLeft);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float frac = Mathf.Clamp01(addShown / Mathf.Max(0.01f, maxAdd));
            int lit = Mathf.RoundToInt(frac * (ticks.Count - 1));
            for (int i = 0; i < ticks.Count; i++)
            {
                Color c = maxed ? Gold : Color.Lerp(new Color32(0xff, 0x7a, 0x1a, 0xff), Gold, i / (float)(ticks.Count - 1));
                ticks[i].color = i <= lit && addShown > 0.001f ? c : new Color(1f, 1f, 1f, 0.15f);
            }
            needle.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(90f, -90f, frac) + Mathf.Sin(Time.time * 20f) * 0.6f);

            multText.text = $"×{1f + addShown:0.00}";
            multText.color = maxed ? Color.Lerp(Gold, Color.white, 0.3f + 0.3f * Mathf.Sin(Time.time * 6f)) : Gold;
            multSub.text = maxed ? "セッション最高潮！ 輪の中で +" + goldBonus + "pt" : "落ちるたびに倍率が上がる";

            float x01 = Mathf.InverseLerp(-HoleHalf, HoleHalf, spotX);
            Place(spotMarker, laneLeft + laneW * x01, 470f);
            spotMarkerImg.color = Color.Lerp(maxed ? Gold : Amber, Color.white, spotPulse * 0.6f);

            statusText.text = maxed
                ? $"<color=#FFD74A>最高潮！</color>  スポットライトの輪の中に落とすと +{goldBonus}pt"
                : $"スポットライトの輪の中に落とすとアドリブ成功：倍率 +{stepInside:0.00}（外は +{stepOutside:0.00}）";
        }
    }
}
