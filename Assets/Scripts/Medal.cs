using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// メダル1枚。軽さを優先して Update や OnCollision は持たせない。
    /// </summary>
    public class Medal : MonoBehaviour
    {
        public Rigidbody Body { get; private set; }
        public Collider Collider { get; private set; }

        /// <summary>プール内の active リストでの位置。-1 なら未使用。</summary>
        public int PoolIndex { get; internal set; } = -1;
        public bool IsActiveInPool => PoolIndex >= 0;

        /// <summary>落下判定済み（二重カウント防止）。</summary>
        public bool Consumed { get; internal set; }

        /// <summary>プレイヤーが投入したメダルか（初期配置・テスト追加は false）。</summary>
        public bool WasInserted { get; private set; }
        public float InsertTime { get; private set; }

        private void Awake()
        {
            // アタッチされているオブジェクトのタグを自動的に "Coin" に設定する
            gameObject.tag = "Coin";
        }

        internal void Setup(Rigidbody body, Collider collider)
        {
            Body = body;
            Collider = collider;
        }

        internal void OnSpawn(int poolIndex)
        {
            PoolIndex = poolIndex;
            Consumed = false;
            WasInserted = false;
            InsertTime = 0f;
        }

        public void MarkInserted(float time)
        {
            WasInserted = true;
            InsertTime = time;
        }
    }
}