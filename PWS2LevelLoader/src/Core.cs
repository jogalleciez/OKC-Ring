using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(PWS2LevelLoader.Core), "PWS2 Level Loader", "0.1.0", "jogal")]
[assembly: MelonGame("FuturLab", "PowerWash Simulator 2")]

namespace PWS2LevelLoader
{
    /// <summary>
    /// MelonLoader entry point. Phase 0: preferences + debug dumper (manual keys and an automation
    /// mode driven by preferences or command-line switches). Later phases add pack discovery and
    /// the Campaign.LoadContent hook.
    ///
    /// Command-line overrides (win over preferences):
    ///   --pws2ll-autodump            dump every GAM_* scene after it initialises
    ///   --pws2ll-delay=8             seconds to wait after scene init before dumping
    ///   --pws2ll-cheat="Name"        after the hub dump, invoke this cheat-menu button by name
    ///   --pws2ll-quit=1              quit the game after this many auto-dumps (0 = never)
    ///   --pws2ll-timeout=150         if no GAM_* scene initialised by then, dump whatever is loaded (and quit if --pws2ll-quit)
    /// </summary>
    public class Core : MelonMod
    {
        internal static Core Instance { get; private set; }
        internal static MelonLogger.Instance Log => Instance.LoggerInstance;

        internal static MelonPreferences_Category Prefs;
        internal static MelonPreferences_Entry<bool> EnableDebugKeys;
        internal static MelonPreferences_Entry<bool> IgnoreGameVersion;
        internal static MelonPreferences_Entry<bool> AutoDump;
        internal static MelonPreferences_Entry<float> AutoDumpDelaySeconds;
        internal static MelonPreferences_Entry<string> AutoRunCheat;
        internal static MelonPreferences_Entry<int> AutoQuitAfterDumps;
        internal static MelonPreferences_Entry<string> AutoSpike;
        internal static MelonPreferences_Entry<bool> AutoResetPackTargets;
        internal static MelonPreferences_Entry<bool> RegisterPacks;
        internal static MelonPreferences_Entry<string> AutoRunCheat2;
        internal static MelonPreferences_Entry<float> AutoRunCheat2DelaySeconds;
        internal static MelonPreferences_Entry<string> SceneAdditionOverride;

        private const string HubScene = "GAM_PW2_HB_HomeBase";
        private string _autoCheat2;
        private float _autoDelay2 = 10f;
        private bool _cheat2Fired;

        // effective automation settings (prefs overridden by CLI)
        private bool _autoDump;
        private float _autoDelay;
        private string _autoCheat;
        private int _autoQuitAfter;
        private string _autoSpike;
        private bool _spikeStarted, _spikeDumped;
        private Queue<string> _spikeQueue;
        private float _autoTimeout = 150f;
        private float _autoTimeoutAt = -1f;

        private float _dumpDueAt = -1f;
        private float _spikeDueAt = -1f;
        private string _pendingScene;
        private int _dumpsDone;
        private bool _cheatFired;

