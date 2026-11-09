using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// ラウンドをクリアしたあと（クリアの帯が出ている間・ショップの間）に穴へ落ちたメダルにも、所持ポイントを入れる。
    ///
    /// MedalPusherGame は、プレイ中と判定待ちの間だけポイントを入れ、クリアのあとは所持メダルだけを増やす。
    /// そこで、クリアのあとに落ちた分のポイント（中央・端の穴のポイント。レアメダルは倍）を、ここで足す。
    /// AddPoints を使うので、エリアのポイント（ショップで使う所持ポイント）に入る。
    ///
    /// ・MedalPusherGame は変えず、MedalDropped の知らせと public の AddPoints を使う
    /// ・シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    /// </summary>
    [DisallowMultipleComponent]
    public class AfterClearScoring : MonoBehaviour
    {
        [Tooltip("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;

        [Tooltip("ショップを開いている間に落ちたメダルにも、ポイントを入れる")]
        public bool includeShop = true;

        /// <summary>シーンに無ければ、MedalPusherGame の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<AfterClearScoring>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<AfterClearScoring>();
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null)
            {
                enabled = false;
                return;
            }
            game.MedalDropped += OnMedalDropped;
        }

        void OnDestroy()
        {
            if (game != null) game.MedalDropped -= OnMedalDropped;
        }

        void OnMedalDropped(Medal medal, DropZoneType type)
        {
            // プレイ中・判定待ちの間は、MedalPusherGame がもう入れている
            bool afterClear = game.State == GameState.Cleared || (includeShop && game.State == GameState.Shop);
            if (!afterClear) return;

            int basePoints = type == DropZoneType.Center ? game.rules.centerPoints
                           : type == DropZoneType.Side ? game.rules.sidePoints
                           : 0;
            if (basePoints <= 0) return;
            float scale = medal != null && medal.IsRare ? game.rules.rarePointMultiplier : 1f; // レアメダルはポイントが倍
            game.AddPoints(Mathf.RoundToInt(basePoints * scale));
        }
    }
}
