Shader "Custom/PixelatedShadowShader"
{
    Properties
    {
        [HideInInspector] _MainTex ("Texture", 2D) = "white" {}
        _ShadowColor ("Shadow Color", Color) = (0,0,0,1)
        _ShadowDarkness ("Shadow Darkness", Range(0, 1)) = 0.5
        _ShadowBlur ("Shadow Blur", Range(0, 5)) = 0.0
        
        [Header(Pixelation Settings)]
        _PixelsPerUnit ("Pixels Per Unit", Float) = 100.0
        _PixelSize ("Pixel Size", Float) = 1.0
    }
    
    SubShader
    {
        // Queue is transparent so it renders after opaque geometry
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "PreviewType"="Plane" }
        LOD 100
        
        // Multiply Blend Mode
        // This takes the destination color (what is already on screen) 
        // and multiplies it by the source color (our shader output).
        Blend DstColor Zero
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float2 worldScale : TEXCOORD1; // Used to keep pixelation scale-independent
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            
            float4 _ShadowColor;
            float _ShadowDarkness;
            float _ShadowBlur;
            float _PixelsPerUnit;
            float _PixelSize;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                // Extract the world scale of the GameObject.
                // We pull the column vectors from the ObjectToWorld matrix to determine the actual scale.
                float scaleX = length(float3(unity_ObjectToWorld[0].x, unity_ObjectToWorld[1].x, unity_ObjectToWorld[2].x));
                float scaleY = length(float3(unity_ObjectToWorld[0].y, unity_ObjectToWorld[1].y, unity_ObjectToWorld[2].y));
                
                o.worldScale = float2(scaleX, scaleY);
                
                return o;
            }

            float GetBlurredAlpha(sampler2D tex, float2 uv, float blurAmount, float2 uvSteps)
            {
                if (blurAmount <= 0) return tex2D(tex, uv).a;

                float alpha = 0;
                float totalWeight = 0;
                int samples = 3; 
                
                // The step size is based on the blur amount and the size of one "virtual pixel" in UV space.
                float2 step = blurAmount * (1.0 / uvSteps);

                for (int x = -samples; x <= samples; x++)
                {
                    for (int y = -samples; y <= samples; y++)
                    {
                        float2 offset = float2(x, y) * step;
                        float weight = 1.0 / (1.0 + x*x + y*y); 
                        alpha += tex2D(tex, uv + offset).a * weight;
                        totalWeight += weight;
                    }
                }
                return alpha / totalWeight;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                
                // 1. Calculate how many "steps" or virtual pixels there should be across the entire texture.
                // We use 100.0 as the baseline standard Unity PPU reference frame. 
                // Multiplying by worldScale makes the shadow density remain constant even if the object is scaled up.
                float2 scaleFactor = i.worldScale * (_PixelsPerUnit / 100.0);
                float safePixelSize = max(_PixelSize, 0.001); // Prevent division by zero errors
                
                float2 uvSteps = (_MainTex_TexelSize.zw / safePixelSize) * scaleFactor;
                uvSteps = max(uvSteps, float2(1.0, 1.0)); // Ensure at least 1 step
                
                // 2. Pixelate the UV coordinates based on the steps
                float2 pixelatedUV = (floor(i.uv * uvSteps) + 0.5) / uvSteps;

                // 3. Sample Shadow Alpha
                float shadowAlpha = 0;
                if (pixelatedUV.x >= 0 && pixelatedUV.x <= 1 && pixelatedUV.y >= 0 && pixelatedUV.y <= 1)
                {
                    if (_ShadowBlur > 0)
                    {
                         shadowAlpha = GetBlurredAlpha(_MainTex, pixelatedUV, _ShadowBlur, uvSteps);
                    }
                    else
                    {
                         shadowAlpha = tex2D(_MainTex, pixelatedUV).a;
                    }
                }

                // 4. Calculate Final Multiply Color
                // Since our blend mode is "Multiply" (Blend DstColor Zero):
                // - Outputting White (1,1,1) means "do nothing / fully transparent"
                // - Outputting a darker color darkens the screen behind it.
                // We interpolate between white and our shadow color based on the alpha and darkness controls.
                float3 blendColor = lerp(float3(1.0, 1.0, 1.0), _ShadowColor.rgb, shadowAlpha * _ShadowDarkness);
                
                return fixed4(blendColor, 1.0);
            }
            ENDCG
        }
    }
}