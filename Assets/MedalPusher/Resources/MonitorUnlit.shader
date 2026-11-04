// 筐体のモニターの画面用。ライトの影響を受けず、映像をそのままの明るさで映す。
// Resources フォルダに置いているのは、ビルドにも必ず含めるため。
Shader "Hidden/MedalPusher/MonitorUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
}
