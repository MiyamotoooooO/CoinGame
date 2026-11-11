using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N026 星「流れ星の願いごと」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：流れ星が落ちる場所に、星の影（光る輪）が出る。影は 左・まん中・右 を順に動く。
    ///   メダルが穴に落ちると、流れ星が尾を引いて影の所へ落ち、当たったメダルと周りのメダルが星メダル（青白く光る輪）になる
    /// ・モニター：夜空と、次に流れ星が落ちる場所（左・まん中・右）、盤面にある星メダルの数、願いごとの数
    /// ・ルール：
    ///   メダルが穴（中央・端）に落ちるたびに流れ星が1つ落ちる（CSV のとおり）。当たった所から starRadius 以内のメダル（最大 starsPerShot 枚）が星メダルになる
    ///   星メダルが中央の穴に落ちると +starBonus pt（曲が終わっても、落ちるまで星メダルのまま）
    ///   星の影は spotSeconds 秒ごとに 左→まん中→右 と動く。メダルが多い所に影がある時に落とすとたくさん星メダルになる
    /// ・影の位置を見て、落とすタイミングをはかる遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// 星メダルの印（StarKind）は、N018 ギャンブルの「星」でも使う。
    /// </summary>
    [DisallowMultipleComponent]
    public class StarRecord : RecordScriptBase
    {
        public static StarRecord Instance { get; private set; }

        /// <summary>星メダルの印の名前</summary>
        public const string StarKind = "Star";

        [Header("流れ星")]
        [Range(1, 10)] public int starsPerShot = 3;
        [Tooltip("星メダルになる半径（メダルの直径に対する倍率）")]
        [Range(0.3f, 4f)] public float starRadius = 1.2f;
        [Tooltip("星の影が動く秒数")]
        [Range(0.5f, 20f)] public float spotSeconds = 3f;
        [Tooltip("落ちてくるまでの秒数")]
        [Range(0.1f, 3f)] public float fallSeconds = 0.7f;

        [Header("星メダル")]
        [Range(0, 300)] public int starBonus = 50;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip shootSound;
        public AudioClip wishSound;

        protected override string DefaultKey => "Star";
        protected override string ScreenTitle => "STAR";
        protected override string ScreenSubtitle => "流れ星の願いごと";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<StarRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        public static readonly Color StarColor = new Color32(0x9c, 0xe0, 0xff, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xe6, 0x7a, 0xff);
        static readonly Color Night = new Color32(0x08, 0x0c, 0x24, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        class Falling { public Vector3 target; public float t; }
        readonly List<Falling> falling = new List<Falling>();
        readonly List<Medal> near = new List<Medal>();
        int wishes;

        GameObject shadow;
        Material shadowMaterial;
        ParticleSystem trail;

        readonly Image[] spots = new Image[3];
        RectTransform comet;
        Text countText, wishText, statusText;
        AudioClip madeShoot, madeWish;

        int SpotIndex => Mathf.FloorToInt(Elapsed / Mathf.Max(0.5f, spotSeconds)) % 3;
        Vector3 SpotPos(int i) => new Vector3((i - 1) * HalfW * 0.55f, D * 0.01f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.4f));

        protected override void BuildBoard()
        {
            shadowMaterial = Glow(StarColor, 1.5f);
            shadow = Shape(PrimitiveType.Cylinder, "StarShadow", Board, Vector3.zero, new Vector3(D * starRadius * 2f, D * 0.004f, D * starRadius * 2f), shadowMaterial);
            trail = Particles("Trail", Board, Vector3.up, D * 0.5f, D * 0.15f, 0.6f, 0f, 20f);
            MedalMarks.RegisterEffect(StarKind, (m, type) =>
            {
                if (type != DropZoneType.Center || effects == null) return;
                effects.AddPoints(starBonus);
                wishes++;
                Play(wishSound, ref madeWish, () => RecordSynth.Notes("StarWish", new[] { RecordSynth.Note(19), RecordSynth.Note(24), RecordSynth.Note(28), RecordSynth.Note(31) }, 0.06f, 0.6f, 0.25f, RecordSynth.Wave.Bell));
                Pop($"星メダル +{starBonus}", StarColor, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 420f));
            });
        }

        protected override void OnBegin()
        {
            falling.Clear();
            wishes = 0;
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            falling.Add(new Falling { target = SpotPos(SpotIndex), t = 0f });
            Play(shootSound, ref madeShoot, () => RecordSynth.Sweep("StarShoot", 2400f, 900f, 0.5f, 0.2f, RecordSynth.Wave.Sine), 0.6f);
        }

        protected override void Tick(float dt, bool running)
        {
            for (int i = falling.Count - 1; i >= 0; i--)
            {
                var f = falling[i];
                f.t += dt / Mathf.Max(0.05f, fallSeconds);
                Vector3 from = f.target + new Vector3(-HalfW * 0.6f, Layout.wallHeight * 1.5f, D * 2f);
                Vector3 pos = Vector3.Lerp(from, f.target, Mathf.Clamp01(f.t));
                Emit(trail, pos, Color.Lerp(Gold, StarColor, f.t), 3);
                if (f.t < 1f) continue;
                Land(f.target);
                falling.RemoveAt(i);
            }
        }

        void Land(Vector3 at)
        {
            int n = 0;
            MedalsNear(at, D * starRadius, near);
            near.Sort((a, b) => (a.transform.position - at).sqrMagnitude.CompareTo((b.transform.position - at).sqrMagnitude));
            foreach (var m in near)
            {
                if (n >= starsPerShot) break;
                if (MedalMarks.Is(m, StarKind)) continue;
                MedalMarks.Set(m, StarKind, StarColor, 3f);
                Push(m, Vector3.up * D * 1.5f);
                n++;
            }
            Emit(trail, at + Vector3.up * D * 0.2f, Gold, 30);
            if (n > 0) Pop($"★×{n}", StarColor, new Vector2(BodyW * (0.2f + 0.3f * Mathf.InverseLerp(-HalfW, HalfW, at.x) * 2f), 200f));
        }

        protected override bool KeepBoardVisible() => trail != null && trail.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            shadow.SetActive(Active);
            if (!Active) return;
            shadow.transform.position = SpotPos(SpotIndex);
            SetGlow(shadowMaterial, StarColor, 0.8f + 0.6f * Mathf.Sin(Time.time * 6f));
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var sky = Panel(Screen, "Sky", W * 0.5f, 200f, W - 48f, 360f, Night);
            float sw = W - 48f;
            for (int i = 0; i < 50; i++)
                Img(sky, $"S{i}", Random.Range(0f, sw), Random.Range(0f, 300f), 3f, 3f, new Color(1f, 1f, 1f, Random.Range(0.2f, 0.9f)), MonitorDisplay.ScreenShape.Circle);
            for (int i = 0; i < 3; i++)
            {
                spots[i] = Img(sky, $"Spot{i}", sw * (0.2f + i * 0.3f), 320f, sw * 0.26f, 40f, new Color(1f, 1f, 1f, 0.08f), MonitorDisplay.ScreenShape.Pill);
                Lbl(sky, $"SpotName{i}", sw * (0.2f + i * 0.3f), 320f, sw * 0.26f, 40f, i == 0 ? "左" : i == 1 ? "まん中" : "右", 20, Ink);
            }
            var c = Grp(sky, "Comet", sw * 0.5f, 120f, 200f, 30f);
            var tail = Img(c, "Tail", 80f, 15f, 160f, 8f, new Color(StarColor.r, StarColor.g, StarColor.b, 0.5f), MonitorDisplay.ScreenShape.Pill);
            Img(c, "Head", 170f, 15f, 24f, 24f, Gold, MonitorDisplay.ScreenShape.Circle);
            comet = c;
            countText = Lbl(Screen, "Count", 24f, 440f, 500f, 50f, "", 30, StarColor, TextAnchor.MiddleLeft);
            wishText = Lbl(Screen, "Wish", W - 24f, 440f, 400f, 40f, "", 22, Gold, TextAnchor.MiddleRight);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float sw = BodyW - 48f;
            int idx = SpotIndex;
            for (int i = 0; i < 3; i++) spots[i].color = i == idx ? new Color(StarColor.r, StarColor.g, StarColor.b, 0.5f + 0.2f * Mathf.Sin(Time.time * 6f)) : new Color(1f, 1f, 1f, 0.08f);
            float t = Mathf.Repeat(Time.time * 0.4f, 1f);
            Place(comet, Mathf.Lerp(-100f, sw + 100f, t), Mathf.Lerp(40f, 200f, t));
            comet.localRotation = Quaternion.Euler(0f, 0f, -12f);
            countText.text = $"★ 星メダル {MedalMarks.Count(StarKind)}枚";
            wishText.text = $"願いごと {wishes}";
            statusText.text = $"穴に落ちるたびに、光る影の所へ流れ星が落ちる。星メダルは中央の穴で +{starBonus}pt";
        }
    }
}
