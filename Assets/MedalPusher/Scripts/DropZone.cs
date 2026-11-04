using UnityEngine;

namespace MedalPusher
{
    public enum DropZoneType
    {
        Center, // 中央の穴（ポイント・メダル払い戻し）
        Side,   // 両端の穴（通常0pt）
        Lost    // 想定外の場所に落ちた（安全用）
    }

    /// <summary>
    /// 落下口のトリガー。メダルが入ったらゲームに通知する。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class DropZone : MonoBehaviour
    {
        public DropZoneType type;
        public MedalPusherGame game;

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null) return;
            var medal = rb.GetComponent<Medal>();
            if (medal == null || medal.Consumed) return;
            game.OnMedalDropped(medal, type);
        }
    }
}
