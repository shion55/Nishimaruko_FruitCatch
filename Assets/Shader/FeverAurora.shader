Shader "FruitCatch/FeverAurora"
{
    Properties
    {
        [PerRendererData] _AuroraAlpha ("Opacity", Range(0, 1)) = 0
        _Rose ("Rose", Color) = (1, 0.30, 0.59, 1)
        _Gold ("Gold", Color) = (1, 0.86, 0.29, 1)
        _Lime ("Lime", Color) = (0.57, 1, 0.32, 1)
        _Mint ("Mint", Color) = (0.24, 0.98, 0.79, 1)
        _Sky ("Sky Blue", Color) = (0.23, 0.70, 1, 1)
        _Violet ("Blue Violet", Color) = (0.47, 0.42, 1, 1)
        _FlowSpeed ("Flow Speed (Cycles per Second)", Range(0, 0.6)) = 0.27
        _LightStrength ("Light Strength", Range(0, 1)) = 0.88
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Rose, _Gold, _Lime, _Mint, _Sky, _Violet;
                float _FlowSpeed, _LightStrength, _AuroraAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half3 Rainbow(float phase)
            {
                float3 offsets = float3(0.0, 1.0 / 6.0, 2.0 / 6.0);
                float3 warm = saturate(1.0 - abs(frac(phase + 0.5 - offsets) - 0.5) * 6.0);
                float3 cool = saturate(1.0 - abs(frac(phase - offsets) - 0.5) * 6.0);
                warm = warm * warm * (3.0 - 2.0 * warm);
                cool = cool * cool * (3.0 - 2.0 * cool);
                return warm.x * _Rose.rgb + warm.y * _Gold.rgb + warm.z * _Lime.rgb
                    + cool.x * _Mint.rgb + cool.y * _Sky.rgb + cool.z * _Violet.rgb;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float cycle = frac(_Time.y * _FlowSpeed);
                float angle = cycle * TWO_PI;
                float turnSin, turnCos;
                sincos(angle, turnSin, turnCos);
                float2 p = input.uv - 0.5;
                float2 flow = float2(p.x * turnCos + p.y * turnSin, p.y * turnCos - p.x * turnSin);
                float sweep = flow.x + 0.19 * sin(flow.y * 4.8 + angle) - 0.12 * flow.y * flow.y;
                float phase = sweep * 0.92 + flow.y * 0.56 - cycle;

                // Broad light passes travel at different angles through the rotating color field.
                float primary = 0.5 + 0.5 * cos(TWO_PI * (sweep * 0.72 - flow.y * 0.22 - cycle + 0.17));
                float secondary = 0.5 + 0.5 * cos(TWO_PI * (flow.y * 0.8 + sweep * 0.34 + cycle + 0.38));
                float light = _LightStrength * (0.72 * smoothstep(0.30, 0.96, primary)
                    + 0.28 * smoothstep(0.55, 0.98, secondary));
                half3 bright = lerp(Rainbow(phase + 0.055), half3(1, 0.97, 1), 0.36);
                half3 tint = lerp(Rainbow(phase), bright, light * 0.65);
                float opacity = lerp(0.50, 0.86, light);
                return half4(tint, opacity * _AuroraAlpha);
            }
            ENDHLSL
        }
    }
}
