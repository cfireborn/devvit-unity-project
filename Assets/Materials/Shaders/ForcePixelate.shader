Shader "Custom/SinglePassPixelShadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Pixelation Settings)]
        _PixelsPerWorldUnit ("Pixels Per World Unit", Float) = 100
        _PixelSizeMultiplier ("Pixel Size Multiplier", Float) = 1

        [Header(Shadow Settings)]
        _ShadowColor ("Shadow Color", Color) = (0, 0, 0, 0.5)
        _ShadowOffset ("World Shadow Offset (X, Y)", Vector) = (-0.5, -0.5, 0, 0)
        _ShadowBlur ("Shadow Blur (World Units)", Range(0, 0.2)) = 0.01
        _ShadowSoftness ("Shadow Softness", Range(0, 0.05)) = 0.005

        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaSplitEnabled ("Alpha Split Enabled", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            fixed4 _Color;
            float _PixelsPerWorldUnit;
            float _PixelSizeMultiplier;

            fixed4 _ShadowColor;
            float4 _ShadowOffset;
            float _ShadowBlur;
            float _ShadowSoftness;

            sampler2D _MainTex;
            sampler2D _AlphaTex;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.worldPos = mul(unity_ObjectToWorld, IN.vertex).xyz;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;

                #ifdef PIXELSNAP_ON
                    OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif

                return OUT;
            }

            float2x2 Inverse2x2(float2x2 m)
            {
                float determinant = m[0][0] * m[1][1] - m[0][1] * m[1][0];
                float absDet = abs(determinant);
                
                if (absDet < 1e-12)
                {
                    determinant = (determinant < 0.0) ? -1e-12 : 1e-12;
                }

                return float2x2(
                     m[1][1] / determinant,
                    -m[0][1] / determinant,
                    -m[1][0] / determinant,
                     m[0][0] / determinant
                );
            }

            // Optimized branchless sampling with precomputed inverse target
            fixed4 SampleSpriteTextureWithBounds(float2 uv, float2 targetPixels, float2 invTargetPixels)
            {
                // Multiply-add instead of divide-add
                uv = (floor(uv * targetPixels) + 0.5) * invTargetPixels;

                fixed4 color = tex2D(_MainTex, uv);

                #if defined(ETC1_EXTERNAL_ALPHA)
                    color.a = tex2D(_AlphaTex, uv).r;
                #endif

                // Branchless bounds check (returns 1 if 0 <= uv <= 1, else 0)
                float2 s = step(0.0, uv) * step(uv, 1.0);
                float inBounds = s.x * s.y;

                return color * inBounds;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // =====================================================
                // 1. CALCULATE MATRICES AND TARGET PIXEL RESOLUTION
                // =====================================================

                float2 uvDx = ddx(IN.texcoord);
                float2 uvDy = ddy(IN.texcoord);
                float3 worldDx = ddx(IN.worldPos);
                float3 worldDy = ddy(IN.worldPos);

                float2x2 screenToUV = float2x2(uvDx.x, uvDy.x, uvDx.y, uvDy.y);
                float2x2 screenToWorld = float2x2(worldDx.x, worldDy.x, worldDx.y, worldDy.y);
                
                // Optimized: Matrix properties allow us to bypass one inversion
                // (A * B^-1)^-1 == B * A^-1
                float2x2 uvToWorld = mul(screenToWorld, Inverse2x2(screenToUV));
                float2x2 worldToUVMat = mul(screenToUV, Inverse2x2(screenToWorld));

                float worldWidth = length(mul(uvToWorld, float2(1.0, 0.0)));
                float worldHeight = length(mul(uvToWorld, float2(0.0, 1.0)));

                float ppu = max(_PixelsPerWorldUnit, 0.001);
                float mult = max(_PixelSizeMultiplier, 0.001);

                float pixelsX = max(round(worldWidth * ppu / mult), 1.0);
                float pixelsY = max(round(worldHeight * ppu / mult), 1.0);
                
                float2 targetPixels = float2(pixelsX, pixelsY);
                float2 invTargetPixels = 1.0 / targetPixels;

                // =====================================================
                // 2. CONVERT OFFSETS & BLUR SCALES
                // =====================================================

                float2 shadowUVOffset = mul(worldToUVMat, _ShadowOffset.xy);
                float2 shadowUV = IN.texcoord - shadowUVOffset;

                float2 blurUVX = mul(worldToUVMat, float2(_ShadowBlur, 0));
                float2 blurUVY = mul(worldToUVMat, float2(0, _ShadowBlur));
                float2 diagonal = (blurUVX + blurUVY) * 0.70710678;

                // =====================================================
                // 3. 13-TAP BLUR (WITH PIXELATION GRID)
                // =====================================================

                fixed a0 = SampleSpriteTextureWithBounds(shadowUV, targetPixels, invTargetPixels).a;

                fixed a1 = SampleSpriteTextureWithBounds(shadowUV + blurUVY, targetPixels, invTargetPixels).a;
                fixed a2 = SampleSpriteTextureWithBounds(shadowUV + blurUVX, targetPixels, invTargetPixels).a;
                fixed a3 = SampleSpriteTextureWithBounds(shadowUV - blurUVY, targetPixels, invTargetPixels).a;
                fixed a4 = SampleSpriteTextureWithBounds(shadowUV - blurUVX, targetPixels, invTargetPixels).a;

                fixed a5 = SampleSpriteTextureWithBounds(shadowUV + diagonal, targetPixels, invTargetPixels).a;
                fixed a6 = SampleSpriteTextureWithBounds(shadowUV + float2(-diagonal.x, diagonal.y), targetPixels, invTargetPixels).a;
                fixed a7 = SampleSpriteTextureWithBounds(shadowUV - diagonal, targetPixels, invTargetPixels).a;
                fixed a8 = SampleSpriteTextureWithBounds(shadowUV + float2(diagonal.x, -diagonal.y), targetPixels, invTargetPixels).a;

                fixed a9 =  SampleSpriteTextureWithBounds(shadowUV + blurUVY * 2.0, targetPixels, invTargetPixels).a;
                fixed a10 = SampleSpriteTextureWithBounds(shadowUV + blurUVX * 2.0, targetPixels, invTargetPixels).a;
                fixed a11 = SampleSpriteTextureWithBounds(shadowUV - blurUVY * 2.0, targetPixels, invTargetPixels).a;
                fixed a12 = SampleSpriteTextureWithBounds(shadowUV - blurUVX * 2.0, targetPixels, invTargetPixels).a;

                fixed shadowAlpha =
                    a0 * 0.25 +
                    (a1 + a2 + a3 + a4) * 0.125 +
                    (a5 + a6 + a7 + a8) * 0.0625 +
                    (a9 + a10 + a11 + a12) * 0.015625;

                // =====================================================
                // 4. SHADOW COLOR & SOFTNESS
                // =====================================================

                fixed4 shadowColor = _ShadowColor;
                shadowColor.a *= shadowAlpha;
                shadowColor.rgb *= shadowColor.a;

                if (_ShadowSoftness > 0.0)
                {
                    float softness = saturate(shadowAlpha / max(_ShadowSoftness, 0.00001));
                    shadowColor.a *= softness;
                    shadowColor.rgb *= softness;
                }

                // =====================================================
                // 5. ORIGINAL SPRITE & COMPOSITE
                // =====================================================

                // Refactored to reuse the bounds and ETC1 check logic
                fixed4 mainColor = SampleSpriteTextureWithBounds(IN.texcoord, targetPixels, invTargetPixels) * IN.color;
                mainColor.rgb *= mainColor.a;

                return mainColor + shadowColor * (1.0 - mainColor.a);
            }
            ENDCG
        }
    }
}