using UnityEngine;
using UnityEngine.SceneManagement;

namespace MedalPusher
{
    /// <summary>
    /// MainScene に、エリア1のムービー2つを自動で置く。
    ///
    /// 【しくみ】
    ///   ・MainScene に来た時に「AreaMovie」（AreaMovieDirector）と「AreaClearMovie」（AreaClearMovie）を作る
    ///   ・AreaMovie       … エリア1が始まった時に流れる（MainScene に来た時・エリア1からやり直した時）
    ///   ・AreaClearMovie  … エリア1のボスを倒した（ノルマ達成した）時に流れる
    ///   ・設定は TestScene に置いてあった物と同じにしてある
    ///   ・もうシーンに置いてある時は、何もしない（2回流れないように）
    ///   ・MainScene.unity は書きかえないので、ほかの人の作業とぶつからない
    ///   ・ムービーが出るまでのすき間は、Area1MovieCover が画面を黒でかくす
    ///   ・始まりのムービーが続けて2回流れないように、Area1MovieOnce を付ける
    /// </summary>
    public static class Area1MoviesAutoSetup
    {
        /// <summary>ムービーを置くシーンの名前</summary>
        const string TargetScene = "MainScene";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (SceneManager.GetActiveScene().name != TargetScene) return;
            if (Object.FindFirstObjectByType<MedalPusherGame>() == null) return;

            // ---- エリア1の始まりのムービー ----
            var director = Object.FindFirstObjectByType<AreaMovieDirector>(FindObjectsInactive.Include);
            if (director == null)
            {
                var go = new GameObject("AreaMovie");
                go.SetActive(false);   // 設定を入れてから動かす（Awake より先に値を入れるため）
                var m = go.AddComponent<AreaMovieDirector>();
                m.areaIndex = 0;
                m.resourceFolder = "AreaMovies/Area1";
                m.pauseGame = true;
                m.allowSkip = true;
                m.sortingOrder = 5000;
                m.autoLoadAudio = true;
                m.audioFolder = "AreaMovies/Area1/Audio/Intro";
                m.dotFilter = true;
                m.useGameCameraSettings = true;
                m.pixelHeight = 400;
                m.flashStrength = 0.8f;
                m.shakeStrength = 22f;
                m.scanlineAlpha = 0.3f;
                go.SetActive(true);
                director = m;
            }

            // 始まりのムービーが、続けて2回流れないようにする
            if (director != null && director.GetComponent<Area1MovieOnce>() == null)
                director.gameObject.AddComponent<Area1MovieOnce>().Setup(director);

            // ムービーが出るまでの1フレームのすき間に、MainScene がちらっと見えないように黒でかくす
            if (director != null && Object.FindFirstObjectByType<Area1MovieCover>() == null)
            {
                var cover = new GameObject("Area1MovieCoverRoot").AddComponent<Area1MovieCover>();
                cover.Setup(director);
            }

            // ---- エリア1のボスを倒した時のムービー ----
            if (Object.FindFirstObjectByType<AreaClearMovie>(FindObjectsInactive.Include) == null)
            {
                var go = new GameObject("AreaClearMovie");
                go.SetActive(false);
                var c = go.AddComponent<AreaClearMovie>();
                c.movie1_ColorReturns = true;   // 案1「色が戻る」
                c.movie2_SignalLost = false;
                c.movie3_Short = false;
                c.lineA = false;
                c.lineB = false;
                c.lineC = true;                 // せりふ C
                c.debugPlayOnSceneStart = false; // 本番なので、シーンに来ただけでは流さない
                c.areaIndex = 0;
                c.resourceFolder = "AreaMovies/Area1";
                c.pauseGame = true;
                c.allowSkip = true;
                c.sortingOrder = 5000;
                c.autoLoadAudio = true;
                c.audioFolder = "AreaMovies/Area1/Audio/Clear";
                c.dotFilter = true;
                c.useGameCameraSettings = true;
                c.pixelHeight = 400;
                c.flashStrength = 0.8f;
                c.shakeStrength = 22f;
                c.scanlineAlpha = 0.3f;
                go.SetActive(true);
            }
        }
    }
}
