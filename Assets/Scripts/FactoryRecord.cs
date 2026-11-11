using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N035 工場「プレス・ライン」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：プッシャーの前に、プレス機がつく。プレス機は 左・まん中・右 を順に動き、ガシャンと押すと、真下のメダルが鉄メダル（灰色の輪・重い）に変わる
    /// ・モニター：工場のライン。プレスまでの回数（10回のメーター）、次にプレスする場所、作った鉄メダルの数
    /// ・ルール（CSV のとおり）：
    ///   プッシャーが pressEvery 回押すごとに、プレス機の真下に近いメダル1枚を鉄メダルに変える（重さ ×ironMass。まわりを強く押す）
    ///   鉄メダルが中央の穴に落ちると +ironBonus pt
    ///   プレスの場所は 左→まん中→右 と順に動く。メダルが無い所ではプレスが空振りする
    /// ・次のプレスの場所にメダルを送り込んでおく遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class FactoryRecord : RecordScriptBase
    {
        public static FactoryRecord Instance { get; private set; }

        public const string IronKind = "Iron";

        [Header("プレス")]
        [Range(1, 30)] public int pressEvery = 10;
        [Tooltip("プレスで変えるメダルを探す半径（メダルの直径に対する倍率）")]
        [Range(0.3f, 3f)] public float pressRadius = 1f;

        [Header("鉄メダル")]
        [Range(1f, 20f)] public float ironMass = 3f;
        [Range(0, 300)] public int ironBonus = 25;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip pressSound;
        public AudioClip ironSound;
        public GameObject pressPrefab;

        protected override string DefaultKey => "Factory";
        protected override string ScreenTitle => "FACTORY";
        protected override string ScreenSubtitle => "プレス・ライン";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<FactoryRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        public static readonly Color IronColor = new Color32(0x8a, 0x92, 0xa0, 0xff);
        static readonly Color Hazard = new Color32(0xff, 0xc8, 0x1a, 0xff);
        static readonly Color Spark = new Color32(0xff, 0xb0, 0x4a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int pushes, slot, made;
        float lastPhase, slam;
        readonly List<Medal> near = new List<Medal>();

        Transform press, ram;
        ParticleSystem sparks;

        readonly List<Image> ticks = new List<Image>();
        readonly Image[] slots = new Image[3];
        Text madeText, statusText;
        AudioClip madePress, madeIron;

        Vector3 SlotPos(int s) => new Vector3((s - 1) * HalfW * 0.55f, 0f, PusherFrontZ - D * 0.6f);

        protected override void BuildBoard()
        {
            press = new GameObject("Press").transform;
            press.SetParent(Board, false);
            if (pressPrefab != null) Instantiate(pressPrefab, press).transform.localPosition = Vector3.zero;
            var steel = Glow(new Color(0.35f, 0.37f, 0.42f), 0.1f);
            if (pressPrefab == null)
            {
                Cube("Top", press, new Vector3(0f, D * 2f, 0f), new Vector3(D * 1.2f, D * 0.3f, D * 1.0f), steel);
                Cube("Stripe", press, new Vector3(0f, D * 2f, -D * 0.51f), new Vector3(D * 1.2f, D * 0.1f, D * 0.02f), Glow(Hazard, 0.8f));
            }
            ram = Cube("Ram", press, new Vector3(0f, D * 1.2f, 0f), new Vector3(D * 0.8f, D * 0.15f, D * 0.8f), steel).transform;
            Cube("Rod", ram, new Vector3(0f, 3f, 0f), new Vector3(0.2f, 6f, 0.2f), steel);
            sparks = Particles("Sparks", Board, Vector3.up, D * 3f, D * 0.06f, 0.5f, 1f, 70f);
            MedalMarks.RegisterEffect(IronKind, (m, type) =>
            {
                if (type != DropZoneType.Center || effects == null) return;
                effects.AddPoints(ironBonus);
                Play(ironSound, ref madeIron, () => RecordSynth.Notes("FactoryIron", new[] { 330f, 220f }, 0.05f, 0.3f, 0.3f, RecordSynth.Wave.Square), 0.7f);
                Pop($"鉄 +{ironBonus}", IronColor, new Vector2(BodyW * 0.5f, 420f));
            });
        }

        protected override void OnBegin()
        {
            pushes = 0;
            slot = 0;
            made = 0;
            lastPhase = game.Pusher != null ? game.Pusher.Phase01 : 0f;
        }

        protected override void Tick(float dt, bool running)
        {
            slam = Mathf.MoveTowards(slam, 0f, dt * 2.5f);
            if (game.Pusher == null) return;
            float ph = game.Pusher.Phase01;
            bool pushed = lastPhase < 0.5f && ph >= 0.5f;
            lastPhase = ph;
            if (!pushed || !running) return;
            pushes++;
            if (pushes < pressEvery) return;
            pushes = 0;
            Press();
        }

        void Press()
        {
            Vector3 at = SlotPos(slot);
            slam = 1f;
            Play(pressSound, ref madePress, () => RecordSynth.Thud("FactoryPress", 90f, 0.5f, 1f));
            Shake(0.02f, 0.2f);
            Medal best = null;
            float bestD = float.MaxValue;
            foreach (var m in MedalsNear(at, D * pressRadius, near))
            {
                if (MedalMarks.Is(m, IronKind)) continue;
                float d = (m.transform.position - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = m; }
            }
            if (best != null)
            {
                MedalMarks.Set(best, IronKind, IronColor, 0.6f);
                MedalMarks.SetSize(best, 1f, ironMass);
                Emit(sparks, best.transform.position, Spark, 30);
                made++;
                Pop("ガシャン！ 鉄メダル", IronColor, new Vector2(BodyW * (0.2f + 0.3f * slot), 200f));
            }
            else Pop("空振り…", Mute, new Vector2(BodyW * (0.2f + 0.3f * slot), 200f));
            slot = (slot + 1) % 3;
        }

        protected override bool KeepBoardVisible() => sparks != null && sparks.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            int s = slam > 0f ? (slot + 2) % 3 : slot;   // プレス中は、押した場所に残す
            Vector3 p = SlotPos(s);
            press.position = Vector3.Lerp(press.position, new Vector3(p.x, Layout.pusherHeight * 0.2f, p.z), slam > 0f ? 1f : 1f - Mathf.Exp(-5f * dt));
            press.localScale = Vector3.one * Mathf.Max(0.01f, BoardShown);
            float down = slam > 0.6f ? 1f : slam / 0.6f;
            ram.localPosition = new Vector3(0f, Mathf.Lerp(D * 1.6f, D * 0.15f, down), 0f);
        }

        protected override void BuildScreen()
        {
            ticks.Clear();
            float W = BodyW;
            var line = Panel(Screen, "Line", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x1c, 0x1e, 0x22, 0xff));
            float lw = W - 48f;
            Img(line, "Belt", lw * 0.5f, 290f, lw, 40f, new Color32(0x30, 0x32, 0x38, 0xff));
            for (int i = 0; i < 3; i++)
            {
                float cx = lw * (0.2f + 0.3f * i);
                slots[i] = Img(line, $"Slot{i}", cx, 180f, lw * 0.22f, 120f, new Color(1f, 1f, 1f, 0.08f));
                Lbl(line, $"SlotName{i}", cx, 260f, lw * 0.22f, 30f, i == 0 ? "左" : i == 1 ? "まん中" : "右", 18, Mute);
            }
            float cell = (lw - 60f) / Mathf.Max(1, pressEvery);
            for (int i = 0; i < pressEvery; i++)
                ticks.Add(Img(line, $"Tick{i}", 30f + cell * (i + 0.5f), 50f, cell - 6f, 24f, new Color(1f, 1f, 1f, 0.1f)));
            Lbl(line, "TickLabel", 30f, 84f, 400f, 26f, "プレスまでの押した回数", 16, Mute, TextAnchor.MiddleLeft);
            madeText = Lbl(Screen, "Made", 24f, 440f, 600f, 40f, "", 24, IronColor, TextAnchor.MiddleLeft);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int i = 0; i < ticks.Count; i++) ticks[i].color = i < pushes ? Hazard : new Color(1f, 1f, 1f, 0.1f);
            for (int i = 0; i < 3; i++) slots[i].color = i == slot ? new Color(Hazard.r, Hazard.g, Hazard.b, 0.3f + 0.15f * Mathf.Sin(Time.time * 6f)) : new Color(1f, 1f, 1f, 0.08f);
            madeText.text = $"作った鉄メダル {made}枚（盤面に {MedalMarks.Count(IronKind)}枚）";
            statusText.text = $"プッシャーが {pressEvery}回押すとプレス。次は「{(slot == 0 ? "左" : slot == 1 ? "まん中" : "右")}」。鉄メダルは中央の穴で +{ironBonus}pt";
        }
    }
}
