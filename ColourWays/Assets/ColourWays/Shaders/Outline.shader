// Full-screen outline (port of M_Outline). Used by a Full Screen Pass Renderer Feature
// with Depth + Normal requirements. Draws ink lines at depth and normal discontinuities so
// the all-white, shadowless world stays readable.
Shader "ColourWays/Outline"
{
    Properties
    {
        _OutlineColour ("Outline Colour", Color) = (0.12, 0.12, 0.14, 1)
        _Thickness ("Thickness (px at 1080p)", Range(0.5, 4)) = 1.25
        _DepthThreshold ("Depth Threshold", Range(0.005, 1)) = 0.06
        _GrazingBoost ("Grazing Angle Depth Boost", Range(0, 20)) = 6
        _NormalThreshold ("Normal Threshold", Range(0.05, 2)) = 0.5
        _FadeStart ("Fade Start Distance", Float) = 60
        _FadeEnd ("Fade End Distance", Float) = 160
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "Outline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            half4 _OutlineColour;
            float _Thickness;
            float _DepthThreshold;
            float _GrazingBoost;
            float _NormalThreshold;
            float _FadeStart;
            float _FadeEnd;

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float2 texel = 1.0 / _ScreenParams.xy;
                float2 o = texel * max(1.0, _Thickness * _ScreenParams.y / 1080.0);

                float rawDepth = SampleSceneDepth(uv);
                float d0 = LinearEyeDepth(rawDepth, _ZBufferParams);

                // Roberts cross on depth.
                float d1 = EyeDepth(uv + float2( o.x,  o.y));
                float d2 = EyeDepth(uv + float2(-o.x, -o.y));
                float d3 = EyeDepth(uv + float2( o.x, -o.y));
                float d4 = EyeDepth(uv + float2(-o.x,  o.y));

                float3 n0 = SampleSceneNormals(uv);
                float3 n1 = SampleSceneNormals(uv + float2( o.x,  o.y));
                float3 n2 = SampleSceneNormals(uv + float2(-o.x, -o.y));
                float3 n3 = SampleSceneNormals(uv + float2( o.x, -o.y));
                float3 n4 = SampleSceneNormals(uv + float2(-o.x,  o.y));

                // Raise the depth threshold at grazing angles to avoid false lines on long floors.
                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 viewDir = normalize(_WorldSpaceCameraPos - positionWS);
                float grazing = 1.0 - saturate(dot(n0, viewDir));
                float depthThreshold = _DepthThreshold * (1.0 + grazing * _GrazingBoost);

                float depthDelta = (abs(d1 - d2) + abs(d3 - d4)) / max(d0, 1e-3);
                float depthEdge = smoothstep(depthThreshold, depthThreshold * 1.5, depthDelta);

                float normalDelta = length(n1 - n2) + length(n3 - n4);
                float normalEdge = smoothstep(_NormalThreshold, _NormalThreshold * 1.3, normalDelta);

                float edge = saturate(max(depthEdge, normalEdge));
                edge *= 1.0 - saturate((d0 - _FadeStart) / max(_FadeEnd - _FadeStart, 1e-3));

                return half4(lerp(scene.rgb, _OutlineColour.rgb, edge * _OutlineColour.a), scene.a);
            }
            ENDHLSL
        }
    }
}
