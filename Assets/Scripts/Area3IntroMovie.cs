using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// エリア3（フェーダー・ダウン）の始まりに流すムービー（約21秒・区画紹介）。
    ///
    /// 【使い方】
    ///   MainScene の Hierarchy で右クリック → Create Empty → 名前を「Area3IntroMovie」にして、このスクリプトを付ける。
    ///   Inspector のチェックボックスで、ボスのせりふを1つ選ぶ（2つ以上は選べない）。
    ///
    /// 【流れ】
    ///   灰色の街（音の出ないスピーカーの山）→ ミキサーのステージの上のボス
    ///   → ミキサーのつまみ（フェーダー）が1本ずつ下がって「ALL CHANNELS : MUTE」→ AREA 3
    ///
    /// 【流れるタイミング】
    ///   エリア3が始まった時（エリア2から進んだ時と、続きからエリア3で始めた時）
    /// </summary>
    public class Area3IntroMovie : AreaMovieBase
    {
        [Header("■ ボスのせりふ（1つだけチェックできる。新しくチェックした方が残る）")]
        [Tooltip("a：「ボリュームは、静かに下げるものよ。」")]
        public bool lineA = true;
        [Tooltip("b：「にぎやかな音は、全部フェードアウト。」")]
        public bool lineB = false;
        [Tooltip("c：「この区画の音量は、わたしが決めるの。」")]
        public bool lineC = false;
        [SerializeField, HideInInspector] private int lastLine = 0;

        [Header("■ 音（空のままでも動く。自動で読みこむ）")]
        public AreaMovieDirector.MovieSound sound = new AreaMovieDirector.MovieSound(new[]
        {
            ("こもった人のざわめき", 0f), ("看板の「ジジッ」", 3.5f), ("スポットライトの「ガチャン」", 6.2f),
            ("つまみが下がる「スーッ」", 12.5f), ("音が消える「カチッ」", 14.3f), ("タイトルの「ドン」", 17.15f),
        });

        protected override int DefaultAreaIndex => 2;
        protected override string DefaultResourceFolder => "AreaMovies/Area3";
        protected override string DefaultAudioFolder => "AreaMovies/Area3/Audio/Intro";
        protected override AreaMovieDirector.MovieSound CurrentSound => sound;

        private const int FaderCount = 8;
        private static readonly float[] Phase = { 0.0f, 1.3f, 2.1f, 0.7f, 2.9f, 1.7f, 0.4f, 2.5f };
        private string lineFile = "Intro/line_a";

        private void OnValidate()
        {
            int pick = KeepOne(new[] { lineA, lineB, lineC }, lastLine);
            lineA = pick == 0; lineB = pick == 1; lineC = pick == 2;
            lastLine = pick;
        }

        /// <summary>エリア3が始まった時（演出中になった時）</summary>
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
            AddCut("street", 0f, 6f, "Intro/sky", "Intro/city_far", "Intro/city_near", "Intro/street", "Intro/speakers", "Intro/signs", "Intro/label");
            AddCut("stage", 6f, 11f, "Intro/stage_bg", "Intro/spots", "Intro/boss_small");
            Cut close = AddCut("close", 11f, 17f, "Intro/close_bg", "Intro/boss_big", "Intro/mixer", "Intro/mute_on", "Common/boss_box", lineFile);
            Texture2D knob = Load("Common/fader_knob");
            for (int i = 0; i < FaderCount; i++) AddExtra(close, "knob" + i, knob);
            AddCut("title", 17f, 21f, "Common/title_bg", "Intro/title_text");
            AddDip(6f);
            AddDip(11f);
            AddHit(11f + 3.3f, 0.3f, 0.8f);   // 全部のつまみが下がった
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
                    Move(L(c, "Intro/speakers"), new Vector2(near, 0f));
                    Move(L(c, "Intro/street"), new Vector2(-(u - 0.5f) * 120f, 0f));
                    // 消えたネオンが、ときどき一瞬だけ光りかける
                    Layer signs = L(c, "Intro/signs");
                    Move(signs, new Vector2(near, 0f));
                    bool blink = Mathf.Repeat(lt * 0.9f, 1f) > 0.82f && Mathf.Repeat(lt * 13f, 1f) < 0.5f;
                    SetAlpha(signs, blink ? 0.55f : 0.04f);
                    Zoom(c, 1.08f, Vector2.zero);
                    Layer label = L(c, "Intro/label");
                    float a = EaseOut((lt - 0.6f) / 0.5f);
                    Move(label, new Vector2((1f - a) * -60f, 0f));
                    SetAlpha(label, a);
                    break;
                }

                case "stage":
                {
                    // 両側のスポットライトがついて、ステージの上のボスを照らす
                    Layer spots = L(c, "Intro/spots");
                    float on = Smooth((lt - 0.3f) / 0.3f);
                    float flick = lt < 0.9f && Mathf.Repeat(lt * 12f, 1f) < 0.4f ? 0.3f : 1f;
                    SetAlpha(spots, on * flick);
                    Zoom(c, Mathf.Lerp(1f, 1.18f, Smooth(u)), new Vector2(290f, 100f));
                    break;
                }

                case "close":
                {
                    Pose(L(c, "Intro/boss_big"), Vector2.zero, 0f, Mathf.Lerp(1.05f, 1f, EaseOut(lt / 1.5f)), new Vector2(340f, -500f));
                    float panelA = Smooth((lt - 0.2f) / 0.4f);
                    SetAlpha(L(c, "Intro/mixer"), panelA);
                    // つまみ：ゆれていたのが、左から1本ずつ下がっていく
                    for (int i = 0; i < FaderCount && i < c.extras.Count; i++)
                    {
                        float live = LiveLevel(i, lt);
                        float down = Smooth((lt - (1.5f + i * 0.18f)) / 0.6f);
                        PlaceKnob(c.extras[i], i, Mathf.Lerp(live, 0f, down), panelA);
                    }
                    SetAlpha(L(c, "Intro/mute_on"), lt < 3.3f ? 0f : (Mathf.Repeat(lt * 2f, 1f) < 0.65f ? 1f : 0.3f));
                    CaptionIn(L(c, "Common/boss_box"), L(c, lineFile), lt, 3.0f, 3.3f, 1.2f);
                    break;
                }

                case "title":
                    TitleIn(L(c, "Common/title_bg"), L(c, "Intro/title_text"), lt);
                    break;
            }
        }

        /// <summary>つまみの「いつもの」高さ（0〜1）。曲に合わせてゆれている感じ</summary>
        private static float LiveLevel(int i, float lt)
        {
            return Mathf.Clamp01(0.55f + 0.25f * Mathf.Sin(lt * 3f + Phase[i]) + 0.1f * Mathf.Sin(lt * 7.3f + i));
        }

        /// <summary>
        /// つまみを置く。level は 0（いちばん下）〜 1（いちばん上）。
        /// ミキサーの画像の中で、i 本目の通り道（x = 170 + i×82、y = 720〜300）に合わせる
        /// </summary>
        private static void PlaceKnob(Layer k, int i, float level, float alpha)
        {
            float xImg = 170f + i * 82f;
            float yImg = 720f - Mathf.Clamp01(level) * 420f;
            k.rt.pivot = new Vector2(0.5f, 0.5f);
            k.rt.localScale = Vector3.one;
            k.rt.localRotation = Quaternion.identity;
            k.rt.sizeDelta = new Vector2(64f, 34f);
            k.rt.anchoredPosition = new Vector2(xImg - W * 0.5f, H * 0.5f - yImg);
            k.img.color = new Color(1f, 1f, 1f, alpha);
        }
    }
}
