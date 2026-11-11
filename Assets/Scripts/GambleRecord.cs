using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N018 ギャンブル「ネオン・スロット」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：そろった時に、盤面の照明（左右のランプ）が点滅する
    /// ・モニター：3つのリールのスロット。メダルが穴に落ちるたびに、左のリールから順に止まる。リーチの時はリールが光る
    /// ・ルール（CSV のとおり）：
    ///   3つそろうとイベント：
    ///     7      ：sevenSeconds 秒のあいだ、中央の穴のポイント ×sevenMultiplier
    ///     ベル    ：盤面にメダルが bellMedals 枚降ってくる（「メダルが20枚降る」）
    ///     チェリー：ゲージ +cherryGauge
    ///     星      ：盤面のメダル starMedals 枚が星メダルになる（中央の穴で +50pt。N026 星と同じ印）
    ///     ドクロ  ：プッシャーが skullSeconds 秒止まる
    ///   リーチ（2つそろった）の時、3つ目がそろう確率は reachChance
    /// ・端の穴でもリールは止まるので、どの穴でもよいからテンポよく落とす遊び。ドクロの時は投入を控える判断も
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class GambleRecord : RecordScriptBase
    {
        public static GambleRecord Instance { get; private set; }

        [Header("リール")]
        [Tooltip("リーチの時に、3つ目がそろう確率")]
        [Range(0f, 1f)] public float reachChance = 0.35f;
        [Tooltip("そろわなかった時、次に回り出すまでの秒数")]
        [Range(0f, 5f)] public float restartSeconds = 0.8f;
        [Tooltip("絵柄の出やすさ（7・ベル・チェリー・星・ドクロ の順）")]
        public float[] symbolWeights = { 1f, 2f, 3f, 2f, 1.5f };

        [Header("イベント")]
        [Range(1f, 60f)] public float sevenSeconds = 10f;
        [Range(1f, 10f)] public float sevenMultiplier = 5f;
        [Range(1, 50)] public int bellMedals = 20;
        [Range(0f, 100f)] public float cherryGauge = 50f;
        [Range(1, 50)] public int starMedals = 10;
        [Range(1f, 30f)] public float skullSeconds = 5f;

        [Header("盤面のランプ")]
        [Range(0f, 10f)] public float lampIntensity = 3f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip stopSound;
        public AudioClip reachSound;
        public AudioClip winSound;
        public AudioClip skullSound;

        protected override string DefaultKey => "Gamble";
        protected override string ScreenTitle => "GAMBLE";
        protected override string ScreenSubtitle => "ネオン・スロット";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<GambleRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly string[] Names = { "7", "ベル", "チェリー", "★", "ドクロ" };
        static readonly Color[] SymbolColors =
        {
            new Color32(0xff, 0x3c, 0x3c, 0xff), new Color32(0xff, 0xd2, 0x3f, 0xff), new Color32(0xff, 0x5a, 0x9a, 0xff),
            new Color32(0x6c, 0xd8, 0xff, 0xff), new Color32(0xb0, 0xb0, 0xc0, 0xff),
        };
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Cabinet = new Color32(0x22, 0x10, 0x2a, 0xff);

        readonly int[] result = new int[3];
        int stopped;              // 止まったリールの数
        float restartTimer, sevenTimer, skullTimer, flashTimer, spin;
        int flashSymbol;

        readonly Light[] lamps = new Light[2];

        readonly Text[] reelTexts = new Text[3];
        readonly Image[] reelBoxes = new Image[3];
        Text eventText, statusText;
        AudioClip madeStop, madeReach, madeWin, madeSkull;

        protected override void BuildBoard()
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                lamps[i] = PointLight(side < 0 ? "LampLeft" : "LampRight", Board, new Vector3(side * HalfW * 0.8f, Layout.wallHeight * 0.6f, (FrontZ + BackZ) * 0.5f), SymbolColors[1], Layout.boardWidth);
            }
        }

        protected override void OnBegin()
        {
            stopped = 0;
            restartTimer = 0f;
            sevenTimer = 0f;
            skullTimer = 0f;
        }

        int RandomSymbol()
        {
            float sum = 0f;
            foreach (float w in symbolWeights) sum += Mathf.Max(0f, w);
            float r = Random.value * Mathf.Max(0.0001f, sum);
            for (int i = 0; i < Names.Length && i < symbolWeights.Length; i++)
            {
                r -= Mathf.Max(0f, symbolWeights[i]);
                if (r <= 0f) return i;
            }
            return 0;
        }

        protected override void Tick(float dt, bool running)
        {
            spin += dt * 18f;
            flashTimer = Mathf.MoveTowards(flashTimer, 0f, dt * 0.5f);
            if (sevenTimer > 0f) sevenTimer -= dt;
            if (skullTimer > 0f) skullTimer -= dt;
            PusherHold = skullTimer > 0f;
            if (restartTimer > 0f)
            {
                restartTimer -= dt;
                if (restartTimer <= 0f) stopped = 0;
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type == DropZoneType.Center && sevenTimer > 0f)
            {
                int extra = AddMultiplied(sevenMultiplier);
                Pop($"×{sevenMultiplier:0} +{CenterPoints + extra}", SymbolColors[0], new Vector2(BodyW * Random.Range(0.3f, 0.7f), 420f));
            }
            if (stopped >= 3) return;

            int sym = RandomSymbol();
            if (stopped == 2 && result[0] == result[1] && Random.value < reachChance) sym = result[0];
            result[stopped] = sym;
            stopped++;
            Play(stopSound, ref madeStop, () => RecordSynth.Notes("GambleStop", new[] { 660f }, 0.03f, 0.08f, 0.25f, RecordSynth.Wave.Square), 0.6f);
            if (stopped == 2 && result[0] == result[1])
                Play(reachSound, ref madeReach, () => RecordSynth.Notes("GambleReach", new[] { 880f, 988f, 880f, 988f }, 0.08f, 0.15f, 0.25f, RecordSynth.Wave.Square));
            if (stopped == 3) Judge();
        }

        void Judge()
        {
            restartTimer = restartSeconds;
            if (result[0] != result[1] || result[1] != result[2]) return;
            restartTimer = restartSeconds + 1.5f;
            int s = result[0];
            flashSymbol = s;
            flashTimer = 1f;
            switch (s)
            {
                case 0:
                    sevenTimer = sevenSeconds;
                    PopCenter($"777！ {sevenSeconds:0}秒間 ×{sevenMultiplier:0}", SymbolColors[0]);
                    break;
                case 1:
                    RainMedals(bellMedals);
                    PopCenter($"ベル！ メダルが {bellMedals}枚降る", SymbolColors[1]);
                    break;
                case 2:
                    AddGauge(cherryGauge);
                    PopCenter($"チェリー！ ゲージ +{cherryGauge:0}", SymbolColors[2]);
                    break;
                case 3:
                    int n = 0;
                    for (int i = 0; i < starMedals; i++)
                    {
                        var m = PickMedal(x => !MedalMarks.Has(x));
                        if (m == null) break;
                        MedalMarks.Set(m, StarRecord.StarKind, SymbolColors[3], 3f);
                        n++;
                    }
                    PopCenter($"星！ {n}枚が星メダルに", SymbolColors[3]);
                    break;
                default:
                    skullTimer = skullSeconds;
                    Play(skullSound, ref madeSkull, () => RecordSynth.Sweep("GambleSkull", 300f, 60f, 0.8f, 0.4f, RecordSynth.Wave.Saw));
                    PopCenter($"ドクロ… プッシャーが {skullSeconds:0}秒止まる", SymbolColors[4]);
                    return;
            }
            Play(winSound, ref madeWin, () => RecordSynth.Notes("GambleWin", new[] { RecordSynth.Note(12), RecordSynth.Note(16), RecordSynth.Note(19), RecordSynth.Note(24), RecordSynth.Note(19), RecordSynth.Note(24) }, 0.07f, 0.5f, 0.3f, RecordSynth.Wave.Square));
            Speaker(1f, 16, SymbolColors[s]);
        }

        protected override void UpdateBoard(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                if (lamps[i] == null) continue;
                bool blink = flashTimer > 0f && Mathf.Repeat(Time.time * 8f + i, 2f) < 1f;
                lamps[i].color = flashTimer > 0f ? SymbolColors[flashSymbol] : SymbolColors[1];
                lamps[i].intensity = BoardShown * lampIntensity * (blink ? 1f : flashTimer > 0f ? 0.1f : 0.25f);
            }
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var cab = Panel(Screen, "Cabinet", W * 0.5f, 200f, W - 48f, 360f, Cabinet);
            float cw = W - 48f;
            Lbl(cab, "Logo", cw * 0.5f, 34f, cw, 50f, "NEON SLOT 198X", 30, SymbolColors[1], TextAnchor.MiddleCenter, true);
            float rw = Mathf.Min(220f, (cw - 120f) / 3f);
            for (int i = 0; i < 3; i++)
            {
                float cx = cw * 0.5f + (i - 1) * (rw + 24f);
                Img(cab, $"ReelFrame{i}", cx, 190f, rw + 10f, 230f, SymbolColors[1], MonitorDisplay.ScreenShape.Pill);
                reelBoxes[i] = Img(cab, $"Reel{i}", cx, 190f, rw, 220f, new Color32(0xf8, 0xf4, 0xe8, 0xff), MonitorDisplay.ScreenShape.Pill);
                reelTexts[i] = Lbl(cab, $"Sym{i}", cx, 190f, rw, 200f, "", 52, Ink, TextAnchor.MiddleCenter);
            }
            eventText = Lbl(cab, "Event", cw * 0.5f, 330f, cw, 40f, "", 24, Ink);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
            Lbl(Screen, "Legend", 24f, 440f, W - 48f, 60f,
                $"7：{sevenSeconds:0}秒 ×{sevenMultiplier:0}　ベル：メダル{bellMedals}枚　チェリー：ゲージ+{cherryGauge:0}　★：星メダル{starMedals}枚　ドクロ：プッシャー停止",
                16, Mute, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int i = 0; i < 3; i++)
            {
                int sym = i < stopped ? result[i] : (Mathf.FloorToInt(spin + i * 1.7f) % Names.Length);
                reelTexts[i].text = Names[sym];
                reelTexts[i].color = SymbolColors[sym];
                bool reach = stopped == 2 && i == 2 && result[0] == result[1];
                reelBoxes[i].color = reach ? Color.Lerp(new Color32(0xf8, 0xf4, 0xe8, 0xff), SymbolColors[result[0]], 0.4f + 0.4f * Mathf.Sin(Time.time * 14f))
                                           : i < stopped ? new Color32(0xf8, 0xf4, 0xe8, 0xff) : new Color32(0xd8, 0xd4, 0xc8, 0xff);
            }
            string ev = sevenTimer > 0f ? $"<color=#FF3C3C>×{sevenMultiplier:0}  残り {Mathf.CeilToInt(sevenTimer)}秒</color>"
                      : skullTimer > 0f ? $"<color=#B0B0C0>プッシャー停止  {Mathf.CeilToInt(skullTimer)}秒</color>" : "";
            eventText.text = ev;
            statusText.text = stopped == 2 && result[0] == result[1]
                ? "<color=#FFD23F>リーチ！</color>  次に穴に落ちると、最後のリールが止まる"
                : "メダルが穴（中央・端）に落ちるたびに、リールが1つ止まる";
        }
    }
}