        public override void OnInitializeMelon()
        {
            Instance = this;

            Prefs = MelonPreferences.CreateCategory("PWS2LevelLoader", "PWS2 Level Loader");
            EnableDebugKeys = Prefs.CreateEntry("EnableDebugKeys", true,
                description: "Enable the F6/F7/F8 dump keys (writes to UserData/PWS2LevelLoader/dumps). Turn off for normal play.");
            IgnoreGameVersion = Prefs.CreateEntry("IgnoreGameVersion", false,
                description: "Load packs even if their gameVersion does not match the running game.");
            AutoDump = Prefs.CreateEntry("AutoDump", false,
                description: "Automatically run all dumps a few seconds after each GAM_* scene initialises.");
            AutoDumpDelaySeconds = Prefs.CreateEntry("AutoDumpDelaySeconds", 8f,
                description: "Seconds to wait after a scene initialises before auto-dumping.");
            AutoRunCheat = Prefs.CreateEntry("AutoRunCheat", "",
                description: "Name of a cheat-menu button to invoke after the hub auto-dump (e.g. to load a job). Empty = none.");
            AutoQuitAfterDumps = Prefs.CreateEntry("AutoQuitAfterDumps", 0,
                description: "Quit the game after this many auto-dumps. 0 = never.");
            AutoRunCheat2 = Prefs.CreateEntry("AutoRunCheat2", "",
                description: "Cheat-menu button to invoke after the job-scene auto-dump (e.g. 'Complete All Tasks - Instant'); a further dump follows AutoRunCheat2DelaySeconds later. Empty = none.");
            AutoRunCheat2DelaySeconds = Prefs.CreateEntry("AutoRunCheat2DelaySeconds", 10f,
                description: "Seconds between AutoRunCheat2 and the dump that records its effect.");
            AutoResetPackTargets = Prefs.CreateEntry("AutoResetPackTargets", false,
                description: "Dev: during an AutoDump run, put the dirt back on a pack level's wash targets a few seconds after it loads (session only), so the dumps show it dirty even when the save has it clean.");
            RegisterPacks = Prefs.CreateEntry("RegisterPacks", true,
                description: "Register discovered packs with the default campaign at boot. Set false to run diagnostics without touching the campaign.");
            AutoSpike = Prefs.CreateEntry("AutoSpike", "",
                description: "Comma-separated spikes to run unattended once the hub has loaded: A = SpikeA scene + stub components, M = load each pack's JobPackManifest directly and dump it. Empty = off.");
            SceneAdditionOverride = Prefs.CreateEntry("SceneAdditionOverride", "",
                description: "Dev: place a pack's scene addition (e.g. the OKC ring in the Streetcar street) at 'x,y,z[,yaw]' instead of the pack.json position, to tune placement without rebuilding the pack. Empty = use pack.json.");
            Prefs.SaveToFile(false);

            _autoDump = AutoDump.Value;
            _autoDelay = AutoDumpDelaySeconds.Value;
            _autoCheat = AutoRunCheat.Value;
            _autoQuitAfter = AutoQuitAfterDumps.Value;
            _autoSpike = AutoSpike.Value;
            _autoCheat2 = AutoRunCheat2.Value;
            _autoDelay2 = AutoRunCheat2DelaySeconds.Value;
            ApplyCommandLine();

            LoggerInstance.Msg($"PWS2 Level Loader {Info.Version} | game {Application.version} | Unity {Application.unityVersion}");
            LoggerInstance.Msg($"Debug keys: {(EnableDebugKeys.Value ? "ON  (F6 control maps, F7 scene, F8 game data)" : "off")}");
            if (_autoDump)
                LoggerInstance.Msg($"AutoDump ON: delay={_autoDelay}s cheat='{_autoCheat}' quitAfter={_autoQuitAfter}");
            if (!string.IsNullOrEmpty(_autoSpike))
                LoggerInstance.Msg($"AutoSpike '{_autoSpike}' armed: runs {_autoDelay}s after the hub loads, then dumps the scene and quits");

            Debug.Dumper.Init();
            Packs.PackRegistry.Discover();
        }

        private void ApplyCommandLine()
        {
            foreach (var raw in Environment.GetCommandLineArgs())
            {
                var arg = raw.Trim();
                if (!arg.StartsWith("--pws2ll-", StringComparison.OrdinalIgnoreCase)) continue;
                var eq = arg.IndexOf('=');
                var key = (eq < 0 ? arg : arg.Substring(0, eq)).ToLowerInvariant();
                var val = eq < 0 ? "" : arg.Substring(eq + 1).Trim('"');
                switch (key)
                {
                    case "--pws2ll-autodump": _autoDump = true; break;
                    case "--pws2ll-delay": if (float.TryParse(val, out var d)) _autoDelay = d; break;
                    case "--pws2ll-cheat": _autoCheat = val; _autoDump = true; break;
                    case "--pws2ll-quit": if (int.TryParse(val, out var q)) _autoQuitAfter = q; break;
                    case "--pws2ll-timeout": if (float.TryParse(val, out var t)) _autoTimeout = t; break;
                    case "--pws2ll-spike": _autoSpike = val; break;
                    default: LoggerInstance.Warning($"Unknown switch {arg}"); break;
                }
            }
        }

