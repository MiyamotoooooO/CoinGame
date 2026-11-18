using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N016 ボウリング「ストライク・レーン」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：ballInterval 秒ごとに、盤面の横から炎をまとったボウリングの球が全速力で転がってきて、通り道のメダルを吹き飛ばす（本物の物理の球）。
    ///   球は反対側の壁にぶつかると爆発して、まわりのメダルをはじく。左右から2つ来た時は、まん中でぶつかって大爆発
    ///   球が出る前に、床に転がる道の矢印が光る
    /// ・モニター：ボウリングのレーンと10本のピン。次の球の向き（左から・両方・右から）、カウントダウン、今の投球で落ちた枚数、スコア表
    /// ・ルール：
    ///   球の向きは、球が出る直前の laneDecideSeconds 秒の間に投入した発射口で決まる（左だけ→左から、右だけ→右から、両方→左右から2つ）
    ///   球はメダルが一番多い横の列を転がる
    ///   球が出てから countSeconds 秒の間に穴に落ちたメダルを数える。strikeCount 枚以上で STRIKE（+strikeBonus pt）、
    ///   spareCount 枚以上で SPARE（+spareBonus pt）
    /// ・投入する側をえらんで、球の向きを決める遊び
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
        [Range(0.5f, 3f)] public float ballSize = 1.3f;
        [Tooltip("球の重さ（メダルの何倍か）")]
        [Range(1f, 80f)] public float ballMass = 30f;
        [Tooltip("球の転がる速さ（メダルの直径 / 秒）")]
        [Range(0.5f, 30f)] public float ballSpeed = 11f;
        [Tooltip("通り道のメダルを吹き飛ばす強さ（メダルの直径 / 秒）")]
        [Range(0f, 30f)] public float hitStrength = 7f;
        [Tooltip("壁にぶつかった時・球どうしがぶつかった時の爆発の強さ")]
        [Range(0f, 30f)] public float blastStrength = 6f;
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
        public AudioClip crashSound;
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
        static readonly Color Fire = new Color32(0xff, 0x8a, 0x2a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        float nextBall, countTimer, leftTime = -99f, rightTime = -99f, rowZ;
        int lane, knocked, frame, strikes;
        bool rowChosen;
        readonly List<string> results = new List<string>();

        class Ball
        {
            public GameObject go;
            public Rigidbody body;
            public int dir;
            public float life;
            public readonly HashSet<Medal> hit = new HashSet<Medal>();
        }
        readonly List<Ball> balls = new List<Ball>();
        readonly List<Medal> near = new List<Medal>();

        readonly GameObject[] arrows = new GameObject[2];
        Material arrowMaterial, ballMaterial;
        ParticleSystem pins, flame, sparks, speedLines;

        readonly List<Image> screenPins = new List<Image>();
        readonly Image[] laneBoxes = new Image[3];
        Text countText, knockText, scoreText, statusText;
        AudioClip madeRoll, madeStrike, madeCrash;

        static Material Shiny(Color c, float metal, float smooth, float glow)
        {
            var m = ProtoAssets.MakeMaterial(c, metal, smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            return m;
        }

        protected override void BuildBoard()
        {
            arrowMaterial = Glow(Lane, 1.5f);
            for (int i = 0; i < 2; i++)
            {
                arrows[i] = Cube("Arrow" + i, Board, Vector3.zero, new Vector3(D * 2.6f, D * 0.01f, D * 0.28f), arrowMaterial);
                arrows[i].SetActive(false);
            }
            ballMaterial = Shiny(BallColor, 0.35f, 0.96f, 0.35f);
            pins = Particles("Pins", Board, Vector3.up, D * 3f, D * 0.12f, 0.9f, 1f, 50f);
            flame = Particles("Flame", Board, Vector3.up, D * 0.8f, D * 0.32f, 0.45f, -0.3f, 35f);
            sparks = Particles("Sparks", Board, Vector3.up, D * 4f, D * 0.08f, 0.6f, 0.8f, 60f);
            speedLines = Particles("Trail", Board, Vector3.up, D * 0.2f, D * 0.05f, 0.3f, 0f, 10f);
        }

        protected override void OnBegin()
        {
            nextBall = ballInterval * 0.6f;
            countTimer = 0f;
            frame = 0;
            strikes = 0;
            rowChosen = false;
            results.Clear();
        }

        protected override void OnEnd() => RemoveBalls();

        void RemoveBalls()
        {
            foreach (var b in balls) if (b.go != null) Destroy(b.go);
            balls.Clear();
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

        /// <summary>メダルが一番多い横の列（z）をさがす</summary>
        float FindRow()
        {
            float best = Mathf.Lerp(FrontZ, PusherFrontZ, 0.45f);
            int bestCount = -1;
            for (int i = 0; i < 6; i++)
            {
                float z = Mathf.Lerp(FrontZ, PusherFrontZ, 0.2f + 0.12f * i);
                int count = 0;
                foreach (var m in BoardMedals)
                {
                    if (m == null || m.Consumed) continue;
                    if (Mathf.Abs(m.transform.position.z - z) < D * 0.8f) count++;
                }
                if (count > bestCount) { bestCount = count; best = z; }
            }
            return best;
        }

        protected override void Tick(float dt, bool running)
        {
            for (int i = balls.Count - 1; i >= 0; i--)
            {
                var b = balls[i];
                b.life -= dt;
                if (b.go == null || b.life <= 0f || b.go.transform.position.y < Layout.holeTopY - D)
                {
                    if (b.go != null) Destroy(b.go);
                    balls.RemoveAt(i);
                }
            }
            if (!running) return;
            if (countTimer > 0f)
            {
                countTimer -= dt;
                if (countTimer <= 0f) Judge();
            }
            nextBall -= dt;
            lane = DecideLane();
            if (nextBall < laneDecideSeconds && !rowChosen)
            {
                rowZ = FindRow();
                rowChosen = true;
            }
            if (nextBall <= 0f)
            {
                nextBall = ballInterval;
                if (!rowChosen) rowZ = FindRow();
                rowChosen = false;
                Roll();
            }
        }

        void Roll()
        {
            RemoveBalls();
            if (lane <= 0) SpawnBall(1);    // 左から右へ
            if (lane >= 0) SpawnBall(-1);   // 右から左へ
            knocked = 0;
            countTimer = countSeconds;
            Play(rollSound, ref madeRoll, () => RecordSynth.Noise("BowlingRoll", 1.5f, 0.03f, 0.2f, 1f, 1.2f, 7));
            Shake(0.02f, 0.3f);
            Pop(lane == 0 ? "ダブル・ボール！" : "ゴーッ！", Fire, new Vector2(BodyW * 0.5f, 300f));
        }

        void SpawnBall(int dir)
        {
            float size = D * ballSize;
            var start = new Vector3(-dir * (HalfW - size * 0.7f), size * 0.55f, rowZ);
            GameObject go;
            if (ballPrefab != null) go = Instantiate(ballPrefab, start, Quaternion.identity);
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "BowlingBall";
                go.transform.position = start;
                go.GetComponent<Renderer>().sharedMaterial = ballMaterial;
                // 指の穴
                var holeMat = ProtoAssets.MakeMaterial(new Color(0.03f, 0.03f, 0.05f), 0f, 0.2f);
                Shape(PrimitiveType.Sphere, "FingerHole", go.transform, new Vector3(-0.1f, 0.4f, -0.25f), Vector3.one * 0.13f, holeMat);
                Shape(PrimitiveType.Sphere, "FingerHole", go.transform, new Vector3(0.1f, 0.4f, -0.25f), Vector3.one * 0.13f, holeMat);
                Shape(PrimitiveType.Sphere, "ThumbHole", go.transform, new Vector3(0f, 0.25f, -0.4f), Vector3.one * 0.15f, holeMat);
                var lgo = new GameObject("FireLight");
                lgo.transform.SetParent(go.transform, false);
                var fireLight = lgo.AddComponent<Light>();
                fireLight.type = LightType.Point;
                fireLight.color = Fire;
                fireLight.range = D * 4f;
                fireLight.intensity = 2.5f;
                fireLight.shadows = LightShadows.None;
            }
            go.transform.localScale = Vector3.one * size;
            if (go.GetComponent<Collider>() == null) go.AddComponent<SphereCollider>();
            var body = go.GetComponent<Rigidbody>();
            if (body == null) body = go.AddComponent<Rigidbody>();
            body.mass = game.tuning.medalMass * ballMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.velocity = new Vector3(dir * ballSpeed * D, 0f, -D * 0.3f);
            body.angularVelocity = new Vector3(0f, 0f, -dir * ballSpeed * 2f);
            balls.Add(new Ball { go = go, body = body, dir = dir, life = 6f });
            Emit(flame, start, Fire, 30);
        }

        void FixedUpdate()
        {
            if (!Active || balls.Count == 0) return;
            float size = D * ballSize;
            for (int i = balls.Count - 1; i >= 0; i--)
            {
                var b = balls[i];
                if (b.go == null || b.body == null) continue;
                Vector3 p = b.body.position;
                // 勢いを保つ（メダルにぶつかっても止まらない）
                var v = b.body.velocity;
                if (v.x * b.dir < ballSpeed * D * 0.75f) b.body.velocity = new Vector3(b.dir * ballSpeed * D * 0.85f, v.y, v.z);
                // 通り道のメダルを吹き飛ばす
                foreach (var m in MedalsNear(p, size * 1.05f, near))
                {
                    if (m.Body == null || !b.hit.Add(m)) continue;
                    Vector3 away = m.transform.position - p;
                    away.y = 0f;
                    Vector3 dirV = new Vector3(b.dir * 0.7f, 0.75f, -0.45f) + away.normalized * 0.4f;
                    Push(m, dirV.normalized * D * hitStrength * Random.Range(0.8f, 1.2f));
                    Emit(sparks, m.transform.position, Color.Lerp(Fire, Color.white, Random.value * 0.5f), 4);
                }
                // 反対側の壁で爆発
                if (p.x * b.dir > HalfW - size * 0.75f)
                {
                    Explode(p, 1f);
                    Destroy(b.go);
                    balls.RemoveAt(i);
                    continue;
                }
            }
            // 左右の球どうしがぶつかった
            if (balls.Count == 2 && balls[0].go != null && balls[1].go != null)
            {
                Vector3 a = balls[0].body.position, c = balls[1].body.position;
                if ((a - c).sqrMagnitude < size * size * 1.2f)
                {
                    Vector3 mid = (a + c) * 0.5f;
                    Explode(mid, 1.8f);
                    PopCenter("CRASH!!", Fire);
                    foreach (var b in balls) if (b.go != null) Destroy(b.go);
                    balls.Clear();
                }
            }
        }

        void Explode(Vector3 at, float power)
        {
            Blast(at, D * 2f * power, D * blastStrength * power, 0.9f);
            Emit(flame, at, Fire, Mathf.RoundToInt(50 * power));
            Emit(sparks, at, Color.white, Mathf.RoundToInt(40 * power));
            Emit(pins, at, Pin, Mathf.RoundToInt(20 * power));
            Shake(0.04f * power, 0.4f);
            Play(crashSound, ref madeCrash, () => RecordSynth.Noise("BowlingCrash", 0.7f, 0.4f, 0.005f, 0.3f, 1.1f, 11), 0.9f);
            Speaker(0.7f * power, 8, Fire);
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
                Emit(pins, new Vector3(0f, D * 0.5f, FrontZ + D), Pin, 60);
                Shake(0.05f, 0.5f);
                Speaker(1f, 16, Strike);
            }
            else if (knocked >= spareCount)
            {
                mark = "/";
                AddPoints(spareBonus);
                PopCenter($"SPARE +{spareBonus}", Lane);
                Emit(pins, new Vector3(0f, D * 0.5f, FrontZ + D), Pin, 25);
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

        protected override bool KeepBoardVisible() => (pins != null && pins.particleCount > 0) || (flame != null && flame.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            // 球のうしろの炎と、スピード線
            foreach (var b in balls)
            {
                if (b.go == null) continue;
                Vector3 p = b.go.transform.position;
                float size = D * ballSize;
                Emit(flame, p - new Vector3(b.dir * size * 0.45f, -size * 0.1f, 0f), Color.Lerp(Fire, new Color(1f, 0.85f, 0.3f), Random.value), 3);
                if (Random.value < 0.8f)
                    Emit(speedLines, p + new Vector3(-b.dir * size * 0.8f, Random.Range(-0.3f, 0.5f) * size, Random.Range(-0.6f, 0.6f) * size), Color.white, 1);
            }

            bool show = Active && nextBall < laneDecideSeconds && balls.Count == 0;
            for (int i = 0; i < 2; i++)
            {
                int dir = i == 0 ? 1 : -1;   // 0：左から、1：右から
                bool use = show && (dir > 0 ? lane <= 0 : lane >= 0);
                if (arrows[i].activeSelf != use) arrows[i].SetActive(use);
                if (!use) continue;
                float z = rowChosen ? rowZ : Mathf.Lerp(FrontZ, PusherFrontZ, 0.45f);
                float slide = Mathf.Repeat(Time.time * 1.5f, 1f);
                arrows[i].transform.position = new Vector3(-dir * HalfW * (0.75f - 0.35f * slide), D * 0.01f, z);
            }
            if (show) SetGlow(arrowMaterial, Lane, 1f + 2f * Mathf.Repeat(Time.time * 3f, 1f));
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
                Lbl(alley, $"LaneName{l}", cx, 320f, 160f, 30f, l == 0 ? "左から" : l == 1 ? "両方（2球）" : "右から", 18, Mute);
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
                ? "<color=#E8C88A>球の向きを決める時間！</color>  左に投入→左から、右→右から、両方→左右から2球"
                : $"球が出る前の {laneDecideSeconds:0}秒に投入した側から、球が横に転がってくる";
        }
    }
}
