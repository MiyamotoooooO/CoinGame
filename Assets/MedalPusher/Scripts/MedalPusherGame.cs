using UnityEngine;

namespace MedalPusher
{
    public enum GameState
    {
        Warmup,  // 開始直後。初期メダルが落ち着くまでカウントしない
        Playing, // 投入できる
        Grace,   // 投入を使い切った。落ちてくるメダルを待つ
        Cleared, // ノルマ達成
        Failed   // ノルマ未達
    }

    /// <summary>
    /// 試作のエントリーポイント。空のGameObjectにこれを1つ付けて再生すると、
    /// 盤面・プッシャー・落下口・カメラ・投入口・デバッグHUDをすべてコードで組み立てる。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class MedalPusherGame : MonoBehaviour
    {
        [Header("盤面レイアウト（再生前に設定）")]
        public BoardLayout layout = new BoardLayout();
        [Header("物理チューニング（再生中も Tab の調整パネルから変更可）")]
        public PhysicsTuning tuning = new PhysicsTuning();
        [Header("ルール（仮）")]
        public RuleSettings rules = new RuleSettings();

        // ---- 今後レコード効果をつなぐためのフック ----
        /// <summary>メダルが穴に落ちた（Warmup中は呼ばれない）</summary>
        public event System.Action<Medal, DropZoneType> MedalDropped;
        /// <summary>プレイヤーがメダルを投入した</summary>
        public event System.Action<Medal> MedalInserted;
        public event System.Action<GameState> StateChanged;

        public GameState State { get; private set; }
        public float StateTime { get; private set; }
        public int RoundNumber { get; private set; }
        public int OwnedMedals { get; private set; }
        public int InsertedThisRound { get; private set; }
        public int Points { get; private set; }
        public int CenterDrops { get; private set; }
        public int SideDrops { get; private set; }
        public int LostDrops { get; private set; }

        public MedalPool Pool { get; private set; }
        public Pusher Pusher { get; private set; }
        public MedalLauncher Launcher { get; private set; }
        public DebugHud Hud { get; private set; }

        public float BoardHalfWidth => layout.boardWidth * 0.5f;
        public float BackWallFrontZ => layout.pusherRetractZ + layout.backWallOffset;
        public float FloorBackZ => layout.pusherRetractZ + layout.pusherDepth + 0.5f;

        public bool HasInsertsLeft => OwnedMedals > 0 && InsertedThisRound < rules.insertLimit;
        public bool CanInsert => rules.freePlay || (State == GameState.Playing && HasInsertsLeft);

        PhysicMaterial medalPhysicMaterial;
        PhysicMaterial boardPhysicMaterial;
        Transform boardRoot;
        bool topView;

        static readonly Color FloorColor = new Color(0.16f, 0.18f, 0.26f);
        static readonly Color WallColor = new Color(0.30f, 0.32f, 0.40f);
        static readonly Color PusherColor = new Color(0.75f, 0.75f, 0.80f);
        static readonly Color CenterColor = new Color(0.25f, 0.85f, 0.45f);
        static readonly Color SideColor = new Color(0.90f, 0.30f, 0.30f);
        static readonly Color MedalColor = new Color(1.00f, 0.80f, 0.30f);

        // ================================================================
        // 初期化
        // ================================================================

        void Awake()
        {
            Time.maximumDeltaTime = 0.1f; // 重い時に物理が雪だるま式に遅れるのを防ぐ
            CreatePhysicMaterials();
            ApplyGlobalPhysics();
            BuildBoard();
            SetupLight();
            ApplyCameraPreset();

            Pool = gameObject.AddComponent<MedalPool>();
            Pool.Init(tuning, ProtoAssets.MakeMaterial(MedalColor, 0.8f, 0.6f), medalPhysicMaterial,
                rules.prefillMedals + 150);

            Launcher = gameObject.AddComponent<MedalLauncher>();
            Launcher.Init(this);

            Hud = gameObject.AddComponent<DebugHud>();
            Hud.Init(this);

            ApplyTuning();
            ResetGame();
        }

        void CreatePhysicMaterials()
        {
            medalPhysicMaterial = new PhysicMaterial("Medal")
            {
                frictionCombine = PhysicMaterialCombine.Average,
                bounceCombine = PhysicMaterialCombine.Average
            };
            boardPhysicMaterial = new PhysicMaterial("Board")
            {
                frictionCombine = PhysicMaterialCombine.Average,
                bounceCombine = PhysicMaterialCombine.Average
            };
        }

        void ApplyGlobalPhysics()
        {
            Time.fixedDeltaTime = 1f / Mathf.Clamp(tuning.physicsHz, 30, 200);
            Physics.gravity = new Vector3(0f, -9.81f * tuning.gravityScale, 0f);
            Physics.defaultSolverIterations = tuning.solverIterations;
            Physics.defaultSolverVelocityIterations = tuning.velocityIterations;
            Physics.sleepThreshold = tuning.sleepThreshold;
        }

        /// <summary>tuning の値を物理に反映する。調整パネルから呼ばれる。</summary>
        public void ApplyTuning()
        {
            ApplyGlobalPhysics();

            medalPhysicMaterial.dynamicFriction = tuning.medalDynamicFriction;
            medalPhysicMaterial.staticFriction = tuning.medalStaticFriction;
            medalPhysicMaterial.bounciness = tuning.medalBounciness;
            boardPhysicMaterial.dynamicFriction = tuning.boardDynamicFriction;
            boardPhysicMaterial.staticFriction = tuning.boardStaticFriction;
            boardPhysicMaterial.bounciness = 0f;

            if (Pool != null) Pool.ApplyBodySettings();
            if (Pusher != null)
            {
                Pusher.cycleSeconds = tuning.pusherCycleSeconds;
                Pusher.stroke = tuning.pusherStroke;
            }
        }

        // ================================================================
        // 盤面の組み立て
        // ================================================================

        void BuildBoard()
        {
            boardRoot = new GameObject("Board").transform;

            float w = layout.boardWidth;
            float hw = w * 0.5f;
            float t = layout.wallThickness;
            float frontZ = layout.floorFrontZ;
            float backZ = FloorBackZ;
            float len = backZ - frontZ;
            float midZ = (backZ + frontZ) * 0.5f;

            // 床（上面が y=0）
            CreateBox("Floor", new Vector3(0f, -0.25f, midZ), new Vector3(w, 0.5f, len), FloorColor, boardPhysicMaterial);

            // 左右の壁
            float wallY = (layout.wallHeight - 0.5f) * 0.5f;
            var wallSize = new Vector3(t, layout.wallHeight + 0.5f, len);
            CreateBox("WallL", new Vector3(-hw - t * 0.5f, wallY, midZ), wallSize, WallColor, boardPhysicMaterial);
            CreateBox("WallR", new Vector3(hw + t * 0.5f, wallY, midZ), wallSize, WallColor, boardPhysicMaterial);

            // 奥の壁：プッシャー上面すれすれまで下りていて、引く時に上のメダルを掻き落とす
            float bwBottom = layout.pusherHeight + layout.backWallGap;
            float bwFront = BackWallFrontZ;
            float bwDepth = backZ - bwFront;
            CreateBox("BackWall",
                new Vector3(0f, (bwBottom + layout.wallHeight) * 0.5f, bwFront + bwDepth * 0.5f),
                new Vector3(w, layout.wallHeight - bwBottom, bwDepth), WallColor, boardPhysicMaterial);

            // プッシャー
            var pusherGo = CreateBox("Pusher", Vector3.zero,
                new Vector3(w - layout.pusherSideGap * 2f, layout.pusherHeight, layout.pusherDepth),
                PusherColor, boardPhysicMaterial);
            pusherGo.AddComponent<Rigidbody>();
            Pusher = pusherGo.AddComponent<Pusher>();
            Pusher.Init(layout.pusherRetractZ, layout.pusherDepth, layout.pusherHeight * 0.5f,
                tuning.pusherStroke, tuning.pusherCycleSeconds);

            // 落下口（手前の縁の下にトリガーを並べる）
            float cw = Mathf.Clamp(layout.centerHoleWidth, 0.5f, w);
            float sideInner = cw * 0.5f;
            float sideOuter = hw + t + 1f;
            float sideW = sideOuter - sideInner;
            // トリガーの範囲: y = -4.0 〜 -0.6（床の上のメダルには触れない）、z = 縁の3unit手前 〜 縁の直前
            const float zoneTop = -0.6f, zoneBottom = -4f, zoneDepth = 2.98f;
            float zoneY = (zoneTop + zoneBottom) * 0.5f;
            float zoneH = zoneTop - zoneBottom;
            float zoneZ = frontZ - 0.02f - zoneDepth * 0.5f;

            CreateZone("Hole_Center", DropZoneType.Center, new Vector3(0f, zoneY, zoneZ), new Vector3(cw, zoneH, zoneDepth));
            CreateZone("Hole_Left", DropZoneType.Side, new Vector3(-(sideInner + sideW * 0.5f), zoneY, zoneZ), new Vector3(sideW, zoneH, zoneDepth));
            CreateZone("Hole_Right", DropZoneType.Side, new Vector3(sideInner + sideW * 0.5f, zoneY, zoneZ), new Vector3(sideW, zoneH, zoneDepth));
            CreateZone("LostZone", DropZoneType.Lost, new Vector3(0f, -15f, 0f), new Vector3(200f, 2f, 200f));

            // 見た目：手前の縁の色帯と、穴の下の受け皿（当たり判定なし）
            float sideVisW = hw - sideInner;
            CreateBox("Strip_Center", new Vector3(0f, 0.004f, frontZ + 0.15f), new Vector3(cw, 0.01f, 0.3f), CenterColor, null, false);
            CreateBox("Strip_L", new Vector3(-(sideInner + sideVisW * 0.5f), 0.004f, frontZ + 0.15f), new Vector3(sideVisW, 0.01f, 0.3f), SideColor, null, false);
            CreateBox("Strip_R", new Vector3(sideInner + sideVisW * 0.5f, 0.004f, frontZ + 0.15f), new Vector3(sideVisW, 0.01f, 0.3f), SideColor, null, false);
            CreateBox("Tray_Center", new Vector3(0f, -4f, zoneZ), new Vector3(cw, 0.2f, 3f), CenterColor, null, false);
            CreateBox("Tray_L", new Vector3(-(sideInner + sideVisW * 0.5f), -4f, zoneZ), new Vector3(sideVisW, 0.2f, 3f), SideColor, null, false);
            CreateBox("Tray_R", new Vector3(sideInner + sideVisW * 0.5f, -4f, zoneZ), new Vector3(sideVisW, 0.2f, 3f), SideColor, null, false);
            CreateBox("Divider_L", new Vector3(-sideInner, -2.2f, zoneZ), new Vector3(0.06f, 3.6f, 3f), WallColor, null, false);
            CreateBox("Divider_R", new Vector3(sideInner, -2.2f, zoneZ), new Vector3(0.06f, 3.6f, 3f), WallColor, null, false);
        }

        GameObject CreateBox(string name, Vector3 position, Vector3 size, Color color, PhysicMaterial pm, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(boardRoot, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = ProtoAssets.MakeMaterial(color);
            var col = go.GetComponent<BoxCollider>();
            if (collider) col.sharedMaterial = pm;
            else DestroyImmediate(col);
            return go;
        }

        void CreateZone(string name, DropZoneType type, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(boardRoot, false);
            go.transform.localPosition = position;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var zone = go.AddComponent<DropZone>();
            zone.type = type;
            zone.game = this;
        }

        void SetupLight()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) return;

            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
        }

