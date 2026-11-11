using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N016 ボウリング「ストライク・レーン」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：ballInterval 秒ごとに、プッシャーの前からボウリングの球が転がってきて、メダルを押し出す（本物の物理の球）。
    ///   球が出る前に、床に転がる道の矢印が光る
    /// ・モニター：ボウリングのレーンと10本のピン。次の球のレーン（左・中・右）、カウントダウン、今の投球で落ちた枚数、スコア表
    /// ・ルール：
    ///   球のレーンは、球が出る直前の laneDecideSeconds 秒の間に投入した発射口で決まる（左だけ→左、右だけ→右、両方→まん中）
    ///   球が出てから countSeconds 秒の間に穴に落ちたメダルを数える。strikeCount 枚以上で STRIKE（+strikeBonus pt）、
    ///   spareCount 枚以上で SPARE（+spareBonus pt）
    /// ・投入する側をえらんで、メダルの山へ球を向ける遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class BowlingRecord : RecordScriptBase
    {
        public static BowlingRecord Instance { get; private set; }

        [Header("球")]
        [Range(3f, 60f)] public float ballInterval = 10f;
        [Tooltip("球の大きさ（メダルの直径に対する倍率）")]
        [Range(0.5f, 3f)] public float ballSize = 1.2f;
        [Tooltip("球の重さ（メダルの何倍か）")]
        [Range(1f, 50f)] public float ballMass = 12f;
        [Tooltip("球の転がる速さ（メダルの直径 / 秒）")]
        [Range(0.5f, 20f)] public float ballSpeed = 4f;
        [Tooltip("レーンを決める時間（球が出る直前の何秒か）")]
        [Range(0.5f, 10f)] public float laneDecideSeconds = 3f;

        [Header("STRIKE / SPARE")]
        [Range(1f, 10f)] public float countSeconds = 4f;
        [Range(1, 50)] public int strikeCount = 10;
        [Range(0, 500)] public int strikeBonus = 100;
        [Range(1, 50)] public int spareCount = 5;
        [Range(0, 300)] public int spareBonus = 30;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip rollSound;
        public AudioClip strikeSound;
        public GameObject ballPrefab;

        protected override string DefaultKey => "Bowling";
        protected override string ScreenTitle => "BOWLING";
        protected override string ScreenSubtitle => "ストライク・レーン";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<BowlingRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color BallColor = new Color32(0x3a, 0x2c, 0xc8, 0xff);
        static readonly Color Lane = new Color32(0xe8, 0xc8, 0x8a, 0xff);
        static readonly Color Pin = new Color32(0xf8, 0xf8, 0xf8, 0xff);
        static readonly Color Strike = new Color32(0xff, 0x4d, 0x6d, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        float nextBall, countTimer, leftTime = -99f, rightTime = -99f, ballLife;
        int lane, knocked, frame, strikes;
        readonly List<string> results = new List<string>();

        GameObject ball;
        Rigidbody ballBody;
        GameObject arrow;
        Material arrowMaterial;
        ParticleSystem pins;

        readonly List<Image> screenPins = new List<Image>();
        readonly Image[] laneBoxes = new Image[3];
        Text countText, knockText, scoreText, statusText;
        AudioClip madeRoll, madeStrike;

        float LaneX(int l) => l * HalfW * 0.45f;

        protected override void BuildBoard()
        {
            arrowMaterial = Glow(Lane, 1.5f);
            arrow = Cube("Arrow", Board, Vector3.zero, new Vector3(D * 0.25f, D * 0.01f, D * 2.2f), arrowMaterial);
            pins = Particles("Pins", Board, Vector3.up, D * 3f, D * 0.12f, 0.9f, 1f, 50f);
        }

        protected override void OnBegin()
        {
            nextBall = ballInterval * 0.6f;
            countTimer = 0f;
            frame = 0;
            strikes = 0;
            results.Clear();
        }

        protected override void OnEnd() => RemoveBall();

        void RemoveBall()
        {
            if (ball != null) Destroy(ball);
            ball = null;
            ballBody = null;
        }

        protected override void OnInsert(Medal medal)
        {
            if (medal.transform.position.x < 0f) leftTime = Time.time;
            else rightTime = Time.time;
        }

        int DecideLane()
        {
            bool left = Time.time - leftTime <= laneDecideSeconds;
            bool right = Time.time - rightTime <= laneDecideSeconds;
            if (left && !right) return -1;
            if (right && !left) return 1;
            return 0;
        }

        protected override void Tick(float dt, bool running)
        {
            if (ball != null)
            {
                ballLife -= dt;
                if (ballLife <= 0f || ball.transform.position.y < Layout.holeTopY - D) RemoveBall();
            }
            if (!running) return;
            if (countTimer > 0f)
            {
                countTimer -= dt;
                if (countTimer <= 0f) Judge();
            }
            nextBall -= dt;
            lane = DecideLane();
            if (nextBall <= 0f)
            {
                nextBall = ballInterval;
                Roll();
            }
        }

        void Roll()
        {
            RemoveBall();
            float size = D * ballSize;
            var start = new Vector3(LaneX(lane), size * 0.6f, PusherFrontZ - size * 0.6f);
            if (ballPrefab != null) ball = Instantiate(ballPrefab, start, Quaternion.identity);
            else
            {
                ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "BowlingBall";
                ball.transform.position = start;
                ball.GetComponent<Renderer>().sharedMaterial = Glow(BallColor, 0.4f);
            }
            ball.transform.localScale = Vector3.one * size;
            if (ball.GetComponent<Collider>() == null) ball.AddComponent<SphereCollider>();
            ballBody = ball.GetComponent<Rigidbody>();
            if (ballBody == null) ballBody = ball.AddComponent<Rigidbody>();
            ballBody.mass = game.tuning.medalMass * ballMass;
            ballBody.velocity = new Vector3(0f, 0f, -ballSpeed * D);
            ballLife = 8f;
            knocked = 0;
            countTimer = countSeconds;
            Play(rollSound, ref madeRoll, () => RecordSynth.Noise("BowlingRoll", 1.5f, 0.03f, 0.2f, 1f, 1.2f, 7));
        }

        void Judge()
        {
            frame++;
            string mark;
            if (knocked >= strikeCount)
            {
                strikes++;
                mark = "X";
                AddPoints(strikeBonus);
                Play(strikeSound, ref madeStrike, () => RecordSynth.Noise("BowlingStrike", 0.8f, 0.5f, 0.005f, 0.25f, 1f, 9));
                PopCenter($"STRIKE! +{strikeBonus}", Strike);
                Emit(pins, new Vector3(LaneX(lane), D * 0.5f, FrontZ + D), Pin, 40);
                Shake(0.04f, 0.4f);
                Speaker(1f, 16, Strike);
            }
            else if (knocked >= spareCount)
            {
                mark = "/";
                AddPoints(spareBonus);
                PopCenter($"SPARE +{spareBonus}", Lane);
                Emit(pins, new Vector3(LaneX(lane), D * 0.5f, FrontZ + D), Pin, 15);
            }
            else mark = knocked.ToString();
            results.Add(mark);
            if (results.Count > 10) results.RemoveAt(0);
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (countTimer <= 0f) return;
            knocked++;
        }

        protected override bool KeepBoardVisible() => pins != null && pins.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            bool show = Active && nextBall < laneDecideSeconds && ball == null;
            arrow.SetActive(show);
            if (show)
            {
                arrow.transform.position = new Vector3(LaneX(lane), D * 0.01f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f));
                SetGlow(arrowMaterial, Lane, 1f + 2f * Mathf.Repeat(Time.time * 3f, 1f));
            }
        }

        protected override void BuildScreen()
        {
            screenPins.Clear();
            float W = BodyW;
            var alley = Panel(Screen, "Alley", W * 0.5f, 190f, W - 48f, 340f, new Color32(0x16, 0x14, 0x22, 0xff));
            float aw = W - 48f;
            for (int l = 0; l < 3; l++)
            {
                float cx = aw * (0.2f + 0.3f * l);
                laneBoxes[l] = Img(alley, $"Lane{l}", cx, 170f, aw * 0.26f, 320f, new Color(Lane.r, Lane.g, Lane.b, 0.15f));
                Lbl(alley, $"LaneName{l}", cx, 320f, 120f, 30f, l == 0 ? "左" : l == 1 ? "まん中" : "右", 18, Mute);
            }
            int idx = 0;
            for (int row = 0; row < 4; row++)
                for (int k = 0; k <= row; k++)
                {
                    screenPins.Add(Img(alley, $"Pin{idx}", aw * 0.5f + (k - row * 0.5f) * 36f, 50f + row * 32f, 18f, 30f, Pin, MonitorDisplay.ScreenShape.Pill));
                    idx++;
                }
            countText = Lbl(alley, "Count", aw * 0.5f, 240f, aw * 0.5f, 80f, "", 56, Ink, TextAnchor.MiddleCenter, true);
            knockText = Lbl(Screen, "Knock", 24f, 430f, 500f, 40f, "", 24, Ink, TextAnchor.MiddleLeft);
            scoreText = Lbl(Screen, "Score", W - 24f, 430f, 500f, 40f, "", 24, Strike, TextAnchor.MiddleRight, true);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int l = 0; l < 3; l++)
            {
                bool sel = lane == l - 1;
                laneBoxes[l].color = sel ? new Color(Lane.r, Lane.g, Lane.b, 0.45f + 0.2f * Mathf.Sin(Time.time * 6f)) : new Color(Lane.r, Lane.g, Lane.b, 0.12f);
            }
            int down = countTimer > 0f ? Mathf.Min(screenPins.Count, knocked) : 0;
            for (int i = 0; i < screenPins.Count; i++) screenPins[i].color = i < down ? new Color(1f, 1f, 1f, 0.12f) : Pin;
            countText.text = countTimer > 0f ? $"{knocked}" : $"{Mathf.CeilToInt(nextBall)}";
            countText.color = countTimer > 0f ? Strike : Ink;
            knockText.text = countTimer > 0f ? $"この投球で落ちた {knocked}枚（{strikeCount}で STRIKE）" : $"次の球まで {Mathf.CeilToInt(nextBall)}秒";
            scoreText.text = results.Count > 0 ? string.Join(" ", results) + $"   X×{strikes}" : "";
            statusText.text = nextBall < laneDecideSeconds
                ? $"<color=#E8C88A>レーンを決める時間！</color>  投入した発射口の側へ球が転がる（両方ならまん中）"
                : $"球が出る前の {laneDecideSeconds:0}秒に投入した側がレーンになる";
        }
    }
}
