using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// エリア4（テンポ・ブレイカー）の始まりに流すムービー（約21秒・区画紹介）。
    ///
    /// 【使い方】
    ///   MainScene の Hierarchy で右クリック → Create Empty → 名前を「Area4IntroMovie」にして、このスクリプトを付ける。
    ///   Inspector のチェックボックスで、ボスのせりふを1つ選ぶ（2つ以上は選べない）。
    ///
    /// 【流れ】
    ///   灰色の街（リズムのランプが、ばらばらに点いて止まっていく）→ メトロノームの形の塔の上のボス（振り子がずれて揺れる）
    ///   → メトロノームの振り子がゆっくりになって止まり「TEMPO : STOP」→ AREA 4
    ///
    /// 【流れるタイミング】
    ///   エリア4が始まった時（エリア3から進んだ時と、続きからエリア4で始めた時）
    /// </summary>
    public class Area4IntroMovie : AreaMovieBase
    {
        [Header("■ ボスのせりふ（1つだけチェックできる。新しくチェックした方が残る）")]
        [Tooltip("a：「リズムは、乱れるものだ。……カチ、カチ、カ。」")]
        public bool lineA = true;
        [Tooltip("b：「テンポ、ゼロ。この街は、もう踊らない。」")]
        public bool lineB = false;
        [Tooltip("c：「拍を数えるな。どうせ、ずれる。」")]
        public bool lineC = false;
        [SerializeField, HideInInspector] private int lastLine = 0;

        [Header("■ 音（空のままでも動く。自動で読みこむ）")]
        public AreaMovieDirector.MovieSound sound = new AreaMovieDirector.MovieSound(new[]
        {
            ("ずれた「カチ、カチ」", 0f), ("ランプの「ジジッ」", 3.5f), ("塔の鐘の「ゴーン」", 6.2f),
            ("振り子がゆっくりになる", 12.5f), ("止まる「カチッ」", 14.3f), ("タイトルの「ドン」", 17.15f),
        });

        protected override int DefaultAreaIndex => 3;
        protected override string DefaultResourceFolder => "AreaMovies/Area4";
        protected override string DefaultAudioFolder => "AreaMovies/Area4/Audio/Intro";
        protected override AreaMovieDirector.MovieSound CurrentSound => sound;

        private const int LampCount = 8;
        private string lineFile = "Intro/line_a";

        private void OnValidate()
        {
            int pick = KeepOne(new[] { lineA, lineB, lineC }, lastLine);
            lineA = pick == 0; lineB = pick == 1; lineC = pick == 2;
            lastLine = pick;
        }

        /// <summary>エリア4が始まった時（演出中になった時）</summary>
        protected override bool ShouldPlay(GameState state, GameState previous)
        {
            bool areaStart = state == GameState.Intro || (state == GameState.Warmup && previous != GameState.Intro);
            return areaStart && game.AreaIndex == Area;
        }

        protected override bool PrepareSelection()
        {
            lineFile = lineB ? "Intro/line_b" : lineC ? "Intro/line_c" : "Intro/line_a";
            return true;
        }

        protected override void BuildMovie()
        {
            Cut street = AddCut("street", 0f, 6f, "Intro/sky", "Intro/city_far", "Intro/city_near", "Intro/street", "Intro/lamps", "Intro/label");
            Texture2D lamp = Load("Common/lamp_on");
            for (int i = 0; i < LampCount; i++) AddExtra(street, "lamp" + i, lamp);
            AddCut("tower", 6f, 11f, "Intro/tower_bg", "Intro/tower_pendulum", "Intro/boss_small");
            AddCut("close", 11f, 17f, "Intro/close_bg", "Intro/boss_big", "Intro/metronome", "Intro/pendulum", "Intro/stop_on", "Common/boss_box", lineFile);
            AddCut("title", 17f, 21f, "Common/title_bg", "Intro/title_text");
            AddDip(6f);
            AddDip(11f);
            AddHit(11f + 3.3f, 0.3f, 0.8f);   // 振り子が止まった
            AddHit(17.15f, 0.9f, 1f);         // タイトル
        }

        protected override void Animate(Cut c, float lt, float u)
        {
            switch (c.name)
            {
                case "street":
                {
                    float near = -(u - 0.5f) * 90f;
                    Move(L(c, "Intro/sky"), new Vector2(-(u - 0.5f) * 20f, 0f));
                    Move(L(c, "Intro/city_far"), new Vector2(-(u - 0.5f) * 50f, 0f));
                    Move(L(c, "Intro/city_near"), new Vector2(near, 0f));
                    Move(L(c, "Intro/lamps"), new Vector2(near, 0f));
                    Move(L(c, "Intro/street"), new Vector2(-(u - 0.5f) * 120f, 0f));
                    // リズムのランプ：ばらばらのタイミングで点いて、だんだん点かなくなる
                    for (int i = 0; i < LampCount && i < c.extras.Count; i++)
                    {
                        bool lit = Mathf.Repeat(lt * (1.7f + 0.37f * i) + i * 0.31f, 1f) < 0.25f;
                        float life = 1f - Smooth((lt - (2.0f + i * 0.25f)) / 0.6f);
                        PlaceLamp(c.extras[i], i, near, lit ? life : 0f);
                    }
                    Zoom(c, 1.08f, Vector2.zero);
                    Layer label = L(c, "Intro/label");
                    float a = EaseOut((lt - 0.6f) / 0.5f);
                    Move(label, new Vector2((1f - a) * -60f, 0f));
                    SetAlpha(label, a);
                    break;
                }

                case "tower":
                {
                    // 塔の振り子：いつもリズムがずれた揺れ方をする
                    float angle = 18f * Mathf.Sin(2f * Mathf.PI * 0.55f * lt) + 6f * Mathf.Sin(lt * 1.7f);
                    Pose(L(c, "Intro/tower_pendulum"), Vector2.zero, angle, 1f, new Vector2(290f, -220f));
                    Zoom(c, Mathf.Lerp(1f, 1.15f, Smooth(u)), new Vector2(290f, 140f));
                    break;
                }

                case "close":
                {
                    Pose(L(c, "Intro/boss_big"), Vector2.zero, 0f, Mathf.Lerp(1.05f, 1f, EaseOut(lt / 1.5f)), new Vector2(340f, -500f));
                    float bodyA = Smooth((lt - 0.2f) / 0.4f);
                    SetAlpha(L(c, "Intro/metronome"), bodyA);
                    // 振り子：ゆっくりになって、まん中で止まる
                    float amp = lt < 1.5f ? 28f : Mathf.Lerp(28f, 0f, Smooth((lt - 1.5f) / 1.8f));
                    float slow = Mathf.Max(0f, lt - 1.5f);
                    float angle = amp * Mathf.Sin(2f * Mathf.PI * (1.2f * lt - 0.15f * slow * slow));
                    Layer p = L(c, "Intro/pendulum");
                    Pose(p, Vector2.zero, angle, 1f, new Vector2(-480f, -160f));
                    SetAlpha(p, bodyA);
                    SetAlpha(L(c, "Intro/stop_on"), lt < 3.3f ? 0f : (Mathf.Repeat(lt * 2f, 1f) < 0.65f ? 1f : 0.3f));
                    CaptionIn(L(c, "Common/boss_box"), L(c, lineFile), lt, 3.0f, 3.3f, 1.2f);
                    break;
                }

                case "title":
                    TitleIn(L(c, "Common/title_bg"), L(c, "Intro/title_text"), lt);
                    break;
            }
        }

        /// <summary>i 番目のランプの光を置く（素材と同じ位置：x = 260 + i×200、y = 740）</summary>
        private static void PlaceLamp(Layer l, int i, float offsetX, float alpha)
        {
            float xImg = 260f + i * 200f;
            l.rt.pivot = new Vector2(0.5f, 0.5f);
            l.rt.localScale = Vector3.one;
            l.rt.sizeDelta = new Vector2(120f, 120f);
            l.rt.anchoredPosition = new Vector2(xImg - W * 0.5f + offsetX, H * 0.5f - 740f);
            l.img.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }
    }
}
