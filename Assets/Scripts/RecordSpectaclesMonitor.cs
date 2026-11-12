using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    // ====================================================================
    // モニター（N018〜N025）の大きな演出。ルールは各レコードのスクリプトが受け持つ（ここは見た目と音だけ）
    // ====================================================================

    /// <summary>N018 ギャンブル：巨大なスロットマシン。レバーを引いてリールが回る</summary>
    public class GambleSpectacle : RecordSpectacle
    {
        public override string Key => "Gamble";
        public override string Title => "JACKPOT CASINO";
        public override string Catch => "3つそろえば大当たり";
        public override Color Theme => C("#ffcc2a");
        public override float BeatInterval => 11f;

        readonly Transform[] reels = new Transform[3];
        readonly float[] angle = new float[3], speed = new float[3];
        readonly bool[] stopping = new bool[3];
        Transform lever;
        readonly List<Material> bulbs = new List<Material>();
        ParticleSystem coins;
        const int Symbols = 8;

        protected override void Build()
        {
            var body = Mat(C("#c8141c"), 0.5f, 0.8f);
            var gold = Mat(C("#f2c23a"), 0.95f, 0.85f);
            Box(Hero, new Vector3(0f, -0.1f, 0f), new Vector3(0.82f, 0.8f, 0.42f), body);
            Box(Hero, new Vector3(0f, -0.48f, -0.04f), new Vector3(0.9f, 0.08f, 0.5f), gold);
            Lathe(Hero, new Vector3(0f, 0.3f, 0f), new[] { new Vector2(0f, 0f), new Vector2(0.4f, 0f), new Vector2(0.36f, 0.1f), new Vector2(0.2f, 0.17f), new Vector2(0f, 0.19f) }, Glow(C("#ff6a2a"), 1.4f), new Vector3(0f, 0f, 0f), 24);
            Star(Hero, new Vector3(0f, 0.42f, -0.12f), 5, 0.1f, 0.045f, 0.03f, Glow(C("#fff2a0"), 3f));
            Box(Hero, new Vector3(0f, 0.02f, -0.2f), new Vector3(0.68f, 0.3f, 0.04f), Mat(C("#1a1a1a")));
            var reelMat = Mat(C("#f8f4ea"), 0.1f, 0.6f);
            for (int i = 0; i < 3; i++)
            {
                reels[i] = Group(Hero, "Reel", new Vector3(-0.21f + i * 0.21f, 0.02f, -0.1f));
                Cyl(reels[i], Vector3.zero, 0.36f, 0.18f, reelMat, new Vector3(0f, 0f, 90f));
                for (int s = 0; s < Symbols; s++)
                {
                    float a = s * 360f / Symbols;
                    var sym = Group(reels[i], "Sym", Quaternion.Euler(a, 0f, 0f) * new Vector3(0f, 0f, -0.185f), new Vector3(a, 0f, 0f));
                    MakeSymbol(sym, s);
                }
            }
            Box(Hero, new Vector3(0f, 0.02f, -0.225f), new Vector3(0.7f, 0.012f, 0.01f), Glow(C("#ff2a2a"), 2f));
            for (int i = 0; i < 14; i++)
            {
                var m = Glow(C("#fff2b0"), 2f);
                bulbs.Add(m);
                float x = i < 7 ? -0.39f + i * 0.13f : -0.39f + (i - 7) * 0.13f;
                Ball(Hero, new Vector3(x, i < 7 ? 0.24f : -0.2f, -0.22f), 0.05f, m);
            }
            lever = Group(Hero, "Lever", new Vector3(0.47f, -0.05f, 0f));
            Cyl(lever, Vector3.zero, 0.08f, 0.06f, gold, new Vector3(0f, 0f, 90f));
            Tube(lever, Vector3.zero, new Vector3(0.04f, 0.42f, 0f), 0.03f, Mat(C("#d8d8d8"), 0.9f, 0.9f));
            Ball(lever, new Vector3(0.04f, 0.45f, 0f), 0.11f, Mat(C("#e8141c"), 0.3f, 0.95f));
            Halo(Hero, new Vector3(0f, 0f, 0.5f), new Color(1f, 0.8f, 0.2f, 0.5f), 2.1f);
            coins = Fx(0.3f, 2.2f, 1f, null, false, 1f);
        }

        void MakeSymbol(Transform s, int kind)
        {
            switch (kind % 4)
            {
                case 0:
                    var red = Glow(C("#e8141c"), 1.2f);
                    Box(s, new Vector3(0f, 0.035f, 0f), new Vector3(0.09f, 0.022f, 0.01f), red);
                    Box(s, new Vector3(0.008f, -0.01f, 0f), new Vector3(0.022f, 0.09f, 0.01f), red, new Vector3(0f, 0f, -25f));
                    break;
                case 1:
                    Ball(s, new Vector3(-0.025f, -0.015f, 0f), 0.05f, Mat(C("#d8141c")));
                    Ball(s, new Vector3(0.025f, -0.02f, 0f), 0.05f, Mat(C("#d8141c")));
                    Box(s, new Vector3(0f, 0.025f, 0f), new Vector3(0.008f, 0.06f, 0.008f), Mat(C("#2a8a2a")));
                    break;
                case 2:
                    Cone(s, new Vector3(0f, -0.04f, 0f), 0.04f, 0.08f, Mat(C("#f2c23a"), 0.9f, 0.8f), new Vector3(-90f, 0f, 0f));
                    break;
                default:
                    Box(s, Vector3.zero, new Vector3(0.1f, 0.04f, 0.01f), Mat(C("#1a3ad8")));
                    break;
            }
        }

        void Spin(bool jackpot)
        {
            Tween(0.35f, k => lever.localRotation = Quaternion.Euler(0f, 0f, -70f * Ease.Spike(k)));
            Sfx(SpectacleSfx.Rise, 0.6f, 1.4f);
            for (int i = 0; i < 3; i++) { speed[i] = 1400f + i * 200f; stopping[i] = false; }
            int symbol = jackpot ? 0 : -1;
            for (int i = 0; i < 3; i++)
            {
                int ii = i;
                int target = symbol >= 0 ? 0 : RandInt(0, Symbols);
                if (!jackpot && i == 2 && target == 0) target = 3;
                After(0.6f + i * 0.3f, () => StopReel(ii, target, jackpot && ii == 2));
            }
        }

        void StopReel(int i, int symbol, bool win)
        {
            stopping[i] = true;
            float from = angle[i];
            float targetA = -symbol * 360f / Symbols;
            float to = from + Mathf.Repeat(targetA - from, 360f) + 360f;
            Tween(0.45f, k => angle[i] = Mathf.LerpUnclamped(from, to, Ease.OutBack(k)), () => { speed[i] = 0f; });
            Sfx(SpectacleSfx.Drum, 0.6f, 1.6f);
            if (win) After(0.45f, Jackpot);
        }

        void Jackpot()
        {
            for (int i = 0; i < 160; i++) Burst(coins, HeroPoint(new Vector3(0f, -0.3f, -0.3f)), Color.Lerp(C("#ffd84a"), C("#fff2b0"), Rand(0f, 1f)), 1, 12f, new Vector3(Rand(-1f, 1f), 1.4f, -0.3f), 0.5f);
            Flash(C("#ffe27a"), 0.5f);
            Banner("JACKPOT!!", Theme);
            Kick(0.8f);
            SlowMo(0.4f, 0.5f);
            Sfx(SpectacleSfx.Fanfare);
            Sfx(SpectacleSfx.Coin, 1f, 1.2f);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.0f)) Spin(true);
        }

        protected override void Idle(float t, float dt)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!stopping[i]) angle[i] += speed[i] * dt;
                reels[i].localRotation = Quaternion.Euler(angle[i], 0f, 0f);
            }
            for (int i = 0; i < bulbs.Count; i++)
            {
                bool on = (i + Mathf.FloorToInt(t * 8f)) % 4 == 0;
                SetGlow(bulbs[i], on ? C("#fff2b0") : C("#ff6a2a"), on ? 3.5f : 0.8f);
            }
        }

        protected override void OnBeat(int n)
        {
            bool win = n % 3 == 0;
            Banner(win ? "BIG CHANCE!" : "SPIN!", Theme);
            Spin(win);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Burst(coins, at, C("#ffd84a"), 4, 5f, Vector3.up, 0.4f);
        }
    }

    /// <summary>N019 天気予報：太陽と雲。10秒ごとに晴れ・雨・雷・虹が変わる</summary>
    public class WeatherSpectacle : RecordSpectacle
    {
        public override string Key => "Weather";
        public override string Title => "WEATHER CHANNEL";
        public override string Catch => "空模様が盤面を変える";
        public override Color Theme => C("#5ac8ff");
        public override float BeatInterval => 10f;
        public override float BeatSpotlight => 1f;

        Transform sun, cloud, rainbow;
        Material sunMat, cloudMat;
        readonly List<Transform> rays = new List<Transform>();
        ParticleSystem rain, snow;
        int weather;

        protected override void Build()
        {
            sun = Group(Hero, "Sun", new Vector3(0.12f, 0.12f, 0.15f));
            sunMat = Glow(C("#ffb02a"), 2.4f);
            Ball(sun, Vector3.zero, 0.42f, sunMat);
            for (int i = 0; i < 10; i++)
            {
                float a = i * 36f;
                var r = Group(sun, "Ray", Vector3.zero, new Vector3(0f, 0f, a));
                Cone(r, new Vector3(0f, 0.26f, 0f), 0.05f, 0.14f, Glow(C("#ffd84a"), 2.5f), Vector3.zero, 8);
                rays.Add(r);
            }
            Halo(sun, new Vector3(0f, 0f, 0.2f), new Color(1f, 0.7f, 0.2f, 0.7f), 1.2f);
            cloud = Group(Hero, "Cloud", new Vector3(-0.12f, -0.12f, -0.1f));
            cloudMat = Mat(C("#f4f6fa"), 0f, 0.3f);
            Ball(cloud, new Vector3(0f, 0f, 0f), 0.36f, cloudMat);
            Ball(cloud, new Vector3(-0.22f, -0.06f, 0.02f), 0.26f, cloudMat);
            Ball(cloud, new Vector3(0.22f, -0.05f, 0.02f), 0.28f, cloudMat);
            Ball(cloud, new Vector3(0.1f, 0.12f, 0.04f), 0.26f, cloudMat);
            Egg(cloud, new Vector3(0f, -0.12f, 0f), new Vector3(0.7f, 0.14f, 0.3f), cloudMat);
            rainbow = Group(Set, "Rainbow", new Vector3(0f, -HalfH + 0.5f, 4f));
            Color[] rc = { C("#ff3a3a"), C("#ff9a2a"), C("#ffe02a"), C("#3ad04a"), C("#3a8aff"), C("#8a4aff") };
            for (int i = 0; i < rc.Length; i++)
                Ring(rainbow, Vector3.zero, 7.5f - i * 0.35f, 0.17f, Add(new Color(rc[i].r, rc[i].g, rc[i].b, 0.55f)), new Vector3(90f, 0f, 0f));
            rainbow.gameObject.SetActive(false);
            rain = Fx(0.12f, 0.9f, 2f, null, false, 1f);
            snow = Fx(0.18f, 4f, 0.05f, null, false, 1f);
        }

        void SetWeather(int w)
        {
            weather = w;
            rainbow.gameObject.SetActive(w == 3);
            if (w == 3) PopIn(rainbow, 0.8f);
            string[] names = { "晴れ！", "雨", "かみなり！", "虹！" };
            Banner(names[w], Theme);
            Sfx(w == 2 ? SpectacleSfx.Crash : w == 3 ? SpectacleSfx.Sparkle : SpectacleSfx.Chime, 0.8f);
        }

        protected override void OnBegin()
        {
            weather = 0;
            rainbow.gameObject.SetActive(false);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.1f)) { Flash(C("#fff2b0"), 0.3f); Sfx(SpectacleSfx.Chime); }
        }

        protected override void Idle(float t, float dt)
        {
            sun.Rotate(0f, 0f, 25f * dt, Space.Self);
            float bright = weather == 0 || weather == 3 ? 1f : 0.25f;
            SetGlow(sunMat, C("#ffb02a"), 2.4f * bright);
            foreach (var r in rays) r.localScale = Vector3.one * (0.6f + 0.4f * bright + 0.1f * Mathf.Sin(t * 5f));
            cloudMat.color = weather == 1 || weather == 2 ? C("#7a808c") : C("#f4f6fa");
            cloud.localPosition = new Vector3(-0.12f + 0.05f * Mathf.Sin(t * 0.7f), -0.12f + (weather == 1 || weather == 2 ? 0.12f : 0f), -0.1f);
            if (weather == 1 || weather == 2)
                Drip(rain, new Vector3(Rand(-HalfW, HalfW), HalfH + 0.5f, 1f), new Color(0.6f, 0.75f, 1f, 0.7f), 90f, 6f, new Vector3(-0.15f, -1f, 0f), 0.05f, dt);
            if (weather == 2 && Every(0.9f)) Bolt(new Vector3(Rand(-HalfW, HalfW) * 0.8f, HalfH, 0.5f), new Vector3(Rand(-HalfW, HalfW) * 0.8f, -HalfH * 0.4f, 0.5f), C("#e8f0ff"), 0.25f, 0.3f);
            if (weather == 2 && Every(0.9f)) { Flash(Color.white, 0.25f); Sfx(SpectacleSfx.Crash, 0.5f, Rand(0.8f, 1.2f)); }
            if (weather == 0) Drip(snow, new Vector3(Rand(-HalfW, HalfW), HalfH + 0.5f, 2f), new Color(1f, 0.95f, 0.6f, 0.5f), 4f, 0.6f, Vector3.down, 0.3f, dt);
            if (rainbow.gameObject.activeSelf) rainbow.localRotation = Quaternion.identity;
        }

        protected override void OnBeat(int n) => SetWeather(n % 4);
    }

    /// <summary>N020 ヒーロー：巨大ロボのヒーローが、モニターの敵と戦う</summary>
    public class HeroSpectacle : RecordSpectacle
    {
        public override string Key => "Hero";
        public override string Title => "SUPER HERO";
        public override string Catch => "落とすたびに攻撃！";
        public override Color Theme => C("#3a7aff");
        public override string BeatText => "FINAL ATTACK!";

        Transform robot, armR, sword, enemy;
        Material eyes, core;
        ParticleSystem dust, blast;
        float slash;

        protected override void Build()
        {
            robot = Group(Hero, "Robot", Vector3.zero);
            var white = Mat(C("#eef0f6"), 0.4f, 0.8f);
            var blue = Mat(C("#2a5ad8"), 0.5f, 0.8f);
            var red = Mat(C("#d8202a"), 0.4f, 0.8f);
            var gold = Mat(C("#f2c23a"), 0.9f, 0.85f);
            Box(robot, new Vector3(0f, 0.05f, 0f), new Vector3(0.36f, 0.3f, 0.22f), blue);
            Box(robot, new Vector3(0f, -0.14f, 0f), new Vector3(0.26f, 0.1f, 0.18f), red);
            core = Glow(C("#5af0ff"), 3f);
            Prism(robot, new Vector3(0f, 0.08f, -0.115f), new[] { new Vector2(0f, 0.07f), new Vector2(-0.07f, -0.04f), new Vector2(0.07f, -0.04f) }, 0.02f, core);
            Box(robot, new Vector3(-0.26f, 0.15f, 0f), new Vector3(0.16f, 0.12f, 0.2f), white);
            Box(robot, new Vector3(0.26f, 0.15f, 0f), new Vector3(0.16f, 0.12f, 0.2f), white);
            // 肩のよろい・胸の線・ベルトのバックル・背中のブースター
            Box(robot, new Vector3(-0.27f, 0.22f, 0f), new Vector3(0.2f, 0.05f, 0.24f), blue, new Vector3(0f, 0f, 12f));
            Box(robot, new Vector3(0.27f, 0.22f, 0f), new Vector3(0.2f, 0.05f, 0.24f), blue, new Vector3(0f, 0f, -12f));
            Box(robot, new Vector3(-0.1f, 0.15f, -0.112f), new Vector3(0.1f, 0.02f, 0.01f), gold, new Vector3(0f, 0f, -20f));
            Box(robot, new Vector3(0.1f, 0.15f, -0.112f), new Vector3(0.1f, 0.02f, 0.01f), gold, new Vector3(0f, 0f, 20f));
            Box(robot, new Vector3(0f, -0.14f, -0.095f), new Vector3(0.08f, 0.06f, 0.02f), gold);
            Cyl(robot, new Vector3(-0.1f, 0.05f, 0.14f), 0.08f, 0.22f, Mat(C("#9aa0aa"), 0.9f, 0.7f));
            Cyl(robot, new Vector3(0.1f, 0.05f, 0.14f), 0.08f, 0.22f, Mat(C("#9aa0aa"), 0.9f, 0.7f));
            Cone(robot, new Vector3(-0.1f, -0.06f, 0.14f), 0.045f, 0.12f, Add(new Color(0.4f, 0.8f, 1f, 0.8f)), new Vector3(180f, 0f, 0f), 12);
            Cone(robot, new Vector3(0.1f, -0.06f, 0.14f), 0.045f, 0.12f, Add(new Color(0.4f, 0.8f, 1f, 0.8f)), new Vector3(180f, 0f, 0f), 12);
            var head = Group(robot, "Head", new Vector3(0f, 0.3f, 0f));
            Box(head, Vector3.zero, new Vector3(0.16f, 0.16f, 0.16f), white);
            eyes = Glow(C("#ffe84a"), 3f);
            Box(head, new Vector3(0f, 0.01f, -0.081f), new Vector3(0.12f, 0.03f, 0.01f), eyes);
            Box(head, new Vector3(0f, -0.05f, -0.082f), new Vector3(0.08f, 0.04f, 0.01f), Mat(C("#c8ccd6"), 0.6f, 0.8f));
            Box(head, new Vector3(-0.085f, 0f, 0f), new Vector3(0.02f, 0.08f, 0.1f), Mat(C("#d8202a"), 0.4f, 0.8f));
            Box(head, new Vector3(0.085f, 0f, 0f), new Vector3(0.02f, 0.08f, 0.1f), Mat(C("#d8202a"), 0.4f, 0.8f));
            Prism(head, new Vector3(0f, 0.09f, -0.06f), new[] { new Vector2(0f, -0.02f), new Vector2(-0.14f, 0.1f), new Vector2(-0.1f, 0.1f), new Vector2(0f, 0.03f), new Vector2(0.1f, 0.1f), new Vector2(0.14f, 0.1f) }, 0.02f, gold);
            Box(robot, new Vector3(-0.27f, -0.02f, 0f), new Vector3(0.1f, 0.24f, 0.1f), white, new Vector3(0f, 0f, -8f));
            armR = Group(robot, "ArmR", new Vector3(0.27f, 0.12f, 0f));
            Box(armR, new Vector3(0f, -0.14f, 0f), new Vector3(0.1f, 0.24f, 0.1f), white);
            sword = Group(armR, "Sword", new Vector3(0f, -0.26f, -0.04f), new Vector3(-90f, 0f, 0f));
            Box(sword, new Vector3(0f, 0.04f, 0f), new Vector3(0.12f, 0.03f, 0.04f), gold);
            Box(sword, new Vector3(0f, 0.32f, 0f), new Vector3(0.05f, 0.55f, 0.015f), Glow(C("#9af0ff"), 2.5f));
            Box(robot, new Vector3(-0.09f, -0.33f, 0f), new Vector3(0.12f, 0.3f, 0.14f), white);
            Box(robot, new Vector3(0.09f, -0.33f, 0f), new Vector3(0.12f, 0.3f, 0.14f), white);
            Box(robot, new Vector3(-0.09f, -0.48f, -0.03f), new Vector3(0.14f, 0.06f, 0.2f), blue);
            Box(robot, new Vector3(0.09f, -0.48f, -0.03f), new Vector3(0.14f, 0.06f, 0.2f), blue);
            Halo(Hero, new Vector3(0f, 0f, 0.5f), new Color(0.3f, 0.6f, 1f, 0.55f), 2f);

            enemy = Group(Set, "Enemy", new Vector3(-HalfW * 0.55f, 1.2f, 3f), Vector3.zero, 2.2f);
            var purple = Mat(C("#7a2ad8"), 0.2f, 0.6f);
            Egg(enemy, Vector3.zero, new Vector3(1f, 0.85f, 0.7f), purple);
            Cone(enemy, new Vector3(-0.25f, 0.32f, 0f), 0.1f, 0.35f, Mat(C("#f2e8d8")), new Vector3(0f, 0f, 25f));
            Cone(enemy, new Vector3(0.25f, 0.32f, 0f), 0.1f, 0.35f, Mat(C("#f2e8d8")), new Vector3(0f, 0f, -25f));
            Ball(enemy, new Vector3(-0.18f, 0.08f, -0.3f), 0.18f, Glow(C("#ff2a2a"), 3f));
            Ball(enemy, new Vector3(0.18f, 0.08f, -0.3f), 0.18f, Glow(C("#ff2a2a"), 3f));
            for (int i = 0; i < 5; i++) Cone(enemy, new Vector3(-0.2f + i * 0.1f, -0.18f, -0.31f), 0.035f, 0.1f, Mat(Color.white), new Vector3(180f, 0f, 0f), 6);
            enemy.gameObject.SetActive(false);
            dust = Fx(0.7f, 1.4f, -0.02f, null, false, 2.2f);
            blast = Fx(0.3f, 0.9f, 0.3f);
        }

        void Slash()
        {
            slash = 1f;
            Shock(HeroPoint(new Vector3(0.3f, 0f, -0.1f)), C("#9af0ff"), 0.4f, 3f, 0.3f, new Vector3(90f, 0f, 35f));
            Sfx(SpectacleSfx.Whoosh, 0.6f, 1.8f);
        }

        void SpawnEnemy()
        {
            enemy.gameObject.SetActive(true);
            enemy.localPosition = new Vector3(-HalfW * 0.55f, 1.2f, 3f);
            enemy.localScale = Vector3.one * 2.2f;
            PopIn(enemy, 0.4f);
            Sfx(SpectacleSfx.Rise, 0.6f, 0.5f);
        }

        void Finisher(float delay)
        {
            After(delay, () =>
            {
                Vector3 a = HeroPoint(new Vector3(0f, 0.08f, -0.12f)), b = enemy.localPosition;
                var beam = Box(Set, (a + b) * 0.5f, new Vector3(0.6f, (b - a).magnitude, 0.6f), Add(new Color(0.5f, 0.95f, 1f, 0.9f)), default, false);
                beam.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
                Temp(beam, 0.5f);
                Tween(0.5f, k => beam.localScale = new Vector3(0.6f * (1f - k), (b - a).magnitude, 0.6f * (1f - k)));
                Flash(C("#9af0ff"), 0.5f);
                Sfx(SpectacleSfx.Laser);
                SlowMo(0.3f, 0.5f);
                After(0.25f, () =>
                {
                    Burst(blast, enemy.localPosition, C("#ffb02a"), 120, 10f);
                    Burst(blast, enemy.localPosition, Color.white, 60, 6f);
                    Shock(enemy.localPosition, C("#ffb02a"), 0.5f, 10f, 0.7f);
                    Shake(0.15f, 0.7f);
                    Sfx(SpectacleSfx.Boom);
                    Sfx(SpectacleSfx.Crash);
                    enemy.gameObject.SetActive(false);
                });
            });
        }

        protected override void OnBegin() { enemy.gameObject.SetActive(false); }

        protected override void OnIntro(float t)
        {
            // 上から降ってきて着地する
            if (t < 0.3f) HeroOffset = Vector3.up * 26f;
            else if (t < 1.1f)
            {
                float k = (t - 0.3f) / 0.8f;
                HeroOffset = Vector3.up * (12f * (1f - Ease.InCubic(k)) + 13f * (1f - Ease.OutBack(k)));
            }
            else HeroOffset = Vector3.zero;
            if (Cross(1.1f))
            {
                Burst(dust, Director.FocusPos + Vector3.down * 2.8f, new Color(0.85f, 0.85f, 0.9f, 0.8f), 40, 4f, Vector3.up, 1f);
                Shake(0.2f, 0.6f);
                Sfx(SpectacleSfx.Boom);
            }
            if (Cross(1.6f)) SpawnEnemy();
            if (Cross(2.1f)) Finisher(0f);
        }

        protected override void Idle(float t, float dt)
        {
            slash *= Mathf.Exp(-dt * 6f);
            armR.localRotation = Quaternion.Euler(-120f * slash, 0f, 20f + 60f * slash);
            robot.localPosition = new Vector3(0f, 0.015f * Mathf.Sin(t * 2f), 0f);
            SetGlow(core, C("#5af0ff"), 2.5f + 1.5f * Mathf.Sin(t * 4f));
            if (enemy.gameObject.activeSelf) enemy.localRotation = Quaternion.Euler(0f, 15f * Mathf.Sin(t * 2f), 6f * Mathf.Sin(t * 3f));
        }

        protected override void OnBeat(int n)
        {
            SpawnEnemy();
            Finisher(1f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type != DropZoneType.Center) return;
            Slash();
            Sparkle(at, C("#9af0ff"), 10, 4f, Vector3.up, 0.5f);
        }
    }

    /// <summary>N021 応援団：大太鼓と大きな旗。コンボで盛り上がる</summary>
    public class CheerSpectacle : RecordSpectacle
    {
        public override string Key => "Cheer";
        public override string Title => "FIGHT! FIGHT!";
        public override string Catch => "続けて落としてコンボ";
        public override Color Theme => C("#ff3a3a");
        public override string BeatText => "フレー！フレー！";

        Transform drum, flagL, flagR, stickL, stickR;
        float hit, lastDrop = -10f;
        int combo;

        protected override void Build()
        {
            drum = Group(Hero, "Drum", new Vector3(0f, -0.12f, 0f), new Vector3(90f, 0f, 0f));
            Lathe(drum, Vector3.zero, new[] { new Vector2(0.28f, -0.22f), new Vector2(0.34f, -0.08f), new Vector2(0.35f, 0f), new Vector2(0.34f, 0.08f), new Vector2(0.28f, 0.22f) }, Mat(C("#8a3a1a"), 0.2f, 0.7f));
            Cyl(drum, new Vector3(0f, -0.225f, 0f), 0.58f, 0.01f, Mat(C("#f2e6c8")));
            Cyl(drum, new Vector3(0f, 0.225f, 0f), 0.58f, 0.01f, Mat(C("#f2e6c8")));
            Ring(drum, new Vector3(0f, -0.2f, 0f), 0.29f, 0.02f, Mat(C("#1a1a1a"), 0.5f, 0.6f));
            Ring(drum, new Vector3(0f, 0.2f, 0f), 0.29f, 0.02f, Mat(C("#1a1a1a"), 0.5f, 0.6f));
            Prism(drum, new Vector3(0f, -0.235f, 0f), new[] { new Vector2(0f, 0.12f), new Vector2(-0.1f, -0.06f), new Vector2(0.1f, -0.06f) }, 0.01f, Mat(C("#e8141c")), new Vector3(-90f, 0f, 0f));
            stickL = Group(Hero, "StickL", new Vector3(-0.35f, 0.3f, -0.3f));
            Tube(stickL, Vector3.zero, new Vector3(0.18f, -0.3f, 0f), 0.04f, Mat(C("#f2dcb0")));
            Ball(stickL, new Vector3(0.18f, -0.3f, 0f), 0.08f, Mat(C("#e8141c")));
            stickR = Group(Hero, "StickR", new Vector3(0.35f, 0.3f, -0.3f));
            Tube(stickR, Vector3.zero, new Vector3(-0.18f, -0.3f, 0f), 0.04f, Mat(C("#f2dcb0")));
            Ball(stickR, new Vector3(-0.18f, -0.3f, 0f), 0.08f, Mat(C("#e8141c")));
            flagL = MakeFlag(new Vector3(-0.6f, -0.3f, 0.2f), C("#e8141c"));
            flagR = MakeFlag(new Vector3(0.6f, -0.3f, 0.2f), C("#f4f4f4"));
            Halo(Hero, new Vector3(0f, 0f, 0.6f), new Color(1f, 0.3f, 0.3f, 0.5f), 2.2f);
        }

        Transform MakeFlag(Vector3 pos, Color c)
        {
            var g = Group(Hero, "Flag", pos);
            Tube(g, Vector3.zero, new Vector3(0f, 1.1f, 0f), 0.025f, Mat(C("#2a1a0a")));
            var cloth = Group(g, "Cloth", new Vector3(0f, 1.05f, 0f));
            for (int i = 0; i < 6; i++) Box(cloth, new Vector3(0.06f + i * 0.08f, -0.18f, 0f), new Vector3(0.08f, 0.36f, 0.01f), Mat(i % 2 == 0 ? c : Color.Lerp(c, Color.black, 0.15f)));
            Ball(g, new Vector3(0f, 1.12f, 0f), 0.05f, Mat(C("#f2c23a"), 0.9f, 0.85f));
            return g;
        }

        void Hit(float power)
        {
            hit = 1f;
            Pulse(power);
            Shock(HeroPoint(new Vector3(0f, -0.12f, -0.25f)), Theme, 0.3f, 4f * power, 0.4f);
            Sfx(SpectacleSfx.Drum, power);
        }

        protected override void OnBegin() { combo = 0; lastDrop = -10f; }

        protected override void OnIntro(float t)
        {
            if (Cross(1.1f)) Hit(1f);
            if (Cross(1.4f)) Hit(1f);
            if (Cross(1.7f)) { Hit(1.3f); Confetti(80, Theme, Color.white); }
        }

        protected override void Idle(float t, float dt)
        {
            hit *= Mathf.Exp(-dt * 8f);
            stickL.localRotation = Quaternion.Euler(0f, 0f, -30f * hit);
            stickR.localRotation = Quaternion.Euler(0f, 0f, 30f * hit);
            for (int f = 0; f < 2; f++)
            {
                var flag = f == 0 ? flagL : flagR;
                flag.localRotation = Quaternion.Euler(0f, 0f, (f == 0 ? 1f : -1f) * (12f + 14f * Mathf.Sin(t * 2.4f + f * 3.14f)));
                var cloth = flag.GetChild(1);
                for (int i = 0; i < cloth.childCount; i++)
                    cloth.GetChild(i).localRotation = Quaternion.Euler(0f, 25f * Mathf.Sin(t * 6f - i * 0.8f), 0f);
            }
        }

        protected override void OnBeat(int n)
        {
            for (int i = 0; i < 3; i++) After(i * 0.3f, () => Hit(1f));
            Confetti(60, Theme, Color.white);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type != DropZoneType.Center) return;
            combo = T - lastDrop <= 1f ? combo + 1 : 1;
            lastDrop = T;
            Hit(Mathf.Min(1.5f, 0.6f + combo * 0.1f));
            if (combo >= 3 && combo % 3 == 0)
            {
                Banner($"{combo} COMBO!", Theme);
                Confetti(20 + combo * 4, Theme, Color.white);
            }
        }
    }

    /// <summary>N022 リズム：ドラムセット。ビートに合わせて光の輪が広がる</summary>
    public class RhythmSpectacle : RecordSpectacle
    {
        public override string Key => "Rhythm";
        public override string Title => "RHYTHM STAGE";
        public override string Catch => "ビートに合わせて落とせ";
        public override Color Theme => C("#ff4ad8");
        public override float BeatInterval => 8f;
        public override string BeatText => "PERFECT!";

        Transform kick, snare, cymbal, hat;
        Material kickFace;
        float bpm = 120f, kickHit, snareHit;
        float BeatPhase => T * bpm / 60f;

        protected override void Build()
        {
            var shell = Mat(C("#c8141c"), 0.6f, 0.9f);
            var chrome = Mat(C("#e0e4ea"), 0.95f, 0.9f);
            var brass = Mat(C("#e8b84a"), 0.95f, 0.8f);
            kick = Group(Hero, "Kick", new Vector3(0f, -0.2f, 0.05f), new Vector3(90f, 0f, 0f));
            Cyl(kick, Vector3.zero, 0.55f, 0.32f, shell);
            kickFace = Glow(C("#ffe0f4"), 1.2f);
            Cyl(kick, new Vector3(0f, -0.162f, 0f), 0.5f, 0.01f, kickFace);
            Ring(kick, new Vector3(0f, -0.16f, 0f), 0.275f, 0.02f, chrome);
            Star(kick, new Vector3(0f, -0.168f, 0f), 5, 0.12f, 0.05f, 0.01f, Glow(Theme, 2f), new Vector3(90f, 0f, 0f));
            snare = Group(Hero, "Snare", new Vector3(-0.38f, -0.05f, -0.05f), new Vector3(-20f, 0f, 0f));
            Cyl(snare, Vector3.zero, 0.26f, 0.12f, chrome);
            Cyl(snare, new Vector3(0f, 0.061f, 0f), 0.25f, 0.005f, Mat(Color.white));
            var tom = Group(Hero, "Tom", new Vector3(0.12f, 0.15f, 0f), new Vector3(-25f, 0f, 0f));
            Cyl(tom, Vector3.zero, 0.22f, 0.16f, shell);
            Cyl(tom, new Vector3(0f, 0.081f, 0f), 0.21f, 0.005f, Mat(Color.white));
            cymbal = Group(Hero, "Cymbal", new Vector3(0.38f, 0.35f, 0.05f), new Vector3(-15f, 0f, 10f));
            Lathe(cymbal, Vector3.zero, new[] { new Vector2(0f, 0.03f), new Vector2(0.04f, 0.025f), new Vector2(0.06f, 0.01f), new Vector2(0.22f, -0.01f), new Vector2(0.22f, -0.015f), new Vector2(0f, 0.015f) }, brass);
            Tube(Hero, new Vector3(0.38f, 0.35f, 0.05f), new Vector3(0.42f, -0.45f, 0.1f), 0.02f, chrome);
            hat = Group(Hero, "Hat", new Vector3(-0.42f, 0.2f, 0.05f));
            Cyl(hat, Vector3.zero, 0.24f, 0.01f, brass);
            Cyl(hat, new Vector3(0f, 0.02f, 0f), 0.24f, 0.01f, brass);
            Tube(Hero, new Vector3(-0.42f, 0.2f, 0.05f), new Vector3(-0.42f, -0.45f, 0.05f), 0.02f, chrome);
            Halo(Hero, new Vector3(0f, 0f, 0.6f), new Color(1f, 0.3f, 0.85f, 0.5f), 2.2f);
        }

        protected override void OnBegin()
        {
            bpm = 120f;
            var rec = Object.FindFirstObjectByType<RhythmRecord>();
            if (rec != null)
            {
                var f = typeof(RhythmRecord).GetField("bpm");
                if (f != null && f.FieldType == typeof(float)) bpm = Mathf.Clamp((float)f.GetValue(rec), 60f, 200f);
            }
        }

        protected override void Idle(float t, float dt)
        {
            kickHit *= Mathf.Exp(-dt * 10f);
            snareHit *= Mathf.Exp(-dt * 10f);
            float step = 60f / bpm;
            if (Every(step))
            {
                int beat = Mathf.FloorToInt(BeatPhase);
                if (beat % 2 == 0) { kickHit = 1f; Shock(HeroPoint(new Vector3(0f, -0.2f, -0.2f)), Theme, 0.3f, 3f, 0.4f); }
                else snareHit = 1f;
                if (!InIntro && beat % 4 == 0) Shock(new Vector3(0f, -HalfH * 0.75f, 0f), Theme, 0.5f, 4f, 0.5f);
            }
            kick.localScale = Vector3.one * (1f + 0.08f * kickHit);
            SetGlow(kickFace, Color.Lerp(C("#ffe0f4"), Theme, kickHit), 1.2f + 3f * kickHit);
            snare.localRotation = Quaternion.Euler(-20f - 8f * snareHit, 0f, 0f);
            cymbal.localRotation = Quaternion.Euler(-15f + 10f * Mathf.Sin(t * 9f) * snareHit, 0f, 10f);
            hat.localPosition = new Vector3(-0.42f, 0.2f - 0.02f * Mathf.Abs(Mathf.Sin(BeatPhase * Mathf.PI)), 0.05f);
        }

        protected override void OnBeat(int n)
        {
            for (int i = 0; i < 4; i++) After(i * 60f / bpm * 0.5f, () => { Flash(Theme, 0.15f); Kick(0.3f); Sfx(SpectacleSfx.Drum, 0.7f); });
            Sparkle(HeroPoint(Vector3.zero), Theme, 40, 6f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type != DropZoneType.Center) return;
            float off = Mathf.Abs(BeatPhase - Mathf.Round(BeatPhase));
            if (off < 0.15f)
            {
                Sparkle(at, Theme, 20, 5f, Vector3.up, 0.6f);
                Shock(at, Color.white, 0.3f, 3f, 0.4f);
                Sfx(SpectacleSfx.Sparkle, 0.6f);
            }
            else Sparkle(at, Color.white, 6, 2f, Vector3.up, 0.5f);
        }
    }

    /// <summary>N023 ルーレット：回る盤と、跳ねる白い玉</summary>
    public class RouletteSpectacle : RecordSpectacle
    {
        public override string Key => "Roulette";
        public override string Title => "ROULETTE";
        public override string Catch => "10枚ごとに運命の回転";
        public override Color Theme => C("#2ad88a");
        public override float BeatInterval => 0f;
        public override int BeatEveryDrops => 10;
        public override string BeatText => "ROULETTE!";

        Transform wheel, ball;
        float wheelSpeed = 30f, ballAngle, ballSpeed, ballR = 0.42f;

        protected override void Build()
        {
            var plate = Group(Hero, "Plate", Vector3.zero, new Vector3(-62f, 0f, 0f));
            Cyl(plate, new Vector3(0f, -0.03f, 0f), 1.04f, 0.06f, Mat(C("#5a2a12"), 0.2f, 0.8f));
            Ring(plate, Vector3.zero, 0.5f, 0.03f, Mat(C("#f2c23a"), 0.95f, 0.85f));
            wheel = Group(plate, "Wheel", Vector3.zero);
            var red = Mat(C("#d8141c"), 0.3f, 0.8f);
            var black = Mat(C("#151515"), 0.3f, 0.8f);
            for (int i = 0; i < 18; i++)
                Wedge(wheel, new Vector3(0f, 0.01f, 0f), 0.4f, i * 20f, (i + 1) * 20f, 0.02f, i == 0 ? Mat(C("#1aa85a"), 0.3f, 0.8f) : i % 2 == 0 ? red : black, new Vector3(90f, 0f, 0f));
            Cone(wheel, new Vector3(0f, 0.02f, 0f), 0.1f, 0.12f, Mat(C("#f2c23a"), 0.95f, 0.85f));
            for (int i = 0; i < 4; i++) Box(wheel, new Vector3(0f, 0.12f, 0f), new Vector3(0.26f, 0.015f, 0.015f), Mat(C("#f2c23a"), 0.95f, 0.85f), new Vector3(0f, i * 45f, 0f));
            ball = Ball(plate, new Vector3(0.42f, 0.04f, 0f), 0.06f, Mat(Color.white, 0.1f, 0.95f));
            Halo(Hero, new Vector3(0f, 0f, 0.5f), new Color(0.2f, 0.9f, 0.5f, 0.45f), 2.1f);
        }

        void Spin()
        {
            wheelSpeed = 720f;
            ballSpeed = -1100f;
            ballR = 0.47f;
            Sfx(SpectacleSfx.Whoosh, 0.7f, 1.4f);
            After(2.2f, () => { Sparkle(HeroPoint(Vector3.zero), Theme, 50, 6f); Sfx(SpectacleSfx.Chime); Kick(0.5f); });
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.0f)) Spin();
        }

        protected override void Idle(float t, float dt)
        {
            wheelSpeed = Mathf.MoveTowards(wheelSpeed, 30f, dt * 300f);
            ballSpeed = Mathf.MoveTowards(ballSpeed, 0f, dt * 480f);
            if (Mathf.Abs(ballSpeed) < 200f) ballR = Mathf.MoveTowards(ballR, 0.33f, dt * 0.3f);
            wheel.Rotate(0f, wheelSpeed * dt, 0f, Space.Self);
            ballAngle += (Mathf.Abs(ballSpeed) < 5f ? wheelSpeed : ballSpeed) * dt;
            float a = ballAngle * Mathf.Deg2Rad;
            float bounce = Mathf.Abs(ballSpeed) > 20f && ballR < 0.45f ? Mathf.Abs(Mathf.Sin(t * 25f)) * 0.03f : 0f;
            ball.localPosition = new Vector3(Mathf.Cos(a) * ballR, 0.04f + bounce, Mathf.Sin(a) * ballR);
        }

        protected override void OnBeat(int n) => Spin();

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Sparkle(at, Theme, 6, 3f, Vector3.up, 0.5f);
        }
    }

    /// <summary>N024 おみくじ：鳥居とおみくじの箱。桜が舞う</summary>
    public class OmikujiSpectacle : RecordSpectacle
    {
        public override string Key => "Omikuji";
        public override string Title => "OMIKUJI";
        public override string Catch => "今日の運だめし";
        public override Color Theme => C("#e8263a");
        public override float BeatInterval => 12f;
        public override string BeatText => "";

        Transform box, stick, bell;
        ParticleSystem petals;

        protected override void Build()
        {
            var vermilion = Mat(C("#e0401c"), 0.2f, 0.6f);
            var black = Mat(C("#1a1a1a"));
            var gate = Group(Hero, "Torii", new Vector3(0f, 0.05f, 0.35f));
            Cyl(gate, new Vector3(-0.42f, -0.1f, 0f), 0.08f, 0.9f, vermilion);
            Cyl(gate, new Vector3(0.42f, -0.1f, 0f), 0.08f, 0.9f, vermilion);
            Box(gate, new Vector3(0f, 0.25f, 0f), new Vector3(1f, 0.06f, 0.08f), vermilion);
            Box(gate, new Vector3(0f, 0.4f, 0f), new Vector3(1.2f, 0.07f, 0.1f), black, new Vector3(0f, 0f, 0f));
            Box(gate, new Vector3(0f, 0.34f, 0f), new Vector3(1.1f, 0.06f, 0.09f), vermilion);
            Box(gate, new Vector3(0f, 0.3f, -0.05f), new Vector3(0.12f, 0.1f, 0.02f), Mat(C("#f2c23a"), 0.9f, 0.8f));

            box = Group(Hero, "Box", new Vector3(0f, -0.15f, -0.1f), new Vector3(0f, 0f, 0f));
            Lathe(box, new Vector3(0f, -0.25f, 0f), new[] { new Vector2(0f, 0f), new Vector2(0.16f, 0f), new Vector2(0.16f, 0.5f), new Vector2(0f, 0.5f) }, Mat(C("#c8282a"), 0.2f, 0.7f), Vector3.zero, 6);
            Lathe(box, new Vector3(0f, 0.25f, 0f), new[] { new Vector2(0f, 0f), new Vector2(0.165f, 0f), new Vector2(0.165f, 0.02f), new Vector2(0f, 0.02f) }, Mat(C("#f2c23a"), 0.9f, 0.85f), Vector3.zero, 6);
            Box(box, new Vector3(0f, 0f, -0.14f), new Vector3(0.12f, 0.3f, 0.01f), Mat(C("#f8f4ea")));
            stick = Group(Hero, "Stick", new Vector3(0f, 0.1f, -0.1f));
            Box(stick, Vector3.zero, new Vector3(0.03f, 0.3f, 0.015f), Mat(C("#f2dcb0")));
            Box(stick, new Vector3(0f, 0.12f, -0.01f), new Vector3(0.032f, 0.06f, 0.005f), Mat(C("#e8141c")));
            stick.gameObject.SetActive(false);
            bell = Group(Hero, "Bell", new Vector3(0f, 0.3f, 0.2f));
            Lathe(bell, new Vector3(0f, -0.08f, 0f), new[] { new Vector2(0f, 0.12f), new Vector2(0.04f, 0.11f), new Vector2(0.07f, 0.05f), new Vector2(0.09f, 0f), new Vector2(0f, 0f) }, Mat(C("#f2c23a"), 0.95f, 0.85f));
            Halo(Hero, new Vector3(0f, 0f, 0.6f), new Color(1f, 0.6f, 0.7f, 0.5f), 2.2f);
            petals = Fx(0.22f, 5f, 0.05f, null, false, 1f);
        }

        void Draw()
        {
            Tween(0.8f, k => box.localRotation = Quaternion.Euler(0f, 0f, 25f * Mathf.Sin(k * 30f) * (1f - k)));
            Sfx(SpectacleSfx.Bell, 0.8f);
            After(0.8f, () =>
            {
                stick.gameObject.SetActive(true);
                Vector3 from = new Vector3(0f, 0.1f, -0.1f);
                Tween(0.6f, k =>
                {
                    stick.localPosition = from + Vector3.up * Ease.OutBack(k) * 0.4f;
                    stick.localRotation = Quaternion.Euler(0f, 0f, k * 720f);
                }, () =>
                {
                    Flash(C("#fff2b0"), 0.45f);
                    Sparkle(HeroPoint(stick.localPosition), C("#ffe27a"), 60, 6f);
                    Banner("おみくじ！", Theme);
                    Sfx(SpectacleSfx.Fanfare, 0.8f);
                    Kick(0.6f);
                });
                Sfx(SpectacleSfx.Rise, 0.5f, 1.5f);
            });
            After(2.6f, () => stick.gameObject.SetActive(false));
        }

        protected override void OnBegin() => stick.gameObject.SetActive(false);

        protected override void OnIntro(float t)
        {
            if (Cross(0.5f)) Draw();
            if (Cross(1.2f)) for (int i = 0; i < 80; i++) Burst(petals, new Vector3(Rand(-HalfW, HalfW), HalfH + Rand(0f, 2f), Rand(0f, 2f)), Color.Lerp(C("#ffc8dc"), Color.white, Rand(0f, 0.5f)), 1, 1.2f, new Vector3(0.4f, -1f, 0f), 0.6f);
        }

        protected override void Idle(float t, float dt)
        {
            Drip(petals, new Vector3(Rand(-HalfW, HalfW), HalfH + 0.5f, Rand(0f, 2f)), Color.Lerp(C("#ffc8dc"), Color.white, Rand(0f, 0.5f)), 5f, 1f, new Vector3(0.4f, -1f, 0f), 0.6f, dt);
            bell.localRotation = Quaternion.Euler(0f, 0f, 8f * Mathf.Sin(t * 2.2f));
        }

        protected override void OnBeat(int n)
        {
            Sfx(SpectacleSfx.Bell, 0.8f, 0.9f);
            for (int i = 0; i < 50; i++) Burst(petals, new Vector3(-HalfW - 0.5f, Rand(-2f, 5f), Rand(0f, 2f)), Color.Lerp(C("#ffc8dc"), Color.white, Rand(0f, 0.5f)), 1, 3f, new Vector3(1f, -0.2f, 0f), 0.4f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Sparkle(at, C("#ffc8dc"), 6, 2.5f, Vector3.up, 0.6f);
        }
    }

    /// <summary>N025 ゲームミュージック：ドットの勇者と、アイテムの箱、コイン</summary>
    public class GameMusicSpectacle : RecordSpectacle
    {
        public override string Key => "GameMusic";
        public override string Title => "PRESS START";
        public override string Catch => "ノルマ未達でもクリア扱い";
        public override Color Theme => C("#5af08a");
        public override string BeatText => "1UP!";

        Transform hero8, itemBox;
        Material boxMat;
        ParticleSystem pixels;

        // ドット絵（下から上）。. は空き
        static readonly string[] Sprite =
        {
            "..BB..BB..",
            "..BB..BB..",
            "...LLLL...",
            "..LLLLLL..",
            ".SLLLLLLS.",
            ".S.TTTT.S.",
            "...TTTT...",
            "..FFFFFF..",
            "..FEFFEF..",
            "..FFFFFF..",
            ".HHHHHHHH.",
            "..HHHHHH..",
            "...HHHH...",
        };

        protected override void Build()
        {
            hero8 = Group(Hero, "Pixel", new Vector3(0.12f, -0.02f, 0f));
            var mats = new Dictionary<char, Material>
            {
                { 'B', Mat(C("#5a3a1a")) }, { 'L', Mat(C("#2a5ad8")) }, { 'S', Mat(C("#ffcfa0")) }, { 'T', Mat(C("#2ad8a0")) },
                { 'F', Mat(C("#ffcfa0")) }, { 'E', Mat(C("#1a1a1a")) }, { 'H', Mat(C("#ff6a2a"), 0.4f, 0.7f) },
            };
            float px = 0.06f;
            for (int y = 0; y < Sprite.Length; y++)
                for (int x = 0; x < Sprite[y].Length; x++)
                {
                    char ch = Sprite[y][x];
                    if (ch == '.' || !mats.ContainsKey(ch)) continue;
                    Box(hero8, new Vector3((x - 4.5f) * px, (y - 6f) * px - 0.05f, 0f), new Vector3(px, px, px), mats[ch]);
                }
            Box(hero8, new Vector3(0.36f, 0.05f, 0f), new Vector3(0.03f, 0.4f, 0.03f), Glow(C("#e0f8ff"), 2f));
            Box(hero8, new Vector3(0.36f, -0.15f, 0f), new Vector3(0.12f, 0.03f, 0.04f), Mat(C("#f2c23a"), 0.9f, 0.8f));
            itemBox = Group(Hero, "ItemBox", new Vector3(-0.32f, 0.38f, 0f));
            boxMat = Glow(C("#f2a02a"), 1.4f);
            Box(itemBox, Vector3.zero, Vector3.one * 0.24f, boxMat);
            Star(itemBox, new Vector3(0f, 0f, -0.125f), 5, 0.08f, 0.035f, 0.01f, Glow(Color.white, 2.5f));
            for (int i = 0; i < 4; i++) Box(itemBox, new Vector3((i % 2 == 0 ? -1 : 1) * 0.09f, (i < 2 ? -1 : 1) * 0.09f, -0.121f), Vector3.one * 0.02f, Mat(C("#5a3a1a")));
            Box(Hero, new Vector3(0f, -0.48f, 0f), new Vector3(1.2f, 0.08f, 0.3f), Mat(C("#c86a2a")));
            Box(Hero, new Vector3(0f, -0.43f, 0f), new Vector3(1.2f, 0.03f, 0.3f), Mat(C("#5ad84a")));
            pixels = Fx(0.22f, 1.2f, 1.2f, null, true, 1f);
        }

        void Bump()
        {
            Vector3 home = new Vector3(-0.32f, 0.38f, 0f);
            Tween(0.25f, k => itemBox.localPosition = home + Vector3.up * Ease.Spike(k) * 0.08f);
            var coin = Group(Hero, "Coin", home + Vector3.up * 0.15f);
            Cyl(coin, Vector3.zero, 0.16f, 0.03f, Glow(C("#ffd84a"), 2f), new Vector3(90f, 0f, 0f));
            Track(coin);
            Tween(0.7f, k =>
            {
                coin.localPosition = home + Vector3.up * (0.15f + Ease.Spike(k) * 0.4f);
                coin.localRotation = Quaternion.Euler(0f, k * 1080f, 0f);
            }, () => { Sparkle(HeroPoint(coin.localPosition), C("#ffd84a"), 12, 3f); Kill(coin); });
            Sfx(SpectacleSfx.Coin, 1f);
        }

        void Jump()
        {
            Tween(0.5f, k => hero8.localPosition = new Vector3(0.12f, -0.02f + Ease.Spike(k) * 0.3f, 0f));
            Sfx(SpectacleSfx.Zap, 0.5f, 2f);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.1f)) { Jump(); After(0.2f, Bump); }
            if (Cross(1.8f)) Sfx(SpectacleSfx.Fanfare, 0.9f, 1.2f);
        }

        protected override void Idle(float t, float dt)
        {
            SetGlow(boxMat, C("#f2a02a"), 1.2f + 0.6f * Mathf.Abs(Mathf.Sin(t * 3f)));
            itemBox.localRotation = Quaternion.Euler(0f, 15f * Mathf.Sin(t), 0f);
            if (!InIntro && Every(2.5f)) Jump();
        }

        protected override void OnBeat(int n)
        {
            Jump();
            After(0.2f, Bump);
            for (int i = 0; i < 60; i++)
            {
                Color c = new[] { C("#ff3a3a"), C("#ffd84a"), C("#5af08a"), C("#3a8aff") }[RandInt(0, 4)];
                Burst(pixels, HeroPoint(new Vector3(-0.32f, 0.38f, 0f)), c, 1, 7f, Vector3.up, 0.8f);
            }
            Sfx(SpectacleSfx.Fanfare, 0.8f, 1.4f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Burst(pixels, at, C("#ffd84a"), 6, 4f, Vector3.up, 0.5f);
        }
    }
}
