// レコードの大きな演出（モデル・粒）に、ゲーム画面と同じドットのフィルターをかける。
// RecordSpectacleDirector が、モデルを描いた絵（すけている所あり）を画面に重ねる時に使う。
// ArcadeScreen.shader と同じ順番（ふちどり → 彩度 → 減色とディザ → 走査線）で、すけている所はすけたまま重ねる。
// さらに、モデルの外側に1ドットの輪郭を付ける（ドット絵らしく見える）。
//
// 置き場所が Resources なので、ビルドにも必ず入る。
Shader "MedalGame/SpectacleDots"
{
    Properties
    {
        [PerRendererData] _MainTex ("演出の絵（スクリプトが自動で入れる）", 2D) = "black" {}
        _DotCount ("ドットの数（横・縦。スクリプトが入れる）", Vector) = (480, 270, 0, 0)
        _ColorLevels ("色の段階数", Range(2, 64)) = 20
        _DitherStrength ("ディザの強さ", Range(0, 1)) = 0.6
        _Saturation ("彩度", Range(0, 2)) = 1.15
        _OutlineStrength ("内側のふちどりの濃さ", Range(0, 1)) = 0.55
        _OutlineThreshold ("ふちどりが出る明るさの差", Range(0.01, 1)) = 0.16
        _OutlineColor ("ふちどりの色", Color) = (0.12, 0.06, 0.05, 1)
        _SilhouetteStrength ("外側の輪郭の濃さ", Range(0, 1)) = 0.85
        _ScanlineStrength ("走査線の濃さ", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            sampler2D _MainTex;
            float4 _DotCount;
            float _ColorLevels, _DitherStrength, _Saturation, _OutlineStrength, _OutlineThreshold;
            float4 _OutlineColor;
            float _SilhouetteStrength, _ScanlineStrength;

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
                o.color = v.color;
                return o;
            }

            // 色（乗算済み）を、すけ具合で割って、ガンマの空間で取り出す
            float4 Fetch(float2 uv)
            {
                float4 c = tex2D(_MainTex, uv);
                float3 rgb = c.a > 0.001 ? c.rgb / c.a : c.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                rgb = LinearToGammaSpace(saturate(rgb));
                #endif
                return float4(saturate(rgb), saturate(c.a));
            }

            float Luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

            float4 frag(v2f i) : SV_Target
            {
                float2 dots = max(_DotCount.xy, float2(1, 1));
                float2 texel = 1.0 / dots;
                float2 px = floor(i.uv * dots);
                float2 center = (px + 0.5) * texel;

                float4 c = Fetch(center);
                float4 r = Fetch(center + float2(texel.x, 0));
                float4 l = Fetch(center - float2(texel.x, 0));
                float4 u = Fetch(center + float2(0, texel.y));
                float4 d = Fetch(center - float2(0, texel.y));

                // 外側の輪郭：自分がすけていて、となりに物がある所に1ドットの輪郭
                float neighbor = max(max(r.a, l.a), max(u.a, d.a));
                if (c.a < 0.35 && neighbor > 0.6)
                {
                    float a = _SilhouetteStrength * i.color.a;
                    float3 oc = _OutlineColor.rgb;
                    #ifndef UNITY_COLORSPACE_GAMMA
                    oc = GammaToLinearSpace(oc);
                    #endif
                    return float4(oc * a, a);
                }
                if (c.a <= 0.003) return float4(0, 0, 0, 0);

                float3 col = c.rgb;
                // 内側のふちどり（明るさの差）
                float lum = Luma(col);
                float brightest = max(max(Luma(r.rgb) * step(0.5, r.a), Luma(l.rgb) * step(0.5, l.a)),
                                      max(Luma(u.rgb) * step(0.5, u.a), Luma(d.rgb) * step(0.5, d.a)));
                float outline = step(_OutlineThreshold, brightest - lum) * _OutlineStrength * step(0.5, c.a);
                col = lerp(col, _OutlineColor.rgb, outline);

                // 彩度
                col = lerp(Luma(col).xxx, col, _Saturation);

                // 減色とディザ（すけ具合も段階に丸める）
                int index = (int)fmod(px.x, 4.0) + (int)fmod(px.y, 4.0) * 4;
                float dither = (BAYER[index] + 0.5) / 16.0 - 0.5;
                float steps = max(_ColorLevels - 1.0, 1.0);
                col = floor(saturate(col) * steps + 0.5 + dither * _DitherStrength) / steps;
                float alpha = saturate(floor(c.a * 6.0 + 0.5 + dither * 0.8) / 6.0);

                // 走査線
                float row = frac(i.uv.y * dots.y);
                col *= 1.0 - _ScanlineStrength * (0.5 - 0.5 * cos(row * 6.2831853));

                col = saturate(col);
                #ifndef UNITY_COLORSPACE_GAMMA
                col = GammaToLinearSpace(col);
                #endif
                alpha *= i.color.a;
                return float4(col * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
