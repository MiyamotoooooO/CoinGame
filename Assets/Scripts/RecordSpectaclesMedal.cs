using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    // ====================================================================
    // メダル（N026〜N036）の大きな演出。ルールは各レコードのスクリプトが受け持つ（ここは見た目と音だけ）
    // ====================================================================

    /// <summary>N026 星：巨大な星と、流れ星の雨</summary>
    public class StarSpectacle : RecordSpectacle
    {
        public override string Key => "Star";
        public override string Title => "STARLIGHT";
        public override string Catch => "流れ星が星メダルに変える";
        public override Color Theme => C("#ffe24a");
        public override string BeatText => "METEOR SHOWER!";

        Transform star;
        Material starMat;
        readonly List<Transform> minis = new List<Transform>();
        ParticleSystem trail, twinkle;

        protected override void Build()
        {
            star = Group(Hero, "Star", Vector3.zero);
            starMat = Glow(C("#ffd83a"), 2.4f);
            Star(star, Vector3.zero, 5, 0.5f, 0.22f, 0.16f, starMat);
            Star(star, new Vector3(0f, 0f, -0.09f), 5, 0.36f, 0.16f, 0.02f, Glow(C("#fff6c0"), 3f));
            Ball(star, new Vector3(-0.08f, 0.06f, -0.1f), 0.05f, Mat(C("#2a1a0a")));
            Ball(star, new Vector3(0.08f, 0.06f, -0.1f), 0.05f, Mat(C("#2a1a0a")));
            Halo(star, new Vector3(0f, 0f, 0.2f), new Color(1f, 0.9f, 0.4f, 0.8f), 1.8f);
            for (int i = 0; i < 5; i++)
            {
                var m = Star(Hero, Vector3.zero, 5, 0.09f, 0.04f, 0.04f, Glow(C("#fff2a0"), 2.5f));
                minis.Add(m);
            }
            trail = Fx(0.25f, 0.7f, 0f);
            twinkle = Fx(0.14f, 1.5f, 0f);
        }

        void Meteor(Vector3 to, float seconds)
        {
            Vector3 from = to + new Vector3(Rand(4f, 8f), Rand(7f, 10f), 0f);
            var m = Star(Set, from, 5, 0.35f, 0.15f, 0.12f, Glow(C("#fff2a0"), 3f));
            Track(m);
            Tween(seconds, k =>
            {
                m.localPosition = Vector3.Lerp(from, to, k);
                m.localRotation = Quaternion.Euler(0f, 0f, k * 720f);
                Drip(trail, m.localPosition, Color.Lerp(C("#fff2a0"), C("#7ab8ff"), Rand(0f, 1f)), 120f, 0.4f, from - to, 0.2f, Time.deltaTime, 0.1f);
            }, () =>
            {
                Sparkle(to, C("#ffe24a"), 20, 4f);
                Shock(to, C("#fff2a0"), 0.2f, 2.5f, 0.4f);
                Kill(m);
            });
        }

        protected override void OnIntro(float t)
        {
            if (Cross(0.9f)) for (int i = 0; i < 6; i++) { float d = i * 0.12f; After(d, () => Meteor(RandScreen(-0.8f, 0.8f, -0.8f, 0.2f), 0.6f)); }
            if (Cross(1.1f)) Sfx(SpectacleSfx.Sparkle);
        }

        protected override void Idle(float t, float dt)
        {
            star.localRotation = Quaternion.Euler(0f, 25f * Mathf.Sin(t * 1.2f), 8f * Mathf.Sin(t * 0.9f));
            SetGlow(starMat, C("#ffd83a"), 2f + 0.8f * Mathf.Sin(t * 4f));
            for (int i = 0; i < minis.Count; i++)
            {
                float a = t * 1.4f + i * Mathf.PI * 2f / minis.Count;
                minis[i].localPosition = new Vector3(Mathf.Cos(a) * 0.65f, Mathf.Sin(a) * 0.5f, Mathf.Sin(a) * 0.2f);
                minis[i].localRotation = Quaternion.Euler(0f, 0f, t * 180f + i * 40f);
            }
            Drip(twinkle, RandScreen(-1f, 1f, 0.3f, 1f, 4f), Color.white, 6f, 0.05f, null, 1f, dt);
        }

        protected override void OnBeat(int n)
        {
            for (int i = 0; i < 10; i++) { float d = i * 0.12f; After(d, () => Meteor(RandScreen(-0.85f, 0.85f, -0.85f, 0f), 0.55f)); }
            Sfx(SpectacleSfx.Sparkle);
            Sfx(SpectacleSfx.Whoosh, 0.6f, 1.4f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            Meteor(RandScreen(-0.7f, 0.7f, -0.8f, -0.2f), 0.5f);
        }
    }

    /// <summary>N027 駆け引き：天使と悪魔の天秤</summary>
    public class DealSpectacle : RecordSpectacle
    {
        public override string Key => "Deal";
        public override string Title => "ANGEL OR DEVIL";
        public override string Catch => "幸福か、悪魔か";
        public override Color Theme => C("#c84aff");
        public override string BeatText => "THE DEAL";

        Transform beam, panL, panR;
        Transform angel, devil;
        float tilt, tiltTarget;
        ParticleSystem feathers, embers;

        protected override void Build()
        {
            var gold = Mat(C("#f2c23a"), 0.95f, 0.85f);
            Cyl(Hero, new Vector3(0f, -0.2f, 0f), 0.05f, 0.6f, gold);
            Cyl(Hero, new Vector3(0f, -0.5f, 0f), 0.4f, 0.04f, gold);
            Ball(Hero, new Vector3(0f, 0.12f, 0f), 0.09f, gold);
            beam = Group(Hero, "Beam", new Vector3(0f, 0.12f, 0f));
            Box(beam, Vector3.zero, new Vector3(1f, 0.035f, 0.035f), gold);
            panL = Group(beam, "PanL", new Vector3(-0.48f, 0f, 0f));
            panR = Group(beam, "PanR", new Vector3(0.48f, 0f, 0f));
            foreach (var pan in new[] { panL, panR })
            {
                Tube(pan, Vector3.zero, new Vector3(-0.1f, -0.28f, 0f), 0.008f, gold);
                Tube(pan, Vector3.zero, new Vector3(0.1f, -0.28f, 0f), 0.008f, gold);
                Lathe(pan, new Vector3(0f, -0.32f, 0f), new[] { new Vector2(0f, 0f), new Vector2(0.12f, 0.01f), new Vector2(0.16f, 0.05f), new Vector2(0f, 0.05f) }, gold);
            }
            angel = Group(panL, "Angel", new Vector3(0f, -0.2f, 0f));
            Ball(angel, Vector3.zero, 0.14f, Mat(C("#fff6e8")));
            Ring(angel, new Vector3(0f, 0.12f, 0f), 0.06f, 0.012f, Glow(C("#fff2a0"), 3f));
            Egg(angel, new Vector3(-0.1f, 0.01f, 0.03f), new Vector3(0.12f, 0.16f, 0.03f), Mat(Color.white), new Vector3(0f, 0f, 30f));
            Egg(angel, new Vector3(0.1f, 0.01f, 0.03f), new Vector3(0.12f, 0.16f, 0.03f), Mat(Color.white), new Vector3(0f, 0f, -30f));
            Halo(angel, new Vector3(0f, 0f, 0.1f), new Color(1f, 0.95f, 0.6f, 0.7f), 0.5f);
            devil = Group(panR, "Devil", new Vector3(0f, -0.2f, 0f));
            Ball(devil, Vector3.zero, 0.14f, Mat(C("#6a1a8a")));
            Cone(devil, new Vector3(-0.05f, 0.05f, 0f), 0.025f, 0.08f, Mat(C("#1a1a1a")), new Vector3(0f, 0f, 20f), 8);
            Cone(devil, new Vector3(0.05f, 0.05f, 0f), 0.025f, 0.08f, Mat(C("#1a1a1a")), new Vector3(0f, 0f, -20f), 8);
            Ball(devil, new Vector3(-0.03f, 0.01f, -0.065f), 0.025f, Glow(C("#ff2a2a"), 3f));
            Ball(devil, new Vector3(0.03f, 0.01f, -0.065f), 0.025f, Glow(C("#ff2a2a"), 3f));
            Prism(devil, new Vector3(0.12f, 0f, 0.03f), new[] { new Vector2(0f, 0f), new Vector2(0.08f, 0.1f), new Vector2(0.1f, -0.06f) }, 0.01f, Mat(C("#3a0a4a")));
            Prism(devil, new Vector3(-0.12f, 0f, 0.03f), new[] { new Vector2(0f, 0f), new Vector2(-0.1f, -0.06f), new Vector2(-0.08f, 0.1f) }, 0.01f, Mat(C("#3a0a4a")));
            Halo(devil, new Vector3(0f, 0f, 0.1f), new Color(0.8f, 0.2f, 1f, 0.6f), 0.5f);
            Halo(Hero, new Vector3(-0.5f, 0f, 0.6f), new Color(1f, 0.9f, 0.6f, 0.45f), 1.4f);
            Halo(Hero, new Vector3(0.5f, 0f, 0.6f), new Color(0.6f, 0.15f, 0.9f, 0.5f), 1.4f);
            feathers = Fx(0.2f, 3f, 0.05f, null, false, 1f);
            embers = Fx(0.16f, 1.5f, -0.2f);
        }

        void Swing()
        {
            tiltTarget = Rand(-1f, 1f) < 0f ? -1f : 1f;
            Sfx(SpectacleSfx.Bell, 0.7f, tiltTarget > 0 ? 0.6f : 1.2f);
            if (tiltTarget < 0)
                for (int i = 0; i < 40; i++) Burst(feathers, new Vector3(Rand(-HalfW, 0f), HalfH + 0.5f, 1f), Color.white, 1, 1f, Vector3.down, 0.8f);
            else
                for (int i = 0; i < 40; i++) Burst(embers, new Vector3(Rand(0f, HalfW), -HalfH - 0.3f, 1f), Color.Lerp(C("#c84aff"), C("#ff2a2a"), Rand(0f, 1f)), 1, 3f, Vector3.up, 0.5f);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.1f)) { tiltTarget = -1f; Flash(Color.white, 0.25f); }
            if (Cross(1.7f)) { tiltTarget = 1f; Flash(C("#6a1a8a"), 0.3f); Sfx(SpectacleSfx.Boom, 0.6f, 0.6f); }
            if (Cross(2.3f)) Swing();
        }

        protected override void Idle(float t, float dt)
        {
            tilt = Mathf.Lerp(tilt, tiltTarget * 18f + 3f * Mathf.Sin(t * 1.5f), 1f - Mathf.Exp(-dt * 3f));
            beam.localRotation = Quaternion.Euler(0f, 0f, -tilt);
            panL.localRotation = Quaternion.Euler(0f, 0f, tilt);
            panR.localRotation = Quaternion.Euler(0f, 0f, tilt);
            angel.localPosition = new Vector3(0f, -0.2f + 0.02f * Mathf.Sin(t * 3f), 0f);
            devil.localPosition = new Vector3(0f, -0.2f + 0.02f * Mathf.Sin(t * 3f + 1.5f), 0f);
        }

        protected override void OnBeat(int n) => Swing();

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type != DropZoneType.Center) return;
            bool good = Rand(0f, 1f) < 0.5f;
            Sparkle(at, good ? C("#fff2a0") : C("#c84aff"), 10, 3f, Vector3.up, 0.6f);
        }
    }

    /// <summary>N028 ゴールドラッシュ：金塊のトロッコとつるはし、金の噴水</summary>
    public class GoldRushSpectacle : RecordSpectacle
    {
        public override string Key => "GoldRush";
        public override string Title => "GOLD RUSH";
        public override string Catch => "5枚に1枚が金メダル";
        public override Color Theme => C("#ffc82a");
        public override string BeatText => "EUREKA!";

        Transform cart, pick;
        readonly Transform[] wheels = new Transform[2];
        ParticleSystem gold;
        float swing;

        protected override void Build()
        {
            cart = Group(Hero, "Cart", new Vector3(0f, -0.12f, 0f));
            var wood = Mat(C("#6a3a1a"), 0.1f, 0.5f);
            var iron = Mat(C("#3a3a40"), 0.8f, 0.6f);
            Prism(cart, Vector3.zero, new[] { new Vector2(-0.42f, 0.18f), new Vector2(-0.32f, -0.18f), new Vector2(0.32f, -0.18f), new Vector2(0.42f, 0.18f) }, 0.4f, wood);
            Box(cart, new Vector3(0f, 0.18f, 0f), new Vector3(0.86f, 0.03f, 0.42f), iron);
            Box(cart, new Vector3(0f, -0.05f, -0.205f), new Vector3(0.7f, 0.03f, 0.01f), iron);
            var nugget = Glow(C("#ffc82a"), 1.2f);
            var shine = Mat(C("#ffd84a"), 1f, 0.95f);
            for (int i = 0; i < 14; i++)
                Egg(cart, new Vector3(Rand(-0.32f, 0.32f), 0.2f + Rand(0f, 0.14f), Rand(-0.12f, 0.12f)), new Vector3(Rand(0.1f, 0.18f), Rand(0.08f, 0.14f), Rand(0.1f, 0.16f)), i % 3 == 0 ? nugget : shine, new Vector3(Rand(0f, 90f), Rand(0f, 90f), 0f));
            for (int i = 0; i < 2; i++)
            {
                wheels[i] = Group(cart, "Wheel", new Vector3(i == 0 ? -0.25f : 0.25f, -0.22f, -0.21f), new Vector3(90f, 0f, 0f));
                Cyl(wheels[i], Vector3.zero, 0.16f, 0.04f, iron);
                Box(wheels[i], new Vector3(0f, -0.022f, 0f), new Vector3(0.14f, 0.01f, 0.02f), Mat(C("#8a8a90"), 0.9f, 0.8f));
            }
            Box(Hero, new Vector3(0f, -0.38f, 0f), new Vector3(1.4f, 0.02f, 0.05f), iron);
            pick = Group(Hero, "Pick", new Vector3(0.48f, 0.2f, -0.15f));
            Tube(pick, Vector3.zero, new Vector3(0f, 0.45f, 0f), 0.04f, Mat(C("#a86a2a")));
            Prism(pick, new Vector3(0f, 0.45f, 0f), new[] { new Vector2(-0.24f, -0.04f), new Vector2(0f, 0.05f), new Vector2(0.24f, -0.04f), new Vector2(0f, 0.01f) }, 0.04f, Mat(C("#b8bcc4"), 0.9f, 0.8f));
            Halo(Hero, new Vector3(0f, 0.1f, 0.5f), new Color(1f, 0.8f, 0.2f, 0.6f), 2f);
            gold = Fx(0.22f, 1.6f, 1.4f);
        }

        void Geyser(int n, float speed)
        {
            Vector3 c = HeroPoint(new Vector3(0f, 0.3f, 0f));
            for (int i = 0; i < n; i++) Burst(gold, c, Color.Lerp(C("#ffc82a"), C("#fff6c0"), Rand(0f, 1f)), 1, speed, Vector3.up, 0.4f);
            Sfx(SpectacleSfx.Coin, 1f, Rand(0.9f, 1.2f));
        }

        protected override void OnIntro(float t)
        {
            float k = Mathf.Clamp01((t - 0.3f) / 0.8f);
            cart.localPosition = new Vector3(Mathf.Lerp(-2f, 0f, Ease.OutCubic(k)), -0.12f, 0f);
            if (Cross(1.15f)) { swing = 1f; Geyser(160, 14f); Flash(C("#ffe27a"), 0.4f); SlowMo(0.4f, 0.5f); }
        }

        protected override void Idle(float t, float dt)
        {
            foreach (var w in wheels) w.Rotate(0f, (InIntro ? -800f : -60f) * dt, 0f, Space.Self);
            swing *= Mathf.Exp(-dt * 4f);
            pick.localRotation = Quaternion.Euler(0f, 0f, 20f + 60f * swing * Mathf.Sin(t * 20f) + 6f * Mathf.Sin(t * 1.5f));
            if (Every(1.8f)) Geyser(6, 4f);
        }

        protected override void OnBeat(int n) { swing = 1f; Geyser(120, 12f); Kick(0.5f); }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Burst(gold, at, C("#ffd84a"), 8, 5f, Vector3.up, 0.5f);
        }
    }

    /// <summary>N029 オルゴール：ふたが開き、踊り子が回る</summary>
    public class MusicBoxSpectacle : RecordSpectacle
    {
        public override string Key => "MusicBox";
        public override string Title => "MUSIC BOX";
        public override string Catch => "ド・レ・ミの順に落とせ";
        public override Color Theme => C("#ffb0d8");
        public override string BeatText => "MELODY!";

        Transform lid, dancer, drum;
        ParticleSystem notes;

        protected override void Build()
        {
            var wood = Mat(C("#7a3a24"), 0.2f, 0.75f);
            var gold = Mat(C("#f2c23a"), 0.95f, 0.85f);
            var box = Group(Hero, "Box", new Vector3(0f, -0.25f, 0f));
            Box(box, Vector3.zero, new Vector3(0.8f, 0.3f, 0.5f), wood);
            Box(box, new Vector3(0f, 0.151f, 0f), new Vector3(0.74f, 0.01f, 0.44f), Mat(C("#c8285a"), 0f, 0.3f));
            Box(box, new Vector3(0f, -0.13f, 0f), new Vector3(0.84f, 0.04f, 0.54f), gold);
            for (int i = 0; i < 4; i++) Ball(box, new Vector3((i % 2 == 0 ? -1 : 1) * 0.37f, -0.17f, (i < 2 ? -1 : 1) * 0.22f), 0.06f, gold);
            Ring(box, new Vector3(0f, 0f, -0.255f), 0.06f, 0.012f, gold, new Vector3(90f, 0f, 0f));
            lid = Group(box, "Lid", new Vector3(0f, 0.15f, 0.25f));
            Box(lid, new Vector3(0f, 0.015f, -0.25f), new Vector3(0.8f, 0.03f, 0.5f), wood);
            Quad(lid, new Vector3(0f, 0f, -0.25f), 0.6f, 0.36f, Mat(C("#dfe6f0"), 1f, 0.98f), new Vector3(-90f, 0f, 0f));
            drum = Group(box, "Comb", new Vector3(0.2f, 0.18f, 0.05f), new Vector3(0f, 0f, 90f));
            Cyl(drum, Vector3.zero, 0.06f, 0.2f, gold);
            dancer = Group(box, "Dancer", new Vector3(-0.08f, 0.16f, 0f));
            Cyl(dancer, new Vector3(0f, 0.01f, 0f), 0.12f, 0.02f, gold);
            var skin = Mat(C("#ffe2cc"));
            var dress = Mat(C("#ff8ac0"), 0.1f, 0.6f);
            Cone(dancer, new Vector3(0f, 0.14f, 0f), 0.09f, 0.12f, dress, new Vector3(180f, 0f, 0f));
            Cap(dancer, new Vector3(0f, 0.2f, 0f), 0.05f, 0.14f, dress);
            Ball(dancer, new Vector3(0f, 0.3f, 0f), 0.06f, skin);
            Tube(dancer, new Vector3(0f, 0.24f, 0f), new Vector3(-0.07f, 0.33f, 0f), 0.015f, skin);
            Tube(dancer, new Vector3(0f, 0.24f, 0f), new Vector3(0.07f, 0.33f, 0f), 0.015f, skin);
            Tube(dancer, new Vector3(0f, 0.09f, 0f), new Vector3(0f, 0.02f, 0f), 0.015f, skin);
            Tube(dancer, new Vector3(0f, 0.1f, 0f), new Vector3(0.06f, 0.06f, 0f), 0.015f, skin);
            Halo(Hero, new Vector3(0f, 0f, 0.5f), new Color(1f, 0.7f, 0.85f, 0.55f), 2f);
            notes = Fx(0.14f, 2f, -0.08f, Hero);
        }

        protected override void OnBegin() => lid.localRotation = Quaternion.identity;

        protected override void OnIntro(float t)
        {
            float k = Mathf.Clamp01((t - 0.9f) / 0.6f);
            lid.localRotation = Quaternion.Euler(-105f * Ease.OutBack(k), 0f, 0f);
            if (Cross(1.1f)) { Sfx(SpectacleSfx.Chime, 1f, 1.2f); Burst(notes, new Vector3(0f, -0.05f, 0f), Theme, 30, 0.6f, Vector3.up, 0.6f); }
        }

        protected override void Idle(float t, float dt)
        {
            dancer.localRotation = Quaternion.Euler(0f, t * 140f, 0f);
            dancer.localPosition = new Vector3(-0.08f, 0.16f + 0.01f * Mathf.Sin(t * 4f), 0f);
            drum.Rotate(0f, 90f * dt, 0f, Space.Self);
            Color c = Color.HSVToRGB(Mathf.Repeat(t * 0.15f, 1f), 0.45f, 1f);
            Drip(notes, new Vector3(Rand(-0.2f, 0.2f), 0f, 0f), c, 3f, 0.4f, Vector3.up, 0.6f, dt);
        }

        protected override void OnBeat(int n)
        {
            for (int i = 0; i < 5; i++) { int ii = i; After(ii * 0.12f, () => { Burst(notes, new Vector3(-0.3f + ii * 0.15f, 0f, 0f), Color.HSVToRGB(ii / 5f, 0.5f, 1f), 6, 0.6f, Vector3.up, 0.4f); }); }
            Sfx(SpectacleSfx.Chime, 1f, 1.2f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Burst(notes, new Vector3(0f, 0f, 0f), Theme, 4, 0.6f, Vector3.up, 0.5f);
        }
    }

    /// <summary>N030 雪国：雪だるまと雪の家、ふぶき</summary>
    public class SnowCountrySpectacle : RecordSpectacle
    {
        public override string Key => "SnowCountry";
        public override string Title => "SNOW COUNTRY";
        public override string Catch => "氷メダルはつるっと滑る";
        public override Color Theme => C("#9ad8ff");
        public override string BeatText => "BLIZZARD!";

        Transform snowman, armL, armR;
        ParticleSystem snow;
        float blizzard;

        protected override void Build()
        {
            var white = Mat(C("#f8fbff"), 0f, 0.35f);
            Egg(Hero, new Vector3(0f, -0.45f, 0.1f), new Vector3(1.6f, 0.2f, 0.6f), white);
            var house = Group(Hero, "Kamakura", new Vector3(-0.42f, -0.35f, 0.25f));
            Lathe(house, Vector3.zero, new[] { new Vector2(0f, 0f), new Vector2(0.3f, 0f), new Vector2(0.28f, 0.12f), new Vector2(0.2f, 0.24f), new Vector2(0f, 0.3f) }, white);
            Egg(house, new Vector3(0.02f, 0.08f, -0.24f), new Vector3(0.18f, 0.18f, 0.06f), Glow(C("#ffb04a"), 1.8f));
            Halo(house, new Vector3(0f, 0.08f, -0.3f), new Color(1f, 0.6f, 0.2f, 0.6f), 0.5f);
            snowman = Group(Hero, "Snowman", new Vector3(0.12f, -0.1f, 0f));
            Ball(snowman, new Vector3(0f, -0.15f, 0f), 0.42f, white);
            Ball(snowman, new Vector3(0f, 0.18f, 0f), 0.3f, white);
            Ball(snowman, new Vector3(-0.05f, 0.22f, -0.13f), 0.035f, Mat(Color.black));
            Ball(snowman, new Vector3(0.05f, 0.22f, -0.13f), 0.035f, Mat(Color.black));
            Cone(snowman, new Vector3(0f, 0.17f, -0.13f), 0.025f, 0.12f, Mat(C("#ff7a1a")), new Vector3(-90f, 0f, 0f), 10);
            Cyl(snowman, new Vector3(0f, 0.36f, 0f), 0.22f, 0.04f, Mat(C("#c8141c")));
            Cyl(snowman, new Vector3(0f, 0.43f, 0f), 0.16f, 0.14f, Mat(C("#c8141c")), new Vector3(10f, 0f, 0f));
            Ring(snowman, new Vector3(0f, 0.04f, 0f), 0.13f, 0.035f, Mat(C("#2a6ad8")));
            Box(snowman, new Vector3(0.1f, -0.05f, -0.1f), new Vector3(0.06f, 0.2f, 0.04f), Mat(C("#2a6ad8")), new Vector3(0f, 0f, 10f));
            for (int i = 0; i < 3; i++) Ball(snowman, new Vector3(0f, -0.04f - i * 0.09f, -0.2f), 0.04f, Mat(C("#1a1a1a")));
            armL = Group(snowman, "ArmL", new Vector3(-0.18f, 0f, 0f));
            Tube(armL, Vector3.zero, new Vector3(-0.25f, 0.15f, 0f), 0.025f, Mat(C("#5a3216")));
            armR = Group(snowman, "ArmR", new Vector3(0.18f, 0f, 0f));
            Tube(armR, Vector3.zero, new Vector3(0.25f, 0.15f, 0f), 0.025f, Mat(C("#5a3216")));
            Halo(Hero, new Vector3(0f, 0f, 0.6f), new Color(0.6f, 0.85f, 1f, 0.5f), 2.2f);
            snow = Fx(0.16f, 4f, 0.08f, null, false, 1f);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(0.4f)) blizzard = 1f;
            if (Cross(1.1f)) { Flash(Color.white, 0.4f); Sfx(SpectacleSfx.Chime, 1f, 1.4f); Sfx(SpectacleSfx.Whoosh, 0.8f, 0.5f); }
        }

        protected override void Idle(float t, float dt)
        {
            blizzard = Mathf.MoveTowards(blizzard, 0.12f, dt * 0.3f);
            Drip(snow, new Vector3(Rand(-HalfW, HalfW * 1.4f), HalfH + 0.5f, Rand(-1f, 3f)), Color.white, 160f * blizzard, 1.2f + 4f * blizzard, new Vector3(-1.2f * blizzard, -1f, 0f), 0.3f, dt);
            armL.localRotation = Quaternion.Euler(0f, 0f, 15f * Mathf.Sin(t * 3f));
            armR.localRotation = Quaternion.Euler(0f, 0f, -15f * Mathf.Sin(t * 3f + 1f));
            snowman.localRotation = Quaternion.Euler(0f, 10f * Mathf.Sin(t * 0.8f), 3f * Mathf.Sin(t * 1.7f));
        }

        protected override void OnBeat(int n) { blizzard = 1f; Sfx(SpectacleSfx.Whoosh, 0.9f, 0.5f); Flash(new Color(0.85f, 0.95f, 1f), 0.25f); }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Sparkle(at, C("#cfefff"), 8, 2.5f, Vector3.up, 0.6f);
        }
    }

    /// <summary>N031 火山：大きな火山が噴火して、溶岩の玉が降る</summary>
    public class VolcanoSpectacle : RecordSpectacle
    {
        public override string Key => "Volcano";
        public override string Title => "ERUPTION";
        public override string Catch => "噴火で炎メダルが生まれる";
        public override Color Theme => C("#ff5a1a");
        public override float BeatInterval => 10f;
        public override string BeatText => "ERUPTION!!";
        public override float BeatSpotlight => 0f;   // 背が高いので、見せ場は定位置のまま噴火する

        Material lava, glowMat;
        ParticleSystem fire, ash, rocks;
        float rumble;

        protected override void Build()
        {
            Lathe(Hero, new Vector3(0f, -0.5f, 0f), new[]
            {
                new Vector2(0f, 0f), new Vector2(0.62f, 0f), new Vector2(0.55f, 0.08f), new Vector2(0.4f, 0.3f), new Vector2(0.24f, 0.62f),
                new Vector2(0.16f, 0.74f), new Vector2(0.17f, 0.76f), new Vector2(0.12f, 0.72f), new Vector2(0f, 0.68f),
            }, Mat(C("#6a3a2a"), 0.05f, 0.25f), Vector3.zero, 18);
            lava = Glow(C("#ff5a1a"), 2.5f);
            Cyl(Hero, new Vector3(0f, 0.2f, 0f), 0.26f, 0.02f, lava);
            for (int i = 0; i < 5; i++)
            {
                float a = -60f + i * 30f;
                var stream = Group(Hero, "Lava", new Vector3(0f, 0.2f, 0f), new Vector3(0f, a, 0f));
                Tube(stream, new Vector3(0f, 0.02f, -0.14f), new Vector3(0f, -0.3f, -0.33f), 0.07f, lava);
                Tube(stream, new Vector3(0f, -0.3f, -0.33f), new Vector3(0f, -0.62f, -0.55f), 0.06f, lava);
            }
            glowMat = SoftAdd(new Color(1f, 0.4f, 0.1f, 0.8f));
            Quad(Hero, new Vector3(0f, 0.32f, 0.1f), 1.4f, 1.4f, glowMat);
            fire = Fx(0.16f, 1.6f, 0.35f, Hero);
            ash = Fx(1f, 3f, -0.03f, null, false, 2.5f);
            rocks = Fx(0.25f, 2f, 1.5f);
        }

        void Erupt(float power)
        {
            Burst(fire, new Vector3(0f, 0.24f, 0f), C("#ffd23a"), Mathf.RoundToInt(120 * power), 1.1f * power, Vector3.up, 0.35f);
            Burst(fire, new Vector3(0f, 0.24f, 0f), C("#ff3a0a"), Mathf.RoundToInt(80 * power), 0.9f * power, Vector3.up, 0.5f);
            Vector3 top = HeroPoint(new Vector3(0f, 0.25f, 0f));
            Smoke(top, new Color(0.25f, 0.2f, 0.2f, 0.8f), Mathf.RoundToInt(30 * power), 3f * power, Vector3.up, 0.4f);
            for (int i = 0; i < Mathf.RoundToInt(6 * power); i++)
            {
                var bomb = Ball(Set, top, Rand(0.25f, 0.45f), Glow(C("#ff6a1a"), 3f));
                Throw(bomb, top, new Vector3(Rand(-5f, 5f), Rand(8f, 12f), Rand(-2f, 0f)), 12f, 2f, true, new Vector3(Rand(-400f, 400f), 0f, Rand(-400f, 400f)));
            }
            Flash(C("#ff6a1a"), 0.3f * power);
            Shake(0.12f * power, 1f);
            Kick(0.8f * power);
            Sfx(SpectacleSfx.Boom, power);
            Sfx(SpectacleSfx.Crash, 0.7f * power, 0.6f);
            rumble = 1f;
        }

        protected override void OnIntro(float t)
        {
            HeroOffset = Vector3.down * 1.4f * (1f - Ease.InOutCubic(Mathf.Clamp01((t - 2.5f) / 0.8f)));   // 噴火口が画面からはみ出さないよう、少し下げる
            if (t > 0.3f && t < 1.15f) Shake(0.04f, 0.2f);
            if (Cross(1.15f)) { Erupt(1.3f); SlowMo(0.3f, 0.55f); }
        }

        protected override void Idle(float t, float dt)
        {
            rumble = Mathf.MoveTowards(rumble, 0f, dt * 0.5f);
            SetGlow(lava, C("#ff5a1a"), 2f + 1.5f * Mathf.Abs(Mathf.Sin(t * 2f)) + 3f * rumble);
            SetUnlitColor(glowMat, new Color(1f, 0.4f, 0.1f, 0.5f + 0.4f * rumble));
            Drip(fire, new Vector3(0f, 0.24f, 0f), C("#ff8a2a"), 10f, 0.35f, Vector3.up, 0.4f, dt);
            Drip(ash, HeroPoint(new Vector3(0f, 0.3f, 0f)), new Color(0.3f, 0.26f, 0.25f, 0.45f), 3f, 1f, Vector3.up, 0.3f, dt);
            HeroTilt = new Vector3(0f, 10f * Mathf.Sin(t * 0.5f), rumble * 2f * Mathf.Sin(t * 40f));
        }

        protected override void OnBeat(int n) => Erupt(1f);

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Sparkle(at, C("#ff8a2a"), 10, 3.5f, Vector3.up, 0.5f);
        }

        protected override void OnFinale() { Erupt(1.5f); After(0.8f, () => Erupt(1.2f)); }
    }

    /// <summary>N032 花火：打ち上げ筒と、夜空に開く大輪の花火</summary>
    public class FireworksSpectacle : RecordSpectacle
    {
        public override string Key => "Fireworks";
        public override string Title => "HANABI";
        public override string Catch => "5枚ごとに花火玉";
        public override Color Theme => C("#ff5ab0");
        public override float BeatInterval => 0f;
        public override int BeatEveryDrops => 5;
        public override string BeatText => "たまや〜！";
        public override float BeatSpotlight => 0f;

        ParticleSystem burst, trail;
        readonly List<Transform> tubes = new List<Transform>();

        protected override void Build()
        {
            var wood = Mat(C("#8a5a2a"), 0.1f, 0.5f);
            for (int i = 0; i < 3; i++)
            {
                var t = Group(Hero, "Tube", new Vector3(-0.3f + i * 0.3f, -0.15f, 0f), new Vector3(0f, 0f, (i - 1) * -12f));
                Cyl(t, Vector3.zero, 0.2f, 0.6f, Mat(i == 1 ? C("#c8141c") : C("#2a4ad8"), 0.2f, 0.6f));
                Ring(t, new Vector3(0f, 0.2f, 0f), 0.1f, 0.012f, Mat(C("#f2c23a"), 0.9f, 0.8f));
                Ring(t, new Vector3(0f, -0.2f, 0f), 0.1f, 0.012f, Mat(C("#f2c23a"), 0.9f, 0.8f));
                Cyl(t, new Vector3(0f, 0.3f, 0f), 0.16f, 0.01f, Mat(C("#1a1a1a")));
                tubes.Add(t);
            }
            Box(Hero, new Vector3(0f, -0.48f, 0f), new Vector3(1f, 0.08f, 0.3f), wood);
            Ball(Hero, new Vector3(0.38f, 0.3f, -0.1f), 0.26f, Mat(C("#2a2a30"), 0.3f, 0.6f));
            Box(Hero, new Vector3(0.38f, 0.45f, -0.1f), new Vector3(0.02f, 0.08f, 0.02f), Mat(C("#c8a060")));
            burst = Fx(0.22f, 1.8f, 0.35f);
            trail = Fx(0.16f, 0.5f, 0f);
        }

        void Launch(Vector3 at, Color c, float size, float delay)
        {
            After(delay, () =>
            {
                Vector3 from = new Vector3(at.x + Rand(-1f, 1f), -HalfH - 0.5f, at.z);
                var shell = Ball(Set, from, 0.2f, Glow(Color.white, 2f));
                Track(shell);
                Sfx(SpectacleSfx.Rise, 0.4f, Rand(1.4f, 1.8f));
                Tween(0.75f, k =>
                {
                    shell.localPosition = Vector3.Lerp(from, at, Ease.OutCubic(k));
                    Drip(trail, shell.localPosition, new Color(1f, 0.85f, 0.5f), 60f, 0.3f, Vector3.down, 0.3f, Time.deltaTime);
                }, () =>
                {
                    Kill(shell);
                    Color c2 = Color.Lerp(c, Color.white, 0.4f);
                    Burst(burst, at, c, Mathf.RoundToInt(110 * size), 6f * size);
                    Burst(burst, at, c2, Mathf.RoundToInt(50 * size), 3.5f * size);
                    Flare(at, c, 4f * size, 0.6f);
                    Flash(c, 0.12f * size);
                    Sfx(SpectacleSfx.Boom, 0.6f * size, Rand(0.9f, 1.3f));
                    Sfx(SpectacleSfx.Sparkle, 0.4f);
                });
            });
        }

        Color RandColor() => new[] { C("#ff3a6a"), C("#ffd23a"), C("#3ae0ff"), C("#7aff5a"), C("#c86aff"), C("#ff8a2a") }[RandInt(0, 6)];

        protected override void OnIntro(float t)
        {
            if (Cross(0.9f)) for (int i = 0; i < 5; i++) Launch(RandScreen(-0.75f, 0.75f, 0.1f, 0.75f, 3f), RandColor(), Rand(0.8f, 1.3f), i * 0.25f);
        }

        protected override void Idle(float t, float dt)
        {
            for (int i = 0; i < tubes.Count; i++) tubes[i].localScale = Vector3.one * (1f + 0.03f * Mathf.Sin(t * 5f + i));
            if (!InIntro && Every(4f)) Launch(RandScreen(-0.75f, 0.75f, 0.2f, 0.75f, 4f), RandColor(), 0.7f, 0f);
        }

        protected override void OnBeat(int n)
        {
            for (int i = 0; i < 4; i++) Launch(RandScreen(-0.75f, 0.75f, 0.1f, 0.75f, 3f), RandColor(), Rand(1f, 1.5f), i * 0.2f);
            Pulse();
        }

        protected override void OnFinale()
        {
            for (int i = 0; i < 10; i++) Launch(RandScreen(-0.8f, 0.8f, 0f, 0.8f, 3f), RandColor(), Rand(1f, 1.6f), i * 0.15f);
        }
    }

    /// <summary>N033 巨大化：空から巨大なメダルが落ちてくる</summary>
    public class GiantSpectacle : RecordSpectacle
    {
        public override string Key => "Giant";
        public override string Title => "GIGANTIC!";
        public override string Catch => "でかくて重いメダル";
        public override Color Theme => C("#ffb83a");
        public override string BeatText => "GIANT STEP!";

        Transform coin;
        ParticleSystem dust, debris;

        protected override void Build()
        {
            coin = Group(Hero, "Coin", Vector3.zero, new Vector3(90f, 0f, 0f));
            var gold = Mat(C("#f2b82a"), 1f, 0.85f);
            Cyl(coin, Vector3.zero, 1f, 0.12f, gold);
            Ring(coin, new Vector3(0f, 0.061f, 0f), 0.46f, 0.03f, Mat(C("#ffd86a"), 1f, 0.9f));
            Ring(coin, new Vector3(0f, -0.061f, 0f), 0.46f, 0.03f, Mat(C("#ffd86a"), 1f, 0.9f));
            Star(coin, new Vector3(0f, -0.07f, 0f), 5, 0.26f, 0.11f, 0.02f, Mat(C("#ffe88a"), 1f, 0.9f), new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15f;
                Box(coin, Quaternion.Euler(0f, a, 0f) * new Vector3(0.5f, 0f, 0f), new Vector3(0.02f, 0.1f, 0.03f), Mat(C("#c8902a"), 1f, 0.8f), new Vector3(0f, a, 0f));
            }
            Halo(Hero, new Vector3(0f, 0f, 0.4f), new Color(1f, 0.75f, 0.2f, 0.55f), 2f);
            dust = Fx(1.1f, 1.6f, -0.02f, null, false, 2.4f);
            debris = Fx(0.2f, 1.2f, 2f);
        }

        void Impact(Vector3 at, float power)
        {
            Burst(dust, at, new Color(0.8f, 0.72f, 0.6f, 0.8f), Mathf.RoundToInt(40 * power), 4f * power, new Vector3(0f, 0.3f, 0f), 1f);
            Burst(debris, at, C("#8a6a4a"), Mathf.RoundToInt(40 * power), 8f * power, Vector3.up, 0.7f);
            Shock(at, Color.white, 0.5f, 12f * power, 0.6f);
            Shake(0.25f * power, 0.8f);
            Kick(power);
            Flash(Color.white, 0.25f * power);
            Sfx(SpectacleSfx.Boom, power, 0.7f);
            Sfx(SpectacleSfx.Crash, 0.7f * power, 0.5f);
        }

        protected override void OnIntro(float t)
        {
            if (t < 0.3f) HeroOffset = Vector3.up * 26f;
            else if (t < 1.0f)
            {
                float k = Mathf.Clamp01((t - 0.3f) / 0.7f);
                float baseK = Mathf.Clamp01((t - 0.3f) / 0.8f);
                HeroOffset = Vector3.up * (14f * (1f - Ease.InCubic(k)) + 13f * (1f - Ease.OutBack(baseK)));
            }
            else HeroOffset = Vector3.zero;
            if (Cross(1.0f)) { Impact(Director.FocusPos + Vector3.down * 3f, 1.4f); SlowMo(0.3f, 0.5f); }
        }

        protected override void Idle(float t, float dt)
        {
            coin.localRotation = Quaternion.Euler(10f * Mathf.Sin(t * 1.2f), 35f * Mathf.Sin(t * 0.9f), 0f) * Quaternion.Euler(90f, 0f, 0f);
        }

        protected override void OnBeat(int n)
        {
            Tween(0.9f, k => HeroOffset = Vector3.up * Ease.Spike(Mathf.Min(1f, k * 1.2f)) * 1.5f, () =>
            {
                HeroOffset = Vector3.zero;
                Impact(Director.FocusPos + Vector3.down * 3f, 1f);
            });
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Burst(dust, at, new Color(0.85f, 0.78f, 0.65f, 0.6f), 4, 1.5f, Vector3.up, 0.8f);
        }
    }

    /// <summary>N034 鏡：豪華な鏡の中から、もう1枚のメダルが現れる</summary>
    public class MirrorSpectacle : RecordSpectacle
    {
        public override string Key => "Mirror";
        public override string Title => "MIRROR MAGIC";
        public override string Catch => "落ちたメダルが分身する";
        public override Color Theme => C("#9ae0ff");
        public override string BeatText => "DOUBLE!";

        Transform frame, twinA, twinB;
        Material glassMat;
        ParticleSystem shards;

        protected override void Build()
        {
            frame = Group(Hero, "Frame", Vector3.zero);
            var gold = Mat(C("#e8c25a"), 0.95f, 0.85f);
            Egg(frame, new Vector3(0f, 0f, 0.02f), new Vector3(0.78f, 1.0f, 0.06f), gold);
            glassMat = Mat(C("#cfe8ff"), 1f, 0.98f);
            Egg(frame, new Vector3(0f, 0f, -0.01f), new Vector3(0.64f, 0.86f, 0.03f), glassMat);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Ball(frame, new Vector3(Mathf.Cos(a) * 0.38f, Mathf.Sin(a) * 0.48f, -0.03f), 0.07f, gold);
            }
            Star(frame, new Vector3(0f, 0.56f, -0.02f), 4, 0.1f, 0.04f, 0.04f, Glow(C("#ffffff"), 2.5f));
            twinA = Group(Hero, "TwinA", new Vector3(0f, 0f, -0.08f), new Vector3(90f, 0f, 0f));
            Cyl(twinA, Vector3.zero, 0.26f, 0.04f, Mat(C("#e8c25a"), 1f, 0.85f));
            twinB = Group(Hero, "TwinB", new Vector3(0f, 0f, -0.08f), new Vector3(90f, 0f, 0f));
            Cyl(twinB, Vector3.zero, 0.26f, 0.04f, Glow(C("#9ae0ff"), 1.5f));
            Halo(Hero, new Vector3(0f, 0f, 0.4f), new Color(0.6f, 0.9f, 1f, 0.55f), 2f);
            shards = Fx(0.16f, 1.2f, 0.6f);
        }

        void Split(float seconds)
        {
            Tween(seconds, k =>
            {
                float s = Ease.OutBack(k);
                twinA.localPosition = new Vector3(-0.22f * s, 0f, -0.1f);
                twinB.localPosition = new Vector3(0.22f * s, 0f, -0.1f);
            }, () => After(1.2f, () => Tween(0.4f, k =>
            {
                twinA.localPosition = new Vector3(-0.22f * (1f - k), 0f, -0.1f);
                twinB.localPosition = new Vector3(0.22f * (1f - k), 0f, -0.1f);
            })));
            Sparkle(HeroPoint(Vector3.zero), Theme, 30, 5f);
            Sfx(SpectacleSfx.Sparkle);
            Flash(Theme, 0.2f);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.1f)) Split(0.5f);
        }

        protected override void Idle(float t, float dt)
        {
            frame.localRotation = Quaternion.Euler(0f, 25f * Mathf.Sin(t * 0.9f), 0f);
            glassMat.color = Color.Lerp(C("#cfe8ff"), C("#ffd8f4"), 0.5f + 0.5f * Mathf.Sin(t * 1.5f));
            twinA.localRotation = Quaternion.Euler(90f, t * 120f, 0f);
            twinB.localRotation = Quaternion.Euler(90f, -t * 120f, 0f);
        }

        protected override void OnBeat(int n)
        {
            Split(0.5f);
            for (int i = 0; i < 40; i++) Burst(shards, HeroPoint(new Vector3(Rand(-0.3f, 0.3f), Rand(-0.4f, 0.4f), 0f)), Color.Lerp(Theme, Color.white, Rand(0f, 1f)), 1, 6f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type != DropZoneType.Center) return;
            Sparkle(at, Theme, 8, 3f, Vector3.up, 0.6f);
            Sparkle(new Vector3(-at.x, at.y, at.z), Theme, 8, 3f, Vector3.up, 0.6f);
        }
    }

    /// <summary>N035 工場：回る歯車とピストン、蒸気</summary>
    public class FactorySpectacle : RecordSpectacle
    {
        public override string Key => "Factory";
        public override string Title => "IRON FACTORY";
        public override string Catch => "10回押すと鉄メダル";
        public override Color Theme => C("#ff8a2a");
        public override string BeatText => "FULL POWER!";

        readonly List<Transform> gears = new List<Transform>();
        readonly List<float> gearSpeed = new List<float>();
        Transform piston;
        Material warn;
        ParticleSystem steam, sparks;
        float boost = 1f;

        protected override void Build()
        {
            var steel = Mat(C("#7a808a"), 0.9f, 0.6f);
            var copper = Mat(C("#c8783a"), 0.95f, 0.7f);
            var dark = Mat(C("#2a2c30"), 0.6f, 0.5f);
            AddGear(new Vector3(-0.2f, 0.05f, 0f), 12, 0.3f, steel, 40f);
            AddGear(new Vector3(0.22f, 0.22f, 0.02f), 8, 0.2f, copper, -60f);
            AddGear(new Vector3(0.25f, -0.18f, 0.04f), 10, 0.18f, steel, -66f);
            AddGear(new Vector3(-0.4f, -0.3f, 0.04f), 6, 0.12f, copper, 100f);
            Box(Hero, new Vector3(0f, -0.45f, 0.08f), new Vector3(1.2f, 0.12f, 0.3f), dark);
            Cyl(Hero, new Vector3(0.48f, 0.15f, 0.15f), 0.16f, 0.9f, Mat(C("#8a3a2a"), 0.2f, 0.4f));
            warn = Glow(C("#ffb02a"), 2f);
            for (int i = 0; i < 4; i++) Box(Hero, new Vector3(-0.45f + i * 0.3f, -0.45f, -0.08f), new Vector3(0.14f, 0.05f, 0.01f), i % 2 == 0 ? warn : Mat(C("#1a1a1a")), new Vector3(0f, 0f, 30f));
            piston = Group(Hero, "Piston", new Vector3(-0.48f, 0.15f, -0.05f));
            Cyl(piston, new Vector3(0f, 0.25f, 0f), 0.16f, 0.3f, dark);
            Cyl(piston, new Vector3(0f, 0.05f, 0f), 0.06f, 0.4f, Mat(C("#e0e4ea"), 1f, 0.9f));
            Box(piston, new Vector3(0f, -0.15f, 0f), new Vector3(0.2f, 0.06f, 0.2f), dark);
            Halo(Hero, new Vector3(0f, 0f, 0.5f), new Color(1f, 0.5f, 0.15f, 0.45f), 2f);
            steam = Fx(0.5f, 1.6f, -0.12f, null, false, 2.5f);
            sparks = Fx(0.1f, 0.6f, 0.4f, Hero);
        }

        void AddGear(Vector3 pos, int teeth, float r, Material m, float speed)
        {
            var g = Group(Hero, "Gear", pos);
            Gear(g, Vector3.zero, teeth, r, 0.06f, m);
            Cyl(g, new Vector3(0f, 0f, -0.035f), r * 0.35f, 0.02f, Mat(C("#2a2c30"), 0.6f, 0.5f), new Vector3(90f, 0f, 0f));
            gears.Add(g);
            gearSpeed.Add(speed);
        }

        void Blast()
        {
            Vector3 top = HeroPoint(new Vector3(0.48f, 0.62f, 0.15f));
            Burst(steam, top, new Color(0.95f, 0.95f, 0.95f, 0.85f), 40, 5f, Vector3.up, 0.3f);
            Burst(sparks, new Vector3(-0.48f, -0.1f, -0.1f), C("#ffb02a"), 30, 1f, Vector3.up, 0.8f);
            Sfx(SpectacleSfx.Whoosh, 0.9f, 0.5f);
            Sfx(SpectacleSfx.Drum, 0.8f, 0.7f);
            Shake(0.06f, 0.4f);
        }

        protected override void OnIntro(float t)
        {
            boost = t < 1.1f ? 0f : Mathf.Lerp(4f, 1f, Mathf.Clamp01((t - 1.1f) / 1.5f));
            if (Cross(1.1f)) { Blast(); Sfx(SpectacleSfx.Horn, 0.6f, 0.6f); }
        }

        protected override void Idle(float t, float dt)
        {
            if (!InIntro) boost = Mathf.MoveTowards(boost, 1f, dt * 0.8f);
            for (int i = 0; i < gears.Count; i++) gears[i].Rotate(0f, 0f, gearSpeed[i] * boost * dt, Space.Self);
            float ph = t * 3f * Mathf.Max(0.3f, boost);
            piston.GetChild(1).localPosition = new Vector3(0f, 0.05f - 0.1f * Mathf.Abs(Mathf.Sin(ph)), 0f);
            piston.GetChild(2).localPosition = new Vector3(0f, -0.15f - 0.1f * Mathf.Abs(Mathf.Sin(ph)), 0f);
            SetGlow(warn, C("#ffb02a"), 1f + 2f * Mathf.Abs(Mathf.Sin(t * 4f)));
            Drip(steam, HeroPoint(new Vector3(0.48f, 0.62f, 0.15f)), new Color(0.9f, 0.9f, 0.92f, 0.5f), 4f * boost, 1.2f, Vector3.up, 0.3f, dt);
        }

        protected override void OnBeat(int n) { boost = 4f; Blast(); }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Burst(sparks, new Vector3(-0.48f, -0.1f, -0.1f), C("#ffb02a"), 6, 0.8f, Vector3.up, 0.8f);
        }
    }

    /// <summary>N036 サイケデリック：虹色に回る輪と、とろける光</summary>
    public class PsychedelicSpectacle : RecordSpectacle
    {
        public override string Key => "Psychedelic";
        public override string Title => "PSYCHEDELIC";
        public override string Catch => "虹のメダルがすべてを混ぜる";
        public override Color Theme => C("#d84aff");
        public override string BeatText => "TRIP!!";

        readonly List<Transform> rings = new List<Transform>();
        readonly List<Material> ringMats = new List<Material>();
        readonly List<Transform> blobs = new List<Transform>();
        readonly List<Transform> screenRings = new List<Transform>();
        readonly List<Material> screenMats = new List<Material>();
        Transform eye;
        float trip;

        protected override void Build()
        {
            for (int i = 0; i < 6; i++)
            {
                var m = Glow(Color.HSVToRGB(i / 6f, 0.8f, 1f), 2f);
                ringMats.Add(m);
                rings.Add(Ring(Hero, Vector3.zero, 0.2f + i * 0.06f, 0.025f, m, new Vector3(Rand(0f, 180f), Rand(0f, 180f), 0f)));
            }
            eye = Group(Hero, "Eye", Vector3.zero);
            Ball(eye, Vector3.zero, 0.24f, Mat(Color.white, 0f, 0.9f));
            Ball(eye, new Vector3(0f, 0f, -0.09f), 0.12f, Glow(C("#5af0c8"), 1.5f));
            Ball(eye, new Vector3(0f, 0f, -0.12f), 0.06f, Mat(Color.black, 0f, 1f));
            for (int i = 0; i < 6; i++) blobs.Add(Ball(Hero, Vector3.zero, Rand(0.08f, 0.14f), Glow(Color.HSVToRGB(Rand(0f, 1f), 0.7f, 1f), 1.8f)));
            for (int i = 0; i < 5; i++)
            {
                var m = Add(new Color(1f, 1f, 1f, 0f));
                screenMats.Add(m);
                screenRings.Add(Ring(Set, new Vector3(0f, 0f, 6f), 2f + i * 1.6f, 0.2f, m, new Vector3(90f, 0f, 0f)));
            }
        }

        protected override void OnIntro(float t)
        {
            if (Cross(0.4f)) trip = 1f;
            if (Cross(1.1f)) { Sfx(SpectacleSfx.Rise, 0.8f, 0.7f); Sfx(SpectacleSfx.Sparkle); }
        }

        protected override void Idle(float t, float dt)
        {
            trip = Mathf.MoveTowards(trip, 0.15f, dt * 0.25f);
            for (int i = 0; i < rings.Count; i++)
            {
                rings[i].Rotate(new Vector3(30f + i * 12f, 50f - i * 7f, 20f) * dt * (1f + 2f * trip), Space.Self);
                SetGlow(ringMats[i], Color.HSVToRGB(Mathf.Repeat(t * 0.3f + i / 6f, 1f), 0.8f, 1f), 2f + 2f * trip);
            }
            for (int i = 0; i < blobs.Count; i++)
            {
                float a = t * (0.6f + i * 0.1f) + i;
                blobs[i].localPosition = new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a * 1.3f) * 0.45f, Mathf.Sin(a) * 0.2f);
                float s = 1f + 0.3f * Mathf.Sin(t * 3f + i);
                blobs[i].localScale = new Vector3(0.12f * s, 0.12f / s, 0.12f);
            }
            eye.localRotation = Quaternion.Euler(12f * Mathf.Sin(t * 0.8f), 25f * Mathf.Sin(t * 0.6f), 0f);
            for (int i = 0; i < screenRings.Count; i++)
            {
                float k = Mathf.Repeat(t * 0.25f + i / 5f, 1f);
                screenRings[i].localScale = Vector3.one * (0.5f + k * 6f);
                Color c = Color.HSVToRGB(Mathf.Repeat(t * 0.2f + i / 5f, 1f), 0.8f, 1f);
                SetUnlitColor(screenMats[i], new Color(c.r, c.g, c.b, 0.35f * trip * Mathf.Sin(k * Mathf.PI)));
                screenRings[i].localRotation = Quaternion.Euler(90f, 0f, t * 20f);
            }
            HeroTilt = new Vector3(0f, 0f, 8f * Mathf.Sin(t * 1.1f));
        }

        protected override void OnBeat(int n)
        {
            trip = 1f;
            Flash(Color.HSVToRGB(Rand(0f, 1f), 0.7f, 1f), 0.3f);
            Sfx(SpectacleSfx.Rise, 0.7f, Rand(0.6f, 1.2f));
            Kick(0.5f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Sparkle(at, Color.HSVToRGB(Rand(0f, 1f), 0.7f, 1f), 10, 3f, Vector3.up, 0.7f);
        }
    }
}
