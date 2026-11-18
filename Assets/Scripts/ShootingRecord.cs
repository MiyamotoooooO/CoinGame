using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N009 射的「ブルズアイ」：操作・狙撃型。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・ふだん：
    ///   盤面の右手前にコルク銃。照準（赤い輪）は、マウスのカーソルが指している盤面の床について動く。銃は照準の方を向く
    ///   メダルが穴（中央・端）に落ちるたびに、弾が1発たまる（最大 maxBullets。銃の横のコルクの数で見える）
    ///   メダルを投入した瞬間に、弾があれば照準へ1発撃つ：
    ///     照準が UFO の影（床の上の青い輪）に重なっていれば命中。UFO から景品のメダルが prizeMedals 枚落ちる
    ///     そうでなければ、照準のまわりのメダルを中央の穴の方へはじく（はじいて落ちた分は数えておき、UFO 撃墜の時に戻す）
    ///     金の的（targetInterval 秒ごとに targetSeconds 秒出る）に重なっていれば BULLSEYE：+bullseyeBonus pt、強くはじく
    ///   盤面の上を UFO がゆらゆら飛び回り、床に影を落とす
    /// ・大技「UFO 撃墜」：UFO に hitsForBoss 回命中すると発動
    ///   UFO が盤面のまん中の上に来て大きくなる。bossSeconds 秒のうちに bossHp 回当てると撃墜：
    ///   UFO が爆発して、ためこんでいたメダル bossMedals 枚＋はじいて落ちた分（最大 pushedCap 枚）が飛び散る
    ///   時間内に撃墜できないと、UFO は逃げていく（景品のメダルを escapeMedals 枚だけ落とす）
    ///   大技が始まった時に、弾を bossReload 発足す
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class ShootingRecord : RecordScriptBase
    {
        public static ShootingRecord Instance { get; private set; }

        [Header("弾")]
        [Range(1, 20)] public int maxBullets = 6;
        [Tooltip("はじく半径（メダルの直径に対する倍率）")]
        [Range(0.5f, 4f)] public float blastRadius = 1.4f;
        [Tooltip("はじく強さ")]
        [Range(0f, 20f)] public float blastStrength = 4f;

        [Header("金の的")]
        [Range(2f, 30f)] public float targetInterval = 10f;
        [Range(1f, 15f)] public float targetSeconds = 5f;
        [Tooltip("的の半径（メダルの直径に対する倍率）")]
        [Range(0.3f, 3f)] public float targetRadius = 0.8f;
        [Range(0, 200)] public int bullseyeBonus = 30;

        [Header("UFO")]
        [Tooltip("UFO の影の半径（命中の広さ。メダルの直径に対する倍率）")]
        [Range(0.3f, 4f)] public float ufoRadius = 1.2f;
        [Tooltip("命中した時に落ちる景品のメダル")]
        [Range(0, 10)] public int prizeMedals = 2;

        [Header("大技：UFO 撃墜")]
        [Range(1, 20)] public int hitsForBoss = 5;
        [Range(1, 20)] public int bossHp = 3;
        [Range(3f, 30f)] public float bossSeconds = 12f;
        [Range(0, 60)] public int bossMedals = 20;
        [Tooltip("はじいて落ちた分として足す最大の枚数")]
        [Range(0, 60)] public int pushedCap = 20;
        [Range(0, 20)] public int escapeMedals = 5;
        [Range(0, 10)] public int bossReload = 3;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip shotSound;
        public AudioClip bullseyeSound;
        public AudioClip reloadSound;
        public AudioClip hitSound;
        public AudioClip explodeSound;

        protected override string DefaultKey => "Shooting";
        protected override string ScreenTitle => "SHOOTING";
        protected override string ScreenSubtitle => "ブルズアイ";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<ShootingRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        const string PrizeKind = "ShootPrize";
        static readonly Color Red = new Color32(0xff, 0x3c, 0x3c, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color SparkColor = new Color32(0xff, 0xe8, 0x9a, 0xff);
        static readonly Color UfoBlue = new Color32(0x4c, 0xc8, 0xff, 0xff);
        static readonly Color Wood = new Color32(0x6a, 0x40, 0x24, 0xff);
        static readonly Color Metal = new Color32(0x50, 0x54, 0x60, 0xff);
        static readonly Color Cork = new Color32(0xd8, 0xa8, 0x68, 0xff);

        int bullets, hits, bossHits;
        float targetTimer, nextTarget, recoil, ufoFlash;
        float bossTimer = -1f;   // 大技の残り（負なら無し）
        float bossGrow;
        float escapeT = -1f, explodeT = -1f;
        Vector3 aim, target;
        bool TargetUp => targetTimer > 0f;
        bool Boss => bossTimer > 0f;

        Transform gun, barrel, ufo;
        readonly GameObject[] corks = new GameObject[20];
        GameObject aimRing, targetDisc, shadow;
        Material aimMaterial, targetMaterial, shadowMaterial, ufoLights;
        float floorY;
        AudioClip madeShot, madeBull, madeReload, madeHit, madeExplode;

        float AimZMin => FrontZ + D * 0.6f;
        float AimZMax => PusherFrontZ - D * 0.3f;
        Vector3 GunPos => new Vector3(HalfW * 0.82f, D * 1.1f, FrontZ - D * 0.2f);
        Vector3 Muzzle => barrel != null ? barrel.TransformPoint(new Vector3(0f, 0f, D * 0.9f)) : GunPos;

        Vector3 UfoPos(float t)
        {
            if (Boss || escapeT >= 0f || explodeT >= 0f)
            {
                var c = new Vector3(0f, SkyY + D * 0.5f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.55f));
                if (escapeT >= 0f) c += new Vector3(escapeT * HalfW * 2f, escapeT * D * 6f, 0f);
                return c + new Vector3(Mathf.Sin(t * 3f) * D * 0.3f, Mathf.Sin(t * 5f) * D * 0.1f, 0f);
            }
            float x = Mathf.Sin(t * 0.55f) * HalfW * 0.75f;
            float z = Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f + 0.25f * Mathf.Sin(t * 0.83f));
            return new Vector3(x, SkyY + Mathf.Sin(t * 2.2f) * D * 0.2f, z);
        }
        Vector3 UfoShadow => new Vector3(ufo.position.x, floorY + D * 0.02f, ufo.position.z);
        float HitRadius => D * ufoRadius * (1f + bossGrow * 0.8f);

        protected override void BuildBoard()
        {
            floorY = FloorY(0f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f));

            // 照準・金の的・UFO の影
            aimMaterial = Glow(Red, 2f);
            aimRing = Shape(PrimitiveType.Cylinder, "Aim", Board, Vector3.zero, new Vector3(D * 0.9f, D * 0.004f, D * 0.9f), aimMaterial);
            targetMaterial = Glow(Gold, 2f);
            targetDisc = Shape(PrimitiveType.Cylinder, "Target", Board, Vector3.zero, new Vector3(D * targetRadius * 2f, D * 0.004f, D * targetRadius * 2f), targetMaterial);
            shadowMaterial = Glow(UfoBlue, 1f);
            shadow = Shape(PrimitiveType.Cylinder, "UfoShadow", Board, Vector3.zero, new Vector3(1f, D * 0.003f, 1f), shadowMaterial);

            // コルク銃（右手前）
            gun = new GameObject("CorkGun").transform;
            gun.SetParent(Board, false);
            gun.position = GunPos;
            var wood = Glow(Wood, 0.1f);
            var metal = Glow(Metal, 0.2f);
            Cube("Stand", gun, new Vector3(0f, -D * 0.55f, 0f), new Vector3(D * 0.25f, D * 1.1f, D * 0.25f), metal);
            barrel = new GameObject("Aim").transform;
            barrel.SetParent(gun, false);
            Cube("Stock", barrel, new Vector3(0f, -D * 0.05f, -D * 0.5f), new Vector3(D * 0.32f, D * 0.36f, D * 0.8f), wood);
            Shape(PrimitiveType.Cylinder, "Barrel", barrel, new Vector3(0f, 0f, D * 0.35f), new Vector3(D * 0.16f, D * 0.5f, D * 0.16f), metal)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shape(PrimitiveType.Cylinder, "CorkTip", barrel, new Vector3(0f, 0f, D * 0.86f), new Vector3(D * 0.13f, D * 0.06f, D * 0.13f), Glow(Cork, 0.3f))
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // 弾（コルク）：銃の台の横に並べる
            var corkMat = Glow(Cork, 0.6f);
            for (int i = 0; i < Mathf.Min(maxBullets, corks.Length); i++)
            {
                corks[i] = Shape(PrimitiveType.Cylinder, $"Cork{i}", gun, new Vector3(-D * 0.35f, -D * 0.2f - i * D * 0.15f, 0f), new Vector3(D * 0.12f, D * 0.05f, D * 0.12f), corkMat);
            }

            // UFO
            ufo = new GameObject("Ufo").transform;
            ufo.SetParent(Board, false);
            Shape(PrimitiveType.Sphere, "Saucer", ufo, Vector3.zero, new Vector3(D * 2.2f, D * 0.35f, D * 2.2f), Glow(new Color(0.75f, 0.78f, 0.85f), 0.3f));
            Shape(PrimitiveType.Sphere, "Dome", ufo, new Vector3(0f, D * 0.2f, 0f), new Vector3(D * 0.9f, D * 0.6f, D * 0.9f), Glow(new Color(0.5f, 0.95f, 0.8f), 0.8f));
            ufoLights = Glow(UfoBlue, 2.5f);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Shape(PrimitiveType.Sphere, $"Light{i}", ufo, new Vector3(Mathf.Cos(a) * D * 0.95f, -D * 0.02f, Mathf.Sin(a) * D * 0.95f), Vector3.one * D * 0.14f, ufoLights);
            }

            MedalMarks.RegisterEffect(PrizeKind, (m, type) => { });   // 景品のメダル（見た目だけ）
        }

        protected override void OnBegin()
        {
            bullets = 1;
            hits = 0;
            bossTimer = -1f;
            bossGrow = 0f;
            escapeT = -1f;
            explodeT = -1f;
            targetTimer = 0f;
            nextTarget = 3f;
            aim = new Vector3(0f, floorY + D * 0.01f, Mathf.Lerp(AimZMin, AimZMax, 0.4f));
            TakePushed(0);
        }

        protected override void OnEnd()
        {
            bossTimer = -1f;
        }

        protected override void Tick(float dt, bool running)
        {
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 5f);
            ufoFlash = Mathf.MoveTowards(ufoFlash, 0f, dt * 4f);
            bossGrow = Mathf.MoveTowards(bossGrow, Boss ? 1f : 0f, dt * 1.5f);
            if (escapeT >= 0f) { escapeT += dt / 1.5f; if (escapeT > 1f) escapeT = -1f; }
            if (explodeT >= 0f) { explodeT += dt / 0.8f; if (explodeT > 1f) explodeT = -1f; }

            // 照準：マウスのカーソルについて動く
            if (MouseOnFloor(floorY, out var p))
                aim = new Vector3(Mathf.Clamp(p.x, -HalfW * 0.9f, HalfW * 0.9f), floorY + D * 0.01f, Mathf.Clamp(p.z, AimZMin, AimZMax));

            if (!running) return;
            if (Boss)
            {
                bossTimer -= dt;
                if (bossTimer <= 0f) BossEscape();
            }
            if (TargetUp) targetTimer -= dt;
            else
            {
                nextTarget -= dt;
                if (nextTarget <= 0f)
                {
                    nextTarget = targetInterval;
                    targetTimer = targetSeconds;
                    target = new Vector3(Random.Range(-HalfW * 0.7f, HalfW * 0.7f), floorY + D * 0.01f, Random.Range(AimZMin, AimZMax));
                }
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            NoteDropForPush();
            if (bullets >= maxBullets) return;
            bullets++;
            Play(reloadSound, ref madeReload, () => RecordSynth.Notes("ShootReload", new[] { 1200f, 900f }, 0.04f, 0.06f, 0.2f, RecordSynth.Wave.Square), 0.5f);
        }

        protected override void OnInsert(Medal medal)
        {
            if (!IsScoring || bullets <= 0 || explodeT >= 0f) return;
            bullets--;
            recoil = 1f;
            Play(shotSound, ref madeShot, () => RecordSynth.Noise("ShootShot", 0.25f, 0.6f, 0.002f, 0.06f, 1f, 6));
            SparkAt(Muzzle, SparkColor, 10);

            // UFO に命中？
            Vector3 flat = new Vector3(aim.x - ufo.position.x, 0f, aim.z - ufo.position.z);
            if (escapeT < 0f && flat.magnitude <= HitRadius)
            {
                Beam(Muzzle, ufo.position, SparkColor, 0.2f);
                HitUfo();
                return;
            }

            Beam(Muzzle, aim, SparkColor, 0.2f);
            bool bull = TargetUp && Vector3.Distance(new Vector3(aim.x, 0f, aim.z), new Vector3(target.x, 0f, target.z)) <= D * targetRadius;
            float strength = blastStrength * (bull ? 1.8f : 1f);
            Vector3 hole = new Vector3(0f, 0f, FrontZ);
            foreach (var m in MedalsNear(aim, D * blastRadius * (bull ? 1.5f : 1f)))
            {
                if (m.Body == null || m.Body.isKinematic) continue;
                Vector3 toHole = hole - m.transform.position;
                toHole.y = 0f;
                Push(m, (toHole.normalized + Vector3.up * 0.35f) * strength);
            }
            WatchPushed(3f);   // はじいて落ちた分を数える
            SparkAt(aim + Vector3.up * D * 0.1f, bull ? Gold : SparkColor, bull ? 50 : 20);
            if (bull)
            {
                targetTimer = 0f;
                AddPoints(bullseyeBonus);
                Play(bullseyeSound, ref madeBull, () => RecordSynth.Notes("ShootBull", new[] { RecordSynth.Note(12), RecordSynth.Note(16), RecordSynth.Note(19), RecordSynth.Note(24) }, 0.06f, 0.4f, 0.3f, RecordSynth.Wave.Square));
                BoardPop($"BULLSEYE! +{bullseyeBonus}", Gold, aim + Vector3.up * D * 2f, D * 1f);
                Speaker(0.9f, 10, Gold);
            }
        }

        void HitUfo()
        {
            ufoFlash = 1f;
            SparkAt(ufo.position, UfoBlue, 30);
            Play(hitSound, ref madeHit, () => RecordSynth.Notes("ShootHit", new[] { RecordSynth.Note(19), RecordSynth.Note(24) }, 0.05f, 0.2f, 0.3f, RecordSynth.Wave.Square));
            if (Boss)
            {
                bossHits++;
                Shake(0.03f, 0.2f);
                if (bossHits >= bossHp) BossDown();
                return;
            }
            // 景品のメダルが落ちる
            for (int i = 0; i < prizeMedals; i++)
            {
                var m = SpawnFree(ufo.position + new Vector3(Random.Range(-D, D) * 0.4f, -D * 0.4f, Random.Range(-D, D) * 0.4f), new Vector3(0f, -D, 0f));
                if (m != null) MedalMarks.Set(m, PrizeKind, Gold, 1.1f);
            }
            BoardPop("HIT!", UfoBlue, ufo.position + Vector3.up * D, D * 0.7f);
            hits++;
            if (hits >= hitsForBoss) StartBoss();
        }

        void StartBoss()
        {
            hits = 0;
            bossHits = 0;
            bossTimer = bossSeconds;
            bullets = Mathf.Min(maxBullets, bullets + bossReload);
            BoardPop("UFO を撃ち落とせ！", UfoBlue, new Vector3(0f, SkyY + D * 1.5f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.55f)), D * 1f);
            Speaker(0.8f, 8, UfoBlue);
        }

        void BossDown()
        {
            bossTimer = -1f;
            explodeT = 0f;
            int count = Budget(bossMedals + TakePushed(pushedCap));
            var at = ufo.position;
            SparkAt(at, Gold, 80);
            SparkAt(at, Color.white, 40);
            for (int i = 0; i < 5; i++) SmokeAt(at + Random.insideUnitSphere * D, new Color(0.6f, 0.55f, 0.5f, 0.9f), 6);
            Shake(0.08f, 0.6f);
            Speaker(1f, 16, Gold);
            Play(explodeSound, ref madeExplode, () => RecordSynth.Thud("ShootExplode", 50f, 1.2f, 1f));
            BoardPop("撃墜！", Gold, at + Vector3.up * D, D * 1.3f);
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * 0.6f);
                var m = SpawnFree(at + Random.insideUnitSphere * D * 0.5f, dir * D * Random.Range(1.5f, 4f) + Vector3.up * D * Random.Range(1f, 3f));
                if (m != null && i % 3 == 0) MedalMarks.Set(m, PrizeKind, Gold, 1.1f);
            }
        }

        void BossEscape()
        {
            bossTimer = -1f;
            escapeT = 0f;
            BoardPop("にげられた…", UfoBlue, ufo.position + Vector3.up * D, D * 0.8f);
            for (int i = 0; i < Budget(escapeMedals); i++)
            {
                var m = SpawnFree(ufo.position + Random.insideUnitSphere * D * 0.5f, new Vector3(0f, -D, 0f));
                if (m != null) MedalMarks.Set(m, PrizeKind, Gold, 1.1f);
            }
        }

        protected override bool KeepBoardVisible() => explodeT >= 0f || escapeT >= 0f;

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            bool show = BoardShown > 0.01f;

            // UFO（撃墜の後は消える）
            bool ufoOn = show && explodeT < 0f;
            ufo.gameObject.SetActive(ufoOn);
            if (ufoOn)
            {
                ufo.position = UfoPos(t);
                float s = Ease.OutBack(BoardShown) * (1f + bossGrow * 0.8f) * (1f + 0.15f * ufoFlash);
                ufo.localScale = Vector3.one * s;
                ufo.rotation = Quaternion.Euler(Mathf.Sin(t * 2f) * 8f + (Boss ? Mathf.Sin(t * 20f) * 4f * ufoFlash : 0f), t * 120f, Mathf.Cos(t * 1.7f) * 8f);
                bool blink = Mathf.Repeat(t * (Boss ? 8f : 3f), 1f) < 0.5f;
                SetGlow(ufoLights, Boss ? Red : UfoBlue, (blink ? 3f : 1f) + 3f * ufoFlash);
            }
            shadow.SetActive(ufoOn && Active && escapeT < 0f);
            if (shadow.activeSelf)
            {
                shadow.transform.position = UfoShadow;
                float r = HitRadius * 2f;
                shadow.transform.localScale = new Vector3(r, D * 0.003f, r);
                SetGlow(shadowMaterial, Boss ? Red : UfoBlue, 0.8f + 0.4f * Mathf.Sin(t * 6f));
            }

            // 照準と金の的
            aimRing.SetActive(Active);
            aimRing.transform.position = aim;
            SetGlow(aimMaterial, Red, (1.5f + Mathf.Sin(t * 8f)) * BoardShown);
            targetDisc.SetActive(Active && TargetUp);
            if (TargetUp)
            {
                targetDisc.transform.position = target;
                SetGlow(targetMaterial, Gold, 2f + Mathf.Sin(t * 10f));
            }

            // 銃：照準（または UFO の大技中は UFO）の方を向く。撃つと少し跳ねる
            gun.gameObject.SetActive(show);
            gun.position = GunPos + Vector3.down * D * 2f * (1f - Ease.OutCubic(BoardShown));
            Vector3 lookAt = aim;
            Vector3 dir = lookAt - gun.position;
            if (dir.sqrMagnitude > 0.0001f)
                barrel.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(-12f * recoil, 0f, 0f);
            barrel.localPosition = new Vector3(0f, 0f, -D * 0.15f * recoil);
            for (int i = 0; i < corks.Length; i++)
                if (corks[i] != null) corks[i].SetActive(i < bullets);
        }
    }
}
