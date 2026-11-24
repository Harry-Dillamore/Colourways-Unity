// Full-screen paint curtain for level transitions (UI shader for a screen-space overlay RawImage).
// Three layers of paint rise from the bottom with wobbly edges and drippy tongues; at _Fill = 1
// the screen is fully covered. Animating _Fill back to 0 drains the paint off the bottom.
Shader "ColourWays/UI/PaintCurtain"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _ColorA ("Colour A", Color) = (1, 0.25, 0.42, 1)
        _ColorB ("Colour B", Color) = (1, 0.82, 0.1, 1)
        _ColorC ("Colour C", Color) = (0.1, 0.62, 1, 1)
        _Fill ("Fill", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            fixed4 _ColorA, _ColorB, _ColorC;
            float _Fill, _Seed;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            // Height of a paint layer's edge at x: base level + gentle waves + rounded drip tongues.
            float Edge(float x, float level, float layer)
            {
                float wave = sin(x * 8.2 + layer * 2.1 + _Seed) * 0.022 + sin(x * 21.0 + layer * 5.3 + _Seed * 1.7) * 0.01;
                float cols = 14.0 + layer * 6.0;
                float col = floor(x * cols);
                float local = abs(frac(x * cols) - 0.5) * 2.0;           // 0 at a column centre, 1 at its sides
                float h = Hash(col * 12.9898 + layer * 78.233 + _Seed);
                float tongue = sqrt(saturate(1.0 - local * local));       // rounded drip profile
                float drip = max(0.0, h - 0.45) * 0.32 * tongue;
                return level + wave + drip;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 colours[3] = { _ColorA, _ColorB, _ColorC };
                fixed4 result = fixed4(0, 0, 0, 0);
                [unroll]
                for (int layer = 0; layer < 3; layer++)
                {
                    // Layers rise one after another; the last one covers everything at _Fill = 1.
                    float t = saturate(_Fill * 1.3 - layer * 0.15);
                    t = t * t * (3.0 - 2.0 * t);
                    float level = lerp(-0.45, 1.25, t);
                    float d = Edge(i.uv.x, level, layer) - i.uv.y;
                    float aa = max(fwidth(d), 1e-4);
                    float cover = smoothstep(-aa, aa, d);
                    result = lerp(result, colours[layer], cover);
                }
                result.a *= i.color.a;
                return result;
            }
            ENDCG
        }
    }
}
