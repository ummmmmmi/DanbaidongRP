using System.Collections.Generic;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    internal class ScreenSpaceDirectionalShadowsPass : ScriptableRenderPass
    {
        // Profiling tag
        private static string m_SSDSClassifyTilesProfilerTag            = "SSDS ClassifyTiles";
        private static string m_SSDS_RTRT_ClassifyTilesProfilerTag      = "SSDS RTRT ClassifyTiles";
        private static string m_RayTracingShadowsProfilerTag            = "RayTracingShadows";
        private static string m_SSDS_PCSS_ProfilerTag                   = "SSDS PCSS";
        private static string m_SSDS_AccumulateProfilerTag              = "SSDS Accumulate";
        private static string m_SSDS_EAWProfilerTag                     = "SSDS EdgeAvoidATrousWavelet";
        private static ProfilingSampler m_SSDSClassifyTilesProfilingSampler = new ProfilingSampler(m_SSDSClassifyTilesProfilerTag);
        private static ProfilingSampler m_SSDS_PCSS_ProfilingSampler = new ProfilingSampler(m_SSDS_PCSS_ProfilerTag);
        private static ProfilingSampler m_RayTracingShadowsProfilingSampler = new ProfilingSampler(m_RayTracingShadowsProfilerTag);
        private static ProfilingSampler m_SSDS_RTRT_ClassifyTiles_ProfilingSampler = new ProfilingSampler(m_SSDS_RTRT_ClassifyTilesProfilerTag);
        private static ProfilingSampler m_SSDS_AccumulateProfilingSampler = new ProfilingSampler(m_SSDS_AccumulateProfilerTag);
        private static ProfilingSampler m_SSDS_EAWProfilingSampler = new ProfilingSampler(m_SSDS_EAWProfilerTag);
        private static readonly ProfilingSampler s_ContactShadowsProfilingSampler = new ProfilingSampler("SSDS Contact Shadows");

        // Public Variables

        // Private Variables
        private ComputeShader m_ScreenSpaceDirectionalShadowsCS;
        private ComputeShader m_ShadowDenoiserCS;
        private int m_ClassifyTilesKernel;
        private int m_RayTracingClassifyTilesKernel;
        private int m_SSShadowsKernel;
        private int m_BilateralHKernel;
        private int m_BilateralVKernel;
        private int m_AccumulateKernel;
        private int m_AccumulateFinalKernel;
        private int m_RasterAccumulateKernel;
        private int m_EdgeAvoidATrousWaveletKernel;
        private int m_ContactShadowsKernel;
        private readonly Dictionary<int, (int frame, int width, int height)> m_RasterShadowHistoryStates = new();

        // Constants
        private const int c_screenSpaceShadowsTileSize = 16;

        // Statics


        public ScreenSpaceDirectionalShadowsPass(RenderPassEvent evt, ComputeShader ssDirectionalShadowsCS, ComputeShader shadowDenoiserCS)
        {
            base.renderPassEvent = evt;
            m_ScreenSpaceDirectionalShadowsCS = ssDirectionalShadowsCS;
            m_ShadowDenoiserCS = shadowDenoiserCS;

            m_ClassifyTilesKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("ShadowClassifyTiles");
            m_RayTracingClassifyTilesKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("RayTracingShadowClassifyTiles");
            m_SSShadowsKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("ScreenSpaceShadowmap");

            m_BilateralHKernel = m_ShadowDenoiserCS.FindKernel("BilateralFilterH");
            m_BilateralVKernel = m_ShadowDenoiserCS.FindKernel("BilateralFilterV");
            m_AccumulateKernel = m_ShadowDenoiserCS.FindKernel("ShadowAccumulate");
            m_AccumulateFinalKernel = m_ShadowDenoiserCS.FindKernel("ShadowAccumulateFinal");
            m_RasterAccumulateKernel = m_ShadowDenoiserCS.FindKernel("RasterShadowAccumulate");
            m_EdgeAvoidATrousWaveletKernel = m_ShadowDenoiserCS.FindKernel("EdgeAvoidATrousWavelet");
            m_ContactShadowsKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("ContactShadows");
        }

        static RTHandle HistoryTracedShadowTextureAllocator(RenderTextureDescriptor desc, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            // Must use scaleFactor
            return rtHandleSystem.Alloc(Vector2.one, TextureXR.slices, colorFormat: desc.graphicsFormat,
                 filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_TracedShadowTexture{1}", viewName, frameIndex));
        }

        internal void ReAllocatedTracedShadowTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, UniversalCameraData cameraData, RenderTextureDescriptor desc, out RTHandle currFrameRT, out RTHandle prevFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.RaytracedShadow);

            if (curTexture == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.RaytracedShadow);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.RaytracedShadow, cameraData.camera.name
                                                            , HistoryTracedShadowTextureAllocator, desc, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.RaytracedShadow);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.RaytracedShadow);
        }

        static RTHandle HistoryShadowMomentsTextureAllocator(RenderTextureDescriptor desc, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            // Must use scaleFactor
            return rtHandleSystem.Alloc(Vector2.one, TextureXR.slices, colorFormat: desc.graphicsFormat,
                 filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_TracedShadowMomentsTexture{1}", viewName, frameIndex));
        }

        /// <summary>
        /// 分配 Raster 阴影法线与级联索引历史纹理。
        /// </summary>
        static RTHandle HistoryShadowMetadataTextureAllocator(RenderTextureDescriptor desc, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {
            frameIndex &= 1;

            return rtHandleSystem.Alloc(Vector2.one, TextureXR.slices, colorFormat: desc.graphicsFormat,
                filterMode: FilterMode.Point, enableRandomWrite: true, useDynamicScale: true,
                name: string.Format("{0}_ScreenSpaceShadowMetadataTexture{1}", viewName, frameIndex));
        }

        internal void ReAllocatedShadowMomentsTextureIfNeeded(HistoryFrameRTSystem historyRTSystem, UniversalCameraData cameraData, RenderTextureDescriptor desc, out RTHandle currFrameRT, out RTHandle prevFrameRT)
        {
            var curTexture = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.RaytracedShadowMoments);

            if (curTexture == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.RaytracedShadowMoments);

                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.RaytracedShadowMoments, cameraData.camera.name
                                                            , HistoryShadowMomentsTextureAllocator, desc, 2);
            }

            currFrameRT = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.RaytracedShadowMoments);
            prevFrameRT = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.RaytracedShadowMoments);
        }

        /// <summary>
        /// 为 Raster Ultra 阴影分配独立的颜色、矩和几何元数据双缓冲历史。
        /// </summary>
        private bool ReAllocateRasterShadowHistoryIfNeeded(HistoryFrameRTSystem historyRTSystem,
            UniversalCameraData cameraData, RenderTextureDescriptor shadowDesc,
            out RTHandle currentShadow, out RTHandle previousShadow,
            out RTHandle currentMoments, out RTHandle previousMoments,
            out RTHandle currentMetadata, out RTHandle previousMetadata)
        {
            bool historyAllocated = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadow) != null &&
                historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadowMoments) != null &&
                historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadowMetadata) != null;

            if (historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadow) == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadow);
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ScreenSpaceDirectionalShadow,
                    cameraData.camera.name, HistoryTracedShadowTextureAllocator, shadowDesc, 2);
            }

            RenderTextureDescriptor momentsDesc = shadowDesc;
            momentsDesc.colorFormat = RenderTextureFormat.RGB111110Float;
            if (historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadowMoments) == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadowMoments);
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ScreenSpaceDirectionalShadowMoments,
                    cameraData.camera.name, HistoryShadowMomentsTextureAllocator, momentsDesc, 2);
            }

            RenderTextureDescriptor metadataDesc = shadowDesc;
            metadataDesc.colorFormat = RenderTextureFormat.ARGBHalf;
            if (historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadowMetadata) == null)
            {
                historyRTSystem.ReleaseHistoryFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadowMetadata);
                historyRTSystem.AllocHistoryFrameRT((int)HistoryFrameType.ScreenSpaceDirectionalShadowMetadata,
                    cameraData.camera.name, HistoryShadowMetadataTextureAllocator, metadataDesc, 2);
            }

            currentShadow = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadow);
            previousShadow = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadow);
            currentMoments = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadowMoments);
            previousMoments = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadowMoments);
            currentMetadata = historyRTSystem.GetCurrentFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadowMetadata);
            previousMetadata = historyRTSystem.GetPreviousFrameRT(HistoryFrameType.ScreenSpaceDirectionalShadowMetadata);

            int cameraId = cameraData.camera.GetInstanceID();
            int currentFrame = historyRTSystem.historyFrameCount;
            bool consecutiveFrame = m_RasterShadowHistoryStates.TryGetValue(cameraId, out var previousState) &&
                previousState.frame == currentFrame - 1 &&
                previousState.width == shadowDesc.width && previousState.height == shadowDesc.height;
            m_RasterShadowHistoryStates[cameraId] = (currentFrame, shadowDesc.width, shadowDesc.height);

            return historyAllocated && consecutiveFrame;
        }

        private class PassData
        {
            // Compute shader
            internal ComputeShader cs;
            internal int classifyTilesKernel;
            internal int rayTracingClassifyKernel;
            internal int shadowmapKernel;
            internal ComputeShader denoiserCS;
            internal int bilateralHKernel;
            internal int bilateralVKernel;
            internal int accumulateKernel;
            internal int accumulateFinalKernel;
            internal int rasterAccumulateKernel;
            internal int eawKernel;
            internal int contactShadowsKernel;

            internal int numTilesX;
            internal int numTilesY;

            // Compute Buffers
            internal BufferHandle dispatchIndirectBuffer;
            internal BufferHandle tileListBuffer;
            internal BufferHandle dispatchRaysIndirectBuffer;
            internal BufferHandle raysCoordBuffer;

            // Texture
            internal TextureHandle dirShadowmapTex;
            internal TextureHandle screenSpaceShadowmapTex;
            internal Vector2Int screenSpaceShadowmapSize;
            internal TextureHandle tracedShadowTex;
            internal TextureHandle prevTracedShadowTex;
            internal TextureHandle shadowMomentsTex;
            internal TextureHandle prevShadowMomentsTex;
            internal TextureHandle normalGBuffer;
            internal TextureHandle materialGBuffer;
            internal TextureHandle stencilHandle;
            internal TextureHandle motionVectorTexture;
            internal TextureHandle prevCameraDepthTexture;
            internal TextureHandle meanVarianceTexture;
            internal TextureHandle shadowMetadataTex;
            internal TextureHandle prevShadowMetadataTex;

            internal int camHistoryFrameCount;
            internal int cascadeDebugMode;
            internal TextureHandle blueNoiseArray;

            // Ray Tracing
            internal bool requireRayTracing;
            internal RayTracingShader rtrtShader;
            internal RayTracingAccelerationStructure rtas;
            internal uint dispatchRaySizeX;
            internal uint dispatchRaySizeY;
            internal ShaderVariablesRaytracing rayTracingCB;
            internal float rayTracingDirShadowPenumbra;
            internal float rayTracingDirShadowCharNormalOffset;
            internal float rayTracingDirShadowCharHalfDirScale;
            internal bool enableDenoiser;
            internal bool enableEAWBlur;
            internal bool enableRasterDenoiser;
            internal bool shadowHistoryValid;
            internal bool hasMotionData;
            internal bool enableContactShadows;

            internal Vector4 contactShadowParams0;
            internal Vector4 contactShadowParams1;
            internal int contactShadowSampleCount;

            internal Matrix4x4 clipToPrevClipMatrix;
        }

        /// <summary>
        /// Initialize the shared pass data.
        /// </summary>
        /// <param name="passData"></param>
        private void InitPassData(RenderGraph renderGraph, PassData passData, UniversalCameraData cameraData, UniversalResourceData resourceData, int historyFramCount)
        {
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.colorFormat = RenderTextureFormat.R16;
            desc.depthBufferBits = 0;
            desc.enableRandomWrite = true;

            passData.cs = m_ScreenSpaceDirectionalShadowsCS;
            passData.classifyTilesKernel = m_ClassifyTilesKernel;
            passData.rayTracingClassifyKernel = m_RayTracingClassifyTilesKernel;
            passData.shadowmapKernel = m_SSShadowsKernel;

            passData.denoiserCS = m_ShadowDenoiserCS;
            passData.bilateralHKernel = m_BilateralHKernel;
            passData.bilateralVKernel = m_BilateralVKernel;
            passData.accumulateKernel = m_AccumulateKernel;
            passData.accumulateFinalKernel = m_AccumulateFinalKernel;
            passData.rasterAccumulateKernel = m_RasterAccumulateKernel;
            passData.eawKernel = m_EdgeAvoidATrousWaveletKernel;
            passData.contactShadowsKernel = m_ContactShadowsKernel;

            passData.camHistoryFrameCount = historyFramCount;
            Shadows shadowSettings = VolumeManager.instance.stack.GetComponent<Shadows>();
            passData.cascadeDebugMode = shadowSettings != null && shadowSettings.active
                ? (int)shadowSettings.cascadeDebugMode.value
                : 0;
            passData.blueNoiseArray = resourceData.blueNoise128RG;

            var width = cameraData.cameraTargetDescriptor.width;
            var height = cameraData.cameraTargetDescriptor.height;
            passData.numTilesX = RenderingUtils.DivRoundUp(width, c_screenSpaceShadowsTileSize);
            passData.numTilesY = RenderingUtils.DivRoundUp(height, c_screenSpaceShadowsTileSize);

            var bufferSystem = GraphicsBufferSystem.instance;
            var dispatchIndirectBuffer = bufferSystem.GetGraphicsBuffer<uint>(GraphicsBufferSystemBufferID.ScreenSpaceShadowIndirect, 3, "dispatchIndirectBuffer", GraphicsBuffer.Target.IndirectArguments);
            passData.dispatchIndirectBuffer = renderGraph.ImportBuffer(dispatchIndirectBuffer, bufferName: "Shadow dispatch indirect");
            passData.tileListBuffer = renderGraph.CreateBuffer(new BufferDesc(passData.numTilesX * passData.numTilesY, sizeof(uint), "ShadowTileListBuffer"));

            var dispatchRaysIndirectBuffer = bufferSystem.GetGraphicsBuffer<uint>(GraphicsBufferSystemBufferID.RTShadowRaysIndirect, 3, "dispatchRaysIndirectBuffer", GraphicsBuffer.Target.IndirectArguments);
            passData.dispatchRaysIndirectBuffer = renderGraph.ImportBuffer(dispatchRaysIndirectBuffer, bufferName: "ShadowRay dispatch indirect");
            passData.raysCoordBuffer = renderGraph.CreateBuffer(new BufferDesc(desc.width * desc.height, sizeof(uint), "ShadowRaysCoordBuffer"));


            passData.dirShadowmapTex = resourceData.directionalShadowsTexture;


            passData.screenSpaceShadowmapTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ScreenSpaceShadowmapTexture", true, Color.white);
            passData.screenSpaceShadowmapSize = new Vector2Int(desc.width, desc.height);


            passData.normalGBuffer = resourceData.gBuffer[2]; // Normal GBuffer
            passData.materialGBuffer = resourceData.gBuffer[0];
            passData.stencilHandle = resourceData.activeDepthTexture;
            passData.motionVectorTexture = resourceData.motionVectorColor;




            MotionVectorsPersistentData motionData = null;
            passData.clipToPrevClipMatrix = Matrix4x4.identity;
            if (cameraData.camera.TryGetComponent<UniversalAdditionalCameraData>(out var additionalCameraData))
                motionData = additionalCameraData.motionVectorsPersistentData;
            if (motionData != null)
            {
                passData.clipToPrevClipMatrix = motionData.previousViewProjection * Matrix4x4.Inverse(motionData.viewProjection);
                passData.hasMotionData = true;
            }

        }

        private void InitRayTracingPassData(RenderGraph renderGraph, PassData passData, UniversalCameraData cameraData, UniversalResourceData resourceData)
        {
            var stack = VolumeManager.instance.stack;
            var volumeSettings = stack.GetComponent<Shadows>();
            if (volumeSettings == null)
            {
                passData.requireRayTracing = false;
                return;
            }

            passData.requireRayTracing &= volumeSettings.rayTracing.value;

            if (passData.requireRayTracing)
            {
                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.colorFormat = RenderTextureFormat.R16;
                desc.depthBufferBits = 0;
                desc.enableRandomWrite = true;

                var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<UniversalRenderPipelineRuntimeShaders>();
                passData.rtrtShader = runtimeShaders.rayTracingShadows;
                passData.rtas = cameraData.rayTracingSystem.RequestAccelerationStructure();

                var width = cameraData.cameraTargetDescriptor.width;
                var height = cameraData.cameraTargetDescriptor.height;
                passData.dispatchRaySizeX = (uint)width;
                passData.dispatchRaySizeY = (uint)height;

                // RayTracing constant buffer
                {
                    var rayTracingSettings = stack.GetComponent<RayTracingSettings>();

                    passData.rayTracingCB = cameraData.rayTracingSystem.GetShaderVariablesRaytracingCB(new Vector2Int(width, height), rayTracingSettings);
                    passData.rayTracingCB._RaytracingRayMaxLength = Mathf.Min(volumeSettings.dirShadowsRayLength.value, rayTracingSettings.directionalShadowRayLength.value);
                    passData.rayTracingCB._RayTracingClampingFlag = 1;
                    passData.rayTracingCB._RaytracingIntensityClamp = 1.0f;
                    passData.rayTracingCB._RaytracingPreExposition = 0;
                    passData.rayTracingCB._RayTracingDiffuseLightingOnly = 0;
                    passData.rayTracingCB._RayTracingAPVRayMiss = 0;
                    passData.rayTracingCB._RayTracingRayMissFallbackHierarchy = 0;
                    passData.rayTracingCB._RayTracingRayMissUseAmbientProbeAsSky = 0;
                    passData.rayTracingCB._RayTracingLastBounceFallbackHierarchy = 0;
                    passData.rayTracingCB._RayTracingAmbientProbeDimmer = 1.0f;
                }

                // Other settings
                passData.rayTracingDirShadowPenumbra = volumeSettings.dirShadowPenumbra.value;
                passData.rayTracingDirShadowCharHalfDirScale = volumeSettings.characterHalfDirScale.value;
                passData.rayTracingDirShadowCharNormalOffset = volumeSettings.characterNormalOffset.value;
                passData.enableDenoiser = volumeSettings.denoiser.value;
                passData.enableEAWBlur = volumeSettings.edgeAvoidingWaveletBlur.value;


                // Import history texture.
                var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);
                RTHandle prevCamDepthTexture = historyRTSystem?.GetPreviousFrameRT(HistoryFrameType.Depth);
                if (prevCamDepthTexture == null)
                {
                    passData.prevCameraDepthTexture = resourceData.cameraDepthTexture;
                }
                else
                {
                    passData.prevCameraDepthTexture = renderGraph.ImportTexture(prevCamDepthTexture);
                }

                RTHandle tracedShadowTexture, prevTracedShadowTexture;
                ReAllocatedTracedShadowTextureIfNeeded(historyRTSystem, cameraData, desc, out tracedShadowTexture, out prevTracedShadowTexture);
                passData.tracedShadowTex = renderGraph.ImportTexture(tracedShadowTexture);
                passData.prevTracedShadowTex = renderGraph.ImportTexture(prevTracedShadowTexture);


                var shadowMomentsDesc = desc;
                shadowMomentsDesc.colorFormat = RenderTextureFormat.RGB111110Float;
                RTHandle shadowMomentsTexture, prevshadowMomentsTexture;
                ReAllocatedShadowMomentsTextureIfNeeded(historyRTSystem, cameraData, shadowMomentsDesc, out shadowMomentsTexture, out prevshadowMomentsTexture);
                passData.shadowMomentsTex = renderGraph.ImportTexture(shadowMomentsTexture);
                passData.prevShadowMomentsTex = renderGraph.ImportTexture(prevshadowMomentsTexture);

                var meanVarianceDesc = desc;
                meanVarianceDesc.colorFormat = RenderTextureFormat.RG32;
                passData.meanVarianceTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, meanVarianceDesc, "_MeanVarianceTexture", true, Color.white);
            }
        }

        /// <summary>
        /// 判断当前主方向光是否需要 Raster Ultra 时空阴影降噪。
        /// </summary>
        private static bool IsRasterShadowDenoiserRequired(UniversalLightData lightData, UniversalShadowData shadowData)
        {
            if (UniversalRenderPipeline.asset?.softShadowQuality != SoftShadowQuality.Ultra ||
                !shadowData.supportsMainLightShadows || !shadowData.supportsSoftShadows ||
                lightData.mainLightIndex < 0)
                return false;

            Light mainLight = lightData.visibleLights[lightData.mainLightIndex].light;
            return mainLight != null && mainLight.shadows == LightShadows.Soft &&
                ShadowUtils.SoftShadowQualityToShaderProperty(mainLight, true) >= (float)SoftShadowQuality.Ultra;
        }

        /// <summary>
        /// 初始化 Raster Ultra 阴影降噪所需的历史纹理和临时纹理。
        /// </summary>
        private void InitRasterDenoiserPassData(RenderGraph renderGraph, PassData passData,
            UniversalCameraData cameraData, UniversalResourceData resourceData)
        {
            RenderTextureDescriptor shadowDesc = cameraData.cameraTargetDescriptor;
            shadowDesc.colorFormat = RenderTextureFormat.R16;
            shadowDesc.depthBufferBits = 0;
            shadowDesc.enableRandomWrite = true;

            HistoryFrameRTSystem historyRTSystem = HistoryFrameRTSystem.GetOrCreate(cameraData.camera);
            RTHandle previousDepth = historyRTSystem?.GetPreviousFrameRT(HistoryFrameType.Depth);
            bool hasPreviousDepth = previousDepth != null;
            passData.prevCameraDepthTexture = hasPreviousDepth
                ? renderGraph.ImportTexture(previousDepth)
                : resourceData.cameraDepthTexture;

            passData.shadowHistoryValid = ReAllocateRasterShadowHistoryIfNeeded(historyRTSystem,
                cameraData, shadowDesc,
                out RTHandle currentShadow, out RTHandle previousShadow,
                out RTHandle currentMoments, out RTHandle previousMoments,
                out RTHandle currentMetadata, out RTHandle previousMetadata) &&
                hasPreviousDepth && passData.hasMotionData;

            passData.tracedShadowTex = renderGraph.ImportTexture(currentShadow);
            passData.prevTracedShadowTex = renderGraph.ImportTexture(previousShadow);
            passData.shadowMomentsTex = renderGraph.ImportTexture(currentMoments);
            passData.prevShadowMomentsTex = renderGraph.ImportTexture(previousMoments);
            passData.shadowMetadataTex = renderGraph.ImportTexture(currentMetadata);
            passData.prevShadowMetadataTex = renderGraph.ImportTexture(previousMetadata);

            RenderTextureDescriptor meanVarianceDesc = shadowDesc;
            meanVarianceDesc.colorFormat = RenderTextureFormat.RG32;
            passData.meanVarianceTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph,
                meanVarianceDesc, "_RasterShadowMeanVarianceTexture", true, Color.white);
            passData.enableEAWBlur = true;
        }

        /// <summary>
        /// 初始化 Raster 主方向光的近距离接触阴影参数。
        /// </summary>
        private static void InitContactShadowsPassData(PassData passData, UniversalLightData lightData,
            UniversalShadowData shadowData)
        {
            passData.enableContactShadows = false;

            Shadows settings = VolumeManager.instance.stack.GetComponent<Shadows>();
            if (settings == null || !settings.active || !settings.contactShadows.value ||
                !shadowData.supportsMainLightShadows || lightData.mainLightIndex < 0)
                return;

            Light mainLight = lightData.visibleLights[lightData.mainLightIndex].light;
            if (mainLight == null || mainLight.shadows == LightShadows.None)
                return;

            float maxDistance = settings.contactShadowDistance.value;
            float fadeDistance = Mathf.Min(settings.contactShadowFadeDistance.value, maxDistance);
            passData.contactShadowParams0 = new Vector4(settings.contactShadowLength.value, maxDistance,
                fadeDistance, settings.contactShadowThickness.value);
            passData.contactShadowParams1 = new Vector4(settings.contactShadowIntensity.value,
                settings.contactShadowNormalBias.value, settings.contactShadowJitter.value,
                settings.contactShadowRayFadeStart.value);
            passData.contactShadowSampleCount = settings.contactShadowSampleCount.value;
            passData.enableContactShadows = settings.contactShadowIntensity.value > 0.0f;
        }

        private static void ExecuteRayTracingShadowsPass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            bool needDenoiser = data.enableDenoiser && data.rayTracingDirShadowPenumbra != 0;
            bool needEAWFilter = data.enableEAWBlur;
            cmd.SetComputeIntParam(data.cs, ShaderConstants._RasterShadowDenoiser, 0);

            using (new ProfilingScope(cmd, m_SSDS_RTRT_ClassifyTiles_ProfilingSampler))
            {
                cmd.SetComputeBufferParam(data.cs, data.rayTracingClassifyKernel, ShaderConstants.g_DispatchIndirectBuffer, data.dispatchIndirectBuffer);
                cmd.SetComputeBufferParam(data.cs, data.rayTracingClassifyKernel, ShaderConstants.g_TileList, data.tileListBuffer);

                cmd.SetComputeBufferParam(data.cs, data.rayTracingClassifyKernel, ShaderConstants._ShadowRayIndirectBuffer, data.dispatchRaysIndirectBuffer);
                cmd.SetComputeBufferParam(data.cs, data.rayTracingClassifyKernel, ShaderConstants._ShadowRayCoordBuffer, data.raysCoordBuffer);

                cmd.SetComputeTextureParam(data.cs, data.rayTracingClassifyKernel, ShaderConstants._DirShadowmapTexture, data.dirShadowmapTex);
                cmd.SetComputeTextureParam(data.cs, data.rayTracingClassifyKernel, ShaderConstants._SSDirShadowmapTexture, data.screenSpaceShadowmapTex);
                cmd.SetComputeTextureParam(data.cs, data.rayTracingClassifyKernel, ShaderConstants._TracedShadowTexture, data.tracedShadowTex);
                cmd.SetComputeTextureParam(data.cs, data.rayTracingClassifyKernel, ShaderConstants._StencilTexture, data.stencilHandle, 0, RenderTextureSubElement.Stencil);
                cmd.SetComputeTextureParam(data.cs, data.rayTracingClassifyKernel, ShaderConstants._ShadowMomentstexture, data.shadowMomentsTex);
                cmd.SetComputeTextureParam(data.cs, data.rayTracingClassifyKernel, ShaderConstants._GBuffer0, data.materialGBuffer);
                cmd.SetComputeTextureParam(data.cs, data.rayTracingClassifyKernel, ShaderConstants._GBuffer2, data.normalGBuffer);

                cmd.DispatchCompute(data.cs, data.rayTracingClassifyKernel, data.numTilesX, data.numTilesY, 1);
            }

            using (new ProfilingScope(cmd, m_RayTracingShadowsProfilingSampler))
            {
                BlueNoiseSystem.BindSTBNParams(BlueNoiseTexFormat._128RG, cmd, data.rtrtShader, data.blueNoiseArray, data.camHistoryFrameCount);
                // Define the shader pass to use for the shadow pass
                cmd.SetRayTracingShaderPass(data.rtrtShader, "VisibilityDXR");

                // Set the acceleration structure for the pass
                cmd.SetRayTracingAccelerationStructure(data.rtrtShader, "_RaytracingAccelerationStructure", data.rtas);

                // Set ConstantBuffer
                ConstantBuffer.PushGlobal(cmd, data.rayTracingCB, RayTracingSystem._ShaderVariablesRaytracing);
                cmd.SetRayTracingFloatParam(data.rtrtShader, ShaderConstants._RayTracingDirShadowPenumbraCOS, Mathf.Cos(data.rayTracingDirShadowPenumbra * 45 *Mathf.Deg2Rad));
                cmd.SetRayTracingFloatParam(data.rtrtShader, ShaderConstants._RayTracingDirShadowCharacterNormalOffset, data.rayTracingDirShadowCharNormalOffset);
                cmd.SetRayTracingFloatParam(data.rtrtShader, ShaderConstants._RayTracingDirShadowCharacterHalfDirScale, data.rayTracingDirShadowCharHalfDirScale);
                cmd.SetRayTracingFloatParam(data.rtrtShader, ShaderConstants._CamHistoryFrameCount, data.camHistoryFrameCount);

                // Set Textures & Buffers
                cmd.SetRayTracingBufferParam(data.rtrtShader, ShaderConstants._ShadowRayCoordBuffer, data.raysCoordBuffer);
                cmd.SetRayTracingTextureParam(data.rtrtShader, ShaderConstants._RayTracingShadowsTextureRW, needDenoiser ? data.tracedShadowTex : data.screenSpaceShadowmapTex);
                cmd.SetGlobalTexture(ShaderConstants._StencilTexture, data.stencilHandle, RenderTextureSubElement.Stencil);

                cmd.DispatchRays(data.rtrtShader, "SingleRayGen", data.dispatchRaysIndirectBuffer, 0);
            }


            if (!needDenoiser)
                return;


            // Accumulate
            using (new ProfilingScope(cmd, m_SSDS_AccumulateProfilingSampler))
            {
                var accumulateKernel = needEAWFilter ? data.accumulateKernel : data.accumulateFinalKernel;
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._CurrentShadowTexture, data.tracedShadowTex);
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._TracedShadowTexture, data.tracedShadowTex);
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._PrevTracedShadowTexture, data.prevTracedShadowTex);
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._ShadowMomentstexture, data.shadowMomentsTex);
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._PrevShadowMomentstexture, data.prevShadowMomentsTex);

                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._DirShadowmapTexture, data.dirShadowmapTex);
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._StencilTexture, data.stencilHandle, 0, RenderTextureSubElement.Stencil);
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._CameraMotionVectorsTexture, data.motionVectorTexture);
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._PrevCameraDepthTexture, data.prevCameraDepthTexture);
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._MeanVarianceTexture, data.meanVarianceTexture);
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._SSDirShadowmapTexture, data.screenSpaceShadowmapTex);
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._GBuffer0, data.materialGBuffer);
                cmd.SetComputeTextureParam(data.denoiserCS, accumulateKernel, ShaderConstants._GBuffer2, data.normalGBuffer);

                cmd.SetComputeMatrixParam(data.denoiserCS, ShaderConstants._ClipToPrevClipMatrix, data.clipToPrevClipMatrix);

                cmd.SetComputeBufferParam(data.denoiserCS, accumulateKernel, ShaderConstants.g_TileList, data.tileListBuffer);
                cmd.DispatchCompute(data.denoiserCS, accumulateKernel, data.dispatchIndirectBuffer, 0);
            }

            if (!needEAWFilter)
                return;

            // Edge-Avoiding A-Trous Wavelet (EAW) Filter
            using (new ProfilingScope(cmd, m_SSDS_EAWProfilingSampler))
            {
                cmd.SetComputeTextureParam(data.denoiserCS, data.eawKernel, ShaderConstants._MeanVarianceTexture, data.meanVarianceTexture);
                cmd.SetComputeTextureParam(data.denoiserCS, data.eawKernel, ShaderConstants._SSDirShadowmapTexture, data.screenSpaceShadowmapTex);
                cmd.SetComputeTextureParam(data.denoiserCS, data.eawKernel, ShaderConstants._TracedShadowTexture, data.tracedShadowTex);
                cmd.SetComputeTextureParam(data.denoiserCS, data.eawKernel, ShaderConstants._GBuffer2, data.normalGBuffer);

                cmd.SetComputeBufferParam(data.denoiserCS, data.eawKernel, ShaderConstants.g_TileList, data.tileListBuffer);
                cmd.DispatchCompute(data.denoiserCS, data.eawKernel, data.dispatchIndirectBuffer, 0);
            }
        }

        /// <summary>
        /// 执行 Raster Ultra 阴影的时序累积和边缘感知滤波。
        /// </summary>
        private static void ExecuteRasterShadowDenoiserPass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            using (new ProfilingScope(cmd, m_SSDS_AccumulateProfilingSampler))
            {
                int kernel = data.rasterAccumulateKernel;
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._CurrentShadowTexture, data.screenSpaceShadowmapTex);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._TracedShadowTexture, data.tracedShadowTex);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._PrevTracedShadowTexture, data.prevTracedShadowTex);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._ShadowMomentstexture, data.shadowMomentsTex);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._PrevShadowMomentstexture, data.prevShadowMomentsTex);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._ShadowMetadataTexture, data.shadowMetadataTex);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._PrevShadowMetadataTexture, data.prevShadowMetadataTex);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._GBuffer0, data.materialGBuffer);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._GBuffer2, data.normalGBuffer);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._CameraMotionVectorsTexture, data.motionVectorTexture);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._PrevCameraDepthTexture, data.prevCameraDepthTexture);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._MeanVarianceTexture, data.meanVarianceTexture);
                cmd.SetComputeTextureParam(data.denoiserCS, kernel, ShaderConstants._SSDirShadowmapTexture, data.screenSpaceShadowmapTex);
                cmd.SetComputeMatrixParam(data.denoiserCS, ShaderConstants._ClipToPrevClipMatrix, data.clipToPrevClipMatrix);
                cmd.SetComputeIntParam(data.denoiserCS, ShaderConstants._ShadowHistoryValid, data.shadowHistoryValid ? 1 : 0);
                cmd.SetComputeBufferParam(data.denoiserCS, kernel, ShaderConstants.g_TileList, data.tileListBuffer);
                cmd.DispatchCompute(data.denoiserCS, kernel, data.dispatchIndirectBuffer, 0);
            }

            using (new ProfilingScope(cmd, m_SSDS_EAWProfilingSampler))
            {
                cmd.SetComputeTextureParam(data.denoiserCS, data.eawKernel, ShaderConstants._MeanVarianceTexture, data.meanVarianceTexture);
                cmd.SetComputeTextureParam(data.denoiserCS, data.eawKernel, ShaderConstants._SSDirShadowmapTexture, data.screenSpaceShadowmapTex);
                cmd.SetComputeTextureParam(data.denoiserCS, data.eawKernel, ShaderConstants._GBuffer2, data.normalGBuffer);
                cmd.SetComputeBufferParam(data.denoiserCS, data.eawKernel, ShaderConstants.g_TileList, data.tileListBuffer);
                cmd.DispatchCompute(data.denoiserCS, data.eawKernel, data.dispatchIndirectBuffer, 0);
            }
        }

        /// <summary>
        /// 将短距离屏幕空间接触阴影合并到主方向光阴影。
        /// </summary>
        private static void ExecuteContactShadowsPass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;
            int kernel = data.contactShadowsKernel;

            using (new ProfilingScope(cmd, s_ContactShadowsProfilingSampler))
            {
                cmd.SetComputeVectorParam(data.cs, ShaderConstants._ContactShadowParams0, data.contactShadowParams0);
                cmd.SetComputeVectorParam(data.cs, ShaderConstants._ContactShadowParams1, data.contactShadowParams1);
                cmd.SetComputeIntParam(data.cs, ShaderConstants._ContactShadowSampleCount, data.contactShadowSampleCount);
                cmd.SetComputeTextureParam(data.cs, kernel, ShaderConstants._SSDirShadowmapTexture, data.screenSpaceShadowmapTex);
                cmd.SetComputeTextureParam(data.cs, kernel, ShaderConstants._GBuffer0, data.materialGBuffer);
                cmd.SetComputeTextureParam(data.cs, kernel, ShaderConstants._GBuffer2, data.normalGBuffer);
                BlueNoiseSystem.BindSTBNParams(BlueNoiseTexFormat._128RG, cmd, data.cs, kernel,
                    data.blueNoiseArray, data.enableRasterDenoiser ? data.camHistoryFrameCount : 0);

                int groupsX = RenderingUtils.DivRoundUp(data.screenSpaceShadowmapSize.x, 8);
                int groupsY = RenderingUtils.DivRoundUp(data.screenSpaceShadowmapSize.y, 8);
                cmd.DispatchCompute(data.cs, kernel, groupsX, groupsY, 1);
            }
        }

        private static void ExecuteComputeShadowsPass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            cmd.SetComputeFloatParam(data.cs, ShaderConstants._CamHistoryFrameCount, data.camHistoryFrameCount);
            cmd.SetComputeIntParam(data.cs, ShaderConstants._RasterShadowDenoiser, data.enableRasterDenoiser ? 1 : 0);
            cmd.SetComputeIntParam(data.cs, ShaderConstants._CascadeShadowDebugMode, data.cascadeDebugMode);

            // BuildIndirect
            using (new ProfilingScope(cmd, m_SSDSClassifyTilesProfilingSampler))
            {
                cmd.SetComputeBufferParam(data.cs, data.classifyTilesKernel, ShaderConstants.g_DispatchIndirectBuffer, data.dispatchIndirectBuffer);
                cmd.SetComputeBufferParam(data.cs, data.classifyTilesKernel, ShaderConstants.g_TileList, data.tileListBuffer);

                cmd.SetComputeTextureParam(data.cs, data.classifyTilesKernel, ShaderConstants._DirShadowmapTexture, data.dirShadowmapTex);
                cmd.SetComputeTextureParam(data.cs, data.classifyTilesKernel, ShaderConstants._SSDirShadowmapTexture, data.screenSpaceShadowmapTex);
                cmd.SetComputeTextureParam(data.cs, data.classifyTilesKernel, ShaderConstants._StencilTexture, data.stencilHandle, 0, RenderTextureSubElement.Stencil);
                cmd.SetComputeTextureParam(data.cs, data.classifyTilesKernel, ShaderConstants._GBuffer0, data.materialGBuffer);
                cmd.SetComputeTextureParam(data.cs, data.classifyTilesKernel, ShaderConstants._GBuffer2, data.normalGBuffer);
                BlueNoiseSystem.BindSTBNParams(BlueNoiseTexFormat._128RG, cmd, data.cs,
                    data.classifyTilesKernel, data.blueNoiseArray, data.enableRasterDenoiser ? data.camHistoryFrameCount : 0);

                cmd.DispatchCompute(data.cs, data.classifyTilesKernel, data.numTilesX, data.numTilesY, 1);
            }

            if (data.cascadeDebugMode != 0)
                return;

            // PCSS ScreenSpaceShadowmap
            using (new ProfilingScope(cmd, m_SSDS_PCSS_ProfilingSampler))
            {
                cmd.SetComputeTextureParam(data.cs, data.shadowmapKernel, ShaderConstants._DirShadowmapTexture, data.dirShadowmapTex);
                cmd.SetComputeTextureParam(data.cs, data.shadowmapKernel, ShaderConstants._PCSSTexture, data.screenSpaceShadowmapTex);
                BlueNoiseSystem.BindSTBNParams(BlueNoiseTexFormat._128RG, cmd, data.cs,
                    data.shadowmapKernel, data.blueNoiseArray, data.enableRasterDenoiser ? data.camHistoryFrameCount : 0);

                // Indirect buffer & dispatch
                cmd.SetComputeBufferParam(data.cs, data.shadowmapKernel, ShaderConstants.g_TileList, data.tileListBuffer);
                cmd.DispatchCompute(data.cs, data.shadowmapKernel, data.dispatchIndirectBuffer, argsOffset: 0);
            }

            if (data.enableContactShadows)
                ExecuteContactShadowsPass(data, context);

            if (data.enableRasterDenoiser)
                ExecuteRasterShadowDenoiserPass(data, context);
        }

        private static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;

            if (data.requireRayTracing)
            {
                // Ray Tracing Shadows
                ExecuteRayTracingShadowsPass(data, context);
            }
            else
            {
                // Compute Shadows
                ExecuteComputeShadowsPass(data, context);
            }

        }

        internal TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData)
        {
            int historyFramCount = 0;
            var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(frameData.Get<UniversalCameraData>().camera);
            if (historyRTSystem != null)
                historyFramCount = historyRTSystem.historyFrameCount;

            using (var builder = renderGraph.AddComputePass<PassData>("Render SS Shadow", out var passData, ProfilingSampler.Get(URPProfileId.RenderSSShadow)))
            {
                // Access resources
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();

                // Setup shared pass data before selecting the shadow path.
                InitPassData(renderGraph, passData, cameraData, resourceData, historyFramCount);

                // Ray Tracing
                passData.requireRayTracing = cameraData.supportedRayTracing && cameraData.rayTracingSystem.GetRayTracingState();
                InitRayTracingPassData(renderGraph, passData, cameraData, resourceData);
                shadowData.rayTracingShadowsEnabled = passData.requireRayTracing;

                passData.enableRasterDenoiser = !passData.requireRayTracing &&
                    IsRasterShadowDenoiserRequired(lightData, shadowData);
                if (passData.enableRasterDenoiser)
                    InitRasterDenoiserPassData(renderGraph, passData, cameraData, resourceData);

                if (!passData.requireRayTracing)
                    InitContactShadowsPassData(passData, lightData, shadowData);

                // Setup builder state
                builder.UseBuffer(passData.dispatchIndirectBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.tileListBuffer, AccessFlags.ReadWrite);
                builder.UseTexture(passData.dirShadowmapTex, AccessFlags.Read);
                builder.UseTexture(passData.screenSpaceShadowmapTex, AccessFlags.ReadWrite);
                builder.UseTexture(passData.normalGBuffer, AccessFlags.Read);
                builder.UseTexture(passData.materialGBuffer, AccessFlags.Read);
                builder.UseTexture(passData.stencilHandle, AccessFlags.Read);
                builder.UseTexture(passData.blueNoiseArray, AccessFlags.Read);

                builder.UseBuffer(passData.raysCoordBuffer, AccessFlags.ReadWrite);
                if (passData.requireRayTracing)
                {
                    builder.UseTexture(passData.tracedShadowTex, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.prevTracedShadowTex, AccessFlags.Read);
                    builder.UseTexture(passData.shadowMomentsTex, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.prevShadowMomentsTex, AccessFlags.Read);
                    builder.UseTexture(passData.motionVectorTexture, AccessFlags.Read);
                    builder.UseTexture(passData.prevCameraDepthTexture, AccessFlags.Read);
                    builder.UseTexture(passData.meanVarianceTexture, AccessFlags.ReadWrite);
                }
                else if (passData.enableRasterDenoiser)
                {
                    builder.UseTexture(passData.tracedShadowTex, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.prevTracedShadowTex, AccessFlags.Read);
                    builder.UseTexture(passData.shadowMomentsTex, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.prevShadowMomentsTex, AccessFlags.Read);
                    builder.UseTexture(passData.shadowMetadataTex, AccessFlags.Write);
                    builder.UseTexture(passData.prevShadowMetadataTex, AccessFlags.Read);
                    builder.UseTexture(passData.motionVectorTexture, AccessFlags.Read);
                    builder.UseTexture(passData.prevCameraDepthTexture, AccessFlags.Read);
                    builder.UseTexture(passData.meanVarianceTexture, AccessFlags.ReadWrite);
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(passData.requireRayTracing);
                //builder.EnableAsyncCompute(true);

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    ExecutePass(data, context);
                });

                return passData.screenSpaceShadowmapTex;
            }
        }

        static class ShaderConstants
        {
            public static readonly int g_DispatchIndirectBuffer = Shader.PropertyToID("g_DispatchIndirectBuffer");
            public static readonly int g_TileList = Shader.PropertyToID("g_TileList");

            public static readonly int _ShadowRayIndirectBuffer = Shader.PropertyToID("_ShadowRayIndirectBuffer");
            public static readonly int _ShadowRayCoordBuffer = Shader.PropertyToID("_ShadowRayCoordBuffer");

            public static readonly int _DirShadowmapTexture = Shader.PropertyToID("_DirShadowmapTexture");
            public static readonly int _SSDirShadowmapTexture = Shader.PropertyToID("_SSDirShadowmapTexture");
            public static readonly int _ScreenSpaceShadowmapTexture = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");
            public static readonly int _PCSSTexture = Shader.PropertyToID("_PCSSTexture");
            public static readonly int _BilateralTexture = Shader.PropertyToID("_BilateralTexture");
            public static readonly int _CamHistoryFrameCount = Shader.PropertyToID("_CamHistoryFrameCount");
            public static readonly int _RasterShadowDenoiser = Shader.PropertyToID("_RasterShadowDenoiser");
            public static readonly int _CascadeShadowDebugMode = Shader.PropertyToID("_CascadeShadowDebugMode");

            public static readonly int _RayTracingShadowsTextureRW = Shader.PropertyToID("_RayTracingShadowsTextureRW");
            public static readonly int _StencilTexture = Shader.PropertyToID("_StencilTexture");
            public static readonly int _RayTracingDirShadowPenumbra = Shader.PropertyToID("_RayTracingDirShadowPenumbra");
            public static readonly int _RayTracingDirShadowPenumbraCOS = Shader.PropertyToID("_RayTracingDirShadowPenumbraCOS");
            public static readonly int _RayTracingDirShadowCharacterNormalOffset = Shader.PropertyToID("_RayTracingDirShadowCharacterNormalOffset");
            public static readonly int _RayTracingDirShadowCharacterHalfDirScale = Shader.PropertyToID("_RayTracingDirShadowCharacterHalfDirScale");

            public static readonly int _TracedShadowTexture = Shader.PropertyToID("_TracedShadowTexture");
            public static readonly int _CurrentShadowTexture = Shader.PropertyToID("_CurrentShadowTexture");
            public static readonly int _PrevTracedShadowTexture = Shader.PropertyToID("_PrevTracedShadowTexture");
            public static readonly int _CameraMotionVectorsTexture = Shader.PropertyToID("_CameraMotionVectorsTexture");
            public static readonly int _PrevCameraDepthTexture = Shader.PropertyToID("_PrevCameraDepthTexture");
            public static readonly int _ShadowMomentstexture = Shader.PropertyToID("_ShadowMomentstexture");
            public static readonly int _PrevShadowMomentstexture = Shader.PropertyToID("_PrevShadowMomentstexture");
            public static readonly int _MeanVarianceTexture = Shader.PropertyToID("_MeanVarianceTexture");
            public static readonly int _ShadowMetadataTexture = Shader.PropertyToID("_ShadowMetadataTexture");
            public static readonly int _PrevShadowMetadataTexture = Shader.PropertyToID("_PrevShadowMetadataTexture");
            public static readonly int _ShadowHistoryValid = Shader.PropertyToID("_ShadowHistoryValid");
            public static readonly int _GBuffer0 = Shader.PropertyToID("_GBuffer0");
            public static readonly int _GBuffer2 = Shader.PropertyToID("_GBuffer2");
            public static readonly int _ContactShadowParams0 = Shader.PropertyToID("_ContactShadowParams0");
            public static readonly int _ContactShadowParams1 = Shader.PropertyToID("_ContactShadowParams1");
            public static readonly int _ContactShadowSampleCount = Shader.PropertyToID("_ContactShadowSampleCount");

            public static readonly int _ClipToPrevClipMatrix = Shader.PropertyToID("_ClipToPrevClipMatrix");

        }
    }
}
