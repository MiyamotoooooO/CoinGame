using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N008 海「ビッグウェーブ」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：stormInterval 秒ごとに、水しぶきをまとった竜巻が盤面に生まれ、stormSeconds 秒かけて動き回る。
    ///   竜巻は「最後にメダルを投入した側（左・右）」へ寄っていく。近くのメダルを巻き込んで持ち上げ、ぐるぐる回す
    ///   竜巻が消える時に「ビッグウェーブ」：巻き込んだメダルを手前（穴の方）へ押し出す
    /// ・モニター：海の天気図。盤面の地図の上に竜巻の位置、巻き込んだ枚数、次の竜巻までの時間
    /// ・ルール：
    ///   ビッグウェーブのあと waveWindow 秒のあいだに中央の穴に落ちると、1枚 +waveBonus pt
    ///   竜巻が巻き込んだメダル1枚ごとにゲージ +gaugePerMedal
    /// ・発射口をえらんで、竜巻を山の多い所へ連れていく遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class SeaRecord : RecordScriptBase
    {
        public static SeaRecord Instance { get; private set; }

        [Header("竜巻")]
        [Range(2f, 30f)] public float stormInterval = 8f;
        [Range(1f, 15f)] public float stormSeconds = 4f;
        [Tooltip("巻き込む半径（メダルの直径に対する倍率）")]
        [Range(0.5f, 5f)] public float stormRadius = 1.6f;
        [Tooltip("持ち上げる強さ")]
        [Range(0f, 40f)] public float liftForce = 14f;
        [Tooltip("回す強さ")]
        [Range(0f, 40f)] public float swirlForce = 10f;
        [Tooltip("投入した側へ寄っていく速さ")]
        [Range(0f, 10f)] public float followSpeed = 1.5f;

        [Header("ビッグウェーブ")]
        [Tooltip("消える時に手前へ押し出す強さ")]
        [Range(0f, 20f)] public float waveStrength = 5f;
        [Range(0f, 10f)] public float waveWindow = 3f;
        [Range(0, 100)] public int waveBonus = 10;
        [Range(0f, 5f)] public float gaugePerMedal = 0.5f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip stormSound;
        public AudioClip waveSound;
        [Tooltip("竜巻の Prefab（空なら仮の渦）")]
        public GameObject tornadoPrefab;

        protected override string DefaultKey => "Sea";
        protected override string ScreenTitle => "SEA";
        protected override string ScreenSubtitle => "ビッグウェーブ";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<SeaRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Water = new Color32(0x4f, 0xc3, 0xf7, 0xff);
        static readonly Color Foam = new Color32(0xe8, 0xf8, 0xff, 0xff);
        static readonly Color Deep = new Color32(0x0b, 0x2a, 0x4a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        float nextStorm, stormTimer, waveTimer, lastSide;
        Vector3 stormPos;
        readonly HashSet<Medal> caught = new HashSet<Medal>();
        readonly List<Medal> near = new List<Medal>();
        int caughtTotal;
        bool Storming => stormTimer > 0f;

        Transform tornado;
        readonly List<Transform> rings = new List<Transform>();
        Material ringMaterial;
        ParticleSystem spray;

        RectTransform map, stormIcon;
        Text countText, timerText, statusText;
        Image waveFill;
        float mapW, mapH;
        AudioClip madeStorm, madeWave;

        protected override void BuildBoard()
        {
            tornado = new GameObject("Tornado").transform;
            tornado.SetParent(Board, false);
            if (tornadoPrefab != null)
            {
                Instantiate(tornadoPrefab, tornado).transform.localPosition = Vector3.zero;
            }
            else
            {
                ringMaterial = Glow(Water, 1.2f);
                for (int i = 0; i < 7; i++)
                {
                    float r = D * (0.6f + i * 0.28f);
                    var ring = Shape(PrimitiveType.Cylinder, $"Ring{i}", tornado, new Vector3(0f, D * (0.2f + i * 0.35f), 0f), new Vector3(r * 2f, D * 0.03f, r * 2f), ringMaterial);
                    rings.Add(ring.transform);
                }
            }
            spray = Particles("Spray", tornado, Vector3.up, D * 3f, D * 0.15f, 1f, 0.5f, 60f);
            tornado.gameObject.SetActive(false);
        }

        protected override void OnBegin()
        {
            nextStorm = 1.5f;
            stormTimer = 0f;
            waveTimer = 0f;
            caughtTotal = 0;
            caught.Clear();
        }

        protected override void OnEnd()
        {
            if (Storming) Release();
            stormTimer = 0f;
        }

        protected override void OnInsert(Medal medal) => lastSide = medal.transform.position.x < 0f ? -1f : 1f;

        protected override void Tick(float dt, bool running)
        {
            if (waveTimer > 0f) waveTimer -= dt;
            if (!running) return;
            if (Storming)
            {
                stormTimer -= dt;
                float targetX = lastSide * HalfW * 0.55f;
                stormPos.x = Mathf.MoveTowards(stormPos.x, targetX, followSpeed * D * dt);
                stormPos.z = Mathf.Lerp(FrontZ + D * 1.2f, PusherFrontZ - D * 0.5f, 0.5f + 0.35f * Mathf.Sin(Time.time * 1.3f));
                if (stormTimer <= 0f) Release();
                return;
            }
            nextStorm -= dt;
            if (nextStorm <= 0f)
            {
                nextStorm = stormInterval;
                stormTimer = stormSeconds;
                caught.Clear();
                stormPos = new Vector3(-lastSide * HalfW * 0.6f, 0f, (FrontZ + PusherFrontZ) * 0.5f);
                Play(stormSound, ref madeStorm, () => RecordSynth.Noise("SeaStorm", 2f, 0.05f, 0.4f, 1.2f, 1f, 8));
            }
        }

        void FixedUpdate()
        {
            if (!Active || !Storming || game == null) return;
            float r = D * stormRadius;
            foreach (var m in MedalsNear(stormPos, r, near))
            {
                if (m.Body == null) continue;
                Vector3 d = m.transform.position - stormPos;
                d.y = 0f;
                Vector3 tangent = new Vector3(-d.z, 0f, d.x).normalized;
                Vector3 inward = -d.normalized;
                float k = 1f - Mathf.Clamp01(d.magnitude / r);
                float height = m.transform.position.y;
                float lift = height < D * 2.2f ? liftForce * k : -2f;
                m.Body.WakeUp();
                m.Body.AddForce((tangent * swirlForce + inward * swirlForce * 0.4f) * k + Vector3.up * lift, ForceMode.Acceleration);
                if (caught.Add(m))
                {
                    caughtTotal++;
                    AddGauge(gaugePerMedal);
                }
            }
        }

        void Release()
        {
            stormTimer = 0f;
            int n = 0;
            foreach (var m in caught)
            {
                if (m == null || !m.IsActiveInPool || m.Consumed) continue;
                Push(m, new Vector3(Random.Range(-0.3f, 0.3f), 0.3f, -1f) * waveStrength);
                n++;
            }
            caught.Clear();
            waveTimer = waveWindow;
            Emit(spray, stormPos + Vector3.up * D, Foam, 60);
            Play(waveSound, ref madeWave, () => RecordSynth.Noise("SeaWave", 1.4f, 0.12f, 0.15f, 0.6f, 1.1f, 2));
            PopCenter($"BIG WAVE!  {n}枚", Water);
            Speaker(0.8f, 8, Water);
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center || waveTimer <= 0f) return;
            AddPoints(waveBonus);
            Pop($"WAVE +{waveBonus}", Water, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 420f));
        }

        protected override bool KeepBoardVisible() => spray != null && spray.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            bool show = Active && Storming;
            if (tornado.gameObject.activeSelf != show) tornado.gameObject.SetActive(show);
            if (!show) return;
            tornado.position = stormPos;
            float grow = Mathf.Clamp01((stormSeconds - stormTimer) / 0.5f) * Mathf.Clamp01(stormTimer / 0.5f);
            tornado.localScale = Vector3.one * Mathf.Max(0.01f, grow);
            for (int i = 0; i < rings.Count; i++)
            {
                rings[i].localRotation = Quaternion.Euler(0f, Time.time * (300f + i * 60f), 0f);
                var p = rings[i].localPosition;
                rings[i].localPosition = new Vector3(Mathf.Sin(Time.time * 3f + i) * D * 0.1f * i, p.y, Mathf.Cos(Time.time * 3f + i) * D * 0.1f * i);
            }
            if (Random.value < 0.6f) Emit(spray, stormPos + new Vector3(0f, D * 0.2f, 0f), Foam, 2);
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            mapW = W - 48f;
            mapH = 340f;
            map = Panel(Screen, "Map", W * 0.5f, 64f + mapH * 0.5f, mapW, mapH, Deep);
            for (int i = 1; i < 6; i++) Img(map, $"GridX{i}", mapW * i / 6f, mapH * 0.5f, 2f, mapH, new Color(1f, 1f, 1f, 0.06f));
            for (int i = 1; i < 4; i++) Img(map, $"GridY{i}", mapW * 0.5f, mapH * i / 4f, mapW, 2f, new Color(1f, 1f, 1f, 0.06f));
            Img(map, "Hole", mapW * 0.5f, mapH - 10f, mapW * HoleHalf / Mathf.Max(0.01f, HalfW), 20f, new Color(1f, 1f, 1f, 0.25f));
            Lbl(map, "HoleLabel", mapW * 0.5f, mapH - 34f, 200f, 24f, "中央の穴", 14, Mute);
            var icon = Grp(map, "Storm", mapW * 0.5f, mapH * 0.5f, 90f, 90f);
            for (int i = 0; i < 3; i++) Img(icon, $"Swirl{i}", 45f, 45f, 90f - i * 26f, 90f - i * 26f, new Color(Water.r, Water.g, Water.b, 0.4f + i * 0.2f), MonitorDisplay.ScreenShape.Ring);
            stormIcon = icon;
            Lbl(Screen, "Title", 24f, 30f, 400f, 36f, "海の天気図", 22, Ink, TextAnchor.MiddleLeft);
            timerText = Lbl(Screen, "Timer", W - 24f, 30f, 420f, 36f, "", 22, Water, TextAnchor.MiddleRight);
            countText = Lbl(Screen, "Count", 24f, 450f, 400f, 50f, "", 30, Ink, TextAnchor.MiddleLeft);
            Lbl(Screen, "WaveLabel", W * 0.5f, 450f, 160f, 30f, "WAVE", 18, Mute, TextAnchor.MiddleRight, true);
            waveFill = Bar(Screen, "Wave", W * 0.5f + 10f, 450f, W * 0.5f - 34f, 18f, Water);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            stormIcon.gameObject.SetActive(Storming);
            if (Storming)
            {
                float x01 = Mathf.InverseLerp(-HalfW, HalfW, stormPos.x);
                float z01 = Mathf.InverseLerp(FrontZ, PusherFrontZ, stormPos.z);
                Place(stormIcon, mapW * x01, mapH * (1f - z01) * 0.9f);
                stormIcon.localRotation = Quaternion.Euler(0f, 0f, -Time.time * 360f);
            }
            timerText.text = Storming ? $"竜巻 発生中  {stormTimer:0.0}" : $"次の竜巻まで {Mathf.CeilToInt(nextStorm)}秒";
            countText.text = $"巻き込み {caught.Count}枚 <size=20>（合計 {caughtTotal}）</size>";
            waveFill.fillAmount = waveTimer / Mathf.Max(0.1f, waveWindow);
            statusText.text = waveTimer > 0f
                ? $"<color=#4FC3F7>ビッグウェーブ！</color>  いま中央の穴に落ちると +{waveBonus}pt"
                : "竜巻は、最後に投入した側へ寄っていく。メダルの山へ連れていこう";
        }
    }
}
