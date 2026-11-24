// Stamps a spherical brush into the shared paint atlas (port of M_PaintFootsteps / M_PaintSplat).
// Geometry is rasterised in its paint-UV layout (atlas space), and each texel tests its own
// world position against the brush, so paint is correct across faces and on scaled objects.
//
// Pass 0 "Stamp": a whole surface mesh (TEXCOORD2 = paint UV), brush from globals. Fallback for
//   non-flat surfaces.
// Pass 1 "StampQuads": batched small quads covering only the texels a brush can reach on a flat
//   face, with the brush passed per vertex, so every stamp of a frame is a single draw.
Shader "Hidden/ColourWays/PaintStamp"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float4 AtlasClip(float2 uv)
        {
            float2 p = uv * 2.0 - 1.0;
            #if UNITY_UV_STARTS_AT_TOP
                p.y = -p.y;
            #endif
            return float4(p, 0.5, 1.0);
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

        // posRadius: xyz world position, w radius. brushNormal: surface normal at the impact.
        // params: x shape, y edge softness, z edge noise, w seed. colour: linear rgba.
        half4 Brush(float3 positionWS, float3 normalWS, float4 posRadius, float3 brushNormal, float4 params, float4 colour)
        {
            float radius = max(posRadius.w, 1e-4);
            float dist = length(positionWS - posRadius.xyz);

            // Cheap reject before the noise: the noisy edge never reaches past this.
            clip(radius * (1.0 + params.z * 0.6) - dist);

            // Noisy edge, frequency relative to brush size so big and small splats look alike.
            float3 q = positionWS / radius + params.w;
            float n = (ValueNoise(q * 2.2) - 0.5) * 0.75 + (ValueNoise(q * 5.3 + 13.1) - 0.5) * 0.35;
            float edge = radius * (1.0 + params.z * n);

            float soft = max(params.y * radius, 1e-4);
            float mask = 1.0 - smoothstep(edge - soft, edge, dist);

            // Only paint faces that face the impact (wraps over edges, never through thin walls).
            mask *= saturate(dot(normalize(normalWS), brushNormal) * 3.0 + 1.0);

            clip(mask - 0.002);
            return half4(colour.rgb, mask * colour.a);
        }
        ENDHLSL

        Pass
        {
            Name "Stamp"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 _CW_BrushPosRadius;
            float4 _CW_BrushNormal;
            float4 _CW_BrushColour;
            float4 _CW_BrushParams;

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
                o.positionCS = AtlasClip(input.paintUV);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return Brush(input.positionWS, input.normalWS, _CW_BrushPosRadius, _CW_BrushNormal.xyz, _CW_BrushParams, _CW_BrushColour);
            }
            ENDHLSL
        }

        Pass
        {
            Name "StampQuads"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float3 positionWS  : POSITION;   // already world space (drawn with an identity matrix)
                float3 normalWS    : NORMAL;
                float2 paintUV     : TEXCOORD0;
                float4 posRadius   : TEXCOORD1;
                float4 brushNormal : TEXCOORD2;
                float4 params      : TEXCOORD3;
                float4 colour      : TEXCOORD4;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                nointerpolation float4 posRadius   : TEXCOORD2;
                nointerpolation float4 brushNormal : TEXCOORD3;
                nointerpolation float4 params      : TEXCOORD4;
                nointerpolation float4 colour      : TEXCOORD5;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = AtlasClip(input.paintUV);
                o.positionWS = input.positionWS;
                o.normalWS = input.normalWS;
                o.posRadius = input.posRadius;
                o.brushNormal = input.brushNormal;
                o.params = input.params;
                o.colour = input.colour;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return Brush(input.positionWS, input.normalWS, input.posRadius, input.brushNormal.xyz, input.params, input.colour);
            }
            ENDHLSL
        }
    }
}
