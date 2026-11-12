// Placement grid overlay. Drawn on top of everything (ZTest Always) at floor height so it never
// reveals walls or pits. Dashed lines + hovered-cell highlight. Expects a quad lying flat.
Shader "ColourWays/Grid"
{
    Properties
    {
        _LineColor ("Line Colour", Color) = (0.55, 0.55, 0.62, 0.55)
        _BorderColor ("Border Colour", Color) = (0.25, 0.25, 0.3, 0.85)
        _LineWidth ("Line Width (cells)", Range(0.002, 0.1)) = 0.018
        _DashCount ("Dashes Per Cell", Range(1, 12)) = 5
        _DashFill ("Dash Fill", Range(0.1, 1)) = 0.55
        _ValidColor ("Hover Valid", Color) = (0.2, 0.55, 1, 0.35)
        _InvalidColor ("Hover Invalid", Color) = (1, 0.3, 0.3, 0.35)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Grid"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LineColor;
                half4 _BorderColor;
                float _LineWidth;
                float _DashCount;
                float _DashFill;
                half4 _ValidColor;
                half4 _InvalidColor;
            CBUFFER_END

            // Per-renderer (MaterialPropertyBlock): origin xz + size, hover cell xy + state.
            float4 _GridOrigin;
            float4 _Hover;
            float _Fade; // 0 = hidden (default), 1 = fully shown; driven by GridOverlay

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float LineMask(float coord, float width)
            {
                float d = abs(frac(coord + 0.5) - 0.5);
                float aa = max(fwidth(coord), 1e-4);
                return 1.0 - smoothstep(width - aa, width + aa, d);
            }

            float Dash(float along)
            {
                float f = frac(along * _DashCount);
                float aa = max(fwidth(along * _DashCount), 1e-4);
                return 1.0 - smoothstep(_DashFill - aa, _DashFill + aa, f);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 g = input.positionWS.xz - _GridOrigin.xy;
                float2 size = _GridOrigin.zw;
                float margin = _LineWidth * 2.0;
                float inside = step(-margin, g.x) * step(-margin, g.y) * step(g.x, size.x + margin) * step(g.y, size.y + margin);

                // Interior dashed lines.
                float vertical = LineMask(g.x, _LineWidth) * Dash(g.y);
                float horizontal = LineMask(g.y, _LineWidth) * Dash(g.x);
                half4 colour = half4(_LineColor.rgb, _LineColor.a * max(vertical, horizontal));

                // Solid outer border.
                float2 edge = min(g, size - g);
                float edgeDist = abs(min(edge.x, edge.y));
                float aa = max(fwidth(g.x), 1e-4);
                float border = 1.0 - smoothstep(_LineWidth * 1.5 - aa, _LineWidth * 1.5 + aa, edgeDist);
                colour = lerp(colour, _BorderColor, border * _BorderColor.a);

                // Hovered cell.
                if (_Hover.z > 0.5)
                {
                    float2 cell = floor(g);
                    if (all(cell == _Hover.xy))
                    {
                        half4 h = _Hover.z < 1.5 ? _ValidColor : _InvalidColor;
                        float2 local = g - cell;
                        float inset = min(min(local.x, 1.0 - local.x), min(local.y, 1.0 - local.y));
                        float ring = 1.0 - smoothstep(0.05, 0.05 + aa, inset);
                        half a = saturate(h.a + ring * 0.5);
                        colour.rgb = lerp(colour.rgb, h.rgb, a);
                        colour.a = max(colour.a, a);
                    }
                }

                colour.a *= inside * saturate(_Fade);
                return colour;
            }
            ENDHLSL
        }
    }
}
