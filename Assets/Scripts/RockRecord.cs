using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N005 ロック「ライブハウス・ファイア」。効果の発動中（曲1曲分）だけ動く。モニターはふだんの画面のまま（盤面だけで見せる）。
    ///
    /// ・盤面：左右の端の穴の上に、炎の柱が立つ。燃え上がっている側の炎は高く、白っぽく燃える。
    ///   中央の穴に落ちると、両方の炎が「ボッ」と上がる
    ///   まん中の上に今の倍率「×2」と、ソロまでの残り（●●○）。燃えている穴の上には残り秒数
    ///   ギターソロの間は、ビートのたびに筐体のスピーカーが大きくふるえ、衝撃波の輪がプッシャーの前から手前へ走って、
    ///   盤面のメダルを少し手前へ押す（音圧で押す）
    /// ・ルール：
    ///   中央の穴のポイントが ×centerMultiplier（基本のポイントに掛かる）
    ///   fireInterval 秒ごとに、左右どちらかの端の穴が fireSeconds 秒「燃える穴」になる。
    ///   燃えている端の穴に落ちると +fireBonus pt（端の穴はふだん 0pt。燃えていない方は 0pt のまま）
    ///   燃える穴に hitsForSolo 回入れると「ギターソロ」：soloSeconds 秒のあいだ中央の穴が ×soloMultiplier。画面が揺れる。
    ///   ソロの間は soloBpm のビートごとに、盤面のメダルを手前へ soloPush の強さで押す
    /// ・発射口（左・右）をえらんで、燃えている側をねらう遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class RockRecord : RecordScriptBase
    {
        public static RockRecord Instance { get; private set; }

        [Header("倍率（中央の穴）")]
        [Range(1f, 5f)] public float centerMultiplier = 2f;

        [Header("燃える端の穴")]
        [Tooltip("燃える穴が出る間かく（秒）")]
        [Range(2f, 30f)] public float fireInterval = 8f;
        [Tooltip("燃えている秒数")]
        [Range(1f, 15f)] public float fireSeconds = 4f;
        [Tooltip("燃えている端の穴に落ちた時のポイント")]
        [Range(0, 100)] public int fireBonus = 20;

        [Header("ギターソロ")]
        [Tooltip("燃える穴に何回入れるとソロか")]
        [Range(1, 10)] public int hitsForSolo = 3;
        [Range(1f, 30f)] public float soloSeconds = 8f;
        [Range(1f, 8f)] public float soloMultiplier = 3f;
        [Tooltip("ソロ中に画面を揺らす強さ")]
        [Range(0f, 0.3f)] public float soloShake = 0.03f;
        [Tooltip("ソロ中に音圧で押すビートの速さ（BPM）")]
        [Range(60f, 240f)] public float soloBpm = 140f;
        [Tooltip("ソロ中のビートで、盤面のメダルを手前へ押す強さ")]
        [Range(0f, 3f)] public float soloPush = 0.35f;

        [Header("盤面の炎")]
        [Tooltip("燃え上がった炎の高さ（盤面の幅に対する割合）")]
        [Range(0.05f, 1f)] public float flameHeightRatio = 0.35f;
        [Range(0f, 10f)] public float flameLightIntensity = 3f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip igniteSound;
        public AudioClip fireHitSound;
        public AudioClip soloSound;

        protected override string DefaultKey => "Rock";
        protected override string ScreenTitle => "ROCK";
        protected override string ScreenSubtitle => "ライブハウス・ファイア";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<RockRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Fire = new Color32(0xff, 0x6a, 0x1a, 0xff);
        static readonly Color FireRed = new Color32(0xe8, 0x2a, 0x1e, 0xff);
        static readonly Color FireYellow = new Color32(0xff, 0xd0, 0x3a, 0xff);
        static readonly Color HotWhite = new Color32(0xff, 0xf4, 0xd8, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);

        int fireSide;            // 燃えている側（-1 左、1 右、0 なし）
        float fireTimer, nextFire, soloTimer, dropPulse, igniteTimer, beatTimer, wave = -1f;
        int hits;
        bool Solo => soloTimer > 0f;

        readonly ParticleSystem[] flames = new ParticleSystem[2];
        readonly Light[] lights = new Light[2];
        ParticleSystem embers;
        GameObject shockRing;
        Material shockMaterial;
        BoardText multText, pipText;
        readonly BoardText[] sideTimers = new BoardText[2];
        AudioClip madeIgnite, madeHit, madeSolo, madeBeat;

        float SideX(int side) => side * (HoleHalf + (HalfW - HoleHalf) * 0.5f);

        protected override void BuildBoard()
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                var holder = new GameObject(side < 0 ? "FlameLeft" : "FlameRight").transform;
                holder.SetParent(Board, false);
                holder.position = new Vector3(SideX(side), 0f, FrontZ + D * 0.3f);
                flames[i] = BuildFlame(holder);
                lights[i] = PointLight("FlameLight", holder, new Vector3(0f, D * 1.2f, D * 0.5f), Fire, Layout.boardWidth * 0.5f);
            }
            embers = Particles("Embers", Board, Vector3.up, D * 5f, D * 0.08f, 1.2f, 0.4f, 30f);

            // 音圧の衝撃波（ソロ中、ビートごとにプッシャーの前から手前へ走る光の帯）
            shockMaterial = Glow(Fire, 2f);
            shockRing = Cube("Shock", Board, Vector3.zero, new Vector3(HalfW * 2f, D * 0.03f, D * 0.12f), shockMaterial);
            shockRing.SetActive(false);

            float midZ = (FrontZ + PusherFrontZ) * 0.5f;
            multText = BoardLabel("Mult", Board, new Vector3(0f, D * 3.4f, midZ), "", D * 1.3f, Ink);
            pipText = BoardLabel("Pips", Board, new Vector3(0f, D * 2.5f, midZ), "", D * 0.5f, Ink);
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                sideTimers[i] = BoardLabel(side < 0 ? "TimerLeft" : "TimerRight", Board, new Vector3(SideX(side), Layout.boardWidth * flameHeightRatio * 0.5f + D * 2f, FrontZ + D * 0.3f), "", D * 0.6f, FireYellow);
            }
        }

        ParticleSystem BuildFlame(Transform parent)
        {
            var ps = Particles("Flame", parent, Vector3.up, D * 2f, D * 0.7f, 0.7f, -0.15f, 6f);
            var main = ps.main;
            main.loop = true;
            main.startColor = Color.white;
            var shape = ps.shape;
            shape.radius = D * 0.25f;
            var col = ps.colorOverLifetime;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(HotWhite, 0f), new GradientColorKey(FireYellow, 0.2f), new GradientColorKey(Fire, 0.5f), new GradientColorKey(FireRed, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.1f)));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = D * 0.4f;
            noise.frequency = 1.5f;
            ps.Play();
            return ps;
        }

        protected override void OnBegin()
        {
            fireSide = 0;
            fireTimer = 0f;
            nextFire = 2f;
            soloTimer = 0f;
            hits = 0;
            beatTimer = 0f;
            wave = -1f;
        }

        protected override void OnEnd()
        {
            fireSide = 0;
            soloTimer = 0f;
        }

        protected override void Tick(float dt, bool running)
        {
            dropPulse = Mathf.MoveTowards(dropPulse, 0f, dt * 3f);
            igniteTimer = Mathf.MoveTowards(igniteTimer, 0f, dt * 2f);
            if (!running) return;

            if (wave >= 0f)
            {
                wave += dt * 2.5f;
                if (wave > 1f) wave = -1f;
            }
            if (Solo)
            {
                soloTimer -= dt;
                Shake(soloShake, 0.2f);
                beatTimer -= dt;
                if (beatTimer <= 0f)
                {
                    beatTimer = 60f / Mathf.Max(30f, soloBpm);
                    SoloBeat();
                }
            }

            if (fireSide != 0)
            {
                fireTimer -= dt;
                if (fireTimer <= 0f) fireSide = 0;
            }
            nextFire -= dt;
            if (nextFire <= 0f)
            {
                nextFire = fireInterval;
                fireSide = Random.value < 0.5f ? -1 : 1;
                fireTimer = fireSeconds;
                igniteTimer = 1f;
                Play(igniteSound, ref madeIgnite, () => RecordSynth.Noise("RockIgnite", 0.6f, 0.06f, 0.05f, 0.25f, 1.2f, 4));
                Emit(embers, new Vector3(SideX(fireSide), D * 0.5f, FrontZ + D * 0.3f), FireYellow, 30);
            }
        }

        /// <summary>ソロのビート：スピーカーをふるわせ、衝撃波でプッシャーの前の床のメダルを手前へ押す</summary>
        void SoloBeat()
        {
            wave = 0f;
            Speaker(0.9f, 2, Fire);
            Play(null, ref madeBeat, () => RecordSynth.Thud("RockBeat", 60f, 0.25f, 0.8f), 0.6f);
            foreach (var m in BoardMedals)
            {
                if (m == null || m.Consumed) continue;
                var p = m.transform.position;
                if (p.y < -0.1f || p.y > Layout.pusherHeight * 0.9f) continue;   // 上の段は押さない
                if (p.z < FrontZ || p.z > PusherFrontZ + D) continue;
                Push(m, new Vector3(0f, D * 0.15f, -D * soloPush));
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type == DropZoneType.Center)
            {
                float mult = Solo ? soloMultiplier : centerMultiplier;
                int extra = AddMultiplied(mult);
                dropPulse = 1f;
                BoardPop($"+{CenterPoints + extra}", Solo ? FireYellow : Fire, new Vector3(medal.transform.position.x, D * 1.2f, FrontZ + D * 0.5f), D * 0.7f);
                return;
            }

            int side = medal.transform.position.x < 0f ? -1 : 1;
            if (fireSide == 0 || side != fireSide) return;

            AddPoints(fireBonus);
            hits++;
            Play(fireHitSound, ref madeHit, () => RecordSynth.Chord("RockHit", new[] { 82.41f, 123.47f, 164.81f }, 0.8f, 0.3f, RecordSynth.Wave.Saw));
            Emit(embers, new Vector3(SideX(side), D * 0.5f, FrontZ + D * 0.3f), FireYellow, 40);
            BoardPop($"燃える穴！ +{fireBonus}", FireYellow, new Vector3(SideX(side), D * 1.5f, FrontZ + D * 0.5f), D * 0.7f);
            Speaker(0.6f);
            if (hits >= hitsForSolo && !Solo)
            {
                hits = 0;
                soloTimer = soloSeconds;
                Play(soloSound, ref madeSolo, () => RecordSynth.Sweep("RockSolo", 659f, 988f, 1.2f, 0.35f, RecordSynth.Wave.Saw));
                beatTimer = 0f;
                BoardPop("GUITAR SOLO!", FireYellow, new Vector3(0f, D * 2.5f, FrontZ + D * 1.5f), D * 1.2f);
                Speaker(1f, 16, Fire);
            }
        }

        protected override bool KeepBoardVisible()
        {
            foreach (var f in flames) if (f != null && f.particleCount > 0) return true;
            return embers != null && embers.particleCount > 0;
        }

        protected override void UpdateBoard(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                var ps = flames[i];
                if (ps == null) continue;
                if (Active && !ps.isPlaying) ps.Play();
                bool burning = Active && fireSide == side;
                float power = !Active ? 0f : Solo ? 1f : burning ? 0.9f + 0.1f * igniteTimer : 0.3f;
                float height = Layout.boardWidth * flameHeightRatio * power * (1f + 0.6f * dropPulse);
                var main = ps.main;
                float speed = Mathf.Max(D * 0.5f, height / 0.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.7f, speed);
                main.startColor = burning || Solo ? Color.Lerp(Color.white, HotWhite, 0.5f) : new Color(1f, 0.85f, 0.7f);
                var emission = ps.emission;
                emission.rateOverTime = Active ? 30f + 170f * power : 0f;

                if (lights[i] != null)
                {
                    float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 12f, i * 3.1f);
                    lights[i].intensity = flameLightIntensity * BoardShown * power * flicker * (1f + 0.5f * dropPulse);
                    lights[i].enabled = lights[i].intensity > 0.001f;
                }

                if (sideTimers[i] != null)
                {
                    sideTimers[i].gameObject.SetActive(burning);
                    if (burning) sideTimers[i].text = $"{fireTimer:0.0}";
                }
            }

            // 衝撃波：プッシャーの前から手前のふちへ走る
            if (shockRing != null)
            {
                bool on = wave >= 0f && Active;
                shockRing.SetActive(on);
                if (on)
                {
                    shockRing.transform.position = new Vector3(0f, D * 0.15f, Mathf.Lerp(PusherFrontZ, FrontZ, wave));
                    SetGlow(shockMaterial, FireYellow, 3f * (1f - wave));
                }
            }

            float t = Time.time;
            if (multText != null)
            {
                multText.gameObject.SetActive(Active);
                float mult = Solo ? soloMultiplier : centerMultiplier;
                multText.text = Solo ? $"SOLO ×{mult:0.#}" : $"×{mult:0.#}";
                multText.Color = Solo ? Color.Lerp(Ink, FireYellow, 0.5f + 0.5f * Mathf.Sin(t * 12f)) : Ink;
                multText.Scale = 1f + 0.12f * dropPulse;
            }
            if (pipText != null)
            {
                pipText.gameObject.SetActive(Active);
                if (Solo) pipText.text = $"残り {Mathf.CeilToInt(soloTimer)}秒";
                else
                {
                    var sb = new System.Text.StringBuilder("ソロまで ");
                    for (int i = 0; i < hitsForSolo; i++) sb.Append(i < hits ? "●" : "○");
                    pipText.text = sb.ToString();
                }
            }
        }
    }
}
