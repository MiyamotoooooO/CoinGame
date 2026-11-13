// マウスカーソルを合わせた物の形を、白一色で描く（HoverTooltip の輪郭づくり用）。
// 奥行きは見ないで、物の形をそのまま描く。
// Resources フォルダに置いているのは、ビルドにも必ず含めるため。
Shader "Hidden/MedalPusher/HoverMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 vert (float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag () : SV_Target
            {
                return fixed4(1, 1, 1, 1);
            }
            ENDCG
        }
    }
}
