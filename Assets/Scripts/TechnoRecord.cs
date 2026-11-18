using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N007 テクノ「レーザー・スイッチ」：高速・連続型。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・ふだん：
    ///   盤面の上に円盤の装置が浮かび、左右にゆらゆら動く。beatsPerShot 拍ごとに、緑のレーザーを盤面のメダルに撃ちこむ
    ///   レーザーが当たったメダルは「光るメダル」（緑に光る）に変わる。光るメダルが中央の穴に落ちると +glowBonus pt
    ///   プッシャーの速さは ×pusherSpeed（速く押して、連続で落とす）
    ///   ビートに合わせて、手前のふちの16マスのライトが進み、盤面を横切るレーザーの線が流れる
    /// ・大技「ベースドロップ」：光るメダルが中央の穴に glowForDrop 枚落ちると発動
    ///   盤面のまん中から、メダルの柱がらせんに噴き上がり、ビートに合わせて回る。まわりのメダルが柱の方へ寄ってくる
    ///   円盤がビートごとに柱へレーザーを撃ちこむ。最後に柱が崩れて、メダルが周りへ散らばる
    ///   柱のメダルの数は、dropMedals 枚＋前のベースドロップから押し出された（穴に落ちた）分（最大 pushedCap 枚）
    ///   柱が立っている間は、プッシャーが ×dropPusherSpeed
    /// ・盤面のメダルが多すぎる時（RecordScriptBase.MedalSoftCap より多い）は、柱のメダルを半分にする
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class TechnoRecord : RecordScriptBase
    {
        public static TechnoRecord Instance { get; private set; }

        [Header("ふだん")]
        [Tooltip("発動中のプッシャーの速さ")]
        [Range(0.2f, 5f)] public float pusherSpeed = 1.5f;
        [Tooltip("ビートの速さ（BPM）。レーザーとライトが合わせて動く")]
        [Range(60f, 200f)] public float bpm = 128f;
        [Tooltip("何拍ごとにレーザーを撃つか")]
        [Range(1, 8)] public int beatsPerShot = 2;
        [Tooltip("光るメダルが中央の穴に落ちた時のポイント")]
        [Range(0, 100)] public int glowBonus = 8;

        [Header("大技：ベースドロップ")]
        [Tooltip("光るメダルが中央の穴に何枚落ちると発動するか")]
        [Range(1, 30)] public int glowForDrop = 8;
        [Tooltip("柱に足すメダルの枚数")]
        [Range(0, 60)] public int dropMedals = 20;
        [Tooltip("押し出された分として柱に足す最大の枚数")]
        [Range(0, 60)] public int pushedCap = 20;
        [Tooltip("柱が積み上がる秒数")]
        [Range(0.2f, 5f)] public float buildSeconds = 1.2f;
        [Tooltip("柱が立って回っている秒数")]
        [Range(0.5f, 10f)] public float holdSeconds = 2.5f;
        [Range(0.2f, 6f)] public float dropPusherSpeed = 2.5f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip laserSound;
        public AudioClip glowSound;
        public AudioClip dropSound;
        public AudioClip collapseSound;

        protected override string DefaultKey => "Techno";
        protected override string ScreenTitle => "TECHNO";
        protected override string ScreenSubtitle => "レーザー・スイッチ";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<TechnoRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        const string GlowKind = "TechnoGlow";
        const string BassKind = "TechnoBass";
        const int Cells = 16;
        static readonly Color Laser = new Color32(0x4c, 0xff, 0x6a, 0xff);
        static readonly Color GlowGreen = new Color32(0x1c, 0xd8, 0x3c, 0xff);   // 光るメダルの色（濃いめの緑。明るすぎると白っぽく見える）
        static readonly Color Cyan = new Color32(0x3f, 0xe0, 0xff, 0xff);
        static readonly Color Magenta = new Color32(0xff, 0x3c, 0xd2, 0xff);
        static readonly Color CellOff = new Color32(0x20, 0x20, 0x2c, 0xff);
        static readonly Color DiscBody = new Color32(0x30, 0x34, 0x40, 0xff);

        int glowCount;
        float dropTimer = -1f;   // 柱が立っている残り（負なら無し）
        int lastBeat = -1;
        float beatFlash;
        Vector3 dropCenter;
        bool Dropping => dropTimer > 0f;
        float Beat => Elapsed * bpm / 60f;

        Transform disc;
        Material discRing, discEye;
        readonly List<Transform> lasers = new List<Transform>();
        Material laserMaterial;
        readonly List<Material> cellMats = new List<Material>();
        AudioClip madeLaser, madeGlow, madeDrop, madeCollapse;

        Vector3 DiscPos(float t)
        {
            float x = Mathf.Sin(t * 0.7f) * HalfW * 0.6f;
            float z = Mathf.Lerp(FrontZ, PusherFrontZ, 0.75f) + Mathf.Sin(t * 1.3f) * D * 0.8f;
            return new Vector3(x, SkyY + D * 0.5f + Mathf.Sin(t * 2.1f) * D * 0.15f, z);
        }
        Vector3 DiscEye => disc != null ? disc.position - Vector3.up * D * 0.25f : Vector3.zero;

        protected override void BuildBoard()
        {
            // 円盤の装置（上空）
            disc = new GameObject("LaserDisc").transform;
            disc.SetParent(Board, false);
            Shape(PrimitiveType.Cylinder, "Body", disc, Vector3.zero, new Vector3(D * 2.6f, D * 0.12f, D * 2.6f), Glow(DiscBody, 0.1f));
            Shape(PrimitiveType.Sphere, "Dome", disc, new Vector3(0f, D * 0.12f, 0f), new Vector3(D * 1.2f, D * 0.6f, D * 1.2f), Glow(new Color(0.5f, 0.9f, 1f), 0.6f));
            discRing = Glow(Laser, 2f);
            Shape(PrimitiveType.Cylinder, "Ring", disc, new Vector3(0f, -D * 0.02f, 0f), new Vector3(D * 2.8f, D * 0.04f, D * 2.8f), discRing);
            discEye = Glow(Laser, 3f);
            Shape(PrimitiveType.Sphere, "Eye", disc, new Vector3(0f, -D * 0.15f, 0f), Vector3.one * D * 0.45f, discEye);

            // 盤面を横切るレーザーの線
            laserMaterial = Glow(Cyan, 3f);
            for (int i = 0; i < 4; i++)
                lasers.Add(Cube($"Laser{i}", Board, new Vector3(0f, D * 0.08f, FrontZ + D * (0.8f + i * 0.7f)), new Vector3(HalfW * 2f, D * 0.015f, D * 0.015f), laserMaterial).transform);

            // 手前のふちの16マスのライト
            float cw = HalfW * 2f / Cells;
            for (int i = 0; i < Cells; i++)
            {
                var m = Glow(CellOff, 0.2f);
                Cube($"Cell{i}", Board, new Vector3(-HalfW + cw * (i + 0.5f), D * 0.02f, FrontZ + D * 0.12f), new Vector3(cw * 0.8f, D * 0.04f, D * 0.18f), m);
                cellMats.Add(m);
            }

            MedalMarks.RegisterEffect(GlowKind, OnGlowMedalDropped);
        }

        void OnGlowMedalDropped(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            AddPoints(glowBonus);
            BoardPop($"+{CenterPoints + glowBonus}", Laser, new Vector3(medal.transform.position.x, D * 1.2f, FrontZ + D * 0.5f), D * 0.6f);
            if (!Active || Dropping) return;
            glowCount++;
            Play(glowSound, ref madeGlow, () => RecordSynth.Notes("TechnoGlow", new[] { 880f, 1320f }, 0.04f, 0.12f, 0.22f, RecordSynth.Wave.Square), 0.6f);
            if (glowCount >= glowForDrop)
            {
                glowCount = 0;
                StartBassDrop();
            }
        }

        protected override void OnBegin()
        {
            glowCount = 0;
            dropTimer = -1f;
            lastBeat = -1;
            TakePushed(0);   // 数え直し
        }

        protected override void OnEnd()
        {
            dropTimer = -1f;
        }

        protected override void Tick(float dt, bool running)
        {
            beatFlash = Mathf.MoveTowards(beatFlash, 0f, dt * 4f);
            PusherSpeedScale = Dropping ? dropPusherSpeed : pusherSpeed;
            if (Dropping) dropTimer -= dt;
            if (!running) return;
            WatchPushed(0.5f);   // 流している間に落ちたメダルは「押し出した分」として数える

            int beat = Mathf.FloorToInt(Beat);
            if (beat == lastBeat) return;
            lastBeat = beat;
            beatFlash = 1f;
            if (Dropping)
            {
                // 柱へレーザーを撃ちこむ
                Beam(DiscEye, dropCenter + Vector3.up * D * Random.Range(0.5f, 3f), Magenta, 0.2f, D * 0.25f);
                SparkAt(dropCenter + Vector3.up * D, Magenta, 12);
                Play(laserSound, ref madeLaser, () => RecordSynth.Sweep("TechnoLaser", 1800f, 300f, 0.15f, 0.2f, RecordSynth.Wave.Square), 0.5f);
                return;
            }
            if (beat % Mathf.Max(1, beatsPerShot) == 0) ShootLaser();
        }

        void ShootLaser()
        {
            var m = PickPlainFloorMedal();
            if (m == null) return;
            var hit = m.transform.position;
            Beam(DiscEye, hit, Laser, 0.25f);
            SparkAt(hit + Vector3.up * D * 0.1f, Laser, 18);
            MarkMedal(m, GlowKind, GlowGreen, 1f, 1.1f);
            Play(laserSound, ref madeLaser, () => RecordSynth.Sweep("TechnoLaser", 1800f, 300f, 0.15f, 0.2f, RecordSynth.Wave.Square), 0.5f);
        }

        void StartBassDrop()
        {
            int count = Budget(dropMedals + TakePushed(pushedCap));
            dropCenter = FloorCenter(0.5f);
            dropTimer = buildSeconds + holdSeconds;
            BoardPop("BASS DROP!", Magenta, dropCenter + Vector3.up * D * 3f, D * 1.3f);
            Play(dropSound, ref madeDrop, () => RecordSynth.Thud("TechnoDrop", 55f, 0.8f, 0.9f));
            Speaker(1f, 16, Magenta);
            Shake(0.04f, 0.4f);
            float spin = 360f * bpm / 60f / 4f;   // 4拍で1回転
            ColumnRoutine(dropCenter, count, buildSeconds, holdSeconds, spin, D * 3f,
                m => MedalMarks.Set(m, BassKind, Magenta, 1.1f),
                () =>
                {
                    Play(collapseSound, ref madeCollapse, () => RecordSynth.Noise("TechnoCollapse", 0.8f, 0.2f, 0.01f, 0.5f, 0.9f, 7));
                    Shake(0.06f, 0.4f);
                    Speaker(1f, 12, Laser);
                });
        }

        protected override bool KeepBoardVisible() => Dropping;

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            if (disc != null)
            {
                disc.position = DiscPos(t);
                disc.rotation = Quaternion.Euler(Mathf.Sin(t * 1.3f) * 6f, t * 90f, Mathf.Cos(t * 1.1f) * 6f);
                disc.localScale = Vector3.one * Ease.OutBack(BoardShown);
            }
            Color main = Dropping ? Magenta : Laser;
            SetGlow(discRing, main, (1.5f + 2.5f * beatFlash) * BoardShown);
            SetGlow(discEye, main, (2f + 3f * beatFlash) * BoardShown);

            float beatFrac = Beat - Mathf.Floor(Beat);
            float flash = 1f - beatFrac;
            for (int i = 0; i < lasers.Count; i++)
            {
                float speed = Dropping ? 0.5f : 0.25f;
                float z = Mathf.Lerp(FrontZ + D * 0.5f, PusherFrontZ, Mathf.Repeat(Beat * speed + i / (float)lasers.Count, 1f));
                var p = lasers[i].localPosition;
                lasers[i].localPosition = new Vector3(p.x, p.y, z);
                lasers[i].localScale = new Vector3(HalfW * 2f * BoardShown, D * 0.015f, D * 0.015f);
            }
            SetGlow(laserMaterial, Dropping ? Magenta : Cyan, (1.5f + 2.5f * flash) * BoardShown);

            // 16マス：ビートで光が進む。光るメダルの数だけ左から緑に埋まる。ベースドロップの間は一斉に点滅
            int cursor = Mathf.FloorToInt(Beat * 2f) % Cells;
            int filled = Mathf.RoundToInt(Cells * glowCount / (float)Mathf.Max(1, glowForDrop));
            for (int i = 0; i < cellMats.Count; i++)
            {
                Color c;
                float g;
                if (Dropping)
                {
                    c = (i + Mathf.FloorToInt(Beat * 2f)) % 2 == 0 ? Magenta : Cyan;
                    g = 1.5f + 2.5f * flash;
                }
                else if (i == cursor) { c = Cyan; g = 3f; }
                else if (i < filled) { c = Laser; g = 1.6f; }
                else { c = CellOff; g = 0.2f; }
                SetGlow(cellMats[i], c, g * BoardShown);
            }
        }
    }
}
