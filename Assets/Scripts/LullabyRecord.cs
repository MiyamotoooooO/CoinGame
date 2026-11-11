using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N040 子守唄「ねんねんころり」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：盤面がやさしいオレンジの光になり、プッシャーの上に「Zzz」の泡が浮かぶ。赤ちゃんが起きると泡が消えて、光が白っぽくなる
    /// ・モニター：ゆりかごで眠る赤ちゃん。眠りの深さのメーター、ふたごになった枚数。起きた時は泣き顔と、また寝るまでの時間
    /// ・ルール（CSV のとおり）：
    ///   発動中は、プッシャーが止まる。その間に投入したメダルは2枚になる（同じ所からもう1枚出る）
    ///   ただし、wakeGap 秒より短い間かくで続けて投入すると、赤ちゃんが起きる：wakeSeconds 秒のあいだプッシャーが動き、メダルは2枚にならない
    /// ・ゆっくり、間をあけて投入する遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// プッシャーは、効果が終わると元に戻る。
    /// </summary>
    [DisallowMultipleComponent]
    public class LullabyRecord : RecordScriptBase
    {
        public static LullabyRecord Instance { get; private set; }

        [Header("ふたご")]
        [Tooltip("投入1枚で、何枚ふやすか")]
        [Range(1, 5)] public int extraMedals = 1;

        [Header("起こさない")]
        [Tooltip("これより短い間かくで投入すると起きる（秒）")]
        [Range(0.1f, 5f)] public float wakeGap = 1.2f;
        [Range(1f, 30f)] public float wakeSeconds = 4f;

        [Header("盤面の光")]
        [Range(0f, 10f)] public float glowIntensity = 2f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip twinSound;
        public AudioClip wakeSound;

        protected override string DefaultKey => "Lullaby";
        protected override string ScreenTitle => "LULLABY";
        protected override string ScreenSubtitle => "ねんねんころり";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<LullabyRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Warm = new Color32(0xff, 0xb0, 0x5a, 0xff);
        static readonly Color Cream = new Color32(0xff, 0xf0, 0xd8, 0xff);
        static readonly Color Pink = new Color32(0xff, 0xb8, 0xc8, 0xff);
        static readonly Color Room = new Color32(0x22, 0x1a, 0x2a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        float lastInsert = -99f, awakeTimer, rock;
        int twins, wakes;
        bool IsAwake => awakeTimer > 0f;
        readonly List<Medal> pending = new List<Medal>();

        Light warm;
        ParticleSystem zzz;

        RectTransform cradle, zText;
        Image eyeL, eyeR, mouth;
        Text sleepText, twinText, statusText;
        Image sleepFill;
        AudioClip madeTwin, madeWake;

        protected override void BuildBoard()
        {
            warm = PointLight("Warm", Board, new Vector3(0f, Layout.wallHeight, (FrontZ + BackZ) * 0.5f), Warm, Layout.boardWidth * 1.5f);
            zzz = Particles("Zzz", Board, Vector3.up, D * 0.6f, D * 0.25f, 2.5f, -0.02f, 15f);
        }

        protected override void OnBegin()
        {
            awakeTimer = 0f;
            twins = 0;
            wakes = 0;
            lastInsert = -99f;
            pending.Clear();
        }

        protected override void OnInsert(Medal medal)
        {
            float gap = Time.time - lastInsert;
            lastInsert = Time.time;
            if (IsAwake) return;
            if (gap < wakeGap)
            {
                awakeTimer = wakeSeconds;
                wakes++;
                Play(wakeSound, ref madeWake, () => RecordSynth.Sweep("LullabyWake", 600f, 900f, 0.6f, 0.25f, RecordSynth.Wave.Triangle));
                PopCenter("おきちゃった…", Pink);
                return;
            }
            pending.Add(medal);   // 次のフレームで、同じ所・同じ速さでもう1枚出す
        }

        protected override void Tick(float dt, bool running)
        {
            rock += dt;
            if (awakeTimer > 0f) awakeTimer -= dt;
            PusherHold = !IsAwake;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var src = pending[i];
                pending.RemoveAt(i);
                if (src == null || !src.IsActiveInPool || src.Body == null) continue;
                for (int k = 0; k < extraMedals; k++)
                {
                    Vector3 offset = new Vector3(0f, D * 0.3f * (k + 1), D * 0.15f * (k + 1));
                    SpawnMedal(src.transform.position + offset, src.Body.velocity);
                    twins++;
                }
                Play(twinSound, ref madeTwin, () => RecordSynth.Notes("LullabyTwin", new[] { RecordSynth.Note(7), RecordSynth.Note(12) }, 0.12f, 0.6f, 0.2f, RecordSynth.Wave.Bell), 0.6f);
            }
        }

        protected override bool KeepBoardVisible() => zzz != null && zzz.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            warm.color = IsAwake ? new Color(1f, 0.95f, 0.9f) : Warm;
            warm.intensity = glowIntensity * BoardShown;
            if (Active && !IsAwake && Random.value < 0.05f)
                Emit(zzz, new Vector3(Random.Range(-HalfW * 0.5f, HalfW * 0.5f), Layout.pusherHeight + D * 0.5f, Layout.pusherRetractZ + D), Cream, 1);
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var room = Panel(Screen, "Room", W * 0.5f, 200f, W - 48f, 360f, Room);
            float rw = W - 48f;
            Img(room, "Moon", rw * 0.85f, 60f, 60f, 60f, Cream, MonitorDisplay.ScreenShape.Circle);
            var c = Grp(room, "Cradle", rw * 0.4f, 230f, 340f, 200f);
            Img(c, "Basket", 170f, 130f, 320f, 120f, new Color32(0xc8, 0x9a, 0x6a, 0xff), MonitorDisplay.ScreenShape.Pill);
            Img(c, "Blanket", 200f, 110f, 220f, 70f, Pink, MonitorDisplay.ScreenShape.Pill);
            Img(c, "Face", 90f, 90f, 90f, 90f, new Color32(0xff, 0xe0, 0xd0, 0xff), MonitorDisplay.ScreenShape.Circle);
            eyeL = Img(c, "EyeL", 72f, 88f, 18f, 4f, new Color32(0x40, 0x30, 0x30, 0xff), MonitorDisplay.ScreenShape.Pill);
            eyeR = Img(c, "EyeR", 108f, 88f, 18f, 4f, new Color32(0x40, 0x30, 0x30, 0xff), MonitorDisplay.ScreenShape.Pill);
            mouth = Img(c, "Mouth", 90f, 112f, 12f, 6f, new Color32(0xd8, 0x6a, 0x6a, 0xff), MonitorDisplay.ScreenShape.Pill);
            Img(c, "RockerL", 40f, 190f, 120f, 12f, new Color32(0x8a, 0x6a, 0x4a, 0xff), MonitorDisplay.ScreenShape.Pill);
            Img(c, "RockerR", 300f, 190f, 120f, 12f, new Color32(0x8a, 0x6a, 0x4a, 0xff), MonitorDisplay.ScreenShape.Pill);
            cradle = c;
            var z = Lbl(room, "Z", rw * 0.6f, 100f, 200f, 80f, "Zzz", 48, Cream, TextAnchor.MiddleCenter, true);
            zText = z.rectTransform;
            Lbl(Screen, "SleepLabel", 24f, 440f, 160f, 30f, "眠りの深さ", 20, Ink, TextAnchor.MiddleLeft);
            sleepFill = Bar(Screen, "Sleep", 180f, 440f, W * 0.45f, 18f, Warm);
            twinText = Lbl(Screen, "Twins", W - 24f, 440f, 400f, 40f, "", 24, Warm, TextAnchor.MiddleRight);
            sleepText = Lbl(Screen, "Sleep", 24f, 490f, W - 48f, 30f, "", 18, Mute, TextAnchor.MiddleLeft);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float rw = BodyW - 48f;
            cradle.localRotation = Quaternion.Euler(0f, 0f, IsAwake ? Mathf.Sin(rock * 12f) * 4f : Mathf.Sin(rock * 1.2f) * 6f);
            eyeL.rectTransform.sizeDelta = IsAwake ? new Vector2(12f, 12f) : new Vector2(18f, 4f);
            eyeR.rectTransform.sizeDelta = IsAwake ? new Vector2(12f, 12f) : new Vector2(18f, 4f);
            mouth.rectTransform.sizeDelta = IsAwake ? new Vector2(24f, 16f) : new Vector2(12f, 6f);
            zText.gameObject.SetActive(!IsAwake);
            Place(zText, rw * 0.6f + Mathf.Sin(rock) * 10f, 100f - Mathf.Repeat(rock * 15f, 40f));
            float calm = IsAwake ? 0f : Mathf.Clamp01((Time.time - lastInsert) / Mathf.Max(0.1f, wakeGap));
            sleepFill.fillAmount = IsAwake ? 1f - awakeTimer / Mathf.Max(0.1f, wakeSeconds) : calm;
            sleepFill.color = IsAwake ? Pink : calm >= 1f ? Warm : Color.Lerp(Pink, Warm, calm);
            twinText.text = $"ふたご {twins}枚";
            sleepText.text = IsAwake ? $"起きている…あと {Mathf.CeilToInt(awakeTimer)}秒で寝る（起こした回数 {wakes}）" : calm >= 1f ? "ぐっすり。いま投入すると2枚になる" : "眠りが浅い…少し待ってから投入しよう";
            statusText.text = IsAwake
                ? "<color=#FFB8C8>起きちゃった！</color>  プッシャーが動き、メダルは2枚にならない"
                : $"プッシャーは止まっている。{wakeGap:0.#}秒以上あけて投入すると、メダルが2枚になる";
        }
    }
}
