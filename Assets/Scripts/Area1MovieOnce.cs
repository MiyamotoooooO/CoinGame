using System.Reflection;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// エリア1の始まりのムービー（AreaMovieDirector）が、続けて2回流れないようにする。
    ///
    /// 【しくみ】
    ///   ・ムービーが1回流れ終わったら、ムービーの係に「合図を聞かない」ようにさせる
    ///   ・ゲームが一度でも「プレイ中」より先に進んだら（遊び始めたら）、また聞くようにもどす
    ///     → だから、エリア1からやり直した時は、ちゃんともう一度流れる
    ///   ・R キー（デバッグの「リセット」ボタンも同じ）でリセットした時も、ムービーは流さない
    ///   ・Area1MoviesAutoSetup が、ムービーの係と同じ GameObject に付ける
    /// </summary>
    public class Area1MovieOnce : MonoBehaviour
    {
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        private AreaMovieDirector director;
        private MedalPusherGame game;
        private FieldInfo playingField;
        private FieldInfo listenField;   // AreaMovieDirector の startupChecked（false の間は合図を聞かない）
        private bool wasPlaying;
        private bool blocked;
        private int startFrame;          // シーンが始まったフレーム（最初の自動リセットは数えないため）

        /// <summary>見はるムービーの係を決める</summary>
        public void Setup(AreaMovieDirector target)
        {
            director = target;
            startFrame = Time.frameCount;
            playingField = typeof(AreaMovieDirector).GetField("playing", Flags);
            listenField = typeof(AreaMovieDirector).GetField("startupChecked", Flags);
            if (playingField == null || listenField == null)
                Debug.LogWarning("[Area1MovieOnce] AreaMovieDirector の中身が変わったため、2回目を止められません。", this);
        }

        private void Start()
        {
            game = FindFirstObjectByType<MedalPusherGame>();
            if (game != null)
            {
                game.StateChanged += OnStateChanged;
                game.GameReset += OnGameReset;
            }
        }

        private void OnDestroy()
        {
            if (game != null)
            {
                game.StateChanged -= OnStateChanged;
                game.GameReset -= OnGameReset;
            }
        }

        /// <summary>
        /// リセットされた時（R キーなど）。この後すぐ「エリアの始まり」の合図が来るので、その前に聞かないようにする。
        /// シーンが始まった時にゲームが自分で行うリセットは数えない（最初のムービーは流すため）
        /// </summary>
        private void OnGameReset()
        {
            if (Time.frameCount <= startFrame + 1) return;
            if (director == null || listenField == null) return;
            blocked = true;
            listenField.SetValue(director, false);
        }

        private void Update()
        {
            if (director == null || playingField == null || listenField == null) return;

            bool playing = (bool)playingField.GetValue(director);
            if (wasPlaying && !playing)
            {
                // 流れ終わった：次に遊び始めるまでは、合図を聞かないようにする
                blocked = true;
            }
            wasPlaying = playing;

            // 係が「聞く」状態にもどっていたら、止め直す（1フレーム目の確認で true になるため）
            if (blocked && (bool)listenField.GetValue(director)) listenField.SetValue(director, false);
        }

        private void OnStateChanged(GameState state)
        {
            if (!blocked || listenField == null || director == null) return;

            // 始まりの演出（Intro / Warmup）が終わって、遊び始めたら、また流せるようにもどす
            if (state != GameState.Intro && state != GameState.Warmup)
            {
                blocked = false;
                listenField.SetValue(director, true);
            }
        }
    }
}
