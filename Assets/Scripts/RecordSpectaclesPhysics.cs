using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    // ====================================================================
    // 物理（N037〜N040）の大きな演出。ルールは各レコードのスクリプトが受け持つ（ここは見た目と音だけ）
    // ====================================================================

    /// <summary>N037 力士：土俵の上で四股を踏む力士</summary>
    public class SumoSpectacle : RecordSpectacle
    {
        public override string Key => "Sumo";
        public override string Title => "YOKOZUNA";
        public override string Catch => "プッシャーが力強く押す";
        public override Color Theme => C("#e8b04a");
        public override float BeatInterval => 9f;
        public override string BeatText => "どすこい！";

        Transform rikishi, legL, legR, armL, armR;
        ParticleSystem dust, salt;
        float push;

        protected override void Build()
        {
            var clay = Mat(C("#c89a6a"), 0f, 0.2f);
            Cyl(Hero, new Vector3(0f, -0.47f, 0.05f), 1.3f, 0.08f, clay);
            Ring(Hero, new Vector3(0f, -0.43f, 0.05f), 0.5f, 0.025f, Mat(C("#e8d8a8")));
            rikishi = Group(Hero, "Rikishi", new Vector3(0f, -0.02f, 0f));
            var skin = Mat(C("#f2c8a0"), 0f, 0.5f);
            var belt = Mat(C("#2a2a6a"), 0.2f, 0.4f);
            Egg(rikishi, new Vector3(0f, 0f, 0f), new Vector3(0.62f, 0.6f, 0.5f), skin);
            Egg(rikishi, new Vector3(0f, -0.08f, -0.08f), new Vector3(0.52f, 0.46f, 0.42f), skin);
            Egg(rikishi, new Vector3(0f, -0.2f, 0f), new Vector3(0.6f, 0.14f, 0.5f), belt);
            for (int i = 0; i < 5; i++) Box(rikishi, new Vector3(-0.08f + i * 0.04f, -0.32f, -0.24f), new Vector3(0.025f, 0.14f, 0.01f), belt);
            Ball(rikishi, new Vector3(0f, -0.21f, 0.24f), 0.1f, belt);
            Egg(rikishi, new Vector3(0f, -0.24f, -0.25f), new Vector3(0.12f, 0.08f, 0.04f), Mat(C("#3a3a8a"), 0.2f, 0.5f));
            Ring(rikishi, new Vector3(0f, -0.13f, 0f), 0.31f, 0.045f, Mat(C("#f4f0e0"), 0.1f, 0.6f));
            var head = Group(rikishi, "Head", new Vector3(0f, 0.38f, -0.02f));
            Ball(head, Vector3.zero, 0.24f, skin);
            Ball(head, new Vector3(0f, 0.06f, 0.03f), 0.24f, Mat(C("#1a1a1a"), 0.2f, 0.7f));
            Cyl(head, new Vector3(0f, 0.17f, 0.02f), 0.05f, 0.08f, Mat(C("#1a1a1a"), 0.2f, 0.7f), new Vector3(70f, 0f, 0f));
            Egg(head, new Vector3(0f, 0.19f, -0.04f), new Vector3(0.1f, 0.05f, 0.08f), Mat(C("#1a1a1a"), 0.2f, 0.7f));
            Box(head, new Vector3(-0.05f, 0.0f, -0.115f), new Vector3(0.05f, 0.012f, 0.01f), Mat(Color.black));
            Box(head, new Vector3(0.05f, 0.0f, -0.115f), new Vector3(0.05f, 0.012f, 0.01f), Mat(Color.black));
            Box(head, new Vector3(-0.05f, 0.035f, -0.112f), new Vector3(0.06f, 0.016f, 0.01f), Mat(Color.black), new Vector3(0f, 0f, 12f));
            Box(head, new Vector3(0.05f, 0.035f, -0.112f), new Vector3(0.06f, 0.016f, 0.01f), Mat(Color.black), new Vector3(0f, 0f, -12f));
            Egg(head, new Vector3(0f, -0.02f, -0.118f), new Vector3(0.04f, 0.035f, 0.03f), skin);
            Box(head, new Vector3(0f, -0.06f, -0.112f), new Vector3(0.05f, 0.01f, 0.01f), Mat(C("#a0402a")));
            Ball(head, new Vector3(-0.07f, -0.04f, -0.1f), 0.04f, Mat(C("#ffa890")));
            Ball(head, new Vector3(0.07f, -0.04f, -0.1f), 0.04f, Mat(C("#ffa890")));
            armL = Group(rikishi, "ArmL", new Vector3(-0.3f, 0.12f, 0f));
            Cap(armL, new Vector3(-0.05f, -0.14f, 0f), 0.14f, 0.36f, skin, new Vector3(0f, 0f, -15f));
            armR = Group(rikishi, "ArmR", new Vector3(0.3f, 0.12f, 0f));
            Cap(armR, new Vector3(0.05f, -0.14f, 0f), 0.14f, 0.36f, skin, new Vector3(0f, 0f, 15f));
            legL = Group(rikishi, "LegL", new Vector3(-0.18f, -0.25f, 0f));
            Cap(legL, new Vector3(0f, -0.1f, 0f), 0.18f, 0.32f, skin);
            legR = Group(rikishi, "LegR", new Vector3(0.18f, -0.25f, 0f));
            Cap(legR, new Vector3(0f, -0.1f, 0f), 0.18f, 0.32f, skin);
            Halo(Hero, new Vector3(0f, 0f, 0.6f), new Color(1f, 0.75f, 0.3f, 0.5f), 2.2f);
            dust = Fx(1f, 1.4f, -0.02f, null, false, 2.4f);
            salt = Fx(0.12f, 1.4f, 1.6f);
        }

        void Stomp(bool left, float delay, float power)
        {
            var leg = left ? legL : legR;
            float side = left ? -1f : 1f;
            Tween(0.6f, k => leg.localRotation = Quaternion.Euler(0f, 0f, side * -70f * Ease.Spike(Mathf.Min(1f, k * 1.3f))), () =>
            {
                Vector3 at = HeroPoint(new Vector3(side * 0.2f, -0.45f, 0f));
                Burst(dust, at, new Color(0.8f, 0.68f, 0.5f, 0.8f), Mathf.RoundToInt(24 * power), 3f * power, Vector3.up, 1f);
                Shock(at, C("#fff2d0"), 0.4f, 8f * power, 0.6f);
                Shake(0.16f * power, 0.6f);
                Kick(0.7f * power);
                Sfx(SpectacleSfx.Boom, power, 0.8f);
                Pulse();
            }, delay);
            Tween(0.6f, k => rikishi.localRotation = Quaternion.Euler(0f, 0f, side * 12f * Ease.Spike(Mathf.Min(1f, k * 1.3f))), null, delay);
        }

        void ThrowSalt()
        {
            Burst(salt, HeroPoint(new Vector3(0.35f, 0.15f, -0.1f)), Color.white, 80, 7f, new Vector3(0.2f, 1f, -0.3f), 0.45f);
            Tween(0.4f, k => armR.localRotation = Quaternion.Euler(0f, 0f, 120f * Ease.Spike(k)));
            Sfx(SpectacleSfx.Whoosh, 0.6f, 1.3f);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(0.5f)) Stomp(true, 0f, 1.2f);
            if (Cross(1.6f)) ThrowSalt();
            if (Cross(1.9f)) Stomp(false, 0f, 1f);
        }

        protected override void Idle(float t, float dt)
        {
            push *= Mathf.Exp(-dt * 6f);
            armL.localPosition = new Vector3(-0.3f, 0.12f, -0.15f * push);
            armR.localPosition = new Vector3(0.3f, 0.12f, -0.15f * push);
            rikishi.localPosition = new Vector3(0f, -0.02f + 0.01f * Mathf.Sin(t * 2f), 0f);
        }

        protected override void OnBeat(int n) { Stomp(true, 0f, 1f); Stomp(false, 0.7f, 1.2f); }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type != DropZoneType.Center) return;
            push = 1f;
            Shock(HeroPoint(new Vector3(0f, 0.1f, -0.3f)), Theme, 0.2f, 2.5f, 0.3f);
        }
    }

    /// <summary>N038 月：大きな月と、ぴょんと跳ねるうさぎ。ふわふわ浮く光</summary>
    public class MoonSpectacle : RecordSpectacle
    {
        public override string Key => "Moon";
        public override string Title => "MOON GRAVITY";
        public override string Catch => "重力が弱くなる";
        public override Color Theme => C("#f2e6a0");
        public override string BeatText => "MOON JUMP!";

        Transform moon, rabbit;
        Material moonMat;
        ParticleSystem floaters, stars;

        protected override void Build()
        {
            moon = Group(Hero, "Moon", new Vector3(0f, 0.05f, 0.1f));
            moonMat = Glow(C("#f8eeb8"), 0.8f);
            Ball(moon, Vector3.zero, 0.8f, moonMat);
            var crater = Mat(C("#d8cc90"), 0f, 0.2f);
            Vector3[] cr = { new Vector3(-0.15f, 0.15f, -0.33f), new Vector3(0.12f, -0.1f, -0.36f), new Vector3(0.2f, 0.2f, -0.28f), new Vector3(-0.2f, -0.18f, -0.3f), new Vector3(0.02f, 0.05f, -0.39f) };
            float[] sz = { 0.16f, 0.12f, 0.08f, 0.1f, 0.06f };
            for (int i = 0; i < cr.Length; i++) Egg(moon, cr[i], new Vector3(sz[i], sz[i], sz[i] * 0.4f), crater, Quaternion.LookRotation(-cr[i]).eulerAngles);
            Halo(moon, new Vector3(0f, 0f, 0.3f), new Color(1f, 0.95f, 0.7f, 0.8f), 1.9f);
            rabbit = Group(Hero, "Rabbit", new Vector3(0.3f, -0.4f, -0.3f), new Vector3(0f, -20f, 0f));
            var white = Mat(Color.white, 0f, 0.5f);
            Egg(rabbit, Vector3.zero, new Vector3(0.18f, 0.16f, 0.22f), white);
            Ball(rabbit, new Vector3(0f, 0.1f, -0.09f), 0.12f, white);
            Egg(rabbit, new Vector3(-0.03f, 0.22f, -0.07f), new Vector3(0.035f, 0.14f, 0.03f), white, new Vector3(0f, 0f, 10f));
            Egg(rabbit, new Vector3(0.03f, 0.22f, -0.07f), new Vector3(0.035f, 0.14f, 0.03f), white, new Vector3(0f, 0f, -10f));
            Ball(rabbit, new Vector3(-0.03f, 0.12f, -0.15f), 0.022f, Mat(C("#e8264a")));
            Ball(rabbit, new Vector3(0.03f, 0.12f, -0.15f), 0.022f, Mat(C("#e8264a")));
            Ball(rabbit, new Vector3(0f, 0.02f, 0.12f), 0.06f, white);
            floaters = Fx(0.14f, 4f, -0.03f, null, true, 1f);
            stars = Fx(0.12f, 2f, 0f);
        }

        void Hop()
        {
            Vector3 home = new Vector3(0.3f, -0.4f, -0.3f);
            Tween(1.4f, k =>
            {
                rabbit.localPosition = home + Vector3.up * Ease.Spike(k) * 0.55f + Vector3.left * Mathf.Sin(k * Mathf.PI * 2f) * 0.1f;
                rabbit.localRotation = Quaternion.Euler(0f, -20f, k * 360f);
            });
            Sfx(SpectacleSfx.Chime, 0.6f, 1.5f);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(0.3f)) Flash(new Color(0.05f, 0.08f, 0.25f), 0.5f);
            if (Cross(1.2f)) { Hop(); for (int i = 0; i < 60; i++) Burst(floaters, RandScreen(-1f, 1f, -1f, -0.3f, 1f), new Color(1f, 0.95f, 0.7f, 0.8f), 1, 0.8f, Vector3.up, 0.5f); }
        }

        protected override void Idle(float t, float dt)
        {
            moon.localRotation = Quaternion.Euler(0f, t * 6f, 0f);
            SetGlow(moonMat, C("#f8eeb8"), 0.6f + 0.25f * Mathf.Sin(t * 1.3f));
            Drip(floaters, RandScreen(-1f, 1f, -1f, -0.4f, 1f), new Color(1f, 0.95f, 0.75f, 0.7f), 6f, 0.6f, Vector3.up, 0.6f, dt);
            Drip(stars, RandScreen(-1f, 1f, 0.2f, 1f, 5f), Color.white, 4f, 0.02f, null, 1f, dt);
        }

        protected override void OnBeat(int n)
        {
            Hop();
            for (int i = 0; i < 40; i++) Burst(floaters, RandScreen(-1f, 1f, -1f, -0.5f, 1f), new Color(1f, 0.95f, 0.7f, 0.8f), 1, 1.2f, Vector3.up, 0.4f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            Burst(floaters, at, new Color(1f, 0.95f, 0.75f, 0.8f), type == DropZoneType.Center ? 8 : 3, 1f, Vector3.up, 0.5f);
        }
    }

    /// <summary>N039 地震：割れる岩盤と回るサイレン。画面ごと3回大きく揺れる</summary>
    public class EarthquakeSpectacle : RecordSpectacle
    {
        public override string Key => "Earthquake";
        public override string Title => "EARTHQUAKE";
        public override string Catch => "盤面を3回強く揺らす";
        public override Color Theme => C("#ff3a2a");
        public override float BeatInterval => 4f;
        public override float BeatSpotlight => 0f;

        Transform slab, siren;
        readonly List<Material> cracks = new List<Material>();
        Material sirenMat, beamMat;
        ParticleSystem rocks, dust;
        float danger;

        protected override void Build()
        {
            slab = Group(Hero, "Slab", new Vector3(0f, -0.1f, 0f), new Vector3(-15f, 0f, 0f));
            var rock = Mat(C("#6a5a50"), 0f, 0.2f);
            var rock2 = Mat(C("#5a4a42"), 0f, 0.2f);
            Prism(slab, new Vector3(-0.22f, 0f, 0f), new[] { new Vector2(-0.3f, -0.25f), new Vector2(0.18f, -0.28f), new Vector2(0.05f, -0.05f), new Vector2(0.2f, 0.12f), new Vector2(0.06f, 0.3f), new Vector2(-0.32f, 0.25f) }, 0.3f, rock);
            Prism(slab, new Vector3(0.24f, 0f, 0f), new[] { new Vector2(-0.04f, -0.28f), new Vector2(0.3f, -0.24f), new Vector2(0.32f, 0.28f), new Vector2(-0.12f, 0.3f), new Vector2(-0.02f, 0.12f), new Vector2(-0.15f, -0.05f) }, 0.3f, rock2);
            Vector3[] pts = { new Vector3(0f, -0.3f, -0.16f), new Vector3(0.03f, -0.12f, -0.16f), new Vector3(-0.04f, 0.02f, -0.16f), new Vector3(0.05f, 0.14f, -0.16f), new Vector3(-0.01f, 0.3f, -0.16f) };
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var m = Glow(C("#ff5a1a"), 2f);
                cracks.Add(m);
                Tube(slab, pts[i], pts[i + 1], 0.03f, m);
            }
            siren = Group(Hero, "Siren", new Vector3(0f, 0.38f, -0.05f));
            Cyl(siren, new Vector3(0f, -0.03f, 0f), 0.2f, 0.05f, Mat(C("#2a2a2a"), 0.6f, 0.6f));
            sirenMat = Glow(C("#ff1a1a"), 3f);
            Lathe(siren, Vector3.zero, new[] { new Vector2(0f, 0f), new Vector2(0.08f, 0f), new Vector2(0.08f, 0.1f), new Vector2(0.06f, 0.14f), new Vector2(0f, 0.15f) }, sirenMat);
            beamMat = Add(new Color(1f, 0.1f, 0.05f, 0.35f));
            var beam = Group(siren, "Beam", new Vector3(0f, 0.07f, 0f), new Vector3(0f, 0f, 90f));
            Cone(beam, new Vector3(0f, -1.6f, 0f), 0.35f, 1.6f, beamMat, Vector3.zero, 16);
            rocks = Fx(0.35f, 2f, 1.6f, null, false, 1f);
            dust = Fx(1.1f, 1.6f, -0.02f, null, false, 2.4f);
        }

        void Quake(float power)
        {
            danger = 1f;
            Shake(0.3f * power, 1.1f);
            Kick(power);
            Flash(C("#ff2a1a"), 0.25f * power);
            Sfx(SpectacleSfx.Boom, power, 0.5f);
            Sfx(SpectacleSfx.Crash, 0.8f * power, 0.4f);
            for (int i = 0; i < Mathf.RoundToInt(25 * power); i++)
                Burst(rocks, new Vector3(Rand(-HalfW, HalfW), HalfH + Rand(0.2f, 2f), Rand(0f, 2f)), Color.Lerp(C("#6a5a50"), C("#a89888"), Rand(0f, 1f)), 1, 2f, Vector3.down, 0.3f);
            Burst(dust, new Vector3(0f, -HalfH, 1f), new Color(0.7f, 0.62f, 0.52f, 0.7f), Mathf.RoundToInt(30 * power), 6f, Vector3.up, 1f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = RandScreen(-0.9f, 0.9f, -0.9f, 0.9f, -2f);
                Bolt(a, a + (Vector3)(Random.insideUnitCircle * 4f), new Color(1f, 1f, 1f, 0.9f), 0.08f, 0.6f, null, 8, 0.5f);
            }
        }

        protected override void OnIntro(float t)
        {
            if (Cross(0.9f)) { Quake(1.2f); Banner("震度7", Theme); }
            if (Cross(1.6f)) Quake(1.1f);
            if (Cross(2.3f)) { Quake(1.3f); SlowMo(0.4f, 0.4f); }
        }

        protected override void Idle(float t, float dt)
        {
            danger = Mathf.MoveTowards(danger, 0.3f, dt * 0.5f);
            siren.localRotation = Quaternion.Euler(0f, t * 400f, 0f);
            SetGlow(sirenMat, C("#ff1a1a"), 2f + 2f * Mathf.Abs(Mathf.Sin(t * 8f)));
            for (int i = 0; i < cracks.Count; i++) SetGlow(cracks[i], C("#ff5a1a"), (1f + 3f * danger) * (0.6f + 0.4f * Mathf.Sin(t * 10f + i)));
            float spread = 0.012f * danger;
            slab.GetChild(0).localPosition = new Vector3(-0.22f - spread, Mathf.Sin(t * 37f) * spread, 0f);
            slab.GetChild(1).localPosition = new Vector3(0.24f + spread, Mathf.Sin(t * 41f) * spread, 0f);
            HeroTilt = new Vector3(0f, 0f, danger * 2f * Mathf.Sin(t * 33f));
        }

        protected override void OnBeat(int n) => Quake(0.6f);

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Burst(dust, at, new Color(0.7f, 0.62f, 0.52f, 0.6f), 3, 1.5f, Vector3.up, 0.8f);
        }
    }

    /// <summary>N040 子守唄：三日月とゆれる星のモビール、柵を跳ぶ羊</summary>
    public class LullabySpectacle : RecordSpectacle
    {
        public override string Key => "Lullaby";
        public override string Title => "LULLABY";
        public override string Catch => "プッシャーもおやすみ";
        public override Color Theme => C("#ffb86a");
        public override float BeatInterval => 6f;
        public override float BeatSpotlight => 0f;
        public override string FinaleText => "おやすみ…";
        public override bool FinaleConfetti => false;

        Transform crescent, mobile, sheep, fence;
        readonly List<Transform> danglers = new List<Transform>();
        ParticleSystem zzz, stars;

        protected override void Build()
        {
            crescent = Group(Hero, "Crescent", new Vector3(-0.05f, 0.05f, 0f), new Vector3(0f, 0f, -20f));
            var moon = Glow(C("#ffd88a"), 1.4f);
            Crescent(crescent, Vector3.zero, 0.3f, 0.13f, 230f, moon);
            Halo(crescent, new Vector3(-0.3f, 0f, 0.1f), new Color(1f, 0.85f, 0.5f, 0.5f), 0.7f);
            Box(crescent, new Vector3(-0.3f, 0.03f, -0.1f), new Vector3(0.06f, 0.012f, 0.01f), Mat(C("#6a3a1a")), new Vector3(0f, 0f, 15f));
            Cone(crescent, new Vector3(-0.25f, 0.33f, 0f), 0.07f, 0.16f, Mat(C("#5a7ad8")), new Vector3(0f, 0f, 30f), 12);
            Ball(crescent, new Vector3(-0.32f, 0.46f, 0f), 0.04f, Mat(Color.white));
            Halo(crescent, new Vector3(0f, 0f, 0.3f), new Color(1f, 0.75f, 0.4f, 0.6f), 1.6f);
            mobile = Group(Hero, "Mobile", new Vector3(0.3f, 0.45f, -0.05f));
            Box(mobile, Vector3.zero, new Vector3(0.5f, 0.012f, 0.012f), Mat(C("#c8a06a")));
            for (int i = 0; i < 3; i++)
            {
                var d = Group(mobile, "Dangle", new Vector3(-0.2f + i * 0.2f, 0f, 0f));
                float len = 0.12f + i % 2 * 0.08f;
                Box(d, new Vector3(0f, -len * 0.5f, 0f), new Vector3(0.004f, len, 0.004f), Mat(Color.white));
                Star(d, new Vector3(0f, -len - 0.04f, 0f), 5, 0.05f, 0.022f, 0.02f, Glow(i == 1 ? C("#9ad0ff") : C("#fff2a0"), 2f));
                danglers.Add(d);
            }
            sheep = Group(Set, "Sheep", Vector3.zero, Vector3.zero, 1.6f);
            var wool = Mat(C("#f8f6f0"), 0f, 0.3f);
            for (int i = 0; i < 6; i++) Ball(sheep, new Vector3(-0.15f + (i % 3) * 0.15f, 0.05f + (i / 3) * 0.1f, 0f), 0.2f, wool);
            Egg(sheep, new Vector3(0.24f, 0.1f, 0f), new Vector3(0.14f, 0.12f, 0.12f), Mat(C("#2a2a2a")));
            for (int i = 0; i < 4; i++) Box(sheep, new Vector3(-0.12f + (i % 2) * 0.24f, -0.12f, (i < 2 ? -1 : 1) * 0.06f), new Vector3(0.04f, 0.14f, 0.04f), Mat(C("#2a2a2a")));
            sheep.gameObject.SetActive(false);
            fence = Group(Set, "Fence", new Vector3(0f, -HalfH + 0.9f, 1f), Vector3.zero, 1.4f);
            var wood = Mat(C("#c8905a"));
            Box(fence, new Vector3(-0.4f, 0f, 0f), new Vector3(0.08f, 0.7f, 0.08f), wood);
            Box(fence, new Vector3(0.4f, 0f, 0f), new Vector3(0.08f, 0.7f, 0.08f), wood);
            Box(fence, new Vector3(0f, 0.15f, 0f), new Vector3(0.9f, 0.08f, 0.05f), wood);
            Box(fence, new Vector3(0f, -0.1f, 0f), new Vector3(0.9f, 0.08f, 0.05f), wood);
            fence.gameObject.SetActive(false);
            zzz = Fx(0.22f, 2.4f, -0.05f, Hero, true, 1.4f);
            stars = Fx(0.12f, 2.5f, 0f);
        }

        void JumpSheep()
        {
            sheep.gameObject.SetActive(true);
            fence.gameObject.SetActive(true);
            float y = -HalfH + 0.9f;
            Tween(2.6f, k =>
            {
                float x = Mathf.Lerp(-HalfW - 1f, HalfW + 1f, k);
                float hop = Mathf.Max(0f, 1f - Mathf.Abs(x) / 2.2f);
                sheep.localPosition = new Vector3(x, y + Mathf.Sin(hop * Mathf.PI * 0.5f) * 1.6f + Mathf.Abs(Mathf.Sin(k * 18f)) * 0.15f, 0.5f);
                sheep.localRotation = Quaternion.Euler(0f, 0f, x < 0f ? 15f * hop : -15f * hop);
            }, () => { sheep.gameObject.SetActive(false); fence.gameObject.SetActive(false); });
            After(1.3f, () => Sfx(SpectacleSfx.Chime, 0.4f, 1.6f));
        }

        protected override void OnBegin() { sheep.gameObject.SetActive(false); fence.gameObject.SetActive(false); }

        protected override void OnIntro(float t)
        {
            if (Cross(0.4f)) Flash(new Color(0.15f, 0.12f, 0.3f), 0.45f);
            if (Cross(1.1f)) Sfx(SpectacleSfx.Bell, 0.6f, 0.7f);
            if (Cross(1.3f)) JumpSheep();
        }

        protected override void Idle(float t, float dt)
        {
            crescent.localRotation = Quaternion.Euler(0f, 0f, -20f + 6f * Mathf.Sin(t * 0.7f));
            mobile.localRotation = Quaternion.Euler(0f, t * 20f, 3f * Mathf.Sin(t));
            for (int i = 0; i < danglers.Count; i++) danglers[i].localRotation = Quaternion.Euler(0f, 0f, 10f * Mathf.Sin(t * 1.4f + i));
            if (Every(1.2f)) Burst(zzz, new Vector3(-0.2f, 0.15f, -0.1f), new Color(0.75f, 0.85f, 1f, 0.9f), 1, 0.3f, new Vector3(0.5f, 1f, 0f), 0.2f);
            Drip(stars, RandScreen(-1f, 1f, 0.1f, 1f, 5f), new Color(1f, 0.95f, 0.8f), 3f, 0.02f, null, 1f, dt);
        }

        protected override void OnBeat(int n) => JumpSheep();

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Sparkle(at, C("#ffd88a"), 5, 1.5f, Vector3.up, 0.6f);
        }

        protected override void OnFinale() { JumpSheep(); }
    }
}
