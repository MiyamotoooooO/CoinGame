using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N013 釣り「つり堀の主」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：端の穴に落ちそうなメダルに、筐体の上から釣り糸が下りてきて、釣り上げて盤面のまん中の手前へ運ぶ。
    ///   ときどき金色の「主（ぬし）」のメダルが盤面に現れる
    ///   bigCatchInterval 秒ごとに「ヌシ釣り」：釣り糸とウキがメダルの山に下り、ウキが沈むと大きな魚が水しぶきを上げて飛び出す。
    ///   飛び出した場所のメダルは空中へ打ち上げられ、中央の穴の方へ降ってくる。魚は宙返りして盤面にもどる
    /// ・モニター：つり堀。残りの竿の数（ウキ）、釣った枚数、主の場所、ヌシ釣りまでの時間
    /// ・ルール：
    ///   端の穴に落ちそうなメダル（端の穴の手前 rescueDepth 以内）を自動で釣り上げ、まん中の手前に戻す（1曲で maxRescues 回まで）
    ///   釣ったメダルは「釣れたメダル」になる。中央の穴に落ちると +caughtBonus pt
    ///   lordInterval 秒ごとに「主」のメダルが盤面に現れる。中央の穴に落とすと +lordBonus pt、竿が1本もどる
    ///   ヌシ釣りで打ち上げたメダルが中央の穴に入ると、1枚 +launchBonus pt
    /// ・竿は有限なので、主をねらって中央へ寄せる遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class FishingRecord : RecordScriptBase
    {
        public static FishingRecord Instance { get; private set; }

        public const string CaughtKind = "Caught";
        public const string LordKind = "Lord";

        [Header("釣り")]
        [Range(1, 20)] public int maxRescues = 5;
        [Tooltip("端の穴の手前、どこまで来たメダルを釣るか（メダルの直径に対する倍率）")]
        [Range(0.2f, 3f)] public float rescueDepth = 0.9f;
        [Tooltip("1回釣ってから次に釣れるまでの秒数")]
        [Range(0f, 5f)] public float castCooldown = 0.8f;
        [Range(0, 100)] public int caughtBonus = 15;

        [Header("主")]
        [Range(3f, 60f)] public float lordInterval = 12f;
        [Range(0, 300)] public int lordBonus = 40;

        [Header("ヌシ釣り（大きな魚が飛び出す）")]
        [Range(4f, 60f)] public float bigCatchInterval = 14f;
        [Tooltip("発動してから最初のヌシ釣りまでの秒数")]
        [Range(1f, 30f)] public float firstBigCatch = 5f;
        [Tooltip("魚が飛び出す時に打ち上げる範囲（メダルの直径に対する倍率）")]
        [Range(0.5f, 5f)] public float blastRadius = 1.9f;
        [Tooltip("打ち上げる上向きの勢い（メダルの直径 / 秒）")]
        [Range(1f, 20f)] public float launchUp = 7f;
        [Tooltip("中央の穴の方へ飛ばす勢い（メダルの直径 / 秒）")]
        [Range(0f, 10f)] public float launchForward = 3.2f;
        [Range(0, 100)] public int launchBonus = 8;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip castSound;
        public AudioClip lordSound;
        public AudioClip splashSound;

        protected override string DefaultKey => "Fishing";
        protected override string ScreenTitle => "FISHING";
        protected override string ScreenSubtitle => "つり堀の主";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<FishingRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Line = new Color32(0xf0, 0xf0, 0xf0, 0xff);
        static readonly Color Fish = new Color32(0x4f, 0xd8, 0xc8, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xcc, 0x33, 0xff);
        static readonly Color Koi = new Color32(0xff, 0x7a, 0x2a, 0xff);
        static readonly Color Water = new Color32(0x8a, 0xe0, 0xff, 0xff);
        static readonly Color Pond = new Color32(0x0c, 0x30, 0x3a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int rescuesLeft, caughtCount, bigCatches;
        float cooldown, nextLord, lineTimer;
        Vector3 lineFrom, lineTo;

        // ---- ヌシ釣り ----
        enum Phase { None, Cast, Bite, Jump, Dive }
        Phase phase;
        float phaseT, nextBig, rippleTimer;
        Vector3 target, rodTip;
        readonly HashSet<Medal> launched = new HashSet<Medal>();
        float launchedTimer;
        readonly List<Medal> near = new List<Medal>();

        GameObject line, bigLine;
        Transform bobber, fish, fishTail;
        ParticleSystem splash, spray, shine;

        class Ripple { public Transform t; public Material m; public float age, life, size; }
        readonly List<Ripple> ripples = new List<Ripple>();

        readonly List<Image> floats = new List<Image>();
        RectTransform lordIcon;
        Text caughtText, statusText;
        float pondW, pondH;
        AudioClip madeCast, madeLord, madeSplash;

        static Material Shiny(Color c, float metal, float smooth, float glow)
        {
            var m = ProtoAssets.MakeMaterial(c, metal, smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            return m;
        }

        protected override void BuildBoard()
        {
            line = Cube("Line", Board, Vector3.zero, new Vector3(D * 0.02f, D * 0.02f, 1f), Glow(Line, 1.5f));
            line.SetActive(false);
            bigLine = Cube("BigLine", Board, Vector3.zero, new Vector3(D * 0.025f, D * 0.025f, 1f), Glow(Line, 2f));
            bigLine.SetActive(false);
            splash = Particles("Splash", Board, Vector3.up, D * 2f, D * 0.1f, 0.7f, 0.8f, 40f);
            spray = Particles("Spray", Board, Vector3.up, D * 6f, D * 0.16f, 1.1f, 1.2f, 35f);
            shine = Particles("Sparkle", Board, Vector3.up, D * 3f, D * 0.08f, 0.8f, 0.3f, 60f);
            BuildBobber();
            BuildFish();

            MedalMarks.RegisterEffect(CaughtKind, (m, type) =>
            {
                if (type != DropZoneType.Center || effects == null) return;
                effects.AddPoints(caughtBonus);
                Pop($"釣れたメダル +{caughtBonus}", Fish, new Vector2(BodyW * 0.5f, 420f));
            });
            MedalMarks.RegisterEffect(LordKind, (m, type) =>
            {
                if (type != DropZoneType.Center || effects == null) return;
                effects.AddPoints(lordBonus);
                if (Active) rescuesLeft = Mathf.Min(maxRescues, rescuesLeft + 1);
                Play(lordSound, ref madeLord, () => RecordSynth.Notes("FishingLord", new[] { RecordSynth.Note(12), RecordSynth.Note(16), RecordSynth.Note(19), RecordSynth.Note(24) }, 0.08f, 0.5f, 0.3f, RecordSynth.Wave.Bell));
                PopCenter($"主を釣り上げた！ +{lordBonus}", Gold);
                Speaker(0.9f, 10, Gold);
            });
        }

        void BuildBobber()
        {
            bobber = new GameObject("Bobber").transform;
            bobber.SetParent(Board, false);
            float s = D * 0.32f;
            Shape(PrimitiveType.Sphere, "Top", bobber, new Vector3(0f, s * 0.35f, 0f), new Vector3(s, s * 0.9f, s), Shiny(new Color(1f, 0.2f, 0.2f), 0.1f, 0.9f, 0.5f));
            Shape(PrimitiveType.Sphere, "Bottom", bobber, new Vector3(0f, -s * 0.05f, 0f), new Vector3(s * 0.98f, s * 0.7f, s * 0.98f), Shiny(Color.white, 0.1f, 0.9f, 0.3f));
            Shape(PrimitiveType.Cylinder, "Stick", bobber, new Vector3(0f, s * 0.95f, 0f), new Vector3(s * 0.12f, s * 0.3f, s * 0.12f), Shiny(new Color(1f, 0.9f, 0.3f), 0f, 0.6f, 0.8f));
            bobber.gameObject.SetActive(false);
        }

        void BuildFish()
        {
            fish = new GameObject("BigFish").transform;
            fish.SetParent(Board, false);
            float L = D * 2.1f;   // 魚の長さ
            var body = Shiny(Koi, 0.35f, 0.9f, 0.35f);
            var belly = Shiny(new Color(1f, 0.93f, 0.8f), 0.2f, 0.85f, 0.2f);
            var gold = Shiny(Gold, 0.9f, 0.9f, 0.6f);
            var fin = Shiny(new Color(1f, 0.55f, 0.25f), 0.1f, 0.6f, 0.4f);
            var dark = ProtoAssets.MakeMaterial(new Color(0.05f, 0.05f, 0.07f), 0f, 0.95f);
            // 体（頭が +Z）
            Shape(PrimitiveType.Sphere, "Body", fish, Vector3.zero, new Vector3(L * 0.34f, L * 0.4f, L), body);
            Shape(PrimitiveType.Sphere, "Belly", fish, new Vector3(0f, -L * 0.07f, L * 0.05f), new Vector3(L * 0.3f, L * 0.28f, L * 0.82f), belly);
            Shape(PrimitiveType.Sphere, "Back", fish, new Vector3(0f, L * 0.09f, -L * 0.05f), new Vector3(L * 0.22f, L * 0.24f, L * 0.75f), gold);
            // 目と口
            for (int s = -1; s <= 1; s += 2)
            {
                Shape(PrimitiveType.Sphere, "Eye", fish, new Vector3(s * L * 0.14f, L * 0.06f, L * 0.36f), Vector3.one * L * 0.09f, Shiny(Color.white, 0f, 0.95f, 0.3f));
                Shape(PrimitiveType.Sphere, "Pupil", fish, new Vector3(s * L * 0.175f, L * 0.065f, L * 0.38f), Vector3.one * L * 0.05f, dark);
                var pec = Cube("Pectoral", fish, new Vector3(s * L * 0.17f, -L * 0.1f, L * 0.15f), new Vector3(L * 0.02f, L * 0.12f, L * 0.18f), fin);
                pec.transform.localRotation = Quaternion.Euler(-30f, 0f, s * 35f);
                // ひげ
                var whisker = Cube("Whisker", fish, new Vector3(s * L * 0.07f, -L * 0.07f, L * 0.5f), new Vector3(L * 0.012f, L * 0.012f, L * 0.16f), gold);
                whisker.transform.localRotation = Quaternion.Euler(25f, s * 30f, 0f);
            }
            Shape(PrimitiveType.Sphere, "Mouth", fish, new Vector3(0f, -L * 0.02f, L * 0.49f), new Vector3(L * 0.1f, L * 0.07f, L * 0.06f), dark);
            // 背びれ
            var dorsal = Cube("Dorsal", fish, new Vector3(0f, L * 0.22f, -L * 0.05f), new Vector3(L * 0.02f, L * 0.16f, L * 0.4f), fin);
            dorsal.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            // 尾びれ（動く）
            fishTail = new GameObject("Tail").transform;
            fishTail.SetParent(fish, false);
            fishTail.localPosition = new Vector3(0f, 0f, -L * 0.46f);
            var t1 = Cube("TailUp", fishTail, new Vector3(0f, L * 0.1f, -L * 0.1f), new Vector3(L * 0.02f, L * 0.24f, L * 0.16f), fin);
            t1.transform.localRotation = Quaternion.Euler(40f, 0f, 0f);
            var t2 = Cube("TailDown", fishTail, new Vector3(0f, -L * 0.1f, -L * 0.1f), new Vector3(L * 0.02f, L * 0.24f, L * 0.16f), fin);
            t2.transform.localRotation = Quaternion.Euler(-40f, 0f, 0f);
            fish.gameObject.SetActive(false);
        }

        protected override void OnBegin()
        {
            rescuesLeft = maxRescues;
            caughtCount = 0;
            bigCatches = 0;
            cooldown = 0f;
            nextLord = 3f;
            nextBig = firstBigCatch;
            phase = Phase.None;
            launched.Clear();
        }

        protected override void OnEnd()
        {
            phase = Phase.None;
            launched.Clear();
        }

        protected override void Tick(float dt, bool running)
        {
            lineTimer = Mathf.MoveTowards(lineTimer, 0f, dt * 1.5f);
            if (launchedTimer > 0f)
            {
                launchedTimer -= dt;
                if (launchedTimer <= 0f) launched.Clear();
            }
            if (phase != Phase.None) AdvanceBig(dt);
            if (!running) return;
            if (cooldown > 0f) cooldown -= dt;
            nextLord -= dt;
            if (nextLord <= 0f)
            {
                nextLord = lordInterval;
                var m = PickMedal(x => !MedalMarks.Has(x) && x.transform.position.z < PusherFrontZ);
                if (m != null) MedalMarks.Set(m, LordKind, Gold, 3f);
            }
            if (rescuesLeft > 0 && cooldown <= 0f) TryRescue();
            if (phase == Phase.None)
            {
                nextBig -= dt;
                if (nextBig <= 0f)
                {
                    nextBig = bigCatchInterval;
                    StartBig();
                }
            }
        }

        // ================================================================
        // ふつうの釣り（端の穴に落ちそうなメダルを戻す）
        // ================================================================

        void TryRescue()
        {
            foreach (var m in BoardMedals)
            {
                if (m == null || m.Consumed) continue;
                Vector3 p = m.transform.position;
                if (Mathf.Abs(p.x) <= HoleHalf + D * 0.2f) continue;          // 端の穴の側だけ
                if (p.z > FrontZ + D * rescueDepth || p.y < -D * 0.3f) continue; // ふちの近くで、まだ落ちていない物
                Rescue(m);
                return;
            }
        }

        void Rescue(Medal m)
        {
            rescuesLeft--;
            caughtCount++;
            cooldown = castCooldown;
            lineFrom = new Vector3(m.transform.position.x * 0.5f, Layout.wallHeight * 1.2f, m.transform.position.z + D);
            var dest = new Vector3(Random.Range(-HoleHalf * 0.5f, HoleHalf * 0.5f), D * 0.6f, FrontZ + D * 1.1f);
            lineTo = m.transform.position;
            Emit(splash, m.transform.position, Fish, 16);
            if (m.Body != null)
            {
                m.Body.velocity = Vector3.zero;
                m.Body.angularVelocity = Vector3.zero;
                m.Body.position = dest;
            }
            m.transform.position = dest;
            if (!MedalMarks.Is(m, LordKind)) MedalMarks.Set(m, CaughtKind, Fish);
            lineTimer = 1f;
            Play(castSound, ref madeCast, () => RecordSynth.Sweep("FishingCast", 1400f, 500f, 0.3f, 0.25f, RecordSynth.Wave.Triangle));
            Pop("フィッシュ！", Fish, new Vector2(m.transform.position.x < 0f ? BodyW * 0.25f : BodyW * 0.75f, 200f));
        }

        // ================================================================
        // ヌシ釣り
        // ================================================================

        Vector3 FindPile()
        {
            Vector3 best = RandomFloorPoint(0.3f, 0.7f, 0.7f);
            int bestCount = -1;
            for (int i = 0; i < 12; i++)
            {
                var p = RandomFloorPoint(0.2f, 0.8f, 0.7f);
                int c = MedalsNear(p, D * blastRadius, near).Count;
                if (c > bestCount) { bestCount = c; best = p; }
            }
            best.y = 0f;
            return best;
        }

        void StartBig()
        {
            target = FindPile();
            rodTip = new Vector3(target.x * 0.5f, Layout.wallHeight * 1.6f, target.z + D * 2.5f);
            phase = Phase.Cast;
            phaseT = 0f;
            bobber.gameObject.SetActive(true);
            Play(castSound, ref madeCast, () => RecordSynth.Sweep("FishingCast", 1400f, 500f, 0.3f, 0.25f, RecordSynth.Wave.Triangle));
            Pop("ヌシの気配…", Water, new Vector2(BodyW * 0.5f, 200f));
        }

        void AdvanceBig(float dt)
        {
            phaseT += dt;
            switch (phase)
            {
                case Phase.Cast:
                {
                    float k = Mathf.Clamp01(phaseT / 0.55f);
                    bobber.position = Vector3.Lerp(rodTip, target + Vector3.up * D * 0.05f, k * k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * D * 1.2f;
                    if (k >= 1f)
                    {
                        SpawnRipple(target, D * 1.6f, 0.9f);
                        Emit(splash, target, Water, 14);
                        phase = Phase.Bite;
                        phaseT = 0f;
                        rippleTimer = 0.3f;
                    }
                    break;
                }
                case Phase.Bite:
                {
                    // ちょん、ちょん…とウキが動いて、最後にぐっと沈む
                    float bob = Mathf.Sin(phaseT * 14f) * D * 0.04f * Mathf.Clamp01(phaseT / 0.4f);
                    float sink = phaseT > 1.1f ? -(phaseT - 1.1f) * D * 2.5f : 0f;
                    bobber.position = target + new Vector3(0f, D * 0.05f + bob + sink, 0f);
                    rippleTimer -= dt;
                    if (rippleTimer <= 0f)
                    {
                        rippleTimer = 0.35f;
                        SpawnRipple(target, D * 1.1f, 0.7f);
                    }
                    if (phaseT >= 1.35f) Strike();
                    break;
                }
                case Phase.Jump:
                {
                    const float jumpTime = 1.5f;
                    float k = Mathf.Clamp01(phaseT / jumpTime);
                    float h = Mathf.Sin(k * Mathf.PI) * D * 3.8f - D * 0.6f * (1f - Mathf.Sin(k * Mathf.PI));
                    Vector3 pos = target + new Vector3(Mathf.Lerp(-D * 0.6f, D * 0.6f, k), h, 0f);
                    fish.position = pos;
                    // 体をしならせて、宙返り
                    float pitch = Mathf.Lerp(-70f, 70f, k);
                    fish.rotation = Quaternion.Euler(pitch, 90f, k * 360f);
                    fishTail.localRotation = Quaternion.Euler(0f, Mathf.Sin(phaseT * 30f) * 35f, 0f);
                    if (Random.value < 0.7f) Emit(shine, pos, Gold, 2);
                    if (Random.value < 0.6f) Emit(spray, pos, Water, 1);
                    if (k >= 1f)
                    {
                        Emit(spray, target, Water, 50);
                        Emit(splash, target, Color.white, 30);
                        SpawnRipple(target, D * 2.6f, 1f);
                        Shake(0.03f, 0.3f);
                        Play(splashSound, ref madeSplash, () => RecordSynth.Noise("FishingSplash", 0.9f, 0.18f, 0.01f, 0.5f, 1.1f, 4), 0.7f);
                        phase = Phase.Dive;
                        phaseT = 0f;
                    }
                    break;
                }
                case Phase.Dive:
                {
                    float k = Mathf.Clamp01(phaseT / 0.35f);
                    fish.position = target + new Vector3(D * 0.6f, -D * 2f * k, 0f);
                    if (k >= 1f)
                    {
                        fish.gameObject.SetActive(false);
                        bobber.gameObject.SetActive(false);
                        phase = Phase.None;
                    }
                    break;
                }
            }
        }

        void Strike()
        {
            bigCatches++;
            phase = Phase.Jump;
            phaseT = 0f;
            fish.gameObject.SetActive(true);
            bobber.gameObject.SetActive(false);
            // 魚が飛び出した場所のメダルを打ち上げる（中央の穴の方へ）
            int n = 0;
            Vector3 aim = new Vector3(0f, 0f, FrontZ + D * 0.6f);
            foreach (var m in MedalsNear(target, D * blastRadius, near))
            {
                if (m.Body == null || m.Consumed) continue;
                Vector3 d = m.transform.position - target;
                d.y = 0f;
                float k = 1f - Mathf.Clamp01(d.magnitude / (D * blastRadius));
                Vector3 toward = aim - m.transform.position;
                toward.y = 0f;
                Vector3 v = Vector3.up * D * launchUp * (0.6f + 0.6f * k) + toward.normalized * D * launchForward * Random.Range(0.7f, 1.2f) + d.normalized * D * 0.8f;
                m.Body.WakeUp();
                m.Body.velocity = v;
                m.Body.angularVelocity = Random.insideUnitSphere * 20f;
                launched.Add(m);
                n++;
            }
            launchedTimer = 5f;
            Emit(spray, target, Water, 90);
            Emit(spray, target, Color.white, 50);
            Emit(splash, target, Water, 40);
            Emit(shine, target + Vector3.up * D, Gold, 40);
            SpawnRipple(target, D * 3.2f, 1.1f);
            SpawnRipple(target, D * 2f, 0.8f);
            Shake(0.06f, 0.6f);
            Play(splashSound, ref madeSplash, () => RecordSynth.Noise("FishingSplash", 0.9f, 0.18f, 0.01f, 0.5f, 1.1f, 4), 1f);
            PopCenter(n > 0 ? $"BIG CATCH!  {n}枚 打ち上げ！" : "BIG CATCH!", Gold);
            Speaker(1f, 16, Gold);
        }

        void SpawnRipple(Vector3 at, float size, float life)
        {
            var mat = RecordMeshKit.Additive(RecordMeshKit.Ripple, new Color(Water.r, Water.g, Water.b, 0.9f));
            var t = RecordMeshKit.Make("Ripple", RecordMeshKit.Annulus(0.6f, 1f, 48, 3), mat, Board, new Vector3(at.x, D * 0.04f, at.z), Vector3.one * size * 0.2f, Vector3.zero);
            ripples.Add(new Ripple { t = t, m = mat, life = life, size = size });
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center || !launched.Remove(medal)) return;
            AddPoints(launchBonus);
            Pop($"打ち上げ +{launchBonus}", Gold, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 380f));
        }

        protected override bool KeepBoardVisible() => (splash != null && splash.particleCount > 0) || (spray != null && spray.particleCount > 0) || ripples.Count > 0;

        protected override void UpdateBoard(float dt)
        {
            line.SetActive(lineTimer > 0f);
            if (lineTimer > 0f)
            {
                Vector3 to = Vector3.Lerp(lineTo, lineFrom, 1f - lineTimer);
                StretchLine(line, lineFrom, to, D * 0.02f);
            }

            bool big = phase != Phase.None && phase != Phase.Dive;
            bigLine.SetActive(big);
            if (big)
            {
                Vector3 end = phase == Phase.Jump ? fish.TransformPoint(new Vector3(0f, 0f, D * 1.05f)) : bobber.position + Vector3.up * D * 0.15f;
                StretchLine(bigLine, rodTip, end, D * 0.025f);
            }

            for (int i = ripples.Count - 1; i >= 0; i--)
            {
                var r = ripples[i];
                r.age += dt;
                float k = Mathf.Clamp01(r.age / r.life);
                if (r.t != null)
                {
                    r.t.localScale = Vector3.one * Mathf.Lerp(r.size * 0.2f, r.size, 1f - (1f - k) * (1f - k));
                    RecordMeshKit.SetColor(r.m, new Color(Water.r, Water.g, Water.b, 0.9f * (1f - k)));
                }
                if (k >= 1f)
                {
                    if (r.t != null) Destroy(r.t.gameObject);
                    if (r.m != null) Destroy(r.m);
                    ripples.RemoveAt(i);
                }
            }
        }

        static void StretchLine(GameObject go, Vector3 from, Vector3 to, float width)
        {
            Vector3 d = to - from;
            if (d.sqrMagnitude < 0.0001f) return;
            go.transform.SetPositionAndRotation((from + to) * 0.5f, Quaternion.LookRotation(d));
            go.transform.localScale = new Vector3(width, width, d.magnitude);
        }

        // ================================================================
        // モニター
        // ================================================================

        protected override void BuildScreen()
        {
            floats.Clear();
            float W = BodyW;
            pondW = W - 48f;
            pondH = 320f;
            var pond = Panel(Screen, "Pond", W * 0.5f, 60f + pondH * 0.5f, pondW, pondH, Pond);
            for (int i = 0; i < 6; i++)
                Img(pond, $"Ripple{i}", Random.Range(60f, pondW - 60f), Random.Range(40f, pondH - 40f), 120f, 40f, new Color(1f, 1f, 1f, 0.08f), MonitorDisplay.ScreenShape.Ring);
            var li = Grp(pond, "Lord", pondW * 0.5f, pondH * 0.5f, 140f, 70f);
            Img(li, "Body", 60f, 35f, 110f, 54f, Gold, MonitorDisplay.ScreenShape.Circle);
            var tail = Img(li, "Tail", 125f, 35f, 34f, 34f, Gold);
            tail.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Img(li, "Eye", 30f, 28f, 10f, 10f, Pond, MonitorDisplay.ScreenShape.Circle);
            lordIcon = li;
            Lbl(Screen, "Title", 24f, 28f, 400f, 32f, "つり堀", 22, Ink, TextAnchor.MiddleLeft);
            Lbl(Screen, "FloatLabel", 24f, 440f, 120f, 30f, "竿", 20, Ink, TextAnchor.MiddleLeft);
            for (int i = 0; i < maxRescues; i++)
                floats.Add(Img(Screen, $"Float{i}", 90f + i * 40f, 440f, 24f, 34f, new Color32(0xff, 0x55, 0x55, 0xff), MonitorDisplay.ScreenShape.Pill));
            caughtText = Lbl(Screen, "Caught", W - 24f, 440f, 520f, 40f, "", 24, Fish, TextAnchor.MiddleRight);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int i = 0; i < floats.Count; i++)
            {
                floats[i].color = i < rescuesLeft ? new Color32(0xff, 0x55, 0x55, 0xff) : new Color(1f, 1f, 1f, 0.12f);
                var rt = floats[i].rectTransform;
                Place(rt, 90f + i * 40f, 440f + (i < rescuesLeft ? Mathf.Sin(Time.time * 3f + i) * 3f : 0f));
            }
            int lords = MedalMarks.Count(LordKind);
            lordIcon.gameObject.SetActive(lords > 0);
            if (lords > 0)
            {
                var all = MedalMarks.Collect(LordKind, new List<Medal>());
                if (all.Count > 0)
                {
                    Vector3 p = all[0].transform.position;
                    Place(lordIcon, pondW * Mathf.InverseLerp(-HalfW, HalfW, p.x), pondH * (1f - Mathf.InverseLerp(FrontZ, PusherFrontZ, p.z)) * 0.85f + 20f);
                }
            }
            caughtText.text = phase != Phase.None ? $"ヌシ釣り中！  釣った {caughtCount}枚" : $"釣った {caughtCount}枚   ヌシまで {Mathf.CeilToInt(Mathf.Max(0f, nextBig))}秒";
            statusText.text = phase == Phase.Bite ? "<color=#8AE0FF>ウキが動いた…！</color>  大物がかかるぞ"
                : phase == Phase.Jump ? $"<color=#FFCC33>BIG CATCH!</color>  打ち上げたメダルが中央の穴に入ると 1枚 +{launchBonus}pt"
                : lords > 0 ? $"<color=#FFCC33>主が出た！</color>  中央の穴に落とすと +{lordBonus}pt・竿が1本もどる"
                : rescuesLeft > 0 ? $"端の穴に落ちそうなメダルを釣って、まん中へ戻す（残り {rescuesLeft}回）" : "竿がなくなった。主を釣ると1本もどる";
        }
    }
}