        void ApplyCameraPreset()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;

            float frontZ = layout.floorFrontZ;
            if (topView)
            {
                cam.transform.position = new Vector3(0f, 14f, frontZ + 2.5f);
                cam.transform.rotation = Quaternion.Euler(78f, 0f, 0f);
            }
            else
            {
                cam.transform.position = new Vector3(0f, 8.5f, frontZ - 6.5f);
                cam.transform.LookAt(new Vector3(0f, -0.5f, frontZ + 3.5f));
            }
        }

        // ================================================================
        // 進行
        // ================================================================

        public void ResetGame()
        {
            Pool.DespawnAll();
            Pusher.ResetPhase();
            OwnedMedals = rules.startMedals;
            RoundNumber = 1;
            StartRound();
            Prefill(rules.prefillMedals);
            SetState(GameState.Warmup);
        }

        void StartRound()
        {
            InsertedThisRound = 0;
            Points = 0;
            CenterDrops = SideDrops = LostDrops = 0;
        }

        public void NextRound()
        {
            if (State != GameState.Cleared) return;
            RoundNumber++;
            StartRound();
            SetState(GameState.Playing);
        }

        void SetState(GameState s)
        {
            State = s;
            StateTime = 0f;
            StateChanged?.Invoke(s);
        }

        void Update()
        {
            StateTime += Time.deltaTime;

            switch (State)
            {
                case GameState.Warmup:
                    if (StateTime >= rules.warmupSeconds) SetState(GameState.Playing);
                    break;

                case GameState.Playing:
                    if (rules.freePlay) break;
                    if (Points >= rules.quota) SetState(GameState.Cleared);
                    else if (!HasInsertsLeft) SetState(GameState.Grace);
                    break;

                case GameState.Grace:
                    if (rules.freePlay || HasInsertsLeft) SetState(GameState.Playing); // 払い戻しで復活した等
                    else if (Points >= rules.quota) SetState(GameState.Cleared);
                    else if (StateTime >= rules.graceSeconds) SetState(GameState.Failed);
                    break;
            }

            if (InputCompat.Down(GameKey.Reset)) ResetGame();
            if (InputCompat.Down(GameKey.NextRound)) NextRound();
            if (InputCompat.Down(GameKey.AddMedals)) AddTestMedals(50);
            if (InputCompat.Down(GameKey.PausePusher)) Pusher.paused = !Pusher.paused;
            if (InputCompat.Down(GameKey.ToggleCamera)) { topView = !topView; ApplyCameraPreset(); }
        }

        public bool TryInsert(Vector3 position, Vector3 velocity)
        {
            if (!CanInsert) return false;

            if (!rules.freePlay) OwnedMedals--;
            InsertedThisRound++;

            var rot = Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(-6f, 6f));
            var m = Pool.Spawn(position, rot);
            m.MarkInserted(Time.time);
            m.Body.velocity = velocity;
            m.Body.angularVelocity = new Vector3(0f, Random.Range(-6f, 6f), 0f); // 少し回転させて自然に見せる
            MedalInserted?.Invoke(m);
            return true;
        }

        /// <summary>DropZone から呼ばれる。</summary>
        public void OnMedalDropped(Medal medal, DropZoneType type)
        {
            if (medal.Consumed) return;
            medal.Consumed = true;

            if (State != GameState.Warmup)
            {
                bool scoring = State == GameState.Playing || State == GameState.Grace;
                switch (type)
                {
                    case DropZoneType.Center:
                        CenterDrops++;
                        OwnedMedals += rules.centerMedalReturn;
                        if (scoring) Points += rules.centerPoints;
                        break;
                    case DropZoneType.Side:
                        SideDrops++;
                        OwnedMedals += rules.sideMedalReturn;
                        if (scoring) Points += rules.sidePoints;
                        break;
                    case DropZoneType.Lost:
                        LostDrops++;
                        break;
                }
                MedalDropped?.Invoke(medal, type);
            }

            Pool.Despawn(medal);
        }

        // ================================================================
        // メダル配置
        // ================================================================

        /// <summary>プッシャー前の床にメダルを敷き詰める（互い違いに積む）。</summary>
        void Prefill(int count)
        {
            float d = tuning.medalDiameter;
            float half = BoardHalfWidth - d * 0.6f;
            float zMin = layout.floorFrontZ + d * 0.7f;
            float zMax = layout.pusherRetractZ - d * 0.6f;
            float step = d * 1.08f;
            int cols = Mathf.Max(1, Mathf.FloorToInt(half * 2f / step) + 1);
            int rows = Mathf.Max(1, Mathf.FloorToInt((zMax - zMin) / step) + 1);

            int spawned = 0;
            for (int layer = 0; spawned < count && layer < 60; layer++)
            {
                float offset = (layer % 2 == 1) ? step * 0.5f : 0f;
                float y = tuning.medalThickness * 0.5f + 0.02f + layer * (tuning.medalThickness + 0.06f);
                for (int r = 0; r < rows && spawned < count; r++)
                {
                    for (int c = 0; c < cols && spawned < count; c++)
                    {
                        float x = -half + c * step + offset;
                        float z = zMin + r * step + offset;
                        if (x > half || z > zMax) continue;
                        x += Random.Range(-0.06f, 0.06f);
                        z += Random.Range(-0.06f, 0.06f);
                        var rot = Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(0f, 360f), Random.Range(-3f, 3f));
                        Pool.Spawn(new Vector3(x, y, z), rot);
                        spawned++;
                    }
                }
            }
        }

        /// <summary>負荷テスト用。盤面の上空からばらまく。</summary>
        public void AddTestMedals(int count)
        {
            float half = BoardHalfWidth - tuning.medalDiameter * 0.6f;
            float zMin = layout.floorFrontZ + 0.8f;
            float zMax = layout.pusherRetractZ - 0.6f;
            for (int i = 0; i < count; i++)
            {
                var pos = new Vector3(Random.Range(-half, half), 2.5f + i * 0.04f, Random.Range(zMin, zMax));
                var rot = Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f));
                Pool.Spawn(pos, rot);
            }
        }
    }
}
