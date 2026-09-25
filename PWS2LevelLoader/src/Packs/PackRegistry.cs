using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppFuturLab;
using Il2CppFuturLab.ContentPacks;
using Il2CppFuturLab.PW2;
using Il2CppFuturLab.PW2.TexturePainter;
using Il2CppFuturLab.PW2.WashGraphics;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;

namespace PWS2LevelLoader.Packs
{
    /// <summary>
    /// Finds content packs under Mods/PWS2LevelLoader/packs and turns each pack that ships a JobPackManifest into a
    /// CampaignJobPackReference for the game's default campaign (ContentPack = null, manifest by GUID, like the base
    /// game's own pack). Pack catalogs are loaded asynchronously from BootScene so they are ready ~3 s later when
    /// Campaign.LoadContent runs. See docs/phase0-findings.md §2 and docs/phase1-spikes.md.
    /// </summary>
    internal static class PackRegistry
    {
        public const string StringTableCollection = "PWS2_Main";

        public static readonly List<PackManifest> Packs = new List<PackManifest>();
        private static bool _stringsRegistered;
        private static readonly Dictionary<string, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator>> _catalogHandles
            = new Dictionary<string, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _catalogsReady = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _catalogsFailed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Use the game's ContentPackManifest.LoadCatalog path (hangs boot for non-DLC packs in v1.3.0; kept for experiments).</summary>
        private const bool UseGameCatalogLoader = false;
        /// <summary>The base game's own pack references carry no previews; the Jobs menu builds them elsewhere.</summary>
        private const bool BuildPreviews = false;

        public static string PacksRoot => Path.Combine(MelonEnvironment.GameRootDirectory, "Mods", "PWS2LevelLoader", "packs");
        public static bool StringsRegistered => _stringsRegistered;

        // ------------------------------------------------------------------ discovery

