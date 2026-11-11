using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N011 ブラックホール「イベント・ホライズン」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：プッシャーの前の床に、渦を巻く黒い穴ができる。穴はゆっくり左右に動き、近くのメダルを回しながら吸い込む
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

        Transform hole;
        readonly List<Transform> discs = new List<Transform>();
        Material rimMaterial;
        ParticleSystem dust;

        readonly List<RectTransform> spirals = new List<RectTransform>();
        Text countText, subText, statusText;
        Image fill;
        AudioClip madeAbsorb, madeNova;

        protected override void BuildBoard()
        {
            hole = new GameObject("BlackHole").transform;
            hole.SetParent(Board, false);
            rimMaterial = Glow(Rim, 2f);
            var black = Glow(Color.black, 0f);
            for (int i = 0; i < 4; i++)
            {
                float r = D * (0.5f + i * 0.35f);
                var disc = Shape(PrimitiveType.Cylinder, $"Disc{i}", hole, new Vector3(0f, D * 0.01f + i * D * 0.002f, 0f), new Vector3(r * 2f, D * 0.003f, r * 0.7f * 2f), i == 0 ? black : rimMaterial);
                discs.Add(disc.transform);
            }
            Shape(PrimitiveType.Sphere, "Core", hole, new Vector3(0f, D * 0.1f, 0f), new Vector3(D * 0.6f, D * 0.25f, D * 0.6f), black);
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

        protected override void UpdateBoard(float dt)
        {
            hole.gameObject.SetActive(Active);
            if (!Active) return;
            hole.position = holePos;
            float size = (0.6f + 0.6f * stored / (float)Mathf.Max(1, capacity)) * BoardShown * (1f + 0.15f * absorbPulse);
            hole.localScale = Vector3.one * Mathf.Max(0.01f, size);
            for (int i = 0; i < discs.Count; i++) discs[i].localRotation = Quaternion.Euler(0f, Time.time * (90f + i * 70f), 0f);
            SetGlow(rimMaterial, Color.Lerp(Rim, Hot, stored / (float)Mathf.Max(1, capacity)), 1.5f + 2f * absorbPulse);
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
