Shader "Custom/ForcePixelate"
{
    Properties
    {
        // [PerRendererData] tells Unity to let the SpriteRenderer component handle passing the texture.
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        [Header(Pixelation Settings)]
        _PixelsPerUnit ("Pixels Per Unit", Float) = 100.0
        _PixelSize ("Pixel Size", Float) = 1.0
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

        // Standard alpha blending for 2D sprites
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
                float2 worldScale : TEXCOORD1; // Pass world scale to fragment
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            
            float _PixelsPerUnit;
            float _PixelSize;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                
                // Combine material tint with SpriteRenderer vertex color
                o.color = v.color * _Color;
                
                // Extract the world scale of the GameObject.
                float scaleX = length(float3(unity_ObjectToWorld[0].x, unity_ObjectToWorld[1].x, unity_ObjectToWorld[2].x));
                float scaleY = length(float3(unity_ObjectToWorld[0].y, unity_ObjectToWorld[1].y, unity_ObjectToWorld[2].y));
                
                o.worldScale = float2(scaleX, scaleY);
                
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // 1. Calculate how many "steps" or virtual pixels there should be across the texture.
                // Multiplying by worldScale makes the pixel density remain constant even if the object is scaled up.
                float2 scaleFactor = i.worldScale * (_PixelsPerUnit / 100.0);
                float safePixelSize = max(_PixelSize, 0.001); // Prevent division by zero
                
                float2 uvSteps = (_MainTex_TexelSize.zw / safePixelSize) * scaleFactor;
                uvSteps = max(uvSteps, float2(1.0, 1.0)); // Ensure at least 1 step
                
                // 2. Pixelate the UV coordinates based on the steps, centering via + 0.5
                float2 pixelatedUV = (floor(i.uv * uvSteps) + 0.5) / uvSteps;

                // 3. Sample the texture using the modified UVs and apply color tinting
                fixed4 col = tex2D(_MainTex, pixelatedUV) * i.color;
                
                return col;
            }
            ENDCG
        }
    }
}