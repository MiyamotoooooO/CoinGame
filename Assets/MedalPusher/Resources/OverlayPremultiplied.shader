// モニターとパーティクルを描いた映像（MonitorOverlayCamera）を、ゲーム画面の上に重ねる用。
// 映像は透明な黒の上に描いているので、色に透明度がもう掛かっている。そのため「乗算済みアルファ」で重ねる
// （普通の重ね方だと、半透明のパーティクルが暗く・薄くなる）。
// Resources フォルダに置いているのは、ビルドにも必ず含めるため。
Shader "Hidden/MedalPusher/OverlayPremultiplied"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                return saturate(c) * i.color.a;
            }
            ENDCG
        }
    }
}
