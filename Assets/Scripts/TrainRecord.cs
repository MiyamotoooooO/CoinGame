using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N010 汽車「ミッドナイト・エクスプレス」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：手前のふちの近くに線路が敷かれ、trainInterval 秒ごとに小さな汽車が汽笛を鳴らして横切る。
    ///   汽車は本当に当たり判定があり、通り道のメダルを押していく。煙を上げる。走る向きは毎回入れかわる
    /// ・モニター：駅の時刻表。次の汽車の向き（→ / ←）とカウントダウン、線路の上の汽車の位置、乗車した枚数
    /// ・ルール：
    ///   汽車が走っている間（と、通り過ぎてから boardWindow 秒）に中央の穴に落ちると「乗車」：1枚 +boardBonus pt
    ///   1回の汽車で乗車が fullCars 枚以上なら「満員！」で +fullBonus pt
    /// ・汽車が来る直前にメダルを投入して、汽車に押してもらう遊び
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
        [Tooltip("盤面を横切る秒数")]
        [Range(1f, 10f)] public float crossSeconds = 3f;
        [Tooltip("線路の位置（手前のふちからの距離。メダルの直径に対する倍率）")]
        [Range(0.3f, 4f)] public float trackFromFront = 0.9f;

        [Header("乗車")]
        [Range(0f, 5f)] public float boardWindow = 1.5f;
        [Range(0, 100)] public int boardBonus = 15;
        [Range(1, 30)] public int fullCars = 5;
        [Range(0, 300)] public int fullBonus = 50;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip whistleSound;
        public AudioClip boardSound;
        [Tooltip("汽車の Prefab（空なら仮の汽車）。当たり判定はこのスクリプトが付ける")]
        public GameObject trainPrefab;

        protected override string DefaultKey => "Train";
        protected override string ScreenTitle => "TRAIN";
        protected override string ScreenSubtitle => "ミッドナイト・エクスプレス";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<TrainRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Engine = new Color32(0x2a, 0x2a, 0x34, 0xff);
        static readonly Color Brass = new Color32(0xe0, 0xb0, 0x40, 0xff);
        static readonly Color Lamp = new Color32(0xff, 0xe6, 0x8a, 0xff);
        static readonly Color Smoke = new Color32(0xd8, 0xd8, 0xe0, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Board1 = new Color32(0x14, 0x1e, 0x18, 0xff);

        float nextTrain, runTimer, windowTimer;
        int direction = 1, boarded, boardedTotal;
        bool Running2 => runTimer > 0f;

        Transform train;
        Rigidbody trainBody;
        GameObject rails;
        ParticleSystem smoke;
        Light headLamp;
        float trainLength;

        RectTransform trackIcon;
        Text arrowText, countText, boardText, statusText;
        float trackLeft, trackW;
        AudioClip madeWhistle, madeBoard;

        float TrackZ => FrontZ + D * trackFromFront;

        protected override void BuildBoard()
        {
            var railMat = Glow(new Color(0.5f, 0.45f, 0.4f), 0.2f);
            rails = new GameObject("Rails");
            rails.transform.SetParent(Board, false);
            Cube("RailA", rails.transform, new Vector3(0f, D * 0.02f, TrackZ - D * 0.25f), new Vector3(Layout.boardWidth, D * 0.03f, D * 0.04f), railMat);
            Cube("RailB", rails.transform, new Vector3(0f, D * 0.02f, TrackZ + D * 0.25f), new Vector3(Layout.boardWidth, D * 0.03f, D * 0.04f), railMat);

            train = new GameObject("Train").transform;
            train.SetParent(Board, false);
            trainBody = train.gameObject.AddComponent<Rigidbody>();
            trainBody.isKinematic = true;
            trainBody.interpolation = RigidbodyInterpolation.Interpolate;
            trainLength = D * 3.2f;
            if (trainPrefab != null)
            {
                Instantiate(trainPrefab, train).transform.localPosition = Vector3.zero;
            }
            else
            {
                var body = Glow(Engine, 0.1f);
                var brass = Glow(Brass, 0.6f);
                Cube("Boiler", train, new Vector3(0f, D * 0.35f, 0f), new Vector3(D * 1.2f, D * 0.45f, D * 0.6f), body);
                Cube("Cab", train, new Vector3(-D * 0.55f, D * 0.55f, 0f), new Vector3(D * 0.5f, D * 0.6f, D * 0.62f), body);
                Shape(PrimitiveType.Cylinder, "Chimney", train, new Vector3(D * 0.35f, D * 0.75f, 0f), new Vector3(D * 0.2f, D * 0.2f, D * 0.2f), brass);
                Cube("Car1", train, new Vector3(-D * 1.35f, D * 0.3f, 0f), new Vector3(D * 0.9f, D * 0.4f, D * 0.55f), brass);
            }
            // 当たり判定（低い箱。メダルを押す）
            var col = train.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(-D * 0.4f, D * 0.25f, 0f);
            col.size = new Vector3(trainLength * 0.8f, D * 0.5f, D * 0.55f);
            headLamp = PointLight("HeadLamp", train, new Vector3(D * 0.7f, D * 0.4f, 0f), Lamp, D * 3f);
            smoke = Particles("Smoke", Board, Vector3.up, D * 1.2f, D * 0.35f, 1.6f, -0.05f, 15f);
            train.gameObject.SetActive(false);
        }

        protected override void OnBegin()
        {
            nextTrain = 2f;
            runTimer = 0f;
            windowTimer = 0f;
            boarded = 0;
            boardedTotal = 0;
        }

        protected override void OnEnd()
        {
            runTimer = 0f;
            if (train != null) train.gameObject.SetActive(false);
        }

        protected override void Tick(float dt, bool running)
        {
            if (windowTimer > 0f)
            {
                windowTimer -= dt;
                if (windowTimer <= 0f) FinishRun();
            }
            if (!running) return;
            if (Running2)
            {
                runTimer -= dt;
                if (runTimer <= 0f)
                {
                    runTimer = 0f;
                    train.gameObject.SetActive(false);
                    windowTimer = boardWindow;
                }
                return;
            }
            if (windowTimer > 0f) return;
            nextTrain -= dt;
            if (nextTrain <= 0f)
            {
                nextTrain = trainInterval;
                runTimer = crossSeconds;
                boarded = 0;
                direction = -direction;
                train.gameObject.SetActive(true);
                train.rotation = Quaternion.Euler(0f, direction > 0 ? 0f : 180f, 0f);
                train.position = new Vector3(-direction * (HalfW + trainLength), 0f, TrackZ);
                trainBody.position = train.position;
                trainBody.rotation = train.rotation;
                Play(whistleSound, ref madeWhistle, () => RecordSynth.Chord("TrainWhistle", new[] { 587f, 740f, 880f }, 1f, 0.3f, RecordSynth.Wave.Triangle));
            }
        }

        void FixedUpdate()
        {
            if (!Active || !Running2 || trainBody == null || !train.gameObject.activeSelf) return;
            float t = 1f - runTimer / Mathf.Max(0.1f, crossSeconds);
            float x = Mathf.Lerp(-direction * (HalfW + trainLength), direction * (HalfW + trainLength), t);
            trainBody.MovePosition(new Vector3(x, 0f, TrackZ));
        }

        void FinishRun()
        {
            windowTimer = 0f;
            if (boarded >= fullCars)
            {
                AddPoints(fullBonus);
                PopCenter($"満員！ +{fullBonus}", Brass);
                Speaker(0.8f, 8, Brass);
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center || (!Running2 && windowTimer <= 0f)) return;
            boarded++;
            boardedTotal++;
            AddPoints(boardBonus);
            Play(boardSound, ref madeBoard, () => RecordSynth.Notes("TrainBoard", new[] { 1046f, 1318f }, 0.06f, 0.15f, 0.25f, RecordSynth.Wave.Bell), 0.7f);
            Pop($"乗車 +{boardBonus}", Lamp, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 300f));
        }

        protected override bool KeepBoardVisible() => smoke != null && smoke.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            rails.SetActive(BoardShown > 0.01f);
            rails.transform.localScale = new Vector3(BoardShown, 1f, 1f);
            if (train.gameObject.activeSelf)
            {
                if (Random.value < 0.5f) Emit(smoke, train.position + train.right * D * 0.35f + Vector3.up * D * 0.9f, Smoke, 1);
                headLamp.intensity = 2f;
            }
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var board = Panel(Screen, "Timetable", W * 0.5f, 170f, W - 48f, 280f, Board1);
            float bw = W - 48f;
            Lbl(board, "Head", 24f, 34f, 400f, 40f, "MIDNIGHT EXPRESS", 26, Brass, TextAnchor.MiddleLeft, true);
            Lbl(board, "Sub", bw - 24f, 34f, 300f, 40f, "ネオン通り → 港", 20, Mute, TextAnchor.MiddleRight);
            arrowText = Lbl(board, "Arrow", 120f, 140f, 200f, 120f, "", 90, Lamp, TextAnchor.MiddleCenter);
            countText = Lbl(board, "Count", bw * 0.6f, 140f, bw * 0.6f, 120f, "", 64, Ink, TextAnchor.MiddleCenter);
            boardText = Lbl(board, "Boarded", bw - 24f, 240f, 500f, 36f, "", 22, Ink, TextAnchor.MiddleRight);

            trackLeft = 60f;
            trackW = W - 120f;
            Img(Screen, "Track", trackLeft + trackW * 0.5f, 380f, trackW, 6f, Mute);
            for (int i = 0; i <= 20; i++) Img(Screen, $"Tie{i}", trackLeft + trackW * i / 20f, 380f, 6f, 24f, new Color(1f, 1f, 1f, 0.2f));
            var icon = Grp(Screen, "TrainIcon", 0f, 362f, 120f, 50f);
            Img(icon, "Body", 70f, 30f, 70f, 34f, Brass, MonitorDisplay.ScreenShape.Pill);
            Img(icon, "Cab", 30f, 24f, 30f, 44f, Brass);
            Img(icon, "Lamp", 104f, 28f, 12f, 12f, Lamp, MonitorDisplay.ScreenShape.Circle);
            trackIcon = icon;
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            int nextDir = Running2 || windowTimer > 0f ? direction : -direction;
            arrowText.text = nextDir > 0 ? "→" : "←";
            countText.text = Running2 ? "通過中" : windowTimer > 0f ? "乗車中" : $"{Mathf.CeilToInt(nextTrain)}";
            countText.color = Running2 ? Lamp : Ink;
            boardText.text = $"この汽車 {boarded}枚 ／ 合計 {boardedTotal}枚";
            trackIcon.gameObject.SetActive(Running2);
            if (Running2)
            {
                float x01 = Mathf.InverseLerp(-HalfW, HalfW, train.position.x);
                Place(trackIcon, trackLeft + trackW * x01, 362f);
                trackIcon.localScale = new Vector3(direction, 1f, 1f);
            }
            statusText.text = Running2 || windowTimer > 0f
                ? $"<color=#FFE68A>乗車中！</color>  中央の穴に落とすと +{boardBonus}pt（{fullCars}枚で満員 +{fullBonus}pt）"
                : "汽車が来る前にメダルを投入して、押してもらおう";
        }
    }
}
