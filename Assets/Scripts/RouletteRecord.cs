using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N023 ルーレット「赤か黒か」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：ルーレットが当たると、盤面の照明（4つのランプ）がぐるぐる回る
    /// ・モニター：ルーレットの盤（8マス、赤と黒）。玉が転がって止まる。賭けた色、回るまでの残り枚数、当たったマスの効果
    /// ・ルール（CSV のとおり）：
    ///   中央の穴に spinEvery 枚落ちるごとにルーレットが回る。止まったマスの効果が起きる：
    ///     メダル +medalPrize（盤面に降らせる）／ゲージ +gaugePrize／timedSeconds 秒間ポイント ×2／timedSeconds 秒間プッシャー ×2／はずれ
    ///   賭け：spinEvery 枚目が落ちた半分で色が決まる（左の半分＝赤、右の半分＝黒）。
    ///   止まったマスの色と賭けた色が同じなら「的中」：効果が2倍（倍率は ×3）になり、+hitBonus pt
    /// ・最後の1枚を落とす側をえらんで、赤か黒かに賭ける遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class RouletteRecord : RecordScriptBase
    {
        public static RouletteRecord Instance { get; private set; }

        public enum Prize { Medal, Gauge, Points, Pusher, Miss }

        [Header("ルーレット")]
        [Range(1, 50)] public int spinEvery = 10;
        [Tooltip("回っている秒数")]
        [Range(1f, 10f)] public float spinSeconds = 3f;

        [Header("効果")]
        [Range(1, 30)] public int medalPrize = 5;
        [Range(0f, 100f)] public float gaugePrize = 30f;
        [Range(1f, 60f)] public float timedSeconds = 10f;
        [Range(0, 300)] public int hitBonus = 20;

        [Header("盤面のランプ")]
        [Range(0f, 10f)] public float lampIntensity = 3f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip spinSound;
        public AudioClip winSound;

        protected override string DefaultKey => "Roulette";
        protected override string ScreenTitle => "ROULETTE";
        protected override string ScreenSubtitle => "赤か黒か";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<RouletteRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        // 8マス：赤と黒が交互
        static readonly Prize[] Pockets = { Prize.Medal, Prize.Gauge, Prize.Points, Prize.Miss, Prize.Pusher, Prize.Medal, Prize.Gauge, Prize.Miss };
        static readonly Color Red = new Color32(0xd8, 0x2a, 0x2a, 0xff);
        static readonly Color Black = new Color32(0x22, 0x22, 0x28, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color Felt = new Color32(0x0e, 0x3a, 0x26, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int count, bet, result = -1;   // bet：-1 赤、1 黒、0 まだ
        float spinTimer, wheelAngle, wheelSpeed, pointsTimer, pusherTimer, flashTimer;
        bool pointsDouble, pusherDouble;
        bool Spinning => spinTimer > 0f;

        readonly List<Light> lamps = new List<Light>();

        RectTransform wheel, ball;
        Text betText, countText, resultText, statusText;
        AudioClip madeSpin, madeWin;

        static bool IsRed(int pocket) => pocket % 2 == 0;
        static string PrizeName(Prize p) => p switch
        {
            Prize.Medal => "メダル",
            Prize.Gauge => "ゲージ",
            Prize.Points => "ポイント×2",
            Prize.Pusher => "プッシャー×2",
            _ => "はずれ",
        };

        protected override void BuildBoard()
        {
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                lamps.Add(PointLight($"Lamp{i}", Board, new Vector3(Mathf.Cos(a) * HalfW * 0.7f, Layout.wallHeight * 0.6f, (FrontZ + BackZ) * 0.5f + Mathf.Sin(a) * D * 2f), i % 2 == 0 ? Red : Gold, Layout.boardWidth * 0.8f));
            }
        }

        protected override void OnBegin()
        {
            count = 0;
            bet = 0;
            result = -1;
            spinTimer = 0f;
            pointsTimer = 0f;
            pusherTimer = 0f;
        }

        protected override void Tick(float dt, bool running)
        {
            flashTimer = Mathf.MoveTowards(flashTimer, 0f, dt * 0.4f);
            if (pointsTimer > 0f) pointsTimer -= dt;
            if (pusherTimer > 0f) pusherTimer -= dt;
            PusherSpeedScale = pusherTimer > 0f ? (pusherDouble ? 3f : 2f) : 1f;

            if (Spinning)
            {
                spinTimer -= dt;
                wheelSpeed = Mathf.Max(0f, wheelSpeed - dt * wheelSpeed / Mathf.Max(0.1f, spinTimer + dt));
                wheelAngle += wheelSpeed * dt;
                if (spinTimer <= 0f) Land();
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            if (pointsTimer > 0f)
            {
                float mult = pointsDouble ? 3f : 2f;
                int extra = AddMultiplied(mult);
                Pop($"×{mult:0} +{CenterPoints + extra}", Gold, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 420f));
            }
            if (Spinning) return;
            count++;
            if (count < spinEvery) return;
            count = 0;
            bet = medal.transform.position.x < 0f ? -1 : 1;
            result = -1;
            spinTimer = spinSeconds;
            wheelSpeed = 900f;
            Play(spinSound, ref madeSpin, () => RecordSynth.Noise("RouletteSpin", 2.5f, 0.6f, 0.05f, 2f, 0.35f, 17));
            PopCenter(bet < 0 ? "赤に賭けた！" : "黒に賭けた！", bet < 0 ? Red : Ink);
        }

        void Land()
        {
            spinTimer = 0f;
            float a = Mathf.Repeat(wheelAngle, 360f);
            result = Mathf.FloorToInt(a / 45f) % Pockets.Length;
            bool hit = (IsRed(result) ? -1 : 1) == bet;
            Prize prize = Pockets[result];
            int k = hit ? 2 : 1;
            switch (prize)
            {
                case Prize.Medal: RainMedals(medalPrize * k); break;
                case Prize.Gauge: AddGauge(gaugePrize * k); break;
                case Prize.Points: pointsTimer = timedSeconds; pointsDouble = hit; break;
                case Prize.Pusher: pusherTimer = timedSeconds; pusherDouble = hit; break;
            }
            if (hit) AddPoints(hitBonus);
            if (prize != Prize.Miss || hit)
            {
                flashTimer = 1f;
                Play(winSound, ref madeWin, () => RecordSynth.Notes("RouletteWin", new[] { RecordSynth.Note(7), RecordSynth.Note(11), RecordSynth.Note(14), RecordSynth.Note(19) }, 0.07f, 0.4f, 0.3f, RecordSynth.Wave.Square));
                Speaker(0.9f, 12, Gold);
            }
            string what = prize switch
            {
                Prize.Medal => $"メダル +{medalPrize * k}",
                Prize.Gauge => $"ゲージ +{gaugePrize * k:0}",
                Prize.Points => $"{timedSeconds:0}秒間 ポイント ×{(hit ? 3 : 2)}",
                Prize.Pusher => $"{timedSeconds:0}秒間 プッシャー ×{(hit ? 3 : 2)}",
                _ => "はずれ",
            };
            PopCenter(hit ? $"的中！ {what}" : what, hit ? Gold : Ink);
        }

        protected override void UpdateBoard(float dt)
        {
            for (int i = 0; i < lamps.Count; i++)
            {
                bool on = flashTimer > 0f ? Mathf.FloorToInt(Time.time * 10f) % lamps.Count == i : true;
                lamps[i].intensity = BoardShown * lampIntensity * (on ? (flashTimer > 0f ? 1f : 0.2f) : 0.05f);
            }
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var table = Panel(Screen, "Table", W * 0.5f, 200f, W - 48f, 360f, Felt);
            float tw = W - 48f;
            float cx = tw * 0.3f, cy = 180f, r = 150f;
            var w = Grp(table, "Wheel", cx, cy, r * 2f, r * 2f);
            Img(w, "Rim", r, r, r * 2f, r * 2f, new Color32(0x6a, 0x40, 0x20, 0xff), MonitorDisplay.ScreenShape.Circle);
            for (int i = 0; i < Pockets.Length; i++)
            {
                float a = (i * 45f + 22.5f) * Mathf.Deg2Rad;
                var pocket = Img(w, $"Pocket{i}", r + Mathf.Sin(a) * r * 0.62f, r - Mathf.Cos(a) * r * 0.62f, 80f, 80f, IsRed(i) ? Red : Black, MonitorDisplay.ScreenShape.Circle);
                Lbl(w, $"PocketText{i}", r + Mathf.Sin(a) * r * 0.62f, r - Mathf.Cos(a) * r * 0.62f, 80f, 40f, PrizeName(Pockets[i]), 12, Ink);
            }
            Img(w, "Hub", r, r, 60f, 60f, Gold, MonitorDisplay.ScreenShape.Circle);
            wheel = w;
            ball = Img(table, "Ball", cx, cy - r * 0.92f, 20f, 20f, Ink, MonitorDisplay.ScreenShape.Circle).rectTransform;
            Img(table, "Pointer", cx, cy - r - 8f, 16f, 24f, Gold);
            betText = Lbl(table, "Bet", tw * 0.72f, 80f, tw * 0.5f, 50f, "", 30, Ink);
            resultText = Lbl(table, "Result", tw * 0.72f, 180f, tw * 0.5f, 80f, "", 30, Gold);
            countText = Lbl(table, "Count", tw * 0.72f, 290f, tw * 0.5f, 40f, "", 22, Ink);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
            Lbl(Screen, "Rule", 24f, 440f, W - 48f, 40f, $"{spinEvery}枚目が落ちた半分で賭ける色が決まる（左＝赤、右＝黒）。的中で効果が2倍・+{hitBonus}pt", 18, Mute, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            // 盤は止まっていて、玉が回る（玉の角度 = wheelAngle）
            float tw = BodyW - 48f;
            float cx = tw * 0.3f, cy = 180f, r = 150f;
            float a = wheelAngle * Mathf.Deg2Rad;
            float rr = Spinning ? r * 0.92f : r * 0.62f;
            Place(ball, cx + Mathf.Sin(a) * rr, cy - Mathf.Cos(a) * rr);
            betText.text = bet == 0 ? "まだ賭けていない" : bet < 0 ? "賭け：赤" : "賭け：黒";
            betText.color = bet < 0 ? new Color32(0xff, 0x6a, 0x6a, 0xff) : Ink;
            resultText.text = Spinning ? "回っています…" : result >= 0 ? $"{(IsRed(result) ? "赤" : "黒")}・{PrizeName(Pockets[result])}" : "";
            countText.text = Spinning ? "" : $"次に回るまで あと {spinEvery - count}枚";
            string timed = pointsTimer > 0f ? $"ポイント ×{(pointsDouble ? 3 : 2)} 残り {Mathf.CeilToInt(pointsTimer)}秒  " : "";
            timed += pusherTimer > 0f ? $"プッシャー ×{(pusherDouble ? 3 : 2)} 残り {Mathf.CeilToInt(pusherTimer)}秒" : "";
            statusText.text = timed != "" ? timed : count == spinEvery - 1 ? "<color=#FFD23F>次の1枚で回る！</color>  落とす側で赤か黒かが決まる" : "中央の穴に落としてルーレットを回そう";
        }
    }
}
