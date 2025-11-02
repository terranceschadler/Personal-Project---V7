// Shader: Hidden/FOW/Paint
// Purpose: Paint (cut) radial holes into a fog mask where 1=fog, 0=clear.
// Pass 0: stamp a soft disc, newMask = min(oldMask, 1 - alpha)
// Pass 1: clear/fill the entire mask to a constant value (_ClearValue)

Shader "Hidden/FOW/Paint"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off     // we write the mask directly

        // --- PASS 0: radial stamp (no mirroring) ---
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // Inputs
            sampler2D _MainTex;        // current mask (R channel used), wrap=Clamp
            float4    _Brush;          // (centerU, centerV, radiusUV, hardness 0..1)
            float     _Feather;        // fraction 0..0.5 of radius

            struct appdata {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };
            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // Curve helper: map hardness (0..1) to an exponent shaping the inner plateau.
            // 0 => very soft rim, 1 => sharper rim.
            float shape_hard(float x, float h)
            {
                // x in [0,1] (1=center, 0=outside). Raise to exponent in [2->0.5] as h goes [0->1].
                float exp = lerp(2.0, 0.5, saturate(h));
                return pow(saturate(x), exp);
            }

            float4 frag(v2f i) : SV_Target
            {
                // Read existing mask
                float oldMask = tex2D(_MainTex, i.uv).r;

                // Radial falloff (pure length — NO component-wise abs, so no mirroring)
                float2  center = _Brush.xy;
                float   radius = max(_Brush.z, 1e-5);
                float   soft   = saturate(_Feather) * radius;

                // Distance from stamp center
                float   d      = length(i.uv - center);

                // Build an "inside strength" where 1=center, 0=outside radius
                // Use smoothstep between (radius - soft) and radius for the rim
                float innerLinear = 1.0 - smoothstep(max(radius - soft, 0.0), radius, d);

                // Shape with hardness
                float inner = shape_hard(innerLinear, saturate(_Brush.w));

                // Convert inner strength (1=center) to a "reveal amount" (1=center reveals to 0)
                float reveal = saturate(inner);

                // New mask: 1=fog, 0=clear. We can only ever reduce fog, never add.
                float newMask = min(oldMask, 1.0 - reveal);

                return float4(newMask, newMask, newMask, 1.0);
            }
            ENDHLSL
        }

        // --- PASS 1: clear/fill whole mask to a constant value ---
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert2
            #pragma fragment frag2
            #include "UnityCG.cginc"

            float _ClearValue; // 0..1

            struct appdata {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };
            struct v2f {
                float4 pos : SV_POSITION;
            };

            v2f vert2(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 frag2(v2f i) : SV_Target
            {
                float v = saturate(_ClearValue);
                return float4(v, v, v, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
