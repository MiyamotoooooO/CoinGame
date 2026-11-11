using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N030 雪国「しんしん雪だるま」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：盤面に雪が舞い、手前のメダルが半透明の水色の氷メダルに変わる（つるつるすべる）
    /// ・モニター：雪の村。雪だるまが、氷メダルが中央に落ちるたびに1段ずつ大きくなる。盤面の氷メダルの数、次の雪までの時間
    /// ・ルール：
    ///   発動した瞬間、盤面の手前側にあるメダル firstFreeze 枚が氷メダルになる（CSV のとおり）。よくすべり、中央の穴で +iceBonus pt
    ///   その後も snowInterval 秒ごとに雪が降り、手前のメダル moreFreeze 枚が氷メダルになる
    ///   氷メダルが続けて snowmanCount 枚、中央の穴に落ちると「雪だるま完成」：+snowmanBonus pt（端の穴に落ちると最初から）
    /// ・すべる氷メダルを、中央の穴へうまく流す遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class SnowCountryRecord : RecordScriptBase
    {
        public static SnowCountryRecord Instance { get; private set; }

        public const string IceKind = "Ice";

        [Header("氷メダル")]
        [Range(0, 50)] public int firstFreeze = 8;
        [Range(2f, 60f)] public float snowInterval = 10f;
        [Range(0, 30)] public int moreFreeze = 3;
        [Tooltip("すべりにくさ（0 でつるつる）")]
        [Range(0f, 1f)] public float iceFriction = 0.03f;
        [Range(0, 200)] public int iceBonus = 20;

        [Header("雪だるま")]
        [Range(2, 10)] public int snowmanCount = 3;
        [Range(0, 300)] public int snowmanBonus = 50;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip freezeSound;
        public AudioClip iceSound;
        public AudioClip snowmanSound;

        protected override string DefaultKey => "SnowCountry";
        protected override string ScreenTitle => "SNOW COUNTRY";
        protected override string ScreenSubtitle => "しんしん雪だるま";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<SnowCountryRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        public static readonly Color IceColor = new Color32(0x9c, 0xe8, 0xff, 0xff);
        static readonly Color SnowWhite = new Color32(0xf4, 0xfa, 0xff, 0xff);
        static readonly Color NightBlue = new Color32(0x16, 0x22, 0x3c, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        float snowTimer;
        int streak, snowmen;
        readonly List<Medal> pick = new List<Medal>();

        ParticleSystem snow, frost;

        readonly List<Image> snowman = new List<Image>();
        Text iceText, timerText, statusText, snowmanText;
        AudioClip madeFreeze, madeIce, madeSnowman;

        protected override void BuildBoard()
        {
            snow = Particles("Snow", Board, Vector3.down, D * 0.8f, D * 0.1f, 4f, 0.05f, 10f);
            snow.transform.position = new Vector3(0f, Layout.wallHeight * 1.2f, (FrontZ + BackZ) * 0.5f);
            var shape = snow.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Layout.boardWidth, 0.1f, BackZ - FrontZ);
            var main = snow.main;
            main.loop = true;
            main.maxParticles = 800;
            frost = Particles("Frost", Board, Vector3.up, D * 1.2f, D * 0.12f, 0.8f, 0f, 60f);
            MedalMarks.RegisterEffect(IceKind, (m, type) =>
            {
                if (effects == null) return;
                if (type != DropZoneType.Center) { streak = 0; return; }
                effects.AddPoints(iceBonus);
                streak++;
                Play(iceSound, ref madeIce, () => RecordSynth.Notes("SnowIce", new[] { 2637f, 3520f }, 0.03f, 0.3f, 0.2f, RecordSynth.Wave.Bell), 0.7f);
                Pop($"氷 +{iceBonus}", IceColor, new Vector2(BodyW * 0.65f, 300f));
                if (streak >= snowmanCount)
                {
                    streak = 0;
                    snowmen++;
                    effects.AddPoints(snowmanBonus);
                    Play(snowmanSound, ref madeSnowman, () => RecordSynth.Notes("SnowMan", new[] { RecordSynth.Note(12), RecordSynth.Note(16), RecordSynth.Note(19), RecordSynth.Note(24) }, 0.12f, 0.7f, 0.3f, RecordSynth.Wave.Bell));
                    PopCenter($"雪だるま完成！ +{snowmanBonus}", SnowWhite);
                    Speaker(0.9f, 12, IceColor);
                }
            });
        }

        protected override void OnBegin()
        {
            streak = 0;
            snowmen = 0;
            snowTimer = snowInterval;
            Freeze(firstFreeze);
        }

        void Freeze(int count)
        {
            if (count <= 0) return;
            pick.Clear();
            foreach (var m in BoardMedals)
            {
                if (m == null || m.Consumed || MedalMarks.Is(m, IceKind)) continue;
                if (m.transform.position.z > PusherFrontZ || m.transform.position.y < -D * 0.2f) continue;
                pick.Add(m);
            }
            pick.Sort((a, b) => a.transform.position.z.CompareTo(b.transform.position.z));   // 手前から
            int n = 0;
            foreach (var m in pick)
            {
                if (n >= count) break;
                MedalMarks.Set(m, IceKind, IceColor, 1.5f);
                MedalMarks.SetSlippery(m, iceFriction);
                Emit(frost, m.transform.position, SnowWhite, 6);
                n++;
            }
            if (n > 0) Play(freezeSound, ref madeFreeze, () => RecordSynth.Noise("SnowFreeze", 0.8f, 0.9f, 0.05f, 0.4f, 0.4f, 21));
        }

        protected override void Tick(float dt, bool running)
        {
            if (!running) return;
            snowTimer -= dt;
            if (snowTimer <= 0f)
            {
                snowTimer = snowInterval;
                Freeze(moreFreeze);
            }
        }

        protected override bool KeepBoardVisible() => (snow != null && snow.particleCount > 0) || (frost != null && frost.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            var e = snow.emission;
            e.rateOverTime = Active ? 90f : 0f;
            if (Active && !snow.isPlaying) snow.Play();
        }

        protected override void BuildScreen()
        {
            snowman.Clear();
            float W = BodyW;
            var village = Panel(Screen, "Village", W * 0.5f, 200f, W - 48f, 360f, NightBlue);
            float vw = W - 48f;
            Img(village, "Ground", vw * 0.5f, 320f, vw, 80f, SnowWhite);
            for (int i = 0; i < 4; i++)
            {
                float x = vw * (0.55f + i * 0.12f);
                Img(village, $"House{i}", x, 260f, 80f, 60f, new Color32(0x6a, 0x3a, 0x2a, 0xff));
                Img(village, $"Roof{i}", x, 222f, 100f, 26f, SnowWhite, MonitorDisplay.ScreenShape.Pill);
                Img(village, $"Window{i}", x, 262f, 18f, 18f, new Color32(0xff, 0xd8, 0x7a, 0xff));
            }
            float sx = vw * 0.22f;
            for (int i = 0; i < snowmanCount; i++)
            {
                float size = 120f - i * 26f;
                snowman.Add(Img(village, $"Ball{i}", sx, 300f - i * (size * 0.75f) - 20f, size, size, SnowWhite, MonitorDisplay.ScreenShape.Circle));
            }
            snowmanText = Lbl(village, "Snowman", sx, 40f, 300f, 36f, "", 22, Ink);
            iceText = Lbl(Screen, "Ice", 24f, 440f, 500f, 40f, "", 26, IceColor, TextAnchor.MiddleLeft);
            timerText = Lbl(Screen, "Timer", W - 24f, 440f, 400f, 40f, "", 22, Ink, TextAnchor.MiddleRight);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int i = 0; i < snowman.Count; i++) snowman[i].color = i < streak ? SnowWhite : new Color(1f, 1f, 1f, 0.15f);
            snowmanText.text = $"雪だるま {snowmen}個";
            iceText.text = $"氷メダル {MedalMarks.Count(IceKind)}枚";
            timerText.text = $"次の雪まで {Mathf.CeilToInt(snowTimer)}秒";
            statusText.text = $"氷メダルは中央の穴で +{iceBonus}pt。続けて {snowmanCount}枚で雪だるま +{snowmanBonus}pt";
        }
    }
}
