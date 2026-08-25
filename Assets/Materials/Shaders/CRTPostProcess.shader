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

            float _Strength;
            float _PixelsPerUnit;
            float _HorizontalScanlineIntensity;
            float _VerticalScanlineIntensity;
            float _Curvature;
            float _ColorBleed;

            float2 Curve(float2 uv, float curveAmount)
            {
                uv = uv * 2.0 - 1.0;
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

                float scanlineDensity = _PixelsPerUnit * 5.0; 
                
                // Horizontal scanlines (vary across the Y axis)
                float hScanline = sin(curvedUV.y * scanlineDensity * PI);
                hScanline = hScanline * 0.5 + 0.5;
                hScanline = lerp(1.0, hScanline, _HorizontalScanlineIntensity); 
                
                // Vertical scanlines (vary across the X axis)
                float vScanline = sin(curvedUV.x * scanlineDensity * PI);
                vScanline = vScanline * 0.5 + 0.5;
                vScanline = lerp(1.0, vScanline, _VerticalScanlineIntensity); 

                // Apply both grid lines
                color.rgb *= (hScanline * vScanline);

                // Chromatic Aberration
                half red = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, curvedUV + float2(_ColorBleed, 0)).r;
                half blue = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, curvedUV - float2(_ColorBleed, 0)).b;
                
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