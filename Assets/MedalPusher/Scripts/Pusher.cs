using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 前後に往復するプッシャー。キネマティックRigidbodyを MovePosition で動かすことで、
    /// 上に乗ったメダルを摩擦で運び、前面でメダルを押し出す。
    /// 動きは (1-cos)/2 のなめらかな往復（端で減速する）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Pusher : MonoBehaviour
    {
        [Tooltip("1往復の秒数")]
        public float cycleSeconds = 4f;
        [Tooltip("前に出る距離")]
        public float stroke = 2f;
        [Tooltip("速度倍率（テクノ等のレコード効果用）")]
        public float speedMultiplier = 1f;
        [Tooltip("停止（メタル終了後の停止等に使う）")]
        public bool paused;

        Rigidbody rb;
        float retractFrontZ;
        float depth;
        float y;
        float currentStroke;
        float phase;

        /// <summary>0=一番奥、0.5=一番手前</summary>
        public float Phase01 => phase - Mathf.Floor(phase);

        public void Init(float retractFrontZ, float depth, float centerY, float stroke, float cycleSeconds)
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            this.retractFrontZ = retractFrontZ;
            this.depth = depth;
            y = centerY;
            this.stroke = stroke;
            this.cycleSeconds = cycleSeconds;
            ResetPhase();
        }

        public void ResetPhase()
        {
            phase = 0f;
            currentStroke = stroke;
            Vector3 p = ComputePosition();
            transform.position = p;
            if (rb != null) rb.position = p;
        }

        void FixedUpdate()
        {
            if (rb == null) return;
            float dt = Time.fixedDeltaTime;
            if (!paused) phase += dt * speedMultiplier / Mathf.Max(0.1f, cycleSeconds);
            // ストローク変更時に位置が瞬間移動するとメダルが弾け飛ぶので、ゆっくり追従させる
            currentStroke = Mathf.MoveTowards(currentStroke, stroke, dt * 0.5f);
            rb.MovePosition(ComputePosition());
        }

        Vector3 ComputePosition()
        {
            float t = (1f - Mathf.Cos(phase * Mathf.PI * 2f)) * 0.5f;
            float frontZ = retractFrontZ - currentStroke * t;
            return new Vector3(0f, y, frontZ + depth * 0.5f);
        }
    }
}
