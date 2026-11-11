using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N012 磁石「N極・S極」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：中央の穴の左右の奥に、赤（N）と青（S）のU字磁石がせり上がる。ON の磁石は光り、近くのメダルが細かく震えながら穴の方へ寄る
    /// ・モニター：左右の磁石の ON/OFF と磁力線、入れかわりまでの時間、超磁力のメーター
    /// ・ルール：
    ///   switchSeconds 秒ごとに、ON の磁石が左右で入れかわる。ON の磁石は、近くのメダルを中央の穴の方へ引き寄せる
    ///   ON の側の半分に落ちると「くっついた」：+stickBonus pt、超磁力 +1
    ///   超磁力が chargeToSuper たまると superSeconds 秒のあいだ両方の磁石が ON で、引く力が superForce 倍
    /// ・ON の磁石の側へ投入して、寄せてもらう遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class MagnetRecord : RecordScriptBase
    {
        public static MagnetRecord Instance { get; private set; }

        [Header("磁石")]
        [Range(1f, 30f)] public float switchSeconds = 6f;
        [Tooltip("引き寄せる半径（メダルの直径に対する倍率）")]
        [Range(0.5f, 6f)] public float pullRadius = 2.5f;
        [Tooltip("引き寄せる強さ")]
        [Range(0f, 30f)] public float pullForce = 5f;

        [Header("ポイント")]
        [Range(0, 100)] public int stickBonus = 5;

        [Header("超磁力")]
        [Range(1, 30)] public int chargeToSuper = 10;
        [Range(1f, 30f)] public float superSeconds = 6f;
        [Range(1f, 5f)] public float superForce = 2f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip stickSound;
        public AudioClip switchSound;
        public AudioClip superSound;

        protected override string DefaultKey => "Magnet";
        protected override string ScreenTitle => "MAGNET";
        protected override string ScreenSubtitle => "N極・S極";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<MagnetRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color NRed = new Color32(0xff, 0x45, 0x45, 0xff);
        static readonly Color SBlue = new Color32(0x45, 0x8c, 0xff, 0xff);
        static readonly Color Steel = new Color32(0xc8, 0xcc, 0xd4, 0xff);
        static readonly Color Super = new Color32(0xc0, 0x6c, 0xff, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int onSide, charge;
        float switchTimer, superTimer, stickPulse;
        bool SuperOn => superTimer > 0f;
        readonly List<Medal> near = new List<Medal>();

        readonly Transform[] magnets = new Transform[2];
        readonly Material[] magnetMaterials = new Material[2];
        ParticleSystem sparks;

        readonly Image[] boxes = new Image[2];
        readonly Text[] labels = new Text[2];
        readonly List<Image> fieldLines = new List<Image>();
        Image chargeFill;
        Text timerText, statusText;
        AudioClip madeStick, madeSwitch, madeSuper;

        Vector3 MagnetPos(int side) => new Vector3(side * HoleHalf * 0.5f, 0f, FrontZ + D * 1.6f);
        Vector3 PullTarget(int side) => new Vector3(side * HoleHalf * 0.5f, 0f, FrontZ - D * 0.2f);

        protected override void BuildBoard()
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                Color c = side < 0 ? NRed : SBlue;
                var holder = new GameObject(side < 0 ? "MagnetN" : "MagnetS").transform;
                holder.SetParent(Board, false);
                holder.position = MagnetPos(side);
                magnetMaterials[i] = Glow(c, 1f);
                var steel = Glow(Steel, 0.3f);
                Cube("ArmL", holder, new Vector3(-D * 0.3f, D * 0.4f, 0f), new Vector3(D * 0.2f, D * 0.8f, D * 0.2f), magnetMaterials[i]);
                Cube("ArmR", holder, new Vector3(D * 0.3f, D * 0.4f, 0f), new Vector3(D * 0.2f, D * 0.8f, D * 0.2f), magnetMaterials[i]);
                Cube("Base", holder, new Vector3(0f, D * 0.85f, 0f), new Vector3(D * 0.8f, D * 0.2f, D * 0.2f), magnetMaterials[i]);
                Cube("TipL", holder, new Vector3(-D * 0.3f, D * 0.05f, 0f), new Vector3(D * 0.22f, D * 0.1f, D * 0.22f), steel);
                Cube("TipR", holder, new Vector3(D * 0.3f, D * 0.05f, 0f), new Vector3(D * 0.22f, D * 0.1f, D * 0.22f), steel);
                magnets[i] = holder;
            }
            sparks = Particles("Sparks", Board, Vector3.up, D * 2f, D * 0.06f, 0.5f, 0.3f, 60f);
        }

        protected override void OnBegin()
        {
            onSide = Random.value < 0.5f ? -1 : 1;
            switchTimer = switchSeconds;
            superTimer = 0f;
            charge = 0;
        }

        bool IsOn(int side) => SuperOn || side == onSide;

        protected override void Tick(float dt, bool running)
        {
            stickPulse = Mathf.MoveTowards(stickPulse, 0f, dt * 3f);
            if (!running) return;
            if (SuperOn)
            {
                superTimer -= dt;
                return;
            }
            switchTimer -= dt;
            if (switchTimer <= 0f)
            {
                switchTimer = switchSeconds;
                onSide = -onSide;
                Play(switchSound, ref madeSwitch, () => RecordSynth.Sweep("MagnetSwitch", 120f, 240f, 0.3f, 0.3f, RecordSynth.Wave.Square), 0.6f);
            }
        }

        void FixedUpdate()
        {
            if (!Running || game == null) return;
            float r = D * pullRadius;
            float force = pullForce * (SuperOn ? superForce : 1f);
            for (int s = -1; s <= 1; s += 2)
            {
                if (!IsOn(s)) continue;
                Vector3 target = PullTarget(s);
                MedalsNear(MagnetPos(s), r, near);
                foreach (var m in near)
                {
                    if (m.Body == null) continue;
                    Vector3 d = target - m.transform.position;
                    d.y = 0f;
                    float k = 1f - Mathf.Clamp01(Vector3.Distance(m.transform.position, MagnetPos(s)) / r);
                    m.Body.WakeUp();
                    // 引き寄せる力と、細かい震え
                    Vector3 jitter = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * 2f;
                    m.Body.AddForce((d.normalized * force + jitter) * (0.3f + k), ForceMode.Acceleration);
                }
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            int side = medal.transform.position.x < 0f ? -1 : 1;
            if (!IsOn(side)) return;
            AddPoints(stickBonus);
            stickPulse = 1f;
            Emit(sparks, MagnetPos(side) + Vector3.up * D * 0.1f, side < 0 ? NRed : SBlue, 14);
            Play(stickSound, ref madeStick, () => RecordSynth.Notes("MagnetStick", new[] { 1568f }, 0.03f, 0.08f, 0.2f, RecordSynth.Wave.Square), 0.6f);
            Pop($"くっついた +{stickBonus}", side < 0 ? NRed : SBlue, new Vector2(side < 0 ? BodyW * 0.3f : BodyW * 0.7f, 300f));
            if (SuperOn) return;
            charge++;
            if (charge >= chargeToSuper)
            {
                charge = 0;
                superTimer = superSeconds;
                Play(superSound, ref madeSuper, () => RecordSynth.Sweep("MagnetSuper", 80f, 640f, 1f, 0.35f, RecordSynth.Wave.Saw));
                PopCenter("超磁力！", Super);
                Speaker(0.9f, 12, Super);
            }
        }

        protected override bool KeepBoardVisible() => sparks != null && sparks.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                bool on = Active && IsOn(side);
                Vector3 p = MagnetPos(side);
                float rise = BoardShown;
                magnets[i].position = new Vector3(p.x, -D * 0.9f * (1f - rise) + (on ? Mathf.Sin(Time.time * 40f) * D * 0.01f : 0f), p.z);
                Color c = SuperOn ? Super : side < 0 ? NRed : SBlue;
                SetGlow(magnetMaterials[i], c, on ? 2f + stickPulse * 2f : 0.3f);
            }
        }

        protected override void BuildScreen()
        {
            fieldLines.Clear();
            float W = BodyW;
            var panel = Panel(Screen, "Field", W * 0.5f, 200f, W - 48f, 340f, new Color32(0x12, 0x14, 0x1c, 0xff));
            float pw = W - 48f;
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                float cx = side < 0 ? pw * 0.25f : pw * 0.75f;
                for (int k = 0; k < 4; k++)
                    fieldLines.Add(Img(panel, $"Line{i}_{k}", cx, 170f, 120f + k * 70f, 80f + k * 50f, new Color(1f, 1f, 1f, 0.1f), MonitorDisplay.ScreenShape.Ring));
                boxes[i] = Img(panel, $"Mag{i}", cx, 170f, 110f, 110f, side < 0 ? NRed : SBlue, MonitorDisplay.ScreenShape.Circle);
                labels[i] = Lbl(panel, $"Label{i}", cx, 170f, 110f, 110f, side < 0 ? "N" : "S", 64, Ink, TextAnchor.MiddleCenter, true);
            }
            timerText = Lbl(panel, "Timer", pw * 0.5f, 310f, pw, 40f, "", 22, Ink);
            Lbl(Screen, "ChargeLabel", 24f, 450f, 140f, 30f, "超磁力", 20, Ink, TextAnchor.MiddleLeft);
            chargeFill = Bar(Screen, "Charge", 140f, 450f, W - 200f, 20f, Super);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                bool on = IsOn(side);
                Color c = SuperOn ? Super : side < 0 ? NRed : SBlue;
                boxes[i].color = on ? Color.Lerp(c, Color.white, stickPulse * 0.4f) : new Color(c.r, c.g, c.b, 0.25f);
                Scale(boxes[i].rectTransform, on ? 1f + 0.05f * Mathf.Sin(Time.time * 12f) : 0.85f);
                for (int k = 0; k < 4; k++)
                {
                    var line = fieldLines[i * 4 + k];
                    float a = on ? 0.15f + 0.2f * Mathf.Repeat(Time.time * 1.5f - k * 0.25f, 1f) : 0.05f;
                    line.color = new Color(c.r, c.g, c.b, a);
                }
            }
            timerText.text = SuperOn ? $"超磁力  残り {Mathf.CeilToInt(superTimer)}秒" : $"ON：{(onSide < 0 ? "左（N）" : "右（S）")}   入れかわりまで {Mathf.CeilToInt(switchTimer)}秒";
            chargeFill.fillAmount = SuperOn ? superTimer / Mathf.Max(0.1f, superSeconds) : charge / (float)Mathf.Max(1, chargeToSuper);
            statusText.text = SuperOn
                ? "<color=#C06CFF>超磁力！</color>  両方の磁石が強く引き寄せる"
                : $"ON の磁石の側の半分に落とすと +{stickBonus}pt。{chargeToSuper}回で超磁力";
        }
    }
}
