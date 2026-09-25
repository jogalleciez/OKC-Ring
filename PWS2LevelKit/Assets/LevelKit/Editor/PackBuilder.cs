// PWS2LevelKit editor tooling: builds an Addressables content pack for PWS2LevelLoader.
// A "pack" is a folder Assets/Packs/<name>/ whose assets are gathered into one Addressables group.
// Output: <dist>/Mods/PWS2LevelLoader/packs/<name>/{catalog.bin, catalog.hash, *.bundle, pack.json}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace PWS2LevelKit.Editor
{
    public static class PackBuilder
    {
        public const string ProfileName = "PWS2Pack";
        public const string PacksRoot = "Assets/Packs";
        /// <summary>Game version every pack generator stamps into pack.json; the loader skips packs built for another version.</summary>
        public const string GameVersion = "1.3.1";

        /// <summary>Where the game expects the pack at runtime, relative to the game root.</summary>
        public static string RuntimePackFolder(string pack) => $"Mods/PWS2LevelLoader/packs/{pack}";

        /// <summary>Load path baked into the catalog: RuntimePath is <game>/PowerWash Simulator 2_Data/StreamingAssets/aa.</summary>
        public static string RuntimeLoadPath(string pack) =>
            "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/../../../" + RuntimePackFolder(pack);

        public static string DefaultDist => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "dist"));

        // ------------------------------------------------------------------ entry points

        [MenuItem("PWS2/Build pack from selected folder")]
        public static void BuildSelected()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) || !path.StartsWith(PacksRoot + "/")) { Debug.LogError($"Select a folder under {PacksRoot}"); return; }
            var pack = path.Substring(PacksRoot.Length + 1).Split('/')[0];
            Build(pack, DefaultDist);
        }

        /// <summary>Command line: Unity.exe -batchmode -quit -projectPath . -executeMethod PWS2LevelKit.Editor.PackBuilder.BuildFromCommandLine -pack SpikeA [-dist path]</summary>
        public static void BuildFromCommandLine()
        {
            string pack = Arg("-pack"), dist = Arg("-dist") ?? DefaultDist;
            if (string.IsNullOrEmpty(pack)) { Debug.LogError("PackBuilder: -pack <name> is required"); EditorApplication.Exit(2); return; }
            try
            {
                bool ok = Build(pack, dist);
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------ build

        public static bool Build(string pack, string dist)
        {
            var packFolder = $"{PacksRoot}/{pack}";
            if (!AssetDatabase.IsValidFolder(packFolder)) { Debug.LogError($"PackBuilder: {packFolder} does not exist"); return false; }
            if (!PackValidator.ValidateStubs()) { Debug.LogError("PackBuilder: stub layout does not match the game; see PackValidator errors"); return false; }

            var settings = GetOrCreateSettings();
            ConfigureSettings(settings, pack);
            var profileId = EnsureProfile(settings, pack);
            settings.activeProfileId = profileId;

            var group = EnsureGroup(settings, pack);
            // Only this pack's group is included in the build; every other pack group is switched off.
            foreach (var g in settings.groups)
            {
                var schema = g?.GetSchema<BundledAssetGroupSchema>();
                if (schema != null && g.Name.StartsWith("Pack_")) schema.IncludeInBuild = g == group;
            }

            int count = SyncEntries(settings, group, packFolder);
            Debug.Log($"PackBuilder: {count} addressable entries in {group.Name}");
            AssetDatabase.SaveAssets();

            // clean the build folder so stale bundles never ship
            var buildPath = settings.profileSettings.EvaluateString(profileId, settings.profileSettings.GetValueByName(profileId, "Local.BuildPath"));
            if (Directory.Exists(buildPath)) Directory.Delete(buildPath, true);

            AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (!string.IsNullOrEmpty(result.Error)) { Debug.LogError("PackBuilder: Addressables build failed: " + result.Error); return false; }
            // Addressables only logs (does not fail) when two bundles map to one file name; treat it as a failure here.
            var bundlePaths = result.AssetBundleBuildResults.Select(r => Path.GetFileName(r.FilePath)).ToList();
            if (bundlePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != bundlePaths.Count) { Debug.LogError("PackBuilder: bundle file name collision: " + string.Join(", ", bundlePaths)); return false; }
            Debug.Log("PackBuilder: bundles: " + string.Join(", ", bundlePaths));

            var outDir = Path.Combine(dist, RuntimePackFolder(pack).Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            foreach (var f in Directory.GetFiles(buildPath, "*.bundle")) File.Copy(f, Path.Combine(outDir, Path.GetFileName(f)));
            // the local catalog lands next to settings.json in Addressables.BuildPath (Library/com.unity.addressables/aa/<target>)
            var catalogDir = UnityEngine.AddressableAssets.Addressables.BuildPath;
            foreach (var f in Directory.GetFiles(catalogDir, "catalog*.*"))
            {
                File.Copy(f, Path.Combine(outDir, Path.GetFileName(f)));
                // the game derives a separate pack's catalog path as <loadPath>/catalog_<ID>.bin
                File.Copy(f, Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + "_" + pack + Path.GetExtension(f)));
            }
            var settingsJson = Path.Combine(catalogDir, "settings.json");
            if (File.Exists(settingsJson)) File.Copy(settingsJson, Path.Combine(outDir, "settings.json"));

            var packJson = Path.Combine(Application.dataPath, "..", packFolder, "pack.json");
            if (File.Exists(packJson)) File.Copy(packJson, Path.Combine(outDir, "pack.json"));
            else Debug.LogWarning($"PackBuilder: {packFolder}/pack.json missing; the loader needs it");

            Debug.Log($"PackBuilder: pack '{pack}' written to {outDir}\n" + string.Join("\n", Directory.GetFiles(outDir).Select(Path.GetFileName)));
            return true;
        }

        // ------------------------------------------------------------------ settings helpers

        public static AddressableAssetSettings GetOrCreateSettings()
        {
            var s = AddressableAssetSettingsDefaultObject.Settings;
            if (s == null)
            {
                s = AddressableAssetSettings.Create(AddressableAssetSettingsDefaultObject.kDefaultConfigFolder, AddressableAssetSettingsDefaultObject.kDefaultConfigAssetName, true, true);
                AddressableAssetSettingsDefaultObject.Settings = s;
            }
            return s;
        }

        private static void ConfigureSettings(AddressableAssetSettings s, string pack)
        {
            s.UniqueBundleIds = true;                 // never collide with the game's own bundle ids
            s.BuildRemoteCatalog = false;             // local catalog.bin next to the bundles
            s.DisableCatalogUpdateOnStartup = true;
            s.ContiguousBundles = true;
            s.NonRecursiveBuilding = true;
            s.MonoScriptBundleNaming = MonoScriptBundleNaming.Custom;
            s.MonoScriptBundleCustomNaming = SharedBundleName(pack);
            s.BuiltInBundleNaming = BuiltInBundleNaming.Custom;
            s.BuiltInBundleCustomNaming = SharedBundleName(pack);
            EditorUtility.SetDirty(s);
        }

        /// <summary>
        /// Name of the MonoScript / built-in-asset bundles. Unity derives the archive-internal CAB id from the bundle
        /// name, and refuses to load two bundles with the same id, so a name shared by every pack ("pws2levelkit")
        /// made the second pack unloadable once the first was loaded (Spike A vs Spike B). One name per pack.
        /// </summary>
        public static string SharedBundleName(string pack) => "pws2pack_" + pack.ToLowerInvariant();

        private static string EnsureProfile(AddressableAssetSettings s, string pack)
        {
            var ps = s.profileSettings;
            var id = ps.GetProfileId(ProfileName);
            if (string.IsNullOrEmpty(id)) id = ps.AddProfile(ProfileName, s.activeProfileId);
            ps.SetValue(id, "Local.BuildPath", "[UnityEngine.AddressableAssets.Addressables.BuildPath]/PWS2Packs/" + pack);
            ps.SetValue(id, "Local.LoadPath", RuntimeLoadPath(pack));
            return id;
        }

        private static AddressableAssetGroup EnsureGroup(AddressableAssetSettings s, string pack)
        {
            var name = "Pack_" + pack;
            var g = s.FindGroup(name) ?? s.CreateGroup(name, false, false, false, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var schema = g.GetSchema<BundledAssetGroupSchema>() ?? g.AddSchema<BundledAssetGroupSchema>();
            schema.BuildPath.SetVariableByName(s, "Local.BuildPath");
            schema.LoadPath.SetVariableByName(s, "Local.LoadPath");
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            // AppendHash, not NoHash: a group with a scene and assets yields two bundles, and with NoHash both were
            // written as pack_<name>_.bundle, the second overwriting the first (Spike A lost its WashTargetData asset).
            schema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
            schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            schema.IncludeInBuild = true;
            schema.IncludeAddressInCatalog = true;
            schema.IncludeGUIDInCatalog = true;
            schema.IncludeLabelsInCatalog = true;
            schema.UseAssetBundleCache = false;
            schema.UseAssetBundleCrc = false;
            schema.RetryCount = 0;
            EditorUtility.SetDirty(g);
            return g;
        }

        /// <summary>Every asset under the pack folder (except pack.json and .cs) becomes an entry addressed by its file name.</summary>
        private static int SyncEntries(AddressableAssetSettings s, AddressableAssetGroup g, string packFolder)
        {
            var guids = AssetDatabase.FindAssets("", new[] { packFolder });
            var keep = new HashSet<string>();
            int n = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".cs" || ext == ".json" || ext == ".asmdef" || ext == ".md") continue;
                var entry = s.CreateOrMoveEntry(guid, g, false, false);
                entry.address = Path.GetFileNameWithoutExtension(path);
                keep.Add(guid);
                n++;
            }
            foreach (var e in g.entries.ToList()) if (!keep.Contains(e.guid)) s.RemoveAssetEntry(e.guid, false);
            return n;
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
