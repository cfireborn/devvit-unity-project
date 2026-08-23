using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class CRTRenderFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader shader;
    private Material material;
    private CRTRenderPass crtPass;

    public override void Create()
    {
        // Create the material using the assigned shader
        if (shader != null)
            material = CoreUtils.CreateEngineMaterial(shader);

        crtPass = new CRTRenderPass
        {
            // Execute before pixel perfect upscaling
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing 
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null) return;

        // Check if the volume component exists and is active
        var settings = VolumeManager.instance.stack.GetComponent<CRTEffectSettings>();
        if (settings == null || !settings.IsActive()) return;

        // Pass data to the pass and enqueue it
        crtPass.Setup(material, settings);
        renderer.EnqueuePass(crtPass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
    }

    // The Render Pass now utilizes the Render Graph API
    class CRTRenderPass : ScriptableRenderPass
    {
        private Material material;
        private CRTEffectSettings settings;

        // Render Graph requires a temporary data class to pass variables into the render pipeline
        private class PassData
        {
            public TextureHandle sourceTexture;
            public Material material;
        }

        public void Setup(Material mat, CRTEffectSettings settings)
        {
            this.material = mat;
            this.settings = settings;
        }

        // This is the Unity 6+ replacement for the old "Execute" method
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid()) return;

            // 1. Pass properties to the shader from the Volume
            material.SetFloat("_Strength", settings.strength.value);
            material.SetFloat("_PixelsPerUnit", settings.pixelsPerUnit.value);
            material.SetFloat("_ScanlineIntensity", settings.scanlineIntensity.value);
            material.SetFloat("_Curvature", settings.curvature.value);
            material.SetFloat("_ColorBleed", settings.colorBleed.value);
            
            // 2. Setup a temporary texture to hold our modified image
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0; // We only need color, not depth
            TextureHandle tempTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "CRT_TempTexture", false);

            // 3. PASS 1: Apply the CRT Material to the temporary texture
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("CRT_ApplyMaterial", out var passData))
            {
                passData.sourceTexture = source;
                passData.material = material;

                builder.SetRenderAttachment(tempTexture, 0); // Output to Temp
                builder.UseTexture(source);                  // Read from Source

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.sourceTexture, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            // 4. PASS 2: Copy the modified image back to the main camera view
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("CRT_CopyBack", out var passData))
            {
                passData.sourceTexture = tempTexture;

                builder.SetRenderAttachment(source, 0);     // Output back to Source
                builder.UseTexture(tempTexture);            // Read from Temp

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.sourceTexture, new Vector4(1, 1, 0, 0), 0.0f, false);
                });
            }
        }
    }
}