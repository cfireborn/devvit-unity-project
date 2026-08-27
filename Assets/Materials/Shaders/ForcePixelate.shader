Shader "Custom/SinglePassPixelShadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Shadow Settings)]

        _ShadowColor ("Shadow Color", Color) = (0, 0, 0, 0.5)

        // World-space direction and distance of the shadow.
        _ShadowOffset ("World Shadow Offset (X, Y)", Vector) = (-0.5, -0.5, 0, 0)

        // Amount of blur applied to the shadow.
        // 0 = crisp shadow.
        // Larger values = softer/larger blur.
        _ShadowBlur ("Shadow Blur", Range(0, 0.05)) = 0.005

        // Additional edge feathering.
        _ShadowSoftness ("Shadow Softness (Edge Feather)", Range(0, 0.05)) = 0.01

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

        // Premultiplied alpha blending
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


            // =========================================================
            // STRUCTS
            // =========================================================

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

                // UV used for the shadow.
                float2 shadowUV : TEXCOORD1;
            };


            // =========================================================
            // PROPERTIES
            // =========================================================

            fixed4 _Color;

            fixed4 _ShadowColor;

            // WORLD-SPACE shadow displacement.
            float4 _ShadowOffset;

            // Shadow blur radius in UV space.
            float _ShadowBlur;

            // Additional edge feathering.
            float _ShadowSoftness;


            // =========================================================
            // TEXTURES
            // =========================================================

            sampler2D _MainTex;
            sampler2D _AlphaTex;


            // =========================================================
            // VERTEX SHADER
            // =========================================================

            v2f vert(appdata_t IN)
            {
                v2f OUT;

                // Normal sprite rendering.
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;

                #ifdef PIXELSNAP_ON
                    OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif


                // -----------------------------------------------------
                // WORLD-SPACE SHADOW OFFSET
                // -----------------------------------------------------
                //
                // _ShadowOffset is specified in world space.
                //
                // Convert it into the sprite's local space using the
                // inverse object transform.
                //
                // Using a 3x3 matrix means translation is ignored,
                // which is correct because this is a direction/vector,
                // not a position.
                //
                float3 localShadowOffset =
                    mul(
                        (float3x3)unity_WorldToObject,
                        _ShadowOffset.xyz
                    );


                // -----------------------------------------------------
                // UV OFFSET
                // -----------------------------------------------------
                //
                // Unity sprites use a local-space mesh corresponding
                // to their texture UVs.
                //
                // The shadow offset is converted into UV space here.
                //
                // NOTE:
                // This assumes the normal rectangular SpriteRenderer
                // mesh relationship between local coordinates and UVs.
                //
                // The offset is negated because we want to sample the
                // source sprite from the position displaced toward the
                // light, creating the shadow in the opposite direction.
                //
                float2 uvOffset = localShadowOffset.xy;


                OUT.shadowUV = IN.texcoord - uvOffset;

                return OUT;
            }


            // =========================================================
            // SAFE TEXTURE SAMPLING
            // =========================================================

            fixed4 SampleSpriteTextureWithBounds(float2 uv)
            {
                // Anything outside the sprite's UV range is transparent.
                if (uv.x < 0.0 ||
                    uv.x > 1.0 ||
                    uv.y < 0.0 ||
                    uv.y > 1.0)
                {
                    return fixed4(0, 0, 0, 0);
                }

                fixed4 color = tex2D(_MainTex, uv);

                #if defined(ETC1_EXTERNAL_ALPHA)

                    fixed4 alpha = tex2D(_AlphaTex, uv);

                    color.a = alpha.r;

                #endif

                return color;
            }


            // =========================================================
            // FRAGMENT SHADER
            // =========================================================

            fixed4 frag(v2f IN) : SV_Target
            {
                // =====================================================
                // 1. SHADOW BLUR
                // =====================================================

                //
                // _ShadowBlur controls the size of the blur.
                //
                // _ShadowSoftness provides additional edge feathering.
                //
                // Add them together so both properties contribute to
                // the final blur radius.
                //
                float blur = _ShadowBlur + _ShadowSoftness;

                float2 sUV = IN.shadowUV;


                // -----------------------------------------------------
                // 13-TAP BLUR
                // -----------------------------------------------------
                //
                //             1
                //
                //       2     3     4
                //
                //   5   6     7     8   9
                //
                //       10    11    12
                //
                //             13
                //
                // This is a lightweight approximation of a Gaussian
                // blur while keeping the number of texture samples
                // reasonably low.
                //


                // Center
                fixed a0 =
                    SampleSpriteTextureWithBounds(
                        sUV
                    ).a;


                // First ring
                fixed a1 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(0, blur)
                    ).a;

                fixed a2 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(blur, 0)
                    ).a;

                fixed a3 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(0, -blur)
                    ).a;

                fixed a4 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(-blur, 0)
                    ).a;


                // Diagonal ring
                float diagonal = blur * 0.7071;

                fixed a5 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(diagonal, diagonal)
                    ).a;

                fixed a6 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(-diagonal, diagonal)
                    ).a;

                fixed a7 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(-diagonal, -diagonal)
                    ).a;

                fixed a8 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(diagonal, -diagonal)
                    ).a;


                // Second ring.
                float outer = blur * 2.0;

                fixed a9 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(0, outer)
                    ).a;

                fixed a10 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(outer, 0)
                    ).a;

                fixed a11 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(0, -outer)
                    ).a;

                fixed a12 =
                    SampleSpriteTextureWithBounds(
                        sUV + float2(-outer, 0)
                    ).a;


                // -----------------------------------------------------
                // BLUR WEIGHTS
                // -----------------------------------------------------
                //
                // Center gets the highest weight.
                // Inner samples provide most of the blur.
                // Outer samples extend the falloff.
                //

                fixed shadowAlpha =
                    a0 * 0.25 +

                    (a1 + a2 + a3 + a4) * 0.125 +

                    (a5 + a6 + a7 + a8) * 0.0625 +

                    (a9 + a10 + a11 + a12) * 0.015625;


                // Normalize approximately so increasing blur doesn't
                // dramatically change the shadow's opacity.
                shadowAlpha *= 0.9142857;


                // =====================================================
                // 2. SHADOW COLOR
                // =====================================================

                fixed4 shadowColor = _ShadowColor;

                shadowColor.a *= shadowAlpha;

                // Premultiplied alpha.
                shadowColor.rgb *= shadowColor.a;


                // =====================================================
                // 3. ORIGINAL SPRITE
                // =====================================================

                fixed4 mainColor =
                    tex2D(
                        _MainTex,
                        IN.texcoord
                    ) * IN.color;


                #if defined(ETC1_EXTERNAL_ALPHA)

                    fixed4 mainAlpha =
                        tex2D(
                            _AlphaTex,
                            IN.texcoord
                        );

                    mainColor.a = mainAlpha.r;

                #endif


                // Premultiplied alpha.
                mainColor.rgb *= mainColor.a;


                // =====================================================
                // 4. COMPOSITE
                // =====================================================

                // The normal sprite is rendered over the shadow.
                fixed4 finalColor =
                    mainColor +
                    shadowColor * (1.0 - mainColor.a);

                return finalColor;
            }

            ENDCG
        }
    }
}