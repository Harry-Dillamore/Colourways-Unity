// ColourWays surface shader.
// Comic halftone look: screen-space dots (and diagonal hatching in deep shade) driven by a fixed
// light direction, optional world tile lines, plus an optional inverted-hull ink outline.
// Unlit, no shadows, cheap for web.
//
// Paintable mode: painted texels get halftone in the paint colour. With "Hide Unpainted" the
// unpainted parts are discarded entirely, so hidden geometry is invisible over ANY background
// and only appears where dudes have painted it.
Shader "ColourWays/Surface"
{
    Properties
    {
        _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)
        [Toggle(_PAINTABLE)] _Paintable ("Paintable (use with PaintableSurface)", Float) = 0
        [Toggle(_HIDE_UNPAINTED)] _HideUnpainted ("Hide Unpainted (invisible until painted)", Float) = 0

        [Header(Halftone)]
        [Toggle(_HALFTONE)] _Halftone ("Halftone", Float) = 1
        _LightDir ("Light Direction (world)", Vector) = (-0.35, 0.85, -0.15, 0)
        _ShadeAmount ("Shade Amount", Range(0, 1.5)) = 1
        _ShadeThreshold ("Shade Threshold (keeps lit tops clean)", Range(0, 0.6)) = 0.12
        _LightMix ("Lit Colour Strength", Range(0, 1)) = 1
        _DotShade ("Dot Colour Multiplier", Range(0, 1.5)) = 0.7
        _DotColor ("Dot Tint (alpha = amount)", Color) = (0, 0, 0, 0)
        _DotScale ("Dot Spacing (px at 1080p)", Range(2, 24)) = 5
        _HatchStart ("Hatching Starts At Darkness", Range(0, 1)) = 0.75

        [Header(Tile Lines)]
        _TileLines ("Tile Line Strength", Range(0, 1)) = 0
        _TileSize ("Tile Size (world)", Float) = 1
        _TileWidth ("Tile Line Width", Range(0.002, 0.1)) = 0.012

        [Header(Unpainted Layout)]
        [Toggle] _UnpaintedFlat ("Unpainted Is Flat (pure base colour, no shading)", Float) = 0
        _UnpaintedShade ("Show Unpainted Layout", Range(0, 1)) = 0
        _UnpaintedInk ("Unpainted Dot Colour", Color) = (0.74, 0.64, 0.92, 1)

        [Header(Paint Look)]
        _PaintEdge ("Paint Edge (coverage threshold)", Range(0.05, 0.9)) = 0.35
        _PaintRim ("Paint Rim Darkening", Range(0, 1)) = 0.16
        _PaintGloss ("Paint Gloss (emboss highlight)", Range(0, 2)) = 0.55
        _PaintEmboss ("Paint Emboss Distance (texels)", Range(0.5, 4)) = 1.6

        [Header(Outline)]
        _OutlineColor ("Outline Colour", Color) = (0.07, 0.07, 0.09, 1)
        _OutlineWidth ("Outline Width (world)", Range(0, 0.1)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "ColourWaysTheme.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _Paintable;
            float _HideUnpainted;
            float _Halftone;
            float4 _LightDir;
            half _ShadeAmount;
            half _ShadeThreshold;
            half _LightMix;
            half _DotShade;
            half4 _DotColor;
            float _DotScale;
            half _HatchStart;
            half _TileLines;
            float _TileSize;
            float _TileWidth;
            half _UnpaintedFlat;
            half _UnpaintedShade;
            half4 _UnpaintedInk;
            half4 _OutlineColor;
            float _OutlineWidth;
            half _PaintEdge;
            half _PaintRim;
            half _PaintGloss;
            float _PaintEmboss;
        CBUFFER_END

        // Globals set by PaintManager.
        TEXTURE2D(_CW_PaintAtlas);
        SAMPLER(sampler_CW_PaintAtlas);
        float4 _CW_PaintAtlasTexel; // (1/w, 1/h, w, h)
        float _CW_PaintEnabled;

        half4 SamplePaint(float2 paintUV, float4 chartRect)
        {
            float2 halfTexel = 0.5 * _CW_PaintAtlasTexel.xy;
            float2 uv = clamp(paintUV, chartRect.xy + halfTexel, chartRect.zw - halfTexel);
            half4 paint = SAMPLE_TEXTURE2D(_CW_PaintAtlas, sampler_CW_PaintAtlas, uv);
            paint.a *= _CW_PaintEnabled;
            return paint;
        }
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma shader_feature_local _PAINTABLE
            #pragma shader_feature_local _HIDE_UNPAINTED
            #pragma shader_feature_local _HALFTONE

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 paintUV    : TEXCOORD2;
                float4 chartRect  : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 paintUV    : TEXCOORD1;
                float4 chartRect  : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.paintUV = input.paintUV;
                o.chartRect = input.chartRect;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            // 0 = fully lit, 1 = "ink". Halftone dots grow with darkness; hatching kicks in for deep shade.
            float Pattern(float2 pixel, half darkness)
            {
                #if defined(_HALFTONE)
                    float spacing = _DotScale * max(_ScreenParams.y / 1080.0, 0.5);
                    float2 sp = pixel / spacing;
                    float2 r = float2(sp.x + sp.y, sp.y - sp.x) * 0.70710678; // 45° screen grid
                    float d = length(frac(r) - 0.5);
                    float radius = sqrt(saturate(darkness)) * 0.62;
                    float aa = max(fwidth(d), 1e-4);
                    float dots = 1.0 - smoothstep(radius - aa, radius + aa, d);

                    float hatchAmount = saturate((darkness - _HatchStart) / max(1.0 - _HatchStart, 1e-3)) * 0.45;
                    float hatchPhase = frac((sp.x - sp.y) * 0.5);
                    float hatchAa = max(fwidth(hatchPhase), 1e-4);
                    float hatch = 1.0 - smoothstep(hatchAmount - hatchAa, hatchAmount + hatchAa, hatchPhase);
                    hatch *= step(1e-3, hatchAmount);
                    return max(dots, hatch);
                #else
                    return darkness;
                #endif
            }

            half3 Shade(half3 colour, float pattern)
            {
                half3 lit = lerp(half3(1, 1, 1), colour, _LightMix);
                half3 ink = lerp(colour * _DotShade, _DotColor.rgb, _DotColor.a);
                return lerp(lit, ink, pattern);
            }

            float TileLineMask(float3 positionWS, float3 n)
            {
                float3 a = abs(n);
                float2 uv = a.y >= a.x && a.y >= a.z ? positionWS.xz : (a.x >= a.z ? positionWS.zy : positionWS.xy);
                uv /= max(_TileSize, 1e-3);
                float2 d = abs(frac(uv + 0.5) - 0.5);
                float2 aa = max(fwidth(uv), 1e-4);
                float2 l = 1.0 - smoothstep(_TileWidth - aa, _TileWidth + aa, d);
                return max(l.x, l.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                half lambert = dot(n, normalize(_LightDir.xyz)) * 0.5 + 0.5;
                half darkness = saturate((1.0 - lambert) * _ShadeAmount);
                darkness = saturate((darkness - _ShadeThreshold) / max(1.0 - _ShadeThreshold, 1e-3));
                float pattern = Pattern(input.positionCS.xy, darkness);

                half3 colour;
                #if defined(_PAINTABLE)
                    half4 paint = SamplePaint(input.paintUV, input.chartRect);
                    #if defined(_HIDE_UNPAINTED)
                        clip(paint.a - 0.08);
                    #endif
                    half3 unpainted = _UnpaintedShade > 0.5
                        ? lerp(_BaseColor.rgb, _UnpaintedInk.rgb, pattern)
                        : Shade(_BaseColor.rgb, pattern);
                    unpainted = _UnpaintedFlat > 0.5 ? _BaseColor.rgb : unpainted;

                    // Coverage: the bilinear alpha is a soft field; cut it with an anti-aliased
                    // threshold so splats get crisp, smooth (vector-like) edges at any zoom.
                    float a = paint.a;
                    float aw = max(fwidth(a), 1e-3) * 0.75;
                    float cover = smoothstep(_PaintEdge - aw, _PaintEdge + aw, a);

                    // True colour: stamps blend onto a clear (black) atlas, which darkens partly
                    // covered texels. Undo that so edges don't get dirty fringes.
                    half3 pc = saturate(paint.rgb / max(a, 0.05));

                    // Thick wet paint: a slightly darker rim just inside the edge, and an emboss
                    // from the coverage field (edges facing the light catch a highlight).
                    float2 off = _CW_PaintAtlasTexel.xy * _PaintEmboss;
                    float aUp = SamplePaint(input.paintUV + off, input.chartRect).a;
                    float aDown = SamplePaint(input.paintUV - off, input.chartRect).a;
                    float slope = saturate(aDown) - saturate(aUp);
                    float rim = 1.0 - smoothstep(_PaintEdge, _PaintEdge + 0.3, a);
                    half3 painted = Shade(pc, pattern);
                    painted *= 1.0 - rim * _PaintRim;
                    painted = saturate(painted + slope * _PaintGloss * 0.5 * (1.0 - rim));
                    painted *= 1.0 - saturate(-slope) * _PaintGloss * 0.3;

                    // Theme each layer before blending, so paint on a dark floor glows
                    // instead of tinting the inverted floor the opposite hue.
                    colour = lerp(CWTheme(unpainted), CWTheme(painted), cover);
                #else
                    colour = CWTheme(Shade(_BaseColor.rgb, pattern));
                #endif

                colour *= 1.0 - TileLineMask(input.positionWS, n) * _TileLines;

                colour = MixFog(colour, input.fogFactor);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // Inverted-hull ink outline. Disable per material with SetShaderPassEnabled("SRPDefaultUnlit", false).
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float fogFactor : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                positionWS += normalWS * _OutlineWidth;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                clip(_OutlineWidth - 1e-5);
                return half4(MixFog(CWTheme(_OutlineColor.rgb), input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _PAINTABLE
            #pragma shader_feature_local _HIDE_UNPAINTED

            struct Attributes { float4 positionOS : POSITION; float2 paintUV : TEXCOORD2; float4 chartRect : TEXCOORD3; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 paintUV : TEXCOORD0; float4 chartRect : TEXCOORD1; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.paintUV = input.paintUV;
                o.chartRect = input.chartRect;
                return o;
            }

            half frag(Varyings input) : SV_Target
            {
                #if defined(_PAINTABLE) && defined(_HIDE_UNPAINTED)
                    clip(SamplePaint(input.paintUV, input.chartRect).a - 0.08);
                #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma shader_feature_local _PAINTABLE
            #pragma shader_feature_local _HIDE_UNPAINTED

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 paintUV : TEXCOORD2; float4 chartRect : TEXCOORD3; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 paintUV : TEXCOORD1; float4 chartRect : TEXCOORD2; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.paintUV = input.paintUV;
                o.chartRect = input.chartRect;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                #if defined(_PAINTABLE) && defined(_HIDE_UNPAINTED)
                    clip(SamplePaint(input.paintUV, input.chartRect).a - 0.08);
                #endif
                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remapped = saturate(octNormalWS * 0.5 + 0.5);
                    return half4(PackFloat2To888(remapped), 0.0);
                #else
                    return half4(normalWS, 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
