using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N014 城壁「籠城戦」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：下段（プッシャーの前の盤面）の左右に、石の城壁がせり上がる（はしに塔、塔の上で旗がはためく）。
    ///   城壁の上には、銃（小さい砲身）と大砲（大きい砲身と車輪の台）が並び、下段のメダルをねらって、休まず撃ちまくる
    ///   　銃：光る弾を速く撃つ。当たると火花
    ///   　大砲：黒い砲弾が山なりに飛ぶ。当たると爆発して煙が出る（まわりのメダルもまとめて動く）
    /// ・効果：弾がメダルに当たると、そのメダルが中央の穴の方へはじかれて寄っていく
    ///   （中央の穴から遠いメダルほど、ねらわれやすい）
    /// ・城壁・銃・大砲・弾は見た目だけ（当たり判定なし）。メダルを動かすのは弾が当たった時だけ
    /// ・モニター：使わない（いつものラウンドの画面のまま）
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class CastleRecord : RecordScriptBase
    {
        public static CastleRecord Instance { get; private set; }

        [Header("城壁（メダルの直径に対する倍率）")]
        [Range(0.3f, 5f)] public float wallHeight = 2.2f;
        [Range(0.1f, 3f)] public float wallThickness = 0.85f;
        [Tooltip("銃と大砲の大きさ（1 がもとの大きさ）")]
        [Range(0.5f, 4f)] public float weaponSize = 1.8f;
        [Tooltip("城壁の長さ（下段の奥行きに対する割合）")]
        [Range(0.3f, 1f)] public float wallLength = 0.95f;

        [Header("銃")]
        [Tooltip("片側の城壁にある銃の数")]
        [Range(0, 10)] public int gunsPerSide = 4;
        [Tooltip("1つの銃が撃つ間隔（秒）。この間でばらつく")]
        public Vector2 gunInterval = new Vector2(0.18f, 0.45f);
        [Tooltip("弾の速さ（メダルの直径 / 秒）")]
        [Range(5f, 80f)] public float bulletSpeed = 32f;
        [Tooltip("弾が当たったメダルを、中央の穴の方へはじく強さ（メダルの直径 / 秒）")]
        [Range(0f, 40f)] public float bulletPush = 9f;

        [Header("大砲")]
        [Tooltip("片側の城壁にある大砲の数")]
        [Range(0, 6)] public int cannonsPerSide = 2;
        [Tooltip("1つの大砲が撃つ間隔（秒）。この間でばらつく")]
        public Vector2 cannonInterval = new Vector2(1.2f, 2.2f);
        [Tooltip("砲弾が当たった時に、まわりのメダルも動く範囲（メダルの直径に対する倍率）")]
        [Range(0.3f, 5f)] public float cannonRadius = 1.4f;
        [Tooltip("砲弾が当たったメダルを、中央の穴の方へはじく強さ（メダルの直径 / 秒）")]
        [Range(0f, 50f)] public float cannonPush = 13f;

        [Header("ねらい")]
        [Tooltip("中央の穴からこれより近いメダルは、ねらわない（メダルの直径に対する倍率）")]
        [Range(0f, 5f)] public float ignoreNearHole = 1f;
        [Tooltip("はじく時に、少し上へ浮かせる強さ（メダルの直径 / 秒）")]
        [Range(0f, 10f)] public float hitLift = 1.8f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip riseSound;
        public AudioClip gunSound;
        public AudioClip cannonSound;
        public AudioClip boomSound;

        protected override string DefaultKey => "Castle";
        protected override string ScreenTitle => "CASTLE";
        protected override string ScreenSubtitle => "籠城戦";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;   // モニターはいつもの画面のまま

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<CastleRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Stone = new Color32(0x8e, 0x88, 0x7c, 0xff);
        static readonly Color StoneDark = new Color32(0x5e, 0x59, 0x52, 0xff);
        static readonly Color Roof = new Color32(0x3a, 0x4f, 0x9a, 0xff);
        static readonly Color Banner = new Color32(0xd9, 0x3a, 0x3a, 0xff);
        static readonly Color Iron = new Color32(0x26, 0x28, 0x2e, 0xff);
        static readonly Color Wood = new Color32(0x7a, 0x50, 0x2c, 0xff);
        static readonly Color Tracer = new Color32(0xff, 0xd8, 0x6a, 0xff);
        static readonly Color Flash = new Color32(0xff, 0xb0, 0x40, 0xff);

        // ---- 盤面の場所 ----
        Vector3 holePos;     // 中央の穴（手前のふちのまん中）
        float cz, len, h, thick;

        // ---- 部品 ----
        class Gun
        {
            public Transform pivot, barrel;
            public bool cannon;
            public int side;
            public float timer, recoil;
            public Vector3 aim;
        }

        readonly List<Gun> guns = new List<Gun>();
        readonly Transform[] walls = new Transform[2];
        readonly List<Transform> flags = new List<Transform>();
        ParticleSystem sparks, smoke, dust;
        Material tracerMat, flashMat, ballMat;

        class Shot
        {
            public Transform t;
            public bool cannon;
            public Medal target;
            public Vector3 from, last, aim;
            public float age, life, arc;
        }

        readonly List<Shot> shots = new List<Shot>();
        readonly Stack<Transform> bulletPool = new Stack<Transform>();
        readonly Stack<Transform> ballPool = new Stack<Transform>();

        class Muzzle { public Transform t; public float age; }
        readonly List<Muzzle> muzzles = new List<Muzzle>();
        readonly Stack<Transform> muzzlePool = new Stack<Transform>();

        readonly List<Medal> lower = new List<Medal>();
        readonly List<Medal> near = new List<Medal>();
        float lastGunSound;
        AudioClip madeRise, madeGun, madeCannon, madeBoom;

        // ================================================================
        // 盤面の部品
        // ================================================================

        static Material Mat(Color c, float metal = 0.05f, float smooth = 0.35f, float glow = 0.08f)
        {
            var m = ProtoAssets.MakeMaterial(c, metal, smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            return m;
        }

        protected override void BuildBoard()
        {
            float depth = Mathf.Max(D, PusherFrontZ - FrontZ);
            cz = (FrontZ + PusherFrontZ) * 0.5f;
            len = depth * wallLength;
            h = D * wallHeight;
            thick = D * wallThickness;
            holePos = new Vector3(0f, 0f, FrontZ);

            var stone = Mat(Stone);
            var stoneDark = Mat(StoneDark);
            var roof = Mat(Roof, 0.1f, 0.5f, 0.15f);
            var banner = Mat(Banner, 0f, 0.3f, 0.45f);
            var iron = Mat(Iron, 0.85f, 0.75f, 0.05f);
            var wood = Mat(Wood, 0f, 0.3f, 0.06f);
            var gold = Mat(new Color(1f, 0.8f, 0.3f), 0.9f, 0.8f, 0.3f);

            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                int inward = -side;   // 中央の向き（+1 なら +X）
                var w = new GameObject(side < 0 ? "WallLeft" : "WallRight").transform;
                w.SetParent(Board, false);
                w.position = new Vector3(side * (HalfW - thick * 0.5f), 0f, cz);
                walls[i] = w;

                // 壁
                Cube("Wall", w, new Vector3(0f, h * 0.5f, 0f), new Vector3(thick, h, len), stone);
                // 石の積み目（横の線と、互いちがいの縦の線）
                int rows = 4;
                for (int r = 1; r < rows; r++)
                    Cube("Course", w, new Vector3(inward * thick * 0.51f, h * r / rows, 0f), new Vector3(D * 0.02f, D * 0.025f, len), stoneDark);
                for (int r = 0; r < rows; r++)
                {
                    int cols = 7;
                    for (int c = 0; c < cols; c++)
                    {
                        float z = -len * 0.5f + len * (c + (r % 2 == 0 ? 0.5f : 1f)) / cols;
                        if (z > len * 0.5f - D * 0.05f) continue;
                        Cube("Joint", w, new Vector3(inward * thick * 0.51f, h * (r + 0.5f) / rows, z), new Vector3(D * 0.02f, h / rows, D * 0.025f), stoneDark);
                    }
                }
                // 上の歩くところと、ぎざぎざ（外側に並べる。銃と大砲は内側に置く）
                Cube("Walk", w, new Vector3(0f, h + D * 0.02f, 0f), new Vector3(thick * 1.1f, D * 0.05f, len), stoneDark);
                int merlons = 9;
                for (int k = 0; k < merlons; k++)
                {
                    float z = -len * 0.5f + len * (k + 0.5f) / merlons;
                    Cube("Merlon", w, new Vector3(-inward * thick * 0.3f, h + h * 0.12f, z), new Vector3(thick * 0.4f, h * 0.24f, len / merlons * 0.6f), stone);
                }
                // 両はしの塔（とんがり屋根と旗）
                for (int e = -1; e <= 1; e += 2)
                {
                    float tz = e * len * 0.5f;
                    float tr = thick * 0.95f, th = h * 1.45f;
                    Shape(PrimitiveType.Cylinder, "Tower", w, new Vector3(0f, th * 0.5f, tz), new Vector3(tr * 2f, th * 0.5f, tr * 2f), stone);
                    Shape(PrimitiveType.Cylinder, "TowerTop", w, new Vector3(0f, th + D * 0.04f, tz), new Vector3(tr * 2.25f, D * 0.04f, tr * 2.25f), stoneDark);
                    RecordMeshKit.Make("Roof", RecordMeshKit.ConeSector(tr * 1.2f, h * 0.9f, 0f, 360f, 16), roof, w, new Vector3(0f, th + D * 0.08f, tz), Vector3.one, Vector3.zero);
                    Shape(PrimitiveType.Sphere, "Knob", w, new Vector3(0f, th + D * 0.08f + h * 0.9f, tz), Vector3.one * D * 0.09f, gold);
                    Shape(PrimitiveType.Cylinder, "Pole", w, new Vector3(0f, th + h * 0.9f + D * 0.35f, tz), new Vector3(D * 0.03f, D * 0.3f, D * 0.03f), iron);
                    var fp = new GameObject("FlagPivot").transform;
                    fp.SetParent(w, false);
                    fp.localPosition = new Vector3(0f, th + h * 0.9f + D * 0.55f, tz);
                    Cube("Flag", fp, new Vector3(0f, 0f, -e * D * 0.22f), new Vector3(D * 0.02f, D * 0.24f, D * 0.42f), banner);
                    flags.Add(fp);
                }

                // 大砲（大きい砲身・車輪の台）と、銃（小さい砲身）を、壁の上の内側に並べる
                int total = gunsPerSide + cannonsPerSide;
                // 大砲を、銃の間に同じくらいの間かくで入れる
                var cannonSlots = new HashSet<int>();
                for (int j = 0; j < cannonsPerSide; j++)
                    cannonSlots.Add(Mathf.Clamp(Mathf.FloorToInt((j + 0.5f) * total / cannonsPerSide), 0, total - 1));
                for (int k = 0; k < total; k++)
                {
                    bool cannon = cannonSlots.Contains(k);

                    float z = -len * 0.42f + len * 0.84f * (k + 0.5f) / total;
                    var g = new Gun { side = side, cannon = cannon, timer = Random.Range(0.2f, 1.2f) };
                    var basePos = new Vector3(inward * thick * 0.15f, h + D * 0.05f, z);
                    // 銃・大砲ごとの入れ物（weaponSize で大きさを変える）
                    var wr = Pivot("Weapon", w, basePos);
                    wr.localScale = Vector3.one * weaponSize;
                    if (cannon)
                    {
                        // 木の台と車輪
                        Cube("Carriage", wr, new Vector3(0f, D * 0.1f, 0f), new Vector3(D * 0.32f, D * 0.16f, D * 0.36f), wood);
                        for (int s = -1; s <= 1; s += 2)
                        {
                            var wh = Shape(PrimitiveType.Cylinder, "Wheel", wr, new Vector3(0f, D * 0.1f, s * D * 0.21f), new Vector3(D * 0.26f, D * 0.03f, D * 0.26f), wood);
                            wh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        }
                        g.pivot = Pivot("CannonPivot", wr, new Vector3(0f, D * 0.26f, 0f));
                        g.barrel = Pivot("Barrel", g.pivot, Vector3.zero);
                        Shape(PrimitiveType.Cylinder, "Tube", g.barrel, new Vector3(0f, 0f, D * 0.25f), new Vector3(D * 0.2f, D * 0.36f, D * 0.2f), iron).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        Shape(PrimitiveType.Sphere, "Breech", g.barrel, new Vector3(0f, 0f, -D * 0.1f), Vector3.one * D * 0.24f, iron);
                        Shape(PrimitiveType.Cylinder, "Lip", g.barrel, new Vector3(0f, 0f, D * 0.6f), new Vector3(D * 0.25f, D * 0.03f, D * 0.25f), iron).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        Shape(PrimitiveType.Cylinder, "Band", g.barrel, new Vector3(0f, 0f, D * 0.1f), new Vector3(D * 0.23f, D * 0.025f, D * 0.23f), gold).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    }
                    else
                    {
                        // 小さな台と、細い砲身
                        Cube("Mount", wr, new Vector3(0f, D * 0.05f, 0f), new Vector3(D * 0.14f, D * 0.1f, D * 0.14f), iron);
                        g.pivot = Pivot("GunPivot", wr, new Vector3(0f, D * 0.15f, 0f));
                        g.barrel = Pivot("Barrel", g.pivot, Vector3.zero);
                        Cube("Body", g.barrel, new Vector3(0f, 0f, D * 0.02f), new Vector3(D * 0.1f, D * 0.1f, D * 0.18f), iron);
                        Shape(PrimitiveType.Cylinder, "Tube", g.barrel, new Vector3(0f, 0f, D * 0.22f), new Vector3(D * 0.05f, D * 0.14f, D * 0.05f), iron).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    }
                    g.pivot.rotation = Quaternion.LookRotation(new Vector3(inward, -0.3f, 0f));
                    guns.Add(g);
                }
            }

            tracerMat = RecordMeshKit.Additive(RecordMeshKit.SoftDot, Tracer);
            flashMat = RecordMeshKit.Additive(RecordMeshKit.SoftDot, Flash);
            ballMat = Mat(new Color(0.08f, 0.08f, 0.09f), 0.6f, 0.8f, 0f);
            sparks = Particles("Sparks", Board, Vector3.up, D * 3f, D * 0.06f, 0.4f, 0.8f, 70f);
            smoke = Particles("Smoke", Board, Vector3.up, D * 0.8f, D * 0.6f, 1.2f, -0.05f, 60f);
            dust = Particles("Dust", Board, Vector3.up, D * 1f, D * 0.4f, 1f, 0f, 80f);
        }

        static Transform Pivot(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        // ================================================================
        // 流れ
        // ================================================================

        protected override void OnBegin()
        {
            Play(riseSound, ref madeRise, () => RecordSynth.Noise("CastleRise", 0.9f, 0.04f, 0.2f, 0.5f, 1f, 5));
            foreach (var g in guns) g.timer = Random.Range(0.9f, 1.8f);   // せり上がってから撃ち始める
            for (int i = 0; i < 2; i++)
                for (int k = 0; k < 6; k++)
                    Emit(dust, walls[i].position + new Vector3(0f, 0f, Random.Range(-len, len) * 0.5f), new Color(0.8f, 0.75f, 0.65f, 0.7f), 3);
        }

        protected override void OnEnd()
        {
            ClearShots();
        }

        protected override void OnStateChanged(GameState state)
        {
            bool scoring = state == GameState.Playing || state == GameState.Grace;
            if (!scoring) ClearShots();
        }

        /// <summary>下段（プッシャーの前の床の上）にあるメダルか</summary>
        bool IsLower(Medal m)
        {
            if (m == null || m.Consumed || m.Body == null) return false;
            Vector3 p = m.transform.position;
            return p.y > -0.2f && p.y < Layout.pusherHeight * 0.6f
                && p.z > FrontZ - D * 0.2f && p.z < Layout.pusherRetractZ
                && Mathf.Abs(p.x) < HalfW;
        }

        /// <summary>ねらうメダルをえらぶ（中央の穴から遠いメダルほど、えらばれやすい）</summary>
        Medal PickTarget()
        {
            lower.Clear();
            foreach (var m in BoardMedals) if (IsLower(m)) lower.Add(m);
            if (lower.Count == 0) return null;
            Medal best = null;
            float bestScore = -1f;
            for (int i = 0; i < 6; i++)
            {
                var m = lower[Random.Range(0, lower.Count)];
                Vector3 d = m.transform.position - holePos;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < D * ignoreNearHole) continue;
                float score = dist * Random.Range(0.6f, 1f);
                if (score > bestScore) { bestScore = score; best = m; }
            }
            return best;
        }

        protected override void Tick(float dt, bool running)
        {
            if (!running || BoardShown < 0.95f) return;
            foreach (var g in guns)
            {
                g.recoil = Mathf.MoveTowards(g.recoil, 0f, dt * (g.cannon ? 2.5f : 8f));
                g.timer -= dt;
                if (g.timer > 0f) continue;
                Vector2 iv = g.cannon ? cannonInterval : gunInterval;
                g.timer = Random.Range(Mathf.Min(iv.x, iv.y), Mathf.Max(iv.x, iv.y));
                var target = PickTarget();
                if (target == null) continue;
                Fire(g, target);
            }
        }

        // ================================================================
        // 撃つ
        // ================================================================

        void Fire(Gun g, Medal target)
        {
            if (shots.Count > 120) return;
            Vector3 aim = target.transform.position;
            g.aim = aim;
            // 砲身をねらう向きへ（大砲は山なりなので、少し上へ向ける）
            Vector3 dir = aim - g.pivot.position;
            if (g.cannon) dir.y += dir.magnitude * 0.45f;
            if (dir.sqrMagnitude > 1e-6f) g.pivot.rotation = Quaternion.LookRotation(dir.normalized);
            g.recoil = 1f;

            Vector3 muzzle = g.barrel.TransformPoint(new Vector3(0f, 0f, g.cannon ? D * 0.65f : D * 0.36f));
            var s = new Shot
            {
                cannon = g.cannon,
                target = target,
                from = muzzle,
                last = muzzle,
                aim = aim,
                age = 0f,
            };
            float dist = Vector3.Distance(muzzle, aim);
            if (g.cannon)
            {
                s.life = 0.35f + dist / (D * 9f);
                s.arc = D * 0.6f + dist * 0.35f;
                s.t = ballPool.Count > 0 ? ballPool.Pop() : MakeBall();
            }
            else
            {
                s.life = Mathf.Max(0.04f, dist / (D * bulletSpeed));
                s.t = bulletPool.Count > 0 ? bulletPool.Pop() : MakeBullet();
            }
            s.t.gameObject.SetActive(true);
            s.t.position = muzzle;
            shots.Add(s);

            // 発射の光と煙
            ShowMuzzle(muzzle, (g.cannon ? D * 0.9f : D * 0.45f) * weaponSize);
            if (g.cannon)
            {
                Emit(smoke, muzzle, new Color(0.85f, 0.85f, 0.85f, 0.7f), 8);
                Emit(sparks, muzzle, Flash, 10);
                Shake(0.015f, 0.12f);
                Play(cannonSound, ref madeCannon, () => RecordSynth.Thud("CastleCannon", 65f, 0.5f, 0.9f), 0.7f);
            }
            else
            {
                Emit(sparks, muzzle, Tracer, 2);
                if (Time.time - lastGunSound > 0.05f)
                {
                    lastGunSound = Time.time;
                    Play(gunSound, ref madeGun, () => RecordSynth.Noise("CastleGun", 0.07f, 0.7f, 0.002f, 0.03f, 0.6f, 11), 0.35f);
                }
            }
        }

        Transform MakeBullet()
        {
            var go = Shape(PrimitiveType.Quad, "Bullet", Board, Vector3.zero, Vector3.one, tracerMat);
            return go.transform;
        }

        Transform MakeBall()
        {
            var go = Shape(PrimitiveType.Sphere, "CannonBall", Board, Vector3.zero, Vector3.one * D * 0.2f * weaponSize, ballMat);
            return go.transform;
        }

        void ShowMuzzle(Vector3 at, float size)
        {
            Transform t = muzzlePool.Count > 0 ? muzzlePool.Pop() : Shape(PrimitiveType.Quad, "Muzzle", Board, Vector3.zero, Vector3.one, flashMat).transform;
            t.gameObject.SetActive(true);
            t.position = at;
            t.localScale = Vector3.one * size;
            muzzles.Add(new Muzzle { t = t, age = 0f });
        }

        void ClearShots()
        {
            foreach (var s in shots) Recycle(s);
            shots.Clear();
        }

        void Recycle(Shot s)
        {
            if (s.t == null) return;
            s.t.gameObject.SetActive(false);
            (s.cannon ? ballPool : bulletPool).Push(s.t);
        }

        /// <summary>【効果】当たったメダルを、中央の穴の方へはじく</summary>
        void PushToHole(Medal m, float speed)
        {
            if (m == null || m.Consumed || m.Body == null) return;
            Vector3 d = holePos - m.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return;
            Push(m, d.normalized * D * speed + Vector3.up * D * hitLift);
        }

        void Hit(Shot s, Vector3 at)
        {
            if (s.cannon)
            {
                // 爆発：当たったメダルと、まわりのメダルをまとめて中央の穴の方へ
                foreach (var m in MedalsNear(at, D * cannonRadius, near))
                {
                    if (!IsLower(m)) continue;
                    Vector3 d = m.transform.position - at;
                    d.y = 0f;
                    float k = 1f - Mathf.Clamp01(d.magnitude / (D * cannonRadius));
                    PushToHole(m, cannonPush * (0.5f + 0.5f * k));
                }
                Emit(smoke, at, new Color(0.7f, 0.68f, 0.65f, 0.8f), 14);
                Emit(sparks, at, Flash, 24);
                ShowMuzzle(at + Vector3.up * D * 0.2f, D * 1.6f);
                Shake(0.025f, 0.2f);
                Play(boomSound, ref madeBoom, () => RecordSynth.Noise("CastleBoom", 0.5f, 0.12f, 0.005f, 0.3f, 0.9f, 3), 0.6f);
            }
            else
            {
                if (s.target != null && IsLower(s.target)) PushToHole(s.target, bulletPush);
                Emit(sparks, at, Tracer, 5);
            }
        }

        // ================================================================
        // 見た目（毎フレーム）
        // ================================================================

        protected override bool KeepBoardVisible() => shots.Count > 0 || (smoke != null && smoke.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            // 城壁がせり上がる・沈む
            for (int i = 0; i < 2; i++)
            {
                var p = walls[i].position;
                walls[i].position = new Vector3(p.x, -(h * 1.6f + D * 1.5f) * (1f - Mathf.SmoothStep(0f, 1f, BoardShown)), p.z);
            }
            for (int i = 0; i < flags.Count; i++)
                flags[i].localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 6f + i * 1.3f) * 25f, 0f);

            // 砲身の反動
            foreach (var g in guns)
                g.barrel.localPosition = new Vector3(0f, 0f, -g.recoil * D * (g.cannon ? 0.15f : 0.06f));

            var cam = Camera.main;
            Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;

            // 弾
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                s.age += dt;
                // ねらったメダルが動いたら、追いかける
                if (s.target != null && !s.target.Consumed && s.target.gameObject.activeInHierarchy) s.aim = s.target.transform.position;
                float k = Mathf.Clamp01(s.age / s.life);
                Vector3 pos = Vector3.Lerp(s.from, s.aim, k);
                if (s.cannon) pos += Vector3.up * Mathf.Sin(k * Mathf.PI) * s.arc;
                if (s.t != null)
                {
                    s.t.position = pos;
                    if (!s.cannon)
                    {
                        // 光る弾：飛ぶ向きに細長く、いつもカメラの方を向く
                        Vector3 v = pos - s.last;
                        s.t.rotation = face;
                        float stretch = Mathf.Max(D * 0.25f, v.magnitude * 1.5f);
                        s.t.localScale = new Vector3(D * 0.12f, D * 0.12f, 1f);
                        if (v.sqrMagnitude > 1e-8f && cam != null)
                        {
                            Vector3 sv = cam.transform.InverseTransformDirection(v);
                            float ang = Mathf.Atan2(sv.y, sv.x) * Mathf.Rad2Deg;
                            s.t.rotation = face * Quaternion.Euler(0f, 0f, ang);
                            s.t.localScale = new Vector3(stretch, D * 0.1f, 1f);
                        }
                    }
                    else if (Random.value < 0.5f) Emit(smoke, pos, new Color(0.8f, 0.8f, 0.8f, 0.35f), 1);
                }
                s.last = pos;
                if (k >= 1f)
                {
                    Hit(s, s.aim);
                    Recycle(s);
                    shots.RemoveAt(i);
                }
            }

            // 発射の光（すぐ消える）
            for (int i = muzzles.Count - 1; i >= 0; i--)
            {
                var m = muzzles[i];
                m.age += dt;
                float k = m.age / 0.09f;
                if (m.t != null)
                {
                    m.t.rotation = face;
                    m.t.localScale *= 1f + dt * 6f;
                }
                if (k >= 1f)
                {
                    if (m.t != null) { m.t.gameObject.SetActive(false); muzzlePool.Push(m.t); }
                    muzzles.RemoveAt(i);
                }
            }
        }
    }
}
