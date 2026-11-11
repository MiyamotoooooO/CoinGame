using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// エリア2（ノイズ・キャンセラー）のボスを倒した時に流すムービー。
    ///
    /// 【使い方】
    ///   MainScene の Hierarchy で右クリック → Create Empty → 名前を「Area2ClearMovie」にして、このスクリプトを付ける。
    ///   Inspector のチェックボックスで、流すムービーと、ボスのせりふを1つずつ選ぶ（それぞれ2つ以上は選べない）。
    ///
    /// 【流れるタイミング】
    ///   エリア2のボスのラウンドで、ノルマを達成した（Cleared になった）時。クリアの帯などは、ムービーの後に出る
    /// </summary>
    public class Area2ClearMovie : AreaMovieBase
    {
        [Header("■ 流すムービー（1つだけチェックできる。新しくチェックした方が残る）")]
        [Tooltip("案1：色が戻る（約18秒）。平らだった音の波がはね上がり、ボスがノイズになって崩れ、看板に広告が戻る")]
        public bool movie1_ColorReturns = true;
        [Tooltip("案2：放送終了（約11.5秒）。ボスの放送が SIGNAL LOST で切れ、ヴィニーが割りこむ")]
        public bool movie2_SignalLost = false;
        [Tooltip("案3：短いクリア演出（約6.5秒）。音の波がはね上がって AREA 2 CLEAR")]
        public bool movie3_Short = false;
        [SerializeField, HideInInspector] private int lastMovie = 0;

        [Header("■ ボスのやられた時のせりふ（1つだけチェックできる。案3では出ない）")]
        [Tooltip("a：「ノイズが……止まらない……！」")]
        public bool lineA = true;
        [Tooltip("b：「この音……耳から、離れない……」")]
        public bool lineB = false;
        [Tooltip("c：「……ヘッドホンを外したのは、久しぶりだ。」")]
        public bool lineC = false;
        [SerializeField, HideInInspector] private int lastLine = 0;

        [Header("■ 音（空のままでも動く。自動で読みこむ）")]
        [Tooltip("案1 の曲と効果音")]
        public AreaMovieDirector.MovieSound sound1 = new AreaMovieDirector.MovieSound(new[]
        {
            ("音の波がはね上がる「ギュイーン」", 0.8f), ("音が戻る「ドン」", 2.0f), ("ボスが崩れるノイズ", 4.2f),
            ("テレビが消える「プツン」", 5.6f), ("街に色が戻る「キラーン」", 6.5f), ("タイトルの「ドン」", 14.15f),
        });
        [Tooltip("案2 の曲と効果音")]
        public AreaMovieDirector.MovieSound sound2 = new AreaMovieDirector.MovieSound(new[]
        {
            ("電波の乱れ", 0.6f), ("テレビが消える「プツン」", 4.3f), ("砂嵐のザー", 4.5f),
            ("ヴィニー登場の「ドン」", 6.5f), ("タイトルの「ドン」", 8.95f),
        });
        [Tooltip("案3 の曲と効果音")]
        public AreaMovieDirector.MovieSound sound3 = new AreaMovieDirector.MovieSound(new[]
        {
            ("音の波がはね上がる「ギュイーン」", 0.4f), ("音が戻る「ドン」", 1.6f), ("タイトルの「ドン」", 3.15f),
        });

        protected override int DefaultAreaIndex => 1;
        protected override string DefaultResourceFolder => "AreaMovies/Area2";
        protected override string DefaultAudioFolder => "AreaMovies/Area2/Audio/Clear";
        protected override AreaMovieDirector.MovieSound CurrentSound => movie == 0 ? sound1 : movie == 1 ? sound2 : sound3;
        protected override string SePrefix => $"m{movie + 1}_se_";

        private const int BarCount = 48;
        private int movie = 0;
        private string lineFile = "Clear/line_a";

        private void OnValidate()
        {
            int m = KeepOne(new[] { movie1_ColorReturns, movie2_SignalLost, movie3_Short }, lastMovie);
            movie1_ColorReturns = m == 0; movie2_SignalLost = m == 1; movie3_Short = m == 2;
            lastMovie = m;
            int l = KeepOne(new[] { lineA, lineB, lineC }, lastLine);
            lineA = l == 0; lineB = l == 1; lineC = l == 2;
            lastLine = l;
        }

        /// <summary>エリア2のボスのラウンドで、ノルマを達成した時</summary>
        protected override bool ShouldPlay(GameState state, GameState previous)
        {
            return state == GameState.Cleared && game.IsBossRound && game.AreaIndex == Area;
        }

        protected override bool PrepareSelection()
        {
            movie = movie1_ColorReturns ? 0 : movie2_SignalLost ? 1 : movie3_Short ? 2 : -1;
            lineFile = lineB ? "Clear/line_b" : lineC ? "Clear/line_c" : "Clear/line_a";
            return movie >= 0;
        }

        protected override void BuildMovie()
        {
            if (movie == 0)
            {
                Cut close = AddCut("close", 0f, 6f, "Intro/close_bg", "Intro/boss_big", "Clear/boss_glitch", "Intro/scope", "Common/boss_box", lineFile);
                for (int i = 0; i < BarCount; i++) AddExtra(close, "bar" + i, null);
                Cut color = AddCut("color", 6f, 14f, "Intro/sky", "Intro/city_far", "Intro/city_near", "Intro/street", "Intro/billboards",
                                   "Clear/sky_c", "Clear/city_far_c", "Clear/city_near_c", "Clear/street_c", "Clear/billboards_c", "Clear/notes");
                AddEdge(color);
                AddCut("title", 14f, 18f, "Common/title_bg", "Clear/title_text");
                AddDip(6f);
                AddDip(14f);
                AddHit(2.0f, 0.9f, 1f);
                AddHit(5.6f, 0.7f, 0.4f);
                AddHit(14.15f, 0.9f, 1f);
            }
            else if (movie == 1)
            {
                AddCut("broadcast", 0f, 4.5f, "Clear/studio_bg", "Clear/boss_tv", "Common/noise", "Common/boss_box", lineFile);
                AddCut("lost", 4.5f, 6.5f, "Common/noise", "Clear/signal_lost");
                AddCut("vinny", 6.5f, 8.8f, "Clear/vinny_bg", "Clear/vinny", "Clear/vinny_box", "Clear/vinny_text");
                AddCut("title", 8.8f, 11.5f, "Common/title_bg", "Clear/title_text");
                AddHit(4.3f, 0.8f, 0.6f);
                AddHit(6.5f, 0.6f, 0.4f);
                AddHit(8.95f, 0.9f, 1f);
            }
            else
            {
                Cut wave = AddCut("wave", 0f, 3f, "Intro/close_bg", "Intro/scope");
                for (int i = 0; i < BarCount; i++) AddExtra(wave, "bar" + i, null);
                AddCut("title", 3f, 6.5f, "Common/title_bg", "Clear/title_text");
                AddHit(1.6f, 1f, 1f);
                AddHit(3.15f, 0.9f, 1f);
            }
        }

        protected override void Animate(Cut c, float lt, float u)
        {
            switch (c.name)
            {
                case "close":
                {
                    // 平らだった音の波が、一気にはね上がる
                    float keep = lt < 0.8f ? 0.02f : Mathf.Lerp(0.02f, 1.2f, EaseIn((lt - 0.8f) / 1.2f));
                    if (lt > 2.0f) keep = 1f + 0.2f * Mathf.Exp(-(lt - 2f) * 3f);
                    WaveformBars(c.extras, lt, keep, Smooth(lt / 0.4f));
                    SetAlpha(L(c, "Intro/scope"), Smooth(lt / 0.4f));

                    // ボス：ノイズが走り、最後はテレビが消えるように縦につぶれて消える
                    Layer boss = L(c, "Intro/boss_big");
                    Layer glitch = L(c, "Clear/boss_glitch");
                    Vector2 center = new Vector2(340f, -139f);
                    float shake = lt > 2.0f ? Mathf.Clamp01((lt - 2.0f) / 2.2f) : 0f;
                    float jx = (Mathf.PerlinNoise(lt * 30f, 1.3f) - 0.5f) * 80f * shake;
                    bool showGlitch = shake > 0f && Mathf.Repeat(lt * (6f + shake * 14f), 1f) < 0.3f + shake * 0.4f;
                    float off = Smooth((lt - 4.6f) / 1.0f);
                    float sx = 1f + off * 0.4f;
                    float sy = Mathf.Lerp(1f, 0.02f, off * off);
                    PoseXY(boss, new Vector2(jx, 0f), sx, sy, center);
                    PoseXY(glitch, new Vector2(-jx, 0f), sx, sy, center);
                    float gone = 1f - Smooth((lt - 5.4f) / 0.2f);
                    SetAlpha(boss, (showGlitch ? 0f : 1f) * gone);
                    SetAlpha(glitch, (showGlitch ? 1f : 0f) * gone);
                    CaptionIn(L(c, "Common/boss_box"), L(c, lineFile), lt, 2.4f, 2.7f, 1.2f);
                    break;
                }

                case "color":
                {
                    // 灰色の街に、右から色が戻る。看板には広告が映る
                    float p = Smooth((lt - 0.5f) / 3.5f);
                    foreach (string n in new[] { "Clear/sky_c", "Clear/city_far_c", "Clear/city_near_c", "Clear/street_c", "Clear/billboards_c" })
                    {
                        RevealFromRight(L(c, n), p);
                    }
                    PlaceEdge(c, p);
                    Layer notes = L(c, "Clear/notes");
                    float na = Smooth((lt - 3.8f) / 0.8f);
                    Move(notes, new Vector2(Mathf.Sin(lt * 1.4f) * 12f, (lt - 3.8f) * 14f - 40f * (1f - na)));
                    SetAlpha(notes, na);
                    Zoom(c, Mathf.Lerp(1.04f, 1.12f, Smooth(u)), new Vector2(0f, 60f));
                    break;
                }

                case "broadcast":
                {
                    Layer boss = L(c, "Clear/boss_tv");
                    float g = Smooth((lt - 0.6f) / 2.6f);
                    float jx = (Mathf.PerlinNoise(lt * 30f, 2.1f) - 0.5f) * 120f * g;
                    float off = Smooth((lt - 3.9f) / 0.4f);
                    PoseXY(boss, new Vector2(jx, 0f), 1f + off * 0.3f, Mathf.Lerp(1f, 0.02f, off), new Vector2(0f, -137f));
                    SetAlpha(boss, 1f - Smooth((lt - 4.25f) / 0.1f));
                    Zoom(c, Mathf.Lerp(1f, 1.06f, Smooth(u)), new Vector2(0f, 150f));
                    Layer noise = L(c, "Common/noise");
                    Noise(noise, g > 0.5f);
                    SetAlpha(noise, g * (Mathf.Repeat(lt * 9f, 1f) < 0.5f ? 0.55f : 0.15f));
                    CaptionIn(L(c, "Common/boss_box"), L(c, lineFile), lt, 0.8f, 1.1f, 1.2f);
                    break;
                }

                case "lost":
                {
                    Noise(L(c, "Common/noise"), false);
                    Layer t = L(c, "Clear/signal_lost");
                    Move(t, new Vector2(Mathf.Repeat(lt * 5f, 1f) < 0.08f ? 20f : 0f, 0f));
                    SetAlpha(t, Mathf.Repeat(lt * 3f, 1f) < 0.7f ? 1f : 0.3f);
                    break;
                }

                case "vinny":
                {
                    Layer v = L(c, "Clear/vinny");
                    float hop = lt > 0.5f ? Mathf.Abs(Mathf.Sin(lt * 2f * Mathf.PI)) * 14f : 0f;
                    Pose(v, new Vector2(Mathf.Lerp(-700f, 0f, EaseBack(lt / 0.5f)), hop), 0f, 1f, new Vector2(-200f, -290f));
                    CaptionIn(L(c, "Clear/vinny_box"), L(c, "Clear/vinny_text"), lt, 0.4f, 0.6f, 0.6f);
                    break;
                }

                case "wave":
                {
                    float keep = lt < 0.4f ? 0.02f : Mathf.Lerp(0.02f, 1.2f, EaseIn((lt - 0.4f) / 1.2f));
                    if (lt > 1.6f) keep = 1f + 0.2f * Mathf.Exp(-(lt - 1.6f) * 3f);
                    WaveformBars(c.extras, lt, keep, 1f);
                    Zoom(c, Mathf.Lerp(1f, 1.12f, Smooth(u)), new Vector2(-490f, 20f));
                    break;
                }

                case "title":
                    TitleIn(L(c, "Common/title_bg"), L(c, "Clear/title_text"), lt);
                    break;
            }
        }
    }
}