        public static void Discover()
        {
            Packs.Clear();
            if (!Directory.Exists(PacksRoot)) { Core.Log.Msg($"No packs folder at {PacksRoot}"); return; }
            foreach (var dir in Directory.GetDirectories(PacksRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var json = Path.Combine(dir, "pack.json");
                if (!File.Exists(json)) { Core.Log.Warning($"Skipping {Path.GetFileName(dir)}: no pack.json"); continue; }
                try
                {
                    var m = PackManifest.Load(json);
                    if (!File.Exists(m.CatalogPath)) { Core.Log.Warning($"Skipping pack '{m.Id}': catalog missing at {m.CatalogPath}"); continue; }
                    if (!string.IsNullOrEmpty(m.GameVersion) && m.GameVersion != Application.version && !Core.IgnoreGameVersion.Value)
                    {
                        Core.Log.Warning($"Skipping pack '{m.Id}': built for game {m.GameVersion}, running {Application.version} (set IgnoreGameVersion=true to force)");
                        continue;
                    }
                    Packs.Add(m);
                    Core.Log.Msg($"Pack '{m.Id}' v{m.Version} ({m.DisplayName}) — {m.Jobs.Count} job(s), manifest {(string.IsNullOrEmpty(m.JobPackManifestGuid) ? "none" : m.JobPackManifestGuid)}");
                }
                catch (Exception ex) { Core.Log.Error($"Skipping pack in {dir}: {ex.Message}"); }
            }
        }

        // ------------------------------------------------------------------ catalogs (async, started at BootScene)

        /// <summary>Kick off every pack catalog load without blocking. Poll with <see cref="PollCatalogs"/>.</summary>
        public static void BeginCatalogLoads()
        {
            if (UseGameCatalogLoader) return;
            foreach (var pack in Packs)
            {
                if (_catalogHandles.ContainsKey(pack.Id)) continue;
                try
                {
                    var path = Path.GetFullPath(pack.CatalogPath).Replace(Path.DirectorySeparatorChar, '/');
                    _catalogHandles[pack.Id] = Addressables.LoadContentCatalogAsync(path, false, null);
                    Core.Log.Msg($"Pack '{pack.Id}': catalog load started ({path})");
                }
                catch (Exception ex) { _catalogsFailed.Add(pack.Id); Core.Log.Error($"Pack '{pack.Id}': LoadContentCatalogAsync threw {ex}"); }
            }
        }

        /// <summary>Call every frame; logs completion once per catalog.</summary>
        public static void PollCatalogs()
        {
            foreach (var kv in _catalogHandles)
            {
                if (_catalogsReady.Contains(kv.Key) || _catalogsFailed.Contains(kv.Key)) continue;
                var h = kv.Value;
                if (!h.IsDone) continue;
                if (h.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && h.Result != null)
                {
                    _catalogsReady.Add(kv.Key);
                    Core.Log.Msg($"Pack '{kv.Key}': catalog ready (locator {SafeStr(() => h.Result.LocatorId)})");
                }
                else
                {
                    _catalogsFailed.Add(kv.Key);
                    Core.Log.Error($"Pack '{kv.Key}': catalog load failed ({h.Status}) {SafeStr(() => h.OperationException?.ToString())}");
                }
            }
        }

        public static bool IsCatalogReady(string packId)
        {
            PollCatalogs();
            return _catalogsReady.Contains(packId);
        }

        // ------------------------------------------------------------------ registration objects

        /// <summary>Pack references to append to the default campaign; only packs with a JobPackManifest.</summary>
        public static List<CampaignJobPackReference> BuildPackReferences()
        {
            var list = new List<CampaignJobPackReference>();
            foreach (var pack in Packs)
            {
                if (string.IsNullOrEmpty(pack.JobPackManifestGuid)) continue;
                if (!UseGameCatalogLoader && !IsCatalogReady(pack.Id))
                {
                    Core.Log.Warning($"Pack '{pack.Id}': catalog not ready when the campaign loaded; pack skipped this session");
                    continue;
                }
                try
                {
                    // IL2CPP stripped the parameterless constructor (the game only deserialises these), so allocate raw.
                    var r = NewIl2CppObject<CampaignJobPackReference>();
                    r.ContentManifest = new AssetReferenceT<JobPackManifest>(pack.JobPackManifestGuid);
                    r.ContentPack = UseGameCatalogLoader ? BuildContentPack(pack) : null;

                    var dlc = new DLCPackSettings();
                    dlc.m_isDlc = false;
                    dlc.m_dlcEntitlementRequired = false;
                    dlc.m_stringTableName = StringTableCollection;
                    r.DLCPackSettings = dlc;

                    var previews = new Il2CppReferenceArray<CampaignJobPreview>(BuildPreviews ? pack.Jobs.Count : 0);
                    for (int i = 0; i < previews.Length; i++)
                    {
                        var j = pack.Jobs[i];
                        var n = (j.UniqueName ?? "").ToUpperInvariant();
                        var p = new CampaignJobPreview();
                        p.UniqueID = j.UniqueId;
                        p.UniqueName = j.UniqueName;
                        p.LocalizedName = MakeLocalized(StringTableCollection, $"SCRIPT_CAREER_{n}_TITLE");
                        p.LocationAddress = MakeLocalized(StringTableCollection, $"SCRIPT_CAREER_{n}_ADDRESS");
                        if (!string.IsNullOrEmpty(j.ThumbnailSelectedGuid)) p.SelectedThumbnailImageAsset = new AssetReferenceSprite(j.ThumbnailSelectedGuid);
                        if (!string.IsNullOrEmpty(j.ThumbnailDeselectedGuid)) p.DeselectedThumbnailImageAsset = new AssetReferenceSprite(j.ThumbnailDeselectedGuid);
                        previews[i] = p;
                    }
                    r.m_jobPreviews = previews;
                    list.Add(r);
                    Core.Log.Msg($"Pack '{pack.Id}': pack reference built ({previews.Length} preview(s), contentPack={(r.ContentPack == null ? "null" : r.ContentPack.RuntimeCatalogPath)})");
                }
                catch (Exception ex) { Core.Log.Error($"Pack '{pack.Id}': building pack reference threw {ex}"); }
            }
            return list;
        }

        /// <summary>
        /// A ContentPackManifest describing the pack folder as a separate catalog, mirroring the DLC ones
        /// (ID/ShortId, Linked, no DLC entitlement, availability = all platforms). The game computes
        /// RuntimeCatalogPath from the load path + "/catalog_&lt;ID&gt;.bin", so that file is created beside catalog.bin.
        /// </summary>
        private static ContentPackManifest BuildContentPack(PackManifest pack)
        {
            var folder = Path.GetFullPath(pack.Folder).Replace('\\', '/').TrimEnd('/');
            var named = Path.Combine(pack.Folder, $"catalog_{pack.Id}.bin");
            if (!File.Exists(named) || File.GetLastWriteTimeUtc(named) < File.GetLastWriteTimeUtc(pack.CatalogPath))
            {
                File.Copy(pack.CatalogPath, named, true);
                var hash = Path.ChangeExtension(pack.CatalogPath, ".hash");
                if (File.Exists(hash)) File.Copy(hash, Path.Combine(pack.Folder, $"catalog_{pack.Id}.hash"), true);
            }

            var cp = ScriptableObject.CreateInstance(Il2CppType.Of<ContentPackManifest>()).Cast<ContentPackManifest>();
            cp.name = "ContentPack_" + pack.Id;
            cp.hideFlags = HideFlags.DontUnloadUnusedAsset;
            cp._ID_k__BackingField = pack.Id;
            cp._ShortId_k__BackingField = pack.Id;
            cp._Type_k__BackingField = ContentPackType.Linked;
            cp._DLC_k__BackingField = null;
            cp.m_dependencies = new Il2CppReferenceArray<ContentPackManifest>(0);
            cp._InstallDirectory_k__BackingField = folder;
            cp._IsSeparateCatalog_k__BackingField = true;
            cp._CoreGroupsToSkip_k__BackingField = new Il2CppSystem.Collections.Generic.List<string>();
            cp.m_buildPathVariableName = pack.Id + ".BuildPath";
            cp.m_buildPathValue = folder;
            cp.m_loadPathVariableName = pack.Id + ".LoadPath";
            cp.m_loadPathValue = folder;
            cp.m_catalogLoaded = false;

            var avail = ScriptableObject.CreateInstance(Il2CppType.Of<AvailabilityProfile>()).Cast<AvailabilityProfile>();
            avail.name = "Availability_" + pack.Id;
            avail.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var cond = new PlatformConditional();
            cond.Platform = Platform.All;
            cond.ReleaseType = ReleaseType.All;
            cond.EditorModifier = EditorModifier.SameAsOnBuilds;
            avail.Condition = cond;
            avail.m_description = "PWS2LevelLoader pack " + pack.Id;
            cp._Availability_k__BackingField = avail;
            return cp;
        }

        public static LocalizedString MakeLocalized(string table, string key)
        {
            var ls = new LocalizedString();
            ls.TableReference = table;      // implicit string -> TableReference
            ls.TableEntryReference = key;   // implicit string -> TableEntryReference
            return ls;
        }

        // ------------------------------------------------------------------ pack scenes

        private const string WashShaderName = "PWS/Wash Target Uber";

        /// <summary>
        /// Called from Core.OnSceneWasLoaded, i.e. before the game's level pipeline continues past the scene load.
        /// Gives every WashTarget in a pack scene the two things a pack cannot serialise: the game's shared
        /// WashRenderSettings asset and the PWS/Wash Target Uber shader (packs author materials with URP/Lit; the
        /// base properties are mapped across here, the wash properties are filled by the game at wash init).
        /// </summary>
        public static void PrepareScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            var jobs = Packs.SelectMany(p => p.Jobs).Where(j => j.SceneName == sceneName).ToList();
            if (jobs.Count == 0) return;
            // a pack scene always; a game scene (augmented job) only when that pack job is the one loading
            // (SceneAugmenter.Apply runs just before this, from the same OnSceneWasLoaded)
            if (jobs.All(j => j.AugmentsScene) && (SceneAugmenter.AppliedScene != sceneName || SceneAugmenter.AppliedJob == null)) return;
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded) { Core.Log.Warning($"PrepareScene: '{sceneName}' is not loaded"); return; }

