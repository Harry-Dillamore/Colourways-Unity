// Stamps a spherical brush into the shared paint atlas (port of M_PaintFootsteps / M_PaintSplat).
// The mesh is rasterised in its paint-UV layout (TEXCOORD2), and each texel tests its own
// world position against the brush, so paint is correct across faces and on scaled objects.
Shader "Hidden/ColourWays/PaintStamp"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Stamp"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _CW_BrushPosRadius; // xyz world position, w radius
            float4 _CW_BrushNormal;    // xyz surface normal at the impact
            float4 _CW_BrushColour;    // linear colour
            float4 _CW_BrushParams;    // x shape, y edge softness, z edge noise, w seed

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 paintUV    : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float2 p = input.paintUV * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                    p.y = -p.y;
                #endif
                o.positionCS = float4(p, 0.5, 1.0);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float ValueNoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(Hash31(i + float3(0, 0, 0)), Hash31(i + float3(1, 0, 0)), f.x),
                         lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), f.x),
                         lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), f.x), f.y),
                    f.z);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float radius = max(_CW_BrushPosRadius.w, 1e-4);
                float3 toTexel = input.positionWS - _CW_BrushPosRadius.xyz;
                float dist = length(toTexel);

                // Noisy edge, frequency relative to brush size so big and small splats look alike.
                float3 q = input.positionWS / radius + _CW_BrushParams.w;
                float n = (ValueNoise(q * 2.2) - 0.5) * 0.75 + (ValueNoise(q * 5.3 + 13.1) - 0.5) * 0.35;
                float edge = radius * (1.0 + _CW_BrushParams.z * n);

                float soft = max(_CW_BrushParams.y * radius, 1e-4);
                float mask = 1.0 - smoothstep(edge - soft, edge, dist);

                // Only paint faces that face the impact (wraps over edges, never through thin walls).
                float facing = saturate(dot(normalize(input.normalWS), _CW_BrushNormal.xyz) * 3.0 + 1.0);
                mask *= facing;

                clip(mask - 0.002);
                return half4(_CW_BrushColour.rgb, mask * _CW_BrushColour.a);
            }
            ENDHLSL
        }
    }
}
