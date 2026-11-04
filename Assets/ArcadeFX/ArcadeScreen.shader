// アーケード風の画面効果。ArcadeCamera が低解像度で描いたゲーム画面を、画面いっぱいに貼るときに使う。
// UI 用のシェーダーなので、URP でも標準のレンダーパイプラインでも動く。
//
// かける順番：画面の湾曲 → 色ずれ → ふちどり → 彩度 → 減色とディザ → 走査線 → 周辺減光
Shader "MedalGame/ArcadeScreen"
{
    Properties
    {
        [PerRendererData] _MainTex ("ゲーム画面（スクリプトが自動で入れる）", 2D) = "black" {}

        [Header(Color)]
        _ColorLevels ("色の段階数（小さいほど色数が減る）", Range(2, 64)) = 20
        _DitherStrength ("ディザの強さ", Range(0, 1)) = 0.6
        _Saturation ("彩度", Range(0, 2)) = 1.1

        [Header(Outline)]
        _OutlineStrength ("ふちどりの濃さ", Range(0, 1)) = 0.55
        _OutlineThreshold ("ふちどりが出る明るさの差", Range(0.01, 1)) = 0.16
        _OutlineColor ("ふちどりの色", Color) = (0.12, 0.06, 0.05, 1)

        [Header(CRT)]
        _ScanlineStrength ("走査線の濃さ", Range(0, 1)) = 0.18
        _ChromaticOffset ("色ずれの量（ドット）", Range(0, 2)) = 0.5
        _VignetteStrength ("周辺減光の強さ", Range(0, 1)) = 0.35
        _Curvature ("画面の湾曲", Range(0, 0.3)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "False"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;   // x,y = 1ドットの大きさ（UV） / z,w = 横と縦のドット数

            float _ColorLevels;
            float _DitherStrength;
            float _Saturation;
            float _OutlineStrength;
            float _OutlineThreshold;
            float4 _OutlineColor;
            float _ScanlineStrength;
            float _ChromaticOffset;
            float _VignetteStrength;
            float _Curvature;

            // 4x4 のディザの並び（Bayer 行列）
            static const float BAYER[16] =
            {
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // ゲーム画面の色を、人の目に近い明るさの空間（ガンマ）で取り出す
            float3 Fetch(float2 uv)
            {
                float3 c = tex2D(_MainTex, uv).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                c = LinearToGammaSpace(c);
                #endif
                return saturate(c);
            }

            float Luma(float3 c)
            {
                return dot(c, float3(0.299, 0.587, 0.114));
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // 画面の湾曲（ブラウン管のふくらみ）。0 なら何もしない
                if (_Curvature > 0.0)
                {
                    float2 c = uv * 2.0 - 1.0;
                    c *= 1.0 + _Curvature * dot(c, c);
                    uv = c * 0.5 + 0.5;
                    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    {
                        return float4(0, 0, 0, 1);
                    }
                }

                float2 texel = _MainTex_TexelSize.xy;
                float2 px = floor(uv * _MainTex_TexelSize.zw);   // 何番目のドットか
                float2 center = (px + 0.5) * texel;               // そのドットの中心

                // 色ずれ：画面の端ほど赤と青が左右にずれる
                float side = (uv.x - 0.5) * 2.0;
                float2 shift = float2(texel.x * _ChromaticOffset * side, 0.0);
                float3 col;
                col.r = Fetch(center + shift).r;
                col.g = Fetch(center).g;
                col.b = Fetch(center - shift).b;

                // ふちどり：上下左右のドットより暗い側のドットに、輪郭の色をのせる（1ドット幅になる）
                float l = Luma(col);
                float brightest = max(
                    max(Luma(Fetch(center + float2(texel.x, 0.0))), Luma(Fetch(center - float2(texel.x, 0.0)))),
                    max(Luma(Fetch(center + float2(0.0, texel.y))), Luma(Fetch(center - float2(0.0, texel.y)))));
                float outline = step(_OutlineThreshold, brightest - l) * _OutlineStrength;
                col = lerp(col, _OutlineColor.rgb, outline);

                // 彩度
                col = lerp(Luma(col).xxx, col, _Saturation);

                // 減色とディザ：ドットの位置ごとに少しずらしてから色の段階に丸める
                int index = (int)fmod(px.x, 4.0) + (int)fmod(px.y, 4.0) * 4;
                float dither = (BAYER[index] + 0.5) / 16.0 - 0.5;
                float steps = max(_ColorLevels - 1.0, 1.0);
                col = floor(saturate(col) * steps + 0.5 + dither * _DitherStrength) / steps;

                // 走査線：ドット1段ごとに、下半分を少し暗くする
                float row = frac(uv.y * _MainTex_TexelSize.w);
                float scan = 1.0 - _ScanlineStrength * (0.5 - 0.5 * cos(row * 6.2831853));
                col *= scan;

                // 周辺減光：四隅ほど暗くする
                float2 d = uv - 0.5;
                float vignette = 1.0 - _VignetteStrength * smoothstep(0.15, 0.55, dot(d, d) * 1.6);
                col *= vignette;

                col = saturate(col);
                #ifndef UNITY_COLORSPACE_GAMMA
                col = GammaToLinearSpace(col);
                #endif
                return float4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
