using System.Collections;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 盤面のまわりの「見えないかべ」。演出でメダルが盤面の外（モニター・横のスロット・床など）へ飛び散らないようにする。
    /// いつも置いておく（どのレコードの演出でも効く）。
    ///
    /// ・左右：盤面の左右のかべの内側の面（盤面の幅の半分）から外へ、上へのばす
    /// ・奥　：奥のかべの前の面から後ろへ、上へのばす（プッシャーが下をくぐる所はあける）
    /// ・手前：盤面の手前のふちより前の、メダルを降らせる高さより上だけ（ふつうに手前へ落ちるメダルはじゃましない）
    /// ・天井：topY の高さ。ここだけは「すり抜ける箱（トリガー）」にして、上へ飛ぶメダルを下向きにはね返す
    ///   （ふつうの箱にすると、上から床の高さを調べる処理が天井に当たってしまうため）
    /// ・どのかべも、盤面の中にははみ出さないので、ふだんのメダルの動きはじゃましない
    /// ・見た目はない（showFence を入れると、調整用に水色で見える）
    /// ・MedalPusherGame は変えない。シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    /// </summary>
    [DisallowMultipleComponent]
    public class BoardFence : MonoBehaviour
    {
        [Tooltip("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        [Tooltip("見えないかべを置く")]
        public bool fenceEnabled = true;
        [Tooltip("天井の高さ（ワールドの Y）。演出でメダルを出す高さより上にする")]
        public float topY = 2f;
        [Tooltip("左右のかべを置き始める高さ（ワールドの Y）。マイナスなら盤面のかべの高さの半分から")]
        public float sideBottomY = -1f;
        [Tooltip("手前のかべを置く")]
        public bool useFront = true;
        [Tooltip("かべの厚み（メートル）。速いメダルがすり抜けないように厚めにする")]
        [Range(0.05f, 1f)] public float thickness = 0.3f;
        [Tooltip("天井に当たったメダルの、はね返る強さ（上向きの速さに対する割合）")]
        [Range(0f, 1f)] public float ceilingBounce = 0.3f;
        [Tooltip("見えないかべを水色で見えるようにする（調整用。再生する前に入れる）")]
        public bool showFence = false;

        Transform root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<BoardFence>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<BoardFence>();
        }

        IEnumerator Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null) yield break;
            yield return null;   // 盤面の大きさが決まるのを1フレーム待つ
            if (fenceEnabled) Build();
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
        }

        void Build()
        {
            var l = game.layout;
            float d = game.tuning.medalDiameter;
            float hw = game.BoardHalfWidth;
            float t = thickness;
            float front = l.floorFrontZ;
            float back = game.BackWallFrontZ;
            float sky = l.launcherHeight + d * 0.5f;              // メダルを降らせる高さ（RecordScriptBase.SkyY と同じ）
            float top = Mathf.Max(topY, sky + d * 2f);

            root = new GameObject("BoardFence").transform;

            // 左右（手前のふちから奥のかべの後ろまで）
            float sideBottom = sideBottomY >= 0f ? sideBottomY : l.wallHeight * 0.5f;
            float sideH = top - sideBottom;
            float sideZ0 = front, sideZ1 = back + t;
            Box("Left", new Vector3(-hw - t * 0.5f, sideBottom + sideH * 0.5f, (sideZ0 + sideZ1) * 0.5f), new Vector3(t, sideH, sideZ1 - sideZ0), false);
            Box("Right", new Vector3(hw + t * 0.5f, sideBottom + sideH * 0.5f, (sideZ0 + sideZ1) * 0.5f), new Vector3(t, sideH, sideZ1 - sideZ0), false);

            // 奥（プッシャーが下をくぐる所はあける）
            float backBottom = Mathf.Max(sideBottom, l.pusherHeight + l.backWallGap + d);
            float backH = top - backBottom;
            float w = hw * 2f + t * 2f;
            Box("Back", new Vector3(0f, backBottom + backH * 0.5f, back + t * 0.5f), new Vector3(w, backH, t), false);

            // 手前（メダルを降らせる高さより上だけ）
            if (useFront)
            {
                float frontBottom = sky + d;
                float frontH = top - frontBottom;
                if (frontH > 0.01f)
                    Box("Front", new Vector3(0f, frontBottom + frontH * 0.5f, front - t * 0.5f), new Vector3(w, frontH, t), false);
            }

            // 天井（すり抜ける箱。上へ飛ぶメダルを下向きにはね返す）
            float ceilT = Mathf.Max(t, 0.5f);
            var ceiling = Box("Ceiling", new Vector3(0f, top + ceilT * 0.5f, (front - t + back + t) * 0.5f), new Vector3(w, ceilT, back - front + t * 2f), true);
            var c = ceiling.AddComponent<FenceCeiling>();
            c.fence = this;
        }

        GameObject Box(string name, Vector3 center, Vector3 size, bool trigger)
        {
            var go = new GameObject("Fence_" + name);
            go.transform.SetParent(root, false);
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = trigger;
            if (showFence)
            {
                var view = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(view.GetComponent<Collider>());
                view.transform.SetParent(go.transform, false);
                view.transform.localScale = size;
                var r = view.GetComponent<Renderer>();
                r.material.color = trigger ? new Color(1f, 0.6f, 0.2f) : new Color(0.3f, 0.8f, 1f);
            }
            return go;
        }

        /// <summary>天井：入ってきたメダル（動く物）の上向きの速さを、下向きに変える</summary>
        class FenceCeiling : MonoBehaviour
        {
            public BoardFence fence;

            void OnTriggerEnter(Collider other) => Bounce(other);
            void OnTriggerStay(Collider other) => Bounce(other);

            void Bounce(Collider other)
            {
                var rb = other.attachedRigidbody;
                if (rb == null || rb.isKinematic) return;
                var v = rb.velocity;
                if (v.y <= 0f) return;
                v.y = -v.y * (fence != null ? fence.ceilingBounce : 0.3f);
                rb.velocity = v;
            }
        }
    }
}
