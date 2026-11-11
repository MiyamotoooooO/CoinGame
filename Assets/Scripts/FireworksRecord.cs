using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N032 花火「たまや〜」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：花火玉メダル（赤い輪）が降ってくる。導火線（輪の光）がプッシャーに押されるたびに短く・速く点滅し、
    ///   fusePushes 回押されると爆発して、まわりのメダルを吹き飛ばす。盤面の上に花火が開く
    /// ・モニター：夜空と、盤面にある花火玉と導火線の残り（○○●）。開いた花火の数
    /// ・ルール（CSV のとおり）：
    ///   中央の穴に spawnEvery 枚落ちるごとに、盤面に花火玉メダルが1つ降ってくる
    ///   花火玉は、プッシャーが fusePushes 回押すと爆発する（まわりのメダルを、手前に吹き飛ばす）
    ///   爆発してから bonusWindow 秒の間に穴に落ちたメダルは、1枚 +blastBonus pt（「たまや〜」）
    ///   花火玉メダルのまま中央の穴に落ちると、その場で花火が開いて +shellBonus pt
    /// ・花火玉がどこで爆発するかを考えて、メダルの山の近くに寄せる遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class FireworksRecord : RecordScriptBase
    {
        public static FireworksRecord Instance { get; private set; }

        public const string ShellKind = "Firework";

        [Header("花火玉")]
        [Range(1, 30)] public int spawnEvery = 5;
        [Range(1, 10)] public int fusePushes = 3;
        [Tooltip("吹き飛ばす半径（メダルの直径に対する倍率）")]
        [Range(0.5f, 5f)] public float blastRadius = 2f;
        [Range(0f, 20f)] public float blastStrength = 5f;

        [Header("ポイント")]
        [Range(0f, 10f)] public float bonusWindow = 2.5f;
        [Range(0, 100)] public int blastBonus = 5;
        [Range(0, 300)] public int shellBonus = 30;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip boomSound;
        public AudioClip fuseSound;

        protected override string DefaultKey => "Fireworks";
        protected override string ScreenTitle => "FIREWORKS";
        protected override string ScreenSubtitle => "たまや〜";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<FireworksRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        public static readonly Color ShellColor = new Color32(0xff, 0x3a, 0x4a, 0xff);
        static readonly Color[] BurstColors =
        {
            new Color32(0xff, 0x5a, 0x8a, 0xff), new Color32(0xff, 0xd2, 0x3f, 0xff), new Color32(0x5a, 0xd8, 0xff, 0xff), new Color32(0x9a, 0xff, 0x6a, 0xff), new Color32(0xc8, 0x8a, 0xff, 0xff),
        };
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int count, opened;
        float bonusTimer, lastPhase;
        readonly Dictionary<Medal, int> fuse = new Dictionary<Medal, int>();
        readonly List<Medal> scratch = new List<Medal>();

        ParticleSystem sky, sparks;

        RectTransform skyPanel;
        readonly List<RectTransform> bursts = new List<RectTransform>();
        readonly List<float> burstAge = new List<float>();
        Text shellText, openedText, statusText;
        AudioClip madeBoom, madeFuse;

        protected override void BuildBoard()
        {
            sky = Particles("SkyBurst", Board, Vector3.up, D * 4f, D * 0.14f, 1.6f, 0.25f, 180f);
            sparks = Particles("Sparks", Board, Vector3.up, D * 2f, D * 0.06f, 0.5f, 0.5f, 40f);
            MedalMarks.RegisterEffect(ShellKind, (m, type) =>
            {
                fuse.Remove(m);
                if (type != DropZoneType.Center || effects == null) return;
                effects.AddPoints(shellBonus);
                SkyBurst(new Vector3(m.transform.position.x, Layout.wallHeight * 1.2f, FrontZ + D));
                Pop($"たまや〜 +{shellBonus}", ShellColor, new Vector2(BodyW * 0.5f, 120f));
            });
        }

        protected override void OnBegin()
        {
            count = 0;
            opened = 0;
            bonusTimer = 0f;
            lastPhase = game.Pusher != null ? game.Pusher.Phase01 : 0f;
        }

        protected override void Tick(float dt, bool running)
        {
            if (bonusTimer > 0f) bonusTimer -= dt;
            for (int i = burstAge.Count - 1; i >= 0; i--) burstAge[i] += dt;

            // プッシャーが一番前まで押した瞬間（Phase01 が 0.5 をこえた）を数える
            if (game.Pusher == null) return;
            float ph = game.Pusher.Phase01;
            bool pushed = lastPhase < 0.5f && ph >= 0.5f;
            lastPhase = ph;
            if (!pushed || !running) return;

            MedalMarks.Collect(ShellKind, scratch);
            foreach (var m in scratch)
            {
                int left = (fuse.TryGetValue(m, out int f) ? f : fusePushes) - 1;
                fuse[m] = left;
                Play(fuseSound, ref madeFuse, () => RecordSynth.Noise("FireworksFuse", 0.3f, 0.9f, 0.01f, 0.2f, 0.3f, 25), 0.5f);
                if (left <= 0) Explode(m);
            }
        }

        void Explode(Medal m)
        {
            Vector3 p = m.transform.position;
            fuse.Remove(m);
            MedalMarks.Clear(m);
            Blast(p, D * blastRadius, blastStrength, 0.5f);
            Push(m, Vector3.up * D * 3f);
            bonusTimer = bonusWindow;
            Emit(sparks, p, ShellColor, 40);
            SkyBurst(new Vector3(p.x, Layout.wallHeight * 1.3f, p.z + D));
            Shake(0.04f, 0.4f);
            PopCenter("ドーン！", BurstColors[Random.Range(0, BurstColors.Length)]);
        }

        void SkyBurst(Vector3 at)
        {
            opened++;
            Color c = BurstColors[Random.Range(0, BurstColors.Length)];
            Emit(sky, at, c, 80);
            Play(boomSound, ref madeBoom, () => RecordSynth.Thud("FireworksBoom", 60f, 1.2f, 1f));
            Speaker(0.8f, 10, c);
            if (skyPanel != null)
            {
                float sw = BodyW - 48f;
                var b = Grp(skyPanel, "Burst", Random.Range(sw * 0.15f, sw * 0.85f), Random.Range(60f, 200f), 200f, 200f);
                for (int i = 0; i < 12; i++)
                {
                    var ray = Img(b, $"Ray{i}", 100f, 100f, 6f, 180f, c, MonitorDisplay.ScreenShape.Pill);
                    ray.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 15f);
                }
                bursts.Add(b);
                burstAge.Add(0f);
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (bonusTimer > 0f)
            {
                AddPoints(blastBonus);
                Pop($"たまや +{blastBonus}", BurstColors[1], new Vector2(BodyW * Random.Range(0.3f, 0.7f), 420f));
            }
            if (type != DropZoneType.Center) return;
            count++;
            if (count < spawnEvery) return;
            count = 0;
            var list = RainMedals(1);
            if (list.Count > 0)
            {
                MedalMarks.Set(list[0], ShellKind, ShellColor, 2.5f);
                fuse[list[0]] = fusePushes;
            }
        }

        protected override bool KeepBoardVisible() => (sky != null && sky.particleCount > 0) || (sparks != null && sparks.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            if (!Active) return;
            MedalMarks.Collect(ShellKind, scratch);
            foreach (var m in scratch)
            {
                int left = fuse.TryGetValue(m, out int f) ? f : fusePushes;
                float speed = 2f + (fusePushes - left) * 4f;
                MedalMarks.Recolor(m, ShellColor, Mathf.Repeat(Time.time * speed, 1f) < 0.5f ? 4f : 0.5f);
                if (Random.value < 0.2f) Emit(sparks, m.transform.position + Vector3.up * D * 0.1f, BurstColors[1], 1);
            }
        }

        protected override void BuildScreen()
        {
            bursts.Clear();
            burstAge.Clear();
            float W = BodyW;
            skyPanel = Panel(Screen, "Sky", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x0a, 0x08, 0x1e, 0xff));
            float sw = W - 48f;
            Img(skyPanel, "River", sw * 0.5f, 340f, sw, 40f, new Color32(0x14, 0x1c, 0x3a, 0xff));
            for (int i = 0; i < 6; i++) Img(skyPanel, $"Roof{i}", sw * (0.08f + i * 0.17f), 310f, sw * 0.15f, 30f, new Color32(0x16, 0x10, 0x16, 0xff));
            shellText = Lbl(Screen, "Shell", 24f, 440f, W * 0.6f, 40f, "", 24, ShellColor, TextAnchor.MiddleLeft);
            openedText = Lbl(Screen, "Opened", W - 24f, 440f, 400f, 40f, "", 22, Ink, TextAnchor.MiddleRight);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                float a = burstAge[i];
                if (a > 1.6f || bursts[i] == null)
                {
                    if (bursts[i] != null) Destroy(bursts[i].gameObject);
                    bursts.RemoveAt(i);
                    burstAge.RemoveAt(i);
                    continue;
                }
                Scale(bursts[i], 0.2f + a);
                foreach (var img in bursts[i].GetComponentsInChildren<Image>())
                {
                    var c = img.color;
                    img.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(1.6f - a));
                }
            }
            MedalMarks.Collect(ShellKind, scratch);
            var sb = new System.Text.StringBuilder($"花火玉 {scratch.Count}個  ");
            foreach (var m in scratch)
            {
                int left = fuse.TryGetValue(m, out int f) ? f : fusePushes;
                sb.Append('[');
                for (int k = 0; k < fusePushes; k++) sb.Append(k < left ? '○' : '●');
                sb.Append("] ");
            }
            shellText.text = sb.ToString();
            openedText.text = $"開いた花火 {opened}";
            statusText.text = bonusTimer > 0f
                ? $"<color=#FFD23F>たまや〜！</color>  いま穴に落ちると +{blastBonus}pt"
                : $"中央の穴に {spawnEvery}枚で花火玉が降る。プッシャーに {fusePushes}回押されると爆発";
        }
    }
}
