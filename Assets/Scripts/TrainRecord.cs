using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N010 汽車「ミッドナイト・エクスプレス」：移動・回収型。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・ふだん：
    ///   プッシャーの前の床のまわりに、四角い線路が敷かれる（手前・右・奥・左）
    ///   trainInterval 秒ごとに、小さな汽車が汽笛を鳴らして線路を1周する（lapSeconds 秒。回る向きは毎回入れかわる）
    ///   汽車は本当に当たり判定があり、通り道のメダルを押していく。煙を上げ、前を照らす。踏切の光が点滅する
    ///   汽車が走っている間（と、走り終わってから boardWindow 秒）に中央の穴に落ちると「乗車」：1枚 +boardBonus pt
    ///   1周で乗車が fullCars 枚以上なら「満員！」で +fullBonus pt
    ///   走っている間（と、そのあと）に穴に落ちたメダルは「汽車が押し出した分」として数える
    /// ・大技「メダルの機関車」：汽車が押し出した分が locoAfter 枚たまると発動
    ///   上からメダルが飛んできて、大きな機関車の形に組み上がり、汽笛を鳴らして手前を横切る（前のメダルを押していく）。
    ///   最後に崩れて、locoMedals 枚のメダルになって散らばる
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class TrainRecord : RecordScriptBase
    {
        public static TrainRecord Instance { get; private set; }

        [Header("汽車")]
        [Range(3f, 40f)] public float trainInterval = 10f;
        [Tooltip("線路を1周する秒数")]
        [Range(2f, 20f)] public float lapSeconds = 6f;
        [Tooltip("手前の線路の位置（手前のふちからの距離。メダルの直径に対する倍率）")]
        [Range(0.3f, 4f)] public float trackFromFront = 0.9f;

        [Header("乗車")]
        [Range(0f, 5f)] public float boardWindow = 1.5f;
        [Range(0, 100)] public int boardBonus = 15;
        [Range(1, 30)] public int fullCars = 5;
        [Range(0, 300)] public int fullBonus = 50;

        [Header("大技：メダルの機関車")]
        [Tooltip("汽車が押し出した分が何枚たまると発動するか")]
        [Range(1, 60)] public int locoAfter = 15;
        [Tooltip("機関車になるメダルの枚数（形の点の数まで）")]
        [Range(1, 40)] public int locoMedals = 25;
        [Range(0.5f, 5f)] public float assembleSeconds = 1.6f;
        [Range(0.5f, 8f)] public float crossSeconds = 3f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip whistleSound;
        public AudioClip boardSound;
        public AudioClip collapseSound;
        [Tooltip("汽車の Prefab（空なら仮の汽車）。当たり判定はこのスクリプトが付ける")]
        public GameObject trainPrefab;

        protected override string DefaultKey => "Train";
        protected override string ScreenTitle => "TRAIN";
        protected override string ScreenSubtitle => "ミッドナイト・エクスプレス";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<TrainRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        const string CoalKind = "TrainCoal";
        static readonly Color Engine = new Color32(0x2a, 0x2a, 0x34, 0xff);
        static readonly Color Brass = new Color32(0xe0, 0xb0, 0x40, 0xff);
        static readonly Color Lamp = new Color32(0xff, 0xe6, 0x8a, 0xff);
        static readonly Color SmokeColor = new Color32(0xd8, 0xd8, 0xe0, 0xff);
        static readonly Color CrossRed = new Color32(0xff, 0x30, 0x30, 0xff);

        float nextTrain, runT = -1f, windowTimer, locoT = -1f;
        int direction = 1, boarded;
        bool Running2 => runT >= 0f;
        bool Loco => locoT >= 0f;

        Transform train;
        Rigidbody trainBody;
        GameObject rails;
        ParticleSystem smoke;
        Light headLamp;
        readonly Material[] crossLamps = new Material[2];
        float floorY;
        Vector3[] corners;
        float[] segLen;
        float pathLen;
        AudioClip madeWhistle, madeBoard, madeCollapse;

        float TrackZ => FrontZ + D * trackFromFront;
        float BackTrackZ => PusherFrontZ - D * 0.6f;
        float SideX => HalfW - D * 0.7f;

        protected override void BuildBoard()
        {
            floorY = FloorY(0f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f));
            corners = new[]
            {
                new Vector3(-SideX, floorY, TrackZ), new Vector3(SideX, floorY, TrackZ),
                new Vector3(SideX, floorY, BackTrackZ), new Vector3(-SideX, floorY, BackTrackZ),
            };
            segLen = new float[4];
            pathLen = 0f;
            for (int i = 0; i < 4; i++) { segLen[i] = Vector3.Distance(corners[i], corners[(i + 1) % 4]); pathLen += segLen[i]; }

            // 四角い線路
            var railMat = Glow(new Color(0.5f, 0.45f, 0.4f), 0.2f);
            var tieMat = Glow(new Color(0.3f, 0.2f, 0.12f), 0f);
            rails = new GameObject("Rails");
            rails.transform.SetParent(Board, false);
            for (int i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                var mid = (a + b) * 0.5f + Vector3.up * D * 0.02f;
                var dir = (b - a).normalized;
                var side = Vector3.Cross(Vector3.up, dir) * D * 0.25f;
                var rot = Quaternion.LookRotation(dir);
                foreach (var off in new[] { side, -side })
                {
                    var r = Cube($"Rail{i}", rails.transform, mid + off, new Vector3(D * 0.04f, D * 0.03f, segLen[i] + D * 0.5f), railMat);
                    r.transform.rotation = rot;
                }
                int ties = Mathf.Max(2, Mathf.RoundToInt(segLen[i] / (D * 0.6f)));
                for (int k = 0; k < ties; k++)
                {
                    var tp = Vector3.Lerp(a, b, (k + 0.5f) / ties) + Vector3.up * D * 0.01f;
                    var tie = Cube($"Tie{i}_{k}", rails.transform, tp, new Vector3(D * 0.7f, D * 0.02f, D * 0.12f), tieMat);
                    tie.transform.rotation = rot;
                }
            }
            // 踏切の光（手前の左右）
            for (int i = 0; i < 2; i++)
            {
                crossLamps[i] = Glow(CrossRed, 0.2f);
                float x = (i == 0 ? -1f : 1f) * (SideX + D * 0.55f);
                Cube($"CrossPole{i}", rails.transform, new Vector3(x, floorY + D * 0.6f, TrackZ - D * 0.5f), new Vector3(D * 0.08f, D * 1.2f, D * 0.08f), Glow(Color.white, 0.2f));
                Shape(PrimitiveType.Sphere, $"CrossLamp{i}", rails.transform, new Vector3(x, floorY + D * 1.25f, TrackZ - D * 0.5f), Vector3.one * D * 0.25f, crossLamps[i]);
            }

            // 小さな汽車
            train = new GameObject("Train").transform;
            train.SetParent(Board, false);
            trainBody = train.gameObject.AddComponent<Rigidbody>();
            trainBody.isKinematic = true;
            trainBody.interpolation = RigidbodyInterpolation.Interpolate;
            if (trainPrefab != null)
            {
                Instantiate(trainPrefab, train).transform.localPosition = Vector3.zero;
            }
            else
            {
                // 前が +Z（LookRotation で進む向きに向ける）
                var body = Glow(Engine, 0.1f);
                var brass = Glow(Brass, 0.6f);
                Cube("Boiler", train, new Vector3(0f, D * 0.35f, D * 0.1f), new Vector3(D * 0.6f, D * 0.45f, D * 1.2f), body);
                Cube("Cab", train, new Vector3(0f, D * 0.55f, -D * 0.55f), new Vector3(D * 0.62f, D * 0.6f, D * 0.5f), body);
                Shape(PrimitiveType.Cylinder, "Chimney", train, new Vector3(0f, D * 0.75f, D * 0.45f), new Vector3(D * 0.2f, D * 0.2f, D * 0.2f), brass);
                Cube("Car1", train, new Vector3(0f, D * 0.3f, -D * 1.35f), new Vector3(D * 0.55f, D * 0.4f, D * 0.9f), brass);
            }
            var col = train.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, D * 0.25f, -D * 0.4f);
            col.size = new Vector3(D * 0.55f, D * 0.5f, D * 2.6f);
            headLamp = PointLight("HeadLamp", train, new Vector3(0f, D * 0.4f, D * 0.8f), Lamp, D * 3f);
            smoke = Particles("Smoke", Board, Vector3.up, D * 1.2f, D * 0.35f, 1.6f, -0.05f, 15f);
            train.gameObject.SetActive(false);

            MedalMarks.RegisterEffect(CoalKind, (m, type) => { });   // 機関車のメダル（見た目だけ）
        }

        protected override void OnBegin()
        {
            nextTrain = 2f;
            runT = -1f;
            windowTimer = 0f;
            boarded = 0;
            locoT = -1f;
            TakePushed(0);
        }

        protected override void OnEnd()
        {
            runT = -1f;
            if (train != null) train.gameObject.SetActive(false);
        }

        /// <summary>線路の上の場所（u は 0〜1 で1周）</summary>
        Vector3 PathPoint(float u, out Vector3 forward)
        {
            float d = Mathf.Repeat(u, 1f) * pathLen;
            for (int i = 0; i < 4; i++)
            {
                if (d <= segLen[i] || i == 3)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % 4];
                    forward = (b - a).normalized;
                    return Vector3.Lerp(a, b, Mathf.Clamp01(d / Mathf.Max(0.0001f, segLen[i])));
                }
                d -= segLen[i];
            }
            forward = Vector3.right;
            return corners[0];
        }

        protected override void Tick(float dt, bool running)
        {
            if (Loco)
            {
                locoT += dt / (assembleSeconds + 0.3f + crossSeconds);
                if (locoT > 1f) locoT = -1f;
            }
            if (windowTimer > 0f)
            {
                windowTimer -= dt;
                if (windowTimer <= 0f) FinishRun();
            }
            if (!running) return;
            if (Running2)
            {
                runT += dt / lapSeconds;
                WatchPushed(boardWindow + 1f);
                if (runT >= 1f)
                {
                    runT = -1f;
                    train.gameObject.SetActive(false);
                    windowTimer = boardWindow;
                }
                return;
            }
            if (windowTimer > 0f || Loco) return;
            nextTrain -= dt;
            if (nextTrain <= 0f)
            {
                nextTrain = trainInterval;
                runT = 0f;
                boarded = 0;
                direction = -direction;
                var p = PathPoint(0f, out var f);
                if (direction < 0) f = -((corners[0] - corners[3]).normalized);
                train.gameObject.SetActive(true);
                train.SetPositionAndRotation(p, Quaternion.LookRotation(f));
                trainBody.position = train.position;
                trainBody.rotation = train.rotation;
                Play(whistleSound, ref madeWhistle, () => RecordSynth.Chord("TrainWhistle", new[] { 587f, 740f, 880f }, 1f, 0.3f, RecordSynth.Wave.Triangle));
            }
        }

        void FixedUpdate()
        {
            if (!Active || !Running2 || trainBody == null || !train.gameObject.activeSelf) return;
            float u = direction > 0 ? runT : 1f - runT;
            var p = PathPoint(u, out var f);
            if (direction < 0) f = -f;
            trainBody.MovePosition(p);
            var target = Quaternion.LookRotation(f);
            trainBody.MoveRotation(Quaternion.RotateTowards(trainBody.rotation, target, 720f * Time.fixedDeltaTime));
        }

        void FinishRun()
        {
            windowTimer = 0f;
            if (boarded >= fullCars)
            {
                AddPoints(fullBonus);
                BoardPop($"満員！ +{fullBonus}", Brass, new Vector3(0f, D * 2.5f, TrackZ + D), D * 1f);
                Speaker(0.8f, 8, Brass);
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            NoteDropForPush();
            if (!Loco && PushedCount >= locoAfter) StartLoco();
            if (type != DropZoneType.Center || (!Running2 && windowTimer <= 0f)) return;
            boarded++;
            AddPoints(boardBonus);
            Play(boardSound, ref madeBoard, () => RecordSynth.Notes("TrainBoard", new[] { 1046f, 1318f }, 0.06f, 0.15f, 0.25f, RecordSynth.Wave.Bell), 0.7f);
            BoardPop($"乗車 +{CenterPoints + boardBonus}", Lamp, new Vector3(medal.transform.position.x, D * 1.2f, FrontZ + D * 0.5f), D * 0.6f);
        }

        // ================================================================
        // 大技：メダルの機関車
        // ================================================================

        /// <summary>機関車の形（横から見た形。x が前、y が上。単位はメダルの直径）</summary>
        static readonly Vector2[] LocoShape =
        {
            // ボイラー（2段）
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(2f, 1f), new Vector2(3f, 1f), new Vector2(4f, 1f),
            new Vector2(0f, 2f), new Vector2(1f, 2f), new Vector2(2f, 2f), new Vector2(3f, 2f), new Vector2(4f, 2f),
            // 運転台
            new Vector2(-1f, 1f), new Vector2(-1f, 2f), new Vector2(-1f, 3f), new Vector2(-2f, 1f), new Vector2(-2f, 2f), new Vector2(-2f, 3f),
            // 煙突・ドーム
            new Vector2(3.5f, 3f), new Vector2(3.5f, 4f), new Vector2(1.5f, 3f),
            // 車輪
            new Vector2(-1.5f, 0f), new Vector2(0.5f, 0f), new Vector2(2f, 0f), new Vector2(3.5f, 0f),
            // 前の排障器と前照灯
            new Vector2(5f, 0.5f), new Vector2(5f, 1.8f),
        };

        Vector3 locoFrom, locoTo;

        void StartLoco()
        {
            TakePushed(0);
            locoT = 0f;
            runT = -1f;
            train.gameObject.SetActive(false);
            float s = D * 0.95f;
            int n = Mathf.Min(LocoShape.Length, Budget(locoMedals));
            var points = new List<Vector3>();
            for (int i = 0; i < n; i++) points.Add(new Vector3(LocoShape[i].x * s, LocoShape[i].y * s + D * 0.5f, 0f));
            float z = Mathf.Lerp(FrontZ, PusherFrontZ, 0.45f);
            locoFrom = new Vector3(-HalfW * 0.55f, floorY, z);
            locoTo = new Vector3(HalfW * 0.25f, floorY, z);
            BoardPop("メダルの機関車！", Brass, new Vector3(0f, D * 5f, z), D * 1.2f);
            SculptureRoutine(points, k => new Pose(Vector3.Lerp(locoFrom, locoTo, Ease.OutCubic(k)), Quaternion.identity),
                assembleSeconds, crossSeconds,
                m => MedalMarks.Set(m, CoalKind, Brass, 1f),
                () =>
                {
                    Play(collapseSound, ref madeCollapse, () => RecordSynth.Noise("TrainCollapse", 0.8f, 0.25f, 0.01f, 0.5f, 0.9f, 8));
                    Shake(0.05f, 0.4f);
                    Speaker(1f, 12, Brass);
                });
            Invoke(nameof(LocoWhistle), assembleSeconds + 0.3f);
        }

        void LocoWhistle()
        {
            Play(whistleSound, ref madeWhistle, () => RecordSynth.Chord("TrainWhistle", new[] { 587f, 740f, 880f }, 1f, 0.3f, RecordSynth.Wave.Triangle), 1.3f);
            Speaker(0.8f, 6, Brass);
        }

        protected override bool KeepBoardVisible() => Loco || (smoke != null && smoke.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            rails.SetActive(BoardShown > 0.01f);
            rails.transform.localScale = new Vector3(1f, Mathf.Max(0.001f, BoardShown), 1f);

            bool alarm = Active && (Running2 || (nextTrain < 2f && !Loco));
            bool blink = Mathf.Repeat(t * 2.5f, 1f) < 0.5f;
            for (int i = 0; i < 2; i++)
                SetGlow(crossLamps[i], CrossRed, alarm ? ((i == 0) == blink ? 3f : 0.2f) : 0.2f);

            if (train.gameObject.activeSelf)
            {
                if (Random.value < 0.4f) Emit(smoke, train.TransformPoint(new Vector3(0f, D * 0.95f, D * 0.45f)), SmokeColor, 1);
                if (headLamp != null) headLamp.intensity = 2f * BoardShown;
            }

            // 機関車の煙：煙突のあたり（形の点 17）から
            if (Loco)
            {
                float move = Mathf.Clamp01((locoT * (assembleSeconds + 0.3f + crossSeconds) - assembleSeconds - 0.3f) / crossSeconds);
                var at = Vector3.Lerp(locoFrom, locoTo, Ease.OutCubic(move)) + new Vector3(3.5f * D * 0.95f, 4.6f * D * 0.95f, 0f);
                if (move > 0f && Random.value < 0.7f) Emit(smoke, at, SmokeColor, 2);
            }
        }
    }
}
