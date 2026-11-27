using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// MainScene に来たしゅんかん、エリア1の始まりのムービーが出るまでの間だけ、画面を真っ黒でかくす。
    /// （ムービーは、シーンが始まって1フレーム後に流れ始めるので、そのすき間に MainScene がちらっと見えるのを防ぐ）
    ///
    /// 【しくみ】
    ///   ・Area1MoviesAutoSetup が、シーンが始まった時に付ける
    ///   ・ムービーが流れ始めたら、黒い画面を消す（ムービーの方がいつも上に出る）
    ///   ・続きからエリア2以降で始めた時など、ムービーが流れない時は、少し待ってから消す
    /// </summary>
    public class Area1MovieCover : MonoBehaviour
    {
        [Tooltip("ムービーが流れない時に、黒い画面を消すまでの最大のフレーム数")]
        public int maxFrames = 10;
        [Tooltip("黒い画面の重なりの順番。ムービー（5000）より下にする")]
        public int sortingOrder = 4999;

        private AreaMovieDirector director;
        private FieldInfo playingField;
        private GameObject canvasGo;
        private int waited;

        /// <summary>ムービーの係を見はるように、黒い画面を作る</summary>
        public void Setup(AreaMovieDirector target)
        {
            director = target;
            playingField = typeof(AreaMovieDirector).GetField("playing", BindingFlags.Instance | BindingFlags.NonPublic);

            canvasGo = new GameObject("Area1MovieCover");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var img = new GameObject("Black").AddComponent<Image>();
            img.transform.SetParent(canvasGo.transform, false);
            img.color = Color.black;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            // フレームの数で数える（シーンを読みこんだ直後は1フレームの時間がとても長くなることがあるため）
            waited++;

            bool playing = director != null && playingField != null && (bool)playingField.GetValue(director);
            if (playing || director == null || waited > maxFrames) Destroy(gameObject);
        }
    }
}
