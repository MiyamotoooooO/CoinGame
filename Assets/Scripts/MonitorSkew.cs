using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// UI の形を斜めにする（平行四辺形のタグや、斜体っぽい文字に使う）。
    /// Image や Text に付けると、上の辺を右へ・下の辺を左へずらす。
    /// skew は「高さ 1 あたり、横にどれだけずらすか」。0.35 くらいでタグ、0.12 くらいで文字が少し傾く。
    /// </summary>
    [DisallowMultipleComponent]
    public class MonitorSkew : BaseMeshEffect
    {
        public float skew = 0.25f;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.position.x += v.position.y * skew;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
