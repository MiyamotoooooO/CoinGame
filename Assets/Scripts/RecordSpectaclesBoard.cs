using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    // ====================================================================
    // 盤面（N008〜N017）の大きな演出。ルールは各レコードのスクリプトが受け持つ（ここは見た目と音だけ）
    // ====================================================================

    /// <summary>N008 海：大きなクジラが跳ねて、波しぶきと竜巻</summary>
    public class SeaSpectacle : RecordSpectacle
    {
        public override string Key => "Sea";
        public override string Title => "OCEAN STORM";
        public override string Catch => "竜巻がメダルを巻き上げる";
        public override Color Theme => C("#2fb8ff");
        public override float BeatInterval => 8f;
        public override string BeatText => "WATERSPOUT!";

        Transform whale, tail, waves, spout, spoutWhirl;
        readonly List<Transform> spoutParts = new List<Transform>();
        readonly List<Material> spoutMats = new List<Material>();
        readonly List<Color> spoutColors = new List<Color>();
        readonly List<float> spoutSpin = new List<float>();
        Material whirlMat;
        ParticleSystem spray, bubbles;
        float spoutLevel;

        protected override void Build()
        {
            whale = Group(Hero, "Whale", new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0f, 12f));
            var blue = Mat(C("#2b5f9e"), 0.2f, 0.7f);
            var belly = Mat(C("#dfe9f2"), 0.1f, 0.6f);
            Egg(whale, Vector3.zero, new Vector3(1.1f, 0.5f, 0.45f), blue);
            Egg(whale, new Vector3(0.08f, -0.1f, 0f), new Vector3(0.9f, 0.32f, 0.4f), belly);
            for (int i = 0; i < 5; i++) Box(whale, new Vector3(0.05f + i * 0.06f, -0.17f, -0.17f), new Vector3(0.02f, 0.12f, 0.02f), Mat(C("#b9c8d6")), new Vector3(0f, 0f, 60f));
            Ball(whale, new Vector3(0.38f, 0.04f, -0.17f), 0.06f, Mat(C("#0c0c10"), 0f, 0.95f));
            Ball(whale, new Vector3(0.39f, 0.05f, -0.2f), 0.02f, Glow(Color.white, 2f));
            Egg(whale, new Vector3(0.05f, -0.18f, -0.2f), new Vector3(0.28f, 0.06f, 0.12f), blue, new Vector3(0f, 30f, -30f));
            tail = Group(whale, "Tail", new Vector3(-0.5f, 0.05f, 0f));
            Egg(tail, new Vector3(-0.12f, 0.02f, 0f), new Vector3(0.3f, 0.14f, 0.14f), blue, new Vector3(0f, 0f, 15f));
            Egg(tail, new Vector3(-0.27f, 0.1f, 0f), new Vector3(0.12f, 0.32f, 0.05f), blue, new Vector3(0f, 0f, -35f));
            Egg(tail, new Vector3(-0.27f, -0.05f, 0f), new Vector3(0.12f, 0.28f, 0.05f), blue, new Vector3(0f, 0f, 40f));

            waves = Group(Hero, "Waves", new Vector3(0f, -0.38f, -0.1f));
            var sea = Clear(new Color(0.15f, 0.55f, 0.95f, 0.75f));
            var foam = Mat(C("#f2fbff"));
            for (int i = 0; i < 7; i++)
            {
                Egg(waves, new Vector3(-0.9f + i * 0.3f, 0f, 0f), new Vector3(0.42f, 0.18f, 0.3f), sea);
                Egg(waves, new Vector3(-0.9f + i * 0.3f + 0.08f, 0.07f, -0.05f), new Vector3(0.16f, 0.05f, 0.1f), foam);
            }
            // 竜巻（ねじれた3層の水の柱と、根元の渦潮）
            spout = Group(Set, "Spout", new Vector3(0f, -HalfH - 0.5f, 2f));
            AddSpoutLayer(RecordMeshKit.Funnel(0.35f, 2.1f, 10.5f, 0.7f), RecordMeshKit.AlphaBlend(RecordMeshKit.WaterStreaks, Color.white), new Color(0.55f, 0.85f, 1f, 0.6f), 300f);
            AddSpoutLayer(RecordMeshKit.Funnel(0.5f, 2.5f, 9.5f, -0.5f), RecordMeshKit.AlphaBlend(RecordMeshKit.WaterStreaks, Color.white), new Color(0.85f, 0.97f, 1f, 0.4f), -200f);
            AddSpoutLayer(RecordMeshKit.Funnel(0.15f, 0.9f, 11f, 1.4f, 32, 20, 1.4f), RecordMeshKit.Additive(RecordMeshKit.WaterStreaks, Color.white), new Color(0.45f, 0.8f, 1f, 0.55f), 520f);
            whirlMat = RecordMeshKit.AlphaBlend(RecordMeshKit.Whirlpool, new Color(0.75f, 0.93f, 1f, 0f));
            spoutWhirl = Part("Whirlpool", RecordMeshKit.Annulus(0.3f, 3.6f, 72, 8), whirlMat, spout, new Vector3(0f, 0.2f, 0f), Vector3.one, new Vector3(-20f, 0f, 0f));
            spray = Fx(0.22f, 1.3f, 0.9f);
            bubbles = Fx(0.12f, 1.6f, -0.2f, Hero);
        }

        void AddSpoutLayer(Mesh mesh, Material mat, Color color, float spin)
        {
            var t = Part("SpoutLayer", mesh, mat, spout, Vector3.zero, Vector3.one, Vector3.zero);
            spoutParts.Add(t);
            spoutMats.Add(mat);
            spoutColors.Add(color);
            spoutSpin.Add(spin);
            RecordMeshKit.SetColor(mat, new Color(color.r, color.g, color.b, 0f));
        }

        void Splash(Vector3 at, int n)
        {
            Burst(spray, at, new Color(0.75f, 0.92f, 1f), n, 9f, Vector3.up, 0.55f);
            Burst(spray, at, Color.white, n / 2, 6f, Vector3.up, 0.8f);
            Sfx(SpectacleSfx.Splash);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(0.6f)) Splash(Director.FocusPos + Vector3.down * 2.5f, 120);
            if (Cross(1.15f)) { SlowMo(0.35f, 0.6f); Shake(0.1f, 0.5f); }
            if (t > 0.3f && t < 1.3f) HeroTilt = new Vector3(0f, 0f, Mathf.Lerp(40f, 0f, (t - 0.3f) / 1f));
            if (Cross(1.6f)) Splash(Director.FocusPos + Vector3.down * 2.5f, 60);
        }

        protected override void Idle(float t, float dt)
        {
            tail.localRotation = Quaternion.Euler(0f, 0f, 18f * Mathf.Sin(t * 2.2f));
            whale.localPosition = new Vector3(0f, 0.05f + 0.04f * Mathf.Sin(t * 1.3f), 0f);
            whale.localRotation = Quaternion.Euler(0f, 0f, 12f + 6f * Mathf.Sin(t * 1.3f + 0.5f));
            for (int i = 0; i < waves.childCount; i++)
            {
                var w = waves.GetChild(i);
                var p = w.localPosition;
                w.localPosition = new Vector3(p.x, (i % 2 == 0 ? 0f : 0.07f) + 0.03f * Mathf.Sin(t * 3f + i), p.z);
            }
            Drip(bubbles, new Vector3(Rand(-0.6f, 0.6f), -0.35f, -0.1f), new Color(0.8f, 0.95f, 1f, 0.8f), 4f, 0.4f, Vector3.up, 0.3f, dt);
            spoutLevel = Mathf.MoveTowards(spoutLevel, 0f, dt * 0.35f);
            spout.localScale = new Vector3(0.5f + 0.5f * spoutLevel, Mathf.Max(0.01f, spoutLevel), 0.5f + 0.5f * spoutLevel);
            spout.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.7f) * 6f, 0f, Mathf.Cos(t * 1.3f) * 6f);
            for (int i = 0; i < spoutParts.Count; i++)
            {
                spoutParts[i].localRotation = Quaternion.Euler(0f, t * spoutSpin[i], 0f);
                Color c = spoutColors[i];
                RecordMeshKit.SetColor(spoutMats[i], new Color(c.r, c.g, c.b, c.a * spoutLevel));
                RecordMeshKit.Scroll(spoutMats[i], dt * 0.12f * Mathf.Sign(spoutSpin[i]), -dt * (0.6f + i * 0.3f));
            }
            spoutWhirl.localRotation = Quaternion.Euler(-20f, t * 160f, 0f);
            RecordMeshKit.SetColor(whirlMat, new Color(0.75f, 0.93f, 1f, 0.9f * spoutLevel));
            if (spoutLevel > 0.1f) Drip(spray, spout.localPosition + Vector3.up * Rand(0f, 9f) * spoutLevel, new Color(0.8f, 0.95f, 1f), 60f * spoutLevel, 3f, new Vector3(Rand(-1f, 1f), 0.3f, 0f), 0.5f, dt);
        }

        protected override void OnBeat(int n)
        {
            spoutLevel = 1f;
            spout.localPosition = new Vector3(Rand(-HalfW * 0.6f, HalfW * 0.6f), -HalfH - 0.5f, 2f);
            Vector3 from = spout.localPosition, to = new Vector3(-from.x, from.y, from.z);
            Move(spout, from, to, 3.5f, Ease.InOutCubic);
            Sfx(SpectacleSfx.Whoosh, 1f, 0.6f);
            Splash(HeroPoint(new Vector3(0.3f, 0.2f, 0f)), 40);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            Burst(spray, at, new Color(0.75f, 0.92f, 1f), type == DropZoneType.Center ? 14 : 6, 5f, Vector3.up, 0.5f);
        }
    }

    /// <summary>N009 射的：コルク銃と、跳ね上がる的</summary>
    public class ShootingSpectacle : RecordSpectacle
    {
        public override string Key => "Shooting";
        public override string Title => "BULLSEYE!";
        public override string Catch => "落ちるたびに弾が飛ぶ";
        public override Color Theme => C("#ff4a3a");
        public override string BeatText => "TRICK SHOT!";

        Transform gun, crosshair;
        readonly List<Transform> targets = new List<Transform>();
        ParticleSystem muzzle;
        float recoil;

        protected override void Build()
        {
            gun = Group(Hero, "Gun", new Vector3(0.05f, -0.05f, 0f), new Vector3(0f, -20f, 8f));
            var wood = Mat(C("#8a4a22"), 0.1f, 0.6f);
            var steel = Mat(C("#5a5e66"), 0.9f, 0.8f);
            Prism(gun, new Vector3(0.2f, -0.12f, 0f), new[] { new Vector2(-0.1f, 0.08f), new Vector2(0.28f, 0.06f), new Vector2(0.32f, -0.2f), new Vector2(0.14f, -0.2f), new Vector2(0.02f, -0.02f), new Vector2(-0.12f, -0.02f) }, 0.1f, wood);
            Cyl(gun, new Vector3(-0.25f, -0.06f, 0f), 0.09f, 0.7f, steel, new Vector3(0f, 0f, 90f));
            Cyl(gun, new Vector3(-0.62f, -0.06f, 0f), 0.11f, 0.05f, Mat(C("#c8a060")), new Vector3(0f, 0f, 90f));
            Box(gun, new Vector3(0.12f, -0.18f, 0f), new Vector3(0.03f, 0.1f, 0.06f), steel, new Vector3(0f, 0f, 15f));
            Ring(gun, new Vector3(0.1f, -0.16f, 0f), 0.05f, 0.012f, steel, new Vector3(90f, 0f, 0f));
            Box(gun, new Vector3(-0.5f, 0.0f, 0f), new Vector3(0.02f, 0.04f, 0.02f), steel);

            var red = Mat(C("#e8262a")); var white = Mat(C("#f6f2ea"));
            for (int i = 0; i < 5; i++)
            {
                var t = Group(Set, "Target", new Vector3(-HalfW * 0.75f + i * HalfW * 0.375f, HalfH - 1.2f, 3f));
                for (int r = 0; r < 4; r++) Cyl(t, new Vector3(0f, 0f, -r * 0.02f), 1.1f - r * 0.27f, 0.04f, r % 2 == 0 ? red : white, new Vector3(90f, 0f, 0f));
                Box(t, new Vector3(0f, -0.9f, 0.05f), new Vector3(0.1f, 0.8f, 0.05f), wood);
                t.gameObject.SetActive(false);
                targets.Add(t);
            }
            crosshair = Group(Set, "Crosshair", Vector3.zero);
            var ch = Glow(C("#ff3030"), 3f);
            Ring(crosshair, Vector3.zero, 0.8f, 0.05f, ch, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 4; i++) Box(crosshair, Quaternion.Euler(0f, 0f, i * 90f) * new Vector3(0.95f, 0f, 0f), new Vector3(0.5f, 0.06f, 0.05f), ch, new Vector3(0f, 0f, i * 90f));
            crosshair.gameObject.SetActive(false);
            muzzle = Fx(0.25f, 0.4f, 0f, Hero);
        }

        void Fire(Vector3 at, bool big)
        {
            recoil = 1f;
            Burst(muzzle, Hero.InverseTransformPoint(gun.TransformPoint(new Vector3(-0.64f, -0.06f, 0f))), new Color(1f, 0.8f, 0.3f), big ? 30 : 10, 1f, Hero.InverseTransformDirection(gun.TransformDirection(Vector3.left)), 0.6f);
            Sparkle(at, C("#ffe08a"), big ? 30 : 10, big ? 6f : 3f);
            Shock(at, Theme, 0.2f, big ? 4f : 2f, 0.4f);
            Sfx(SpectacleSfx.Zap, big ? 1f : 0.5f, big ? 0.7f : 1.2f);
            if (big) { Kick(0.6f); Sfx(SpectacleSfx.Boom, 0.6f); }
        }

        void ShowTargets()
        {
            foreach (var t in targets)
            {
                t.gameObject.SetActive(true);
                t.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Transform tt = t;
                Tween(0.4f, k => tt.localRotation = Quaternion.Euler(90f * (1f - Ease.OutBack(k)), 0f, 0f), null, Rand(0f, 0.3f));
            }
        }

        void ShootTarget(int i, float delay)
        {
            var t = targets[i];
            After(delay, () =>
            {
                Fire(t.localPosition, true);
                Tween(0.35f, k => t.localRotation = Quaternion.Euler(-100f * Ease.OutCubic(k), 0f, 0f));
                Aim(t.localPosition, 0.3f);
            });
        }

        void Aim(Vector3 at, float seconds)
        {
            crosshair.gameObject.SetActive(true);
            crosshair.localPosition = at + Vector3.back;
            crosshair.localScale = Vector3.one * 2f;
            Tween(seconds, k => crosshair.localScale = Vector3.one * Mathf.Lerp(2f, 1f, Ease.OutCubic(k)), () => After(0.25f, () => crosshair.gameObject.SetActive(false)));
        }

        protected override void OnBegin() { foreach (var t in targets) t.gameObject.SetActive(false); crosshair.gameObject.SetActive(false); }

        protected override void OnIntro(float t)
        {
            if (Cross(0.5f)) ShowTargets();
            if (Cross(1.2f)) for (int i = 0; i < targets.Count; i++) ShootTarget(i, i * 0.18f);
            if (Cross(3.0f)) foreach (var tt in targets) tt.gameObject.SetActive(false);
        }

        protected override void Idle(float t, float dt)
        {
            recoil *= Mathf.Exp(-dt * 10f);
            gun.localPosition = new Vector3(0.05f + 0.12f * recoil, -0.05f + 0.01f * Mathf.Sin(t * 2f), 0f);
            gun.localRotation = Quaternion.Euler(0f, -20f + 8f * Mathf.Sin(t * 0.9f), 8f + 18f * recoil);
        }

        protected override void OnBeat(int n)
        {
            ShowTargets();
            for (int i = 0; i < targets.Count; i++) ShootTarget(i, 0.5f + i * 0.15f);
            After(2.2f, () => { foreach (var tt in targets) tt.gameObject.SetActive(false); });
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            Vector3 hit = RandScreen(-0.7f, 0.7f, -0.75f, -0.2f, 0f);
            Aim(hit, 0.2f);
            After(0.2f, () => Fire(hit, false));
        }
    }

    /// <summary>N010 汽車：蒸気機関車が画面を走り抜ける</summary>
    public class TrainSpectacle : RecordSpectacle
    {
        public override string Key => "Train";
        public override string Title => "STEAM EXPRESS";
        public override string Catch => "汽車がメダルを押していく";
        public override Color Theme => C("#ffb03a");
        public override float BeatInterval => 10f;
        public override string BeatText => "ALL ABOARD!";
        public override float BeatSpotlight => 0f;

        Transform loco, runner;
        readonly List<Transform> wheels = new List<Transform>();
        readonly List<Transform> runnerWheels = new List<Transform>();
        ParticleSystem steam, runSteam, sparks;

        Transform BuildLoco(Transform parent, string name, List<Transform> wheelList)
        {
            var g = Group(parent, name, Vector3.zero);
            var body = Mat(C("#1d2a3a"), 0.6f, 0.7f);
            var red = Mat(C("#c8282a"), 0.4f, 0.6f);
            var gold = Mat(C("#e8b84a"), 0.95f, 0.85f);
            Cyl(g, new Vector3(0.12f, 0.08f, 0f), 0.34f, 0.7f, body, new Vector3(0f, 0f, 90f));
            for (int i = 0; i < 3; i++) Ring(g, new Vector3(-0.1f + i * 0.2f, 0.08f, 0f), 0.172f, 0.012f, gold, new Vector3(0f, 0f, 90f));
            Cyl(g, new Vector3(0.47f, 0.08f, 0f), 0.3f, 0.02f, Mat(C("#2a2a2a")), new Vector3(0f, 0f, 90f));
            Halo(g, new Vector3(0.5f, 0.12f, 0f), new Color(1f, 0.9f, 0.5f, 0.9f), 0.4f);
            Ball(g, new Vector3(0.49f, 0.12f, 0f), 0.08f, Glow(C("#fff2a0"), 4f));
            Lathe(g, new Vector3(0.32f, 0.22f, 0f), new[] { new Vector2(0f, 0f), new Vector2(0.05f, 0f), new Vector2(0.05f, 0.12f), new Vector2(0.09f, 0.2f), new Vector2(0.09f, 0.22f), new Vector2(0f, 0.22f) }, body);
            Ball(g, new Vector3(0.1f, 0.26f, 0f), 0.12f, gold);
            Box(g, new Vector3(-0.32f, 0.16f, 0f), new Vector3(0.3f, 0.42f, 0.36f), red);
            Box(g, new Vector3(-0.32f, 0.39f, 0f), new Vector3(0.36f, 0.04f, 0.4f), body);
            Box(g, new Vector3(-0.32f, 0.22f, -0.181f), new Vector3(0.18f, 0.14f, 0.01f), Glow(C("#ffd27a"), 1.8f));
            Box(g, new Vector3(0f, -0.12f, 0f), new Vector3(0.95f, 0.06f, 0.3f), body);
            Prism(g, new Vector3(0.53f, -0.13f, 0f), new[] { new Vector2(-0.04f, 0.08f), new Vector2(-0.04f, -0.12f), new Vector2(0.12f, -0.12f) }, 0.3f, red);
            var wheel = Mat(C("#b02020"), 0.5f, 0.6f);
            foreach (float x in new[] { -0.32f, -0.05f, 0.2f })
            {
                var w = Group(g, "Wheel", new Vector3(x, -0.17f, -0.16f), new Vector3(90f, 0f, 0f));
                Cyl(w, Vector3.zero, x < -0.2f ? 0.26f : 0.22f, 0.04f, wheel);
                for (int s = 0; s < 3; s++) Box(w, new Vector3(0f, -0.025f, 0f), new Vector3(0.2f, 0.01f, 0.02f), gold, new Vector3(0f, s * 60f, 0f));
                wheelList.Add(w);
            }
            Box(g, new Vector3(-0.05f, -0.17f, -0.2f), new Vector3(0.55f, 0.025f, 0.02f), Mat(C("#d8d8d8"), 0.9f, 0.9f));
            return g;
        }

        protected override void Build()
        {
            loco = BuildLoco(Hero, "Loco", wheels);
            loco.localRotation = Quaternion.Euler(0f, -18f, 0f);
            steam = Fx(0.35f, 1.6f, -0.1f, Hero, false, 2.5f);

            runner = BuildLoco(Set, "Runner", runnerWheels);
            runner.localScale = Vector3.one * 4f;
            for (int i = 1; i <= 2; i++)
            {
                var car = Group(runner, "Car", new Vector3(-0.62f * i - 0.15f, 0f, 0f));
                Box(car, new Vector3(0f, 0.12f, 0f), new Vector3(0.55f, 0.36f, 0.34f), Mat(i == 1 ? C("#2f7a4a") : C("#3a4fa0"), 0.3f, 0.6f));
                Box(car, new Vector3(0f, 0.33f, 0f), new Vector3(0.6f, 0.04f, 0.38f), Mat(C("#1d1d1d")));
                for (int k = 0; k < 3; k++) Box(car, new Vector3(-0.17f + k * 0.17f, 0.18f, -0.171f), new Vector3(0.11f, 0.1f, 0.01f), Glow(C("#ffe2a0"), 1.6f));
                foreach (float x in new[] { -0.17f, 0.17f })
                {
                    var w = Group(car, "Wheel", new Vector3(x, -0.12f, -0.16f), new Vector3(90f, 0f, 0f));
                    Cyl(w, Vector3.zero, 0.16f, 0.04f, Mat(C("#222")));
                    runnerWheels.Add(w);
                }
            }
            runner.gameObject.SetActive(false);
            runSteam = Fx(0.9f, 2f, -0.08f, null, false, 2.5f);
            sparks = Fx(0.12f, 0.5f, 0.6f);
        
            // レールと枕木
            var rail = Mat(C("#9aa0aa"), 0.9f, 0.7f);
            for (int i = 0; i < 2; i++) Box(Hero, new Vector3(0f, -0.31f, i == 0 ? -0.12f : 0.12f), new Vector3(1.4f, 0.02f, 0.02f), rail);
            for (int i = 0; i < 9; i++) Box(Hero, new Vector3(-0.64f + i * 0.16f, -0.33f, 0f), new Vector3(0.05f, 0.02f, 0.34f), Mat(C("#5a3216")));
        }

        void Run(float seconds, float y)
        {
            runner.gameObject.SetActive(true);
            float from = -HalfW - 5f, to = HalfW + 7f;
            Sfx(SpectacleSfx.Horn, 0.9f);
            Sfx(SpectacleSfx.Whoosh, 0.8f, 0.7f);
            Tween(seconds, k =>
            {
                runner.localPosition = new Vector3(Mathf.Lerp(from, to, k), y + 0.05f * Mathf.Sin(k * 80f), 1f);
                Vector3 chimney = runner.localPosition + new Vector3(0.32f * 4f, 0.5f * 4f, 0f);
                Drip(runSteam, chimney, new Color(0.95f, 0.95f, 1f, 0.75f), 50f, 1.5f, new Vector3(-0.5f, 1f, 0f), 0.4f, Time.deltaTime, 0.2f);
                Drip(sparks, runner.localPosition + new Vector3(0f, -0.2f * 4f, -0.5f), new Color(1f, 0.8f, 0.3f), 30f, 3f, new Vector3(-1f, 0.6f, 0f), 0.5f, Time.deltaTime, 0.6f);
                if (k > 0.35f && k < 0.65f) Shake(0.06f, 0.2f);
            }, () => runner.gameObject.SetActive(false));
        }

        protected override void OnBegin() => runner.gameObject.SetActive(false);

        protected override void OnIntro(float t)
        {
            if (Cross(0.35f)) Run(2.4f, -2.2f);
        }

        protected override void Idle(float t, float dt)
        {
            foreach (var w in wheels) w.Rotate(0f, -400f * dt, 0f, Space.Self);
            foreach (var w in runnerWheels) w.Rotate(0f, -900f * dt, 0f, Space.Self);
            loco.localPosition = new Vector3(0f, 0.006f * Mathf.Sin(t * 30f), 0f);
            Drip(steam, new Vector3(0.32f * Mathf.Cos(-18f * Mathf.Deg2Rad), 0.46f, 0.1f), new Color(1f, 1f, 1f, 0.7f), 6f, 0.5f, new Vector3(-0.4f, 1f, 0f), 0.3f, dt);
        }

        protected override void OnBeat(int n) => Run(2.2f, Rand(-2.8f, -1.8f));

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Burst(steam, new Vector3(0.3f, 0.46f, 0.1f), Color.white, 6, 0.8f, Vector3.up, 0.4f);
        }

        protected override void OnFinale() => Run(2f, -2.2f);
    }

    /// <summary>N011 ブラックホール：回る光の円盤と、吸い込まれる光</summary>
    public class BlackHoleSpectacle : RecordSpectacle
    {
        public override string Key => "BlackHole";
        public override string Title => "EVENT HORIZON";
        public override string Catch => "すべてを吸い込む";
        public override Color Theme => C("#b06bff");
        public override string BeatText => "GRAVITY WAVE";
        public override string FinaleText => "SUPERNOVA!!";

        Transform disk, diskA, diskB, lens, jets;
        Material diskMat, photonMat, jetMat;
        ParticleSystem matter, stars;

        protected override void Build()
        {
            const float core = 0.2f;   // 黒い穴の半径
            // 円盤（ほぼ真横から見える。手前の半分は穴の前を通り、奥の半分は穴にかくれる）
            disk = Group(Hero, "Disk", Vector3.zero, new Vector3(-80f, 0f, 10f));
            diskMat = RecordMeshKit.Additive(RecordMeshKit.Accretion, Color.white);
            diskA = Part("DiskA", RecordMeshKit.Annulus(core * 1.08f, 1.05f, 96, 8), diskMat, disk, Vector3.zero, Vector3.one, Vector3.zero);
            diskB = Part("DiskB", RecordMeshKit.Annulus(core * 1.15f, 0.8f, 96, 6), RecordMeshKit.Additive(RecordMeshKit.Accretion, new Color(1f, 0.7f, 1f, 0.55f)), disk, new Vector3(0f, 0.004f, 0f), Vector3.one, new Vector3(0f, 77f, 0f));
            // 真っ黒な穴
            Ball(Hero, Vector3.zero, core * 2f, Mat(Color.black, 0f, 0f));
            // 奥の円盤の光が、重力で曲がって穴の上と下に回りこんで見える輪（アインシュタイン・リング）
            lens = Part("Lens", RecordMeshKit.Annulus(core * 1.02f, core * 2.1f, 96, 6), RecordMeshKit.Additive(RecordMeshKit.Accretion, new Color(1f, 0.9f, 0.8f, 0.85f)), Hero, new Vector3(0f, 0f, 0.06f), Vector3.one, new Vector3(-90f, 0f, 0f));
            // 穴のふちの、細くて強い光の輪
            photonMat = Add(new Color(1f, 0.92f, 0.75f, 1f));
            Ring(Hero, new Vector3(0f, 0f, -0.01f), core * 1.03f, 0.006f, photonMat, new Vector3(90f, 0f, 0f));
            Halo(Hero, new Vector3(0f, 0f, 0.3f), new Color(0.6f, 0.3f, 1f, 0.8f), 1.7f);
            Halo(Hero, new Vector3(0f, 0f, 0.25f), new Color(1f, 0.6f, 0.3f, 0.35f), 0.9f);
            // 上下に噴き出す光の柱
            jets = Group(Hero, "Jets", Vector3.zero, new Vector3(0f, 0f, 10f));
            jetMat = RecordMeshKit.Additive(RecordMeshKit.WaterStreaks, new Color(0.6f, 0.78f, 1f, 0.85f));
            Part("JetUp", RecordMeshKit.Funnel(0.02f, 0.11f, 1.2f, 2f, 20, 10, 0.8f), jetMat, jets, new Vector3(0f, core * 0.7f, 0f), Vector3.one, Vector3.zero);
            Part("JetDown", RecordMeshKit.Funnel(0.02f, 0.11f, 1.2f, 2f, 20, 10, 0.8f), jetMat, jets, new Vector3(0f, -core * 0.7f, 0f), Vector3.one, new Vector3(180f, 0f, 0f));
            matter = Fx(0.12f, 1.2f, 0f);
            stars = Fx(0.1f, 2.5f, 0f);
        }

        void Inhale(int n, float radius)
        {
            Vector3 c = HeroPoint(Vector3.zero);
            for (int i = 0; i < n; i++)
            {
                Vector3 from = c + (Vector3)(Random.insideUnitCircle.normalized * radius);
                var ep = new ParticleSystem.EmitParams { position = from, velocity = (c - from) * 1.2f + Vector3.Cross(c - from, Vector3.forward) * 0.4f, startColor = Color.Lerp(C("#ffd27a"), Theme, Rand(0f, 1f)), startLifetime = 0.8f, startSize = 0.15f };
                matter.Emit(ep, 1);
            }
        }

        protected override void OnIntro(float t)
        {
            if (Cross(0.3f)) { Flash(Color.black, 0.6f); Sfx(SpectacleSfx.Rise, 0.8f, 0.5f); }
            if (t > 0.5f && t < 2.5f) Inhale(6, 9f);
            if (Cross(1.15f)) { SlowMo(0.25f, 0.6f); Shock(HeroPoint(Vector3.zero), Theme, 12f, 0.5f, 0.8f); }
        }

        protected override void Idle(float t, float dt)
        {
            diskA.Rotate(0f, 170f * dt, 0f, Space.Self);
            diskB.Rotate(0f, 115f * dt, 0f, Space.Self);
            lens.Rotate(0f, 120f * dt, 0f, Space.Self);
            disk.localRotation = Quaternion.Euler(-80f + 3f * Mathf.Sin(t * 0.7f), 0f, 10f);
            jets.localScale = new Vector3(1f, 1f + 0.25f * Mathf.Sin(t * 9f), 1f);
            RecordMeshKit.Scroll(jetMat, 0f, -dt * 1.8f);
            SetUnlitColor(photonMat, new Color(1f, 0.92f, 0.75f, 0.8f + 0.2f * Mathf.Sin(t * 23f)));
            Inhale(Mathf.RoundToInt(dt * 40f + Rand(0f, 1f)), 4f);
        }

        protected override void OnBeat(int n)
        {
            Shock(HeroPoint(Vector3.zero), Theme, 14f, 0.4f, 1f);
            Shock(HeroPoint(Vector3.zero), C("#ffd27a"), 10f, 0.3f, 0.8f);
            Inhale(80, 10f);
            Sfx(SpectacleSfx.Rise, 0.8f, 0.4f);
            Shake(0.08f, 0.8f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            Vector3 c = HeroPoint(Vector3.zero);
            for (int i = 0; i < (type == DropZoneType.Center ? 10 : 4); i++)
            {
                var ep = new ParticleSystem.EmitParams { position = at, velocity = (c - at) * Rand(0.9f, 1.3f) + (Vector3)(Random.insideUnitCircle * 0.8f), startColor = Theme, startLifetime = 1f, startSize = 0.14f };
                matter.Emit(ep, 1);
            }
        }

        protected override void OnFinale()
        {
            After(1.4f, () =>
            {
                Flash(Color.white, 0.9f);
                Burst(stars, HeroPoint(Vector3.zero), C("#ffe8b0"), 200, 14f);
                Burst(stars, HeroPoint(Vector3.zero), Theme, 120, 9f);
                Shock(HeroPoint(Vector3.zero), Color.white, 0.5f, 20f, 1f);
                Shake(0.2f, 1f);
                Sfx(SpectacleSfx.Boom);
                Sfx(SpectacleSfx.Crash);
            });
        }
    }

    /// <summary>N012 磁石：巨大なU字磁石と、走る電気</summary>
    public class MagnetSpectacle : RecordSpectacle
    {
        readonly List<Transform> floating = new List<Transform>();
        public override string Key => "Magnet";
        public override string Title => "MAGNETIC FORCE";
        public override string Catch => "メダルを穴へ引き寄せる";
        public override Color Theme => C("#ff3a4a");
        public override string BeatText => "MAX POWER!";

        Transform magnet;
        readonly Transform[] fields = new Transform[3];
        readonly Material[] fieldMats = new Material[3];
        ParticleSystem pull;

        protected override void Build()
        {
            magnet = Group(Hero, "Magnet", new Vector3(0f, 0.1f, 0f), new Vector3(0f, 0f, 180f));
            var red = Mat(C("#d8202a"), 0.5f, 0.8f);
            var blue = Mat(C("#2a5ad8"), 0.5f, 0.8f);
            var silver = Mat(C("#e0e4ea"), 0.95f, 0.9f);
            var arc = Group(magnet, "Arc", new Vector3(0f, 0.12f, 0f));
            for (int i = 0; i <= 12; i++)
            {
                float a = Mathf.PI * i / 12f;
                Vector3 p = new Vector3(Mathf.Cos(a) * 0.3f, Mathf.Sin(a) * 0.3f, 0f);
                Ball(arc, p, 0.2f, i < 6 ? red : i > 6 ? blue : Mat(C("#8a3a8a"), 0.5f, 0.8f));
            }
            Box(magnet, new Vector3(-0.3f, -0.05f, 0f), new Vector3(0.2f, 0.36f, 0.2f), blue);
            Box(magnet, new Vector3(0.3f, -0.05f, 0f), new Vector3(0.2f, 0.36f, 0.2f), red);
            Box(magnet, new Vector3(-0.3f, -0.28f, 0f), new Vector3(0.21f, 0.1f, 0.21f), silver);
            Box(magnet, new Vector3(0.3f, -0.28f, 0f), new Vector3(0.21f, 0.1f, 0.21f), silver);
            for (int i = 0; i < 3; i++)
            {
                fieldMats[i] = Add(new Color(0.6f, 0.8f, 1f, 0.4f));
                fields[i] = Group(magnet, "Field", new Vector3(0f, -0.33f, 0f));
                Ring(fields[i], Vector3.zero, 0.3f, 0.015f, fieldMats[i], new Vector3(90f, 0f, 0f));
            }
            pull = Fx(0.12f, 0.8f, 0f);
        
            // 引き寄せられて浮かぶメダル
            var gold = Mat(C("#f2b82a"), 1f, 0.85f);
            for (int i = 0; i < 4; i++)
            {
                var coin = Group(Hero, "Coin", new Vector3(-0.3f + i * 0.2f, -0.55f - (i % 2) * 0.1f, -0.05f), new Vector3(70f, i * 30f, 0f));
                Cyl(coin, Vector3.zero, 0.13f, 0.02f, gold);
                Star(coin, new Vector3(0f, 0.012f, 0f), 5, 0.04f, 0.017f, 0.005f, Mat(C("#ffe88a"), 1f, 0.9f), new Vector3(90f, 0f, 0f));
                floating.Add(coin);
            }
        }

        void Arcs(int n)
        {
            for (int i = 0; i < n; i++)
                Bolt(HeroPoint(new Vector3(-0.3f, 0.45f, 0f)), HeroPoint(new Vector3(0.3f, 0.45f, 0f)), C("#bfe4ff"), 0.06f, 0.2f, null, 8, 0.25f);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.1f)) { Arcs(4); Sfx(SpectacleSfx.Zap, 1f, 0.6f); Shake(0.08f, 0.4f); }
            if (t > 1.1f && t < 2.6f && Every(0.15f)) Arcs(1);
        }

        protected override void Idle(float t, float dt)
        {
            for (int i = 0; i < 3; i++)
            {
                float k = Mathf.Repeat(t * 0.8f + i / 3f, 1f);
                fields[i].localPosition = new Vector3(0f, -0.33f - k * 0.5f, 0f);
                fields[i].localScale = new Vector3(1f + k * 1.6f, 1f, 1f + k * 0.4f);
                SetUnlitColor(fieldMats[i], new Color(0.6f, 0.8f, 1f, 0.5f * (1f - k)));
            }
            magnet.localRotation = Quaternion.Euler(0f, 15f * Mathf.Sin(t), 180f + 4f * Mathf.Sin(t * 3f));
            for (int i = 0; i < floating.Count; i++)
            {
                var p = floating[i].localPosition;
                floating[i].localPosition = new Vector3(p.x, -0.55f - (i % 2) * 0.1f + 0.04f * Mathf.Sin(t * 3f + i), p.z);
                floating[i].Rotate(0f, 180f * dt, 0f, Space.Self);
            }
            if (Every(1.3f)) Arcs(1);
        }

        protected override void OnBeat(int n)
        {
            Arcs(6);
            Sfx(SpectacleSfx.Zap, 1f, 0.5f);
            Vector3 c = HeroPoint(new Vector3(0f, -0.4f, 0f));
            for (int i = 0; i < 80; i++)
            {
                Vector3 from = c + (Vector3)(Random.insideUnitCircle.normalized * Rand(5f, 10f));
                var ep = new ParticleSystem.EmitParams { position = from, velocity = (c - from) * 1.3f, startColor = i % 2 == 0 ? Theme : C("#4a7aff"), startLifetime = 0.75f, startSize = 0.14f };
                pull.Emit(ep, 1);
            }
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type != DropZoneType.Center) return;
            Arcs(1);
            Sparkle(at, C("#bfe4ff"), 8, 3f, Vector3.up, 0.5f);
        }
    }

    /// <summary>N013 釣り：釣りざおと、跳ねる大きな魚</summary>
    public class FishingSpectacle : RecordSpectacle
    {
        public override string Key => "Fishing";
        public override string Title => "BIG CATCH!";
        public override string Catch => "落ちそうなメダルを釣り上げる";
        public override Color Theme => C("#3ad0a0");
        public override string BeatText => "HIT!!";

        Transform rod, reel, fish;
        Transform line;
        ParticleSystem splash;

        protected override void Build()
        {
            rod = Group(Hero, "Rod", new Vector3(-0.15f, -0.1f, 0f), new Vector3(0f, 0f, -35f));
            Tube(rod, new Vector3(0f, -0.4f, 0f), new Vector3(0f, 0.6f, 0f), 0.04f, Mat(C("#2a2a2a"), 0.4f, 0.8f));
            Tube(rod, new Vector3(0f, -0.45f, 0f), new Vector3(0f, -0.2f, 0f), 0.07f, Mat(C("#8a5a2a")));
            for (int i = 0; i < 4; i++) Ring(rod, new Vector3(0.02f, -0.05f + i * 0.17f, 0f), 0.025f, 0.006f, Mat(C("#d0d0d0"), 0.9f, 0.9f), new Vector3(0f, 0f, 90f));
            reel = Group(rod, "Reel", new Vector3(0.07f, -0.26f, 0f), new Vector3(90f, 0f, 0f));
            Cyl(reel, Vector3.zero, 0.14f, 0.06f, Mat(C("#d8d8e0"), 0.9f, 0.85f));
            Box(reel, new Vector3(0.05f, 0.04f, 0f), new Vector3(0.1f, 0.02f, 0.02f), Mat(C("#333")));
            line = Box(Hero, Vector3.zero, new Vector3(0.006f, 1f, 0.006f), Glow(Color.white, 1.2f));

            fish = Group(Hero, "Fish", new Vector3(0.35f, -0.1f, -0.05f), Vector3.zero, 0.75f);
            var scale = Mat(C("#e86a3a"), 0.4f, 0.8f);
            Egg(fish, Vector3.zero, new Vector3(0.55f, 0.28f, 0.16f), scale);
            Egg(fish, new Vector3(0.02f, -0.05f, 0f), new Vector3(0.45f, 0.14f, 0.15f), Mat(C("#ffe2c8")));
            Prism(fish, new Vector3(-0.3f, 0f, 0f), new[] { new Vector2(0.06f, 0f), new Vector2(-0.12f, 0.14f), new Vector2(-0.08f, 0f), new Vector2(-0.12f, -0.14f) }, 0.03f, scale);
            Prism(fish, new Vector3(0f, 0.13f, 0f), new[] { new Vector2(-0.12f, 0f), new Vector2(0.08f, 0f), new Vector2(-0.06f, 0.1f) }, 0.02f, scale);
            Ball(fish, new Vector3(0.18f, 0.03f, -0.07f), 0.05f, Mat(Color.black, 0f, 0.95f));
            Ball(fish, new Vector3(0.185f, 0.04f, -0.09f), 0.016f, Glow(Color.white, 2f));
            Halo(Hero, new Vector3(0f, -0.2f, 0.4f), new Color(0.2f, 0.9f, 0.7f, 0.5f), 1.8f);
            splash = Fx(0.15f, 1f, 0.5f, Hero);
        
            // 水面と、うき
            Egg(Hero, new Vector3(0.25f, -0.52f, 0f), new Vector3(0.9f, 0.06f, 0.4f), Clear(new Color(0.2f, 0.6f, 0.95f, 0.75f)));
            Ring(Hero, new Vector3(0.25f, -0.5f, 0f), 0.2f, 0.01f, Glow(C("#cfefff"), 1.5f));
            var bob = Group(Hero, "Bobber", new Vector3(-0.05f, -0.47f, -0.05f));
            Ball(bob, Vector3.zero, 0.07f, Mat(C("#e8262a"), 0.2f, 0.8f));
            Ball(bob, new Vector3(0f, 0.03f, 0f), 0.05f, Mat(Color.white));
            Cyl(bob, new Vector3(0f, 0.07f, 0f), 0.01f, 0.06f, Mat(C("#222")));
        }

        void Leap(float seconds)
        {
            Vector3 from = new Vector3(0.6f, -0.5f, -0.05f), to = new Vector3(0.1f, -0.5f, -0.05f);
            Burst(splash, from, new Color(0.7f, 0.95f, 1f), 30, 0.9f, Vector3.up, 0.5f);
            Sfx(SpectacleSfx.Splash, 0.8f);
            Tween(seconds, k =>
            {
                fish.localPosition = Vector3.Lerp(from, to, k) + Vector3.up * Ease.Spike(k) * 0.75f;
                fish.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(60f, -80f, k));
            }, () => { Burst(splash, to, new Color(0.7f, 0.95f, 1f), 30, 0.9f, Vector3.up, 0.5f); Sfx(SpectacleSfx.Splash, 0.7f, 1.2f); });
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.0f)) Leap(1.1f);
        }

        protected override void Idle(float t, float dt)
        {
            reel.Rotate(0f, 300f * dt, 0f, Space.Self);
            rod.localRotation = Quaternion.Euler(0f, 0f, -35f + 6f * Mathf.Sin(t * 2.3f));
            Vector3 tip = rod.localPosition + rod.localRotation * new Vector3(0f, 0.6f, 0f);
            Vector3 hook = fish.localPosition + new Vector3(0.22f, 0.05f, 0f);
            Vector3 d = hook - tip;
            line.localPosition = (tip + hook) * 0.5f;
            line.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            line.localScale = new Vector3(0.006f, d.magnitude, 0.006f);
            if (!InIntro && Beats == 0) fish.localRotation = Quaternion.Euler(0f, 0f, 10f * Mathf.Sin(t * 4f));
        }

        protected override void OnBeat(int n) => Leap(1f);

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type != DropZoneType.Center) Sparkle(at + Vector3.up * 0.4f, Theme, 12, 3f, Vector3.up, 0.5f);
        }
    }

    /// <summary>N014 城壁：せり上がる城と旗、大砲</summary>
    public class CastleSpectacle : RecordSpectacle
    {
        public override string Key => "Castle";
        public override string Title => "FORTRESS";
        public override string Catch => "端の穴を守り抜け";
        public override Color Theme => C("#c8b48a");
        public override string BeatText => "FIRE!";

        Transform castle;
        readonly List<Transform> flags = new List<Transform>();
        ParticleSystem dust, smoke;

        protected override void Build()
        {
            castle = Group(Hero, "Castle", new Vector3(0f, -0.1f, 0f));
            var stone = Mat(C("#a8a29a"), 0.05f, 0.25f);
            var dark = Mat(C("#6a6560"), 0.05f, 0.2f);
            var roof = Mat(C("#3a5ad0"), 0.2f, 0.6f);
            Box(castle, new Vector3(0f, -0.15f, 0f), new Vector3(0.9f, 0.4f, 0.4f), stone);
            for (int i = 0; i < 7; i++) Box(castle, new Vector3(-0.39f + i * 0.13f, 0.08f, -0.15f), new Vector3(0.08f, 0.08f, 0.1f), stone);
            Prism(castle, new Vector3(0f, -0.24f, -0.21f), new[] { new Vector2(-0.1f, -0.11f), new Vector2(0.1f, -0.11f), new Vector2(0.1f, 0.05f), new Vector2(0f, 0.12f), new Vector2(-0.1f, 0.05f) }, 0.02f, Mat(C("#3a2416")));
            foreach (float x in new[] { -0.45f, 0.45f, 0f })
            {
                bool mid = x == 0f;
                float h = mid ? 0.75f : 0.6f, d = mid ? 0.26f : 0.22f;
                float z = mid ? 0.12f : 0f;
                Cyl(castle, new Vector3(x, -0.35f + h * 0.5f, z), d, h, stone);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3f;
                    Box(castle, new Vector3(x + Mathf.Cos(a) * d * 0.45f, -0.35f + h + 0.03f, z + Mathf.Sin(a) * d * 0.45f), new Vector3(0.05f, 0.06f, 0.05f), stone);
                }
                Cone(castle, new Vector3(x, -0.35f + h + 0.03f, z), d * 0.6f, mid ? 0.32f : 0.26f, roof);
                Box(castle, new Vector3(x, -0.35f + h * 0.6f, z - d * 0.5f), new Vector3(0.04f, 0.08f, 0.01f), Glow(C("#ffd27a"), 1.5f));
                var pole = Group(castle, "Flag", new Vector3(x, -0.35f + h + (mid ? 0.35f : 0.29f), z));
                Box(pole, new Vector3(0f, 0.06f, 0f), new Vector3(0.01f, 0.14f, 0.01f), dark);
                var flag = Group(pole, "Cloth", new Vector3(0f, 0.1f, 0f));
                Box(flag, new Vector3(0.06f, 0f, 0f), new Vector3(0.12f, 0.07f, 0.005f), Mat(mid ? C("#e8262a") : C("#ffd23a")));
                flags.Add(flag);
            }
            dust = Fx(0.6f, 1.6f, -0.02f, null, false, 2.2f);
            smoke = Fx(0.4f, 1.2f, -0.05f, null, false, 2.5f);
        
            // 窓・門の飾り・草
            var window = Glow(C("#ffd27a"), 1.6f);
            for (int i = 0; i < 4; i++) Box(castle, new Vector3(-0.3f + i * 0.2f, -0.05f, -0.205f), new Vector3(0.05f, 0.07f, 0.01f), window);
            Ring(castle, new Vector3(0f, -0.2f, -0.215f), 0.11f, 0.015f, Mat(C("#5a5650"), 0.1f, 0.3f), new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 12; i++) Cone(castle, new Vector3(-0.5f + i * 0.09f, -0.36f, -0.22f), 0.03f, 0.07f, Mat(C("#3aa84a")), Vector3.zero, 6);
        }

        void Cannon(Vector3 from, float dir)
        {
            var ball = Ball(Set, from, 0.35f, Mat(C("#1a1a1a"), 0.6f, 0.7f));
            Burst(smoke, from, new Color(0.9f, 0.9f, 0.9f, 0.8f), 18, 2f, new Vector3(dir, 0.6f, 0f), 0.5f);
            Sparkle(from, C("#ffb03a"), 14, 4f, new Vector3(dir, 0.5f, 0f), 0.5f);
            Throw(ball, from, new Vector3(dir * 7f, 7f, 0f), 9f, 1.4f);
            Sfx(SpectacleSfx.Boom, 0.8f, Rand(0.9f, 1.2f));
            Kick(0.4f);
        }

        protected override void OnIntro(float t)
        {
            float k = Mathf.Clamp01((t - 0.6f) / 0.9f);
            castle.localPosition = new Vector3(0f, Mathf.Lerp(-1.1f, -0.1f, Ease.OutCubic(k)), 0f);
            if (t > 0.6f && t < 1.5f)
            {
                Shake(0.05f, 0.2f);
                Drip(dust, HeroPoint(new Vector3(Rand(-0.5f, 0.5f), -0.45f, -0.2f)), new Color(0.75f, 0.68f, 0.55f, 0.7f), 40f, 1.5f, Vector3.up, 0.8f, Time.deltaTime);
            }
            if (Cross(0.6f)) Sfx(SpectacleSfx.Crash, 0.6f, 0.5f);
            if (Cross(1.6f)) Sfx(SpectacleSfx.Fanfare, 0.8f);
        }

        protected override void Idle(float t, float dt)
        {
            for (int i = 0; i < flags.Count; i++) flags[i].localRotation = Quaternion.Euler(0f, 25f * Mathf.Sin(t * 5f + i), 0f);
        }

        protected override void OnBeat(int n)
        {
            for (int i = 0; i < 3; i++)
            {
                int ii = i;
                After(0.3f + i * 0.3f, () => Cannon(HeroPoint(new Vector3(ii % 2 == 0 ? -0.45f : 0.45f, 0.2f, -0.2f)), ii % 2 == 0 ? -1f : 1f));
            }
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type != DropZoneType.Center) Smoke(at + Vector3.up * 0.3f, new Color(0.8f, 0.75f, 0.6f, 0.7f), 8, 1.5f, Vector3.up);
        }
    }

    /// <summary>N015 メリーゴーランド：回る木馬と電球</summary>
    public class CarouselSpectacle : RecordSpectacle
    {
        public override string Key => "Carousel";
        public override string Title => "MERRY-GO-ROUND";
        public override string Catch => "回して、前へ送る";
        public override Color Theme => C("#ff9ad0");
        public override string BeatText => "SPIN!";

        Transform turn;
        readonly List<Transform> horses = new List<Transform>();
        readonly List<Material> bulbs = new List<Material>();
        float spin = 1f;

        protected override void Build()
        {
            var gold = Mat(C("#f0c048"), 0.9f, 0.85f);
            Lathe(Hero, new Vector3(0f, 0.25f, 0f), new[] { new Vector2(0f, 0.32f), new Vector2(0.06f, 0.3f), new Vector2(0.5f, 0.05f), new Vector2(0.52f, 0f), new Vector2(0.52f, -0.04f), new Vector2(0f, -0.04f) }, Mat(C("#ff6aa8"), 0.2f, 0.6f), Vector3.zero, 16);
            Ball(Hero, new Vector3(0f, 0.6f, 0f), 0.08f, gold);
            for (int i = 0; i < 16; i++)
            {
                var m = Glow(C("#fff2b0"), 2f);
                bulbs.Add(m);
                float a = i * Mathf.PI * 2f / 16f;
                Ball(Hero, new Vector3(Mathf.Cos(a) * 0.52f, 0.23f, Mathf.Sin(a) * 0.52f), 0.045f, m);
            }
            Cyl(Hero, new Vector3(0f, -0.38f, 0f), 1.0f, 0.06f, Mat(C("#f4e8d8")));
            Cyl(Hero, new Vector3(0f, -0.43f, 0f), 1.04f, 0.04f, gold);
            Cyl(Hero, new Vector3(0f, -0.05f, 0f), 0.12f, 0.62f, gold);
            turn = Group(Hero, "Turn", Vector3.zero);
            Color[] hc = { C("#ffffff"), C("#ffd0e8"), C("#d0e8ff"), C("#fff0c0"), C("#e0d0ff"), C("#d0ffe8") };
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f;
                var pos = new Vector3(Mathf.Cos(a) * 0.36f, 0f, Mathf.Sin(a) * 0.36f);
                Cyl(turn, pos + Vector3.up * -0.05f, 0.02f, 0.62f, gold);
                var horse = Group(turn, "Horse", pos, new Vector3(0f, -a * Mathf.Rad2Deg, 0f));
                var body = Mat(hc[i], 0.2f, 0.7f);
                Egg(horse, Vector3.zero, new Vector3(0.08f, 0.09f, 0.2f), body);
                Egg(horse, new Vector3(0f, 0.08f, 0.1f), new Vector3(0.05f, 0.12f, 0.06f), body, new Vector3(-30f, 0f, 0f));
                Egg(horse, new Vector3(0f, 0.13f, 0.14f), new Vector3(0.05f, 0.05f, 0.09f), body);
                foreach (float z in new[] { -0.07f, 0.07f })
                    Box(horse, new Vector3(0f, -0.08f, z), new Vector3(0.025f, 0.1f, 0.025f), body, new Vector3(z > 0 ? -30f : 30f, 0f, 0f));
                Box(horse, new Vector3(0f, 0.05f, 0f), new Vector3(0.085f, 0.015f, 0.08f), Mat(C("#e8262a")));
                horses.Add(horse);
            }
            Halo(Hero, new Vector3(0f, 0.1f, 0.6f), new Color(1f, 0.6f, 0.85f, 0.5f), 2f);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.1f)) { Sfx(SpectacleSfx.Chime); Sfx(SpectacleSfx.Sparkle); }
        }

        protected override void Idle(float t, float dt)
        {
            spin = Mathf.MoveTowards(spin, 1f, dt * 0.6f);
            turn.Rotate(0f, 60f * spin * dt, 0f, Space.Self);
            for (int i = 0; i < horses.Count; i++)
            {
                var p = horses[i].localPosition;
                horses[i].localPosition = new Vector3(p.x, 0.08f * Mathf.Sin(t * 2.5f * spin + i * 1.05f), p.z);
            }
            for (int i = 0; i < bulbs.Count; i++)
            {
                bool on = ((i + Mathf.FloorToInt(t * 6f * spin)) % 3) == 0;
                SetGlow(bulbs[i], on ? C("#fff2b0") : C("#ff8ac8"), on ? 3.5f : 0.6f);
            }
            HeroTilt = new Vector3(14f, 0f, 0f);
        }

        protected override void OnBeat(int n)
        {
            spin = 4f;
            Sparkle(HeroPoint(new Vector3(0f, 0.3f, 0f)), Theme, 40, 6f);
            Sfx(SpectacleSfx.Sparkle);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) { spin = Mathf.Max(spin, 2f); Sparkle(at, C("#fff2b0"), 8, 3f, Vector3.up, 0.6f); }
        }
    }

    /// <summary>N016 ボウリング：転がる球と、はじけ飛ぶピン</summary>
    public class BowlingSpectacle : RecordSpectacle
    {
        public override string Key => "Bowling";
        public override string Title => "STRIKE!!";
        public override string Catch => "奥から球が押し出す";
        public override Color Theme => C("#4a8aff");
        public override float BeatInterval => 10f;
        public override string BeatText => "STRIKE!!";

        static readonly Vector3 BallHome = new Vector3(-0.55f, -0.12f, 0f);
        Transform ball;
        readonly List<Transform> pins = new List<Transform>();
        readonly List<Vector3> pinHome = new List<Vector3>();
        Material pinMat, stripe;

        protected override void Build()
        {
            pinMat = Mat(C("#f6f6f2"), 0.1f, 0.8f);
            stripe = Mat(C("#e8262a"), 0.2f, 0.7f);
            // ピンは右に三角に並べる（手前から見て、横に転がる球がぶつかる）
            int[] rows = { 1, 2, 3, 4 };
            for (int r = 0; r < rows.Length; r++)
                for (int i = 0; i < rows[r]; i++)
                {
                    var home = new Vector3(0.12f + r * 0.11f, -0.2f, (i - (rows[r] - 1) * 0.5f) * 0.13f);
                    var pin = Group(Hero, "Pin", home);
                    Lathe(pin, Vector3.zero, new[] { new Vector2(0f, 0f), new Vector2(0.035f, 0f), new Vector2(0.055f, 0.08f), new Vector2(0.03f, 0.17f), new Vector2(0.022f, 0.2f), new Vector2(0.032f, 0.25f), new Vector2(0f, 0.29f) }, pinMat, Vector3.zero, 16);
                    Cyl(pin, new Vector3(0f, 0.18f, 0f), 0.05f, 0.015f, stripe);
                    pins.Add(pin);
                    pinHome.Add(home);
                }
            ball = Group(Hero, "Ball", BallHome);
            Ball(ball, Vector3.zero, 0.24f, Mat(C("#2a4ad8"), 0.6f, 0.95f));
            for (int i = 0; i < 3; i++) Ball(ball, new Vector3(-0.03f + i * 0.03f, 0.06f + (i == 1 ? 0.03f : 0f), -0.1f), 0.035f, Mat(Color.black));
            Halo(ball, new Vector3(0f, 0f, 0.1f), new Color(0.3f, 0.6f, 1f, 0.5f), 0.5f);
            Box(Hero, new Vector3(0f, -0.21f, 0f), new Vector3(1.3f, 0.02f, 0.5f), Mat(C("#d8a868"), 0.1f, 0.85f));
            for (int i = 0; i < 2; i++) Box(Hero, new Vector3(0f, -0.2f, i == 0 ? -0.26f : 0.26f), new Vector3(1.3f, 0.03f, 0.03f), Mat(C("#5a3216")));
        }

        void ResetPins()
        {
            for (int i = 0; i < pins.Count; i++) { pins[i].localPosition = pinHome[i]; pins[i].localRotation = Quaternion.identity; }
        }

        void Strike(float delay)
        {
            Tween(0.6f, k =>
            {
                ball.localPosition = Vector3.Lerp(BallHome, new Vector3(0.3f, -0.08f, 0f), k);
                ball.localRotation = Quaternion.Euler(0f, 0f, -k * 720f);
            }, () =>
            {
                Kick(0.7f);
                Shake(0.08f, 0.4f);
                Sfx(SpectacleSfx.Crash, 0.9f, 1.4f);
                Sparkle(HeroPoint(new Vector3(0.3f, -0.05f, 0f)), Color.white, 30, 6f);
                for (int i = 0; i < pins.Count; i++)
                {
                    var p = pins[i];
                    Vector3 from = p.localPosition;
                    Vector3 vel = new Vector3(Rand(0.4f, 1.1f), Rand(1f, 1.8f), from.z * 5f + Rand(-0.3f, 0.3f));
                    Vector3 spin = new Vector3(Rand(-900f, 900f), Rand(-400f, 400f), Rand(-900f, 900f));
                    Quaternion r0 = p.localRotation;
                    Tween(1.1f, k2 =>
                    {
                        float tt = k2 * 1.1f;
                        p.localPosition = from + vel * tt + Vector3.down * 3f * tt * tt;
                        p.localRotation = r0 * Quaternion.Euler(spin * tt);
                    });
                }
                After(1.5f, () => { ResetPins(); ball.localPosition = BallHome; });
            }, delay);
            After(delay, () => Sfx(SpectacleSfx.Whoosh, 0.7f, 0.6f));
        }

        protected override void OnBegin() { ResetPins(); ball.localPosition = BallHome; }

        protected override void OnIntro(float t)
        {
            if (Cross(1.2f)) { Strike(0f); After(0.6f, () => SlowMo(0.3f, 0.5f)); }
        }

        protected override void Idle(float t, float dt)
        {
            HeroTilt = new Vector3(18f, 6f * Mathf.Sin(t * 0.6f), 0f);
        }

        protected override void OnBeat(int n) => Strike(0.3f);

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Sparkle(at, Theme, 8, 3f, Vector3.up, 0.6f);
        }
    }

    /// <summary>N017 招き猫：手招きする招き猫と、小判の雨</summary>
    public class ManekiNekoSpectacle : RecordSpectacle
    {
        public override string Key => "ManekiNeko";
        public override string Title => "LUCKY CAT";
        public override string Catch => "10枚ごとに福を招く";
        public override Color Theme => C("#ffcf3a");
        public override float BeatInterval => 0f;
        public override int BeatEveryDrops => 10;
        public override string BeatText => "大当たり！";

        Transform paw, cat;
        Transform koban;
        ParticleSystem coins;
        float wave;

        protected override void Build()
        {
            cat = Group(Hero, "Cat", Vector3.zero);
            var white = Mat(C("#fbf8f2"), 0.1f, 0.7f);
            var red = Mat(C("#e8262a"), 0.2f, 0.7f);
            var gold = Mat(C("#f2c23a"), 0.95f, 0.85f);
            Egg(cat, new Vector3(0f, -0.2f, 0f), new Vector3(0.62f, 0.56f, 0.48f), white);
            Ball(cat, new Vector3(0f, 0.2f, 0f), 0.46f, white);
            Cone(cat, new Vector3(-0.14f, 0.36f, 0f), 0.08f, 0.14f, white, new Vector3(0f, 0f, 18f));
            Cone(cat, new Vector3(0.14f, 0.36f, 0f), 0.08f, 0.14f, white, new Vector3(0f, 0f, -18f));
            Cone(cat, new Vector3(-0.14f, 0.37f, -0.02f), 0.05f, 0.1f, Mat(C("#ff9ab0")), new Vector3(0f, 0f, 18f));
            Cone(cat, new Vector3(0.14f, 0.37f, -0.02f), 0.05f, 0.1f, Mat(C("#ff9ab0")), new Vector3(0f, 0f, -18f));
            var black = Mat(Color.black);
            Box(cat, new Vector3(-0.09f, 0.23f, -0.215f), new Vector3(0.07f, 0.015f, 0.01f), black, new Vector3(0f, 0f, -12f));
            Box(cat, new Vector3(0.09f, 0.23f, -0.215f), new Vector3(0.07f, 0.015f, 0.01f), black, new Vector3(0f, 0f, 12f));
            Ball(cat, new Vector3(0f, 0.17f, -0.225f), 0.03f, Mat(C("#ff7a9a")));
            for (int s = -1; s <= 1; s += 2)
                for (int k = 0; k < 2; k++) Box(cat, new Vector3(s * 0.15f, 0.15f - k * 0.03f, -0.2f), new Vector3(0.12f, 0.006f, 0.006f), black, new Vector3(0f, 0f, s * (k == 0 ? 8f : -8f)));
            Ring(cat, new Vector3(0f, 0.0f, 0f), 0.2f, 0.03f, red);
            Ball(cat, new Vector3(0f, -0.04f, -0.2f), 0.08f, gold);
            koban = Group(cat, "Koban", new Vector3(0.12f, -0.25f, -0.25f), new Vector3(0f, 0f, -10f));
            Egg(koban, Vector3.zero, new Vector3(0.2f, 0.3f, 0.04f), gold);
            Box(koban, new Vector3(0f, 0f, -0.022f), new Vector3(0.08f, 0.1f, 0.005f), Mat(C("#c89020"), 0.9f, 0.8f));
            Ball(cat, new Vector3(-0.17f, -0.2f, -0.2f), 0.14f, white);
            paw = Group(cat, "Paw", new Vector3(0.22f, 0.12f, -0.05f));
            Cap(paw, new Vector3(0f, 0.12f, 0f), 0.12f, 0.3f, white);
            Ball(paw, new Vector3(0f, 0.27f, -0.02f), 0.14f, white);
            Ball(paw, new Vector3(0f, 0.27f, -0.08f), 0.05f, Mat(C("#ff9ab0")));
            Halo(Hero, new Vector3(0f, 0f, 0.5f), new Color(1f, 0.85f, 0.2f, 0.6f), 2f);
            coins = Fx(0.32f, 2.4f, 0.8f, null, false, 1f);
        }

        void Shower(int n)
        {
            for (int i = 0; i < n; i++) Burst(coins, new Vector3(Rand(-HalfW, HalfW), HalfH + Rand(0.5f, 3f), Rand(-1f, 1f)), Color.Lerp(C("#ffd84a"), C("#fff2b0"), Rand(0f, 1f)), 1, 1f, Vector3.down, 0.3f);
            Sfx(SpectacleSfx.Coin);
            After(0.15f, () => Sfx(SpectacleSfx.Coin, 0.8f, 1.2f));
            After(0.3f, () => Sfx(SpectacleSfx.Coin, 0.7f, 1.4f));
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.1f)) { wave = 1f; Shower(80); Sfx(SpectacleSfx.Bell); }
        }

        protected override void Idle(float t, float dt)
        {
            wave = Mathf.MoveTowards(wave, 0.3f, dt * 0.3f);
            paw.localRotation = Quaternion.Euler(0f, 0f, -10f - 50f * Mathf.Abs(Mathf.Sin(t * (2f + 6f * wave))) * Mathf.Max(0.3f, wave));
            cat.localRotation = Quaternion.Euler(0f, 10f * Mathf.Sin(t * 0.8f), 3f * Mathf.Sin(t * 1.6f));
            koban.localRotation = Quaternion.Euler(0f, 20f * Mathf.Sin(t * 1.4f), -10f);
        }

        protected override void OnBeat(int n)
        {
            wave = 1f;
            Shower(120);
            Flash(C("#ffe27a"), 0.35f);
            Sfx(SpectacleSfx.Bell);
            Kick(0.5f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) { wave = Mathf.Max(wave, 0.6f); Sparkle(at, Theme, 6, 3f, Vector3.up, 0.5f); }
        }
    }
}
