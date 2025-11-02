// Assets/Shaders/Hidden/FOW/OverlayBlit.shader
Shader "Hidden/FOW/OverlayBlit"
{
    Properties
    {
        _MainTex   ("Source", 2D) = "white" {}
        _FogMask   ("Fog Mask (R8)", 2D) = "white" {}
        _FogColor  ("Fog Color", Color) = (0,0,0,1)
        _FogOpacity("Fog Opacity", Float) = 1.0
        _DebugMode ("Debug Mode (0=Normal,1=ForceBlack,2=ShowMask)", Float) = 0
    }
    SubShader
    {
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            sampler2D _FogMask;
            float4 _FogWorldRect; // fogOriginX, fogOriginZ, fogSizeX, fogSizeZ
            float4 _CamWorldRect; // camLeftX, camBottomZ, camSizeX, camSizeZ
            float4 _FogColor;
            float _FogOpacity;
            float _DebugMode; // 0 normal, 1 force black, 2 show mask
            float4 _ScreenSpaceToggle; // x=1 to treat UV as fogUV directly

            struct appdata { float4 vertex: POSITION; float2 uv: TEXCOORD0; };
            struct v2f    { float4 pos: SV_POSITION; float2 uv: TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample scene
                fixed4 col = tex2D(_MainTex, i.uv);

                // Compute fog UV
                float2 fogUV;
                if (_ScreenSpaceToggle.x > 0.5)
                {
                    // Directly map screen UV -> fog UV (for debug/sanity)
                    fogUV = i.uv;
                }
                else
                {
                    // Reconstruct world XZ in camera ortho rect
                    float2 worldXZ;
                    worldXZ.x = _CamWorldRect.x + i.uv.x * _CamWorldRect.z; // left + u*width
                    worldXZ.y = _CamWorldRect.y + i.uv.y * _CamWorldRect.w; // bottom + v*height

                    // Map to fog UV
                    fogUV.x = (worldXZ.x - _FogWorldRect.x) / _FogWorldRect.z;
                    fogUV.y = (worldXZ.y - _FogWorldRect.y) / _FogWorldRect.w;
                }

                float mask = tex2D(_FogMask, saturate(fogUV)).r;

                if (_DebugMode >= 1.5) // 2 = ShowMask
                {
                    return fixed4(mask, mask, mask, 1);
                }

                float a;
                if (_DebugMode >= 0.5) // 1 = ForceBlack
                    a = saturate(_FogOpacity);
                else
                    a = saturate(mask) * saturate(_FogOpacity);

                col.rgb = lerp(col.rgb, _FogColor.rgb, a);
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
