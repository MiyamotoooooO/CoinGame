using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 上から降ってくるメダル（ラウンドの始まりのおまけ・F1 のテスト用など）を、モニターより手前に描く。
    ///
    /// モニターは「くっきり表示」のために、盤面の絵の上に重ねて描いている。そのため、モニターの前を
    /// 落ちていくメダルも、モニターの下にかくれてしまう。
    /// そこで、モニターの下のふちより高い所にあるメダルだけ、モニターと一緒に重ねて描くレイヤー
    /// （設定の frontEffectsLayer。パーティクルと同じ）に移す。重ねて描く時は奥行きも比べるので、
    /// モニターより手前のメダルは手前に、奥のメダルは奥に見える。
    /// 下のふちより下に来たら、元のレイヤーに戻す（盤面の上のメダルは、いつもどおりに描く）。
    ///
    /// ・メダルの当たり判定は変わらない（どのレイヤーどうしもぶつかる設定のため）
    /// ・MedalPusherGame は変えず、game.Pool.Active（出ているメダル）を見て動く
    /// ・シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    /// </summary>
    [DisallowMultipleComponent]
    public class FallingMedalFront : MonoBehaviour
    {
        [Tooltip("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;

        [Tooltip("モニターの下のふちから、さらに下へ何 unit までを「高い所」とみなすか（大きいほど長く手前に描く）")]
        [Range(0f, 3f)] public float extraMargin = 0.2f;

        [Tooltip("モニターが見つからない時の高さ（この高さより上のメダルを手前に描く）。0 なら筐体の壁の高さ")]
        public float fallbackHeight = 0f;

        readonly Dictionary<Medal, int> originalLayers = new Dictionary<Medal, int>();
        Renderer monitorRenderer;
        float searchTimer;

        /// <summary>シーンに無ければ、MedalPusherGame の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<FallingMedalFront>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<FallingMedalFront>();
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null) enabled = false;
        }

        void LateUpdate()
        {
            if (game == null || game.Pool == null) return;
            var medals = game.Pool.Active;
            if (medals == null) return;

            bool use = game.monitor.effectsInFrontOfMonitor;
            int front = Mathf.Clamp(game.monitor.frontEffectsLayer, 0, 31);
            float limit = Threshold();

            for (int i = 0; i < medals.Count; i++)
            {
                var m = medals[i];
                if (m == null) continue;
                var go = m.gameObject;
                bool high = use && m.transform.position.y > limit;
                bool moved = originalLayers.TryGetValue(m, out int original);

                if (high && go.layer != front)
                {
                    if (!moved) originalLayers[m] = go.layer;
                    SetLayer(m.transform, front);
                }
                else if (!high && moved)
                {
                    if (go.layer == front) SetLayer(m.transform, original);
                    originalLayers.Remove(m);
                }
            }
        }

        /// <summary>この高さより上のメダルを、モニターと一緒に描く。</summary>
        float Threshold()
        {
            if (monitorRenderer == null)
            {
                // モニターの画面は、始まってから作られることもあるので、ときどき探す
                searchTimer -= Time.unscaledDeltaTime;
                if (searchTimer <= 0f)
                {
                    searchTimer = 1f;
                    var go = GameObject.Find(game.monitor.screenObjectName);
                    if (go != null) monitorRenderer = go.GetComponentInChildren<Renderer>();
                }
            }
            if (monitorRenderer != null) return monitorRenderer.bounds.min.y - extraMargin;
            return (fallbackHeight > 0f ? fallbackHeight : game.layout.wallHeight) - extraMargin;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        void OnDestroy()
        {
            // 手前に移したままのメダルを戻す
            foreach (var pair in originalLayers)
                if (pair.Key != null) SetLayer(pair.Key.transform, pair.Value);
            originalLayers.Clear();
        }
    }
}
