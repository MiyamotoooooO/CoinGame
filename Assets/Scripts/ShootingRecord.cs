using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N009 射的「ブルズアイ」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：床の上を照準（赤い輪）が左右にゆっくり動く。ときどき金の的が床に出る。
    ///   弾を撃つと、筐体の上から光の線が走り、当たった所で火花が散って、まわりのメダルが中央の穴の方へはじける
    /// ・モニター：射的の屋台。弾の数（最大 maxBullets）、照準と的の位置、BULLSEYE の数
    /// ・ルール：
    ///   メダルが穴（中央・端）に落ちるたびに、弾が1発たまる（CSV の「穴に落ちるたびに撃つ」を、ためて撃つ形にした）
    ///   メダルを投入した瞬間に、弾があれば照準の所へ1発撃つ。まわりのメダルが中央の穴の方へはじける
    ///   金の的（targetInterval 秒ごとに targetSeconds 秒出る）に照準が重なっている時に撃つと BULLSEYE：+bullseyeBonus pt、強くはじける
    /// ・照準が的に重なるタイミングで投入する遊び
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

        [Header("照準")]
        [Tooltip("照準が左右に1往復する秒数")]
        [Range(1f, 20f)] public float sweepSeconds = 4f;

        [Header("金の的")]
        [Range(2f, 30f)] public float targetInterval = 10f;
        [Range(1f, 15f)] public float targetSeconds = 5f;
        [Tooltip("的の半径（メダルの直径に対する倍率）")]
        [Range(0.3f, 3f)] public float targetRadius = 0.8f;
        [Range(0, 200)] public int bullseyeBonus = 30;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip shotSound;
        public AudioClip bullseyeSound;
        public AudioClip reloadSound;

        protected override string DefaultKey => "Shooting";
        protected override string ScreenTitle => "SHOOTING";
        protected override string ScreenSubtitle => "ブルズアイ";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<ShootingRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Red = new Color32(0xff, 0x3c, 0x3c, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color Spark = new Color32(0xff, 0xe8, 0x9a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Wood = new Color32(0x5a, 0x34, 0x22, 0xff);

        int bullets, bullseyes;
        float targetTimer, nextTarget, beamTimer;
        Vector3 aim, target, lastShot;
        bool TargetUp => targetTimer > 0f;

        GameObject aimRing, targetDisc, beam;
        Material aimMaterial, targetMaterial, beamMaterial;
        ParticleSystem sparks;

        readonly List<Image> bulletIcons = new List<Image>();
        RectTransform aimIcon, targetIcon;
        Text bullseyeText, statusText;
        float rangeW, rangeH;
        AudioClip madeShot, madeBull, madeReload;

        float AimZ => FrontZ + D * 1.4f;

        protected override void BuildBoard()
        {
            aimMaterial = Glow(Red, 2f);
            aimRing = Shape(PrimitiveType.Cylinder, "Aim", Board, Vector3.zero, new Vector3(D * 0.9f, D * 0.004f, D * 0.9f), aimMaterial);
            targetMaterial = Glow(Gold, 2f);
            targetDisc = Shape(PrimitiveType.Cylinder, "Target", Board, Vector3.zero, new Vector3(D * targetRadius * 2f, D * 0.004f, D * targetRadius * 2f), targetMaterial);
            beamMaterial = Glow(Spark, 4f);
            beam = Cube("Beam", Board, Vector3.zero, new Vector3(D * 0.04f, D * 0.04f, 1f), beamMaterial);
            beam.SetActive(false);
            sparks = Particles("Sparks", Board, Vector3.up, D * 4f, D * 0.07f, 0.6f, 0.8f, 50f);
        }

        protected override void OnBegin()
        {
            bullets = 1;
            bullseyes = 0;
            targetTimer = 0f;
            nextTarget = 3f;
        }

        protected override void Tick(float dt, bool running)
        {
            beamTimer = Mathf.MoveTowards(beamTimer, 0f, dt * 4f);
            float phase = Elapsed / Mathf.Max(0.5f, sweepSeconds) * Mathf.PI * 2f;
            aim = new Vector3(Mathf.Sin(phase) * HalfW * 0.8f, D * 0.01f, AimZ + Mathf.Sin(phase * 0.5f) * D * 0.6f);
            if (!running) return;
            if (TargetUp) targetTimer -= dt;
            else
            {
                nextTarget -= dt;
                if (nextTarget <= 0f)
                {
                    nextTarget = targetInterval;
                    targetTimer = targetSeconds;
                    target = new Vector3(Random.Range(-HalfW * 0.7f, HalfW * 0.7f), D * 0.01f, AimZ + Random.Range(-D * 0.5f, D * 0.5f));
                }
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (bullets >= maxBullets) return;
            bullets++;
            Play(reloadSound, ref madeReload, () => RecordSynth.Notes("ShootReload", new[] { 1200f, 900f }, 0.04f, 0.06f, 0.2f, RecordSynth.Wave.Square), 0.5f);
        }

        protected override void OnInsert(Medal medal)
        {
            if (!IsScoring || bullets <= 0) return;
            bullets--;
            Vector3 hole = new Vector3(0f, 0f, FrontZ);
            bool bull = TargetUp && Vector3.Distance(new Vector3(aim.x, 0f, aim.z), new Vector3(target.x, 0f, target.z)) <= D * targetRadius;
            float strength = blastStrength * (bull ? 1.8f : 1f);
            foreach (var m in MedalsNear(aim, D * blastRadius * (bull ? 1.5f : 1f)))
            {
                Vector3 toHole = hole - m.transform.position;
                toHole.y = 0f;
                Push(m, (toHole.normalized + Vector3.up * 0.35f) * strength);
            }
            lastShot = aim;
            beamTimer = 1f;
            Emit(sparks, aim + Vector3.up * D * 0.1f, bull ? Gold : Spark, bull ? 50 : 20);
            Play(shotSound, ref madeShot, () => RecordSynth.Noise("ShootShot", 0.25f, 0.6f, 0.002f, 0.06f, 1f, 6));
            if (bull)
            {
                bullseyes++;
                targetTimer = 0f;
                AddPoints(bullseyeBonus);
                Play(bullseyeSound, ref madeBull, () => RecordSynth.Notes("ShootBull", new[] { RecordSynth.Note(12), RecordSynth.Note(16), RecordSynth.Note(19), RecordSynth.Note(24) }, 0.06f, 0.4f, 0.3f, RecordSynth.Wave.Square));
                PopCenter($"BULLSEYE! +{bullseyeBonus}", Gold);
                Speaker(0.9f, 10, Gold);
            }
        }

        protected override bool KeepBoardVisible() => sparks != null && sparks.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            aimRing.SetActive(Active);
            aimRing.transform.position = aim;
            SetGlow(aimMaterial, Red, (1.5f + Mathf.Sin(Time.time * 8f)) * BoardShown);
            targetDisc.SetActive(Active && TargetUp);
            if (TargetUp)
            {
                targetDisc.transform.position = target;
                SetGlow(targetMaterial, Gold, 2f + Mathf.Sin(Time.time * 10f));
            }
            beam.SetActive(beamTimer > 0f);
            if (beamTimer > 0f)
            {
                Vector3 from = new Vector3(lastShot.x * 0.3f, Layout.wallHeight * 1.2f, lastShot.z + D * 2f);
                Vector3 d = lastShot - from;
                beam.transform.SetPositionAndRotation((from + lastShot) * 0.5f, Quaternion.LookRotation(d));
                beam.transform.localScale = new Vector3(D * 0.05f * beamTimer, D * 0.05f * beamTimer, d.magnitude);
            }
        }

        protected override void BuildScreen()
        {
            bulletIcons.Clear();
            float W = BodyW;
            Img(Screen, "Roof", W * 0.5f, 40f, W - 48f, 50f, Red);
            for (int i = 0; i < 10; i++) Img(Screen, $"Stripe{i}", 24f + (W - 48f) * (i + 0.5f) / 10f, 40f, (W - 48f) / 20f, 50f, Ink);
            rangeW = W - 48f;
            rangeH = 280f;
            var range = Panel(Screen, "Range", W * 0.5f, 90f + rangeH * 0.5f, rangeW, rangeH, new Color32(0x1a, 0x12, 0x10, 0xff));
            Img(range, "Shelf", rangeW * 0.5f, rangeH - 30f, rangeW, 20f, Wood);
            var ti = Grp(range, "TargetIcon", 0f, 0f, 80f, 80f);
            Img(ti, "R1", 40f, 40f, 80f, 80f, Gold, MonitorDisplay.ScreenShape.Circle);
            Img(ti, "R2", 40f, 40f, 54f, 54f, Ink, MonitorDisplay.ScreenShape.Circle);
            Img(ti, "R3", 40f, 40f, 28f, 28f, Red, MonitorDisplay.ScreenShape.Circle);
            targetIcon = ti;
            var ai = Grp(range, "AimIcon", 0f, 0f, 70f, 70f);
            Img(ai, "Ring", 35f, 35f, 70f, 70f, Red, MonitorDisplay.ScreenShape.Ring);
            Img(ai, "H", 35f, 35f, 70f, 3f, Red);
            Img(ai, "V", 35f, 35f, 3f, 70f, Red);
            aimIcon = ai;

            Lbl(Screen, "BulletLabel", 24f, 440f, 100f, 30f, "弾", 20, Ink, TextAnchor.MiddleLeft);
            for (int i = 0; i < maxBullets; i++)
                bulletIcons.Add(Img(Screen, $"Bullet{i}", 90f + i * 34f, 440f, 18f, 44f, Gold, MonitorDisplay.ScreenShape.Pill));
            bullseyeText = Lbl(Screen, "Bull", W - 24f, 440f, 360f, 40f, "", 24, Gold, TextAnchor.MiddleRight, true);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float ax = Mathf.InverseLerp(-HalfW, HalfW, aim.x) * rangeW;
            Place(aimIcon, ax, rangeH * 0.5f + Mathf.Sin(Time.time * 2f) * 10f);
            targetIcon.gameObject.SetActive(TargetUp);
            if (TargetUp) Place(targetIcon, Mathf.InverseLerp(-HalfW, HalfW, target.x) * rangeW, rangeH * 0.5f);
            for (int i = 0; i < bulletIcons.Count; i++) bulletIcons[i].color = i < bullets ? Gold : new Color(1f, 1f, 1f, 0.12f);
            bullseyeText.text = $"BULLSEYE ×{bullseyes}";
            statusText.text = TargetUp
                ? $"<color=#FFD23F>金の的が出た！</color>  照準が重なった時に投入すると BULLSEYE +{bullseyeBonus}pt"
                : bullets > 0 ? "投入すると照準の所へ撃つ。メダルが中央の穴の方へはじける" : "穴にメダルが落ちると弾がたまる";
        }
    }
}
