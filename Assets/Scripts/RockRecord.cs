using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N005 ロック「ライブハウス・ファイア」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：左右の端の穴の上に、炎の柱が立つ。燃え上がっている側の炎は高く、白っぽく燃える。
    ///   中央の穴に落ちると、両方の炎が「ボッ」と上がる
    /// ・モニター：暗いライブハウスのステージ。真ん中に大きく今の倍率「×2」。
    ///   下の左右に「燃える穴」の札（燃えている間は残り秒数）、まん中にソロまでの残り（●●○）
    /// ・ルール：
    ///   中央の穴のポイントが ×centerMultiplier（基本のポイントに掛かる）
    ///   fireInterval 秒ごとに、左右どちらかの端の穴が fireSeconds 秒「燃える穴」になる。
    ///   燃えている端の穴に落ちると +fireBonus pt（端の穴はふだん 0pt。燃えていない方は 0pt のまま）
    ///   燃える穴に hitsForSolo 回入れると「ギターソロ」：soloSeconds 秒のあいだ中央の穴が ×soloMultiplier。画面が揺れる
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<RockRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Fire = new Color32(0xff, 0x6a, 0x1a, 0xff);
        static readonly Color FireRed = new Color32(0xe8, 0x2a, 0x1e, 0xff);
        static readonly Color FireYellow = new Color32(0xff, 0xd0, 0x3a, 0xff);
        static readonly Color HotWhite = new Color32(0xff, 0xf4, 0xd8, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);
        static readonly Color Dark = new Color32(0x24, 0x22, 0x26, 0xff);

        int fireSide;            // 燃えている側（-1 左、1 右、0 なし）
        float fireTimer, nextFire, soloTimer, dropPulse, igniteTimer;
        int hits;
        bool Solo => soloTimer > 0f;

        readonly ParticleSystem[] flames = new ParticleSystem[2];
        readonly Light[] lights = new Light[2];
        ParticleSystem embers;

        RectTransform multGroup, soloTag;
        Text multText, multSub, soloText, statusText;
        readonly Text[] sideTexts = new Text[2];
        readonly Image[] sideBoxes = new Image[2];
        readonly List<Image> pips = new List<Image>();
        readonly List<Image> bgFlames = new List<Image>();
        AudioClip madeIgnite, madeHit, madeSolo;

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

            if (Solo)
            {
                soloTimer -= dt;
                Shake(soloShake, 0.2f);
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

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type == DropZoneType.Center)
            {
                float mult = Solo ? soloMultiplier : centerMultiplier;
                int extra = AddMultiplied(mult);
                dropPulse = 1f;
                Pop($"×{mult:0.#}  +{CenterPoints + extra}", Solo ? FireYellow : Fire, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 300f));
                return;
            }

            int side = medal.transform.position.x < 0f ? -1 : 1;
            if (fireSide == 0 || side != fireSide) return;

            AddPoints(fireBonus);
            hits++;
            Play(fireHitSound, ref madeHit, () => RecordSynth.Chord("RockHit", new[] { 82.41f, 123.47f, 164.81f }, 0.8f, 0.3f, RecordSynth.Wave.Saw));
            Emit(embers, new Vector3(SideX(side), D * 0.5f, FrontZ + D * 0.3f), FireYellow, 40);
            Pop($"燃える穴！ +{fireBonus}", FireYellow, new Vector2(side < 0 ? BodyW * 0.2f : BodyW * 0.8f, 470f));
            Speaker(0.6f);
            if (hits >= hitsForSolo && !Solo)
            {
                hits = 0;
                soloTimer = soloSeconds;
                Play(soloSound, ref madeSolo, () => RecordSynth.Sweep("RockSolo", 659f, 988f, 1.2f, 0.35f, RecordSynth.Wave.Saw));
                PopCenter("GUITAR SOLO!", FireYellow);
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
            }
        }

        protected override void BuildScreen()
        {
            pips.Clear();
            bgFlames.Clear();
            float W = BodyW;
            var stage = Panel(Screen, "Stage", W * 0.5f, 210f, W - 48f, 400f, new Color32(0x10, 0x0c, 0x0e, 0xff));
            float sw = W - 48f;
            int n = Mathf.Max(12, Mathf.FloorToInt(sw / 34f));
            for (int i = 0; i < n; i++)
            {
                Color c = i % 3 == 0 ? FireRed : i % 3 == 1 ? Fire : FireYellow;
                var img = Img(stage, $"BgFlame{i}", (i + 0.5f) * sw / n, 400f, sw / n * 1.6f, 80f, c);
                img.rectTransform.pivot = new Vector2(0.5f, 0f);
                Place(img.rectTransform, (i + 0.5f) * sw / n, 400f);
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 1f : -1f) * 6f);
                bgFlames.Add(img);
            }
            multGroup = Grp(stage, "Mult", sw * 0.5f, 170f, 460f, 220f);
            multText = Lbl(multGroup, "Text", 230f, 100f, 460f, 200f, "", 170, Ink, TextAnchor.MiddleCenter, true);
            var outline = multText.gameObject.AddComponent<Outline>();
            outline.effectColor = FireRed;
            outline.effectDistance = new Vector2(5f, -5f);
            multSub = Lbl(multGroup, "Sub", 230f, 205f, 460f, 30f, "中央の穴のポイント", 20, Ink);
            soloTag = Grp(stage, "SoloTag", sw * 0.5f, 36f, 440f, 52f);
            Tag(soloTag, "Bg", 220f, 26f, 440f, 50f, "", 10, FireYellow, Dark);
            soloText = Lbl(soloTag, "Text", 220f, 26f, 440f, 50f, "", 28, Dark, TextAnchor.MiddleCenter, true);
            soloTag.gameObject.SetActive(false);

            // 下：左右の燃える穴の札と、ソロまでの印
            for (int i = 0; i < 2; i++)
            {
                float cx = i == 0 ? 24f + 150f : W - 24f - 150f;
                sideBoxes[i] = Img(Screen, $"Side{i}", cx, 480f, 300f, 64f, new Color(1f, 1f, 1f, 0.08f), MonitorDisplay.ScreenShape.Pill);
                sideTexts[i] = Lbl(Screen, $"SideText{i}", cx, 480f, 300f, 64f, "", 24, Ink);
            }
            for (int i = 0; i < hitsForSolo; i++)
                pips.Add(Img(Screen, $"Pip{i}", W * 0.5f + (i - (hitsForSolo - 1) * 0.5f) * 40f, 480f, 26f, 26f, new Color(1f, 1f, 1f, 0.15f), MonitorDisplay.ScreenShape.Circle));
            Lbl(Screen, "PipLabel", W * 0.5f, 515f, 260f, 26f, "ソロまで", 16, Mute);
            statusText = Lbl(Screen, "Status", 24f, 570f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float t = Time.time;
            for (int i = 0; i < bgFlames.Count; i++)
            {
                bool left = i < bgFlames.Count / 2;
                bool hot = Solo || (fireSide == -1 && left) || (fireSide == 1 && !left);
                float h = (hot ? 260f : 70f) * (0.6f + 0.4f * Mathf.PerlinNoise(t * 3f, i * 0.7f)) + 60f * dropPulse;
                var rt = bgFlames[i].rectTransform;
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
            }
            float mult = Solo ? soloMultiplier : centerMultiplier;
            multText.text = $"×{mult:0.#}";
            multText.color = Solo ? Color.Lerp(Ink, FireYellow, 0.5f + 0.5f * Mathf.Sin(t * 12f)) : Ink;
            Scale(multGroup, 1f + 0.08f * dropPulse);
            if (soloTag.gameObject.activeSelf != Solo) soloTag.gameObject.SetActive(Solo);
            if (Solo) soloText.text = $"GUITAR SOLO ×{soloMultiplier:0.#}  {Mathf.CeilToInt(soloTimer)}";

            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                bool burning = fireSide == side;
                sideBoxes[i].color = burning ? Color.Lerp(Fire, FireYellow, 0.5f + 0.5f * Mathf.Sin(t * 10f)) : new Color(1f, 1f, 1f, 0.08f);
                sideTexts[i].text = burning ? $"{(side < 0 ? "左" : "右")}の穴が燃えている  {fireTimer:0.0}" : $"{(side < 0 ? "左" : "右")}の穴";
                sideTexts[i].color = burning ? Dark : Mute;
            }
            for (int i = 0; i < pips.Count; i++) pips[i].color = i < hits ? Fire : new Color(1f, 1f, 1f, 0.15f);

            statusText.text = Solo
                ? $"<color=#FFD03A>ギターソロ！</color>  中央の穴 ×{soloMultiplier:0.#}  残り {Mathf.CeilToInt(soloTimer)}秒"
                : fireSide != 0
                    ? $"燃えている{(fireSide < 0 ? "左" : "右")}の端の穴に入れると +{fireBonus}pt！（{hitsForSolo}回でギターソロ）"
                    : $"中央の穴 ×{centerMultiplier:0.#}  次の燃える穴まで {Mathf.CeilToInt(nextFire)}秒";
        }
    }
}
