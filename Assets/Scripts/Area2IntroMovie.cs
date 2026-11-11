using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// エリア2（ノイズ・キャンセラー）の始まりに流すムービー（約21秒・区画紹介）。
    ///
    /// 【使い方】
    ///   MainScene の Hierarchy で右クリック → Create Empty → 名前を「Area2IntroMovie」にして、このスクリプトを付ける。
    ///   Inspector のチェックボックスで、ボスのせりふを1つ選ぶ（2つ以上は選べない）。
    ///
    /// 【流れ】
    ///   灰色の街（大きな看板に砂嵐）→ 電波塔の上で電波を出すボス → 音の波が平らになって「NOISE CANCEL : ON」→ AREA 2
    ///
    /// 【流れるタイミング】
    ///   エリア2が始まった時（エリア1から進んだ時と、続きからエリア2で始めた時）
    /// </summary>
    public class Area2IntroMovie : AreaMovieBase
    {
        [Header("■ ボスのせりふ（1つだけチェックできる。新しくチェックした方が残る）")]
        [Tooltip("a：「……うるさい。全部、消してやる。」")]
        public bool lineA = true;
        [Tooltip("b：「聞こえるか？　……いや、もう何も聞こえない。」")]
        public bool lineB = false;
        [Tooltip("c：「雑音は、ゼロにする。それがオレの仕事だ。」")]
        public bool lineC = false;
        [SerializeField, HideInInspector] private int lastLine = 0;

        [Header("■ 音（空のままでも動く。自動で読みこむ）")]
        public AreaMovieDirector.MovieSound sound = new AreaMovieDirector.MovieSound(new[]
        {
            ("砂嵐のザー", 0f), ("看板の「ジジッ」", 3.5f), ("電波の「ピーー」", 6.2f),
            ("音の波がしぼむ", 12.5f), ("無音の「ピー」", 14.3f), ("タイトルの「ドン」", 17.15f),
        });

        protected override int DefaultAreaIndex => 1;
        protected override string DefaultResourceFolder => "AreaMovies/Area2";
        protected override string DefaultAudioFolder => "AreaMovies/Area2/Audio/Intro";
        protected override AreaMovieDirector.MovieSound CurrentSound => sound;

        // 看板の画面（素材を作った時と同じ位置。左上x, 左上y, 右下x, 右下y）
        private static readonly Rect[] Screens =
        {
            Rect.MinMaxRect(230, 300, 610, 540), Rect.MinMaxRect(1260, 260, 1710, 540), Rect.MinMaxRect(800, 380, 1080, 560),
        };
        private const int BarCount = 48;
        private string lineFile = "Intro/line_a";

        private void OnValidate()
        {
            int pick = KeepOne(new[] { lineA, lineB, lineC }, lastLine);
            lineA = pick == 0; lineB = pick == 1; lineC = pick == 2;
            lastLine = pick;
        }

        /// <summary>エリア2が始まった時（演出中になった時）</summary>
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
            Cut street = AddCut("street", 0f, 6f, "Intro/sky", "Intro/city_far", "Intro/city_near", "Intro/street", "Intro/billboards", "Intro/label");
            Texture2D noise = Load("Common/noise");
            for (int i = 0; i < Screens.Length; i++) AddExtra(street, "noise" + i, noise);
            AddCut("tower", 6f, 11f, "Intro/tower_bg", "Intro/waves", "Intro/boss_small");
            Cut close = AddCut("close", 11f, 17f, "Intro/close_bg", "Intro/boss_big", "Intro/scope", "Intro/cancel_on", "Common/boss_box", lineFile);
            for (int i = 0; i < BarCount; i++) AddExtra(close, "bar" + i, null);
            AddCut("title", 17f, 21f, "Common/title_bg", "Intro/title_text");
            AddDip(6f);
            AddDip(11f);
            AddHit(11f + 3.3f, 0.3f, 0.8f);   // 音の波が平らになった
            AddHit(17.15f, 0.9f, 1f);         // タイトル
        }

        protected override void Animate(Cut c, float lt, float u)
        {
            switch (c.name)
            {
                case "street":
                {
                    // 灰色の街を、奥ほどゆっくり、手前ほど速く横に流す
                    float near = -(u - 0.5f) * 90f;
                    Move(L(c, "Intro/sky"), new Vector2(-(u - 0.5f) * 20f, 0f));
                    Move(L(c, "Intro/city_far"), new Vector2(-(u - 0.5f) * 50f, 0f));
                    Move(L(c, "Intro/city_near"), new Vector2(near, 0f));
                    Move(L(c, "Intro/billboards"), new Vector2(near, 0f));
                    Move(L(c, "Intro/street"), new Vector2(-(u - 0.5f) * 120f, 0f));
                    // 看板の画面に砂嵐（ときどき強く乱れる）
                    bool strong = Mathf.Repeat(lt * 0.8f, 1f) > 0.85f;
                    for (int i = 0; i < Screens.Length && i < c.extras.Count; i++)
                    {
                        Layer n = c.extras[i];
                        Rect r = Screens[i];
                        n.rt.pivot = new Vector2(0.5f, 0.5f);
                        n.rt.sizeDelta = r.size;
                        n.rt.anchoredPosition = new Vector2(r.center.x - W * 0.5f + near, H * 0.5f - r.center.y);
                        Noise(n, strong);
                        float b = 0.45f + 0.25f * Random.value;
                        n.img.color = new Color(b, b, b, 1f);
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
                    // 電波塔の上から、電波の輪が何度も広がる
                    Layer waves = L(c, "Intro/waves");
                    float phase = Mathf.Repeat(lt / 1.3f, 1f);
                    Pose(waves, Vector2.zero, 0f, Mathf.Lerp(0.6f, 1.7f, phase), new Vector2(290f, 240f));
                    SetAlpha(waves, (1f - phase) * Smooth(lt / 0.5f));
                    Zoom(c, Mathf.Lerp(1f, 1.18f, Smooth(u)), new Vector2(290f, 100f));
                    break;
                }

                case "close":
                {
                    Pose(L(c, "Intro/boss_big"), Vector2.zero, 0f, Mathf.Lerp(1.05f, 1f, EaseOut(lt / 1.5f)), new Vector2(340f, -500f));
                    SetAlpha(L(c, "Intro/scope"), Smooth((lt - 0.2f) / 0.4f));
                    // 音の波：にぎやかにゆれていた波が、だんだん平らになる
                    float keep = lt < 1.5f ? 1f : Mathf.Lerp(1f, 0.02f, Smooth((lt - 1.5f) / 1.8f));
                    Waveform(c, lt, keep, Smooth((lt - 0.2f) / 0.4f));
                    // 平らになったら「NOISE CANCEL : ON」が点滅
                    Layer on = L(c, "Intro/cancel_on");
                    SetAlpha(on, lt < 3.3f ? 0f : (Mathf.Repeat(lt * 2f, 1f) < 0.65f ? 1f : 0.3f));
                    CaptionIn(L(c, "Common/boss_box"), L(c, lineFile), lt, 3.0f, 3.3f, 1.2f);
                    break;
                }

                case "title":
                    TitleIn(L(c, "Common/title_bg"), L(c, "Intro/title_text"), lt);
                    break;
            }
        }

        private void Waveform(Cut c, float lt, float keep, float alpha) => WaveformBars(c.extras, lt, keep, alpha);
    }
}
