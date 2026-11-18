using UnityEngine;

namespace MedalPusher
{
    // ====================================================================
    // 定番（N001〜N007）の大きな演出。ルールは各レコードのスクリプトが受け持つ（ここは見た目と音だけ）
    // ====================================================================

    /// <summary>N001 ポップス：巨大なマイクと、赤・黄・青のスポットライト、紙吹雪</summary>
    public class PopsSpectacle : RecordSpectacle
    {
        public override string Key => "Pops";
        public override string Title => "POP STAR!";
        public override string Catch => "コールの色をねらえ！";
        public override Color Theme => C("#ff4fa3");
        public override string BeatText => "CALL & RESPONSE!";

        static readonly Color[] Calls = { new Color(1f, 0.23f, 0.36f), new Color(1f, 0.82f, 0.23f), new Color(0.23f, 0.55f, 1f) };
        Transform mic, head;
        readonly Transform[] beams = new Transform[3];
        readonly Material[] beamMats = new Material[3];
        Material band;
        ParticleSystem sparks;
        float beamLevel, beamTarget;

        protected override void Build()
        {
            var chrome = Mat(C("#d9dde6"), 0.9f, 0.85f);
            mic = Group(Hero, "Mic", new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0f, -14f));
            head = Group(mic, "Head", new Vector3(0f, 0.28f, 0f));
            Ball(head, Vector3.zero, 0.36f, Mat(C("#3a3a48"), 0.7f, 0.7f));
            for (int i = -2; i <= 2; i++)
            {
                float y = i * 0.06f;
                Ring(head, new Vector3(0f, y, 0f), Mathf.Sqrt(0.0324f - y * y) + 0.006f, 0.012f, chrome);
            }
            Ring(head, Vector3.zero, 0.186f, 0.016f, chrome, new Vector3(90f, 0f, 0f));
            Ring(head, Vector3.zero, 0.186f, 0.016f, chrome, new Vector3(0f, 0f, 90f));
            Halo(head, new Vector3(0f, 0f, 0.3f), new Color(1f, 0.5f, 0.8f, 0.7f), 1.1f);
            Cyl(mic, new Vector3(0f, 0.07f, 0f), 0.22f, 0.08f, chrome);
            Lathe(mic, Vector3.zero, new[] { new Vector2(0f, -0.42f), new Vector2(0.06f, -0.42f), new Vector2(0.09f, -0.02f), new Vector2(0.1f, 0.05f), new Vector2(0f, 0.05f) }, Mat(C("#20202a"), 0.6f, 0.8f));
            band = Glow(Theme, 2.5f);
            Ring(mic, new Vector3(0f, -0.06f, 0f), 0.09f, 0.016f, band);
            Ring(mic, new Vector3(0f, -0.2f, 0f), 0.08f, 0.012f, band);
            for (int i = 0; i < 3; i++)
            {
                beamMats[i] = Add(new Color(Calls[i].r, Calls[i].g, Calls[i].b, 0f));
                beams[i] = Group(Set, "Beam" + i, new Vector3((i - 1) * HalfW * 0.6f, HalfH + 0.6f, 2f));
                Cone(beams[i], new Vector3(0f, -12f, 0f), 2.4f, 12f, beamMats[i], Vector3.zero, 24);
            }
            sparks = Fx(0.09f, 0.7f, 0.2f, Hero);
        
