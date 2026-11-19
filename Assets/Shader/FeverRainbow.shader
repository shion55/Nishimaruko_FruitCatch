
Shader "Custom/FeverRainbow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Bands ("Rainbow Bands", Range(0.5,10)) = 2
        _Speed ("Scroll Speed", Range(-5,5)) = 0.5
        _Angle ("Stripe Angle", Range(0,360)) = 45
        _Strength ("Rainbow Strength", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Bands;
                float _Speed;
                float _Angle;
                float _Strength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            // HSVのHueを鮮やかなRGBへ変換
            float3 HueToRGB(float hue)
            {
                float3 rgb = abs(
                    frac(hue + float3(0.0, 2.0/3.0, 1.0/3.0))
                    * 6.0 - 3.0
                ) - 1.0;

                return saturate(rgb);
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(
                    _MainTex, sampler_MainTex, input.uv
                );

                // 斜め方向は固定
                float angle = radians(_Angle);

                float2 direction = float2(
                    cos(angle),
                    sin(angle)
                );

                // 一方向に流れる色相
                float phase = dot(
                    input.uv - 0.5, direction
                ) * _Bands - _Time.y * _Speed;

                // 0〜1の色相を繰り返す
                float hue = frac(phase);

                float3 rainbow = HueToRGB(hue);

                // 元Spriteの陰影を少しだけ残す
                float brightness = dot(
                    tex.rgb,
                    float3(0.299, 0.587, 0.114)
                );

                rainbow *= lerp(
                    1.0, brightness, 0.15
                );

                float3 finalRGB = lerp(
                    tex.rgb,
                    rainbow,
                    _Strength
                );

                return half4(
                    finalRGB * input.color.rgb * _Color.rgb,
                    tex.a * input.color.a * _Color.a
                );
            }
            ENDHLSL
        }
    }
}
