using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N018 ギャンブル「ネオン・スロット」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：そろった時に、盤面の照明（左右のランプ）が点滅する
    /// ・モニター：3つのリールのスロット。絵柄（7・ベル・さくらんぼ・星・ドクロ）が上から下へ流れて回り、
    ///   メダルが穴に落ちるたびに、左のリールから順に止まる（止まる時に少し跳ねる）。リーチの時はリールが光り、そろうと絵柄が光って大きくなる
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

        const int ReelCells = 4;
        const float CellH = 112f;
        readonly Image[][] reelCells = new Image[3][];
        readonly Image[] reelBoxes = new Image[3];
        readonly Image[] reelGlows = new Image[3];
        readonly float[] reelPos = new float[3];
        readonly float[] stopTime = new float[3];
        float reelH = 224f;
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
            stopTime[stopped] = Time.time;
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
            float rw = Mathf.Min(200f, (cw - 120f) / 3f);
            for (int i = 0; i < 3; i++)
            {
                float cx = cw * 0.5f + (i - 1) * (rw + 24f);
                reelGlows[i] = Img(cab, $"ReelGlow{i}", cx, 190f, rw + 26f, reelH + 26f, new Color(1f, 0.85f, 0.3f, 0f), MonitorDisplay.ScreenShape.Pill);
                Img(cab, $"ReelFrame{i}", cx, 190f, rw + 10f, reelH + 10f, SymbolColors[1], MonitorDisplay.ScreenShape.Pill);
                var window = Panel(cab, $"Reel{i}", cx, 190f, rw, reelH, new Color32(0xf8, 0xf4, 0xe8, 0xff));
                reelBoxes[i] = window.GetComponent<Image>();
                reelCells[i] = new Image[ReelCells];
                float size = Mathf.Min(rw - 16f, CellH - 10f);
                for (int k = 0; k < ReelCells; k++)
                {
                    var img = Img(window, $"Sym{i}_{k}", rw * 0.5f, reelH * 0.5f, size, size, Color.white);
                    img.sprite = RecordSymbolSprites.Get(k % RecordSymbolSprites.Count);
                    img.preserveAspect = true;
                    reelCells[i][k] = img;
                }
                // 上と下を暗くして、丸いリールに見せる
                Img(window, "ShadeTop", rw * 0.5f, 18f, rw, 36f, new Color(0f, 0f, 0f, 0.28f));
                Img(window, "ShadeBottom", rw * 0.5f, reelH - 18f, rw, 36f, new Color(0f, 0f, 0f, 0.28f));
                Img(window, "Shine", rw * 0.3f, reelH * 0.5f, 10f, reelH - 30f, new Color(1f, 1f, 1f, 0.18f), MonitorDisplay.ScreenShape.Pill);
                reelPos[i] = Random.Range(0f, 5f);
            }
            Img(cab, "Payline", cw * 0.5f, 190f, 3f * (rw + 24f) + 10f, 3f, new Color(1f, 0.2f, 0.2f, 0.7f));
            eventText = Lbl(cab, "Event", cw * 0.5f, 330f, cw, 40f, "", 24, Ink);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);

            // 絵柄の説明（絵と短い文字）
            string[] legend =
            {
                $"{sevenSeconds:0}秒 ×{sevenMultiplier:0}", $"メダル{bellMedals}枚", $"ゲージ+{cherryGauge:0}", $"星メダル{starMedals}枚", "プッシャー停止",
            };
            float lw = (W - 48f) / 5f;
            for (int i = 0; i < 5; i++)
            {
                float x = 24f + lw * i;
                var icon = Img(Screen, $"LegendIcon{i}", x + 22f, 440f, 40f, 40f, Color.white);
                icon.sprite = RecordSymbolSprites.Get(i);
                icon.preserveAspect = true;
                Lbl(Screen, $"Legend{i}", x + 46f, 440f, lw - 50f, 40f, legend[i], 16, Mute, TextAnchor.MiddleLeft);
            }
        }

        protected override void UpdateScreen(float dt)
        {
            float rw = 0f;
            bool win = flashTimer > 0f && stopped == 3 && result[0] == result[1] && result[1] == result[2];
            for (int i = 0; i < 3; i++)
            {
                if (reelBoxes[i] == null) continue;
                rw = reelBoxes[i].rectTransform.sizeDelta.x;
                float frac, offset = 0f;
                int baseSym;
                if (i < stopped)
                {
                    // 止まった：結果の絵柄をまん中に。止まった直後は少し跳ねる
                    float b = Time.time - stopTime[i];
                    offset = Mathf.Exp(-b * 9f) * Mathf.Sin(b * 28f) * 18f;
                    baseSym = result[i];
                    frac = 0f;
                    reelPos[i] = result[i];
                }
                else
                {
                    reelPos[i] += dt * 14f;
                    baseSym = Mathf.FloorToInt(reelPos[i]);
                    frac = reelPos[i] - baseSym;
                }
                for (int k = 0; k < ReelCells; k++)
                {
                    int slot = k - 2;   // -2, -1, 0, 1
                    int sym = ((baseSym - slot) % 5 + 5) % 5;
                    var img = reelCells[i][k];
                    img.sprite = RecordSymbolSprites.Get(sym);
                    float y = reelH * 0.5f + (slot + frac) * CellH + offset;
                    Place(img.rectTransform, rw * 0.5f, y);
                    bool center = i < stopped && slot == 0;
                    float pulse = win && center ? 1.15f + 0.15f * Mathf.Sin(Time.time * 16f) : 1f;
                    img.rectTransform.localScale = i < stopped ? Vector3.one * pulse : new Vector3(1f, 1.12f, 1f);
                    img.color = i < stopped ? (center ? Color.white : new Color(1f, 1f, 1f, 0.55f)) : new Color(1f, 1f, 1f, 0.8f);
                }
                bool reach = stopped == 2 && i == 2 && result[0] == result[1];
                reelBoxes[i].color = reach ? Color.Lerp(new Color32(0xf8, 0xf4, 0xe8, 0xff), SymbolColors[result[0]], 0.4f + 0.4f * Mathf.Sin(Time.time * 14f))
                                           : i < stopped ? new Color32(0xf8, 0xf4, 0xe8, 0xff) : new Color32(0xe8, 0xe4, 0xd8, 0xff);
                Color glow = win ? SymbolColors[flashSymbol] : reach ? SymbolColors[result[0]] : new Color(1f, 0.85f, 0.3f);
                float ga = win ? 0.6f + 0.4f * Mathf.Sin(Time.time * 12f + i) : reach ? 0.35f + 0.3f * Mathf.Sin(Time.time * 14f) : 0f;
                reelGlows[i].color = new Color(glow.r, glow.g, glow.b, Mathf.Max(0f, ga));
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
