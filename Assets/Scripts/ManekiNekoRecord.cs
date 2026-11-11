using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N017 招き猫「コバンの手招き」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：盤面の右奥の壁ぎわに招き猫（コバン）が置かれる。手招きするたびに小判が光り、チャリンと鳴る。
    ///   手招きすると、盤面の上からメダルが降ってくる。降ってきたうちの1枚は金色の「小判メダル」
    /// ・モニター：コバンの店。手招きまでの残り枚数（招きメーター）、盤面にある小判メダルの数、手招きの回数
    /// ・ルール：
    ///   中央の穴に beckonEvery 枚落ちるごとに手招き：盤面にメダルが medalsPerBeckon 枚降ってくる
    ///   （CSV の「手持ちのメダル +1」の代わり。手持ちを増やす方法がゲームに無いため、盤面に降らせる）
    ///   降ってきたうちの1枚は小判メダル。中央の穴に落とすと +kobanBonus pt
    /// ・小判メダルをねらって中央へ寄せる遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class ManekiNekoRecord : RecordScriptBase
    {
        public static ManekiNekoRecord Instance { get; private set; }

        public const string KobanKind = "Koban";

        [Header("手招き")]
        [Range(1, 50)] public int beckonEvery = 10;
        [Tooltip("手招き1回で降ってくるメダルの数（小判メダル1枚をふくむ）")]
        [Range(1, 20)] public int medalsPerBeckon = 2;

        [Header("小判メダル")]
        [Range(0, 300)] public int kobanBonus = 30;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip beckonSound;
        public AudioClip kobanSound;
        [Tooltip("招き猫の Prefab（空なら仮の招き猫）。子の「Paw」を手として動かす")]
        public GameObject catPrefab;

        protected override string DefaultKey => "ManekiNeko";
        protected override string ScreenTitle => "MANEKI NEKO";
        protected override string ScreenSubtitle => "コバンの手招き";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<ManekiNekoRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color White = new Color32(0xfa, 0xf6, 0xf0, 0xff);
        static readonly Color Red = new Color32(0xe0, 0x3a, 0x3a, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xcc, 0x33, 0xff);
        static readonly Color Shop = new Color32(0x2a, 0x16, 0x14, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int count, beckons;
        float pawTimer;

        Transform cat, paw;
        Material coinMaterial;
        ParticleSystem shine;

        RectTransform screenPaw;
        readonly List<Image> meter = new List<Image>();
        Text kobanText, beckonText, statusText;
        AudioClip madeBeckon, madeKoban;

        protected override void BuildBoard()
        {
            cat = new GameObject("ManekiNeko").transform;
            cat.SetParent(Board, false);
            cat.position = new Vector3(HalfW - D * 0.6f, Layout.pusherHeight, BackZ - D * 0.6f);
            cat.rotation = Quaternion.Euler(0f, 200f, 0f);
            if (catPrefab != null)
            {
                var inst = Instantiate(catPrefab, cat);
                inst.transform.localPosition = Vector3.zero;
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) if (t.name == "Paw") paw = t;
            }
            else
            {
                var white = Glow(White, 0.15f);
                Shape(PrimitiveType.Sphere, "Body", cat, new Vector3(0f, D * 0.4f, 0f), new Vector3(D * 0.7f, D * 0.8f, D * 0.6f), white);
                Shape(PrimitiveType.Sphere, "Head", cat, new Vector3(0f, D * 0.95f, 0f), new Vector3(D * 0.62f, D * 0.55f, D * 0.55f), white);
                Cube("EarL", cat, new Vector3(-D * 0.2f, D * 1.22f, 0f), new Vector3(D * 0.14f, D * 0.16f, D * 0.1f), white).transform.localRotation = Quaternion.Euler(0f, 0f, 20f);
                Cube("EarR", cat, new Vector3(D * 0.2f, D * 1.22f, 0f), new Vector3(D * 0.14f, D * 0.16f, D * 0.1f), white).transform.localRotation = Quaternion.Euler(0f, 0f, -20f);
                Cube("Collar", cat, new Vector3(0f, D * 0.7f, 0f), new Vector3(D * 0.6f, D * 0.06f, D * 0.5f), Glow(Red, 0.4f));
                coinMaterial = Glow(Gold, 1f);
                Shape(PrimitiveType.Cylinder, "Koban", cat, new Vector3(0f, D * 0.45f, D * 0.3f), new Vector3(D * 0.35f, D * 0.02f, D * 0.5f), coinMaterial).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var pivot = new GameObject("Paw").transform;
                pivot.SetParent(cat, false);
                pivot.localPosition = new Vector3(D * 0.3f, D * 0.85f, 0f);
                Shape(PrimitiveType.Sphere, "PawBall", pivot, new Vector3(0f, D * 0.25f, 0f), new Vector3(D * 0.18f, D * 0.36f, D * 0.18f), white);
                paw = pivot;
            }
            shine = Particles("Shine", Board, Vector3.up, D * 1.5f, D * 0.1f, 0.8f, -0.1f, 50f);

            MedalMarks.RegisterEffect(KobanKind, (m, type) =>
            {
                if (type != DropZoneType.Center || effects == null) return;
                effects.AddPoints(kobanBonus);
                Play(kobanSound, ref madeKoban, () => RecordSynth.Notes("NekoKoban", new[] { 2093f, 2637f, 3136f }, 0.05f, 0.4f, 0.25f, RecordSynth.Wave.Bell));
                Pop($"小判 +{kobanBonus}", Gold, new Vector2(BodyW * 0.5f, 420f));
            });
        }

        protected override void OnBegin()
        {
            count = 0;
            beckons = 0;
        }

        protected override void Tick(float dt, bool running)
        {
            pawTimer = Mathf.MoveTowards(pawTimer, 0f, dt * 0.8f);
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            count++;
            if (count < beckonEvery) return;
            count = 0;
            beckons++;
            pawTimer = 1f;
            var rained = RainMedals(medalsPerBeckon);
            if (rained.Count > 0) MedalMarks.Set(rained[0], KobanKind, Gold, 3f);
            Emit(shine, cat.position + Vector3.up * D * 0.6f, Gold, 30);
            Play(beckonSound, ref madeBeckon, () => RecordSynth.Notes("NekoBeckon", new[] { 1568f, 2093f }, 0.08f, 0.5f, 0.3f, RecordSynth.Wave.Bell));
            PopCenter($"おいでおいで！ メダル +{rained.Count}", Gold);
            Speaker(0.7f, 8, Gold);
        }

        protected override bool KeepBoardVisible() => shine != null && shine.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            cat.localScale = Vector3.one * Mathf.Max(0.01f, BoardShown);
            if (paw != null)
            {
                float wave = pawTimer > 0f ? Mathf.Sin(Time.time * 14f) * 40f * pawTimer : Mathf.Sin(Time.time * 2f) * 6f;
                paw.localRotation = Quaternion.Euler(wave, 0f, 0f);
            }
            if (coinMaterial != null) SetGlow(coinMaterial, Gold, 0.8f + 2.5f * pawTimer);
        }

        protected override void BuildScreen()
        {
            meter.Clear();
            float W = BodyW;
            var shop = Panel(Screen, "Shop", W * 0.5f, 200f, W - 48f, 360f, Shop);
            float sw = W - 48f;
            Img(shop, "Noren", sw * 0.5f, 30f, sw, 60f, Red);
            Lbl(shop, "Name", sw * 0.5f, 30f, sw, 60f, "コバンの店", 28, Ink);
            float cx = sw * 0.3f;
            Img(shop, "Body", cx, 260f, 170f, 170f, White, MonitorDisplay.ScreenShape.Circle);
            Img(shop, "Head", cx, 160f, 150f, 130f, White, MonitorDisplay.ScreenShape.Circle);
            Img(shop, "EyeL", cx - 30f, 160f, 14f, 8f, new Color32(0x30, 0x20, 0x20, 0xff), MonitorDisplay.ScreenShape.Pill);
            Img(shop, "EyeR", cx + 30f, 160f, 14f, 8f, new Color32(0x30, 0x20, 0x20, 0xff), MonitorDisplay.ScreenShape.Pill);
            Img(shop, "Collar", cx, 225f, 120f, 14f, Red, MonitorDisplay.ScreenShape.Pill);
            Img(shop, "Koban", cx, 280f, 60f, 80f, Gold, MonitorDisplay.ScreenShape.Circle);
            var p = Img(shop, "Paw", cx + 75f, 130f, 40f, 80f, White, MonitorDisplay.ScreenShape.Pill);
            p.rectTransform.pivot = new Vector2(0.5f, 0f);
            Place(p.rectTransform, cx + 75f, 170f);
            screenPaw = p.rectTransform;
            kobanText = Lbl(shop, "Koban", sw * 0.72f, 150f, sw * 0.5f, 60f, "", 40, Gold, TextAnchor.MiddleCenter);
            beckonText = Lbl(shop, "Beckon", sw * 0.72f, 220f, sw * 0.5f, 40f, "", 22, Ink);
            Lbl(Screen, "MeterLabel", 24f, 440f, 160f, 30f, "手招きまで", 20, Ink, TextAnchor.MiddleLeft);
            float cell = (W - 220f) / Mathf.Max(1, beckonEvery);
            for (int i = 0; i < beckonEvery; i++)
                meter.Add(Img(Screen, $"Coin{i}", 180f + cell * (i + 0.5f), 440f, Mathf.Min(30f, cell - 6f), Mathf.Min(30f, cell - 6f), new Color(1f, 1f, 1f, 0.12f), MonitorDisplay.ScreenShape.Circle));
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float wave = pawTimer > 0f ? Mathf.Sin(Time.time * 14f) * 30f * pawTimer : Mathf.Sin(Time.time * 2f) * 5f;
            screenPaw.localRotation = Quaternion.Euler(0f, 0f, wave);
            for (int i = 0; i < meter.Count; i++) meter[i].color = i < count ? Gold : new Color(1f, 1f, 1f, 0.12f);
            kobanText.text = $"小判 {MedalMarks.Count(KobanKind)}枚";
            beckonText.text = $"手招き {beckons}回";
            statusText.text = $"中央の穴に {beckonEvery}枚でメダルが降ってくる。金の小判メダルは中央の穴で +{kobanBonus}pt";
        }
    }
}
