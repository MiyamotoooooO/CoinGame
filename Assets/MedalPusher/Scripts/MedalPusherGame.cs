using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    public enum GameState
    {
        Intro,    // エリアの始まりの演出中。カウントしない
        Warmup,   // 開始直後。初期メダルが落ち着くまでカウントしない
        Playing,  // 投入できる
        Grace,    // 投入を使い切った。落ちてくるメダルを待つ
        Cleared,  // ノルマ達成
        Failed,   // ノルマ未達
        Shop,     // エリアの最後（ボス）のあと。ショップ（仮）
        Finished  // 用意したエリアをすべて終えた
    }

    /// <summary>
    /// 試作のエントリーポイント。空のGameObjectにこれを1つ付けて再生する。
    /// 盤面は「シーンに置いたモデルを使う」か「コードで仮の盤面を作る」かを Board Source で選ぶ。
    /// プッシャーの動き・落下口・カメラ・投入口・デバッグHUDはどちらでもコードで用意する。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class MedalPusherGame : MonoBehaviour
    {
        [Header("盤面の出どころ")]
        [Tooltip("Scene: シーンのモデルを使う / Generate: コードで仮の盤面を作る")]
        public BoardSource boardSource = BoardSource.Scene;
        public SceneBoard sceneBoard = new SceneBoard();

        [Header("盤面レイアウト（再生前に設定。コンポーネントの右クリックメニューで一括入力できる）")]
        public BoardLayout layout = new BoardLayout();
        [Header("物理チューニング（再生中も Tab の調整パネルから変更可）")]
        public PhysicsTuning tuning = new PhysicsTuning();
        [Header("ルール（仮）")]
        public RuleSettings rules = new RuleSettings();
        [Header("エリアとラウンド（6エリアまで増やせる。タイトル・サムネイルは仮）")]
        public AreaDefinition[] areas = DefaultAreas();
        [Header("モニター")]
        public MonitorSettings monitor = new MonitorSettings();
        [Header("見た目（右クリック →「メダルの見た目をモデルにする」で Medal.obj を入れられる）")]
        public VisualSettings visuals = new VisualSettings();

        // ---- 今後レコード効果をつなぐためのフック ----
        /// <summary>メダルが穴に落ちた（Intro・Warmup中は呼ばれない）</summary>
        public event System.Action<Medal, DropZoneType> MedalDropped;
        /// <summary>プレイヤーがメダルを投入した</summary>
        public event System.Action<Medal> MedalInserted;
        public event System.Action<GameState> StateChanged;

        public GameState State { get; private set; }
        public float StateTime { get; private set; }
        public int RoundNumber { get; private set; }
        /// <summary>今のエリアの番号（0から）</summary>
        public int AreaIndex { get; private set; }
        /// <summary>エリアの中のラウンドの番号（0から）</summary>
        public int RoundIndex { get; private set; }
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
        public MonitorDisplay Monitor { get; private set; }

        public AreaDefinition CurrentArea => areas[Mathf.Clamp(AreaIndex, 0, areas.Length - 1)];
        public RoundDefinition CurrentRound => CurrentArea.rounds[Mathf.Clamp(RoundIndex, 0, CurrentArea.rounds.Length - 1)];
        public int CurrentQuota => CurrentRound.quota;
        public bool IsBossRound => CurrentRound.isBoss;
        public bool IsLastRoundOfArea => RoundIndex >= CurrentArea.rounds.Length - 1;
        /// <summary>ボスの残りHP（ノルマからポイントを引いた値）</summary>
        public int BossHp => Mathf.Max(0, CurrentQuota - Points);

        public float BoardHalfWidth => layout.boardWidth * 0.5f;
        public float BackWallFrontZ => layout.pusherRetractZ + layout.backWallOffset;
        public float FloorBackZ => layout.pusherRetractZ + layout.pusherDepth + 0.5f;

        public bool HasInsertsLeft => OwnedMedals > 0 && InsertedThisRound < rules.insertLimit;
        public bool CanInsert => rules.freePlay || (State == GameState.Playing && HasInsertsLeft);

        PhysicMaterial medalPhysicMaterial;
        PhysicMaterial boardPhysicMaterial;
        Transform boardRoot;
        readonly List<Collider> boardColliders = new List<Collider>();
        bool topView;
        float resetTime;

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
            boardRoot = new GameObject("BoardRuntime").transform;
            if (boardSource == BoardSource.Scene && !SetupSceneBoard())
            {
                Debug.LogWarning("[MedalPusher] シーンに「Pusher」が見つからないため、コードで仮の盤面を作ります。");
                boardSource = BoardSource.Generate;
            }
            if (boardSource == BoardSource.Generate) BuildBoard();
            SetupLight();
            ApplyCameraPreset();

            Pool = gameObject.AddComponent<MedalPool>();
            // 仮のメダル（モデルが無い時）は金属っぽさを控えめにする。強いと、映り込む景色が無いため暗く沈む
            Pool.Init(tuning, visuals, ProtoAssets.MakeMaterial(MedalColor, 0.2f, 0.5f), medalPhysicMaterial,
                rules.prefillMedals + 150);

            Launcher = gameObject.AddComponent<MedalLauncher>();
            Launcher.Init(this);

            Hud = gameObject.AddComponent<DebugHud>();
            Hud.Init(this);

            EnsureAreas();
            Monitor = gameObject.AddComponent<MonitorDisplay>();
            Monitor.Init(this);

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
            foreach (var c in boardColliders)
                if (c != null) c.contactOffset = tuning.contactOffset;

            if (Pool != null) Pool.ApplyBodySettings();
            if (Pusher != null)
            {
                Pusher.cycleSeconds = tuning.pusherCycleSeconds;
                Pusher.stroke = tuning.pusherStroke;
            }
        }

        // ================================================================
        // シーンの盤面を使う
        // ================================================================

        /// <summary>シーンに置いた盤面モデルに当たり判定・プッシャーの動き・穴の判定を付ける。Pusher が無ければ false。</summary>
        bool SetupSceneBoard()
        {
            var pusherT = Pick(sceneBoard.pusher, "Pusher");
            if (pusherT == null) return false;

            // 動かない部品（盤面・仕切り・筐体）
            var parts = new List<Transform>();
            if (sceneBoard.staticParts != null)
                foreach (var t in sceneBoard.staticParts) if (t != null) parts.Add(t);
            if (parts.Count == 0)
                foreach (var n in new[] { "Playfield", "Dividers", "Cabinet" })
                {
                    var t = FindByName(n);
                    if (t != null) parts.Add(t);
                }
            foreach (var t in parts) PrepareColliders(t, false);
            CheckModelScale(FindByName("Playfield"));

            // プッシャー：凸の当たり判定＋キネマティックRigidbody。
            // 前面が Layout の pusherRetractZ に来る位置を「一番奥」とし、そこから手前へ stroke だけ動かす
            PrepareColliders(pusherT, true);
            if (pusherT.GetComponent<Rigidbody>() == null) pusherT.gameObject.AddComponent<Rigidbody>();
            Pusher = pusherT.GetComponent<Pusher>();
            if (Pusher == null) Pusher = pusherT.gameObject.AddComponent<Pusher>();
            float shiftZ = 0f;
            if (TryGetFrontZ(pusherT, out float modelFrontZ)) shiftZ = layout.pusherRetractZ - modelFrontZ;
            Pusher.Init(pusherT.position + new Vector3(0f, 0f, shiftZ), tuning.pusherStroke, tuning.pusherCycleSeconds);

            // 穴：シーンにあればそれを使い、無ければ Layout の寸法で作る
            bool c = SetupSceneHole(Pick(sceneBoard.holeCenter, "Hole_Center"), DropZoneType.Center);
            bool l = SetupSceneHole(Pick(sceneBoard.holeLeft, "Hole_Side_Left"), DropZoneType.Side);
            bool r = SetupSceneHole(Pick(sceneBoard.holeRight, "Hole_Side_Right"), DropZoneType.Side);
            CreateHoleZones(!c, !l, !r);
            return true;
        }

        /// <summary>メッシュに当たり判定が無ければ MeshCollider を付け、全部の当たり判定に盤面の物理マテリアルを設定する。</summary>
        void PrepareColliders(Transform root, bool convex)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = convex;
            }
            foreach (var col in root.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger) continue;
                if (convex && col is MeshCollider mc) mc.convex = true;
                col.sharedMaterial = boardPhysicMaterial;
                boardColliders.Add(col);
            }
        }

        /// <summary>シーンの穴オブジェクトをトリガーにして DropZone を付ける。無ければ false。</summary>
        bool SetupSceneHole(Transform hole, DropZoneType type)
        {
            if (hole == null) return false;
            var col = hole.GetComponentInChildren<Collider>();
            if (col == null)
            {
                var mf = hole.GetComponentInChildren<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return false;
                var box = mf.gameObject.AddComponent<BoxCollider>();
                box.center = mf.sharedMesh.bounds.center;
                box.size = mf.sharedMesh.bounds.size;
                col = box;
            }
            col.isTrigger = true;
            foreach (var rend in hole.GetComponentsInChildren<Renderer>()) rend.enabled = false;
            var zone = col.gameObject.AddComponent<DropZone>();
            zone.type = type;
            zone.game = this;
            return true;
        }

        /// <summary>モデルの前面（いちばん手前 = Z が最小）の位置を見た目の範囲から求める。</summary>
        static bool TryGetFrontZ(Transform root, out float frontZ)
        {
            frontZ = 0f;
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;
            frontZ = float.MaxValue;
            foreach (var rend in renderers) frontZ = Mathf.Min(frontZ, rend.bounds.min.z);
            return true;
        }

        /// <summary>
        /// 盤面モデルの実際の幅と Layout の幅が合っているか確かめる。
        /// 取り込みの Scale Factor が違う（例: 10 にし忘れ）と、メダルやプッシャーの位置がずれるため。
        /// </summary>
        void CheckModelScale(Transform playfield)
        {
            if (playfield == null) return;
            var renderers = playfield.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float expected = layout.boardWidth + layout.wallThickness * 2f;
            if (Mathf.Abs(b.size.x - expected) > expected * 0.1f)
            {
                Debug.LogWarning($"[MedalPusher] Playfield の幅が {b.size.x:0.###} ですが、Layout では {expected:0.###} の想定です。" +
                                 "モデルの Scale Factor（盤面モデル v3 なら 10）か、Layout の寸法を確認してください。" +
                                 "（MedalPusherGame を右クリック →「盤面モデル v3 の寸法を入れる」でも合わせられます）");
            }
        }

        static Transform Pick(Transform assigned, string name) => assigned != null ? assigned : FindByName(name);

        /// <summary>名前でシーンから探す。同じ名前が親子で続く場合（.objの取り込みでよくある）は上の方を返す。</summary>
        static Transform FindByName(string name)
        {
            var go = GameObject.Find(name);
            if (go == null) return null;
            var t = go.transform;
            while (t.parent != null && t.parent.name == name) t = t.parent;
            return t;
        }

        // ================================================================
        // コードで仮の盤面を作る
        // ================================================================

        void BuildBoard()
        {
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
            Pusher.Init(new Vector3(0f, layout.pusherHeight * 0.5f, layout.pusherRetractZ + layout.pusherDepth * 0.5f),
                tuning.pusherStroke, tuning.pusherCycleSeconds);

            // 落下口
            CreateHoleZones(true, true, true);

            // 見た目：手前の縁の色帯と、穴の下の受け皿（当たり判定なし）
            float cw = Mathf.Clamp(layout.centerHoleWidth, 0.5f, w);
            float sideInner = cw * 0.5f;
            float sideVisW = hw - sideInner;
            float zoneZ = frontZ - 0.02f - layout.holeDepth * 0.5f;
            float bottom = layout.holeBottomY;
            float divH = -bottom - 0.4f;
            CreateBox("Strip_Center", new Vector3(0f, 0.004f, frontZ + 0.15f), new Vector3(cw, 0.01f, 0.3f), CenterColor, null, false);
            CreateBox("Strip_L", new Vector3(-(sideInner + sideVisW * 0.5f), 0.004f, frontZ + 0.15f), new Vector3(sideVisW, 0.01f, 0.3f), SideColor, null, false);
            CreateBox("Strip_R", new Vector3(sideInner + sideVisW * 0.5f, 0.004f, frontZ + 0.15f), new Vector3(sideVisW, 0.01f, 0.3f), SideColor, null, false);
            CreateBox("Tray_Center", new Vector3(0f, bottom, zoneZ), new Vector3(cw, 0.2f, layout.holeDepth), CenterColor, null, false);
            CreateBox("Tray_L", new Vector3(-(sideInner + sideVisW * 0.5f), bottom, zoneZ), new Vector3(sideVisW, 0.2f, layout.holeDepth), SideColor, null, false);
            CreateBox("Tray_R", new Vector3(sideInner + sideVisW * 0.5f, bottom, zoneZ), new Vector3(sideVisW, 0.2f, layout.holeDepth), SideColor, null, false);
            CreateBox("Divider_L", new Vector3(-sideInner, bottom + divH * 0.5f, zoneZ), new Vector3(0.06f, divH, layout.holeDepth), WallColor, null, false);
            CreateBox("Divider_R", new Vector3(sideInner, bottom + divH * 0.5f, zoneZ), new Vector3(0.06f, divH, layout.holeDepth), WallColor, null, false);
        }

        /// <summary>
        /// Layout の寸法で穴の判定を作る（手前の縁の下に並べる）。ロスト判定はいつも作る。
        /// </summary>
        void CreateHoleZones(bool center, bool left, bool right)
        {
            float hw = BoardHalfWidth;
            float cw = Mathf.Clamp(layout.centerHoleWidth, 0.1f, layout.boardWidth);
            float sideInner = cw * 0.5f;
            float sideOuter = hw + layout.wallThickness + layout.boardWidth * 0.125f;
            float sideW = sideOuter - sideInner;
            float zoneY = (layout.holeTopY + layout.holeBottomY) * 0.5f;
            float zoneH = layout.holeTopY - layout.holeBottomY;
            float zoneZ = layout.floorFrontZ - 0.02f - layout.holeDepth * 0.5f;

            if (center) CreateZone("Hole_Center", DropZoneType.Center, new Vector3(0f, zoneY, zoneZ), new Vector3(cw, zoneH, layout.holeDepth));
            if (left) CreateZone("Hole_Left", DropZoneType.Side, new Vector3(-(sideInner + sideW * 0.5f), zoneY, zoneZ), new Vector3(sideW, zoneH, layout.holeDepth));
            if (right) CreateZone("Hole_Right", DropZoneType.Side, new Vector3(sideInner + sideW * 0.5f, zoneY, zoneZ), new Vector3(sideW, zoneH, layout.holeDepth));
            CreateZone("LostZone", DropZoneType.Lost, new Vector3(0f, layout.holeBottomY - 10f, 0f), new Vector3(200f, 2f, 200f));
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
            if (collider)
            {
                col.sharedMaterial = pm;
                boardColliders.Add(col);
            }
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

        /// <summary>カメラの位置・角度・視野角・ゆがみを反映する。調整パネルからも呼ばれる。</summary>
        public void ApplyCameraPreset()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            float w = layout.boardWidth;
            float frontZ = layout.floorFrontZ;
            cam.nearClipPlane = tuning.medalDiameter * 0.1f;

            var lens = cam.GetComponent<LensDistortion>();
            if (lens == null) lens = cam.gameObject.AddComponent<LensDistortion>();
            lens.strength = topView ? 0f : layout.cameraLensDistortion;
            if (topView)
            {
                cam.fieldOfView = 55f;
                cam.transform.position = new Vector3(0f, w * 1.75f, frontZ + w * 0.3125f);
                cam.transform.rotation = Quaternion.Euler(78f, 0f, 0f);
            }
            else if (boardSource == BoardSource.Scene)
            {
                // モデル付属の README にあるプレイヤー視点
                cam.fieldOfView = layout.cameraFieldOfView;
                cam.transform.position = layout.cameraPosition;
                cam.transform.rotation = Quaternion.Euler(layout.cameraRotation);
            }
            else
            {
                cam.fieldOfView = 55f;
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
            Pusher.ResetPhase(rules.startPusherPosition);
            OwnedMedals = rules.startMedals;
            RoundNumber = 1;
            AreaIndex = 0;
            RoundIndex = 0;
            resetTime = Time.time;
            StartRound();
            Prefill();
            BeginArea();
        }

        /// <summary>エリアの始まり。モニターの演出を流し、終わったらプレイを始める。</summary>
        void BeginArea()
        {
            Monitor.ShowArea(AreaIndex, RoundIndex);
            if (monitor.playIntro && Monitor.CanPlayIntro)
            {
                SetState(GameState.Intro);
                Monitor.PlayIntro(OnIntroFinished);
            }
            else
            {
                SetState(GameState.Warmup);
            }
        }

        void OnIntroFinished()
        {
            if (State == GameState.Intro) SetState(GameState.Warmup);
        }

        void StartRound()
        {
            InsertedThisRound = 0;
            Points = 0;
            CenterDrops = SideDrops = LostDrops = 0;
        }

        /// <summary>Enter キー：演出のスキップ → 次のラウンド → ショップ → 次のエリア と進める。</summary>
        public void Advance()
        {
            switch (State)
            {
                case GameState.Intro:
                    Monitor.SkipIntro();
                    break;
                case GameState.Cleared:
                    if (IsLastRoundOfArea) SetState(GameState.Shop);
                    else NextRound();
                    break;
                case GameState.Shop:
                    NextArea();
                    break;
            }
        }

        void NextRound()
        {
            RoundIndex++;
            RoundNumber++;
            StartRound();
            Monitor.ShowRound(RoundIndex);
            SetState(GameState.Playing);
        }

        void NextArea()
        {
            if (AreaIndex + 1 >= areas.Length)
            {
                SetState(GameState.Finished);
                return;
            }
            AreaIndex++;
            RoundIndex = 0;
            RoundNumber++;
            StartRound();
            BeginArea();
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
                    // リセット直後だけ、初期メダルが落ち着くまで待つ（演出中の時間も含めて数える）
                    if (Time.time - resetTime >= rules.warmupSeconds) SetState(GameState.Playing);
                    break;

                case GameState.Playing:
                    if (rules.freePlay) break;
                    if (Points >= CurrentQuota) SetState(GameState.Cleared);
                    else if (!HasInsertsLeft) SetState(GameState.Grace);
                    break;

                case GameState.Grace:
                    if (rules.freePlay || HasInsertsLeft) SetState(GameState.Playing); // 払い戻しで復活した等
                    else if (Points >= CurrentQuota) SetState(GameState.Cleared);
                    else if (StateTime >= rules.graceSeconds) SetState(GameState.Failed);
                    break;
            }

            if (InputCompat.Down(GameKey.Reset)) ResetGame();
            if (InputCompat.Down(GameKey.NextRound)) Advance();
            if (InputCompat.Down(GameKey.AddMedals)) AddTestMedals(50);
            if (InputCompat.Down(GameKey.PausePusher)) Pusher.paused = !Pusher.paused;
            if (InputCompat.Down(GameKey.ToggleCamera) && State != GameState.Intro) { topView = !topView; ApplyCameraPreset(); }
        }

        // ================================================================
        // エリアとラウンドの初期値
        // ================================================================

        /// <summary>エリア1（仮）：150pt → 200pt → ボス300pt。タイトルとサムネイルは仮。</summary>
        static AreaDefinition[] DefaultAreas() => new[]
        {
            new AreaDefinition
            {
                title = "エリア1 ～仮タイトル～",
                rounds = new[]
                {
                    new RoundDefinition("ラウンド1（仮）", 150, false, new Color(0.20f, 0.45f, 0.50f)),
                    new RoundDefinition("ラウンド2（仮）", 200, false, new Color(0.25f, 0.50f, 0.30f)),
                    new RoundDefinition("ボス（仮）", 300, true, new Color(0.60f, 0.15f, 0.15f)),
                }
            }
        };

        /// <summary>Inspector でエリアやラウンドが空にされていても動くようにする。</summary>
        void EnsureAreas()
        {
            if (areas == null || areas.Length == 0) areas = DefaultAreas();
            foreach (var a in areas)
                if (a.rounds == null || a.rounds.Length == 0) a.rounds = DefaultAreas()[0].rounds;
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

            if (State != GameState.Warmup && State != GameState.Intro)
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

        /// <summary>
        /// 開始時のメダルを、下段（プッシャーの前の盤面）と上段（プッシャーの上面）にランダムに置く。
        /// 少し浮かせて置き、準備中（Warmup）の間に自然に崩れて落ち着かせる。
        /// </summary>
        void Prefill()
        {
            float d = tuning.medalDiameter;
            float r = d * 0.5f;
            float margin = d * 0.05f;

            // 開始時のプッシャー前面の位置（ResetPhase で決めた位置と同じ）
            float startT = Mathf.Clamp01(rules.startPusherPosition);
            float pusherFront = layout.pusherRetractZ - tuning.pusherStroke * startT;

            // 下段：盤面の前端 〜 プッシャーの前面
            float half = BoardHalfWidth - r - margin;
            ScatterMedals(rules.prefillMedals, half,
                layout.floorFrontZ + r + margin, pusherFront - r - margin, 0f);

            // 上段：プッシャーの上面のうち、奥の壁より手前の部分（前の縁から少しはみ出してもよい）
            float upperHalf = BoardHalfWidth - layout.pusherSideGap - r - margin;
            ScatterMedals(rules.prefillUpperMedals, upperHalf,
                pusherFront + r * 0.8f, BackWallFrontZ - r - margin, layout.pusherHeight);
        }

        /// <summary>
        /// 範囲の中にメダルをランダムに置く。同じ高さの段の中では重ならない場所を探し、
        /// 何回か探して空きが見つからなければ1つ上の段に置く（だから自然な山になる）。
        /// </summary>
        void ScatterMedals(int count, float halfWidth, float zMin, float zMax, float baseY)
        {
            if (count <= 0) return;
            if (zMax < zMin) zMin = zMax = (zMin + zMax) * 0.5f; // 範囲が狭すぎる時は1列に並べる

            float d = tuning.medalDiameter;
            float th = tuning.medalThickness;
            float minDist = d * 1.02f;
            float layerHeight = th + d * 0.25f; // 傾けたメダル同士が上下で重ならない高さ
            const int triesPerLayer = 10;
            const int maxLayers = 40;

            var layers = new List<List<Vector2>>();
            for (int i = 0; i < count; i++)
            {
                for (int layer = 0; layer < maxLayers; layer++)
                {
                    if (layer == layers.Count) layers.Add(new List<Vector2>());
                    var placed = layers[layer];
                    bool found = false;
                    Vector2 p = default;
                    for (int t = 0; t < triesPerLayer && !found; t++)
                    {
                        p = new Vector2(Random.Range(-halfWidth, halfWidth), Random.Range(zMin, zMax));
                        found = true;
                        foreach (var q in placed)
                        {
                            if ((p - q).sqrMagnitude < minDist * minDist) { found = false; break; }
                        }
                    }
                    if (!found) continue;

                    placed.Add(p);
                    float y = baseY + th * 0.5f + d * 0.02f + layer * layerHeight + Random.Range(0f, d * 0.05f);
                    var rot = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), Random.Range(-8f, 8f));
                    Pool.Spawn(new Vector3(p.x, y, p.y), rot);
                    break;
                }
            }
        }

        // ================================================================
        // 寸法の一括入力（Inspector でコンポーネントを右クリック）
        // ================================================================

        /// <summary>
        /// 盤面モデル v3（MedalGame_Models_v3）を Scale Factor 10 で取り込んだ時の寸法を入れる。
        /// メダルは参考作品に合わせて大きめ（直径0.45 = 盤面の幅に約10枚並ぶ）にしている。
        /// </summary>
        [ContextMenu("盤面モデル v3 の寸法を入れる")]
        void ApplyModelV3Preset()
        {
            RecordUndo("盤面モデル v3 の寸法を入れる");
            boardSource = BoardSource.Scene;

            layout.boardWidth = 4.8f;          // 盤面の幅 480mm
            layout.floorFrontZ = -1.1f;        // 盤面の前端
            layout.wallHeight = 0.45f;
            layout.wallThickness = 0.12f;
            layout.pusherHeight = 0.32f;
            layout.pusherDepth = 1.6f;
            layout.pusherRetractZ = 1.6f;      // プッシャー前面（一番奥）。奥の壁の0.1手前まで戻り、上のメダルを壁で掻き落とす
            layout.pusherSideGap = 0.02f;
            layout.backWallOffset = 0.1f;      // 奥の壁の前面 Z = 1.6 + 0.1 = 1.7（モデルの壁の位置）
            layout.backWallGap = 0.02f;
            layout.centerHoleWidth = 3.0f;     // 仕切りの中心が X=±1.5
            layout.holeTopY = -0.4f;
            layout.holeBottomY = -0.78f;
            layout.holeDepth = 0.43f;
            layout.launcherHeight = 0.7f;
            layout.launcherZFromWall = 0.3f;   // 奥の壁から0.3手前（Z=1.4）に落とす。メダルの半径0.225より大きくして壁に当てない
            // 盤面を正面の少し上からのぞき込む視点
            layout.cameraPosition = new Vector3(0f, 2.071f, -3.197f);
            layout.cameraRotation = new Vector3(17.25f, 0f, 0f);
            layout.cameraFieldOfView = 64f;
            layout.cameraLensDistortion = 0.3f;

            tuning.pusherStroke = 0.6f;        // 前面が Z=1.6〜1.0 の間を往復する
            tuning.medalDiameter = 0.45f;      // 盤面の幅4.8に約10枚並ぶ大きさ
            tuning.medalThickness = 0.045f;    // 直径の約1/10（実物のメダルと同じ比率）
            tuning.colliderSegments = 12;
            tuning.physicsHz = 100;            // 薄いメダルは更新回数を多めにしないと震えやすい
            tuning.solverIterations = 10;
            tuning.contactOffset = 0.004f;     // 初期値 0.01 だと薄いメダルが浮いて重なる。厚さの1割弱にする
            tuning.maxDepenetrationVelocity = 1f;
            tuning.sleepThreshold = 0.001f;

            rules.launchSpeed = 3.5f;
            rules.launchUpSpeed = 0.3f;
            rules.launchForwardSpeed = 0.1f;
        }

        /// <summary>メダルの見た目に、盤面モデル一式の Medal.obj を入れる（エディタでのみ動く）。</summary>
        [ContextMenu("メダルの見た目をモデルにする")]
        void AssignMedalModel()
        {
#if UNITY_EDITOR
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("Medal t:Model"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileName(path) != "Medal.obj") continue;
                RecordUndo("メダルの見た目をモデルにする");
                visuals.medalModel = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Debug.Log($"[MedalPusher] メダルの見た目に {path} を入れました。");
                return;
            }
            Debug.LogWarning("[MedalPusher] Medal.obj が見つかりません。Visuals の Medal Model に直接ドラッグしてください。");
