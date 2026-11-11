Shader "Custom/PaintSplatter"
{
    Properties
    {
        _MainTex ("Splatter Texture", 2D) = "white" {}
        _DepthThreshold ("Edge Clip Sensitivity", Range(0.01, 1.0)) = 0.2
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
        }

        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
                float4 screenPos    : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _DepthThreshold;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                
                output.positionCS = vertexInput.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                output.screenPos = ComputeScreenPos(vertexInput.positionCS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Calculate Normalized Screen Coordinates for Depth Lookup
                float2 screenUV = input.screenPos.xy / input.screenPos.w;

                // Sample the scene's actual depth buffer behind this pixel
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneLinearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                // Calculate the decal quad fragment's depth
                float decalLinearDepth = input.screenPos.w;

                // Edge Overhang Check: If the difference between the quad and the actual background 
                // geometry is too large, the quad is hanging off into empty space / further background.
                float depthDifference = abs(sceneLinearDepth - decalLinearDepth);
                clip(_DepthThreshold - depthDifference);

                // Normal texture and color blending
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 col = input.color;
                col.a *= tex.a;

                return col;
            }
            ENDHLSL
        }
    }

    // Fallback for non-URP or standard preview
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 col = i.color;
                col.a *= tex.a;
                return col;
            }
            ENDCG
        }
    }
}