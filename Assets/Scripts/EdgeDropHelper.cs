using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 盤面の手前のふちで、傾いたまま引っかかって落ちないメダルを、落としてあげる。
    ///
    /// 引っかかる理由
    ///   ・メダルが立つのを防ぐ「寝かせる力」（MedalPool）が、ふちで傾きかけたメダルを寝かせ直してしまい、
    ///     ふちの上でつり合って止まる
    ///   ・となりのメダルとの摩擦で、少しはみ出したまま支えられてしまう
    /// やること
    ///   1. ふちのそば（ふちから半径 × Edge Band の内側）のメダルには、寝かせる力をかけない
    ///   2. ふちよりはみ出していて、傾いていて、ほとんど動かないまま Stuck Seconds 秒たったメダルを起こして、
    ///      手前の下へ軽く押す（落ちかけのメダルだけ。盤面の上でふつうに寝ているメダルは押さない）
    ///
    /// ・MedalPusherGame は変えない。シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    /// </summary>
    [DisallowMultipleComponent]
    public class EdgeDropHelper : MonoBehaviour
    {
        [Tooltip("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;

        [Tooltip("寝かせる力をかけない、ふちのそばの広さ（メダルの半径を 1 とした数。ふちから奥へ）")]
        [Range(0f, 3f)] public float edgeBand = 1.0f;
        [Tooltip("押す対象：メダルのまん中が、ふちよりこれだけ奥まで（半径を 1 とした数）")]
        [Range(-1f, 2f)] public float overhang = 0.5f;
        [Tooltip("押す対象：寝ている状態から、この角度以上傾いているメダル（度）")]
        [Range(0f, 60f)] public float tiltAngle = 12f;
        [Tooltip("押す対象：これより遅いメダル（メートル/秒）")]
        [Range(0f, 1f)] public float slowSpeed = 0.15f;
        [Tooltip("引っかかってから押すまでの秒数")]
        [Range(0f, 3f)] public float stuckSeconds = 0.3f;
        [Tooltip("手前の下へ押す強さ（加速度）")]
        [Range(0f, 30f)] public float pushAccel = 8f;

        readonly Dictionary<Medal, float> stuck = new Dictionary<Medal, float>();
        readonly List<Medal> remove = new List<Medal>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<EdgeDropHelper>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<EdgeDropHelper>();
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null || game.Pool == null)
            {
                enabled = false;
                return;
            }
            // ふちのそばのメダルには、寝かせる力をかけない
            game.Pool.skipAntiStand = m => m.Body != null && m.Body.position.z < EdgeZ + Radius * edgeBand;
        }

        void OnDestroy()
        {
            if (game != null && game.Pool != null) game.Pool.skipAntiStand = null;
        }

        float EdgeZ => game.layout.floorFrontZ;                 // 盤面の手前のふち（手前ほど Z が小さい）
        float Radius => game.tuning.medalDiameter * 0.5f;

        void FixedUpdate()
        {
            var pool = game.Pool;
            if (pool == null) return;
            float zLimit = EdgeZ + Radius * overhang;
            float cosTilt = Mathf.Cos(tiltAngle * Mathf.Deg2Rad);
            float slow2 = slowSpeed * slowSpeed;
            float dt = Time.fixedDeltaTime;

            var list = pool.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null || m.Consumed) continue;
                var rb = m.Body;
                if (rb == null || rb.isKinematic) continue;
                Vector3 p = rb.position;
                bool near = p.z < zLimit && rb.velocity.y > -0.3f;                 // ふちにかかっていて、まだ落ちていない
                float flat = Mathf.Abs((rb.rotation * Vector3.up).y);              // 1 = 寝ている
                bool caught = near && flat < cosTilt && rb.velocity.sqrMagnitude < slow2;
                if (!caught)
                {
                    stuck.Remove(m);
                    continue;
                }
                stuck.TryGetValue(m, out float t);
                t += dt;
                stuck[m] = t;
                if (t < stuckSeconds) continue;

                // 起こして、手前の下へ押す
                rb.WakeUp();
                rb.AddForce((Vector3.back * 0.7f + Vector3.down).normalized * pushAccel, ForceMode.Acceleration);
            }

            // 使い終わったメダル（プールに戻った物）の記録を消す
            if (stuck.Count > 64)
            {
                remove.Clear();
                foreach (var kv in stuck) if (kv.Key == null || !kv.Key.IsActiveInPool) remove.Add(kv.Key);
                foreach (var k in remove) stuck.Remove(k);
            }
        }
    }
}
