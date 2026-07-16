using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(Shadows))]
    sealed class ScreenSpaceShadowsEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_RayTracing;
        SerializedDataParameter m_DirShadowsRayLength;
        SerializedDataParameter m_DirShadowPenumbra;
        SerializedDataParameter m_CharacterLayerMask;
        SerializedDataParameter m_CharacterNormalOffset;
        SerializedDataParameter m_CharacterHalfDirScale;

        SerializedDataParameter m_Penumbra;
        SerializedDataParameter m_Intensity;

        SerializedDataParameter m_PerObjectShadowPenumbra;

        SerializedDataParameter m_ContactShadows;
        SerializedDataParameter m_ContactShadowLength;
        SerializedDataParameter m_ContactShadowDistance;
        SerializedDataParameter m_ContactShadowFadeDistance;
        SerializedDataParameter m_ContactShadowThickness;
        SerializedDataParameter m_ContactShadowNormalBias;
        SerializedDataParameter m_ContactShadowSampleCount;
        SerializedDataParameter m_ContactShadowIntensity;
        SerializedDataParameter m_ContactShadowJitter;
        SerializedDataParameter m_ContactShadowRayFadeStart;

        SerializedDataParameter m_ShadowScatterMode;
        SerializedDataParameter m_OcclusionPenumbra;
        SerializedDataParameter m_ShadowRampTex;
        SerializedDataParameter m_ScatterR;
        SerializedDataParameter m_ScatterG;
        SerializedDataParameter m_ScatterB;

        SerializedDataParameter m_Denoiser;
        SerializedDataParameter m_EdgeAvoidingWaveletBlur;

        static GUIContent s_PerObjectShadowPenumbra = EditorGUIUtility.TrTextContent("Penumbra (PerObjectShadow)", "Controls the width of PerObjectShadow.");

        public override void OnEnable()
        {
            var o = new PropertyFetcher<Shadows>(serializedObject);
            m_RayTracing = Unpack(o.Find(x => x.rayTracing));
            m_DirShadowsRayLength = Unpack(o.Find(x => x.dirShadowsRayLength));
            m_DirShadowPenumbra = Unpack(o.Find(x => x.dirShadowPenumbra));
            m_CharacterLayerMask = Unpack(o.Find(x => x.characterLayerMask));
            m_CharacterNormalOffset = Unpack(o.Find(x => x.characterNormalOffset));
            m_CharacterHalfDirScale = Unpack(o.Find(x => x.characterHalfDirScale));

            m_Intensity = Unpack(o.Find(x => x.intensity));

            m_Penumbra = Unpack(o.Find(x => x.penumbra));
            m_PerObjectShadowPenumbra = Unpack(o.Find(x => x.perObjectShadowPenumbra));

            m_ContactShadows = Unpack(o.Find(x => x.contactShadows));
            m_ContactShadowLength = Unpack(o.Find(x => x.contactShadowLength));
            m_ContactShadowDistance = Unpack(o.Find(x => x.contactShadowDistance));
            m_ContactShadowFadeDistance = Unpack(o.Find(x => x.contactShadowFadeDistance));
            m_ContactShadowThickness = Unpack(o.Find(x => x.contactShadowThickness));
            m_ContactShadowNormalBias = Unpack(o.Find(x => x.contactShadowNormalBias));
            m_ContactShadowSampleCount = Unpack(o.Find(x => x.contactShadowSampleCount));
            m_ContactShadowIntensity = Unpack(o.Find(x => x.contactShadowIntensity));
            m_ContactShadowJitter = Unpack(o.Find(x => x.contactShadowJitter));
            m_ContactShadowRayFadeStart = Unpack(o.Find(x => x.contactShadowRayFadeStart));

            m_ShadowScatterMode = Unpack(o.Find(x => x.shadowScatterMode));
            m_OcclusionPenumbra = Unpack(o.Find(x => x.occlusionPenumbra));
            m_ShadowRampTex = Unpack(o.Find(x => x.shadowRampTex));
            m_ScatterR = Unpack(o.Find(x => x.scatterR));
            m_ScatterG = Unpack(o.Find(x => x.scatterG));
            m_ScatterB = Unpack(o.Find(x => x.scatterB));

            m_Denoiser = Unpack(o.Find(x => x.denoiser));
            m_EdgeAvoidingWaveletBlur = Unpack(o.Find(x => x.edgeAvoidingWaveletBlur));
        }

        void RayTracedShadowsGUI()
        {
            var pipelineAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;

            if (!pipelineAsset.supportsRayTracing)
            {
                EditorGUILayout.HelpBox("Check RayTracing in pipeline asset (" + pipelineAsset.name +") rendering settings.", MessageType.Error, true);
            }
            else
            {
                PropertyField(m_DirShadowsRayLength);
                PropertyField(m_DirShadowPenumbra);
                PropertyField(m_CharacterLayerMask);
                PropertyField(m_CharacterNormalOffset);
                PropertyField(m_CharacterHalfDirScale);

                EditorGUILayout.Space(10);

                PropertyField(m_Intensity);

                EditorGUILayout.Space(10);
                PropertyField(m_Denoiser);
                PropertyField(m_EdgeAvoidingWaveletBlur);

                EditorGUILayout.Space(20);
                PropertyField(m_PerObjectShadowPenumbra, s_PerObjectShadowPenumbra);
                EditorGUILayout.Space(10);
                if (m_ShadowScatterMode.value.intValue == (int)ShadowScatterMode.RampTexture)
                {
                    PropertyField(m_ShadowScatterMode);
                    PropertyField(m_OcclusionPenumbra);
                    PropertyField(m_ShadowRampTex);
                }
                else if (m_ShadowScatterMode.value.intValue == (int)ShadowScatterMode.SubSurface)
                {
                    PropertyField(m_ShadowScatterMode);
                    PropertyField(m_OcclusionPenumbra);
                    PropertyField(m_ScatterR);
                    PropertyField(m_ScatterG);
                    PropertyField(m_ScatterB);
                }
                else
                {
                    PropertyField(m_ShadowScatterMode);
                }
            }

        }

        void RasterShadowsGUI()
        {
            EditorGUILayout.Space(10);

            PropertyField(m_Intensity);

            PropertyField(m_Penumbra);
            PropertyField(m_PerObjectShadowPenumbra, s_PerObjectShadowPenumbra);

            EditorGUILayout.Space(10);
            PropertyField(m_ContactShadows);
            if (m_ContactShadows.value.boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    PropertyField(m_ContactShadowLength);
                    PropertyField(m_ContactShadowDistance);
                    PropertyField(m_ContactShadowFadeDistance);
                    PropertyField(m_ContactShadowThickness);
                    PropertyField(m_ContactShadowNormalBias);
                    PropertyField(m_ContactShadowSampleCount);
                    PropertyField(m_ContactShadowIntensity);
                    PropertyField(m_ContactShadowJitter);
                    PropertyField(m_ContactShadowRayFadeStart);
                }
            }

            EditorGUILayout.Space(10);

            if (m_ShadowScatterMode.value.intValue == (int)ShadowScatterMode.RampTexture)
            {
                PropertyField(m_ShadowScatterMode);
                PropertyField(m_OcclusionPenumbra);
                PropertyField(m_ShadowRampTex);
            }
            else if (m_ShadowScatterMode.value.intValue == (int)ShadowScatterMode.SubSurface)
            {
                PropertyField(m_ShadowScatterMode);
                PropertyField(m_OcclusionPenumbra);
                PropertyField(m_ScatterR);
                PropertyField(m_ScatterG);
                PropertyField(m_ScatterB);
            }
            else
            {
                PropertyField(m_ShadowScatterMode);
            }

        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            PropertyField(m_RayTracing);
            if (EditorGUI.EndChangeCheck() && m_RayTracing.value.boolValue)
            {
                m_ShadowScatterMode.value.intValue = (int)ShadowScatterMode.None;
                serializedObject.ApplyModifiedProperties();
            }
            // Flag to track if the ray tracing parameters were displayed
            bool rayTracingSettingsDisplayed = m_RayTracing.overrideState.boolValue
                && m_RayTracing.value.boolValue;

            // The rest of the ray tracing UI is only displayed if the asset supports ray tracing and the checkbox is checked.
            if (rayTracingSettingsDisplayed)
            {
                RayTracedShadowsGUI();
            }
            else
            {
                RasterShadowsGUI();
            }
            
        }
    }
}