            // マイクスタンドと、まわりの星
            var standMat = Mat(C("#2a2a34"), 0.8f, 0.7f);
            Tube(Hero, new Vector3(0.02f, -0.38f, 0.02f), new Vector3(0.02f, -0.62f, 0.02f), 0.035f, standMat);
            Cyl(Hero, new Vector3(0.02f, -0.63f, 0.02f), 0.34f, 0.03f, standMat);
            Ring(Hero, new Vector3(0.02f, -0.615f, 0.02f), 0.17f, 0.012f, Glow(Theme, 2.5f));
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f + 0.3f;
                Star(Hero, new Vector3(Mathf.Cos(a) * 0.45f, 0.15f + Mathf.Sin(a) * 0.35f, -0.05f), 5, 0.06f, 0.025f, 0.02f, Glow(Calls[i % 3], 2.5f), new Vector3(0f, 0f, i * 20f));
            }
        }

        protected override void OnBegin() { beamLevel = 0f; beamTarget = 0f; }

        protected override void OnIntro(float t)
        {
            if (Cross(0.4f)) beamTarget = 1f;
            if (Cross(1.1f))
            {
                Confetti(120, Calls[0], Calls[1], Calls[2], Color.white);
                Sfx(SpectacleSfx.Sparkle);
            }
            if (Cross(2.9f)) beamTarget = 0f;
        }

        protected override void Idle(float t, float dt)
        {
            mic.localRotation = Quaternion.Euler(4f * Mathf.Sin(t * 2.3f), 18f * Mathf.Sin(t * 1.1f), -14f + 6f * Mathf.Sin(t * 1.7f));
            SetGlow(band, Calls[(int)(t * 2f) % 3], 2.6f);
            beamLevel = Mathf.MoveTowards(beamLevel, beamTarget, dt * 2.5f);
            for (int i = 0; i < 3; i++)
            {
                beams[i].localRotation = Quaternion.Euler(0f, 0f, (i - 1) * -12f + 28f * Mathf.Sin(t * 1.3f + i * 2.1f));
                var c = Calls[i];
                SetUnlitColor(beamMats[i], new Color(c.r, c.g, c.b, 0.28f * beamLevel * (0.8f + 0.2f * Mathf.Sin(t * 9f + i))));
            }
        }

        protected override void OnBeat(int n)
        {
            beamTarget = 1f;
            After(2.2f, () => beamTarget = 0f);
            Confetti(70, Calls[0], Calls[1], Calls[2]);
            Sfx(SpectacleSfx.Chime);
            Kick(0.4f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            Burst(sparks, new Vector3(0f, 0.3f, 0f), Calls[RandInt(0, 3)], type == DropZoneType.Center ? 14 : 5, 0.9f);
            if (type == DropZoneType.Center) Sparkle(at + Vector3.up * 0.3f, Calls[RandInt(0, 3)], 10, 3f, Vector3.up, 0.7f);
        }

        protected override void OnFinale() { beamTarget = 1f; Confetti(160, Calls[0], Calls[1], Calls[2], Color.white); }
    }

    /// <summary>N002 フォーク：大きなアコースティックギターと、夕焼け、舞う落ち葉</summary>
    public class FolkSpectacle : RecordSpectacle
    {
        public override string Key => "Folk";
        public override string Title => "FOLK ROAD";
        public override string Catch => "楽譜すごろく、一歩ずつ";
        public override Color Theme => C("#f0a040");
        public override float BeatInterval => 10f;
        public override string BeatText => "HARMONY!";

        Transform guitar;
        readonly Transform[] strings = new Transform[6];
        Material stringMat;
        ParticleSystem leaves, notes;
        float strum;

        protected override void Build()
        {
            Halo(Hero, new Vector3(0f, 0.1f, 0.6f), new Color(1f, 0.55f, 0.2f, 0.65f), 2.3f);
            guitar = Group(Hero, "Guitar", new Vector3(0f, -0.05f, 0f), new Vector3(0f, 0f, -24f), 0.62f);
            var wood = Mat(C("#c98a42"), 0.1f, 0.6f);
            var dark = Mat(C("#5a3216"), 0.1f, 0.5f);
            Egg(guitar, new Vector3(0f, -0.3f, 0f), new Vector3(0.78f, 0.74f, 0.2f), wood);
            Egg(guitar, new Vector3(0f, 0.12f, 0f), new Vector3(0.6f, 0.56f, 0.2f), wood);
            Cyl(guitar, new Vector3(0f, -0.08f, -0.1f), 0.2f, 0.02f, Mat(C("#140a04")), new Vector3(90f, 0f, 0f));
            Ring(guitar, new Vector3(0f, -0.08f, -0.105f), 0.12f, 0.015f, Glow(C("#ffd27a"), 0.8f), new Vector3(90f, 0f, 0f));
            Box(guitar, new Vector3(0f, -0.42f, -0.1f), new Vector3(0.24f, 0.05f, 0.03f), dark);
            Box(guitar, new Vector3(0f, 0.68f, -0.04f), new Vector3(0.1f, 0.8f, 0.06f), dark);
            Box(guitar, new Vector3(0f, 1.15f, -0.04f), new Vector3(0.15f, 0.2f, 0.06f), dark);
            for (int i = 0; i < 6; i++)
            {
                float y = 1.08f + (i % 3) * 0.06f;
                Cyl(guitar, new Vector3(i < 3 ? -0.1f : 0.1f, y, -0.04f), 0.035f, 0.07f, Mat(C("#e8e2d0"), 0.8f, 0.8f), new Vector3(0f, 0f, 90f));
            }
            stringMat = Glow(C("#fff2d0"), 0.6f);
            for (int i = 0; i < 6; i++)
                strings[i] = Box(guitar, new Vector3(-0.05f + i * 0.02f, 0.37f, -0.115f), new Vector3(0.006f, 1.55f, 0.006f), stringMat);
            leaves = Fx(0.28f, 4f, 0.08f, null, false, 1f);
            notes = Fx(0.12f, 1.5f, -0.1f, Hero);
        }

        void Strum()
        {
            strum = 1f;
            Shock(HeroPoint(new Vector3(0f, -0.1f, 0f)), C("#ffd27a"), 0.3f, 5f, 0.8f);
            Burst(notes, new Vector3(0f, -0.05f, -0.1f), C("#ffe7a0"), 18, 0.7f, Vector3.up, 0.8f);
            Sfx(SpectacleSfx.Chime, 0.8f, Rand(0.85f, 1.1f));
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.1f)) Strum();
            if (Cross(1.6f)) Strum();
            if (t > 0.4f && t < 3f) Drip(leaves, new Vector3(-HalfW - 1f, Rand(-2f, 4f), 0f), Leaf(), 30f, 4f, new Vector3(1f, -0.2f, 0f), 0.4f, Time.deltaTime);
        }

        Color Leaf() => Color.Lerp(C("#ff8a2a"), C("#ffd14a"), Rand(0f, 1f));

        protected override void Idle(float t, float dt)
        {
            strum *= Mathf.Exp(-dt * 3f);
            for (int i = 0; i < 6; i++)
            {
                float w = 1f + 1.6f * strum * Mathf.Abs(Mathf.Sin(t * 70f + i));
                strings[i].localScale = new Vector3(0.006f * w, 1.55f, 0.006f * w);
            }
            SetGlow(stringMat, C("#fff2d0"), 0.6f + 3f * strum);
            guitar.localRotation = Quaternion.Euler(0f, 10f * Mathf.Sin(t * 0.9f), -24f + 3f * Mathf.Sin(t * 1.6f));
            Drip(leaves, new Vector3(-HalfW - 1f, Rand(-1f, 4.5f), 0f), Leaf(), 1.5f, 3f, new Vector3(1f, -0.25f, 0f), 0.4f, dt);
        }

        protected override void OnBeat(int n)
        {
            Strum();
            After(0.25f, Strum);
            for (int i = 0; i < 40; i++) Burst(leaves, new Vector3(-HalfW - 1f, Rand(-3f, 5f), 0f), Leaf(), 1, 5f, new Vector3(1f, 0.1f, 0f), 0.5f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Burst(notes, new Vector3(0f, -0.05f, -0.1f), C("#ffe7a0"), 4, 0.6f, Vector3.up, 0.6f);
        }

        protected override void OnFinale() { Strum(); After(0.4f, Strum); After(0.8f, Strum); }
    }

    /// <summary>N003 ブルース：青いグラスと氷、たまっていくブルーノート</summary>
    public class BluesSpectacle : RecordSpectacle
    {
        public override string Key => "Blues";
        public override string Title => "BLUE NOTE";
        public override string Catch => "グラスに青い音をためて";
        public override Color Theme => C("#3f7bff");
        public override string BeatText => "MIDNIGHT BLUES";

        readonly Transform[] ice = new Transform[3];
        readonly System.Collections.Generic.List<Transform> orbit = new System.Collections.Generic.List<Transform>();
        Transform liquid;
        ParticleSystem haze;
        Material noteMat;

        protected override void Build()
        {
            Halo(Hero, new Vector3(0f, 0f, 0.6f), new Color(0.25f, 0.45f, 1f, 0.55f), 2.2f);
            var glass = Group(Hero, "Glass", new Vector3(0f, -0.08f, 0f));
            // コースター
            Cyl(glass, new Vector3(0f, -0.43f, 0f), 0.9f, 0.03f, Mat(C("#141a2e"), 0.2f, 0.5f));
            Ring(glass, new Vector3(0f, -0.414f, 0f), 0.44f, 0.01f, Glow(C("#3f7bff"), 2.2f));
            // 厚いガラスの底（光をたくさん返す）
            var thick = RecordMeshKit.Glass(new Color(0.78f, 0.9f, 1f, 0.42f), 0.98f);
            NoShadow(Lathe(glass, Vector3.zero, new[] { new Vector2(0f, -0.415f), new Vector2(0.285f, -0.415f), new Vector2(0.305f, -0.4f), new Vector2(0.31f, -0.31f), new Vector2(0f, -0.3f) }, thick, default, 48));
            // 側面（外側と内側の2枚。上に向かって少し広がるロックグラス）
            var wall = RecordMeshKit.Glass(new Color(0.82f, 0.92f, 1f, 0.14f), 0.985f);
            NoShadow(Lathe(glass, Vector3.zero, new[] { new Vector2(0.305f, -0.4f), new Vector2(0.312f, -0.3f), new Vector2(0.342f, 0.3f) }, wall, default, 56));
            NoShadow(Lathe(glass, Vector3.zero, new[] { new Vector2(0.326f, 0.3f), new Vector2(0.296f, -0.3f) }, wall, default, 56));
            // 口のふち（丸くて厚い）
            NoShadow(Ring(glass, new Vector3(0f, 0.3f, 0f), 0.334f, 0.012f, RecordMeshKit.Glass(new Color(0.92f, 0.97f, 1f, 0.55f), 0.99f)));
            Ring(glass, new Vector3(0f, 0.304f, 0f), 0.334f, 0.005f, Add(new Color(0.75f, 0.88f, 1f, 0.6f)));
            // ガラスの左右のふちが明るく見える光（輪郭）と、表面の映りこみ
            var edgeMat = Add(new Color(0.7f, 0.85f, 1f, 0.55f));
            Quad(glass, new Vector3(-0.326f, -0.05f, -0.01f), 0.016f, 0.7f, edgeMat, new Vector3(0f, 0f, -2.8f));
            Quad(glass, new Vector3(0.326f, -0.05f, -0.01f), 0.016f, 0.7f, edgeMat, new Vector3(0f, 0f, 2.8f));
            var shine = SoftAdd(new Color(1f, 1f, 1f, 0.55f));
            Quad(glass, new Vector3(-0.19f, -0.02f, -0.34f), 0.07f, 0.58f, shine, new Vector3(0f, 0f, -2f));
            Quad(glass, new Vector3(-0.12f, 0.02f, -0.34f), 0.02f, 0.5f, shine, new Vector3(0f, 0f, -2f));
            Quad(glass, new Vector3(0.22f, 0.1f, -0.33f), 0.03f, 0.3f, SoftAdd(new Color(1f, 1f, 1f, 0.3f)), new Vector3(0f, 0f, 2f));
            // 青いお酒（すけて、少し光る）と、表面
            var drink = RecordMeshKit.Glass(new Color(0.12f, 0.36f, 1f, 0.6f), 0.95f);
            drink.EnableKeyword("_EMISSION");
            drink.SetColor("_EmissionColor", new Color(0.1f, 0.3f, 1f) * 0.9f);
            liquid = NoShadow(Lathe(glass, Vector3.zero, new[] { new Vector2(0f, -0.3f), new Vector2(0.293f, -0.3f), new Vector2(0.309f, 0.06f), new Vector2(0f, 0.06f) }, drink, default, 48));
            Cyl(liquid, new Vector3(0f, 0.062f, 0f), 0.615f, 0.004f, Add(new Color(0.45f, 0.7f, 1f, 0.55f)));
            Ring(liquid, new Vector3(0f, 0.064f, 0f), 0.306f, 0.006f, Add(new Color(0.7f, 0.88f, 1f, 0.8f)));
            Halo(glass, new Vector3(0f, -0.12f, 0f), new Color(0.2f, 0.45f, 1f, 0.45f), 0.7f);
            // 氷（すける角の丸い立方体）
            var iceMat = RecordMeshKit.Glass(new Color(0.92f, 0.97f, 1f, 0.38f), 0.99f);
            for (int i = 0; i < 3; i++)
            {
                ice[i] = NoShadow(Box(glass, new Vector3(-0.12f + i * 0.12f, 0.06f, Rand(-0.06f, 0.06f)), Vector3.one * 0.12f, iceMat, new Vector3(Rand(0f, 40f), Rand(0f, 90f), Rand(0f, 40f))));
                Halo(ice[i], Vector3.zero, new Color(0.8f, 0.9f, 1f, 0.35f), 1.1f);
            }
            // グラスの外の水滴
            var drop = Mat(new Color(0.92f, 0.97f, 1f), 0.3f, 0.99f);
            for (int i = 0; i < 16; i++)
            {
                float a = Rand(200f, 340f) * Mathf.Deg2Rad;   // 手前側だけ
                float y = Rand(-0.32f, 0.22f);
                float r = Mathf.Lerp(0.312f, 0.342f, (y + 0.3f) / 0.6f) + 0.004f;
                NoShadow(Ball(glass, new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), Rand(0.012f, 0.026f), drop));
            }
            // ストロー
            Cyl(glass, new Vector3(0.1f, 0.25f, -0.05f), 0.02f, 0.6f, Mat(C("#ff4a6a"), 0.2f, 0.7f), new Vector3(0f, 0f, -18f));
            noteMat = Glow(C("#4f8bff"), 2.5f);
            haze = Fx(0.7f, 2.2f, -0.05f, null, true, 2.2f);
        }

        static Transform NoShadow(Transform t)
        {
            RecordMeshKit.NoShadow(t.gameObject);
            return t;
        }

        Transform MakeNote(Transform parent, Vector3 pos, float s)
        {
            var g = Group(parent, "BlueNote", pos, Vector3.zero, s);
            Egg(g, Vector3.zero, new Vector3(0.16f, 0.12f, 0.06f), noteMat, new Vector3(0f, 0f, 20f));
            Box(g, new Vector3(0.07f, 0.17f, 0f), new Vector3(0.025f, 0.34f, 0.025f), noteMat);
            Box(g, new Vector3(0.12f, 0.3f, 0f), new Vector3(0.1f, 0.03f, 0.025f), noteMat, new Vector3(0f, 0f, -35f));
            return g;
        }

        protected override void OnBegin()
        {
            foreach (var t in orbit) if (t != null) Object.Destroy(t.gameObject);
            orbit.Clear();
            for (int i = 0; i < 3; i++) AddOrbitNote();
        }

        void AddOrbitNote()
        {
            if (orbit.Count >= 8) return;
            var n = MakeNote(Hero, Vector3.zero, 0.8f);
            PopIn(n, 0.4f);
            orbit.Add(n);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.1f)) { Sfx(SpectacleSfx.Bell, 0.8f, 0.8f); SlowMo(0.5f, 0.6f); }
        }

        protected override void Idle(float t, float dt)
        {
            for (int i = 0; i < 3; i++) ice[i].localPosition = new Vector3(-0.12f + i * 0.12f, 0.05f + 0.015f * Mathf.Sin(t * 2f + i * 2f), ice[i].localPosition.z);
            liquid.localRotation = Quaternion.Euler(3f * Mathf.Sin(t * 1.3f), 0f, 3f * Mathf.Sin(t * 1.7f));
            for (int i = 0; i < orbit.Count; i++)
            {
                if (orbit[i] == null) continue;
                float a = t * 0.9f + i * Mathf.PI * 2f / orbit.Count;
                orbit[i].localPosition = new Vector3(Mathf.Cos(a) * 0.55f, 0.15f + 0.1f * Mathf.Sin(a * 2f), Mathf.Sin(a) * 0.3f);
                orbit[i].localRotation = Quaternion.Euler(0f, 0f, 15f * Mathf.Sin(t * 3f + i));
            }
            Drip(haze, new Vector3(Rand(-HalfW, HalfW), -HalfH - 0.5f, 1f), new Color(0.2f, 0.35f, 1f, 0.25f), 3f, 0.9f, Vector3.up, 0.5f, dt);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) return;
            // 端の穴：ブルーノートが1つ飛んでくる
            var n = MakeNote(Set, at, 1.4f);
            Track(n);
            Vector3 to = Director.ResidentPos + Vector3.up * 0.6f;
            Tween(0.7f, k => n.localPosition = Vector3.Lerp(at, to, Ease.InOutCubic(k)) + Vector3.up * Ease.Spike(k) * 2f, () =>
            {
                Kill(n);
                AddOrbitNote();
                Pulse();
                Sfx(SpectacleSfx.Bell, 0.5f, Rand(0.9f, 1.2f));
            });
        }

        protected override void OnBeat(int n)
        {
            for (int i = 0; i < 30; i++) Burst(haze, new Vector3(Rand(-HalfW, HalfW), -HalfH, 1f), new Color(0.25f, 0.4f, 1f, 0.35f), 1, 2f, Vector3.up, 0.4f);
            Sfx(SpectacleSfx.Bell, 0.9f, 0.75f);
            Flash(new Color(0.1f, 0.25f, 1f), 0.25f);
        }
    }

    /// <summary>N004 シティポップ：ネオンの夕日と、走るオープンカー</summary>
    public class CityPopSpectacle : RecordSpectacle
    {
        public override string Key => "CityPop";
        public override string Title => "CITY LIGHTS";
        public override string Catch => "真夜中のドライブへ";
        public override Color Theme => C("#ff5fd2");
        public override float BeatInterval => 10f;
        public override string BeatText => "NEON NIGHT!";

        Transform car;
        readonly Transform[] wheels = new Transform[4];
        readonly System.Collections.Generic.List<Transform> grid = new System.Collections.Generic.List<Transform>();
        Material sunMat, lightMat;
        ParticleSystem trail;

        protected override void Build()
        {
            var back = Group(Hero, "Back", new Vector3(0f, 0.05f, 0.4f));
            sunMat = Glow(C("#ff7a3a"), 1.6f);
            Wedge(back, new Vector3(0f, -0.05f, 0f), 0.62f, 0f, 180f, 0.05f, sunMat);
            var stripe = Mat(C("#2a0b3a"));
            for (int i = 0; i < 4; i++) Box(back, new Vector3(0f, -0.02f + i * 0.07f, -0.04f), new Vector3(1.3f, 0.02f + i * 0.006f, 0.02f), stripe);
            Halo(back, new Vector3(0f, 0.1f, 0.05f), new Color(1f, 0.3f, 0.7f, 0.6f), 2.2f);
            var neon = Glow(C("#29e0ff"), 2.2f);
            for (int i = 0; i < 7; i++)
            {
                float x = -0.75f + i * 0.25f, h = Rand(0.2f, 0.55f);
                Box(back, new Vector3(x, -0.05f + h * 0.5f, 0.06f), new Vector3(0.2f, h, 0.05f), Mat(C("#140a2a")));
                Box(back, new Vector3(x, -0.05f + h, 0.03f), new Vector3(0.2f, 0.012f, 0.02f), i % 2 == 0 ? neon : Glow(Theme, 2f));
            }
            var floor = Group(Hero, "Grid", new Vector3(0f, -0.3f, 0f));
            var gridMat = Glow(C("#ff3ad0"), 2f);
            for (int i = 0; i < 6; i++) grid.Add(Box(floor, new Vector3(0f, 0f, -0.3f + i * 0.12f), new Vector3(1.8f, 0.008f, 0.008f), gridMat));
            for (int i = -4; i <= 4; i++) Box(floor, new Vector3(i * 0.22f, 0f, 0f), new Vector3(0.008f, 0.008f, 0.75f), gridMat, new Vector3(0f, i * -6f, 0f));

            car = Group(Hero, "Car", new Vector3(0f, -0.15f, -0.25f), new Vector3(0f, -12f, 0f));
            var paint = Mat(C("#ff3d8b"), 0.7f, 0.85f);
            Box(car, new Vector3(0f, 0f, 0f), new Vector3(0.95f, 0.13f, 0.38f), paint);
            Box(car, new Vector3(-0.05f, 0.08f, 0f), new Vector3(0.45f, 0.06f, 0.36f), paint);
            Box(car, new Vector3(0.12f, 0.15f, 0f), new Vector3(0.03f, 0.1f, 0.34f), Clear(new Color(0.6f, 0.9f, 1f, 0.5f)), new Vector3(0f, 0f, 25f));
            Box(car, new Vector3(-0.12f, 0.11f, 0f), new Vector3(0.22f, 0.04f, 0.3f), Mat(C("#f2e6d8")));
            lightMat = Glow(C("#fff3b0"), 3f);
            Ball(car, new Vector3(0.48f, 0.01f, 0.12f), 0.07f, lightMat);
            Ball(car, new Vector3(0.48f, 0.01f, -0.12f), 0.07f, lightMat);
            Box(car, new Vector3(-0.48f, 0.02f, 0f), new Vector3(0.02f, 0.04f, 0.3f), Glow(C("#ff2a2a"), 3f));
            Box(car, new Vector3(0f, -0.06f, 0f), new Vector3(0.97f, 0.012f, 0.4f), Glow(C("#29e0ff"), 2f));
            var tire = Mat(C("#111"), 0.2f, 0.6f);
            int w = 0;
            foreach (float x in new[] { -0.3f, 0.3f })
                foreach (float z in new[] { -0.19f, 0.19f })
                {
                    wheels[w] = Group(car, "Wheel", new Vector3(x, -0.07f, z), new Vector3(90f, 0f, 0f));
                    Cyl(wheels[w], Vector3.zero, 0.2f, 0.06f, tire);
                    Box(wheels[w], new Vector3(0f, 0.032f, 0f), new Vector3(0.12f, 0.01f, 0.03f), Mat(C("#dcdcdc"), 0.9f, 0.9f));
                    w++;
                }
            Halo(car, new Vector3(0.75f, 0.01f, 0f), new Color(1f, 0.95f, 0.7f, 0.5f), 0.5f);
            trail = Fx(0.2f, 0.7f, 0f);
        
            // ヤシの木（左右）
            var trunk = Mat(C("#3a1a4a"), 0.1f, 0.4f);
            var leaf = Glow(C("#29e0ff"), 1.2f);
            foreach (float side in new[] { -1f, 1f })
            {
                var palm = Group(Hero, "Palm", new Vector3(side * 0.78f, -0.3f, 0.2f), new Vector3(0f, 0f, side * -8f));
                for (int i = 0; i < 5; i++) Cyl(palm, new Vector3(side * i * 0.012f, 0.06f + i * 0.11f, 0f), 0.06f - i * 0.006f, 0.12f, trunk, new Vector3(0f, 0f, side * -4f * i));
                for (int i = 0; i < 6; i++) Egg(palm, new Vector3(side * 0.06f, 0.62f, 0f), new Vector3(0.36f, 0.05f, 0.08f), leaf, new Vector3(0f, i * 60f, -30f + (i % 2) * 15f));
            }
        }

        protected override void OnIntro(float t)
        {
            float k = Mathf.Clamp01((t - 0.3f) / 0.9f);
            car.localPosition = new Vector3(Mathf.Lerp(-2.4f, 0f, Ease.OutCubic(k)), -0.15f, -0.25f);
            if (t > 0.3f && t < 1.2f) Drip(trail, HeroPoint(car.localPosition + new Vector3(-0.5f, 0f, 0f)), Color.Lerp(Theme, C("#29e0ff"), Rand(0f, 1f)), 80f, 0.5f, Vector3.left, 0.3f, Time.deltaTime, 0.15f);
            if (Cross(0.4f)) Sfx(SpectacleSfx.Rise, 0.6f);
        }

        protected override void Idle(float t, float dt)
        {
            foreach (var wh in wheels) wh.Rotate(0f, 720f * dt, 0f, Space.Self);
            if (!InIntro) car.localPosition = new Vector3(0.03f * Mathf.Sin(t * 1.3f), -0.15f + 0.008f * Mathf.Sin(t * 22f), -0.25f);
            for (int i = 0; i < grid.Count; i++)
            {
                float z = Mathf.Repeat(-0.3f + i * 0.12f - t * 0.4f, 0.72f) - 0.3f;
                grid[i].localPosition = new Vector3(0f, 0f, z);
            }
            SetGlow(sunMat, C("#ff7a3a"), 1.4f + 0.3f * Mathf.Sin(t * 2f));
        }

        protected override void OnBeat(int n)
        {
            SetGlow(lightMat, Color.white, 8f);
            After(0.6f, () => SetGlow(lightMat, C("#fff3b0"), 3f));
            for (int i = 0; i < 14; i++)
            {
                var streak = Box(Set, new Vector3(-HalfW - 3f, Rand(-3.5f, 4.5f), Rand(0f, 3f)), new Vector3(Rand(1.5f, 4f), 0.06f, 0.06f), Add(i % 2 == 0 ? Theme : C("#29e0ff")));
                Track(streak);
                Vector3 from = streak.localPosition, to = from + Vector3.right * (HalfW * 2f + 8f);
                Tween(Rand(0.5f, 0.9f), k => streak.localPosition = Vector3.Lerp(from, to, k), () => Kill(streak), Rand(0f, 0.4f));
            }
            Sfx(SpectacleSfx.Rise, 0.7f, 1.2f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Sparkle(at + Vector3.up * 0.2f, Rand(0f, 1f) < 0.5f ? Theme : C("#29e0ff"), 8, 3f, Vector3.up, 0.6f);
        }
    }

    /// <summary>N005 ロック：フライングVのギターとアンプの壁、炎の柱といなずま</summary>
    public class RockSpectacle : RecordSpectacle
    {
        public override string Key => "Rock";
        public override string Title => "ROCK'N'ROLL!!";
        public override string Catch => "ポイント×2で燃え上がれ";
        public override Color Theme => C("#ff3b1f");
        public override float BeatInterval => 7f;
        public override string BeatText => "POWER CHORD!!";

        Transform guitar;
        readonly Transform[] pillars = new Transform[2];
        readonly Material[] fireMats = new Material[4];
        readonly Transform[] cones = new Transform[4];
        ParticleSystem sparks, embers;
        float fire, fireTarget;

        protected override void Build()
        {
            var amp = Group(Hero, "Amps", new Vector3(0f, -0.1f, 0.35f));
            var black = Mat(C("#151515"), 0.2f, 0.4f);
            var grill = Mat(C("#2c2c2c"), 0.3f, 0.2f);
            for (int ix = -1; ix <= 1; ix += 2)
                for (int iy = 0; iy < 2; iy++)
                {
                    var cab = Group(amp, "Cab", new Vector3(ix * 0.36f, -0.25f + iy * 0.42f, 0f));
                    Box(cab, Vector3.zero, new Vector3(0.66f, 0.4f, 0.3f), black);
                    Box(cab, new Vector3(0f, 0f, -0.151f), new Vector3(0.58f, 0.32f, 0.01f), grill);
                    for (int s = 0; s < 4; s++)
                        Cyl(cab, new Vector3(-0.13f + (s % 2) * 0.26f, -0.07f + (s / 2) * 0.14f, -0.16f), 0.12f, 0.01f, Mat(C("#444"), 0.5f, 0.6f), new Vector3(90f, 0f, 0f));
                }
            Box(amp, new Vector3(0f, 0.38f, -0.12f), new Vector3(0.5f, 0.06f, 0.02f), Glow(C("#ffcc66"), 1.6f));

            guitar = Group(Hero, "FlyingV", new Vector3(0f, 0.05f, -0.15f), new Vector3(0f, 0f, 30f), 0.85f);
            var red = Mat(C("#d8141c"), 0.5f, 0.9f);
            Prism(guitar, new Vector3(0f, -0.3f, 0f), new[] { new Vector2(0f, 0.18f), new Vector2(-0.36f, -0.42f), new Vector2(-0.24f, -0.46f), new Vector2(0f, -0.06f), new Vector2(0.24f, -0.46f), new Vector2(0.36f, -0.42f) }, 0.07f, red);
            Prism(guitar, new Vector3(0f, -0.3f, -0.04f), new[] { new Vector2(0f, 0.1f), new Vector2(-0.16f, -0.18f), new Vector2(0f, -0.03f), new Vector2(0.16f, -0.18f) }, 0.01f, Mat(C("#f4f4f4")));
            Box(guitar, new Vector3(0f, 0.25f, 0f), new Vector3(0.07f, 0.85f, 0.05f), Mat(C("#2a1408")));
            Prism(guitar, new Vector3(0f, 0.72f, 0f), new[] { new Vector2(-0.06f, 0f), new Vector2(0.06f, 0f), new Vector2(0.1f, 0.18f), new Vector2(0f, 0.22f), new Vector2(-0.1f, 0.18f) }, 0.05f, red);
            for (int i = 0; i < 2; i++) Box(guitar, new Vector3(0f, -0.38f + i * 0.08f, -0.05f), new Vector3(0.16f, 0.04f, 0.02f), Mat(C("#c8c8c8"), 0.9f, 0.9f));
            Box(guitar, new Vector3(0f, 0.25f, -0.03f), new Vector3(0.03f, 1.2f, 0.005f), Glow(C("#ffeecc"), 1.2f));

            for (int i = 0; i < 2; i++)
            {
                pillars[i] = Group(Set, "Pillar", new Vector3((i == 0 ? -1f : 1f) * (HalfW - 1.1f), -HalfH - 0.5f, 1.5f));
                for (int k = 0; k < 2; k++)
                {
                    int idx = i * 2 + k;
                    fireMats[idx] = Add(k == 0 ? new Color(1f, 0.35f, 0.05f, 0.85f) : new Color(1f, 0.85f, 0.3f, 0.9f));
                    cones[idx] = Cone(pillars[i], Vector3.zero, k == 0 ? 1.1f : 0.6f, 1f, fireMats[idx], Vector3.zero, 18);
                }
            }
            sparks = Fx(0.12f, 0.6f, 0.15f, Hero);
            embers = Fx(0.16f, 1.6f, -0.15f);
        }

        void Strike(float power)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector3 target = HeroPoint(new Vector3(Rand(-0.2f, 0.2f), 0.3f, 0f));
                Bolt(new Vector3(target.x + Rand(-4f, 4f), HalfH + 1f, 0.5f), target, C("#bfe4ff"), 0.22f, 0.35f);
            }
            Shock(HeroPoint(Vector3.zero), Theme, 0.5f, 12f, 0.7f);
            Flash(Color.white, 0.35f * power);
            Shake(0.12f * power, 0.6f);
            Kick(power);
            Sfx(SpectacleSfx.Crash, power);
            Sfx(SpectacleSfx.Boom, 0.8f);
            fireTarget = 1f;
            After(2f, () => fireTarget = 0f);
        }

        protected override void OnBegin() { fire = 0f; fireTarget = 0f; }

        protected override void OnIntro(float t)
        {
            if (Cross(1.15f)) { Strike(1f); SlowMo(0.3f, 0.5f); }
        }

        protected override void Idle(float t, float dt)
        {
            fire = Mathf.MoveTowards(fire, Mathf.Max(fireTarget, 0.15f), dt * 3f);
            for (int i = 0; i < 4; i++)
            {
                float flicker = 1f + 0.15f * Mathf.Sin(t * 23f + i * 1.7f) + 0.1f * Mathf.Sin(t * 41f + i);
                float h = (i % 2 == 0 ? 6.5f : 4.5f) * fire * flicker;
                float r = (i % 2 == 0 ? 1.1f : 0.6f) * (0.6f + 0.4f * fire);
                cones[i].localScale = new Vector3(r, Mathf.Max(0.01f, h), r);
            }
            if (fire > 0.3f)
                for (int i = 0; i < 2; i++)
                    Drip(embers, pillars[i].localPosition + Vector3.up * 2f, new Color(1f, Rand(0.4f, 0.8f), 0.1f), 30f * fire, 3f, Vector3.up, 0.5f, dt, 0.6f);
            guitar.localRotation = Quaternion.Euler(0f, 15f * Mathf.Sin(t * 1.4f), 30f + 8f * Mathf.Sin(t * 4.1f));
        }

        protected override void OnBeat(int n) => Strike(0.8f);

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            Burst(sparks, new Vector3(0f, -0.2f, -0.2f), new Color(1f, Rand(0.5f, 0.9f), 0.2f), type == DropZoneType.Center ? 16 : 6, 1.1f);
            if (type == DropZoneType.Center) Sparkle(at, new Color(1f, 0.5f, 0.1f), 12, 4f, Vector3.up, 0.6f);
        }

        protected override void OnFinale() { Strike(1.2f); After(0.6f, () => Strike(1f)); }
    }

    /// <summary>N006 ジャズ：金色のサックスと、むらさきの煙、スポットライト</summary>
    public class JazzSpectacle : RecordSpectacle
    {
        public override string Key => "Jazz";
        public override string Title => "BLUE MOON JAZZ";
        public override string Catch => "落ちるたびに、倍率がスイング";
        public override Color Theme => C("#ffb52e");
        public override string BeatText => "SAX SOLO!";

        Transform sax, spot;
        Material spotMat;
        ParticleSystem notes, smoke;
        float solo;

        protected override void Build()
        {
            spotMat = Add(new Color(1f, 0.85f, 0.5f, 0.12f));
            spot = Group(Set, "Spot", Vector3.zero);
            Cone(spot, new Vector3(0f, -13f, 0f), 2.6f, 13f, spotMat, Vector3.zero, 24);
            Halo(Hero, new Vector3(0f, 0f, 0.5f), new Color(0.6f, 0.3f, 1f, 0.6f), 2.2f);

            sax = Group(Hero, "Sax", new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, -12f), 0.95f);
            var brass = Mat(C("#e8b230"), 0.95f, 0.8f);
            var pts = new[]
            {
                new Vector3(-0.18f, 0.52f, 0f), new Vector3(-0.08f, 0.44f, 0f), new Vector3(-0.04f, 0.32f, 0f), new Vector3(-0.04f, 0.1f, 0f),
                new Vector3(-0.04f, -0.15f, 0f), new Vector3(-0.02f, -0.32f, 0f), new Vector3(0.06f, -0.42f, 0f), new Vector3(0.17f, -0.42f, 0f),
                new Vector3(0.24f, -0.34f, 0f), new Vector3(0.26f, -0.2f, 0f),
            };
            for (int i = 0; i < pts.Length - 1; i++)
            {
                float d = Mathf.Lerp(0.05f, 0.15f, i / (float)(pts.Length - 1));
                Tube(sax, pts[i], pts[i + 1], d, brass);
                Ball(sax, pts[i + 1], d, brass);
            }
            Lathe(sax, new Vector3(0.26f, -0.22f, 0f), new[] { new Vector2(0.075f, 0f), new Vector2(0.09f, 0.08f), new Vector2(0.13f, 0.15f), new Vector2(0.2f, 0.19f), new Vector2(0.19f, 0.2f), new Vector2(0.06f, 0.08f), new Vector2(0.06f, 0f) }, brass);
            Cyl(sax, new Vector3(-0.2f, 0.53f, 0f), 0.04f, 0.08f, Mat(C("#111")), new Vector3(0f, 0f, 60f));
            var key = Mat(C("#fff0c0"), 0.9f, 0.9f);
            for (int i = 0; i < 6; i++) Cyl(sax, new Vector3(-0.1f, 0.25f - i * 0.08f, -0.03f), 0.05f, 0.02f, key, new Vector3(90f, 0f, 0f));
            notes = Fx(0.16f, 1.8f, -0.05f, Hero);
            smoke = Fx(1.2f, 3f, -0.03f, null, false, 2f);
        }

        protected override void OnIntro(float t)
        {
            if (Cross(1.1f)) { Sfx(SpectacleSfx.Chime, 0.9f, 0.8f); solo = 1f; }
        }

        protected override void Idle(float t, float dt)
        {
            solo = Mathf.MoveTowards(solo, 0f, dt * 0.4f);
            spot.localPosition = new Vector3(Hero.localPosition.x, HalfH + 2f, 2f);
            SetUnlitColor(spotMat, new Color(1f, 0.85f, 0.5f, 0.08f + 0.14f * solo));
            sax.localRotation = Quaternion.Euler(0f, 20f * Mathf.Sin(t * 0.8f), -12f + 7f * Mathf.Sin(t * 2.6f));
            Drip(notes, new Vector3(0.3f, -0.05f, 0f), Color.Lerp(Theme, Color.white, Rand(0f, 0.5f)), 3f + 30f * solo, 0.6f, new Vector3(0.4f, 1f, 0f), 0.5f, dt);
            Drip(smoke, new Vector3(Rand(-HalfW, HalfW), -HalfH - 0.5f, 2f), new Color(0.45f, 0.2f, 0.75f, 0.18f), 2f, 0.6f, Vector3.up, 0.6f, dt);
        }

        protected override void OnBeat(int n)
        {
            solo = 1f;
            Sfx(SpectacleSfx.Chime, 0.9f, Rand(0.7f, 1f));
            Shock(HeroPoint(new Vector3(0.3f, 0f, 0f)), Theme, 0.4f, 8f, 1f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Burst(notes, new Vector3(0.3f, -0.05f, 0f), Theme, 5, 0.7f, new Vector3(0.4f, 1f, 0f), 0.4f);
        }
    }

    /// <summary>N007 テクノ：光るLEDのキューブとレーザー、ストロボ</summary>
    public class TechnoSpectacle : RecordSpectacle
    {
        public override string Key => "Techno";
        public override string Title => "TECHNO OVERDRIVE";
        public override string Catch => "プッシャー×2のビート";
        public override Color Theme => C("#29f0ff");
        public override float BeatInterval => 8f;
        public override string BeatText => "DROP THE BASS!";

        Transform cube;
        readonly Material[] leds = new Material[25];
        readonly Transform[] lasers = new Transform[6];
        readonly Material[] laserMats = new Material[6];
        float fan;

        protected override void Build()
        {
            cube = Group(Hero, "Cube", Vector3.zero, new Vector3(-12f, 0f, 0f), 0.8f);
            Box(cube, Vector3.zero, Vector3.one * 0.9f, Mat(C("#0b0d14"), 0.6f, 0.8f));
            var edge = Glow(Theme, 2.5f);
            foreach (var a in new[] { -0.45f, 0.45f })
                foreach (var b in new[] { -0.45f, 0.45f })
                {
                    Box(cube, new Vector3(a, b, 0f), new Vector3(0.03f, 0.03f, 0.93f), edge);
                    Box(cube, new Vector3(a, 0f, b), new Vector3(0.03f, 0.93f, 0.03f), edge);
                    Box(cube, new Vector3(0f, a, b), new Vector3(0.93f, 0.03f, 0.03f), edge);
                }
            for (int i = 0; i < 25; i++)
            {
                leds[i] = Glow(Theme, 2f);
                Box(cube, new Vector3(-0.32f + (i % 5) * 0.16f, -0.32f + (i / 5) * 0.16f, -0.46f), new Vector3(0.12f, 0.12f, 0.02f), leds[i]);
            }
            Halo(Hero, new Vector3(0f, 0f, 0.6f), new Color(0.1f, 0.9f, 1f, 0.5f), 2f);
            for (int i = 0; i < 6; i++)
            {
                laserMats[i] = Add(new Color(0.2f, 1f, 0.9f, 0f));
                lasers[i] = Group(Set, "Laser", new Vector3((i < 3 ? -1f : 1f) * (HalfW - 0.5f), -HalfH - 0.3f, 2f));
                Box(lasers[i], new Vector3(0f, 8f, 0f), new Vector3(0.06f, 16f, 0.06f), laserMats[i]);
            }
        
            // 左右のスピーカーと、上のアンテナの光
            foreach (float side in new[] { -1f, 1f })
            {
                var spk = Group(Hero, "Speaker", new Vector3(side * 0.62f, -0.15f, 0.05f));
                Box(spk, Vector3.zero, new Vector3(0.26f, 0.5f, 0.26f), Mat(C("#12141c"), 0.5f, 0.7f));
                Cyl(spk, new Vector3(0f, 0.1f, -0.135f), 0.18f, 0.02f, Mat(C("#2a2e3a"), 0.6f, 0.6f), new Vector3(90f, 0f, 0f));
                Cyl(spk, new Vector3(0f, -0.12f, -0.135f), 0.12f, 0.02f, Mat(C("#2a2e3a"), 0.6f, 0.6f), new Vector3(90f, 0f, 0f));
                Ring(spk, new Vector3(0f, 0.1f, -0.14f), 0.09f, 0.01f, Glow(Theme, 2.5f), new Vector3(90f, 0f, 0f));
            }
            for (int i = 0; i < 3; i++)
            {
                Tube(Hero, new Vector3(-0.15f + i * 0.15f, 0.36f, 0f), new Vector3(-0.15f + i * 0.15f, 0.5f + (i % 2) * 0.06f, 0f), 0.012f, Mat(C("#c8ccd6"), 0.9f, 0.8f));
                Ball(Hero, new Vector3(-0.15f + i * 0.15f, 0.51f + (i % 2) * 0.06f, 0f), 0.04f, Glow(i % 2 == 0 ? Theme : C("#ff3ad8"), 3f));
            }
        }

        protected override void OnBegin() => fan = 0f;

        protected override void OnIntro(float t)
        {
            if (t > 0.4f && t < 3f) fan = Mathf.Max(fan, 0.7f);
            if (Cross(1.1f)) { Sfx(SpectacleSfx.Laser); Flash(Theme, 0.3f); }
        }

        protected override void Idle(float t, float dt)
        {
            int beat = Mathf.FloorToInt(t * 2.13f);
            float inBeat = Mathf.Repeat(t * 2.13f, 1f);
            for (int i = 0; i < 25; i++)
            {
                int x = i % 5, y = i / 5;
                int pattern = (beat / 4) % 3;
                bool on = pattern == 0 ? ((x + y + beat) % 2 == 0) : pattern == 1 ? (Mathf.Abs(x - 2) + Mathf.Abs(y - 2) == beat % 3) : (y == beat % 5 || x == beat % 5);
                Color c = (beat % 2 == 0) ? Theme : C("#ff3ad8");
                SetGlow(leds[i], on ? c : c * 0.1f, on ? 3f * (1f - inBeat * 0.6f) : 0.2f);
            }
            cube.localRotation = Quaternion.Euler(-12f + 6f * Mathf.Sin(t), 28f * Mathf.Sin(t * 0.8f), 0f);
            cube.localScale = Vector3.one * 0.8f * (1f + 0.06f * (1f - inBeat));
            fan = Mathf.MoveTowards(fan, 0f, dt * 0.35f);
            for (int i = 0; i < 6; i++)
            {
                float side = i < 3 ? 1f : -1f;
                float a = side * (15f + (i % 3) * 14f + 30f * fan * Mathf.Sin(t * 2.4f + i));
                lasers[i].localRotation = Quaternion.Euler(0f, 0f, a);
                Color c = i % 2 == 0 ? Theme : C("#ff3ad8");
                SetUnlitColor(laserMats[i], new Color(c.r, c.g, c.b, (0.1f + 0.7f * fan) * (0.6f + 0.4f * (1f - inBeat))));
            }
        }

        protected override void OnBeat(int n)
        {
            fan = 1f;
            for (int i = 0; i < 4; i++) After(i * 0.235f, () => { Flash(Color.white, 0.18f); Kick(0.35f); Sfx(SpectacleSfx.Drum, 0.8f); Shock(HeroPoint(Vector3.zero), Theme, 0.5f, 9f, 0.5f); });
            Sfx(SpectacleSfx.Laser, 0.8f);
        }

        protected override void OnDrop(DropZoneType type, Vector3 at)
        {
            if (type == DropZoneType.Center) Sparkle(at, Rand(0f, 1f) < 0.5f ? Theme : C("#ff3ad8"), 10, 4f, Vector3.up, 0.5f);
        }
    }
}
