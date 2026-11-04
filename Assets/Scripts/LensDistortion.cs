using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// カメラの映像に軽いたる型のゆがみ（弱い魚眼）をかける。Built-in レンダーパイプライン用。
    /// MedalPusherGame がカメラに自動で付ける。strength が 0 なら何もしない。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class LensDistortion : MonoBehaviour
    {
        [Range(0f, 1f)] public float strength = 0.3f;

        Material material;
        static readonly int StrengthId = Shader.PropertyToID("_Strength");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (strength <= 0f || !EnsureMaterial())
            {
                Graphics.Blit(src, dst);
                return;
            }
            material.SetFloat(StrengthId, strength);
            material.SetFloat(AspectId, (float)src.width / Mathf.Max(1, src.height));
            Graphics.Blit(src, dst, material);
        }

        bool EnsureMaterial()
        {
            if (material != null) return true;
            var shader = Shader.Find("Hidden/MedalPusher/LensDistortion");
            if (shader == null || !shader.isSupported) return false;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
