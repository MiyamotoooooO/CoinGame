using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// マウスカーソルを合わせると、白い輪郭と説明の枠（HoverTooltip）を出す物に付ける印。
    ///
    /// ・title（名前）、sub（種類）、body（説明）、status（今の様子。空なら出さない）を出す
    /// ・refresh を入れておくと、説明を出している間、毎フレーム呼ぶ（今の数字などを書きかえる用）
    /// ・輪郭と当たり判定は、この GameObject と子の見た目（MeshRenderer など）から作る。
    ///   子に別の HoverInfo が付いている所（手に持ったブロックなど）は、そちらの物として分ける
    /// ・extraRenderers：子ではないが、いっしょに光らせたい見た目（ボスの手の袖など）
    /// ・rectTarget：見た目が UI（ワールドの Canvas）の時は、その四角を輪郭と当たり判定に使う
    /// ・ふつうは HoverTooltip が自動で付ける。シーンの物に自分で付けて、文を書いてもよい
    /// </summary>
    [DisallowMultipleComponent]
    public class HoverInfo : MonoBehaviour
    {
        [Tooltip("名前（説明の枠のいちばん上）")]
        public string title = "";
        [Tooltip("種類（名前の下の小さい文字）")]
        public string sub = "";
        [Tooltip("説明")]
        [TextArea(2, 6)] public string body = "";
        [Tooltip("今の様子（空なら出さない）")]
        public string status = "";
        [Tooltip("今の様子を赤い文字にする（お邪魔中など）")]
        public bool statusWarning;

        /// <summary>説明を出している間、毎フレーム呼ぶ（文や数字を書きかえる用）。</summary>
        [NonSerialized] public Action<HoverInfo> refresh;
        /// <summary>子ではないが、いっしょに光らせる見た目。</summary>
        [NonSerialized] public List<Renderer> extraRenderers;
        /// <summary>見た目が UI の時の四角。</summary>
        [NonSerialized] public RectTransform rectTarget;

        /// <summary>いま有効な物（カーソルを合わせる候補）。</summary>
        public static readonly List<HoverInfo> All = new List<HoverInfo>();

        readonly List<Renderer> renderers = new List<Renderer>();
        float cacheTimer;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>輪郭と当たり判定に使う見た目（0.5秒ごとに探し直す）。</summary>
        public List<Renderer> Renderers
        {
            get
            {
                cacheTimer -= Time.unscaledDeltaTime;
                if (cacheTimer <= 0f)
                {
                    cacheTimer = 0.5f;
                    Collect();
                }
                return renderers;
            }
        }

        void Collect()
        {
            renderers.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!Usable(r)) continue;
                if (r.GetComponentInParent<HoverInfo>(true) != this) continue;   // 子の別の物（持っているブロックなど）は分ける
                renderers.Add(r);
            }
            if (extraRenderers != null)
                foreach (var r in extraRenderers)
                    if (r != null && Usable(r)) renderers.Add(r);
        }

        /// <summary>輪郭に使える見た目か（メッシュだけ。文字・パーティクル・線は使わない）。</summary>
        static bool Usable(Renderer r)
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) return false;
            if (r.GetComponent<TextMesh>() != null) return false;
            return true;
        }

        /// <summary>今見えている所をまとめた箱（見えている物が無ければ false）。</summary>
        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            if (rectTarget != null && rectTarget.gameObject.activeInHierarchy)
            {
                var corners = new Vector3[4];
                rectTarget.GetWorldCorners(corners);
                bounds = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < 4; i++) bounds.Encapsulate(corners[i]);
                bounds.Expand(0.01f);
                any = true;
            }
            foreach (var r in Renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }
    }
}
