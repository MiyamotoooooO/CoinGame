using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N016 ボウリング「ストライク・レーン」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・流れ（1回分）：
    ///   1. 準備：プッシャーが止まる。中央の穴の手前の床から、1段4枚 × 20段のメダルのタワーがニョキニョキ生えてくる
    ///      （メダルは平らに重ねる。1段4枚を 2×2 にならべて20段。柱の数は columns、段の数は towerLevels で変えられる）。
    ///      同時に、モニターの上からボウリング用のモニターが下りてきて、モニターのまん中あたりで止まる（両方同じ時間で定位置へ）
    ///   2. ボウリング用のモニターに「READY?」→「GO!!」
    ///   3. GO と同時に、モニターの下あたりから大きなボウリングの球が飛び出し、勢いよく転がってタワーに突っこむ
    ///      （球にふれたメダルは必ず吹き飛ぶ）
    ///   4. 【効果】タワーが倒れて、メダルが大量に中央の穴へ落ちる
    ///   5. ボウリング用のモニターに「STRIKE!!」。その1.5秒後に、モニターは上へ帰っていく。プッシャーも動き出す
    ///   6. クールタイム（cooldownSeconds 秒）：待ってから、また 1 にもどる（曲が流れている間くりかえす）
    /// ・タワーのメダルは本物のメダル（新しく出す）。倒れるまでは物理を止めて、きれいに積んだまま立たせておく
    /// ・ボウリング用のモニターは、いつものモニターより手前に描く（設定の frontEffectsLayer）
    /// ・いつものモニターの画面は変えない（UseMonitor = false）
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class BowlingRecord : RecordScriptBase
    {
        public static BowlingRecord Instance { get; private set; }

        [Header("流れ（秒）")]
        [Tooltip("曲が始まってから、最初のタワーが出るまで")]
        [Range(0f, 10f)] public float firstDelay = 1f;
        [Tooltip("タワーが生えてくる時間と、ボウリング用モニターが下りてくる時間（同じ）")]
        [Range(0.3f, 5f)] public float riseSeconds = 1.6f;
        [Tooltip("「READY?」を出しておく時間")]
        [Range(0.2f, 5f)] public float readySeconds = 1.1f;
        [Tooltip("「STRIKE!!」を出してから、モニターが上へ帰るまで")]
        [Range(0f, 10f)] public float strikeHold = 1.5f;
        [Tooltip("クールタイム：モニターが帰ってから、次のタワーが出るまで（仮で30秒）")]
        [Range(0f, 120f)] public float cooldownSeconds = 30f;
        [Tooltip("ON：曲が流れている間、クールタイムごとにくりかえす。OFF：1曲で1回だけ")]
        public bool repeat = true;

        [Header("タワー")]
        [Range(1, 60)] public int towerLevels = 20;
        [Tooltip("中央の穴（手前のふち）から、タワーのまん中までの距離（メダルの直径に対する倍率）")]
        [Range(0.6f, 5f)] public float towerDistance = 1.6f;
        [Tooltip("メダルを平らに重ねる柱の数（1段4枚を、この本数に分けて積む）。1 = 1本の柱（いちばん高い）、2 = 2本の柱、4 = 2×2（いちばん低い）")]
        [Range(1, 4)] public int columns = 4;
        [Tooltip("倒れる時に、下の段から順にくずれる間隔（秒）")]
        [Range(0f, 0.2f)] public float collapseStep = 0.02f;

        [Header("ボウリングの球")]
        [Tooltip("球の大きさ（半径。メダルの直径に対する倍率）")]
        [Range(0.3f, 3f)] public float ballRadius = 0.8f;
        [Tooltip("床を転がる速さ（メダルの直径 / 秒）")]
        [Range(2f, 60f)] public float ballSpeed = 16f;
        [Tooltip("モニターの下から床に着くまでの時間（秒）")]
        [Range(0.1f, 3f)] public float dropSeconds = 0.55f;

        [Header("効果（吹き飛ばす強さ。メダルの直径 / 秒）")]
        [Tooltip("球にふれたメダルを、前へ飛ばす強さ（球の速さに対する倍率）")]
        [Range(0.5f, 3f)] public float hitForward = 1.2f;
        [Tooltip("球にふれたメダルを、横へ飛ばす強さ")]
        [Range(0f, 20f)] public float hitSide = 4f;
        [Tooltip("球にふれたメダルを、上へ飛ばす強さ")]
        [Range(0f, 20f)] public float hitUp = 3.5f;
        [Tooltip("タワーのメダルを、くずれる時に上へはね上げる強さ（どの高さのメダルも、中央の穴に落ちるように飛ぶ向きを計算する）")]
        [Range(0f, 30f)] public float towerBlast = 6.5f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip riseSound;
        public AudioClip readySound;
        public AudioClip rollSound;
        public AudioClip strikeSound;

        protected override string DefaultKey => "Bowling";
        protected override string ScreenTitle => "BOWLING";
        protected override string ScreenSubtitle => "ストライク・レーン";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;   // いつものモニターの画面は変えない

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<BowlingRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed()
        {
            ReleaseTower(false);
            if (Instance == this) Instance = null;
        }

        static readonly Color Neon = new Color32(0xff, 0x3c, 0xb4, 0xff);
        static readonly Color Cyan = new Color32(0x3c, 0xe6, 0xff, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color Lane = new Color32(0x0c, 0x0a, 0x1c, 0xff);

        enum Phase { Wait, Rise, Ready, Go, Roll, Strike, Leave, Done }

        Phase phase;
        float phaseTime, waitSeconds;

        // ---- タワー ----
        class Piece
        {
            public Medal m;
            public Vector3 home;
            public int level;
            public bool kinematicWas, collideWas;
            public RigidbodyInterpolation interpWas;
        }
        readonly List<Piece> tower = new List<Piece>();
        Vector3 towerPos;
        bool towerStanding;
        float towerHeight;
        float collapseTime = -1f;   // 0 以上なら、くずれている途中

        // ---- 球 ----
        Transform ball;
        Rigidbody ballBody;
        float ballR, ballTime;
        Vector3 ballStart, ballLand, ballEnd;
        float rollLength;
        Vector3 ballPrev;
        readonly List<Medal> near = new List<Medal>();

        // ---- ボウリング用モニター ----
        Transform panel;
        TextMesh mainText, headText;
        Material frameMat;
        Vector3 panelHome, panelUp;
        float panelW, panelH;
        Renderer monitorRenderer;

        ParticleSystem sparks, dust, confetti;
        AudioClip madeRise, madeReady, madeRoll, madeStrike, madeHit;
        float lastHitSound;

        // ================================================================
        // 部品
        // ================================================================

        static Material Mat(Color c, float metal = 0.1f, float smooth = 0.6f, float glow = 0.1f)
        {
            var m = ProtoAssets.MakeMaterial(c, metal, smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            return m;
        }

        protected override void BuildBoard()
        {
            ballR = D * ballRadius;
            BuildBall();
            sparks = Particles("Sparks", Board, Vector3.up, D * 5f, D * 0.08f, 0.6f, 0.8f, 70f);
            dust = Particles("Dust", Board, Vector3.up, D * 1.2f, D * 0.4f, 0.9f, 0f, 80f);
            confetti = Particles("Confetti", Board, Vector3.up, D * 4f, D * 0.12f, 1.8f, 0.4f, 45f);
        }

        void BuildBall()
        {
            var go = Shape(PrimitiveType.Sphere, "BowlingBall", Board, Vector3.zero, Vector3.one * ballR * 2f, Mat(new Color(0.22f, 0.1f, 0.55f), 0.3f, 0.95f, 0.25f), true);
            ball = go.transform;
            var hole = ProtoAssets.MakeMaterial(new Color(0.02f, 0.02f, 0.03f), 0f, 0.9f);
            var swirl = Mat(Neon, 0.2f, 0.9f, 0.6f);
            Shape(PrimitiveType.Sphere, "FingerHole", ball, new Vector3(-0.1f, 0.4f, -0.25f), Vector3.one * 0.13f, hole);
            Shape(PrimitiveType.Sphere, "FingerHole", ball, new Vector3(0.1f, 0.4f, -0.25f), Vector3.one * 0.13f, hole);
            Shape(PrimitiveType.Sphere, "ThumbHole", ball, new Vector3(0f, 0.25f, -0.4f), Vector3.one * 0.15f, hole);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                var s = Shape(PrimitiveType.Sphere, "Swirl", ball, Quaternion.Euler(0f, a, 20f) * new Vector3(0.49f, 0f, 0f), new Vector3(0.05f, 0.22f, 0.12f), swirl);
                s.transform.localRotation = Quaternion.Euler(0f, a, 20f);
            }
            ballBody = go.AddComponent<Rigidbody>();
            ballBody.isKinematic = true;
            ballBody.interpolation = RigidbodyInterpolation.Interpolate;
            ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            go.SetActive(false);
        }

        /// <summary>ボウリング用のモニター（いつものモニターの前に下りてくる板）。いつものモニターの場所がわかってから作る</summary>
        void BuildPanel()
        {
            if (panel != null) return;
            FindMonitor();
            Bounds b = monitorRenderer != null ? monitorRenderer.bounds : new Bounds(new Vector3(0f, Layout.wallHeight + D * 1.5f, BackZ), new Vector3(HalfW * 1.6f, D * 2.5f, 0.1f));
            panelW = b.size.x * 0.5f;
            panelH = panelW * 0.5f;
            panelHome = new Vector3(b.center.x, b.center.y, b.min.z - D * 0.25f);
            panelUp = panelHome + Vector3.up * (b.size.y * 0.5f + panelH + D * 3f);

            panel = new GameObject("BowlingMonitor").transform;
            panel.SetParent(Board, false);
            var dark = Mat(new Color(0.08f, 0.08f, 0.1f), 0.6f, 0.8f, 0f);
            var screen = Mat(Lane, 0f, 0.9f, 0.6f);
            frameMat = Mat(Neon, 0.1f, 0.8f, 2.5f);
            var wire = Mat(new Color(0.6f, 0.6f, 0.65f), 0.9f, 0.8f, 0f);
            float t = D * 0.08f;
            Cube("Case", panel, new Vector3(0f, 0f, D * 0.06f), new Vector3(panelW + t * 2f, panelH + t * 2f, D * 0.1f), dark);
            Cube("Screen", panel, new Vector3(0f, 0f, 0f), new Vector3(panelW, panelH, D * 0.02f), screen);
            // 光るふち
            Cube("FrameTop", panel, new Vector3(0f, panelH * 0.5f + t * 0.5f, -D * 0.02f), new Vector3(panelW + t * 2f, t, D * 0.04f), frameMat);
            Cube("FrameBottom", panel, new Vector3(0f, -panelH * 0.5f - t * 0.5f, -D * 0.02f), new Vector3(panelW + t * 2f, t, D * 0.04f), frameMat);
            Cube("FrameLeft", panel, new Vector3(-panelW * 0.5f - t * 0.5f, 0f, -D * 0.02f), new Vector3(t, panelH + t * 2f, D * 0.04f), frameMat);
            Cube("FrameRight", panel, new Vector3(panelW * 0.5f + t * 0.5f, 0f, -D * 0.02f), new Vector3(t, panelH + t * 2f, D * 0.04f), frameMat);
            // つり下げるワイヤー
            for (int s = -1; s <= 1; s += 2)
                Shape(PrimitiveType.Cylinder, "Wire", panel, new Vector3(s * panelW * 0.35f, panelH * 0.5f + D * 3f, D * 0.06f), new Vector3(D * 0.03f, D * 3f, D * 0.03f), wire);
            // 文字（上の小さな文字と、まん中の大きな文字）
            headText = AddText(panel, "BOWLING", new Vector3(0f, panelH * 0.32f, -D * 0.03f), panelH * 0.16f, Cyan);
            mainText = AddText(panel, "", new Vector3(0f, -panelH * 0.06f, -D * 0.03f), panelH * 0.42f, Color.white);

            SetLayerAll(panel, FrontLayer);
            panel.gameObject.SetActive(false);
        }

        int FrontLayer => game.monitor.effectsInFrontOfMonitor ? Mathf.Clamp(game.monitor.frontEffectsLayer, 0, 31) : 0;

        static void SetLayerAll(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerAll(t.GetChild(i), layer);
        }

        void FindMonitor()
        {
            if (monitorRenderer != null) return;
            var go = GameObject.Find(game.monitor.screenObjectName);
            if (go != null) monitorRenderer = go.GetComponentInChildren<Renderer>();
        }

        static Font textFont;

        TextMesh AddText(Transform parent, string text, Vector3 pos, float size, Color color)
        {
            if (textFont == null)
            {
                try { textFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { textFont = null; }
            }
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            if (textFont != null)
            {
                tm.font = textFont;
                go.GetComponent<MeshRenderer>().sharedMaterial = textFont.material;
            }
            tm.text = text;
            tm.fontSize = 96;
            tm.fontStyle = FontStyle.Bold;
            tm.characterSize = size / 9.6f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tm;
        }

        // ================================================================
        // 流れ
        // ================================================================

        protected override void OnBegin()
        {
            SetPhase(Phase.Wait, firstDelay);
        }

        protected override void OnEnd()
        {
            // 曲が終わったら：立っているタワーはふつうのメダルにもどす。球とモニターは消える
            ReleaseTower(false);
            HideBall();
            if (panel != null) panel.gameObject.SetActive(false);
            phase = Phase.Done;
        }

        protected override void OnStateChanged(GameState state)
        {
            bool scoring = state == GameState.Playing || state == GameState.Grace;
            if (!scoring && Active)
            {
                ReleaseTower(false);
                HideBall();
                if (panel != null) panel.gameObject.SetActive(false);
                phase = Phase.Done;
            }
        }

        void SetPhase(Phase p, float wait = 0f)
        {
            phase = p;
            phaseTime = 0f;
            waitSeconds = wait;
        }

        protected override void Tick(float dt, bool running)
        {
            // プッシャーは、タワーが出てから、モニターが帰り始めるまで止める
            PusherHold = phase == Phase.Rise || phase == Phase.Ready || phase == Phase.Go || phase == Phase.Roll || phase == Phase.Strike;
            if (!running) return;
            phaseTime += dt;
            switch (phase)
            {
                case Phase.Wait:
                    if (phaseTime >= waitSeconds)
                    {
                        BuildPanel();
                        SpawnTower();
                        panel.gameObject.SetActive(true);
                        SetText("", Color.white);
                        Play(riseSound, ref madeRise, () => RecordSynth.Noise("BowlingRise", 1.4f, 0.05f, 0.3f, 0.6f, 0.8f, 6), 0.8f);
                        SetPhase(Phase.Rise);
                    }
                    break;

                case Phase.Rise:
                {
                    float k = Mathf.Clamp01(phaseTime / Mathf.Max(0.05f, riseSeconds));
                    UpdateTowerRise(k);
                    // モニターも同じ時間で下りてくる（最後に少しはずむ）
                    float e = k < 1f ? 1f - Mathf.Pow(1f - k, 3f) : 1f;
                    panel.position = Vector3.LerpUnclamped(panelUp, panelHome, e);
                    if (Random.value < dt * 20f) Emit(dust, towerPos, new Color(0.9f, 0.85f, 0.7f, 0.6f), 2);
                    if (k >= 1f)
                    {
                        Shake(0.02f, 0.2f);
                        SetText("READY?", Gold);
                        Play(readySound, ref madeReady, () => RecordSynth.Notes("BowlingReady", new[] { RecordSynth.Note(12), RecordSynth.Note(12) }, 0.18f, 0.2f, 0.3f, RecordSynth.Wave.Square));
                        SetPhase(Phase.Ready);
                    }
                    break;
                }

                case Phase.Ready:
                    if (phaseTime >= readySeconds)
                    {
                        SetText("GO!!", Cyan);
                        Play(null, ref madeReady, () => RecordSynth.Notes("BowlingReady", new[] { RecordSynth.Note(12), RecordSynth.Note(12) }, 0.18f, 0.2f, 0.3f, RecordSynth.Wave.Square));
                        LaunchBall();
                        SetPhase(Phase.Roll);
                    }
                    break;

                case Phase.Roll:
                    // 球の動きは FixedUpdate。タワーを倒して 0.6秒たったら STRIKE
                    if (!towerStanding && phaseTime > 0.1f && strikeWait >= 0f)
                    {
                        strikeWait += dt;
                        if (strikeWait >= 0.6f)
                        {
                            strikeWait = -1f;
                            SetText("STRIKE!!", Gold);
                            Emit(confetti, panel.position + Vector3.down * panelH * 0.5f, Neon, 40);
                            Emit(confetti, panel.position + Vector3.down * panelH * 0.5f, Gold, 40);
                            Play(strikeSound, ref madeStrike, () => RecordSynth.Notes("BowlingStrike", new[] { RecordSynth.Note(0), RecordSynth.Note(4), RecordSynth.Note(7), RecordSynth.Note(12), RecordSynth.Note(16) }, 0.07f, 0.6f, 0.3f, RecordSynth.Wave.Square));
                            PopCenter("STRIKE!!", Gold);
                            Speaker(1f, 16, Neon);
                            SetPhase(Phase.Strike);
                        }
                    }
                    break;

                case Phase.Strike:
                    if (phaseTime >= strikeHold)
                    {
                        HideBall();
                        SetPhase(Phase.Leave);
                    }
                    break;

                case Phase.Leave:
                {
                    float k = Mathf.Clamp01(phaseTime / 0.8f);
                    panel.position = Vector3.Lerp(panelHome, panelUp, k * k);
                    if (k >= 1f)
                    {
                        panel.gameObject.SetActive(false);
                        if (repeat) SetPhase(Phase.Wait, cooldownSeconds);
                        else SetPhase(Phase.Done);
                    }
                    break;
                }
            }
        }

        float strikeWait = -1f;

        void SetText(string s, Color c)
        {
            if (mainText == null) return;
            mainText.text = s;
            mainText.color = c;
        }

        // ================================================================
        // タワー（1段4枚 × 10段のメダル。下からニョキニョキ生えてくる）
        // ================================================================

        float Thick => game.tuning.medalThickness;

        void SpawnTower()
        {
            ReleaseTower(false);
            if (game.Pool == null) return;
            towerPos = new Vector3(0f, 0f, FrontZ + D * Mathf.Max(towerDistance, 1.1f));
            float t = Thick;
            int cols = columns >= 4 ? 4 : columns >= 2 ? 2 : 1;
            int perCol = 4 / cols;                        // 1段で、1本の柱に重ねる枚数
            float levelH = t * perCol;                    // 1段の高さ
            towerHeight = levelH * towerLevels;
            float o = D * 0.51f;
            // タワーの場所にいるメダルは、少しわきへよける
            foreach (var m in MedalsNear(towerPos, D * 1.8f, near))
            {
                if (m.Body == null || m.transform.position.y > Layout.pusherHeight * 0.6f) continue;
                Vector3 d = m.transform.position - towerPos;
                d.y = 0f;
                if (d.sqrMagnitude < 1e-4f) d = Vector3.right;
                Push(m, d.normalized * D * 2.5f + Vector3.up * D * 0.5f);
            }
            for (int lv = 0; lv < towerLevels; lv++)
                for (int i = 0; i < 4; i++)
                {
                    // メダルは平らに重ねる。1段4枚を cols 本の柱に分ける（2本なら、横にならべた2本の柱に2枚ずつ）
                    int col = i % cols;
                    int stack = lv * perCol + i / cols;   // 柱の下から何枚目か
                    Vector3 off = cols == 1 ? Vector3.zero
                                : cols == 2 ? new Vector3(col == 0 ? -o : o, 0f, 0f)
                                : new Vector3(col % 2 == 0 ? -o : o, 0f, col < 2 ? -o : o);
                    Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    Vector3 home = towerPos + off + Vector3.up * (t * (stack + 0.5f) + D * 0.005f);
                    var m = game.Pool.Spawn(home + Vector3.down * (towerHeight + D), rot);
                    if (m == null || m.Body == null) continue;
                    var p = new Piece { m = m, home = home, level = lv, kinematicWas = m.Body.isKinematic, collideWas = m.Body.detectCollisions, interpWas = m.Body.interpolation };
                    m.Body.isKinematic = true;
                    m.Body.interpolation = RigidbodyInterpolation.None;
                    tower.Add(p);
                }
            towerStanding = true;
            collapseTime = -1f;
            strikeWait = -1f;
        }

        void UpdateTowerRise(float k)
        {
            // 下の段から順に、床からニョキニョキ出てくる（上の段ほど少し遅れて、全部 k = 1 でそろう）
            float drop = towerHeight + D * 0.5f;
            foreach (var p in tower)
            {
                if (p.m == null || p.m.Body == null) continue;
                float lv = (p.home.y - towerPos.y) / Mathf.Max(0.001f, towerHeight);   // 0 下 〜 1 上
                float kk = Mathf.Clamp01(k * 1.25f - (1f - lv) * 0.25f);
                kk = 1f - (1f - kk) * (1f - kk);
                Vector3 pos = p.home + Vector3.down * drop * (1f - kk);
                p.m.Body.position = pos;
                p.m.transform.position = pos;
            }
        }

        /// <summary>タワーのメダルを、全部ふつうのメダルにもどす。blast が true なら、中央の穴の方へ吹き飛ばす</summary>
        void ReleaseTower(bool blast)
        {
            foreach (var p in tower) ReleasePiece(p, blast);
            tower.Clear();
            towerStanding = false;
            collapseTime = -1f;
        }

        /// <summary>球が当たった：下の段から順にくずす（FixedUpdate で進める）</summary>
        void StartCollapse()
        {
            towerStanding = false;
            collapseTime = 0f;
        }

        void UpdateCollapse(float dt)
        {
            if (collapseTime < 0f) return;
            collapseTime += dt;
            int upTo = collapseStep > 0f ? Mathf.FloorToInt(collapseTime / collapseStep) : towerLevels;
            for (int i = tower.Count - 1; i >= 0; i--)
            {
                if (tower[i].level > upTo) continue;
                ReleasePiece(tower[i], true);
                tower.RemoveAt(i);
            }
            if (tower.Count == 0) collapseTime = -1f;
        }

        void ReleasePiece(Piece p, bool blast)
        {
            var m = p.m;
            if (m == null || m.Body == null) return;
            m.Body.isKinematic = p.kinematicWas;
            m.Body.detectCollisions = p.collideWas;
            m.Body.interpolation = p.interpWas;
            if (!m.gameObject.activeInHierarchy || m.Consumed) return;
            m.Body.WakeUp();
            if (!blast) { m.Body.velocity = Vector3.zero; return; }
            // 【効果】中央の穴（手前のふちの少し先）に落ちるように、高さに合わせて飛ばす向きを計算する
            Vector3 pos = m.transform.position;
            float hx = HoleHalf * 0.7f;
            Vector3 target = new Vector3(Mathf.Clamp(pos.x + Random.Range(-1f, 1f) * D, -hx, hx), -D * 0.3f, FrontZ - D * Random.Range(0.4f, 1.2f));
            float g = Mathf.Max(0.1f, -Physics.gravity.y);
            float vy = D * towerBlast * 0.4f * Random.Range(0.6f, 1.2f);
            float h = Mathf.Max(0f, pos.y - target.y);
            float T = (vy + Mathf.Sqrt(vy * vy + 2f * g * h)) / g;   // 着くまでの時間
            Vector3 flat = target - pos;
            flat.y = 0f;
            m.Body.velocity = flat / Mathf.Max(0.05f, T) * 1.05f + Vector3.up * vy;
            m.Body.angularVelocity = Random.insideUnitSphere * 30f;
        }

        // ================================================================
        // ボウリングの球
        // ================================================================

        void LaunchBall()
        {
            FindMonitor();
            float topY = monitorRenderer != null ? monitorRenderer.bounds.min.y : Layout.wallHeight + D;
            float startZ = monitorRenderer != null ? monitorRenderer.bounds.min.z - ballR : BackZ;
            ballStart = new Vector3(0f, topY - ballR * 1.2f, startZ);
            ballLand = new Vector3(0f, ballR, Mathf.Min(PusherFrontZ - ballR * 1.5f, startZ - D));
            ballEnd = new Vector3(0f, ballR, FrontZ + ballR * 0.2f);
            rollLength = Mathf.Max(0.01f, ballLand.z - ballEnd.z);
            ballTime = 0f;
            ballPrev = ballStart;
            ball.gameObject.SetActive(true);
            ball.position = ballStart;
            ballBody.position = ballStart;
            strikeWait = 0f;
            Play(rollSound, ref madeRoll, () => RecordSynth.Noise("BowlingRoll", 2.2f, 0.03f, 0.05f, 1.6f, 0.9f, 12));
            Emit(sparks, ballStart, Cyan, 20);
        }

        void HideBall()
        {
            if (ball != null) ball.gameObject.SetActive(false);
        }

        /// <summary>時間 t の球の場所（落ちてくる → 床を転がる → 手前のふちから穴へ落ちる）</summary>
        Vector3 BallAt(float t)
        {
            if (t < dropSeconds)
            {
                float k = t / dropSeconds;
                Vector3 p = Vector3.Lerp(ballStart, ballLand, k);
                p.y = Mathf.Lerp(ballStart.y, ballLand.y, k * k) + Mathf.Sin(k * Mathf.PI) * D * 0.4f;
                return p;
            }
            float s = (t - dropSeconds) * D * ballSpeed;
            if (s <= rollLength) return new Vector3(0f, ballR, ballLand.z - s);
            // 手前のふちを越えたら、穴へ落ちる
            float over = (s - rollLength) / (D * ballSpeed);
            return ballEnd + new Vector3(0f, -0.5f * 9.81f * over * over * 2f, -(s - rollLength));
        }

        void FixedUpdate()
        {
            if (Active) UpdateCollapse(Time.fixedDeltaTime);   // タワーが下の段から順にくずれる
            if (!Active || ball == null || !ball.gameObject.activeSelf || phase == Phase.Done) return;
            if (phase != Phase.Roll && phase != Phase.Strike) return;
            ballTime += Time.fixedDeltaTime;
            Vector3 pos = BallAt(ballTime);
            Vector3 vel = (pos - ballPrev) / Time.fixedDeltaTime;
            ballBody.MovePosition(pos);
            // 転がる回転
            if (vel.sqrMagnitude > 1e-4f)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, vel.normalized);
                ballBody.MoveRotation(Quaternion.AngleAxis(vel.magnitude * Time.fixedDeltaTime / ballR * Mathf.Rad2Deg, axis) * ballBody.rotation);
            }
            ballPrev = pos;
            bool onFloor = ballTime >= dropSeconds && pos.y > -ballR;

            // タワーに届いたら、倒す
            if (towerStanding && onFloor && pos.z - ballR <= towerPos.z + D * 1.05f)
            {
                StartCollapse();
                Emit(sparks, towerPos + Vector3.up * towerHeight * 0.5f, Gold, 40);
                Emit(sparks, towerPos + Vector3.up * towerHeight * 0.5f, Neon, 30);
                Shake(0.06f, 0.4f);
                Play(null, ref madeHit, () => RecordSynth.Noise("BowlingCrash", 0.6f, 0.4f, 0.002f, 0.3f, 1f, 8));
            }

            // 念のため：球が手前のふちまで来ても倒れていなければ、ここで倒す
            if (towerStanding && ballTime > dropSeconds + rollLength / Mathf.Max(0.01f, D * ballSpeed)) StartCollapse();

            // 球にふれたメダルは、必ず吹き飛ぶ
            if (pos.y > -ballR)
            {
                Vector3 fwd = vel; fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.back;
                float reach = ballR + D * 0.55f;
                foreach (var m in MedalsNear(pos, reach, near))
                {
                    if (m.Body == null || m.Body.isKinematic) continue;
                    Vector3 d = m.transform.position - pos;
                    if (d.magnitude > reach) continue;
                    Vector3 side = new Vector3(d.x, 0f, 0f);
                    side = side.sqrMagnitude > 1e-4f ? side.normalized : (Random.value < 0.5f ? Vector3.left : Vector3.right);
                    float speed = Mathf.Max(vel.magnitude, D * ballSpeed);
                    m.Body.WakeUp();
                    m.Body.velocity = fwd * speed * hitForward + side * D * hitSide * Random.Range(0.6f, 1.2f) + Vector3.up * D * hitUp * Random.Range(0.6f, 1.2f);
                    m.Body.angularVelocity = Random.insideUnitSphere * 25f;
                    if (Time.time - lastHitSound > 0.04f)
                    {
                        lastHitSound = Time.time;
                        Emit(sparks, m.transform.position, Cyan, 3);
                    }
                }
            }
            if (pos.y < -ballR * 3f) ball.gameObject.SetActive(false);
        }

        // ================================================================
        // 見た目（毎フレーム）
        // ================================================================

        protected override bool KeepBoardVisible() => (confetti != null && confetti.particleCount > 0) || (sparks != null && sparks.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            if (panel != null && panel.gameObject.activeSelf)
            {
                SetGlow(frameMat, phase == Phase.Strike ? Color.Lerp(Neon, Gold, Mathf.PingPong(t * 6f, 1f)) : Neon, phase == Phase.Strike ? 4f : 2.5f);
                if (mainText != null)
                {
                    float pulse = phase == Phase.Strike ? 1f + 0.12f * Mathf.Sin(t * 18f) : phase == Phase.Ready ? 1f + 0.05f * Mathf.Sin(t * 8f) : 1f;
                    mainText.transform.localScale = Vector3.one * pulse;
                    if (phase == Phase.Strike) mainText.color = Color.HSVToRGB(Mathf.Repeat(t * 1.5f, 1f), 0.6f, 1f);
                }
                // モニターは、カメラの方を向く（少しだけ）
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 to = panel.position - cam.transform.position;
                    to.x = 0f;
                    if (to.sqrMagnitude > 1e-4f) panel.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
                }
            }
            if (ball != null && ball.gameObject.activeSelf && Random.value < 0.5f)
                Emit(sparks, ball.position + Vector3.down * ballR * 0.8f, Random.value < 0.5f ? Cyan : Neon, 1);
        }
    }
}
