using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N019 天気予報「198X ウェザーニュース」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：今の天気に合わせて、雨粒・雪・稲光が降る。晴れの時は暖かい光が当たる
    /// ・モニター：天気予報の番組。お天気キャスター、今の天気マーク、「次の天気」の予報と、変わるまでの時間
    /// ・ルール：changeSeconds 秒ごとに天気が変わる。次の天気は前もって予報に出る
    ///   晴れ：中央の穴のポイント ×sunnyMultiplier
    ///   雨  ：メダルがすべりやすくなる（すべりにくさ ×rainFriction）。中央の穴 +rainBonus pt
    ///   雷  ：thunderEvery 秒ごとに盤面のどこかに雷が落ち、まわりのメダルがはじける。画面が揺れる
    ///   雪  ：プッシャーがゆっくり（×snowPusher）。中央の穴に落ちるたびにゲージ +snowGauge
    /// ・予報を見て、晴れや雷の時にメダルを多く入れ、雪の時は控えるなどの判断をする遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// 物理の変更（すべりやすさ・プッシャー）は、効果が終わると元に戻る。
    /// </summary>
    [DisallowMultipleComponent]
    public class WeatherRecord : RecordScriptBase
    {
        public static WeatherRecord Instance { get; private set; }

        public enum Kind { Sunny, Rain, Thunder, Snow }

        [Header("天気")]
        [Range(3f, 60f)] public float changeSeconds = 10f;

        [Header("晴れ")]
        [Range(1f, 5f)] public float sunnyMultiplier = 1.5f;
        [Header("雨")]
        [Range(0.05f, 1f)] public float rainFriction = 0.4f;
        [Range(0, 50)] public int rainBonus = 3;
        [Header("雷")]
        [Range(0.5f, 10f)] public float thunderEvery = 2.5f;
        [Range(0f, 20f)] public float thunderStrength = 5f;
        [Header("雪")]
        [Range(0.1f, 1f)] public float snowPusher = 0.5f;
        [Range(0f, 10f)] public float snowGauge = 2f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip chimeSound;
        public AudioClip thunderSound;

        protected override string DefaultKey => "Weather";
        protected override string ScreenTitle => "WEATHER";
        protected override string ScreenSubtitle => "198X ウェザーニュース";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<WeatherRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly string[] KindNames = { "晴れ", "雨", "雷", "雪" };
        static readonly Color[] KindColors =
        {
            new Color32(0xff, 0xb8, 0x3a, 0xff), new Color32(0x4f, 0x9c, 0xf7, 0xff), new Color32(0xff, 0xee, 0x58, 0xff), new Color32(0xd8, 0xee, 0xff, 0xff),
        };
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Studio = new Color32(0x10, 0x1c, 0x34, 0xff);

        Kind now, next;
        float changeTimer, thunderTimer, boltTimer;
        Vector3 boltPos;

        ParticleSystem rain, snow, sparks;
        Light sun, bolt;
        GameObject boltLine;

        RectTransform iconNow, iconNext;
        Text nowText, nextText, timerText, statusText, effectText;
        Image nowBox;
        AudioClip madeChime, madeThunder;

        protected override void BuildBoard()
        {
            float top = Layout.wallHeight * 1.3f;
            float midZ = (FrontZ + BackZ) * 0.5f;
            rain = Particles("Rain", Board, Vector3.down, D * 10f, D * 0.06f, 0.6f, 1f, 3f);
            rain.transform.position = new Vector3(0f, top, midZ);
            var shape = rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Layout.boardWidth, 0.1f, BackZ - FrontZ);
            snow = Particles("Snow", Board, Vector3.down, D * 1f, D * 0.1f, 4f, 0.05f, 10f);
            snow.transform.position = new Vector3(0f, top, midZ);
            var shape2 = snow.shape;
            shape2.shapeType = ParticleSystemShapeType.Box;
            shape2.scale = new Vector3(Layout.boardWidth, 0.1f, BackZ - FrontZ);
            sparks = Particles("Sparks", Board, Vector3.up, D * 4f, D * 0.08f, 0.6f, 1f, 60f);
            var rainMain = rain.main;
            rainMain.loop = true;      // 雨と雪は降り続ける（量は emission で変える）
            rainMain.maxParticles = 1500;
            var snowMain = snow.main;
            snowMain.loop = true;
            snowMain.maxParticles = 1000;

            sun = PointLight("Sun", Board, new Vector3(0f, Layout.wallHeight * 1.5f, FrontZ), KindColors[0], Layout.boardWidth * 1.5f);
            bolt = PointLight("Bolt", Board, Vector3.zero, Color.white, Layout.boardWidth);
            boltLine = Cube("BoltLine", Board, Vector3.zero, new Vector3(D * 0.08f, 1f, D * 0.08f), Glow(KindColors[2], 6f));
            boltLine.SetActive(false);
        }

        protected override void OnBegin()
        {
            now = (Kind)Random.Range(0, 4);
            next = PickNext(now);
            changeTimer = changeSeconds;
            thunderTimer = thunderEvery;
        }

        static Kind PickNext(Kind current)
        {
            Kind k = current;
            while (k == current) k = (Kind)Random.Range(0, 4);
            return k;
        }

        protected override void Tick(float dt, bool running)
        {
            boltTimer = Mathf.MoveTowards(boltTimer, 0f, dt * 3f);
            MedalFrictionScale = now == Kind.Rain ? rainFriction : 1f;
            PusherSpeedScale = now == Kind.Snow ? snowPusher : 1f;
            if (!running) return;

            changeTimer -= dt;
            if (changeTimer <= 0f)
            {
                changeTimer = changeSeconds;
                now = next;
                next = PickNext(now);
                Play(chimeSound, ref madeChime, () => RecordSynth.Notes("WeatherChime", new[] { RecordSynth.Note(16), RecordSynth.Note(12), RecordSynth.Note(7), RecordSynth.Note(0) }, 0.18f, 0.6f, 0.25f, RecordSynth.Wave.Bell));
                PopCenter($"天気が「{KindNames[(int)now]}」に変わった", KindColors[(int)now]);
            }
            if (now == Kind.Thunder)
            {
                thunderTimer -= dt;
                if (thunderTimer <= 0f)
                {
                    thunderTimer = thunderEvery;
                    Strike();
                }
            }
        }

        void Strike()
        {
            boltPos = RandomFloorPoint();
            boltTimer = 1f;
            Blast(boltPos, D * 1.5f, thunderStrength, 0.8f);
            Emit(sparks, boltPos, KindColors[2], 30);
            Shake(0.06f, 0.35f);
            Play(thunderSound, ref madeThunder, () => RecordSynth.Noise("WeatherThunder", 1.4f, 0.08f, 0.01f, 0.5f, 1.3f, 11));
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            switch (now)
            {
                case Kind.Sunny:
                    int extra = AddMultiplied(sunnyMultiplier);
                    Pop($"晴れ +{CenterPoints + extra}", KindColors[0], new Vector2(BodyW * Random.Range(0.3f, 0.7f), 300f));
                    break;
                case Kind.Rain:
                    AddPoints(rainBonus);
                    break;
                case Kind.Snow:
                    AddGauge(snowGauge);
                    break;
            }
        }

        protected override bool KeepBoardVisible() =>
            (rain != null && rain.particleCount > 0) || (snow != null && snow.particleCount > 0) || (sparks != null && sparks.particleCount > 0);

        protected override void UpdateBoard(float dt)
        {
            var re = rain.emission;
            re.rateOverTime = Active && now == Kind.Rain ? 400f : 0f;
            if (Active && now == Kind.Rain && !rain.isPlaying) rain.Play();
            var se = snow.emission;
            se.rateOverTime = Active && now == Kind.Snow ? 120f : 0f;
            if (Active && now == Kind.Snow && !snow.isPlaying) snow.Play();
            sun.intensity = Active && now == Kind.Sunny ? 2.5f * BoardShown : 0f;
            sun.enabled = sun.intensity > 0f;
            bolt.intensity = boltTimer * 8f;
            bolt.enabled = boltTimer > 0f;
            bolt.transform.position = boltPos + Vector3.up * D * 1.5f;
            boltLine.SetActive(boltTimer > 0.4f);
            if (boltTimer > 0.4f)
            {
                float h = Layout.wallHeight * 1.5f;
                boltLine.transform.position = boltPos + Vector3.up * h * 0.5f;
                boltLine.transform.localScale = new Vector3(D * 0.08f, h, D * 0.08f);
                boltLine.transform.rotation = Quaternion.Euler(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
            }
        }

        RectTransform MakeIcon(Transform parent, string name, float cx, float cy, float size)
        {
            var g = Grp(parent, name, cx, cy, size, size);
            return g;
        }

        void DrawIcon(RectTransform icon, Kind k, float size)
        {
            for (int i = icon.childCount - 1; i >= 0; i--) Destroy(icon.GetChild(i).gameObject);
            float c = size * 0.5f;
            switch (k)
            {
                case Kind.Sunny:
                    for (int i = 0; i < 8; i++)
                    {
                        var ray = Img(icon, $"Ray{i}", c, c, size * 0.08f, size * 0.95f, KindColors[0]);
                        ray.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 22.5f);
                    }
                    Img(icon, "Sun", c, c, size * 0.6f, size * 0.6f, KindColors[0], MonitorDisplay.ScreenShape.Circle);
                    break;
                case Kind.Rain:
                    Img(icon, "Cloud", c, c * 0.8f, size * 0.9f, size * 0.5f, new Color(0.85f, 0.88f, 0.95f), MonitorDisplay.ScreenShape.Pill);
                    for (int i = 0; i < 3; i++) Img(icon, $"Drop{i}", c - size * 0.25f + i * size * 0.25f, c * 1.5f, size * 0.07f, size * 0.22f, KindColors[1], MonitorDisplay.ScreenShape.Pill);
                    break;
                case Kind.Thunder:
                    Img(icon, "Cloud", c, c * 0.7f, size * 0.9f, size * 0.5f, new Color(0.5f, 0.5f, 0.6f), MonitorDisplay.ScreenShape.Pill);
                    var b1 = Img(icon, "Bolt1", c, c * 1.3f, size * 0.12f, size * 0.45f, KindColors[2]);
                    b1.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 20f);
                    break;
                default:
                    for (int i = 0; i < 3; i++)
                    {
                        var arm = Img(icon, $"Arm{i}", c, c, size * 0.08f, size * 0.85f, KindColors[3]);
                        arm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 60f);
                    }
                    Img(icon, "Core", c, c, size * 0.25f, size * 0.25f, KindColors[3], MonitorDisplay.ScreenShape.Circle);
                    break;
            }
        }

        Kind shownNow = (Kind)(-1), shownNext = (Kind)(-1);

        protected override void BuildScreen()
        {
            float W = BodyW;
            var studio = Panel(Screen, "Studio", W * 0.5f, 200f, W - 48f, 360f, Studio);
            float sw = W - 48f;
            Tag(studio, "News", 110f, 30f, 180f, 34f, "WEATHER NEWS", 16, KindColors[0], new Color32(0x10, 0x10, 0x18, 0xff));
            // キャスター（仮の絵）
            float cx = sw * 0.15f;
            Img(studio, "CasterBody", cx, 290f, 120f, 150f, new Color32(0x3a, 0x5a, 0xa0, 0xff), MonitorDisplay.ScreenShape.Pill);
            Img(studio, "CasterHead", cx, 180f, 80f, 80f, new Color32(0xff, 0xe0, 0xc8, 0xff), MonitorDisplay.ScreenShape.Circle);
            Img(studio, "CasterHair", cx, 160f, 90f, 50f, new Color32(0x40, 0x2a, 0x20, 0xff), MonitorDisplay.ScreenShape.Pill);
            nowBox = Img(studio, "NowBox", sw * 0.45f, 190f, 260f, 260f, new Color(1f, 1f, 1f, 0.06f), MonitorDisplay.ScreenShape.Pill);
            iconNow = MakeIcon(studio, "IconNow", sw * 0.45f, 170f, 160f);
            nowText = Lbl(studio, "Now", sw * 0.45f, 290f, 260f, 40f, "", 30, Ink);
            Lbl(studio, "NextLabel", sw * 0.78f, 70f, 300f, 30f, "次の天気", 20, Mute);
            iconNext = MakeIcon(studio, "IconNext", sw * 0.78f, 170f, 110f);
            nextText = Lbl(studio, "Next", sw * 0.78f, 250f, 300f, 36f, "", 26, Ink);
            timerText = Lbl(studio, "Timer", sw * 0.78f, 300f, 300f, 30f, "", 20, Mute);
            shownNow = (Kind)(-1);
            shownNext = (Kind)(-1);
            effectText = Lbl(Screen, "Effect", 24f, 440f, W - 48f, 40f, "", 24, Ink, TextAnchor.MiddleLeft);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            if (shownNow != now) { DrawIcon(iconNow, now, 160f); shownNow = now; }
            if (shownNext != next) { DrawIcon(iconNext, next, 110f); shownNext = next; }
            Color c = KindColors[(int)now];
            nowBox.color = new Color(c.r, c.g, c.b, 0.15f + 0.05f * Mathf.Sin(Time.time * 3f));
            nowText.text = $"いま：{KindNames[(int)now]}";
            nextText.text = KindNames[(int)next];
            timerText.text = $"あと {Mathf.CeilToInt(changeTimer)}秒";
            Scale(iconNow, 1f + 0.04f * Mathf.Sin(Time.time * 2f));
            effectText.text = now switch
            {
                Kind.Sunny => $"晴れ：中央の穴のポイント ×{sunnyMultiplier:0.#}",
                Kind.Rain => $"雨：メダルがすべりやすい・中央の穴 +{rainBonus}pt",
                Kind.Thunder => "雷：盤面に雷が落ちて、メダルがはじける",
                _ => $"雪：プッシャーがゆっくり・中央の穴でゲージ +{snowGauge:0}",
            };
            effectText.color = c;
            statusText.text = "予報を見て、メダルを入れる時と控える時を決めよう";
        }
    }
}
