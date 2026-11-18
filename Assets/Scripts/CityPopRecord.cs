using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N004 シティポップ「ミッドナイト・ドライブ」。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・ルール：
    ///   中央の穴に落ちると、1枚ごとに +明かりの数 pt（はじめは startLights）。端の穴は 0pt のまま
    ///   lightSeconds 秒ごとに、明かりが1つ増える（最大 maxLights）
    ///   消灯：darkSeconds 秒どの穴にも落ちないと、明かりが1つ消える（1 より下にはならない）。「次の明かり」も 0 からやり直し
    ///   明かりが最大になると「CITY LIGHTS」：cityLightsSeconds 秒のあいだ、中央の穴のポイントが ×cityLightsMultiplier
    ///   （この間は消灯しない）。終わると明かりは afterLights に戻り、また増えていく
    /// ・盤面：
    ///   奥の壁の前に、ネオンのビルがせり上がる。窓の明かりの数が、そのまま今の加算。上に大きく「+8」
    ///   手前のふちを、車のヘッドライト（白）とテールランプ（赤）が流れる（明かりが多いほど車が多い）
    ///   消灯が近づくと、一番上の明かりがチカチカする。消灯すると火花が散って、明かりが1つ消える
    ///   「CITY LIGHTS」の間は、窓がピンクと水色に光り、車が速くなる
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class CityPopRecord : RecordScriptBase
    {
        public static CityPopRecord Instance { get; private set; }

        [Header("明かり（中央の穴の加算）")]
        [Range(1, 30)] public int startLights = 2;
        [Range(1, 30)] public int maxLights = 12;
        [Tooltip("明かりが1つ増える秒数")]
        [Range(1f, 60f)] public float lightSeconds = 10f;
        [Tooltip("この秒数どの穴にも落ちないと、明かりが1つ消える")]
        [Range(1f, 30f)] public float darkSeconds = 6f;

        [Header("CITY LIGHTS（明かりが最大）")]
        [Range(1f, 60f)] public float cityLightsSeconds = 10f;
        [Range(1f, 5f)] public float cityLightsMultiplier = 2f;
        [Tooltip("CITY LIGHTS が終わった時の明かりの数")]
        [Range(1, 30)] public int afterLights = 6;

        [Header("盤面")]
        [Range(2, 12)] public int buildingCount = 8;
        [Range(0, 16)] public int carCount = 8;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip lightUpSound;
        public AudioClip darkSound;
        public AudioClip cityLightsSound;

        protected override string DefaultKey => "CityPop";
        protected override string ScreenTitle => "CITY POP";
        protected override string ScreenSubtitle => "ミッドナイト・ドライブ";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<CityPopRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Pink = new Color32(0xff, 0x5c, 0xc8, 0xff);
        static readonly Color Cyan = new Color32(0x4c, 0xe8, 0xff, 0xff);
        static readonly Color WindowOn = new Color32(0xff, 0xd8, 0x80, 0xff);
        static readonly Color WindowOff = new Color32(0x1c, 0x1a, 0x30, 0xff);
        static readonly Color BuildingColor = new Color32(0x24, 0x20, 0x3c, 0xff);
        static readonly Color Head = new Color32(0xff, 0xf6, 0xe0, 0xff);
        static readonly Color Tail = new Color32(0xff, 0x30, 0x40, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);

        int lights;
        float lightTimer, darkTimer, cityTimer, upFlash, dropPulse;
        bool City => cityTimer > 0f;

        Transform skyline;
        readonly List<Material> windowMats = new List<Material>();
        readonly List<Vector3> windowPos = new List<Vector3>();
        readonly List<Material> neonMats = new List<Material>();
        readonly List<Transform> cars = new List<Transform>();
        readonly List<float> carOffset = new List<float>();
        Light cityGlow;
        ParticleSystem sparks;
        BoardText addText, subText;
        AudioClip madeUp, madeDark, madeCity;

        float SkylineZ => BackZ - D * 0.2f;
        float SkylineY => Layout.pusherHeight;

        protected override void BuildBoard()
        {
            skyline = new GameObject("Skyline").transform;
            skyline.SetParent(Board, false);
            skyline.position = new Vector3(0f, SkylineY, SkylineZ);

            var rand = new System.Random(4);
            int n = Mathf.Max(2, buildingCount);
            float width = HalfW * 1.9f;
            float maxH = HalfW * 0.5f;
            var widths = new float[n];
            var heights = new float[n];
            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                widths[i] = 0.7f + (float)rand.NextDouble() * 0.6f;
                heights[i] = maxH * (0.45f + (float)rand.NextDouble() * 0.55f);
                sum += widths[i];
            }
            // 窓（明かり）を、高いビルほど多めに配る
            var perBuilding = new int[n];
            for (int k = 0; k < maxLights; k++)
            {
                int best = 0;
                float bestScore = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    float score = heights[i] / (perBuilding[i] + 1f);
                    if (score > bestScore) { bestScore = score; best = i; }
                }
                perBuilding[best]++;
            }

            var body = Glow(BuildingColor, 0.2f);
            float x = -width * 0.5f;
            float depth = D * 0.3f;
            for (int i = 0; i < n; i++)
            {
                float w = width * widths[i] / sum;
                float cx = x + w * 0.5f;
                Cube($"Building{i}", skyline, new Vector3(cx, heights[i] * 0.5f, 0f), new Vector3(w * 0.92f, heights[i], depth), body);
                var neon = Glow(i % 2 == 0 ? Pink : Cyan, 1.5f);
                neonMats.Add(neon);
                Cube($"Neon{i}", skyline, new Vector3(cx, heights[i] + D * 0.04f, -depth * 0.5f), new Vector3(w * 0.8f, D * 0.05f, D * 0.05f), neon);
                // 窓：下から上へ並べる（明かりは下の窓から順につく）
                int wn = perBuilding[i];
                for (int k = 0; k < wn; k++)
                {
                    float wy = heights[i] * (k + 0.6f) / (wn + 0.4f);
                    var mat = Glow(WindowOff, 0f);
                    var local = new Vector3(cx, wy, -depth * 0.5f - D * 0.01f);
                    Cube($"Window{i}_{k}", skyline, local, new Vector3(w * 0.4f, heights[i] / (wn + 1f) * 0.45f, D * 0.02f), mat);
                    windowMats.Add(mat);
                    windowPos.Add(local);
                }
                x += w;
            }
            // 明かりがつく順：下の段から（高さの順）
            var order = new List<int>();
            for (int i = 0; i < windowMats.Count; i++) order.Add(i);
            order.Sort((a, b) => windowPos[a].y.CompareTo(windowPos[b].y));
            var sortedMats = new List<Material>();
            var sortedPos = new List<Vector3>();
            foreach (int i in order) { sortedMats.Add(windowMats[i]); sortedPos.Add(windowPos[i]); }
            windowMats.Clear(); windowMats.AddRange(sortedMats);
            windowPos.Clear(); windowPos.AddRange(sortedPos);

            // 車の光（手前のふち）
            for (int i = 0; i < carCount; i++)
            {
                bool head = i % 2 == 0;
                var car = Shape(PrimitiveType.Sphere, $"Car{i}", Board, Vector3.zero, new Vector3(D * 0.22f, D * 0.1f, D * 0.1f), Glow(head ? Head : Tail, 3f));
                cars.Add(car.transform);
                carOffset.Add((float)rand.NextDouble());
            }

            cityGlow = PointLight("CityGlow", skyline, new Vector3(0f, maxH * 0.6f, -D * 1.5f), Pink, HalfW * 1.8f);
            sparks = Particles("Sparks", Board, Vector3.up, D * 3f, D * 0.08f, 0.7f, 0.6f, 40f);
            addText = BoardLabel("AddText", Board, new Vector3(0f, SkylineY + maxH + D * 1.4f, SkylineZ), "", D * 1.3f, Ink);
            subText = BoardLabel("SubText", Board, new Vector3(0f, SkylineY + maxH + D * 0.5f, SkylineZ), "", D * 0.45f, Ink);
        }

        protected override void OnBegin()
        {
            lights = Mathf.Clamp(startLights, 1, maxLights);
            lightTimer = 0f;
            darkTimer = 0f;
            cityTimer = 0f;
        }

        protected override void Tick(float dt, bool running)
        {
            upFlash = Mathf.MoveTowards(upFlash, 0f, dt * 2f);
            dropPulse = Mathf.MoveTowards(dropPulse, 0f, dt * 3f);
            if (!running) return;

            if (City)
            {
                cityTimer -= dt;
                if (cityTimer <= 0f)
                {
                    cityTimer = 0f;
                    lights = Mathf.Clamp(afterLights, 1, maxLights);
                    lightTimer = 0f;
                    darkTimer = 0f;
                }
                return;
            }

            lightTimer += dt;
            if (lightTimer >= lightSeconds)
            {
                lightTimer = 0f;
                if (lights < maxLights)
                {
                    lights++;
                    upFlash = 1f;
                    Play(lightUpSound, ref madeUp, () => RecordSynth.Notes("CityUp", new[] { RecordSynth.Note(16), RecordSynth.Note(19) }, 0.07f, 0.4f, 0.2f, RecordSynth.Wave.Triangle), 0.7f);
                    if (lights >= maxLights) StartCityLights();
                }
            }

            darkTimer += dt;
            if (darkTimer >= darkSeconds)
            {
                darkTimer = 0f;
                lightTimer = 0f;
                if (lights > 1)
                {
                    Emit(sparks, TopWindowWorld(), WindowOn, 24);
                    lights--;
                    Play(darkSound, ref madeDark, () => RecordSynth.Noise("CityDark", 0.4f, 0.1f, 0.01f, 0.3f, 0.6f, 3), 0.7f);
                }
            }
        }

        void StartCityLights()
        {
            cityTimer = cityLightsSeconds;
            Play(cityLightsSound, ref madeCity, () => RecordSynth.Chord("CityLights", new[] { RecordSynth.Note(0), RecordSynth.Note(4), RecordSynth.Note(7), RecordSynth.Note(11), RecordSynth.Note(14) }, 1.8f, 0.3f, RecordSynth.Wave.Triangle));
            BoardPop("CITY LIGHTS", Pink, new Vector3(0f, SkylineY + HalfW * 0.3f, SkylineZ - D), D * 1.2f);
            Speaker(1f, 12, Pink);
        }

        Vector3 TopWindowWorld()
        {
            int i = Mathf.Clamp(lights - 1, 0, windowPos.Count - 1);
            return windowPos.Count > 0 && skyline != null ? skyline.TransformPoint(windowPos[i]) : new Vector3(0f, SkylineY, SkylineZ);
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            darkTimer = 0f;
            if (type != DropZoneType.Center) return;
            int extra = lights;
            if (City) extra += AddMultiplied(cityLightsMultiplier);
            AddPoints(lights);
            dropPulse = 1f;
            BoardPop($"+{CenterPoints + extra}", City ? Pink : WindowOn, new Vector3(medal.transform.position.x, D * 1.2f, FrontZ + D * 0.5f), D * 0.7f);
        }

        protected override bool KeepBoardVisible() => sparks != null && sparks.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            float t = Time.time;
            float shown = Ease.OutCubic(BoardShown);
            if (skyline != null) skyline.localScale = new Vector3(1f, Mathf.Max(0.001f, shown), 1f);   // 下からせり上がる

            bool warn = Active && !City && lights > 1 && darkTimer > darkSeconds - 2f;
            for (int i = 0; i < windowMats.Count; i++)
            {
                bool on = Active && i < lights;
                Color c = WindowOn;
                float g = 2f;
                if (City)
                {
                    c = ((i + Mathf.FloorToInt(t * 6f)) % 2 == 0) ? Pink : Cyan;
                    g = 3f;
                }
                if (on && i == lights - 1)
                {
                    if (warn && Mathf.Repeat(t * 10f, 1f) < 0.5f) on = false;   // 消灯が近い：チカチカ
                    g += 2f * upFlash;
                }
                SetGlow(windowMats[i], on ? c : WindowOff, on ? g : 0f);
            }
            for (int i = 0; i < neonMats.Count; i++)
            {
                Color c = i % 2 == 0 ? Pink : Cyan;
                float pulse = City ? 2.5f + 1.5f * Mathf.Sin(t * 10f + i) : 1.2f + 0.3f * Mathf.Sin(t * 2f + i);
                SetGlow(neonMats[i], c, pulse * shown);
            }

            // 車：明かりが多いほど多く走る。CITY LIGHTS の間は速い
            int running = Active ? Mathf.Clamp(Mathf.CeilToInt(cars.Count * lights / (float)Mathf.Max(1, maxLights)), 1, cars.Count) : 0;
            float speed = City ? 0.45f : 0.2f;
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                if (car == null) continue;
                bool on = i < running && BoardShown > 0.01f;
                if (car.gameObject.activeSelf != on) car.gameObject.SetActive(on);
                if (!on) continue;
                bool head = i % 2 == 0;
                float u = Mathf.Repeat(carOffset[i] + t * speed * (1f + 0.15f * (i % 3)), 1f);
                float x = head ? Mathf.Lerp(HalfW, -HalfW, u) : Mathf.Lerp(-HalfW, HalfW, u);
                float z = FrontZ + D * (head ? 0.08f : 0.22f);
                car.position = new Vector3(x, D * 0.12f, z);
            }

            if (cityGlow != null)
            {
                cityGlow.intensity = BoardShown * (City ? 3f : 0.8f + 1.2f * lights / Mathf.Max(1f, maxLights));
                cityGlow.color = City ? (Mathf.Repeat(t * 2f, 1f) < 0.5f ? Pink : Cyan) : Pink;
                cityGlow.enabled = cityGlow.intensity > 0.001f;
            }

            if (addText != null)
            {
                addText.gameObject.SetActive(Active);
                addText.text = City ? $"+{lights} ×{cityLightsMultiplier:0.#}" : $"+{lights}";
                addText.Color = City ? (Mathf.Repeat(t * 3f, 1f) < 0.5f ? Pink : Cyan) : WindowOn;
                addText.Scale = 1f + 0.15f * dropPulse;
            }
            if (subText != null)
            {
                subText.gameObject.SetActive(Active);
                subText.text = City ? $"CITY LIGHTS  {Mathf.CeilToInt(cityTimer)}"
                             : warn ? $"消灯まで {Mathf.CeilToInt(darkSeconds - darkTimer)}"
                             : lights >= maxLights ? "" : $"次の明かり {Mathf.CeilToInt(lightSeconds - lightTimer)}";
                subText.Color = warn ? Color.Lerp(Ink, Pink, 0.6f) : Ink;
            }
        }
    }
}
