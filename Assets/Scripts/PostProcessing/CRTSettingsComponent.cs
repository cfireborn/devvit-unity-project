using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable, VolumeComponentMenu("Custom/CRT Effect"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public class CRTEffectSettings : VolumeComponent, IPostProcessComponent
{
    public ClampedFloatParameter strength = new ClampedFloatParameter(1f, 0f, 1f);
    public FloatParameter pixelsPerUnit = new FloatParameter(100f);
    public ClampedFloatParameter scanlineIntensity = new ClampedFloatParameter(0.3f, 0f, 1f);
    public ClampedFloatParameter curvature = new ClampedFloatParameter(0.5f, 0f, 1f);
    public ClampedFloatParameter colorBleed = new ClampedFloatParameter(0.0015f, 0f, 0.01f);

    public bool IsActive() => strength.value > 0f;
    public bool IsTileCompatible() => false;
}