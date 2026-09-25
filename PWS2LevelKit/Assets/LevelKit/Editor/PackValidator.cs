// PWS2LevelKit editor tooling: checks every stub type's serialized field names against the game's IL2CPP layout.
// A stub member Unity serialises under a name the real type does not have arrives empty at runtime, silently
// (PrefabToLoad cost a session that way). The reference list is docs/tools/stub-fields.json, exported from the
// proxy assemblies by docs/tools/export_stub_fields.ps1; re-export it after every game update.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace PWS2LevelKit.Editor
{
    public static class PackValidator
    {
        [Serializable] class FieldList { public string gameVersion; public ProxyType[] types; }
        [Serializable] class ProxyType { public string name; public string assembly; public string[] fields; }

        public static string FieldListPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "tools", "stub-fields.json"));

        [MenuItem("PWS2/Validate stubs")]
        public static void ValidateMenu() => ValidateStubs();

        public static void ValidateFromCommandLine() => EditorApplication.Exit(ValidateStubs() ? 0 : 1);

        /// <summary>Logs every mismatch; returns false if any stub field would not reach the real type.</summary>
        public static bool ValidateStubs()
        {
            if (!File.Exists(FieldListPath)) { Debug.LogError($"PackValidator: {FieldListPath} missing; run docs/tools/export_stub_fields.ps1"); return false; }
            var list = JsonUtility.FromJson<FieldList>(File.ReadAllText(FieldListPath));
            var proxies = list.types.ToDictionary(t => t.name, t => new HashSet<string>(t.fields ?? new string[0]));

            var stubAssemblies = new HashSet<string>(AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", new[] { "Assets/Stubs" })
                .Select(g => AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Select(a => JsonUtility.FromJson<AsmdefName>(a.text).name));

            var errors = new List<string>();
            var warnings = new List<string>();
            int checkedTypes = 0, checkedFields = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => stubAssemblies.Contains(a.GetName().Name)))
            {
                foreach (var type in asm.GetTypes())
                {
                    if (type.IsEnum || type.IsInterface || type.IsDefined(typeof(CompilerGeneratedAttribute), false)) continue;
                    if (!typeof(UnityEngine.Object).IsAssignableFrom(type) && !type.IsSerializable) continue;
                    var fields = SerializedFields(type).ToList();
                    var key = type.FullName.Replace('+', '/');
                    if (!proxies.TryGetValue(key, out var real))
                    {
                        if (fields.Count > 0) errors.Add($"{key}: no IL2CPP type of that name ({fields.Count} serialized fields would be lost)");
                        else warnings.Add($"{key}: no IL2CPP type of that name");
                        continue;
                    }
                    checkedTypes++;
                    foreach (var f in fields)
                    {
                        checkedFields++;
                        var mangled = Il2CppName(f.Name);
                        if (real.Contains(mangled)) continue;
                        var plain = BackingPropertyName(f.Name);
                        string hint =
                            plain != null && real.Contains(plain) ? $" (the game has a plain field '{plain}': declare it [SerializeField] public, not [field: SerializeField])" :
                            plain == null && real.Contains($"_{f.Name}_k__BackingField") ? $" (the game has an auto-property: declare it [field: SerializeField] public ... {{ get; set; }})" :
                            "";
                        errors.Add($"{key}.{f.Name}: not a field of the game type{hint}");
                    }
                }
            }

            foreach (var w in warnings) Debug.LogWarning("PackValidator: " + w);
            foreach (var e in errors) Debug.LogError("PackValidator: " + e);
            Debug.Log($"PackValidator: {checkedTypes} types, {checkedFields} serialized fields checked against game {list.gameVersion}: " +
                      (errors.Count == 0 ? "OK" : $"{errors.Count} error(s)"));
            return errors.Count == 0;
        }

        [Serializable] class AsmdefName { public string name; }

        /// <summary>Declared instance fields Unity serialises: public or [SerializeField]/[SerializeReference], not [NonSerialized].</summary>
        static IEnumerable<FieldInfo> SerializedFields(Type t) =>
            t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
             .Where(f => !f.IsNotSerialized && !f.IsInitOnly &&
                         (f.IsPublic || f.IsDefined(typeof(SerializeField), false) || f.IsDefined(typeof(SerializeReference), false)));

        /// <summary>"&lt;X&gt;k__BackingField" -> "X"; null for a plain field.</summary>
        static string BackingPropertyName(string field) =>
            field.StartsWith("<") && field.EndsWith(">k__BackingField") ? field.Substring(1, field.Length - "<>k__BackingField".Length) : null;

        /// <summary>Il2CppInterop's name for a field: "&lt;X&gt;k__BackingField" becomes "_X_k__BackingField".</summary>
        static string Il2CppName(string field)
        {
            var p = BackingPropertyName(field);
            return p != null ? $"_{p}_k__BackingField" : field;
        }
    }
}