#endif
        }

        /// <summary>コードで作る仮の盤面（最初の試作）の寸法に戻す。</summary>
        [ContextMenu("試作の仮盤面に戻す")]
        void ApplyGeneratedPreset()
        {
            RecordUndo("試作の仮盤面に戻す");
            boardSource = BoardSource.Generate;
            layout = new BoardLayout();
            tuning = new PhysicsTuning();
            var defaults = new RuleSettings();
            rules.launchSpeed = defaults.launchSpeed;
            rules.launchUpSpeed = defaults.launchUpSpeed;
            rules.launchForwardSpeed = defaults.launchForwardSpeed;
        }

        void RecordUndo(string label)
        {
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, label);
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>負荷テスト用。盤面の上空からばらまく。</summary>
        public void AddTestMedals(int count)
        {
            float d = tuning.medalDiameter;
            float half = BoardHalfWidth - d * 0.6f;
            float zMin = layout.floorFrontZ + d * 0.8f;
            float zMax = layout.pusherRetractZ - d * 0.6f;
            float y0 = layout.launcherHeight + d * 0.5f;
            for (int i = 0; i < count; i++)
            {
                var pos = new Vector3(Random.Range(-half, half), y0 + i * tuning.medalThickness * 0.25f, Random.Range(zMin, zMax));
                var rot = Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f));
                Pool.Spawn(pos, rot);
            }
        }
    }
}
