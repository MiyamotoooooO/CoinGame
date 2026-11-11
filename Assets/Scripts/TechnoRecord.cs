using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N007 テクノ「レーザー・スイッチ」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：中央の穴の左半分・右半分の上に、レーザーのゲートが立つ。ON のゲートは緑に光り、OFF は赤く暗い。
    ///   盤面をレーザーの線がビートに合わせて横切る
    /// ・モニター：16マスのシーケンサー（ビートで光が進む）。下に左右のゲートの ON/OFF と、BUILD のメーター
    /// ・ルール：
    ///   発動中は、プッシャーの速さ ×pusherSpeed（CSV のとおり）
    ///   switchSeconds 秒ごとに、ON のゲートが左右で入れかわる。ON の側の半分に落ちると +onBonus pt、BUILD +1
    ///   BUILD が buildToDrop たまると「DROP!」：dropSeconds 秒のあいだ、両方のゲートが ON、プッシャー ×dropPusherSpeed
    /// ・ON の側の発射口をえらんでねらう遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class TechnoRecord : RecordScriptBase
    {
        public static TechnoRecord Instance { get; private set; }

        [Header("プッシャー")]
        [Tooltip("発動中のプッシャーの速さ（CSV の ×2）")]
        [Range(0.2f, 5f)] public float pusherSpeed = 2f;

        [Header("レーザーのゲート")]
        [Tooltip("ON の側が入れかわる秒数")]
        [Range(1f, 30f)] public float switchSeconds = 7.5f;
        [Tooltip("ON の側の半分に落ちた時のポイント")]
        [Range(0, 100)] public int onBonus = 8;

        [Header("DROP（BUILD が満タン）")]
        [Range(1, 30)] public int buildToDrop = 8;
        [Range(1f, 30f)] public float dropSeconds = 6f;
        [Range(0.2f, 6f)] public float dropPusherSpeed = 3f;

        [Header("見せ方")]
        [Tooltip("ビートの速さ（BPM）。レーザーとシーケンサーが合わせて動く")]
        [Range(60f, 200f)] public float bpm = 128f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip gateHitSound;
        public AudioClip switchSound;
        public AudioClip dropSound;

        protected override string DefaultKey => "Techno";
        protected override string ScreenTitle => "TECHNO";
        protected override string ScreenSubtitle => "レーザー・スイッチ";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<TechnoRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color On = new Color32(0x3c, 0xff, 0x8a, 0xff);
        static readonly Color Off = new Color32(0xff, 0x3c, 0x5a, 0xff);
        static readonly Color Laser = new Color32(0x3f, 0xe0, 0xff, 0xff);
        static readonly Color Magenta = new Color32(0xff, 0x3c, 0xd2, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Dark = new Color32(0x14, 0x14, 0x1a, 0xff);

        int onSide;              // ON の側（-1 左、1 右）
        float switchTimer, dropTimer, hitPulse;
        int build;
        bool Dropping => dropTimer > 0f;
        float Beat => Elapsed * bpm / 60f;

        readonly GameObject[] gates = new GameObject[2];
        readonly Material[] gateMaterials = new Material[2];
        readonly List<Transform> lasers = new List<Transform>();
        Material laserMaterial;
        ParticleSystem sparks;

        readonly List<Image> steps = new List<Image>();
        readonly Image[] gateBoxes = new Image[2];
        readonly Text[] gateTexts = new Text[2];
        Image buildFill;
        Text buildText, statusText, bigText;
        AudioClip madeHit, madeSwitch, madeDrop;

        protected override void BuildBoard()
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                gateMaterials[i] = Glow(Off, 1f);
                gates[i] = Cube(side < 0 ? "GateLeft" : "GateRight", Board,
                                new Vector3(side * HoleHalf * 0.5f, D * 0.9f, FrontZ - D * 0.1f),
                                new Vector3(HoleHalf * 0.9f, D * 0.05f, D * 0.05f), gateMaterials[i]);
            }
            laserMaterial = Glow(Laser, 3f);
            for (int i = 0; i < 4; i++)
            {
                var l = Cube($"Laser{i}", Board, new Vector3(0f, D * 0.08f, FrontZ + D * (0.8f + i * 0.7f)), new Vector3(Layout.boardWidth, D * 0.015f, D * 0.015f), laserMaterial);
                lasers.Add(l.transform);
            }
            sparks = Particles("Sparks", Board, Vector3.up, D * 3f, D * 0.07f, 0.6f, 0.6f, 35f);
        }

        protected override void OnBegin()
        {
            onSide = Random.value < 0.5f ? -1 : 1;
            switchTimer = switchSeconds;
            dropTimer = 0f;
            build = 0;
        }

        protected override void Tick(float dt, bool running)
        {
            hitPulse = Mathf.MoveTowards(hitPulse, 0f, dt * 3f);
            PusherSpeedScale = Dropping ? dropPusherSpeed : pusherSpeed;
            if (!running) return;
            if (Dropping)
            {
                dropTimer -= dt;
                return;
            }
            switchTimer -= dt;
            if (switchTimer <= 0f)
            {
                switchTimer = switchSeconds;
                onSide = -onSide;
                Play(switchSound, ref madeSwitch, () => RecordSynth.Sweep("TechnoSwitch", 220f, 880f, 0.25f, 0.25f, RecordSynth.Wave.Square));
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            int side = medal.transform.position.x < 0f ? -1 : 1;
            if (!Dropping && side != onSide) return;

            AddPoints(onBonus);
            hitPulse = 1f;
            Emit(sparks, new Vector3(side * HoleHalf * 0.5f, D * 0.9f, FrontZ), Dropping ? Magenta : On, 18);
            Play(gateHitSound, ref madeHit, () => RecordSynth.Notes("TechnoHit", new[] { 880f, 1320f }, 0.04f, 0.12f, 0.25f, RecordSynth.Wave.Square), 0.7f);
            Pop($"ON +{onBonus}", Dropping ? Magenta : On, new Vector2(side < 0 ? BodyW * 0.3f : BodyW * 0.7f, 430f));
            if (Dropping) return;
            build++;
            if (build >= buildToDrop)
            {
                build = 0;
                dropTimer = dropSeconds;
                Play(dropSound, ref madeDrop, () => RecordSynth.Thud("TechnoDrop", 55f, 0.8f, 0.9f));
                PopCenter("DROP!", Magenta);
                Shake(0.05f, 0.5f);
                Speaker(1f, 16, Magenta);
            }
        }

        protected override bool KeepBoardVisible() => sparks != null && sparks.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            float beatFrac = Beat - Mathf.Floor(Beat);
            float flash = 1f - beatFrac;
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                bool on = Active && (Dropping || side == onSide);
                Color c = Dropping ? Color.Lerp(Magenta, Laser, 0.5f + 0.5f * Mathf.Sin(Time.time * 10f)) : on ? On : Off;
                SetGlow(gateMaterials[i], c, (on ? 2f + 2f * flash + 2f * hitPulse : 0.4f) * BoardShown);
            }
            for (int i = 0; i < lasers.Count; i++)
            {
                float z = Mathf.Lerp(FrontZ + D * 0.5f, PusherFrontZ, Mathf.Repeat(Beat * 0.25f + i / (float)lasers.Count, 1f));
                var p = lasers[i].localPosition;
                lasers[i].localPosition = new Vector3(p.x, p.y, z);
                lasers[i].localScale = new Vector3(Layout.boardWidth * BoardShown, D * 0.015f, D * 0.015f);
            }
            SetGlow(laserMaterial, Dropping ? Magenta : Laser, 1.5f + 2.5f * flash);
        }

        protected override void BuildScreen()
        {
            steps.Clear();
            float W = BodyW;
            var panel = Panel(Screen, "Seq", W * 0.5f, 180f, W - 48f, 300f, Dark);
            float pw = W - 48f;
            bigText = Lbl(panel, "Big", pw * 0.5f, 70f, pw, 100f, "", 64, Ink, TextAnchor.MiddleCenter, true);
            float cell = (pw - 40f) / 16f;
            for (int i = 0; i < 16; i++)
                steps.Add(Img(panel, $"Step{i}", 20f + cell * (i + 0.5f), 190f, cell - 8f, 70f, new Color(1f, 1f, 1f, 0.08f)));
            Lbl(panel, "Bpm", pw - 16f, 270f, 300f, 30f, $"{bpm:0} BPM", 18, Mute, TextAnchor.MiddleRight, true);

            for (int i = 0; i < 2; i++)
            {
                float cx = i == 0 ? W * 0.3f : W * 0.7f;
                gateBoxes[i] = Img(Screen, $"Gate{i}", cx, 400f, W * 0.36f, 70f, Off, MonitorDisplay.ScreenShape.Pill);
                gateTexts[i] = Lbl(Screen, $"GateText{i}", cx, 400f, W * 0.36f, 70f, "", 28, Dark, TextAnchor.MiddleCenter, true);
            }
            Lbl(Screen, "BuildLabel", 24f, 490f, 120f, 30f, "BUILD", 20, Ink, TextAnchor.MiddleLeft, true);
            buildFill = Bar(Screen, "Build", 140f, 490f, W - 260f, 20f, Laser);
            buildText = Lbl(Screen, "BuildText", W - 24f, 490f, 100f, 30f, "", 22, Ink, TextAnchor.MiddleRight, true);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            int beat = Mathf.FloorToInt(Beat) % 16;
            for (int i = 0; i < steps.Count; i++)
            {
                bool accent = i % 4 == 0;
                Color c = i == beat ? (Dropping ? Magenta : Laser) : accent ? new Color(1f, 1f, 1f, 0.22f) : new Color(1f, 1f, 1f, 0.08f);
                steps[i].color = c;
            }
            bigText.text = Dropping ? $"DROP!  {Mathf.CeilToInt(dropTimer)}" : $"PUSHER ×{pusherSpeed:0.#}";
            bigText.color = Dropping ? Magenta : Ink;
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                bool on = Dropping || side == onSide;
                gateBoxes[i].color = on ? Color.Lerp(Dropping ? Magenta : On, Color.white, hitPulse * 0.4f) : new Color(Off.r, Off.g, Off.b, 0.35f);
                gateTexts[i].text = $"{(side < 0 ? "LEFT" : "RIGHT")}  {(on ? "ON" : "OFF")}";
            }
            buildFill.fillAmount = Dropping ? dropTimer / Mathf.Max(0.1f, dropSeconds) : build / (float)Mathf.Max(1, buildToDrop);
            buildFill.color = Dropping ? Magenta : Laser;
            buildText.text = Dropping ? "DROP" : $"{build}/{buildToDrop}";
            statusText.text = Dropping
                ? $"<color=#FF3CD2>DROP!</color>  両方のゲートが ON・プッシャー ×{dropPusherSpeed:0.#}"
                : $"ON の側（{(onSide < 0 ? "左" : "右")}）の半分に落とすと +{onBonus}pt  入れかわりまで {Mathf.CeilToInt(switchTimer)}秒";
        }
    }
}
