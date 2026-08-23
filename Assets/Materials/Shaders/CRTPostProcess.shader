Shader "Hidden/Custom/CRT_Shader"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "CRT Effect"

            HLSLPROGRAM
            #pragma vertex Vert 
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Variables passed from the Render Feature
            float _Strength;
            float _PixelsPerUnit;
            float _ScanlineIntensity;
            float _Curvature;
            float _ColorBleed;

            // Mathematical curve - now controlled by the _Curvature slider
            float2 Curve(float2 uv, float curveAmount)
            {
                uv = uv * 2.0 - 1.0;
                // If _Curvature is 0, the offset becomes 0, resulting in a flat screen
                float2 offset = abs(uv.yx) * (curveAmount * float2(0.166, 0.25)); 
                uv = uv + uv * offset * offset;
                uv = uv * 0.5 + 0.5;
                return uv;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 curvedUV = Curve(uv, _Curvature);

                if (curvedUV.x < 0.0 || curvedUV.x > 1.0 || curvedUV.y < 0.0 || curvedUV.y > 1.0)
                    return half4(0, 0, 0, 1);

                half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, curvedUV);

                // Scanlines - controlled by PPU (density) and _ScanlineIntensity (opacity)
                float scanlineDensity = _PixelsPerUnit * 5.0; 
                float scanline = sin(curvedUV.y * scanlineDensity * PI);
                
                // Map the sine wave (-1 to 1) to (0 to 1), then mix it based on intensity
                scanline = scanline * 0.5 + 0.5;
                scanline = lerp(1.0, scanline, _ScanlineIntensity); 
                color.rgb *= scanline;

                // Chromatic Aberration - offset distance controlled by _ColorBleed
                half red = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, curvedUV + float2(_ColorBleed, 0)).r;
                half blue = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, curvedUV - float2(_ColorBleed, 0)).b;
                
                // We only lerp the red and blue channels if ColorBleed is greater than 0
                float bleedBlend = _ColorBleed > 0 ? 0.7 : 0.0;
                color.r = lerp(color.r, red, bleedBlend);
                color.b = lerp(color.b, blue, bleedBlend);

                half4 originalColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                return lerp(originalColor, color, _Strength);
            }
            ENDHLSL
        }
    }
}