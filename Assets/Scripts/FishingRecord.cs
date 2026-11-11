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
    /// ・モニター：つり堀。残りの竿の数（ウキ）、釣った枚数、主の場所
    /// ・ルール：
    ///   端の穴に落ちそうなメダル（端の穴の手前 rescueDepth 以内）を自動で釣り上げ、まん中の手前に戻す（1曲で maxRescues 回まで）
    ///   釣ったメダルは「釣れたメダル」になる。中央の穴に落ちると +caughtBonus pt
    ///   lordInterval 秒ごとに「主」のメダルが盤面に現れる。中央の穴に落とすと +lordBonus pt、竿が1本もどる
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

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip castSound;
        public AudioClip lordSound;

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
        static readonly Color Pond = new Color32(0x0c, 0x30, 0x3a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int rescuesLeft, caughtCount;
        float cooldown, nextLord, lineTimer;
        Vector3 lineFrom, lineTo;

        GameObject line;
        ParticleSystem splash;

        readonly List<Image> floats = new List<Image>();
        RectTransform lordIcon;
        Text caughtText, statusText;
        float pondW, pondH;
        AudioClip madeCast, madeLord;

        protected override void BuildBoard()
        {
            line = Cube("Line", Board, Vector3.zero, new Vector3(D * 0.02f, D * 0.02f, 1f), Glow(Line, 1.5f));
            line.SetActive(false);
            splash = Particles("Splash", Board, Vector3.up, D * 2f, D * 0.1f, 0.7f, 0.8f, 40f);
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

        protected override void OnBegin()
        {
            rescuesLeft = maxRescues;
            caughtCount = 0;
            cooldown = 0f;
            nextLord = 3f;
        }

        protected override void Tick(float dt, bool running)
        {
            lineTimer = Mathf.MoveTowards(lineTimer, 0f, dt * 1.5f);
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
        }

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

        protected override bool KeepBoardVisible() => splash != null && splash.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            line.SetActive(lineTimer > 0f);
            if (lineTimer <= 0f) return;
            Vector3 to = Vector3.Lerp(lineTo, lineFrom, 1f - lineTimer);
            Vector3 d = to - lineFrom;
            if (d.sqrMagnitude < 0.0001f) return;
            line.transform.SetPositionAndRotation((lineFrom + to) * 0.5f, Quaternion.LookRotation(d));
            line.transform.localScale = new Vector3(D * 0.02f, D * 0.02f, d.magnitude);
        }

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
            caughtText = Lbl(Screen, "Caught", W - 24f, 440f, 400f, 40f, "", 24, Fish, TextAnchor.MiddleRight);
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
            caughtText.text = $"釣った {caughtCount}枚";
            statusText.text = lords > 0
                ? $"<color=#FFCC33>主が出た！</color>  中央の穴に落とすと +{lordBonus}pt・竿が1本もどる"
                : rescuesLeft > 0 ? $"端の穴に落ちそうなメダルを釣って、まん中へ戻す（残り {rescuesLeft}回）" : "竿がなくなった。主を釣ると1本もどる";
        }
    }
}
