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
        if (shader != null)
            material = CoreUtils.CreateEngineMaterial(shader);

        crtPass = new CRTRenderPass
        {
            // Changed to AfterRendering to avoid Pixel Perfect Camera conflicts
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null) return;

        var settings = VolumeManager.instance.stack.GetComponent<CRTEffectSettings>();
        if (settings == null || !settings.IsActive()) return;

        crtPass.Setup(material, settings);
        renderer.EnqueuePass(crtPass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
    }

    class CRTRenderPass : ScriptableRenderPass
    {
        private Material material;
        private CRTEffectSettings settings;

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

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid()) return;

            // Push the new variables to the shader
            material.SetFloat("_Strength", settings.strength.value);
            material.SetFloat("_PixelsPerUnit", settings.pixelsPerUnit.value);
            material.SetFloat("_HorizontalScanlineIntensity", settings.horizontalScanlineIntensity.value);
            material.SetFloat("_VerticalScanlineIntensity", settings.verticalScanlineIntensity.value);
            material.SetFloat("_Curvature", settings.curvature.value);
            material.SetFloat("_ColorBleed", settings.colorBleed.value);

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0; 
            TextureHandle tempTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "CRT_TempTexture", false);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("CRT_ApplyMaterial", out var passData))
            {
                passData.sourceTexture = source;
                passData.material = material;

                builder.SetRenderAttachment(tempTexture, 0); 
                builder.UseTexture(source);                  

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.sourceTexture, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("CRT_CopyBack", out var passData))
            {
                passData.sourceTexture = tempTexture;

                builder.SetRenderAttachment(source, 0);     
                builder.UseTexture(tempTexture);            

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.sourceTexture, new Vector4(1, 1, 0, 0), 0.0f, false);
                });
            }
        }
    }
}