using System;
using System.IO;
using Il2CppFuturLab;
using Il2CppFuturLab.PW2;
using Il2CppFuturLab.PW2.TexturePainter;
using Il2CppInterop.Runtime;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace PWS2LevelLoader.Debug
{
    /// <summary>
    /// Spike A (plan Phase 1): load a pack catalog built by PWS2LevelKit, load its test scene additively,
    /// and check that the stub-serialised components arrived as the real game types with their values.
    /// Driven by the F5 key or by the AutoSpike preference; polls Addressables handles from OnUpdate.
    /// </summary>
    internal static class SpikeRunner
    {
        private enum Step { Idle, LoadingCatalog, LoadingScene, LoadingManifests, LoadingGameAsset, Done }
        private static AsyncOperationHandle<Sprite> _gameSprite;
        private static readonly System.Collections.Generic.List<(Packs.PackManifest pack, AsyncOperationHandle<JobPackManifest> handle)> _manifestLoads = new System.Collections.Generic.List<(Packs.PackManifest, AsyncOperationHandle<JobPackManifest>)>();

        private static Step _step = Step.Idle;
        private static AsyncOperationHandle<IResourceLocator> _catalogHandle;
        private static AsyncOperationHandle<SceneInstance> _sceneHandle;
        private static string _pack, _sceneKey;
        private static float _startedAt;

        public static bool Running => _step != Step.Idle && _step != Step.Done;
        public static bool Finished => _step == Step.Done;
        public static bool Passed { get; private set; }

        public static string PackFolder(string pack) => Path.Combine(MelonEnvironment.GameRootDirectory, "Mods", "PWS2LevelLoader", "packs", pack);

        public static void StartSpikeA(string pack = "SpikeA", string sceneKey = "GAM_SPIKEA_Cube")
        {
            if (Running) { Core.Log.Warning("Spike already running"); return; }
            _pack = pack; _sceneKey = sceneKey; Passed = false;
            var catalog = Path.Combine(PackFolder(pack), "catalog.bin");
            if (!File.Exists(catalog)) { Core.Log.Error($"SPIKE A FAIL: catalog not found at {catalog}"); _step = Step.Done; return; }
            _startedAt = Time.realtimeSinceStartup;
            if (Packs.PackRegistry.IsCatalogReady(pack))
            {
                Core.Log.Msg($"SPIKE A: catalog for '{pack}' already loaded by the pack registry; loading scene '{_sceneKey}' additively");
                try { LoadScene(); } catch (Exception ex) { Core.Log.Error($"SPIKE A FAIL: LoadSceneAsync threw {ex}"); _step = Step.Done; }
                return;
            }
            Core.Log.Msg($"SPIKE A: loading catalog {catalog}");
            try
            {
                _catalogHandle = Addressables.LoadContentCatalogAsync(catalog, false, null);
                _step = Step.LoadingCatalog;
            }
            catch (Exception ex) { Core.Log.Error($"SPIKE A FAIL: LoadContentCatalogAsync threw {ex}"); _step = Step.Done; }
        }

        /// <summary>Spike M: load every pack's JobPackManifest the way the game does (AssetReferenceT.LoadAssetAsync) and dump it.</summary>
        public static void StartSpikeM()
        {
            if (Running) { Core.Log.Warning("Spike already running"); return; }
            Passed = false; _manifestLoads.Clear();
            foreach (var pack in Packs.PackRegistry.Packs)
            {
                if (string.IsNullOrEmpty(pack.JobPackManifestGuid)) continue;
                if (!Packs.PackRegistry.IsCatalogReady(pack.Id)) { Core.Log.Error($"SPIKE M: catalog for '{pack.Id}' not ready"); continue; }
                try
                {
                    var aref = new AssetReferenceT<JobPackManifest>(pack.JobPackManifestGuid);
                    Core.Log.Msg($"SPIKE M: loading manifest {pack.JobPackManifestGuid} of pack '{pack.Id}' (RuntimeKeyIsValid={Safe(() => aref.RuntimeKeyIsValid().ToString())})");
                    _manifestLoads.Add((pack, aref.LoadAssetAsync()));
                }
                catch (Exception ex) { Core.Log.Error($"SPIKE M FAIL: LoadAssetAsync threw for '{pack.Id}': {ex}"); }
            }
            if (_manifestLoads.Count == 0) { Core.Log.Error("SPIKE M FAIL: nothing to load"); _step = Step.Done; return; }
            _step = Step.LoadingManifests; _startedAt = Time.realtimeSinceStartup;
        }

        private static void FinishSpikeM()
        {
            bool all = true;
            var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PWS2LevelLoader", "dumps");
            foreach (var (pack, h) in _manifestLoads)
            {
                if (h.Status != AsyncOperationStatus.Succeeded || h.Result == null)
                {
                    all = false;
                    Core.Log.Error($"SPIKE M FAIL: '{pack.Id}' manifest load status {h.Status}: {Safe(() => h.OperationException?.ToString())}");
                    continue;
                }
                var m = h.Result;
                int jobs = Safe(() => m.m_gameJobs?.Count) ?? -1;
                Core.Log.Msg($"SPIKE M: '{pack.Id}' manifest '{m.name}' il2cpp={Safe(() => m.GetIl2CppType().FullName)} jobs={jobs} playerPrefabs={Safe(() => m.m_playerPrefabs?.Length.ToString())} networkPrefabs={Safe(() => m.m_networkPrefabs?.Length.ToString())} washers={Safe(() => m.m_powerWashers?.Count.ToString())} messages={Safe(() => m.m_messageSequences?.Count.ToString())} hud={Safe(() => m.HUDCustomization.LoadoutStanceSprites == null ? "null" : "ok")}");
                for (int i = 0; i < jobs; i++)
                {
                    var j = m.m_gameJobs[i];
                    if (j == null) { Core.Log.Error($"SPIKE M: job[{i}] is null (missing script?)"); all = false; continue; }
                    Core.Log.Msg($"   job '{j.name}' uniqueName={Safe(() => j.UniqueName)} id={Safe(() => j.UniqueId.ToString())} scene={Safe(() => j.Location?.SceneName)} sceneRef={Safe(() => j.Location?.SceneAssetReference?.AssetGUID)} swd={Safe(() => j.Location?.SceneWashableData?.name)} components={Safe(() => j.Location?.SceneWashableData?.m_washableComponents?.Length.ToString())} customer={Safe(() => j.Customer?.name)} stages={Safe(() => j.m_stages?.Length.ToString())} unlockedByDefault={Safe(() => j.UnlockedByDefault.ToString())} title={Safe(() => j.LocalizedName?.TableEntryReference.Key)}");
                    if (string.IsNullOrEmpty(Safe(() => j.Location?.SceneName))) all = false;
                }
                try
                {
                    var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}_spikeM_{pack.Id}.txt");
                    using var w = new StreamWriter(file, false, System.Text.Encoding.UTF8);
                    new Il2CppDump(w, maxDepth: 9, maxElems: 200).Dump("JobPackManifest", m);
                    Core.Log.Msg($"SPIKE M: full dump -> {file}");
                }
                catch (Exception ex) { Core.Log.Warning($"SPIKE M: dump failed: {ex.Message}"); }
            }
            Passed = all;
            Core.Log.Msg($"SPIKE M {(all ? "PASS" : "FAIL")}");
        }

        /// <summary>Spike G: with our catalogs loaded, can the game's own assets still be resolved? Loads Billboard's thumbnail sprite by GUID.</summary>
        public static void StartSpikeG()
        {
            if (Running) { Core.Log.Warning("Spike already running"); return; }
            Passed = false;
            int ours = 0; foreach (var l in Iter(Addressables.ResourceLocators)) { var id = Safe(() => l.LocatorId); Core.Log.Msg($"SPIKE G: locator '{id}'"); if (id.Contains("PWS2LevelLoader")) ours++; }
            Core.Log.Msg($"SPIKE G: {ours} pack locator(s) present; loading game sprite 53673f1550c0aca4284677e4b9740f96 (Thumbnail_PW2_Billboard_A)");
            try { _gameSprite = new AssetReferenceSprite("53673f1550c0aca4284677e4b9740f96").LoadAssetAsync(); _step = Step.LoadingGameAsset; _startedAt = Time.realtimeSinceStartup; }
            catch (Exception ex) { Core.Log.Error($"SPIKE G FAIL: LoadAssetAsync threw {ex}"); _step = Step.Done; }
        }

        public static void Update()
        {
            if (Input.GetKeyDown(KeyCode.F5) && !Running) StartSpikeA();
            if (!Running) return;
            if (Time.realtimeSinceStartup - _startedAt > 60f) { Core.Log.Error($"SPIKE FAIL: step {_step} timed out after 60 s"); _step = Step.Done; return; }

            try
            {
                switch (_step)
                {
                    case Step.LoadingCatalog:
                        if (!_catalogHandle.IsDone) return;
                        if (_catalogHandle.Status != AsyncOperationStatus.Succeeded)
                        {
                            Core.Log.Error($"SPIKE A FAIL: catalog load status {_catalogHandle.Status}: {Safe(() => _catalogHandle.OperationException?.ToString())}");
                            _step = Step.Done; return;
                        }
                        Core.Log.Msg($"SPIKE A: catalog loaded, locator id = {Safe(() => _catalogHandle.Result.LocatorId)}; loading scene '{_sceneKey}' additively");
                        LoadScene();
                        return;

                    case Step.LoadingGameAsset:
                        if (!_gameSprite.IsDone) return;
                        Passed = _gameSprite.Status == AsyncOperationStatus.Succeeded && _gameSprite.Result != null;
                        Core.Log.Msg($"SPIKE G {(Passed ? "PASS" : "FAIL")}: status={_gameSprite.Status} result={(Passed ? _gameSprite.Result.name : "null")} ex={Safe(() => _gameSprite.OperationException?.ToString())}");
                        _step = Step.Done;
                        return;

                    case Step.LoadingManifests:
                        foreach (var (_, h) in _manifestLoads) if (!h.IsDone) return;
                        FinishSpikeM();
                        _step = Step.Done;
                        return;

                    case Step.LoadingScene:
                        if (!_sceneHandle.IsDone) return;
                        if (_sceneHandle.Status != AsyncOperationStatus.Succeeded)
                        {
                            Core.Log.Error($"SPIKE A FAIL: scene load status {_sceneHandle.Status}: {Safe(() => _sceneHandle.OperationException?.ToString())}");
                            _step = Step.Done; return;
                        }
                        var scene = _sceneHandle.Result.Scene;
                        Core.Log.Msg($"SPIKE A: scene '{scene.name}' loaded, {scene.rootCount} roots");
                        Verify(scene);
                        _step = Step.Done;
                        return;
                }
            }
            catch (Exception ex)
            {
                Core.Log.Error($"SPIKE A FAIL: exception in step {_step}: {ex}");
                _step = Step.Done;
            }
        }

        private static void LoadScene()
        {
            Il2CppSystem.Object sceneKey = new Il2CppSystem.String(IL2CPP.ManagedStringToIl2Cpp(_sceneKey));
            _sceneHandle = Addressables.LoadSceneAsync(sceneKey, LoadSceneMode.Additive, true, 100, SceneReleaseMode.ReleaseSceneWhenSceneUnloaded);
            _step = Step.LoadingScene;
        }

        private static void Verify(Scene scene)
        {
            int washTargets = 0, uniqueIds = 0, roots = 0, spawns = 0;
            bool cubeIdOk = false, dataOk = false, rootLinkOk = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                foreach (var c in root.GetComponentsInChildren(Il2CppType.Of<Component>(), true))
                {
                    if (c == null) continue;
                    string tn = Safe(() => c.GetIl2CppType().FullName);
                    var uid = c.TryCast<UniqueId>();
                    if (uid != null) { uniqueIds++; Core.Log.Msg($"   UniqueId on '{c.gameObject.name}': m_id={uid.m_id} FullName={Safe(() => uid.FullName)}"); if (uid.m_id == 4242) cubeIdOk = true; }
                    var wt = c.TryCast<WashTarget>();
                    if (wt != null)
                    {
                        washTargets++;
                        var d = wt.m_washTargetData;
                        Core.Log.Msg($"   WashTarget on '{c.gameObject.name}': il2cpp type {tn}, data={(d == null ? "null" : d.name)} res={Safe(() => d?.SuggestedControlMapResolution.ToString())} areaInUse={Safe(() => d?.ControlMapAreaInUse.ToString())} threshold={Safe(() => d?.SurfaceCleanThreshold.ToString())} root={(wt.m_gameJobWashTargetRoot == null ? "null" : wt.m_gameJobWashTargetRoot.gameObject.name)} renderer={(wt.m_meshRenderer == null ? "null" : "ok")} uid={(wt.m_uniqueId == null ? "null" : wt.m_uniqueId.m_id.ToString())}");
                        if (d != null && d.SuggestedControlMapResolution == 64) dataOk = true;
                        if (wt.m_gameJobWashTargetRoot != null) rootLinkOk = true;
                    }
                    if (c.TryCast<GameJobWashTargetRoot>() != null) { roots++; Core.Log.Msg($"   GameJobWashTargetRoot on '{c.gameObject.name}' ({tn})"); }
                    var sp = c.TryCast<CharacterSpawnPosition>();
                    if (sp != null) { spawns++; Core.Log.Msg($"   CharacterSpawnPosition on '{c.gameObject.name}': type={sp.m_type} radius={sp.m_Radius} mask={sp.m_obstructionMask.value}"); }
                }
            }
            Passed = washTargets == 1 && cubeIdOk && dataOk && rootLinkOk && roots == 1 && spawns == 1;
            Core.Log.Msg($"SPIKE A {(Passed ? "PASS" : "FAIL")}: washTargets={washTargets} uniqueIds={uniqueIds} roots={roots} spawns={spawns} cubeId4242={cubeIdOk} data64={dataOk} rootLink={rootLinkOk}");
        }

        private static System.Collections.Generic.IEnumerable<T> Iter<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> src) where T : class
        {
            if (src == null) yield break;
            var e = src.GetEnumerator();
            var b = e.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (b.MoveNext()) yield return e.Current;
        }

        private static string Safe(Func<string> f) { try { return f() ?? "null"; } catch (Exception ex) { return $"<{ex.GetType().Name}>"; } }
        private static int? Safe(Func<int?> f) { try { return f(); } catch { return null; } }
    }
}