        private int _hubLoads;

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            LoggerInstance.Msg($"Scene loaded: {sceneName} (build index {buildIndex})");
            if (sceneName == HubScene && ++_hubLoads == 6 && (_autoDump || !string.IsNullOrEmpty(_autoSpike)))
            {
                LoggerInstance.Error("Hub reload loop detected (6 loads); dumping and quitting so the game does not spin forever.");
                try { Debug.Dumper.RunAll(); } catch (Exception ex) { LoggerInstance.Error($"dump failed: {ex}"); }
                RequestQuit();
            }
            if (sceneName == "BootScene" && RegisterPacks.Value)
            {
                try { Packs.PackRegistry.BeginCatalogLoads(); }
                catch (Exception ex) { LoggerInstance.Error($"BeginCatalogLoads failed: {ex}"); }
            }
            try { Packs.PackRegistry.LogPrefabLoaders(sceneName); }
            catch (Exception ex) { LoggerInstance.Error($"LogPrefabLoaders failed: {ex}"); }
            try { Packs.SceneAugmenter.Apply(sceneName); }
            catch (Exception ex) { LoggerInstance.Error($"SceneAugmenter.Apply failed: {ex}"); }
            try { Packs.PackRegistry.PrepareScene(sceneName); }
            catch (Exception ex) { LoggerInstance.Error($"PrepareScene failed: {ex}"); }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (sceneName == HubScene && !RegisterPacks.Value)
            {
                // diagnostics mode: catalogs only now, so a bad pack can never stall boot
                try { Packs.PackRegistry.BeginCatalogLoads(); }
                catch (Exception ex) { LoggerInstance.Error($"BeginCatalogLoads failed: {ex}"); }
            }
            if (sceneName == HubScene && !Packs.PackRegistry.StringsRegistered)
            {
                try { Packs.PackRegistry.RegisterStrings(); }
                catch (Exception ex) { LoggerInstance.Error($"RegisterStrings failed: {ex}"); }
                Packs.PackRegistry.LogLoadedJobs();
            }
            if (!string.IsNullOrEmpty(_autoSpike) && sceneName == HubScene && !_spikeStarted)
            {
                _spikeDueAt = Time.realtimeSinceStartup + _autoDelay;
                LoggerInstance.Msg($"AutoSpike scheduled in {_autoDelay}s");
            }
            if (!_autoDump || string.IsNullOrEmpty(sceneName) || !sceneName.StartsWith("GAM_", StringComparison.Ordinal)) return;
            _pendingScene = sceneName;
            _dumpDueAt = Time.realtimeSinceStartup + _autoDelay;
            LoggerInstance.Msg($"AutoDump scheduled for '{sceneName}' in {_autoDelay}s");
            var augmented = Packs.SceneAugmenter.AppliedScene == sceneName ? Packs.SceneAugmenter.AppliedJob : null;
            var packScene = Packs.PackRegistry.Packs.Any(p => p.Jobs.Any(j => j.SceneName == sceneName && !j.AugmentsScene));
            if (AutoResetPackTargets.Value && (packScene || augmented != null))
            {
                _resetScene = sceneName;
                _resetRoots = augmented?.SceneAdditions.Select(a => a.Name).ToList();
                _resetDueAt = Time.realtimeSinceStartup + Math.Min(4f, _autoDelay / 2f);
            }
        }

        private float _quitRequestedAt = -1f;
        private float _quitDueAt = -1f;
        private float _resetDueAt = -1f;
        private string _resetScene;
        private List<string> _resetRoots;

        /// <summary>Application.Quit can be vetoed by the game (OnAppWantsToQuit); kill the process if it is still running 10 s later.</summary>
        private void RequestQuit()
        {
            if (_quitRequestedAt >= 0f) return;
            _quitRequestedAt = Time.realtimeSinceStartup;
            Application.Quit();
        }

