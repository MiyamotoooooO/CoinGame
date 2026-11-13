using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// ラウンドをクリアするたびに、次のラウンドの始めに上から落とす「追加メダル」の枚数を増やしていく（全エリア共通）。
    /// ラウンドが進むとノルマが上がり、盤面のメダルも減っていくので、盤面にいつも一定くらいのメダルがあるようにする。
    ///
    /// 枚数の決め方（値は仮。Inspector で変えられる）
    ///   クリアしたのがエリアの n ラウンド目なら、 Base Medals ＋ Step × (n − 1) 枚
    ///   例：Base 5・Step 2 → 1→2 ラウンドは 5 枚、2→3 は 7 枚、3→4 は 9 枚、4→5 は 11 枚、5→6 は 13 枚、
    ///       ボス（6 ラウンド目）のあと次のエリアへ進む時は 15 枚
    ///   Keep Board Medals を 1 以上にすると、盤面のメダルがその枚数より少ない時は、足りない分まで増やす（多い方を使う）
    ///   どちらの場合も Max Medals 枚まで
    ///
    /// ・しくみ：ラウンドをクリアした時に、MedalPusherGame の rules.roundBonusMedals（次のラウンドの始めに落とす枚数）を
    ///   書きかえる。落とし方（1枚ずつ降らせる・レアメダルが混ざる）は、もとのまま
    /// ・MedalPusherGame は変えない。シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    /// </summary>
    [DisallowMultipleComponent]
    public class RoundBonusScaling : MonoBehaviour
    {
        [Tooltip("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;

        [Tooltip("1 ラウンド目をクリアした時の枚数（仮）")]
        [Min(0)] public int baseMedals = 5;
        [Tooltip("ラウンドが1つ進むごとに増やす枚数（仮）")]
        [Min(0)] public int step = 2;
        [Tooltip("盤面のメダルがこの枚数より少ない時は、足りない分まで増やす（0 なら使わない）")]
        [Min(0)] public int keepBoardMedals = 0;
        [Tooltip("1回に落とす枚数の上限")]
        [Min(0)] public int maxMedals = 40;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<RoundBonusScaling>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<RoundBonusScaling>();
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null)
            {
                enabled = false;
                return;
            }
            game.StateChanged += OnStateChanged;
        }

        void OnDestroy()
        {
            if (game != null) game.StateChanged -= OnStateChanged;
        }

        /// <summary>ラウンドをクリアした時に、次のラウンドの始めに落とす枚数を決める。</summary>
        void OnStateChanged(GameState state)
        {
            if (state != GameState.Cleared) return;
            int n = game.RoundIndex + 1;   // クリアしたのは、このエリアの n ラウンド目
            int count = baseMedals + step * Mathf.Max(0, n - 1);
            if (keepBoardMedals > 0 && game.Pool != null)
                count = Mathf.Max(count, keepBoardMedals - game.Pool.ActiveCount);
            count = Mathf.Clamp(count, 0, maxMedals);
            game.rules.roundBonusMedals = count;
        }

        /// <summary>今クリアした時に落とす枚数（確かめる用）。</summary>
        public int NextBonus => game != null ? game.rules.roundBonusMedals : 0;
    }
}
