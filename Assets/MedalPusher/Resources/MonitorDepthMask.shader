// 物の「奥行きだけ」を描く（色は描かない）。モニターの中の 3D のボスで、壁より下の体をかくすのに使う。
// Resources フォルダに置いているのは、ビルドにも必ず含めるため。
Shader "Hidden/MedalPusher/MonitorDepthMask"
{
    SubShader
    {
        // ほかの物より先に描く
        Tags { "Queue" = "Geometry-10" "RenderType" = "Opaque" }

        Pass
        {
            ColorMask 0
            ZWrite On
            Cull Off

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
                return 0;
            }
            ENDCG
        }
    }
}
