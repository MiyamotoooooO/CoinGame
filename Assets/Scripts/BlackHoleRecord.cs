using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N011 ブラックホール「イベント・ホライズン」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：プッシャーの前の床に、ブラックホールができる（内側が白く熱く外側が紫の、渦を巻く光の円盤・真っ黒な穴・ふちの光の輪・
    ///   まわりのゆがんだ光の輪・らせんを描いて吸い込まれ続ける光の粒）。穴はゆっくり左右に動き、近くのメダルを回しながら吸い込む。
    ///   吸い込むたびに、上に光の柱が噴き出す
    /// ・モニター：吸い込んだ枚数（大きく）と、穴の大きさのメーター。満タンに近いほど画面がゆがむ
    /// ・ルール：
    ///   吸い込んだメダル1枚ごとに +absorbPoints pt。radiateEvery 枚ごとに「放射」：+radiateBonus pt
    ///   吸い込んだ枚数が capacity に達すると「超新星」：吸い込んだメダルを全部、手前（穴の方）へ一気に吐き出す
    ///   曲が終わる時も、残っているメダルを吐き出す（CSV のとおり）
    /// ・穴がメダルの山の近くに来るのを待つか、吐き出しのために穴を早くいっぱいにするかの遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class BlackHoleRecord : RecordScriptBase
    {
        public static BlackHoleRecord Instance { get; private set; }

        [Header("吸い込み")]
        [Tooltip("引き寄せる半径（メダルの直径に対する倍率）")]
        [Range(0.5f, 6f)] public float pullRadius = 2.2f;
        [Tooltip("引き寄せる強さ")]
        [Range(0f, 40f)] public float pullForce = 9f;
        [Tooltip("ここまで近づくと吸い込む（メダルの直径に対する倍率）")]
        [Range(0.1f, 2f)] public float captureRadius = 0.35f;
        [Tooltip("左右に1往復する秒数")]
        [Range(2f, 40f)] public float driftSeconds = 14f;

        [Header("ポイント")]
        [Range(0, 50)] public int absorbPoints = 3;
        [Range(1, 50)] public int radiateEvery = 10;
        [Range(0, 200)] public int radiateBonus = 20;

        [Header("超新星（吐き出し）")]
        [Range(5, 200)] public int capacity = 30;
        [Tooltip("吐き出す強さ")]
        [Range(0f, 20f)] public float ejectSpeed = 4f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip absorbSound;
        public AudioClip novaSound;

        protected override string DefaultKey => "BlackHole";
        protected override string ScreenTitle => "BLACK HOLE";
        protected override string ScreenSubtitle => "イベント・ホライズン";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<BlackHoleRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Rim = new Color32(0xb0, 0x6c, 0xff, 0xff);
        static readonly Color Hot = new Color32(0xff, 0x9a, 0x3c, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int stored, absorbedTotal;
        Vector3 holePos;
        float absorbPulse, cooldown;   // cooldown：吐き出した直後は吸い込まない
        readonly List<Medal> near = new List<Medal>();

        Transform hole, diskA, diskB, lens, jet, photon;
        Material diskMatA, diskMatB, lensMat, jetMat, photonMat, floorGlowMat;
        ParticleSystem dust, swirl;
        float jetLevel, swirlCarry;

        readonly List<RectTransform> spirals = new List<RectTransform>();
        Text countText, subText, statusText;
        Image fill;
        AudioClip madeAbsorb, madeNova;

        protected override void BuildBoard()
        {
            hole = new GameObject("BlackHole").transform;
            hole.SetParent(Board, false);
            float core = D * 0.38f;   // 黒い穴の半径

            // 床にじんわり広がる紫の光
            floorGlowMat = RecordMeshKit.Additive(RecordMeshKit.SoftDot, new Color(0.55f, 0.25f, 1f, 0.55f));
            var glow = Shape(PrimitiveType.Quad, "FloorGlow", hole, new Vector3(0f, D * 0.015f, 0f), Vector3.one * D * 5f, floorGlowMat);
            glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // 渦を巻く光の円盤（2枚を別の速さで回す）
            diskMatA = RecordMeshKit.Additive(RecordMeshKit.Accretion, Color.white);
            diskA = RecordMeshKit.Make("AccretionDisk", RecordMeshKit.Annulus(core * 1.05f, D * 1.95f, 96, 8), diskMatA, hole, new Vector3(0f, D * 0.04f, 0f), Vector3.one, Vector3.zero);
            diskMatB = RecordMeshKit.Additive(RecordMeshKit.Accretion, new Color(1f, 0.75f, 1f, 0.6f));
            diskB = RecordMeshKit.Make("AccretionDisk2", RecordMeshKit.Annulus(core * 1.1f, D * 1.5f, 96, 6), diskMatB, hole, new Vector3(0f, D * 0.06f, 0f), Vector3.one, new Vector3(0f, 77f, 0f));

            // 真っ黒な穴（光をまったく返さない）
            var black = ProtoAssets.MakeMaterial(Color.black, 0f, 0f);
            Shape(PrimitiveType.Sphere, "EventHorizon", hole, new Vector3(0f, core * 0.55f, 0f), Vector3.one * core * 2f, black);

            // 穴のふちの細い光の輪
            photonMat = RecordMeshKit.Additive(Texture2D.whiteTexture, new Color(1f, 0.85f, 0.6f, 1f));
            photon = RecordMeshKit.Make("PhotonRing", RecordMeshKit.Torus(core * 1.04f, D * 0.018f, 0f, 360f, 64, 8), photonMat, hole, new Vector3(0f, core * 0.55f, 0f), Vector3.one, new Vector3(90f, 0f, 0f));

            // まわりの空間がゆがんで見える光の輪（いつもカメラの方を向く）
            lensMat = RecordMeshKit.Additive(RecordMeshKit.LensRing, new Color(1f, 0.78f, 0.55f, 0.9f));
            var lensGo = Shape(PrimitiveType.Quad, "Lens", hole, new Vector3(0f, core * 0.55f, 0f), Vector3.one * core * 4.6f, lensMat);
            lens = lensGo.transform;

            // 吸い込んだ時に噴き出す光の柱
            jetMat = RecordMeshKit.Additive(RecordMeshKit.WaterStreaks, new Color(0.6f, 0.75f, 1f, 0.9f));
            jet = RecordMeshKit.Make("Jet", RecordMeshKit.Funnel(D * 0.04f, D * 0.22f, D * 3.5f, 2.5f, 20, 10, 0.8f), jetMat, hole, new Vector3(0f, core * 0.8f, 0f), new Vector3(1f, 0.01f, 1f), Vector3.zero);

            // らせんを描いて吸い込まれる光の粒（穴について動く）
            swirl = Particles("Swirl", hole, Vector3.up, 0f, D * 0.07f, 1.1f, 0f, 5f);
            var main = swirl.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var vel = swirl.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(4.5f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
            vel.radial = new ParticleSystem.MinMaxCurve(-D * 1.6f);
            var noise = swirl.noise;
            noise.enabled = false;

            dust = Particles("Dust", Board, Vector3.up, D * 2f, D * 0.12f, 0.8f, 0f, 70f);
        }

        protected override void OnBegin()
        {
            stored = 0;
            absorbedTotal = 0;
        }

        protected override void OnEnd()
        {
            if (stored > 0) Eject();
        }

        protected override void Tick(float dt, bool running)
        {
            absorbPulse = Mathf.MoveTowards(absorbPulse, 0f, dt * 3f);
            if (cooldown > 0f) cooldown -= dt;
            float phase = Elapsed / Mathf.Max(1f, driftSeconds) * Mathf.PI * 2f;
            holePos = new Vector3(Mathf.Sin(phase) * HalfW * 0.6f, 0f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.55f));
        }

        void FixedUpdate()
        {
            if (!Running || game == null || game.Pool == null || cooldown > 0f) return;
            float r = D * pullRadius;
            MedalsNear(holePos, r, near);
            foreach (var m in near)
            {
                if (m.Body == null || m.Consumed) continue;
                Vector3 d = holePos - m.transform.position;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist <= D * captureRadius)
                {
                    Absorb(m);
                    if (cooldown > 0f) break;   // 超新星で吐き出した
                    continue;
                }
                float k = 1f - Mathf.Clamp01(dist / r);
                Vector3 tangent = new Vector3(-d.z, 0f, d.x).normalized;
                m.Body.WakeUp();
                m.Body.AddForce((d.normalized * 1f + tangent * 0.8f) * pullForce * (0.3f + k), ForceMode.Acceleration);
            }
        }

        void Absorb(Medal m)
        {
            Emit(dust, m.transform.position, Rim, 6);
            MedalMarks.Clear(m);
            game.Pool.Despawn(m);
            stored++;
            absorbedTotal++;
            absorbPulse = 1f;
            jetLevel = 1f;
            AddPoints(absorbPoints);
            Play(absorbSound, ref madeAbsorb, () => RecordSynth.Sweep("BlackHoleAbsorb", 600f, 80f, 0.3f, 0.25f, RecordSynth.Wave.Sine), 0.5f);
            if (absorbedTotal % Mathf.Max(1, radiateEvery) == 0)
            {
                AddPoints(radiateBonus);
                Pop($"放射 +{radiateBonus}", Rim, new Vector2(BodyW * 0.5f, 120f));
            }
            if (stored >= capacity) Eject();
        }

        void Eject()
        {
            int n = stored;
            stored = 0;
            cooldown = 2.5f;
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(-60f, 60f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0.5f, -Mathf.Cos(a));
                SpawnMedal(holePos + new Vector3(Random.Range(-D * 0.3f, D * 0.3f), D * (0.6f + i * 0.12f), 0f), dir * ejectSpeed * D);
            }
            Emit(dust, holePos + Vector3.up * D * 0.3f, Hot, 80);
            Play(novaSound, ref madeNova, () => RecordSynth.Thud("BlackHoleNova", 45f, 1.2f, 1f));
            PopCenter($"超新星！ {n}枚", Hot);
            Shake(0.05f, 0.6f);
            Speaker(1f, 16, Hot);
        }

        protected override bool KeepBoardVisible() => dust != null && dust.particleCount > 0;

        static readonly Color[] SwirlColors =
        {
            new Color(1f, 0.95f, 0.85f), new Color(1f, 0.7f, 0.3f), new Color(1f, 0.45f, 0.2f), new Color(0.75f, 0.4f, 1f), new Color(0.55f, 0.6f, 1f),
        };

        protected override void UpdateBoard(float dt)
        {
            hole.gameObject.SetActive(Active);
            if (!Active) return;
            hole.position = holePos;
            float fill = stored / (float)Mathf.Max(1, capacity);
            float size = (0.75f + 0.55f * fill) * BoardShown * (1f + 0.15f * absorbPulse);
            hole.localScale = Vector3.one * Mathf.Max(0.01f, size);
            float t = Time.time;
            diskA.localRotation = Quaternion.Euler(0f, t * (140f + 120f * fill), 0f);
            diskB.localRotation = Quaternion.Euler(0f, 77f + t * (95f + 80f * fill), 0f);
            // いっぱいに近いほど、光が熱く（白く）なる
            RecordMeshKit.SetColor(diskMatA, Color.Lerp(new Color(1f, 1f, 1f, 0.95f), new Color(1f, 0.85f, 0.7f, 1f), fill) * (1f + 0.5f * absorbPulse));
            RecordMeshKit.SetColor(photonMat, Color.Lerp(new Color(1f, 0.85f, 0.6f, 0.9f), Color.white, absorbPulse) * (0.85f + 0.15f * Mathf.Sin(t * 20f)));
            RecordMeshKit.SetColor(floorGlowMat, Color.Lerp(new Color(0.55f, 0.25f, 1f, 0.5f), new Color(1f, 0.5f, 0.2f, 0.6f), fill));
            var cam = Camera.main;
            if (cam != null) lens.rotation = Quaternion.LookRotation(lens.position - cam.transform.position, Vector3.up);
            lens.Rotate(0f, 0f, t * 30f % 360f, Space.Self);
            RecordMeshKit.SetColor(lensMat, new Color(1f, 0.78f, 0.55f, 0.6f + 0.3f * absorbPulse));

            // 光の柱（吸い込んだ時に噴き出して、だんだん消える）
            jetLevel = Mathf.MoveTowards(jetLevel, 0f, dt * 1.6f);
            jet.localScale = new Vector3(1f + jetLevel * 0.5f, Mathf.Max(0.01f, jetLevel), 1f + jetLevel * 0.5f);
            jet.localRotation = Quaternion.Euler(0f, t * 600f, 0f);
            RecordMeshKit.Scroll(jetMat, 0f, -dt * 2f);

            // らせんの光の粒
            swirlCarry += dt * (70f + 120f * fill);
            int n = Mathf.FloorToInt(swirlCarry);
            swirlCarry -= n;
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float r = D * Random.Range(1.4f, 2.1f);
                var ep = new ParticleSystem.EmitParams
                {
                    position = new Vector3(Mathf.Cos(a) * r, D * Random.Range(0.03f, 0.2f), Mathf.Sin(a) * r),
                    velocity = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * D * 0.6f,
                    startColor = SwirlColors[Random.Range(0, SwirlColors.Length)],
                    startSize = D * Random.Range(0.04f, 0.09f),
                    startLifetime = Random.Range(0.6f, 1.1f),
                    applyShapeToPosition = false,
                };
                swirl.Emit(ep, 1);
            }
        }

        protected override void BuildScreen()
        {
            spirals.Clear();
            float W = BodyW;
            var space = Panel(Screen, "Space", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x06, 0x05, 0x10, 0xff));
            float sw = W - 48f;
            for (int i = 0; i < 40; i++)
                Img(space, $"Star{i}", Random.Range(0f, sw), Random.Range(0f, 360f), 3f, 3f, new Color(1f, 1f, 1f, Random.Range(0.2f, 0.8f)), MonitorDisplay.ScreenShape.Circle);
            for (int i = 0; i < 4; i++)
            {
                var s = Img(space, $"Spiral{i}", sw * 0.3f, 180f, 280f - i * 50f, 120f - i * 22f, new Color(Rim.r, Rim.g, Rim.b, 0.25f + i * 0.15f), MonitorDisplay.ScreenShape.Ring);
                spirals.Add(s.rectTransform);
            }
            Img(space, "Core", sw * 0.3f, 180f, 90f, 90f, Color.black, MonitorDisplay.ScreenShape.Circle);
            countText = Lbl(space, "Count", sw * 0.75f, 150f, sw * 0.45f, 140f, "", 110, Ink, TextAnchor.MiddleCenter, true);
            subText = Lbl(space, "Sub", sw * 0.75f, 240f, sw * 0.45f, 30f, "", 20, Mute);
            Lbl(Screen, "CapLabel", 24f, 450f, 160f, 30f, "超新星まで", 18, Ink, TextAnchor.MiddleLeft);
            fill = Bar(Screen, "Cap", 180f, 450f, W - 300f, 20f, Rim);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            for (int i = 0; i < spirals.Count; i++)
            {
                spirals[i].localRotation = Quaternion.Euler(0f, 0f, Time.time * (40f + i * 30f));
                Scale(spirals[i], 1f + 0.1f * absorbPulse + 0.4f * stored / (float)Mathf.Max(1, capacity));
            }
            countText.text = $"{stored}";
            subText.text = $"吸い込んだ枚数（合計 {absorbedTotal}）";
            fill.fillAmount = stored / (float)Mathf.Max(1, capacity);
            fill.color = Color.Lerp(Rim, Hot, fill.fillAmount);
            statusText.text = $"吸い込むたびに +{absorbPoints}pt、{radiateEvery}枚ごとに放射 +{radiateBonus}pt。{capacity}枚で超新星（手前へ吐き出す）";
        }
    }
}
