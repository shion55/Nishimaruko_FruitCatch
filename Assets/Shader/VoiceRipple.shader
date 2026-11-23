Shader "Custom/VoiceRipple"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _FeverTint ("Fever Tint", Color) = (1, 0.68, 0.2, 0)
        [HideInInspector] _RippleSettings ("Speed, Width, Distortion", Vector) = (6, 0.18, 0.035, 0)
        [HideInInspector] _RippleLifetime ("Lifetime", Float) = 0.75
        [HideInInspector] _RippleColor ("Ring Color", Color) = (0.7, 0.95, 1, 0.12)
        [HideInInspector] _SpriteUVRect ("Sprite UV Rect", Vector) = (0, 0, 1, 1)
        [HideInInspector] _UVPerLocalUnit ("UV Per Local Unit", Vector) = (1, 1, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
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
                float2 positionWS : TEXCOORD1;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _FeverTint;
                float4 _RippleSettings;
                float _RippleLifetime;
                float4 _RippleColor;
                float4 _SpriteUVRect;
                float4 _UVPerLocalUnit;
                float4 _Ripples[8]; // xy: world center, z: age (seconds), w: captured strength
                float4 _RippleSizes[8]; // x: radius multiplier captured from voice volume
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.positionWS = world.xy;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 offsetWS = 0;
                float highlight = 0;
                float width = max(_RippleSettings.y, 0.01);

                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float4 ripple = _Ripples[i];
                    float2 delta = input.positionWS - ripple.xy;
                    float distanceFromCenter = length(delta);
                    float radius = ripple.z * _RippleSettings.x * max(1.0, _RippleSizes[i].x);
                    float bandPosition = (distanceFromCenter - radius) / width;
                    float band = 1.0 - smoothstep(0.0, 1.0, abs(bandPosition));
                    float remaining = saturate(1.0 - ripple.z / max(_RippleLifetime, 0.1));
                    float envelope = ripple.w * remaining * remaining * saturate(ripple.z / 0.04);

                    // 円周の内外で逆方向にずらし、屈折の山と谷を作る。
                    float displacement = sin(clamp(bandPosition, -1.0, 1.0) * PI) * band;
                    offsetWS += delta / max(distanceFromCenter, 0.0001) * displacement * envelope;
                    highlight += band * envelope;
                }

                // 波が重なっても過度に歪ませない。
                offsetWS *= _RippleSettings.z / max(1.0, length(offsetWS));
                float2 localOffset = mul((float3x3)GetWorldToObjectMatrix(), float3(offsetWS, 0)).xy;
                float2 offsetUV = localOffset * _UVPerLocalUnit.xy;
                float2 spriteUV = (input.uv - _SpriteUVRect.xy) / max(_SpriteUVRect.zw, 0.0001);
                float2 edge = min(spriteUV, 1.0 - spriteUV);
                float edgeFade = smoothstep(0.0, 0.02, min(edge.x, edge.y));
                float2 uv = clamp(input.uv + offsetUV * edgeFade,
                    _SpriteUVRect.xy, _SpriteUVRect.xy + _SpriteUVRect.zw);

                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * input.color * _Color;
                color.rgb = lerp(color.rgb, _RippleColor.rgb,
                    saturate(highlight * _RippleColor.a * edgeFade));
                color.rgb = lerp(color.rgb, _FeverTint.rgb, saturate(_FeverTint.a));
                return color;
            }
            ENDHLSL
        }
    }
}
