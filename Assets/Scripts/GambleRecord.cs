using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// N018 ギャンブル「ゴールデン・スロット」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・ゲームにいつも置いてあるスロット台（SlotRefill）を、曲が流れている間だけパワーアップする
    ///   見た目：スロット台が金色になり、キラキラ光る。リールの上の星（止まると光る）は、金色ではなく虹色に光る
    ///   中身：　そろう確率を上げる（リーチ・あたりの確率）。そろった時に出るメダルの数を2倍にする
    /// ・効果：スロットがそろうと、出目ごとのいつもの演出（777 のタワー、チェリーの爆弾、ベルのシャワーなど）が2倍の枚数で起きる。
    ///   さらに、盤面の上からおまけのメダルが降ってくる
    /// ・曲が終わったら、スロット台は元の色・元の確率・元の枚数にもどる
    /// ・SlotRefill.cs（スロット台のファイル）は変えない。public の確率と枚数を一時的に変えて、
    ///   見た目（本体の色・星の色）は、スロット台の部品の素材を一時的に入れかえて変える（曲が終わったら元にもどす）
    /// ・モニター：使わない（いつものラウンドの画面のまま）
    ///
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class GambleRecord : RecordScriptBase
    {
        public static GambleRecord Instance { get; private set; }

        [Header("確率（曲が流れている間）")]
        [Tooltip("まん中のリールが、左と同じ絵で止まる確率（リーチ）。元の値より低い時は、元の値のまま")]
        [Range(0f, 1f)] public float boostReachChance = 0.65f;
        [Tooltip("リーチの時に、右のリールもそろう確率（あたり）。元の値より低い時は、元の値のまま")]
        [Range(0f, 1f)] public float boostWinChance = 0.8f;

        [Header("効果")]
        [Tooltip("そろった時に出るメダルの数の倍率")]
        [Range(1f, 10f)] public float medalMultiplier = 2f;
        [Tooltip("そろった時に、おまけで上から降ってくるメダルの数")]
        [Range(0, 50)] public int bonusRain = 8;

        [Header("見た目")]
        [Tooltip("金色の光の強さ")]
        [Range(0f, 3f)] public float goldGlow = 0.5f;
        [Tooltip("虹色が回る速さ")]
        [Range(0f, 3f)] public float rainbowSpeed = 0.6f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の音）")]
        public AudioClip goldSound;
        public AudioClip winSound;

        protected override string DefaultKey => "Gamble";
        protected override string ScreenTitle => "GAMBLE";
        protected override string ScreenSubtitle => "ゴールデン・スロット";
        protected override float RecordSoundVolume => volume;
        protected override bool UseMonitor => false;   // モニターはいつもの画面のまま

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<GambleRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed()
        {
            Restore();
            if (rainbowTex != null) Destroy(rainbowTex);
            if (Instance == this) Instance = null;
        }

        static readonly Color Gold = new Color32(0xff, 0xc8, 0x3c, 0xff);

        // ---- スロット台 ----
        SlotRefill slot;
        Transform machine;
        Material starOn;
        Renderer[] starR, lampR, buttonR;
        FieldInfo frameGlowField;

        // 元にもどすために覚えておく
        bool boosted;
        float origReach, origWin;
        readonly List<int> origMedals = new List<int>();
        readonly List<(Renderer r, Material[] mats)> origMats = new List<(Renderer, Material[])>();
        Texture origStarTex;
        Color origStarColor;

        Material goldBright, goldPale, goldDark;
        Texture2D rainbowTex, starMask;
        float rainbowTimer, lastFrameGlow;
        ParticleSystem sparkle;
        AudioClip madeGold, madeWin;

        static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        protected override void BuildBoard()
        {
            sparkle = Particles("Sparkle", Board, Vector3.up, D * 1.2f, D * 0.07f, 0.9f, -0.1f, 80f);
        }

        static Material Metal(Color c, float smooth, float glow)
        {
            var m = ProtoAssets.MakeMaterial(c, 0.95f, smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * glow);
            return m;
        }

        /// <summary>スロット台をさがして、中の部品（本体・星）を見つける。SlotRefill の中の private の部品は、名前で読む</summary>
        bool FindSlot()
        {
            if (slot == null) slot = FindFirstObjectByType<SlotRefill>();
            if (slot == null) return false;
            var t = typeof(SlotRefill);
            machine = t.GetField("machine", Priv)?.GetValue(slot) as Transform;
            starOn = t.GetField("starOn", Priv)?.GetValue(slot) as Material;
            starR = t.GetField("starR", Priv)?.GetValue(slot) as Renderer[];
            lampR = t.GetField("lampR", Priv)?.GetValue(slot) as Renderer[];
            buttonR = t.GetField("buttonR", Priv)?.GetValue(slot) as Renderer[];
            frameGlowField = t.GetField("frameGlow", Priv);
            if (machine == null) Debug.LogWarning("GambleRecord：スロット台の本体が見つからないので、金色にはしません（確率と枚数は変えます）。");
            return true;
        }

        // ================================================================
        // 流れ
        // ================================================================

        protected override void OnBegin()
        {
            if (!FindSlot()) { Debug.LogWarning("GambleRecord：スロット台（SlotRefill）が見つかりません。"); return; }
            Boost();
            if (machine != null)
            {
                Emit(sparkle, machine.position, Gold, 40);
                Play(goldSound, ref madeGold, () => RecordSynth.Notes("GambleGold", new[] { 1047f, 1319f, 1568f, 2093f, 2637f }, 0.05f, 0.6f, 0.3f, RecordSynth.Wave.Bell));
            }
            lastFrameGlow = CurrentFrameGlow();
        }

        protected override void OnEnd() => Restore();

        /// <summary>確率を上げて、枚数を2倍にして、見た目を金色・虹色にする</summary>
        void Boost()
        {
            if (boosted) return;
            boosted = true;
            origReach = slot.reachChance;
            origWin = slot.winChance;
            slot.reachChance = Mathf.Max(origReach, boostReachChance);
            slot.winChance = Mathf.Max(origWin, boostWinChance);
            origMedals.Clear();
            foreach (var o in slot.outcomes)
            {
                origMedals.Add(o.medals);
                o.medals = Mathf.Max(o.medals, Mathf.RoundToInt(o.medals * medalMultiplier));
            }

            // 本体を金色に（赤と青の板は明るい金、黒っぽいふちは暗い金。リール・星・ランプ・ボタン・まっ黒の窓・白い玉・お札はそのまま）
            origMats.Clear();
            if (machine != null)
            {
                if (goldBright == null)
                {
                    goldBright = Metal(new Color(1f, 0.78f, 0.25f), 0.88f, goldGlow);
                    goldPale = Metal(new Color(1f, 0.88f, 0.55f), 0.9f, goldGlow);
                    goldDark = Metal(new Color(0.55f, 0.38f, 0.08f), 0.8f, goldGlow * 0.4f);
                }
                var skip = new HashSet<Renderer>();
                if (starR != null) foreach (var r in starR) skip.Add(r);
                if (lampR != null) foreach (var r in lampR) skip.Add(r);
                if (buttonR != null) foreach (var r in buttonR) skip.Add(r);
                foreach (var r in machine.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (skip.Contains(r) || r.name.StartsWith("Reel")) continue;
                    var mats = r.sharedMaterials;
                    var next = new Material[mats.Length];
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        next[i] = mats[i];
                        var m = mats[i];
                        if (m == null || !m.HasProperty("_Color")) continue;
                        Color c = m.color;
                        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                        bool red = c.r > 0.5f && c.g < 0.3f && c.b < 0.3f;
                        bool blue = c.b > 0.4f && c.r < 0.3f;
                        bool dark = max > 0.05f && max < 0.15f;
                        if (red) next[i] = goldBright;
                        else if (blue) next[i] = goldPale;
                        else if (dark) next[i] = goldDark;
                        else continue;
                        changed = true;
                    }
                    if (!changed) continue;
                    origMats.Add((r, mats));
                    r.sharedMaterials = next;
                }
            }

            // 星：光った時の色を、金色から虹色に
            if (starOn != null)
            {
                origStarTex = starOn.mainTexture;
                origStarColor = starOn.color;
                MakeRainbowStar(0f);
                starOn.mainTexture = rainbowTex;
                starOn.color = Color.white;
            }
        }

        /// <summary>元の確率・枚数・見た目にもどす</summary>
        void Restore()
        {
            if (!boosted) return;
            boosted = false;
            if (slot != null)
            {
                slot.reachChance = origReach;
                slot.winChance = origWin;
                for (int i = 0; i < slot.outcomes.Count && i < origMedals.Count; i++) slot.outcomes[i].medals = origMedals[i];
            }
            foreach (var (r, mats) in origMats) if (r != null) r.sharedMaterials = mats;
            origMats.Clear();
            if (starOn != null)
            {
                starOn.mainTexture = origStarTex;
                starOn.color = origStarColor;
            }
        }

        /// <summary>虹色の星の絵（元の星の形はそのまま。色だけ、まん中から外へ虹色の輪にする。hue をずらすと色が回る）</summary>
        void MakeRainbowStar(float hue)
        {
            var src = starOn != null ? starOn.mainTexture as Texture2D : null;
            if (starMask == null && src != null)
            {
                try
                {
                    // 元の星の形（すけ具合）を、読める形で写しておく
                    starMask = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                    starMask.SetPixels(src.GetPixels());
                    starMask.Apply();
                }
                catch { starMask = null; }
            }
            int n = starMask != null ? starMask.width : 64;
            if (rainbowTex == null)
                rainbowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "GambleRainbowStar", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            var mask = starMask != null ? starMask.GetPixels() : null;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f);
                    Color c = Color.HSVToRGB(Mathf.Repeat(hue + d * 1.6f + ang * 0.5f, 1f), 0.75f, 1f);
                    float a = mask != null ? mask[y * n + x].a : (d < 0.45f ? 1f : 0f);
                    // まん中は白く光る
                    c = Color.Lerp(Color.white, c, Mathf.Clamp01(d * 4f));
                    c.a = a;
                    px[y * n + x] = c;
                }
            rainbowTex.SetPixels(px);
            rainbowTex.Apply();
        }

        float CurrentFrameGlow()
        {
            if (slot == null || frameGlowField == null) return 0f;
            return (float)frameGlowField.GetValue(slot);
        }

        protected override void Tick(float dt, bool running)
        {
            if (!boosted || slot == null) return;
            // そろったかどうか：スロット台の「あたりの光」が点いた瞬間
            float fg = CurrentFrameGlow();
            if (fg > 1f && lastFrameGlow <= 0.01f && running) OnSlotWin();
            lastFrameGlow = fg;
        }

        void OnSlotWin()
        {
            if (bonusRain > 0) RainMedals(bonusRain);
            if (machine != null)
            {
                for (int i = 0; i < 6; i++)
                    Emit(sparkle, machine.position + Random.insideUnitSphere * D * 0.6f, Color.HSVToRGB(i / 6f, 0.7f, 1f), 10);
            }
            Play(winSound, ref madeWin, () => RecordSynth.Notes("GambleWin", new[] { 1568f, 2093f, 2637f, 3136f }, 0.06f, 0.5f, 0.3f, RecordSynth.Wave.Bell));
            PopCenter("ゴールデン大当たり！", Gold);
            Speaker(1f, 16, Gold);
        }

        // ================================================================
        // 見た目（毎フレーム）
        // ================================================================

        protected override bool KeepBoardVisible() => sparkle != null && sparkle.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            if (!boosted) return;
            float t = Time.time;
            // 金色がゆっくり光る
            float g = goldGlow * (0.75f + 0.35f * Mathf.Sin(t * 3f));
            if (goldBright != null) goldBright.SetColor("_EmissionColor", goldBright.color * g);
            if (goldPale != null) goldPale.SetColor("_EmissionColor", goldPale.color * g);
            // 星の虹色を回す（0.05 秒ごとに描き直す）
            rainbowTimer -= dt;
            if (rainbowTimer <= 0f && starOn != null)
            {
                rainbowTimer = 0.05f;
                MakeRainbowStar(t * rainbowSpeed);
            }
            // スロット台のまわりのキラキラ
            if (machine != null && Random.value < dt * 12f)
                Emit(sparkle, machine.position + Random.insideUnitSphere * D * 0.7f, Random.value < 0.6f ? Gold : Color.HSVToRGB(Random.value, 0.6f, 1f), 1);
        }
    }
}
