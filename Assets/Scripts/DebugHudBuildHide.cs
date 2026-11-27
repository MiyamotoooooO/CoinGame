using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// ビルドしたゲームでは、デバッグ用の黒い表示（DebugHud：左上の状況・右の調整パネル・下の操作の案内など）を最初から消す。
    ///
    /// 【しくみ】
    ///   ・ゲームが始まる前に DebugHud.Visible を false にする（エディタでは何もしない）
    ///   ・F2 キーでの切り替えは、InputCompat の方でエディタだけにしてある
    ///   ・画面まん中の大きな文字（CLEAR! など）は、今まで通り出る
    /// </summary>
    public static class DebugHudBuildHide
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void HideInBuild()
        {
            if (!Application.isEditor) DebugHud.Visible = false;
        }
    }
}
