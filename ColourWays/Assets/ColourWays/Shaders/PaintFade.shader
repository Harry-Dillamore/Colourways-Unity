// Fades every painted texel in the paint atlas by a fixed amount (linear fade, so paint really
// reaches zero instead of lingering at low alpha like a multiplicative fade would).
// Drawn as one procedural full-screen triangle into the atlas by PaintManager.
Shader "Hidden/ColourWays/PaintFade"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Fade"
            ZTest Always
            ZWrite Off
            Cull Off
            ColorMask A
            BlendOp RevSub
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _CW_FadeAmount;

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(uint id : SV_VertexID)
            {
                Varyings o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.positionCS = float4(uv * 2.0 - 1.0, 0.5, 1.0);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(0, 0, 0, _CW_FadeAmount);
            }
            ENDHLSL
        }
    }
}
