using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Garage.Unity.EditorTools
{
    /// <summary>
    /// Garage/Setup/Configure HDRP: creates the HDRP assets for four PC quality presets, assigns them to the
    /// graphics and quality settings, and creates the global Volume profile (physical exposure, ACES, subtle bloom,
    /// SSAO, SSR, contact shadows, DoF for close inspection, faint film grain and vignette, physically based sky).
    /// Idempotent: re-running updates the same assets.
    /// </summary>
    public static class HdrpSetup
    {
        public const string VolumeProfilePath = EditorUtil.SettingsDir + "/WorkshopVolumeProfile.asset";
        private static readonly string[] Presets = { "Bajo", "Medio", "Alto", "Ultra" };

        [MenuItem("Garage/Setup/Configure HDRP", priority = 1)]
        public static void Configure()
        {
            EditorUtil.EnsureFolder(EditorUtil.SettingsDir);
            HDRenderPipelineAsset[] assets = new HDRenderPipelineAsset[Presets.Length];
            for (int i = 0; i < Presets.Length; i++)
            {
                string path = $"{EditorUtil.SettingsDir}/HDRP_{Presets[i]}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                    AssetDatabase.CreateAsset(asset, path);
                }

                ApplyPreset(asset, i);
                assets[i] = asset;
            }

            GraphicsSettings.defaultRenderPipeline = assets[2];
            ConfigureQualityLevels(assets);
            CreateVolumeProfile();
            EditorUtil.SetLayerName(UnityCompat.DeviceUiLayer, "DeviceUI");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            Debug.Log("[Garage] HDRP configurado (presets Bajo/Medio/Alto/Ultra). Si HDRP pide el Global Settings, abre Window > Rendering > HDRP Wizard y pulsa 'Fix All'.");
        }

        /// <summary>Preset values through serialized properties (robust to API changes; missing paths are skipped).</summary>
        private static void ApplyPreset(HDRenderPipelineAsset asset, int level)
        {
            var so = new SerializedObject(asset);
            void Set(string path, object v)
            {
                SerializedProperty p = so.FindProperty(path);
                if (p == null)
                {
                    return;
                }

                switch (v)
                {
                    case bool b: p.boolValue = b; break;
                    case int n when p.propertyType == SerializedPropertyType.Enum: p.enumValueIndex = n; break;
                    case int n: p.intValue = n; break;
                    case float f: p.floatValue = f; break;
                }
            }

            int[] atlas = { 2048, 4096, 4096, 8192 };
            int[] maxShadows = { 64, 96, 128, 192 };
            Set("m_RenderPipelineSettings.supportSSAO", true);
            Set("m_RenderPipelineSettings.supportSSR", level >= 1);
            Set("m_RenderPipelineSettings.supportContactShadows", true);
            Set("m_RenderPipelineSettings.supportDecals", true);
            Set("m_RenderPipelineSettings.supportVolumetrics", level >= 2);
            Set("m_RenderPipelineSettings.supportMotionVectors", true);
            Set("m_RenderPipelineSettings.supportShadowMask", true);
            Set("m_RenderPipelineSettings.hdShadowInitParams.punctualLightShadowAtlas.shadowAtlasResolution", atlas[level]);
            Set("m_RenderPipelineSettings.hdShadowInitParams.areaLightShadowAtlas.shadowAtlasResolution", atlas[level] / 2);
            Set("m_RenderPipelineSettings.hdShadowInitParams.maxShadowRequests", maxShadows[level]);
            Set("m_RenderPipelineSettings.lightLoopSettings.reflectionProbeCacheSize", 32 + level * 32);
            Set("m_RenderPipelineSettings.decalSettings.perChannelMask", level >= 2);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureQualityLevels(HDRenderPipelineAsset[] assets)
        {
            Object[] qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qs.Length == 0)
            {
                return;
            }

            var so = new SerializedObject(qs[0]);
            SerializedProperty levels = so.FindProperty("m_QualitySettings");
            if (levels == null)
            {
                return;
            }

            levels.arraySize = Presets.Length;
            for (int i = 0; i < Presets.Length; i++)
            {
                SerializedProperty e = levels.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = Presets[i];
                SerializedProperty rp = e.FindPropertyRelative("customRenderPipeline");
                if (rp != null)
                {
                    rp.objectReferenceValue = assets[i];
                }

                SerializedProperty vsync = e.FindPropertyRelative("vSyncCount");
                if (vsync != null)
                {
                    vsync.intValue = 1;
                }

                SerializedProperty lod = e.FindPropertyRelative("lodBias");
                if (lod != null)
                {
                    lod.floatValue = new[] { 0.7f, 1f, 1.5f, 2f }[i];
                }

                SerializedProperty aniso = e.FindPropertyRelative("anisotropicTextures");
                if (aniso != null)
                {
                    aniso.intValue = i >= 1 ? 2 : 1;
                }
            }

            SerializedProperty current = so.FindProperty("m_CurrentQuality");
            if (current != null)
            {
                current.intValue = 2;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Creates or updates the global volume profile.</summary>
        public static VolumeProfile CreateVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            VolumeComponent exposure = EditorUtil.AddOverride(profile, "Exposure");
            EditorUtil.SetParam(exposure, "mode", "AutomaticHistogram");
            EditorUtil.SetParam(exposure, "limitMin", 4f);
            EditorUtil.SetParam(exposure, "limitMax", 13f);
            EditorUtil.SetParam(exposure, "compensation", 0.3f);

            VolumeComponent tone = EditorUtil.AddOverride(profile, "Tonemapping");
            EditorUtil.SetParam(tone, "mode", "ACES");

            VolumeComponent bloom = EditorUtil.AddOverride(profile, "Bloom");
            EditorUtil.SetParam(bloom, "intensity", 0.08f);
            EditorUtil.SetParam(bloom, "scatter", 0.6f);

            VolumeComponent ao = EditorUtil.AddOverride(profile, "ScreenSpaceAmbientOcclusion");
            EditorUtil.SetParam(ao, "intensity", 0.7f);
            EditorUtil.SetParam(ao, "radius", 1.2f);

            VolumeComponent ssr = EditorUtil.AddOverride(profile, "ScreenSpaceReflection");
            EditorUtil.SetParam(ssr, "enabled", true);

            VolumeComponent contact = EditorUtil.AddOverride(profile, "ContactShadows");
            EditorUtil.SetParam(contact, "enable", true);
            EditorUtil.SetParam(contact, "length", 0.15f);

            VolumeComponent dof = EditorUtil.AddOverride(profile, "DepthOfField");
            EditorUtil.SetParam(dof, "focusMode", "Off");

            VolumeComponent grain = EditorUtil.AddOverride(profile, "FilmGrain");
            EditorUtil.SetParam(grain, "intensity", 0.08f);

            VolumeComponent vignette = EditorUtil.AddOverride(profile, "Vignette");
            EditorUtil.SetParam(vignette, "intensity", 0.15f);

            VolumeComponent env = EditorUtil.AddOverride(profile, "VisualEnvironment");
            EditorUtil.SetParam(env, "skyType", 4); // SkyType.PhysicallyBased
            EditorUtil.AddOverride(profile, "PhysicallyBasedSky");

            VolumeComponent fog = EditorUtil.AddOverride(profile, "Fog");
            EditorUtil.SetParam(fog, "enabled", true);
            EditorUtil.SetParam(fog, "meanFreePath", 120f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
