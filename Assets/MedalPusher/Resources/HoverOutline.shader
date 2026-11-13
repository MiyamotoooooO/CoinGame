// HoverMask で描いた白い形のまわり1マスだけを、輪郭の色でぬる（HoverTooltip 用）。
// 形の中と、形から離れた所は透明。色には透明度を掛けておく（乗算済みアルファで重ねるため）。
// Resources フォルダに置いているのは、ビルドにも必ず含めるため。
Shader "Hidden/MedalPusher/HoverOutline"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "black" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;

            fixed4 frag (v2f_img i) : SV_Target
            {
                if (tex2D(_MainTex, i.uv).r > 0.5) return fixed4(0, 0, 0, 0);   // 形の中は透明
                float2 t = _MainTex_TexelSize.xy;
                float n = 0;
                n = max(n, tex2D(_MainTex, i.uv + float2( t.x, 0)).r);
                n = max(n, tex2D(_MainTex, i.uv + float2(-t.x, 0)).r);
                n = max(n, tex2D(_MainTex, i.uv + float2(0,  t.y)).r);
                n = max(n, tex2D(_MainTex, i.uv + float2(0, -t.y)).r);
                n = max(n, tex2D(_MainTex, i.uv + float2( t.x,  t.y)).r);
                n = max(n, tex2D(_MainTex, i.uv + float2(-t.x,  t.y)).r);
                n = max(n, tex2D(_MainTex, i.uv + float2( t.x, -t.y)).r);
                n = max(n, tex2D(_MainTex, i.uv + float2(-t.x, -t.y)).r);
                if (n < 0.5) return fixed4(0, 0, 0, 0);
                return fixed4(_Color.rgb * _Color.a, _Color.a);
            }
            ENDCG
        }
    }
}