        public override void OnUpdate()
        {
            try { Debug.StateTracer.Update(); } catch (Exception ex) { LoggerInstance.Error($"StateTracer failed: {ex}"); }
            if (_quitDueAt >= 0f && Time.realtimeSinceStartup >= _quitDueAt) { _quitDueAt = -1f; RequestQuit(); }
            if (_resetDueAt >= 0f && Time.realtimeSinceStartup >= _resetDueAt)
            {
                _resetDueAt = -1f;
                try { Debug.WashReset.ResetPackTargets(_resetScene, _resetRoots); } catch (Exception ex) { LoggerInstance.Error($"WashReset failed: {ex}"); }
            }
            if (_quitRequestedAt >= 0f && Time.realtimeSinceStartup - _quitRequestedAt > 10f)
            {
                LoggerInstance.Warning("Quit request ignored for 10 s; killing the process.");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            try { Packs.PackRegistry.PollCatalogs(); } catch (Exception ex) { LoggerInstance.Error($"PollCatalogs failed: {ex}"); }
            try { Packs.SceneAugmenter.PreloadPoll(); Packs.SceneAugmenter.Update(); } catch (Exception ex) { LoggerInstance.Error($"SceneAugmenter failed: {ex}"); }
            try { Patches.CampaignInjection.TryInjectAll(); } catch (Exception ex) { LoggerInstance.Error($"TryInjectAll failed: {ex}"); }
            if ((_autoDump || !string.IsNullOrEmpty(_autoSpike)) && _dumpsDone == 0)
            {
                if (_autoTimeoutAt < 0f) _autoTimeoutAt = Time.realtimeSinceStartup + _autoTimeout;
                else if (_dumpDueAt < 0f && Time.realtimeSinceStartup > _autoTimeoutAt)
                {
                    LoggerInstance.Warning($"AutoDump timeout ({_autoTimeout}s) with no GAM_* scene; dumping what is loaded.");
                    _pendingScene = "(timeout)";
                    _dumpDueAt = Time.realtimeSinceStartup;
                    _autoQuitAfter = 1; // a hung boot must not leave the game running
                }
            }

            if (EnableDebugKeys.Value)
            {
                try { Debug.Dumper.Update(); }
                catch (Exception ex) { LoggerInstance.Error($"Dumper failed: {ex}"); }
            }
            try { Debug.Dumper.PollSprites(); } catch (Exception ex) { LoggerInstance.Error($"PollSprites failed: {ex}"); }

            try { Debug.SpikeRunner.Update(); }
            catch (Exception ex) { LoggerInstance.Error($"SpikeRunner failed: {ex}"); }

            if (_spikeDueAt >= 0f && Time.realtimeSinceStartup >= _spikeDueAt)
            {
                _spikeDueAt = -1f; _spikeStarted = true;
                _spikeQueue = new Queue<string>(_autoSpike.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            }
            if (_spikeStarted && !_spikeDumped && !Debug.SpikeRunner.Running)
            {
                if (_spikeQueue != null && _spikeQueue.Count > 0)
                {
                    var which = _spikeQueue.Dequeue();
                    LoggerInstance.Msg($"AutoSpike: starting '{which}'");
                    if (which.Equals("A", StringComparison.OrdinalIgnoreCase)) Debug.SpikeRunner.StartSpikeA();
                    else if (which.Equals("M", StringComparison.OrdinalIgnoreCase)) Debug.SpikeRunner.StartSpikeM();
                    else if (which.Equals("G", StringComparison.OrdinalIgnoreCase)) Debug.SpikeRunner.StartSpikeG();
                    else LoggerInstance.Warning($"Unknown AutoSpike '{which}'");
                }
                else
                {
                    _spikeDumped = true;
                    LoggerInstance.Msg($"AutoSpike finished: {(Debug.SpikeRunner.Passed ? "last spike PASS" : "last spike FAIL")}. Dumping scene, then quitting.");
                    try { Debug.Dumper.RunAll(); } catch (Exception ex) { LoggerInstance.Error($"dump failed: {ex}"); }
                    _quitDueAt = Time.realtimeSinceStartup + 5f;
                }
            }

            if (_dumpDueAt >= 0f && Time.realtimeSinceStartup >= _dumpDueAt)
            {
                _dumpDueAt = -1f;
                var scene = _pendingScene;
                try
                {
                    Debug.Dumper.RunAll();
                    _dumpsDone++;
                    LoggerInstance.Msg($"AutoDump #{_dumpsDone} done for '{scene}'");
                }
                catch (Exception ex) { LoggerInstance.Error($"AutoDump failed: {ex}"); }

                if (!_cheatFired && !string.IsNullOrEmpty(_autoCheat) && scene == HubScene)
                {
                    _cheatFired = true;
                    try
                    {
                        if (Debug.Dumper.TryRunCheat(_autoCheat)) LoggerInstance.Msg($"Invoked cheat '{_autoCheat}'");
                        else LoggerInstance.Warning($"Cheat '{_autoCheat}' not found (see the cheat list in the gamedata dump)");
                    }
                    catch (Exception ex) { LoggerInstance.Error($"Cheat invocation failed: {ex}"); }
                    return; // do not quit yet: wait for the job scene dump
                }
                if (_cheatFired && !_cheat2Fired && !string.IsNullOrEmpty(_autoCheat2) && scene != HubScene && scene != "(timeout)")
                {
                    // second cheat inside the job (e.g. "Complete All Tasks - Instant"), then one more dump to see its effect
                    _cheat2Fired = true;
                    try
                    {
                        if (Debug.Dumper.TryRunCheat(_autoCheat2)) LoggerInstance.Msg($"Invoked cheat 2 '{_autoCheat2}'; dumping again in {_autoDelay2}s");
                        else LoggerInstance.Warning($"Cheat 2 '{_autoCheat2}' not found (see the cheat list in the gamedata dump)");
                    }
                    catch (Exception ex) { LoggerInstance.Error($"Cheat 2 invocation failed: {ex}"); }
                    _pendingScene = scene;
                    _dumpDueAt = Time.realtimeSinceStartup + _autoDelay2;
                    return;
                }
                if (_cheat2Fired && scene != HubScene) Packs.PackRegistry.LogLoadedJobs(); // job status after cheat 2

                if (_autoQuitAfter > 0 && _dumpsDone >= _autoQuitAfter)
                {
                    LoggerInstance.Msg("AutoDump complete, quitting.");
                    _quitDueAt = Time.realtimeSinceStartup + 5f; // let the screenshot and the async sprite dump finish
                }
            }
        }
    }
}
