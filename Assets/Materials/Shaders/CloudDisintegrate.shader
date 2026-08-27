Shader "Custom/ForcePixelateDispersion"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        [Header(Pixelation Settings)]
        _PixelsPerUnit ("Pixels Per Unit", Float) = 100.0
        _PixelSize ("Pixel Size", Float) = 1.0
        
        [Header(Dispersion Settings)]
        _SpawnProgress ("Spawn Progress (1=Full, 0=Empty)", Range(0.0, 1.0)) = 1.0
        _CloudExpand ("Dispersion Expansion", Float) = 0.5
        _NoiseScale ("Noise Scale", Float) = 4.0
        _EdgeSmoothness ("Puff Edge Smoothness", Range(0.001, 0.5)) = 0.05
        _AlphaSteps ("Alpha Gradient Steps", Range(1.0, 100.0)) = 1.0
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
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float2 worldScale : TEXCOORD1; 
                float3 objectCenterWorld : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            
            float _PixelsPerUnit;
            float _PixelSize;
            
            float _SpawnProgress;
            float _CloudExpand;
            float _NoiseScale;
            float _EdgeSmoothness;
            float _AlphaSteps;

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

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                
                // Extract the world scale of the GameObject[cite: 1]
                float scaleX = length(float3(unity_ObjectToWorld[0].x, unity_ObjectToWorld[1].x, unity_ObjectToWorld[2].x));
                float scaleY = length(float3(unity_ObjectToWorld[0].y, unity_ObjectToWorld[1].y, unity_ObjectToWorld[2].y));
                o.worldScale = float2(scaleX, scaleY);
                
                o.objectCenterWorld = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float progressInvert = 1.0 - _SpawnProgress;
                float expansionMult = 1.0 + (progressInvert * _CloudExpand);
                
                float2 centerUV = i.uv - 0.5;
                centerUV /= expansionMult;
                float2 expandedUV = centerUV + 0.5;

                // Pixelation Math[cite: 1]
                float2 scaleFactor = i.worldScale * (_PixelsPerUnit / 100.0);
                float safePixelSize = max(_PixelSize, 0.001); 
                float2 uvSteps = (_MainTex_TexelSize.zw / safePixelSize) * scaleFactor;
                uvSteps = max(uvSteps, float2(1.0, 1.0)); 
                
                float2 pixelatedUV = (floor(expandedUV * uvSteps) + 0.5) / uvSteps;
                float boundsMask = step(0.0, pixelatedUV.x) * step(pixelatedUV.x, 1.0) * step(0.0, pixelatedUV.y) * step(pixelatedUV.y, 1.0);

                float2 localPixelatedPos = (pixelatedUV - 0.5) * i.worldScale;
                float2 noiseUV = (i.objectCenterWorld.xy + localPixelatedPos) * _NoiseScale;
                
                float n1 = 1.0 - cellular(noiseUV);
                float n2 = 1.0 - cellular(noiseUV * 2.5 + float2(4.2, 7.3)); 
                
                float noiseVal = saturate((n1 * 0.7 + n2 * 0.4) * 1.3);
                
                // Calculate base smoothmask
                float mask = smoothstep(progressInvert, progressInvert + _EdgeSmoothness, noiseVal);
                
                // Posterize the alpha to the requested number of steps
                mask = floor(mask * _AlphaSteps + 0.5) / _AlphaSteps;

                fixed4 col = tex2D(_MainTex, pixelatedUV) * i.color;
                col.a *= mask * boundsMask;
                
                return col;
            }
            ENDCG
        }
    }
}