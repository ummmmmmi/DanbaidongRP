using System;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Options to select a tonemapping algorithm to use for color grading.
    /// </summary>
    public enum TonemappingMode
    {
        /// <summary>
        /// Use this option if you do not want to apply tonemapping
        /// </summary>
        None,

        /// <summary>
        /// Use this option if you only want range-remapping with minimal impact on color hue and saturation.
        /// It is generally a great starting point for extensive color grading.
        /// </summary>
        Neutral,

        /// <summary>
        /// Use this option to apply a close approximation of the reference ACES tonemapper for a more filmic look.
        /// It is more contrasted than Neutral and has an effect on actual color hue and saturation.
        /// Note that if you use this tonemapper all the grading operations will be done in the ACES color spaces for optimal precision and results.
        /// </summary>
        ACES,

        /// <summary>
        /// Use a simplified ACES tonemapper variant.
        /// </summary>
        ACESSimpleVer,

        /// <summary>
        /// Use the Gran Turismo tonemapper.
        /// </summary>
        GranTurismo,

        /// <summary>
        /// Use an external 3D LUT sampled in LogC space as the tonemapper.
        /// </summary>
        External,
    }

    /// <summary>
    /// Available options for when HDR Output is enabled and Tonemap is set to Neutral.
    /// </summary>
    public enum NeutralRangeReductionMode
    {
        /// <summary>
        /// Simple Reinhard tonemapping curve.
        /// </summary>
        Reinhard = HDRRangeReduction.Reinhard,
        /// <summary>
        /// Range reduction curve as specified in the BT.2390 standard.
        /// </summary>
        BT2390 = HDRRangeReduction.BT2390
    }

    /// <summary>
    /// Preset used when selecting ACES tonemapping for HDR displays.
    /// </summary>
    public enum HDRACESPreset
    {
        /// <summary>
        /// Preset for a display with a maximum range of 1000 nits.
        /// </summary>
        ACES1000Nits = HDRRangeReduction.ACES1000Nits,
        /// <summary>
        /// Preset for a display with a maximum range of 2000 nits.
        /// </summary>
        ACES2000Nits = HDRRangeReduction.ACES2000Nits,
        /// <summary>
        /// Preset for a display with a maximum range of 4000 nits.
        /// </summary>
        ACES4000Nits = HDRRangeReduction.ACES4000Nits,
    }

    /// <summary>
    /// Tonemap mode to be used when outputting to HDR device and when the main mode is not supported on HDR.
    /// </summary>
    public enum FallbackHDRTonemap
    {
        /// <summary>
        /// No tonemapping.
        /// </summary>
        None = 0,

        /// <summary>
        /// Tonemapping mode with minimal impact on color hue and saturation.
        /// </summary>
        Neutral,

        /// <summary>
        /// ACES tonemapper for a more filmic look.
        /// </summary>
        ACES
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that contains a <see cref="NeutralRangeReductionMode"/> value.
    /// </summary>
    [Serializable]
    public sealed class NeutralRangeReductionModeParameter : VolumeParameter<NeutralRangeReductionMode>
    {
        /// <summary>
        /// Creates a new <see cref="NeutralRangeReductionModeParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public NeutralRangeReductionModeParameter(NeutralRangeReductionMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that contains a <see cref="HDRACESPreset"/> value.
    /// </summary>
    [Serializable]
    public sealed class HDRACESPresetParameter : VolumeParameter<HDRACESPreset>
    {
        /// <summary>
        /// Creates a new <see cref="HDRACESPresetParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public HDRACESPresetParameter(HDRACESPreset value, bool overrideState = false) : base(value, overrideState) { }
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that contains a <see cref="FallbackHDRTonemap"/> value.
    /// </summary>
    [Serializable]
    public sealed class FallbackHDRTonemapParameter : VolumeParameter<FallbackHDRTonemap>
    {
        /// <summary>
        /// Creates a new <see cref="FallbackHDRTonemapParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public FallbackHDRTonemapParameter(FallbackHDRTonemap value, bool overrideState = false) : base(value, overrideState) { }
    }

    /// <summary>
    /// A volume component that holds settings for the tonemapping effect.
    /// </summary>
    [Serializable, VolumeComponentMenu("Post-processing/Tonemapping")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("post-processing-tonemapping")]
    public sealed class Tonemapping : VolumeComponent, IPostProcessComponent
    {
        /// <summary>
        /// Use this to select a tonemapping algorithm to use for color grading.
        /// </summary>
        [Tooltip("Select a tonemapping algorithm to use for the color grading process.")]
        public TonemappingModeParameter mode = new TonemappingModeParameter(TonemappingMode.None);

        // -- HDR Output options --

        /// <summary>
        /// Specifies the range reduction mode used when HDR output is enabled and Neutral tonemapping is enabled.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Specifies the range reduction mode used when HDR output is enabled and Neutral tonemapping is enabled.")]
        public NeutralRangeReductionModeParameter neutralHDRRangeReductionMode = new NeutralRangeReductionModeParameter(NeutralRangeReductionMode.BT2390);

        /// <summary>
        /// Specifies the preset for HDR displays.
        /// </summary>
        [Tooltip("Use the ACES preset for HDR displays.")]
        public HDRACESPresetParameter acesPreset = new HDRACESPresetParameter(HDRACESPreset.ACES1000Nits);

        /// <summary>
        /// A custom 3D texture lookup table to apply.
        /// This parameter is only used when <see cref="TonemappingMode.External"/> is set.
        /// </summary>
        [Tooltip("A custom 3D texture lookup table to apply when External tonemapping is selected. The LUT size must match the URP Color Grading LUT Size.")]
        public Texture3DParameter lutTexture = new Texture3DParameter(null);

        /// <summary>
        /// How much of the lookup texture will contribute to the tonemapping effect.
        /// This parameter is only used when <see cref="TonemappingMode.External"/> is set.
        /// </summary>
        [Tooltip("How much of the lookup texture will contribute to the tonemapping effect.")]
        public ClampedFloatParameter lutContribution = new ClampedFloatParameter(1f, 0f, 1f);

        /// <summary>
        /// Specifies the fallback tonemapping algorithm to use when outputting to an HDR device, when the main mode is not supported.
        /// </summary>
        [Tooltip("Specifies the fallback tonemapping algorithm to use when outputting to an HDR device, when the selected mode is not supported.")]
        public FallbackHDRTonemapParameter fallbackMode = new FallbackHDRTonemapParameter(FallbackHDRTonemap.Neutral);

        /// <summary>
        /// Specify how much hue to preserve. Values closer to 0 are likely to preserve hue. As values get closer to 1, Unity doesn't correct hue shifts.
        /// </summary>
        [Tooltip("Specify how much hue to preserve. Values closer to 0 are likely to preserve hue. As values get closer to 1, Unity doesn't correct hue shifts.")]
        public ClampedFloatParameter hueShiftAmount = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        /// <summary>
        /// Enable to use values detected from the output device as paper white. When enabled, output images might differ between SDR and HDR. For best accuracy, set this value manually.
        /// </summary>
        [Tooltip("Enable to use values detected from the output device as paper white. When enabled, output images might differ between SDR and HDR. For best accuracy, set this value manually.")]
        public BoolParameter detectPaperWhite = new BoolParameter(false);

        /// <summary>
        /// The reference brightness of a paper white surface. This property determines the maximum brightness of UI. The brightness of the scene is scaled relative to this value. The value is in nits.
        /// </summary>
        [Tooltip("The reference brightness of a paper white surface. This property determines the maximum brightness of UI. The brightness of the scene is scaled relative to this value. The value is in nits.")]
        public ClampedFloatParameter paperWhite = new ClampedFloatParameter(300.0f, 0.0f, 400.0f);

        /// <summary>
        /// Enable to use the minimum and maximum brightness values detected from the output device. For best accuracy, considering calibrating these values manually.
        /// </summary>
        [Tooltip("Enable to use the minimum and maximum brightness values detected from the output device. For best accuracy, considering calibrating these values manually.")]
        public BoolParameter detectBrightnessLimits = new BoolParameter(true);

        /// <summary>
        /// The minimum brightness of the screen (in nits). This value is assumed to be 0.005f with ACES Tonemap.
        /// </summary>
        [Tooltip("The minimum brightness of the screen (in nits). This value is assumed to be 0.005f with ACES Tonemap.")]
        public ClampedFloatParameter minNits = new ClampedFloatParameter(0.005f, 0.0f, 50.0f);

        /// <summary>
        /// The maximum brightness of the screen (in nits). This value is defined by the preset when using ACES Tonemap.
        /// </summary>
        [Tooltip("The maximum brightness of the screen (in nits). This value is defined by the preset when using ACES Tonemap.")]
        public ClampedFloatParameter maxNits = new ClampedFloatParameter(1000.0f, 0.0f, 5000.0f);

        #region GT ToneMapping
        /// <summary>
        /// The maximum brightness of the screen.
        /// </summary>
        [Tooltip("The maximum brightness of the screen.")]
        public ClampedFloatParameter maxBrightness = new ClampedFloatParameter(1.0f, 1.0f, 20.0f);

        /// <summary>
        /// The contrast GT Tonemapping.
        /// </summary>
        [Tooltip("The contrast.")]
        public ClampedFloatParameter contrast = new ClampedFloatParameter(1.11f, 0.0f, 5.0f);

        /// <summary>
        /// Linear section start. This controls linear start point in 0.0-1.0.
        /// </summary>
        [Tooltip("Linear section start. This controls linear start point in 0.0-1.0.")]
        public ClampedFloatParameter linearSectionStart = new ClampedFloatParameter(0.2f, 0.0f, 1.0f);

        /// <summary>
        /// Linear section Length. This controls linear length.
        /// </summary>
        [Tooltip("Linear section Length. This controls linear length.")]
        public ClampedFloatParameter linearSectionLength = new ClampedFloatParameter(0.4f, 0.0f, 1.0f);

        /// <summary>
        /// Black tightness pow. Pow of curve that before linearSectionStart (Dark part).
        /// </summary>
        [Tooltip("Black tightness pow. Pow of curve that before linearSectionStart (Dark part).")]
        public ClampedFloatParameter blackPow = new ClampedFloatParameter(1.29f, 1.0f, 3.0f);

        /// <summary>
        /// Black tightness min. Add of curve that before linearSectionStart (Dark part).
        /// </summary>
        [Tooltip("Black tightness min. Add of curve that before linearSectionStart (Dark part).")]
        public ClampedFloatParameter blackMin = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        #endregion

        /// <inheritdoc/>
        public bool IsActive()
        {
            if (mode.value == TonemappingMode.External)
                return ValidateLUT() && lutContribution.value > 0f;

            return mode.value != TonemappingMode.None;
        }

        internal TonemappingMode GetHDRTonemappingMode()
        {
            if (mode.value == TonemappingMode.External)
            {
                if (fallbackMode.value == FallbackHDRTonemap.None)
                    return TonemappingMode.None;

                if (fallbackMode.value == FallbackHDRTonemap.ACES)
                    return TonemappingMode.ACES;

                return TonemappingMode.Neutral;
            }

            return mode.value;
        }

        /// <summary>
        /// Validates the format and size of the LUT texture set in <see cref="lutTexture"/>.
        /// </summary>
        /// <returns><c>true</c> if the LUT is valid, <c>false</c> otherwise.</returns>
        public bool ValidateLUT()
        {
            var urpAsset = UniversalRenderPipeline.asset;
            if (urpAsset == null || lutTexture.value == null)
                return false;

            if (lutTexture.value.width != urpAsset.colorGradingLutSize)
                return false;

            switch (lutTexture.value)
            {
                case Texture3D texture3D:
                    return texture3D.width == texture3D.height &&
                        texture3D.height == texture3D.depth;
                case RenderTexture renderTexture:
                    return renderTexture.dimension == TextureDimension.Tex3D &&
                        renderTexture.width == renderTexture.height &&
                        renderTexture.height == renderTexture.volumeDepth;
                default:
                    return false;
            }
        }

        /// <inheritdoc/>
        [Obsolete("Unused #from(2023.1)", false)]
        public bool IsTileCompatible() => true;
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="TonemappingMode"/> value.
    /// </summary>
    [Serializable]
    public sealed class TonemappingModeParameter : VolumeParameter<TonemappingMode>
    {
        /// <summary>
        /// Creates a new <see cref="TonemappingModeParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public TonemappingModeParameter(TonemappingMode value, bool overrideState = false) : base(value, overrideState) { }
    }
}
