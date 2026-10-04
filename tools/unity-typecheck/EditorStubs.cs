#pragma warning disable CS0067, CS0649, CS1591
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEditor
{
    [Flags] public enum ImportAssetOptions { Default = 0, ForceUpdate = 1 }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute { public MenuItem(string itemName) { } public MenuItem(string itemName, bool isValidateFunction) { } public MenuItem(string itemName, bool isValidateFunction, int priority) { } public int priority; }
    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string p) where T : UnityEngine.Object => null;
        public static UnityEngine.Object[] LoadAllAssetsAtPath(string p) => null;
        public static void SaveAssets() { } public static void Refresh() { } public static void CreateAsset(UnityEngine.Object o, string p) { }
        public static bool IsValidFolder(string p) => true; public static string CreateFolder(string a, string b) => ""; public static void ImportAsset(string p) { } public static void ImportAsset(string p, ImportAssetOptions o) { } public static string[] FindAssets(string filter, string[] folders) => null; public static string GUIDToAssetPath(string guid) => null;
        public static bool Contains(UnityEngine.Object o) => true; public static void AddObjectToAsset(UnityEngine.Object o, UnityEngine.Object a) { }
    }
    public static class EditorUtility
    {
        public static void SetDirty(UnityEngine.Object o) { } public static bool DisplayDialog(string t, string m, string ok, string cancel = "") => true;
        public static void DisplayProgressBar(string t, string i, float p) { } public static bool DisplayCancelableProgressBar(string t, string i, float p) => false; public static void ClearProgressBar() { }
    }
    public class EditorBuildSettingsScene { public EditorBuildSettingsScene(string path, bool enabled) { } public string path; public bool enabled; }
    public static class EditorBuildSettings { public static EditorBuildSettingsScene[] scenes; }
    public enum BuildTarget { StandaloneWindows64 = 19 }
    public enum BuildTargetGroup { Standalone = 1 }
    [Flags] public enum BuildOptions { None = 0, Development = 1 }
    public struct BuildPlayerOptions { public string[] scenes; public string locationPathName; public BuildTarget target; public BuildTargetGroup targetGroup; public BuildOptions options; }
    public static class BuildPipeline { public static UnityEditor.Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions o) => null; }
    public static class EditorApplication { public static void Exit(int code) { } }
    [Flags] public enum StaticEditorFlags { ContributeGI = 1, OccluderStatic = 2, BatchingStatic = 4, NavigationStatic = 8, OccludeeStatic = 16, OffMeshLinkGeneration = 32, ReflectionProbeStatic = 64 }
    public static class GameObjectUtility { public static void SetStaticEditorFlags(GameObject g, StaticEditorFlags f) { } public static bool AreStaticEditorFlagsSet(GameObject g, StaticEditorFlags f) => false; }
    public class AssetImporter : UnityEngine.Object { public static AssetImporter GetAtPath(string p) => null; public void SaveAndReimport() { } }
    public class AssetPostprocessor { public string assetPath; public AssetImporter assetImporter; }
    public enum ModelImporterMaterialImportMode { None, ImportStandard, ImportViaMaterialDescription }
    public class ModelImporter : AssetImporter { public float globalScale; public bool useFileScale, importAnimation, importCameras, importLights, addCollider; public ModelImporterMaterialImportMode materialImportMode; }
    public static class PrefabUtility { public static UnityEngine.Object InstantiatePrefab(UnityEngine.Object o, Transform parent) => null; }
    public enum TextureImporterType { Default, NormalMap }
    public enum TextureImporterShape { Texture2D = 1, TextureCube = 2 }
    public class TextureImporter : AssetImporter { public TextureImporterType textureType; public TextureImporterShape textureShape; public bool sRGBTexture, alphaIsTransparency, mipmapEnabled; public int maxTextureSize, anisoLevel; }
    public static class Lightmapping { public static bool BakeAsync() => true; }
    public static class StaticOcclusionCulling { public static bool Compute() => true; }
    public enum SerializedPropertyType { Generic, Integer, Boolean, Float, String, Color, ObjectReference, LayerMask, Enum, Vector2, Vector3 }
    public class SerializedProperty
    {
        public SerializedPropertyType propertyType; public bool boolValue; public int intValue; public float floatValue; public string stringValue; public UnityEngine.Object objectReferenceValue; public int enumValueIndex; public Color colorValue; public string name;
        public SerializedProperty FindPropertyRelative(string n) => null; public bool NextVisible(bool c) => false; public bool Next(bool c) => false; public SerializedProperty Copy() => this; public int arraySize; public SerializedProperty GetArrayElementAtIndex(int i) => null;
    }
    public class SerializedObject { public SerializedObject(UnityEngine.Object o) { } public SerializedProperty FindProperty(string p) => null; public bool ApplyModifiedProperties() => true; public bool ApplyModifiedPropertiesWithoutUndo() => true; public void Update() { } public SerializedProperty GetIterator() => null; }
    public static class PlayerSettings { public static ColorSpace colorSpace; }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }
    public static class EditorSceneManager { public static Scene NewScene(NewSceneSetup s, NewSceneMode m) => default; public static bool SaveScene(Scene s, string path = "", bool copy = false) => true; }
}

namespace UnityEngine.Rendering
{
    public static class VolumeProfileExt { public static VolumeComponent Add(this VolumeProfile p, Type t, bool overrides = false) => null; }
}

namespace UnityEngine.Rendering.HighDefinition
{
    public class HDRenderPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset { public override UnityEngine.Rendering.RenderPipeline CreatePipeline() => null; }
    public class HDAdditionalReflectionData : MonoBehaviour { }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown, Succeeded, Failed, Cancelled }
    public class BuildSummary { public BuildResult result; public ulong totalSize; public System.TimeSpan totalTime; public int totalErrors; }
    public class BuildReport { public BuildSummary summary; }
}