            WashRenderSettings settings = null;
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<WashRenderSettings>()))
            {
                settings = o?.TryCast<WashRenderSettings>();
                if (settings != null) break;
            }
            var shader = Shader.Find(WashShaderName);
            if (shader == null)
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Shader>()))
                {
                    var s = o?.TryCast<Shader>();
                    if (s != null && s.name == WashShaderName) { shader = s; break; }
                }

            int targets = 0, settingsSet = 0, swapped = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                foreach (var c in root.GetComponentsInChildren(Il2CppType.Of<WashTarget>(), true))
                {
                    var wt = c?.TryCast<WashTarget>();
                    if (wt == null) continue;
                    targets++;
                    if (wt.m_renderSettings == null && settings != null) { wt.m_renderSettings = settings; settingsSet++; }
                    var mr = wt.m_meshRenderer;
                    if (mr == null || shader == null) continue;
                    var mats = mr.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || (m.shader != null && m.shader.name == WashShaderName)) continue;
                        var nm = new Material(shader);
                        nm.name = m.name + "_wash";
                        if (m.HasProperty("_BaseMap")) nm.SetTexture("_BaseColour", m.GetTexture("_BaseMap"));
                        if (m.HasProperty("_BaseColor")) nm.SetColor("_BaseColourTint", m.GetColor("_BaseColor"));
                        if (m.HasProperty("_BumpMap")) nm.SetTexture("_Normal", m.GetTexture("_BumpMap"));
                        nm.SetVector("_Tiling", new Vector4(1, 1, 0, 0));
                        mats[i] = nm; changed = true; swapped++;
                    }
                    if (changed) mr.sharedMaterials = mats;
                }
            }
            Core.Log.Msg($"PrepareScene '{sceneName}': {targets} wash target(s); WashRenderSettings {(settings == null ? "NOT FOUND" : $"'{settings.name}' set on {settingsSet}")}; shader {(shader == null ? "NOT FOUND" : $"'{shader.name}' applied to {swapped} material(s)")}");
        }

        /// <summary>
        /// Diagnostic: list every PrefabLoader in a freshly loaded GAM_ scene. The game instantiates each entry's
        /// prefab (GameplayAudioManager, GraphicsPipelineManager, cats, ...) and registers the instances into the
        /// level's VContainer scope (ContainerUtil.WaitForPrefabLoader / RegisterBootSystems); pack scenes need the
        /// same list, so this captures the GUIDs from the game's own scenes.
        /// </summary>
        public static void LogPrefabLoaders(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || !sceneName.StartsWith("GAM_", StringComparison.Ordinal)) return;
            try
            {
                var scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid() || !scene.isLoaded) return;
                int loaders = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null) continue;
                    foreach (var c in root.GetComponentsInChildren(Il2CppType.Of<PrefabLoader>(), true))
                    {
                        var pl = c?.TryCast<PrefabLoader>();
                        if (pl == null) continue;
                        loaders++;
                        var arr = pl.m_prefabsToLoad;
                        Core.Log.Msg($"[prefabs] {sceneName}: PrefabLoader on '{TransformPath(pl.transform)}' (active={pl.gameObject.activeInHierarchy}, parent={SafeStr(() => pl.m_parentLoader == null ? "none" : TransformPath(pl.m_parentLoader.transform))}, timePerFrame={pl.m_instantiateTimePerFrame}) with {(arr == null ? 0 : arr.Length)} entries");
                        if (arr == null) continue;
                        for (int i = 0; i < arr.Length; i++)
                        {
                            var p = arr[i];
                            if (p == null) { Core.Log.Msg($"[prefabs]   [{i}] null"); continue; }
                            string guid = SafeStr(() => p.PrefabAsset?.AssetGUID), name = SafeStr(() => p.DebugName);
                            string cond = "";
                            try
                            {
                                var cs = p.m_executeConditions;
                                if (cs != null) for (int k = 0; k < cs.Length; k++) cond += $"{(k > 0 ? ";" : "")}{cs[k].Platform}/{cs[k].ReleaseType}/{cs[k].EditorModifier}";
                            }
                            catch { cond = "?"; }
                            Core.Log.Msg($"[prefabs]   [{i}] guid={guid} rootLevel={SafeStr(() => p.InstantiateAtRootLevel.ToString())} xr={SafeStr(() => p.m_useXRCondition.ToString())} cond=[{cond}] name={name}");
                        }
                    }
                }
                int all = 0;
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PrefabLoader>())) if (o != null) all++;
                Core.Log.Msg($"[prefabs] {sceneName}: {loaders} PrefabLoader component(s) in the scene, {all} loaded in total");
            }
            catch (Exception ex) { Core.Log.Warning($"LogPrefabLoaders failed: {ex.GetType().Name}: {ex.Message}"); }
        }

        private static string TransformPath(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        // ------------------------------------------------------------------ after load

        /// <summary>Log whether the default campaign actually loaded each pack job (call once the hub is up).</summary>
        public static void LogLoadedJobs()
        {
            try
            {
                var manifests = Resources.FindObjectsOfTypeAll(Il2CppType.Of<GameDataManifest>());
                GameDataManifest manifest = null;
                foreach (var o in manifests) { manifest = o?.TryCast<GameDataManifest>(); if (manifest != null) break; }
                if (manifest == null) { Core.Log.Warning("LogLoadedJobs: no GameDataManifest loaded"); return; }
                var loaded = new Dictionary<int, GameJob>();
                foreach (var j in Iter(manifest.GetLoadedGameJobs())) if (j != null) loaded[j.UniqueId] = j;
                Core.Log.Msg($"Loaded game jobs: {loaded.Count}");
                LogCampaignManifestKeys();
                if (loaded.TryGetValue(2015219005, out var billboard)) Debug.JobHealth.Log(billboard, "reference Billboard");
                foreach (var pack in Packs)
                    foreach (var pj in pack.Jobs)
                    {
                        if (loaded.TryGetValue(pj.UniqueId, out var job))
                        {
                            Core.Log.Msg($"REGISTERED: pack '{pack.Id}' job '{pj.UniqueName}' ({pj.UniqueId}) -> '{job.name}' scene={SafeStr(() => job.Location?.SceneName)} unlockedByDefault={job.UnlockedByDefault} title={SafeStr(() => job.LocalizedName?.GetLocalizedString())}");
                            Debug.JobHealth.Log(job, pack.Id);
                            LogJobStatus(job);
                        }
                        else
                            Core.Log.Warning($"NOT REGISTERED: pack '{pack.Id}' job '{pj.UniqueName}' ({pj.UniqueId})");
                    }
            }
            catch (Exception ex) { Core.Log.Error($"LogLoadedJobs failed: {ex}"); }
        }

        /// <summary>How the default campaign classifies a job against the current save (what the Jobs menu goes by).</summary>
        public static void LogJobStatus(GameJob job)
        {
            try
            {
                var psd = SaveManager.Instance?.PlayerSaveData;
                if (psd == null) { Core.Log.Warning($"JobStatus[{job.name}]: player save data not loaded yet"); return; }
                Campaign campaign = null;
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Campaign>()))
                {
                    var c = o?.TryCast<Campaign>();
                    if (c != null && c.UniqueName == Patches.CampaignInjection.DefaultCampaignName) { campaign = c; break; }
                }
                if (campaign == null) { Core.Log.Warning($"JobStatus[{job.name}]: default campaign not found"); return; }
                var csd = psd.GetCampaignSaveData(campaign.UniqueId);
                if (csd == null) { Core.Log.Warning($"JobStatus[{job.name}]: no campaign save data"); return; }
                Core.Log.Msg($"JobStatus[{job.name}]: freePlayUnlocked={campaign.IsFreePlayUnlocked(job, csd)} freePlay={campaign.GetFreePlayStatus(job, csd)} career={campaign.GetCareerJobStatus(job, csd)} level(FreePlay)={campaign.GetLevelStatus(GameMode.FreePlay, job, csd)} level(Career)={campaign.GetLevelStatus(GameMode.Career, job, csd)} isDlc={campaign.IsDlc(job)}");
            }
            catch (Exception ex) { Core.Log.Warning($"JobStatus[{job?.name}] failed: {ex.GetType().Name}: {ex.Message}"); }
        }

        /// <summary>
        /// Campaign.m_loadedContentManifests is keyed by the pack reference index and some game code walks it as
        /// 0..Count-1, so the loaded keys must stay contiguous. Log them for the default campaign.
        /// </summary>
        public static void LogCampaignManifestKeys()
        {
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Campaign>()))
                {
                    var c = o?.TryCast<Campaign>();
                    if (c == null || c.UniqueName != Patches.CampaignInjection.DefaultCampaignName) continue;
                    var dict = c.m_loadedContentManifests;
                    int refs = c.m_jobPackReferences?.Length ?? 0;
                    if (dict == null) { Core.Log.Msg($"Campaign DEFAULT: {refs} pack refs, loaded manifests = null"); return; }
                    var sb = new System.Text.StringBuilder();
                    for (int k = 0; k < refs + 2; k++)
                    {
                        if (!dict.ContainsKey(k)) continue;
                        var m = dict[k];
                        sb.Append(sb.Length == 0 ? "" : ", ").Append(k).Append('=').Append(m == null ? "null" : m.name);
                    }
                    Core.Log.Msg($"Campaign DEFAULT: {refs} pack refs, {dict.Count} loaded manifest(s): {{{sb}}}");
                    return;
                }
            }
            catch (Exception ex) { Core.Log.Warning($"LogCampaignManifestKeys failed: {ex.GetType().Name}: {ex.Message}"); }
        }

        /// <summary>Add every pack string (derived + explicit) to the loaded English PWS2_Main table. Idempotent.</summary>
        public static void RegisterStrings()
        {
            if (Packs.Count == 0) return;
            StringTable table = null;
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<StringTable>()))
            {
                var t = o?.TryCast<StringTable>();
                if (t == null) continue;
                string coll = SafeStr(() => t.TableCollectionName), code = SafeStr(() => t.LocaleIdentifier.Code);
                if (coll == StringTableCollection && code == "en") { table = t; break; }
            }
            if (table == null) { Core.Log.Warning("RegisterStrings: PWS2_Main (en) string table not loaded yet"); return; }

            int added = 0, total = 0;
            foreach (var pack in Packs)
            {
                var all = new Dictionary<string, string>(pack.Strings ?? new Dictionary<string, string>());
                foreach (var j in pack.Jobs) foreach (var kv in j.DerivedStrings()) if (!all.ContainsKey(kv.Key)) all[kv.Key] = kv.Value;
                foreach (var kv in all)
                {
                    total++;
                    try
                    {
                        var existing = table.GetEntry(kv.Key);
                        if (existing != null) { existing.Value = kv.Value; continue; }
                        table.AddEntry(kv.Key, kv.Value);
                        added++;
                    }
                    catch (Exception ex) { Core.Log.Warning($"RegisterStrings: '{kv.Key}' failed: {ex.Message}"); }
                }
            }
            _stringsRegistered = true;
            Core.Log.Msg($"RegisterStrings: {added} added / {total} total entries in {table.name}");
        }

        // ------------------------------------------------------------------ interop helpers

        private static T NewIl2CppObject<T>() where T : Il2CppSystem.Object
        {
            var ptr = IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<T>.NativeClassPtr);
            return (T)Activator.CreateInstance(typeof(T), ptr);
        }

        private static IEnumerable<T> Iter<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> src) where T : class
        {
            if (src == null) yield break;
            var e = src.GetEnumerator();
            var b = e.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (b.MoveNext()) yield return e.Current;
        }

        private static string SafeStr(Func<string> f) { try { return f() ?? "null"; } catch (Exception ex) { return $"<{ex.GetType().Name}>"; } }
    }
}
