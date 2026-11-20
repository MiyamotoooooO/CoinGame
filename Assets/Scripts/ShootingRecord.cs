using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N009 射的「ブルズアイ」：操作・狙撃型。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・盤面の上（前の円盤のあたり）に、木の的（白黒の輪と、赤・黄色のまん中）が立ち、左右に動く
    /// ・照準（白い輪と4本のとげ）は、マウスのカーソルが指している所（的と同じ面の上）について動く
    /// ・盤面の手前のまん中に、大きな銃。いつも照準の方を向く
    /// ・メダルを投入すると、そのメダルが発射口からではなく銃から照準へ飛ぶ（メダルが弾。投入の枚数はいつも通り数える）
    ///   当たったメダルは的ではね返って盤面に落ちる。はずれたメダルはそのまま奥へ飛んで盤面に落ちる
    /// ・的に1回当てると、的が後ろへ倒れて消え、メダルが出る。少しすると次の的が下から出てくる
    ///   ふつうの的：当たった場所で枚数が変わる（まん中ほど多い。ringMedals：黄色 5・赤 4・白 3・その外 2・いちばん外 1）
    ///   金の的　　：goldChance の確率で出る。小さくて速い。当てるとメダル goldMedals 枚
    ///              goldSeconds 秒のうちに当てないと、下へ引っこんで（メダルは出ない）次の的になる
    /// ・的・照準・文字・当たった時のエフェクトは、モニターより手前に映す（モニターに隠れない）
    /// ・盤面の外へメダルが飛ばないように、照準は盤面の中（左右のかべの内側・高さは的のまわり）までしか動かない
    ///   （はずれたメダルは、いつも置いてある見えないかべ BoardFence に当たって盤面の中に落ちる）
    /// ・当たった時のエフェクト：光の輪が広がる・的のかけらが飛び散る・当たった所が白く光る（金の的は大きく、金色）
    /// ・発動中は、マウスを置いても物の説明（HoverTooltip）を出さない（狙うじゃまにならないように）
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class ShootingRecord : RecordScriptBase
    {
        public static ShootingRecord Instance { get; private set; }

        [Header("弾（投入したメダル）")]
        [Tooltip("メダルが銃から的まで飛ぶ速さ（メートル/秒）")]
        [Range(1f, 20f)] public float shotSpeed = 6f;
        [Tooltip("飛んでいるメダルを見張る最大の秒数")]
        [Range(0.5f, 5f)] public float shotLife = 2f;

        [Header("的")]
        [Tooltip("的の半径（メダルの直径に対する倍率）")]
        [Range(0.5f, 4f)] public float targetRadius = 1.3f;
        [Tooltip("的のまん中の高さ（ワールドの Y）")]
        public float targetY = 0.588f;
        [Tooltip("的の奥行き（ワールドの Z。盤面を前後に伸ばす前の値。伸ばした倍率は自動でかける）")]
        public float targetZ = 0.4f;
        [Tooltip("的の向き（ワールドの回転。0,0,0 で手前を向く）")]
        public Vector3 targetRotation = Vector3.zero;
        [Tooltip("的と照準をモニターより手前に映す（モニターと一緒に上に描くレイヤーに置く）")]
        public bool drawOverMonitor = true;
        [Tooltip("的が左右に動く広さ（盤面の幅の半分に対する割合）")]
        [Range(0f, 1f)] public float targetSwing = 0.7f;
        [Tooltip("ふつうの的が左右に1往復する秒数")]
        [Range(1f, 20f)] public float swingSeconds = 6f;
        [Tooltip("当ててから、次の的が出てくるまでの秒数")]
        [Range(0.2f, 5f)] public float respawnDelay = 1f;
        [Tooltip("ふつうの的に当てた時のメダルの枚数（まん中から外へ：黄色・赤・白・その外の2本・いちばん外の2本）")]
        public int[] ringMedals = { 5, 4, 3, 2, 1 };
        [Tooltip("的が後ろへ倒れて消えるまでの秒数")]
        [Range(0.2f, 2f)] public float fallSeconds = 0.7f;

        [Header("金の的")]
        [Tooltip("次の的が金の的になる確率")]
        [Range(0f, 1f)] public float goldChance = 0.15f;
        [Tooltip("金の的に当てた時のメダルの枚数")]
        [Range(0, 60)] public int goldMedals = 20;
        [Tooltip("金の的の速さ（ふつうの的に対する倍率）")]
        [Range(1f, 5f)] public float goldSpeed = 2.5f;
        [Tooltip("金の的の大きさ（ふつうの的に対する倍率）")]
        [Range(0.3f, 1f)] public float goldSize = 0.75f;
        [Tooltip("金の的が出ている秒数（当てないと引っこむ）")]
        [Range(2f, 30f)] public float goldSeconds = 8f;

        [Header("盤面の外へ飛ばさない")]
        [Tooltip("照準が動ける高さ（的のまん中から上と下へ。的の半径に対する倍率）")]
        [Range(1f, 4f)] public float aimHeightRange = 1.6f;

        [Header("当たった時のエフェクト")]
        [Range(0, 40)] public int chipCount = 14;
        [Range(0.1f, 2f)] public float fxSeconds = 0.6f;

        [Header("銃")]
        [Tooltip("銃の大きさ（倍率）")]
        [Range(0.5f, 4f)] public float gunScale = 1.8f;
        [Tooltip("銃の場所のずれ（手前のふちのまん中から。メダルの直径に対する倍率）")]
        public Vector3 gunOffset = new Vector3(0f, 1.3f, -0.4f);

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip shotSound;
        public AudioClip hitSound;
        public AudioClip goldSound;
        public AudioClip goldAppearSound;

        protected override string DefaultKey => "Shooting";
        protected override string ScreenTitle => "SHOOTING";
        protected override string ScreenSubtitle => "ブルズアイ";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<ShootingRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed()
        {
            if (Instance == this) Instance = null;
            HoverTooltip.Blockers.Remove(this);
        }

        const string PrizeKind = "ShootPrize";
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color SparkColor = new Color32(0xff, 0xe8, 0x9a, 0xff);
        static readonly Color RingWhite = new Color32(0xf2, 0xee, 0xe4, 0xff);
        static readonly Color RingDark = new Color32(0x3a, 0x26, 0x18, 0xff);
        static readonly Color RingRed = new Color32(0xf0, 0x40, 0x20, 0xff);
        static readonly Color RingYellow = new Color32(0xff, 0xe0, 0x20, 0xff);
        static readonly Color GoldLight = new Color32(0xff, 0xe2, 0x6a, 0xff);
        static readonly Color GoldDark = new Color32(0xb0, 0x78, 0x10, 0xff);
        static readonly Color GoldRed = new Color32(0xff, 0x80, 0x20, 0xff);
        static readonly Color Wood = new Color32(0x6a, 0x40, 0x24, 0xff);
        static readonly Color Metal = new Color32(0x50, 0x54, 0x60, 0xff);
        static readonly Color MuzzleGold = new Color32(0xd8, 0xa8, 0x48, 0xff);

        // 的の輪（外から。半径は的の半径に対する割合）
        static readonly float[] RingRadii = { 1f, 0.92f, 0.76f, 0.62f, 0.48f, 0.3f, 0.17f };
        static readonly Color[] NormalColors = { RingWhite, RingDark, RingWhite, RingDark, RingWhite, RingRed, RingYellow };
        // ringMedals の区切り（的の半径に対する割合。これより内側ならその枚数）
        static readonly float[] ZoneEdges = { 0.17f, 0.3f, 0.48f, 0.76f, 1f };
        static readonly Color[] GoldColors = { GoldLight, GoldDark, GoldLight, GoldDark, GoldLight, GoldRed, Color.white };

        // 的の様子
        enum TargetState { Rising, Live, Falling, Sinking, Waiting }
        TargetState state = TargetState.Waiting;
        float stateT;          // 今の様子になってからの割合・秒
        bool isGold;
        float goldLeft;        // 金の的の残り秒数
        float recoil, targetFlash, swingPhase;

        Transform gun, barrel, normalTarget, goldTarget, reticle;
        Transform target;      // いま出ている方（ふつう・金）
        BoardText goldLabel;
        Material reticleMat;
        Vector3 aimPoint;      // 照準（的と同じ面の上）
        bool aimOnTarget;
        // 飛んでいるメダル（弾）と、前のフレームで的の面のどちら側にいたか
        readonly List<(Medal medal, float side, float life)> shots = new List<(Medal, float, float)>();
        Transform fxRoot;
        // エフェクトのかけら（動き・寿命）
        class FxPiece { public Transform tr; public Vector3 vel, spin, scale; public float life, max; public bool ring, fall; }
        readonly List<FxPiece> fx = new List<FxPiece>();
        Material fxWhite, fxGold, fxWood, fxRed;
        AudioClip madeShot, madeHit, madeGold, madeGoldAppear;

        Vector3 GunPos => new Vector3(0f, 0f, FrontZ) + gunOffset * D;
        Vector3 Muzzle => barrel != null ? barrel.TransformPoint(new Vector3(0f, 0f, D * 0.9f)) : GunPos;
        float TargetR => D * targetRadius * (isGold ? goldSize : 1f);
        bool TargetHittable => state == TargetState.Live || state == TargetState.Rising;

        Vector3 TargetCenter(float t)
        {
            float x = Mathf.Sin(swingPhase) * HalfW * targetSwing;   // 位相は UpdateBoard で進める
            float down = 0f;
            if (state == TargetState.Rising) down = 1f - Ease.OutCubic(stateT);
            else if (state == TargetState.Sinking) down = Ease.InCubic(stateT);
            return new Vector3(x, targetY - down * D * 5f + Mathf.Sin(t * 2f) * D * 0.05f, TargetZ);
        }

        Quaternion TargetRotation() => Quaternion.Euler(targetRotation);

        /// <summary>的のまん中から支柱の下はしまで（的の半径に対する割合。倒れる時の軸）</summary>
        const float PostBottom = 1.55f;

        /// <summary>的と照準を、モニターより手前に映す（drawOverMonitor を外すと、ふつうの映り方）</summary>
        void PutOverMonitor(Transform root)
        {
            if (drawOverMonitor) DrawOverMonitor(root);
        }

        /// <summary>的を1つ作る（輪を重ねる。円柱を立てて、面を手前に向ける。内側の輪ほど少し手前）</summary>
        Transform BuildTarget(string name, Color[] colors, float glow)
        {
            var root = new GameObject(name).transform;
            root.SetParent(Board, false);
            for (int i = 0; i < RingRadii.Length; i++)
            {
                var ring = Shape(PrimitiveType.Cylinder, $"Ring{i}", root, new Vector3(0f, 0f, -0.006f * i), new Vector3(RingRadii[i] * 2f, 0.01f, RingRadii[i] * 2f), Glow(colors[i], i >= 5 ? glow + 0.45f : glow));
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            var rim = Shape(PrimitiveType.Cylinder, "Rim", root, new Vector3(0f, 0f, 0.008f), new Vector3(2.06f, 0.01f, 2.06f), Glow(new Color(0.05f, 0.04f, 0.04f), 0f));
            rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Cube("Post", root, new Vector3(0f, -1.25f, 0.02f), new Vector3(0.12f, 0.6f, 0.06f), Glow(Wood, 0.1f));
            PutOverMonitor(root);
            root.gameObject.SetActive(false);
            return root;
        }

        protected override void BuildBoard()
        {
            normalTarget = BuildTarget("Target", NormalColors, 0.15f);
            goldTarget = BuildTarget("GoldTarget", GoldColors, 0.9f);
            target = normalTarget;

            // 金の的の上の文字
            goldLabel = BoardLabel("GoldLabel", Board, Vector3.zero, "", D * 0.5f, Gold);
            goldLabel.gameObject.SetActive(false);

            // 照準（白い輪と、4本のとげ）
            reticle = new GameObject("Reticle").transform;
            reticle.SetParent(Board, false);
            reticleMat = Glow(Color.white, 2f);
            const int seg = 16;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var c = Cube($"Ring{i}", reticle, new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f), new Vector3(0.21f, 0.04f, 0.02f), reticleMat);
                c.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
            }
            for (int i = 0; i < 4; i++)
            {
                var spike = Cube($"Spike{i}", reticle, Vector3.zero, new Vector3(0.36f, 0.08f, 0.02f), reticleMat);
                float a = i * 90f;
                spike.transform.localRotation = Quaternion.Euler(0f, 0f, a);
                spike.transform.localPosition = Quaternion.Euler(0f, 0f, a) * new Vector3(0.62f, 0f, 0f);
            }
            Cube("DotH", reticle, Vector3.zero, new Vector3(0.18f, 0.03f, 0.02f), reticleMat);
            Cube("DotV", reticle, Vector3.zero, new Vector3(0.03f, 0.18f, 0.02f), reticleMat);
            PutOverMonitor(reticle);

            // 銃（手前のまん中。大きめ）
            gun = new GameObject("Gun").transform;
            gun.SetParent(Board, false);
            var wood = Glow(Wood, 0.1f);
            var metal = Glow(Metal, 0.2f);
            Cube("Stand", gun, new Vector3(0f, -D * 0.55f, 0f), new Vector3(D * 0.25f, D * 1.1f, D * 0.25f), metal);
            barrel = new GameObject("Aim").transform;
            barrel.SetParent(gun, false);
            Cube("Stock", barrel, new Vector3(0f, -D * 0.05f, -D * 0.5f), new Vector3(D * 0.32f, D * 0.36f, D * 0.8f), wood);
            Shape(PrimitiveType.Cylinder, "Barrel", barrel, new Vector3(0f, 0f, D * 0.35f), new Vector3(D * 0.16f, D * 0.5f, D * 0.16f), metal)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shape(PrimitiveType.Cylinder, "MuzzleRing", barrel, new Vector3(0f, 0f, D * 0.86f), new Vector3(D * 0.2f, D * 0.06f, D * 0.2f), Glow(MuzzleGold, 0.5f))
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            MedalMarks.RegisterEffect(PrizeKind, (m, type) => { });   // 景品のメダル（見た目だけ）

            // エフェクトの入れ物と色
            fxRoot = new GameObject("ShootFx").transform;
            fxRoot.SetParent(Board, false);
            fxWhite = Glow(Color.white, 2.5f);
            fxGold = Glow(Gold, 2.5f);
            fxWood = Glow(RingDark, 0.3f);
            fxRed = Glow(RingRed, 1f);
        }


        protected override void OnBegin()
        {
            shots.Clear();
            isGold = false;   // 最初はいつもふつうの的
            target = normalTarget;
            swingPhase = 0f;
            SetState(TargetState.Rising);
            HoverTooltip.Blockers.Add(this);
        }

        protected override void OnEnd()
        {
            shots.Clear();
            HoverTooltip.Blockers.Remove(this);
        }

        void SetState(TargetState s)
        {
            state = s;
            stateT = 0f;
        }

        /// <summary>次の的を出す（まれに金の的）</summary>
        void NextTarget()
        {
            isGold = Random.value < goldChance;
            target = isGold ? goldTarget : normalTarget;
            swingPhase = Random.Range(0f, Mathf.PI * 2f);
            goldLeft = goldSeconds;
            SetState(TargetState.Rising);
            if (isGold)
            {
                Play(goldAppearSound, ref madeGoldAppear, () => RecordSynth.Notes("ShootGoldIn", new[] { RecordSynth.Note(12), RecordSynth.Note(19), RecordSynth.Note(24) }, 0.07f, 0.3f, 0.3f, RecordSynth.Wave.Square));
                BoardPop("金の的！", Gold, new Vector3(0f, targetY + D * 2f, TargetZ), D * 0.9f);
            }
        }

        protected override bool KeepBoardVisible() => state == TargetState.Falling || state == TargetState.Sinking;

        protected override void Tick(float dt, bool running)
        {
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 5f);
            targetFlash = Mathf.MoveTowards(targetFlash, 0f, dt * 4f);
            WatchShots(dt);
            UpdateFx(dt);

            switch (state)
            {
                case TargetState.Rising:
                    stateT += dt / 0.5f;
                    if (stateT >= 1f) SetState(TargetState.Live);
                    break;
                case TargetState.Live:
                    if (isGold && running)
                    {
                        goldLeft -= dt;
                        if (goldLeft <= 0f) SetState(TargetState.Sinking);
                    }
                    break;
                case TargetState.Falling:
                    stateT += dt / Mathf.Max(0.1f, fallSeconds);
                    if (stateT >= 1f) SetState(TargetState.Waiting);
                    break;
                case TargetState.Sinking:
                    stateT += dt / 0.8f;
                    if (stateT >= 1f) SetState(TargetState.Waiting);
                    break;
                case TargetState.Waiting:
                    stateT += dt;
                    if (Active && stateT >= respawnDelay) NextTarget();
                    break;
            }
        }

        /// <summary>照準：マウスのカーソルから伸びる線が、的の面と交わる所</summary>
        void UpdateAim()
        {
            if (target == null || !MouseRay(out var ray)) return;
            var plane = new Plane(target.forward, TargetCenter(Time.time));
            if (!plane.Raycast(ray, out float dist)) return;
            aimPoint = ClampAim(ray.GetPoint(dist));
            aimOnTarget = TargetHittable && (target.position - aimPoint).magnitude <= TargetR;
        }

        /// <summary>照準を盤面の中に収める（左右は盤面のかべの内側、高さは的のまわり）</summary>
        Vector3 ClampAim(Vector3 p)
        {
            float edge = HalfW - D * 0.7f;
            float range = D * targetRadius * aimHeightRange;
            p.x = Mathf.Clamp(p.x, -edge, edge);
            p.y = Mathf.Clamp(p.y, targetY - range, targetY + range);
            return p;
        }

        /// <summary>投入したメダルを、銃から照準へ飛ばす（重力も入れて、ちょうど照準を通るように）</summary>
        protected override void OnInsert(Medal medal)
        {
            if (medal == null || medal.Body == null || target == null) return;
            UpdateAim();
            Vector3 from = Muzzle;
            Vector3 to = aimPoint;
            float time = Mathf.Max(0.08f, Vector3.Distance(from, to) / Mathf.Max(0.5f, shotSpeed));
            Vector3 v = (to - from) / time - 0.5f * Physics.gravity * time;

            medal.transform.SetPositionAndRotation(from, Quaternion.LookRotation(v) * Quaternion.Euler(90f, 0f, 0f));
            medal.Body.position = from;
            medal.Body.rotation = medal.transform.rotation;
            medal.Body.velocity = v;
            medal.Body.angularVelocity = v.normalized * 20f;
            shots.Add((medal, SideOf(from), shotLife));

            recoil = 1f;
            Play(shotSound, ref madeShot, () => RecordSynth.Noise("ShootShot", 0.25f, 0.6f, 0.002f, 0.06f, 1f, 6));
            SparkAt(from, SparkColor, 10);
        }

        /// <summary>的の面のどちら側か（手前が負）</summary>
        float SideOf(Vector3 p) => Vector3.Dot(p - new Vector3(0f, 0f, TargetZ), target.forward);

        /// <summary>的の奥行き（盤面を前後に伸ばした倍率に合わせたもの）</summary>
        float TargetZ => targetZ * DepthScale;

        /// <summary>飛んでいるメダルが的の面を通ったら、当たったかを見る</summary>
        void WatchShots(float dt)
        {
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var (m, side, life) = shots[i];
                life -= dt;
                if (m == null || !m.IsActiveInPool || m.Body == null || life <= 0f || target == null)
                {
                    shots.RemoveAt(i);
                    continue;
                }
                float now = SideOf(m.transform.position);
                if (side < 0f && now >= 0f)
                {
                    shots.RemoveAt(i);
                    // 面を通った点（速さから戻して求める）
                    Vector3 p = m.transform.position;
                    Vector3 v = m.Body.velocity;
                    float along = Vector3.Dot(v, target.forward);
                    if (along > 0.0001f) p -= v * (now / along);
                    if (TargetHittable && (p - target.position).magnitude <= TargetR)
                    {
                        // 当たり：的ではね返って、手前の盤面に落ちる
                        m.Body.velocity = new Vector3(v.x * 0.2f, -0.5f, -Mathf.Abs(along) * 0.25f);
                        HitTarget(p, (p - target.position).magnitude / Mathf.Max(0.0001f, TargetR));
                    }
                    continue;
                }
                shots[i] = (m, now, life);
            }
        }

        /// <summary>ふつうの的で、当たった場所（まん中からの割合）のメダルの枚数</summary>
        int MedalsAt(float r01)
        {
            if (ringMedals == null || ringMedals.Length == 0) return 1;
            for (int i = 0; i < ZoneEdges.Length && i < ringMedals.Length; i++)
                if (r01 <= ZoneEdges[i]) return ringMedals[i];
            return ringMedals[ringMedals.Length - 1];
        }

        /// <summary>当たり：的が後ろへ倒れて消え、メダルが出る</summary>
        void HitTarget(Vector3 hitPoint, float r01)
        {
            targetFlash = 1f;
            var at = target.position;
            int count = Budget(isGold ? goldMedals : MedalsAt(r01));
            if (isGold)
            {
                Play(goldSound, ref madeGold, () => RecordSynth.Notes("ShootGold", new[] { RecordSynth.Note(12), RecordSynth.Note(16), RecordSynth.Note(19), RecordSynth.Note(24) }, 0.06f, 0.4f, 0.3f, RecordSynth.Wave.Square));
                SparkAt(at, Gold, 80);
                SparkAt(at, Color.white, 40);
                Shake(0.05f, 0.4f);
                Speaker(1f, 14, Gold);
                BoardPop($"金の的！ +{count}枚", Gold, at + Vector3.up * TargetR * 1.4f, D * 1.1f);
            }
            else
            {
                Play(hitSound, ref madeHit, () => RecordSynth.Notes("ShootHit", new[] { RecordSynth.Note(19), RecordSynth.Note(24) }, 0.05f, 0.2f, 0.3f, RecordSynth.Wave.Square));
                SparkAt(at, SparkColor, 30);
                Shake(0.02f, 0.15f);
                BoardPop($"HIT! +{count}枚", SparkColor, at + Vector3.up * TargetR * 1.4f, D * 0.8f);
            }
            SparkAt(hitPoint, Color.white, 10);
            HitFx(at, hitPoint, isGold);

            // メダルを的の場所から飛び散らせる
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * 0.6f);
                var m = SpawnFree(at + Random.insideUnitSphere * D * 0.4f, dir * D * Random.Range(1f, 3f) + Vector3.up * D * Random.Range(0.5f, 2f));
                if (m != null) MedalMarks.Set(m, PrizeKind, Gold, 1f);
            }
            SetState(TargetState.Falling);
        }

        // ================================================================
        // 当たった時のエフェクト（どれも当たり判定なし。モニターより手前に映す）
        // ================================================================

        /// <summary>光の輪が広がる・的のかけらが飛び散る・当たった所が白く光る</summary>
        void HitFx(Vector3 center, Vector3 hitPoint, bool gold)
        {
            var rot = TargetRotation();
            float big = gold ? 1.6f : 1f;
            float r = TargetR;
            // 光の輪（金の的は2つ）
            FxRing(center, rot, r, r * 2.6f * big, gold ? fxGold : fxWhite, fxSeconds);
            if (gold) FxRing(center, rot, r * 0.5f, r * 4f, fxWhite, fxSeconds * 1.4f);
            // 当たった所の光（ひし形の星）
            for (int i = 0; i < 2; i++)
            {
                var star = Cube("Flash", fxRoot, Vector3.zero, Vector3.one, gold ? fxGold : fxWhite).transform;
                star.SetPositionAndRotation(hitPoint - rot * Vector3.forward * D * 0.1f, rot * Quaternion.Euler(0f, 0f, 45f + i * 45f));
                AddFx(star, Vector3.zero, Vector3.zero, new Vector3(D * 0.9f * big, D * 0.12f * big, D * 0.02f), fxSeconds * 0.5f, false, false);
            }
            // 的のかけら（白・こげ茶・赤。金の的は金色も）
            int n = Mathf.RoundToInt(chipCount * big);
            for (int i = 0; i < n; i++)
            {
                Material m = gold && i % 2 == 0 ? fxGold : (i % 3 == 0 ? fxWood : (i % 5 == 0 ? fxRed : fxWhite));
                var chip = Cube("Chip", fxRoot, Vector3.zero, Vector3.one, m).transform;
                Vector2 c2 = Random.insideUnitCircle * r * 0.8f;
                chip.SetPositionAndRotation(center + rot * new Vector3(c2.x, c2.y, -D * 0.05f), Random.rotation);
                Vector3 outDir = rot * new Vector3(c2.x, c2.y, 0f).normalized;
                Vector3 vel = (outDir * Random.Range(0.6f, 1.6f) + rot * Vector3.back * Random.Range(0.3f, 1f) + Vector3.up * Random.Range(0.5f, 1.5f)) * big;
                float sz = D * Random.Range(0.08f, 0.16f);
                AddFx(chip, vel, Random.insideUnitSphere * 720f, new Vector3(sz, sz * 0.4f, sz), fxSeconds * Random.Range(0.8f, 1.4f), false, true);
            }
        }

        /// <summary>的の面に、広がりながら細くなる光の輪を出す</summary>
        void FxRing(Vector3 center, Quaternion rot, float from, float to, Material m, float seconds)
        {
            var ring = new GameObject("Ring").transform;
            ring.SetParent(fxRoot, false);
            ring.SetPositionAndRotation(center - rot * Vector3.forward * D * 0.08f, rot);
            const int seg = 24;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var c = Cube($"Seg{i}", ring, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f), new Vector3(0.28f, 0.06f, 0.02f), m);
                c.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
            }
            AddFx(ring, Vector3.zero, Vector3.zero, new Vector3(from, to, 1f), seconds, true, false);
        }

        void AddFx(Transform tr, Vector3 vel, Vector3 spin, Vector3 scale, float seconds, bool ring, bool fall)
        {
            DrawOverMonitor(tr);
            tr.localScale = ring ? Vector3.one * scale.x : scale;
            fx.Add(new FxPiece { tr = tr, vel = vel, spin = spin, scale = scale, life = 0f, max = Mathf.Max(0.05f, seconds), ring = ring, fall = fall });
        }

        void UpdateFx(float dt)
        {
            for (int i = fx.Count - 1; i >= 0; i--)
            {
                var f = fx[i];
                f.life += dt;
                float k = f.life / f.max;
                if (f.tr == null || k >= 1f)
                {
                    if (f.tr != null) Destroy(f.tr.gameObject);
                    fx.RemoveAt(i);
                    continue;
                }
                if (f.ring)
                {
                    // 輪：広がる（scale.x → scale.y）。だんだん細く
                    float rr = Mathf.Lerp(f.scale.x, f.scale.y, Ease.OutCubic(k));
                    f.tr.localScale = new Vector3(rr, rr, 1f - k);
                    continue;
                }
                if (f.fall) f.vel += Physics.gravity * dt;
                f.tr.position += f.vel * dt;
                f.tr.Rotate(f.spin * dt, Space.Self);
                float shrink = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
                f.tr.localScale = f.scale * (f.fall ? shrink : Ease.OutBack(Mathf.Min(1f, k * 4f)) * (1f - k));
            }
        }

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            bool show = BoardShown > 0.01f;

            // 的：左右に動く（金の的は速い）。出る時は下から上がり、当たると後ろへ倒れて消える
            float speed = isGold ? goldSpeed : 1f;
            swingPhase += dt * Mathf.PI * 2f / Mathf.Max(0.5f, swingSeconds) * speed;
            bool targetOn = show && state != TargetState.Waiting;
            if (normalTarget.gameObject.activeSelf != (targetOn && !isGold)) normalTarget.gameObject.SetActive(targetOn && !isGold);
            if (goldTarget.gameObject.activeSelf != (targetOn && isGold)) goldTarget.gameObject.SetActive(targetOn && isGold);
            if (targetOn)
            {
                float s = TargetR * Ease.OutBack(BoardShown) * (1f + 0.08f * targetFlash);
                if (state == TargetState.Falling)
                {
                    // 支柱の下を軸にして、後ろへ倒れる（前の6割）。倒れたら小さくなって消える（後の4割）
                    float fall = Ease.InCubic(Mathf.Clamp01(stateT / 0.6f));
                    float shrink = 1f - Ease.InCubic(Mathf.Clamp01((stateT - 0.6f) / 0.4f));
                    var rot = TargetRotation() * Quaternion.Euler(90f * fall, 0f, 0f);
                    var pivot = TargetCenter(t) + TargetRotation() * Vector3.down * (PostBottom * s);
                    s *= shrink;
                    target.SetPositionAndRotation(pivot + rot * Vector3.up * (PostBottom * s), rot);
                }
                else target.SetPositionAndRotation(TargetCenter(t), TargetRotation());
                target.localScale = new Vector3(s, s, s);
                if (isGold && state == TargetState.Live && Mathf.Repeat(t * 8f, 1f) < 0.2f)
                    SparkAt(target.position + Random.insideUnitSphere * TargetR, Gold, 1);
            }

            // 金の的の残り秒数
            bool goldOn = targetOn && isGold && state == TargetState.Live;
            if (goldLabel.gameObject.activeSelf != goldOn) goldLabel.gameObject.SetActive(goldOn);
            if (goldOn)
            {
                goldLabel.transform.position = target.position + Vector3.up * (TargetR + D * 0.45f);
                goldLabel.text = $"金の的 +{goldMedals}枚  のこり{Mathf.CeilToInt(goldLeft)}秒";
            }

            // 照準
            UpdateAim();
            reticle.gameObject.SetActive(Active);
            if (Active)
            {
                reticle.SetPositionAndRotation(aimPoint - target.forward * D * 0.05f, TargetRotation());
                float rs = D * 0.7f * (1f + 0.15f * Mathf.Sin(t * 6f));
                reticle.localScale = new Vector3(rs, rs, rs);
                SetGlow(reticleMat, aimOnTarget ? new Color(1f, 0.35f, 0.3f) : Color.white, 2f);
            }

            // 銃：手前のまん中。照準の方を向く。撃つと少し跳ねる
            gun.gameObject.SetActive(show);
            gun.position = GunPos + Vector3.down * D * 2f * (1f - Ease.OutCubic(BoardShown));
            gun.localScale = Vector3.one * gunScale;
            Vector3 dir = aimPoint - gun.position;
            if (dir.sqrMagnitude > 0.0001f)
                barrel.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(-12f * recoil, 0f, 0f);
            barrel.localPosition = new Vector3(0f, 0f, -D * 0.15f * recoil);
        }
    }
}
