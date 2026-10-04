using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Garage.Unity.EditorTools
{
    /// <summary>Shared helpers for the idempotent editor builders.</summary>
    public static class EditorUtil
    {
        public const string Root = "Assets/Garage";
        public const string MaterialsDir = Root + "/Materials";
        public const string SettingsDir = Root + "/Settings";
        public const string ScenesDir = Root + "/Scenes";
        public const string GeneratedDir = Root + "/Generated";

        /// <summary>Creates a folder path under Assets if missing.</summary>
        public static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        /// <summary>Finds an HDRP/Core runtime type by simple name.</summary>
        public static Type FindType(string fullName)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(fullName, false);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>Adds (or gets) a volume override by HDRP type name and returns it (null if the type does not exist).</summary>
        public static VolumeComponent AddOverride(VolumeProfile profile, string typeName)
        {
            Type t = FindType("UnityEngine.Rendering.HighDefinition." + typeName) ?? FindType("UnityEngine.Rendering." + typeName);
            if (t == null)
            {
                Debug.LogWarning($"[Garage] Override de volumen '{typeName}' no existe en esta versión de HDRP; se omite.");
                return null;
            }

            VolumeComponent existing = profile.components.FirstOrDefault(c => c != null && c.GetType() == t);
            if (existing != null)
            {
                return existing;
            }

            VolumeComponent comp = profile.Add(t, true);
            comp.name = typeName;
            if (AssetDatabase.Contains(profile))
            {
                AssetDatabase.AddObjectToAsset(comp, profile);
            }

            return comp;
        }

        /// <summary>Sets a VolumeParameter field (by name) with override state; enums are parsed from strings.</summary>
        public static bool SetParam(VolumeComponent comp, string field, object value)
        {
            if (comp == null)
            {
                return false;
            }

            FieldInfo f = comp.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
            if (f == null)
            {
                Debug.LogWarning($"[Garage] {comp.GetType().Name}.{field} no existe; se omite.");
                return false;
            }

            object param = f.GetValue(comp);
            Type valueType = param.GetType();
            while (valueType != null && !(valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(VolumeParameter<>)))
            {
                valueType = valueType.BaseType;
            }

            if (valueType == null)
            {
                return false;
            }

            Type tArg = valueType.GetGenericArguments()[0];
            object converted = value is string s && tArg.IsEnum ? Enum.Parse(tArg, s) : Convert.ChangeType(value, tArg.IsEnum ? Enum.GetUnderlyingType(tArg) : tArg);
            if (tArg.IsEnum && !(converted.GetType().IsEnum))
            {
                converted = Enum.ToObject(tArg, converted);
            }

            MethodInfo ov = param.GetType().GetMethod("Override", new[] { tArg });
            if (ov == null)
            {
                return false;
            }

            ov.Invoke(param, new[] { converted });
            return true;
        }

        /// <summary>Saves (creates or replaces) a material asset.</summary>
        public static Material SaveMaterial(Material m, string name)
        {
            EnsureFolder(MaterialsDir);
            string path = $"{MaterialsDir}/{name}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (m.enableInstancing && !existing.enableInstancing)
                {
                    existing.enableInstancing = true; // HDRP decals require GPU instancing
                    EditorUtility.SetDirty(existing);
                }

                return existing; // keep user/downloader edits: idempotent
            }

            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Project root (folder containing Assets).</summary>
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>Repository root (parent of the Unity project).</summary>
        public static string RepoRoot => Path.GetDirectoryName(ProjectRoot);

        /// <summary>Calls an optional static method by reflection (used for HDRP editor utilities).</summary>
        public static void TryInvokeStatic(string typeName, string method, params object[] args)
        {
            Type t = FindType(typeName);
            MethodInfo m = t?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
            try
            {
                m?.Invoke(null, args);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogWarning($"[Garage] {typeName}.{method}: {e.InnerException?.Message}");
            }
        }

        /// <summary>Validates HDRP material keywords after assigning textures.</summary>
        public static void ValidateHdrpMaterial(Material m)
        {
            TryInvokeStatic("UnityEditor.Rendering.HighDefinition.HDShaderUtils", "ResetMaterialKeywords", m);
            TryInvokeStatic("UnityEngine.Rendering.HighDefinition.HDMaterial", "ValidateMaterial", m);
        }

        /// <summary>Sets the name of a user layer in the TagManager.</summary>
        public static void SetLayerName(int index, string name)
        {
            UnityEngine.Object[] tm = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tm.Length == 0)
            {
                return;
            }

            var so = new SerializedObject(tm[0]);
            SerializedProperty layers = so.FindProperty("layers");
            if (layers != null && index < layers.arraySize)
            {
                layers.GetArrayElementAtIndex(index).stringValue = name;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
