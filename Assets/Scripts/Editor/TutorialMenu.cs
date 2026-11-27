using UnityEditor;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// チュートリアルを、もう一度見られるようにするメニュー（Editor フォルダに置く。ゲームには入らない）。
    /// 上のメニュー「MedalPusher」→「チュートリアルをもう一度見る」で、見終わった記録を消す。
    /// </summary>
    public static class TutorialMenu
    {
        [MenuItem("MedalPusher/チュートリアルをもう一度見る")]
        static void ResetTutorial()
        {
            PlayerPrefs.DeleteKey(TutorialGuide.DoneKey);
            PlayerPrefs.Save();
            Debug.Log("[MedalPusher] チュートリアルの記録を消しました。次にエリア1のラウンド1を遊ぶと、もう一度出ます。");
        }
    }
}
