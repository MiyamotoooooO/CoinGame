// 画面の中央を少し大きく、端を少し縮める「たる型」のゆがみ（魚眼の弱い版）。
// 四隅がちょうど画面の端に来るように拡大するので、黒い縁は出ない。
// Resources フォルダに置いているのは、ビルドにも必ず含めるため。
Shader "Hidden/MedalPusher/LensDistortion"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Strength; // 0 でゆがみなし。0.3 前後で軽い魚眼
            float _Aspect;   // 画面の横 / 縦

            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 d = i.uv - 0.5;
                d.x *= _Aspect;
                float r2 = dot(d, d);
                float cornerR2 = 0.25 * (_Aspect * _Aspect + 1.0);
                d *= (1.0 + _Strength * r2) / (1.0 + _Strength * cornerR2);
                d.x /= _Aspect;
                return tex2D(_MainTex, d + 0.5);
            }
            ENDCG
        }
    }
}
