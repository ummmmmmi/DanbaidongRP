using System;

namespace UnityEngine.Rendering.Universal
{
    public enum ShadowScatterMode
    {
        None = 0,
        RampTexture = 1,
        SubSurface = 2,
    }

    public enum ScreenSpaceShadowCascadeDebugMode
    {
        None = 0,
        CascadeIndex = 1,
        BlendWeight = 2,
        CascadeDifference = 3,
    }

    /// <summary>
    /// 屏幕空间阴影级联调试模式参数。
    /// </summary>
    [Serializable]
    public sealed class ScreenSpaceShadowCascadeDebugModeParameter : VolumeParameter<ScreenSpaceShadowCascadeDebugMode>
    {
        /// <summary>
        /// 创建屏幕空间阴影级联调试模式参数。
        /// </summary>
        public ScreenSpaceShadowCascadeDebugModeParameter(ScreenSpaceShadowCascadeDebugMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class ShadowScatterModeParameter : VolumeParameter<ShadowScatterMode>
    {
        public ShadowScatterModeParameter(ShadowScatterMode value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    [Serializable, VolumeComponentMenu("Lighting/Shadows")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed partial class Shadows : VolumeComponent, IPostProcessComponent
    {
        private static Texture2D s_DefaultShadowRampTex;

        protected override void OnEnable()
        {
            base.OnEnable();

            if (s_DefaultShadowRampTex == null)
            {
                var runtimeTextures = GraphicsSettings.GetRenderPipelineSettings<UniversalRenderPipelineRuntimeTextures>();
                s_DefaultShadowRampTex = runtimeTextures.defaultDirShadowRampTex;
            }
        }

        [Tooltip("Use RayTracing for opaques.")]
        public BoolParameter rayTracing = new BoolParameter(false);

        [Tooltip("Controls the ray length for ray traced directional shadows.")]
        public MinFloatParameter dirShadowsRayLength = new MinFloatParameter(1000.0f, 0.01f);

        [Tooltip("Penumbra controls shadows soften width.")]
        public ClampedFloatParameter dirShadowPenumbra = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        [Tooltip("Controls character self shadows layer.")]
        public LayerMaskParameter characterLayerMask = new LayerMaskParameter(0);

        [Tooltip("Controls character face/hair normal offset.")]
        public ClampedFloatParameter characterNormalOffset = new ClampedFloatParameter(0.001f, 0.0f, 0.05f);

        [Tooltip("Controls character face/hair half dir scale.")]
        public ClampedFloatParameter characterHalfDirScale = new ClampedFloatParameter(0.8f, 0.0f, 1.0f);

        [Tooltip("Shadow intensity.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [Tooltip("Penumbra controls shadows soften width.")]
        public ClampedFloatParameter penumbra = new ClampedFloatParameter(1.0f, 0.001f, 3.0f);

        [Tooltip("Penumbra controls shadows soften width. (For Per Object Shadow)")]
        public ClampedFloatParameter perObjectShadowPenumbra = new ClampedFloatParameter(1.0f, 0.001f, 3.0f);

        [Tooltip("Debugs screen-space shadow cascade index, blend weight, or adjacent-cascade difference.")]
        public ScreenSpaceShadowCascadeDebugModeParameter cascadeDebugMode =
            new ScreenSpaceShadowCascadeDebugModeParameter(ScreenSpaceShadowCascadeDebugMode.None);

        [Tooltip("Adds short-range screen-space contact shadows to raster directional shadows.")]
        public BoolParameter contactShadows = new BoolParameter(false);

        [Tooltip("Maximum world-space ray length used to find nearby occluders.")]
        public ClampedFloatParameter contactShadowLength = new ClampedFloatParameter(0.5f, 0.01f, 5.0f);

        [Tooltip("Maximum camera distance where contact shadows are visible.")]
        public ClampedFloatParameter contactShadowDistance = new ClampedFloatParameter(20.0f, 0.1f, 100.0f);

        [Tooltip("Distance range used to fade contact shadows before the maximum distance.")]
        public ClampedFloatParameter contactShadowFadeDistance = new ClampedFloatParameter(5.0f, 0.01f, 50.0f);

        [Tooltip("World-space depth tolerance used by the screen-space ray test.")]
        public ClampedFloatParameter contactShadowThickness = new ClampedFloatParameter(0.05f, 0.001f, 0.5f);

        [Tooltip("World-space normal offset used to prevent self-intersection.")]
        public ClampedFloatParameter contactShadowNormalBias = new ClampedFloatParameter(0.02f, 0.0f, 0.2f);

        [Tooltip("Number of depth samples along each contact shadow ray.")]
        public ClampedIntParameter contactShadowSampleCount = new ClampedIntParameter(16, 4, 32);

        [Tooltip("Strength of the additional contact shadow occlusion.")]
        public ClampedFloatParameter contactShadowIntensity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [Tooltip("Randomizes sample positions. Ultra quality filters the changing pattern temporally.")]
        public ClampedFloatParameter contactShadowJitter = new ClampedFloatParameter(0.75f, 0.0f, 1.0f);

        [Tooltip("Normalized ray position where contact shadow fading begins.")]
        public ClampedFloatParameter contactShadowRayFadeStart = new ClampedFloatParameter(0.8f, 0.0f, 0.99f);

        [Tooltip("Shadow scatter mode.")]
        public ShadowScatterModeParameter shadowScatterMode = new ShadowScatterModeParameter(ShadowScatterMode.SubSurface);

        [Tooltip("Shadow ramp texture.")]
        public NoInterpTextureParameter shadowRampTex = new NoInterpTextureParameter(s_DefaultShadowRampTex);

        [Tooltip("Shadow subsurface R channel.")]
        public ClampedFloatParameter scatterR = new ClampedFloatParameter(0.3f, 0.01f, 1.0f);
        [Tooltip("Shadow subsurface G channel.")]
        public ClampedFloatParameter scatterG = new ClampedFloatParameter(0.1f, 0.01f, 1.0f);
        [Tooltip("Shadow subsurface B channel.")]
        public ClampedFloatParameter scatterB = new ClampedFloatParameter(0.07f, 0.01f, 1.0f);

        [Tooltip("Penumbra controls shadows scatter occlusion soften width.")]
        public ClampedFloatParameter occlusionPenumbra = new ClampedFloatParameter(1.0f, 0.001f, 3.0f);

        [Tooltip("Use ray tracing shadow denoiser.")]
        public BoolParameter denoiser = new BoolParameter(true);

        [Tooltip("Use Edge-Avoiding A-Trous Wavelet (EAW) filter.")]
        public BoolParameter edgeAvoidingWaveletBlur = new BoolParameter(true);

        /// <inheritdoc/>
        public bool IsActive() => true; // Always enable screenSpaceShadows.

        /// <inheritdoc/>
        [Obsolete("Unused #from(2023.1)", false)]
        public bool IsTileCompatible() => false;
    }
}
