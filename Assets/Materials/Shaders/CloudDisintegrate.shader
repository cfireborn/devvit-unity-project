Shader "Custom/ForcePixelateShadowDispersion"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Pixelation Settings)]
        _PixelsPerWorldUnit ("Pixels Per World Unit", Float) = 100
        _PixelSizeMultiplier ("Pixel Size Multiplier", Float) = 1

        [Header(Dispersion Settings)]
        _SpawnProgress ("Spawn Progress (1=Full, 0=Empty)", Range(0.0, 1.0)) = 1.0
        _CloudExpand ("Dispersion Expansion", Float) = 0.5
        _NoiseScale ("Noise Scale", Float) = 4.0
        _OrganicWarp ("Organic Warp Strength", Range(0.0, 1.0)) = 0.25
        _EdgeSmoothness ("Puff Edge Smoothness", Range(0.001, 0.5)) = 0.05
        _AlphaSteps ("Alpha Gradient Steps", Range(1.0, 100.0)) = 1.0

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
                float3 objectCenterWorld : TEXCOORD2;
            };

            fixed4 _Color;
            float _PixelsPerWorldUnit;
            float _PixelSizeMultiplier;

            float _SpawnProgress;
            float _CloudExpand;
            float _NoiseScale;
            float _OrganicWarp;
            float _EdgeSmoothness;
            float _AlphaSteps;

            fixed4 _ShadowColor;
            float4 _ShadowOffset;
            float _ShadowBlur;
            float _ShadowSoftness;

            sampler2D _MainTex;
            sampler2D _AlphaTex;

            float2 hash2(float2 p) 
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453123);
            }

            float cellular(float2 p) 
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float min_dist = 1.0;
                
                for(int y = -1; y <= 1; y++) 
                {
                    for(int x = -1; x <= 1; x++) 
                    {
                        float2 neighbor = float2(float(x), float(y));
                        float2 point_pos = hash2(i + neighbor);
                        float2 diff = neighbor + point_pos - f;
                        float dist = length(diff);
                        min_dist = min(min_dist, dist);
                    }
                }
                return min_dist;
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

            fixed4 SampleDispersedSprite(float2 uv, float2 targetPixels, float2 invTargetPixels, float2x2 uvToWorld, float3 objectCenterWorld)
            {
                float progressInvert = 1.0 - _SpawnProgress;
                float expansionMult = 1.0 + (progressInvert * _CloudExpand);

                float2 centerUV = uv - 0.5;
                centerUV /= expansionMult;
                float2 expandedUV = centerUV + 0.5;

                float2 pixelatedUV = (floor(expandedUV * targetPixels) + 0.5) * invTargetPixels;
                fixed4 color = tex2D(_MainTex, pixelatedUV);

                #if defined(ETC1_EXTERNAL_ALPHA)
                    color.a = tex2D(_AlphaTex, pixelatedUV).r;
                #endif

                float2 s = step(0.0, pixelatedUV) * step(pixelatedUV, 1.0);
                float inBounds = s.x * s.y;

                float2 localPixelatedPos = mul(uvToWorld, (pixelatedUV - 0.5));
                float2 baseNoiseUV = (objectCenterWorld.xy + localPixelatedPos) * _NoiseScale;
                
                float2 warpOffset = float2(
                    sin(baseNoiseUV.y * 2.1 + 1.5) + cos(baseNoiseUV.x * 1.7),
                    cos(baseNoiseUV.x * 2.3) + sin(baseNoiseUV.y * 1.9 + 0.8)
                ) * _OrganicWarp;
                
                float2 noiseUV = baseNoiseUV + warpOffset;
                
                float n1 = 1.0 - cellular(noiseUV);
                float n2 = 1.0 - cellular(noiseUV * 2.5 + float2(4.2, 7.3)); 
                
                float noiseVal = saturate((n1 * 0.7 + n2 * 0.4) * 1.3);
                
                float mask = smoothstep(progressInvert, progressInvert + _EdgeSmoothness, noiseVal);
                mask = floor(mask * _AlphaSteps + 0.5) / _AlphaSteps;

                color.a *= mask * inBounds;
                return color;
            }

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.worldPos = mul(unity_ObjectToWorld, IN.vertex).xyz;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                OUT.objectCenterWorld = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;

                #ifdef PIXELSNAP_ON
                    OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif

                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uvDx = ddx(IN.texcoord);
                float2 uvDy = ddy(IN.texcoord);
                float3 worldDx = ddx(IN.worldPos);
                float3 worldDy = ddy(IN.worldPos);

                float2x2 screenToUV = float2x2(uvDx.x, uvDy.x, uvDx.y, uvDy.y);
                float2x2 screenToWorld = float2x2(worldDx.x, worldDy.x, worldDx.y, worldDy.y);
                
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

                float2 shadowUVOffset = mul(worldToUVMat, _ShadowOffset.xy);
                float2 shadowUV = IN.texcoord - shadowUVOffset;

                float2 blurUVX = mul(worldToUVMat, float2(_ShadowBlur, 0));
                float2 blurUVY = mul(worldToUVMat, float2(0, _ShadowBlur));
                float2 diagonal = (blurUVX + blurUVY) * 0.70710678;

                fixed a0 = SampleDispersedSprite(shadowUV, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;
                fixed a1 = SampleDispersedSprite(shadowUV + blurUVY, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;
                fixed a2 = SampleDispersedSprite(shadowUV + blurUVX, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;
                fixed a3 = SampleDispersedSprite(shadowUV - blurUVY, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;
                fixed a4 = SampleDispersedSprite(shadowUV - blurUVX, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;

                fixed a5 = SampleDispersedSprite(shadowUV + diagonal, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;
                fixed a6 = SampleDispersedSprite(shadowUV + float2(-diagonal.x, diagonal.y), targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;
                fixed a7 = SampleDispersedSprite(shadowUV - diagonal, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;
                fixed a8 = SampleDispersedSprite(shadowUV + float2(diagonal.x, -diagonal.y), targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;

                fixed a9 =  SampleDispersedSprite(shadowUV + blurUVY * 2.0, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;
                fixed a10 = SampleDispersedSprite(shadowUV + blurUVX * 2.0, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;
                fixed a11 = SampleDispersedSprite(shadowUV - blurUVY * 2.0, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;
                fixed a12 = SampleDispersedSprite(shadowUV - blurUVX * 2.0, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld).a;

                fixed shadowAlpha =
                    a0 * 0.25 +
                    (a1 + a2 + a3 + a4) * 0.125 +
                    (a5 + a6 + a7 + a8) * 0.0625 +
                    (a9 + a10 + a11 + a12) * 0.015625;

                fixed4 shadowColor = _ShadowColor;
                shadowColor.a *= shadowAlpha;
                shadowColor.rgb *= shadowColor.a;

                if (_ShadowSoftness > 0.0)
                {
                    float softness = saturate(shadowAlpha / max(_ShadowSoftness, 0.00001));
                    shadowColor.a *= softness;
                    shadowColor.rgb *= softness;
                }

                fixed4 mainColor = SampleDispersedSprite(IN.texcoord, targetPixels, invTargetPixels, uvToWorld, IN.objectCenterWorld) * IN.color;
                mainColor.rgb *= mainColor.a;

                return mainColor + shadowColor * (1.0 - mainColor.a);
            }
            ENDCG
        }
    }
}