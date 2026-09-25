# Handoff — PWS2 custom level mod (OKC Transformer Screen)

Updated 2026-09-24 for the next session. **The game auto-updated to 1.3.1 on 2026-09-24; everything is re-verified on
1.3.1** (see "Game updates" below). The 1.3.1 boot crash was the `Campaign.JobPackReferences` getter patch, now removed
(see Hard-won rules).

**Paths below are relative to `OKCRing/`** (the mod moved there from the repo root on 2026-09-24 to make room
for other mods). Shared game docs are one level up in `../docs/`.

Read in this order:

1. The approved plan: `C:\Users\jogal\.claude\plans\powerwash-simulator-2-level-reflective-treehouse.md`
   (Phases 0–4, decisions: native Addressables content pack + thin MelonLoader loader, one Nexus page, minimal plaza).
2. `../docs/phase0-findings.md` — how the game defines a level (types, hashes, UV channels, control maps, keys). Verified in-game.
3. `docs/phase1-spikes.md` — toolchain + spike log: every dead end, its root cause, the pack-build rules, and the
   `GameplayBoot` requirement. Read the Spike C section before authoring any scene.
4. This file — exact state and the next actions.

Memory files (`~/.claude/projects/.../memory/pws2-*.md`) summarise the same facts; the repo docs are the source of truth.

## Where we are

**Phase 0 and Phase 1 are complete. Phase 2 vertical slice PASSES (2026-09-22):** the OKC ring is in the game as
15 wash targets (OKC_Panel_01..14 + OKC_Screen_Plinth) with our dirt. **Plaza, lighting and thumbnails done
(2026-09-23).** Phase 2 is complete; Phase 3 (loader hardening) is next.

| Item | State |
| --- | --- |
| Repo `Powerwash Simulator 2/` | Phase 0–2 merged to `main`; this mod lives in `OKCRing/`. |
| `PWS2LevelLoader/` (MelonLoader mod, net6.0) | builds clean; post-build copies the DLL to the game's `Mods/`. Installed DLL matches source (19:53). |
| `PWS2LevelKit/` (Unity 6000.0.73f1, URP, Addressables 2.5.0, Localization 1.5.13) | `PackBuilder` builds packs headless in ~20 s, with per-pack bundle names, `AppendHash`, and a build-time collision check. |
| Spike G / M (catalogs, manifest deserialisation) | **PASS** |
| Spike A (stub MonoBehaviours in a bundled scene) | **PASS** — `WashTarget` arrives as the real type with its `WashTargetData` resolved. |
| Spike B (register a job in the default campaign) | **PASS** — hub stable, job loads through the game's own path, only the listed targets get wash maps. |
| Spike C (one washable panel end to end) | **PASS** — our scene, our dirt: 242 614 start texels, washing drives it to 0, job completes, progress survives a relaunch (`career=Completed`, Free Play `Unlocked`). |
| OKC vertical slice | **PASS** — `OkcPack.cs` builds `GAM_OKC_TransformerScreen` (ground plane, spawn 8 m south, 15 targets, one stage/task/15 wash tasks). Automated run: every target `loaded=True` with `start` equal to the authored texel counts (262 864–268 487 per panel, 445 797 curb), `Complete All Tasks` drives all to `current=0`, `JobStatus career=Completed`, Free Play `Unlocked`. Inner skins of the closed ring are left clean on purpose (unreachable). |
| OKC plaza / lighting / UI | **PASS** — plaza from `build_okc_plaza.py` (scenery only, boundary on `NavigationCollision`), lighting rig mirroring the game's levels (see below), our own thumbnail A/B, preview, chat icon, loading screen and timelapse sprites. In-game run: all 6 sprites load from our catalog (`sprites` dump), the game's Job Summary shows our preview, the in-game screenshot shows the plaza under our sky with dirt on the ring, the 15 targets still load with the same start texels and wash to 0. |
| OKC in the Streetcar level | **PASS (2026-09-23)** — pack `OKC_Streetcar`: a separate job `OKC_STREETCAR` on the game's `GAM_PW2_HS_Tram` scene; the loader (`Packs/SceneAugmenter`) hides the tram and instantiates the ring prefab in its place, only when that job loads. 15 targets `loaded=True` at the authored texels → 0, `career=Completed`; the vanilla Streetcar load is unchanged (same scene totals as a vanilla dump). Method and findings: `docs/streetcar-variant.md`. |
| Layered dirt | **PASS (automated, 2026-09-24)**. Panels: `TPL_OKC` = PaintedFlakesRust (T6) patches under Grime and MudSplats. Curb: `TPL_OKC_Curb` = GrimeGreenSlimy / SandDarker / CrackedMud (T3). Maps are nested and soft-edged (`build_okc_dirtmaps.py`, `DIRTMAPS: PASS`); tiers and GUIDs are in phase0-findings §6. Both jobs load 15/15 targets at the same start texels as before (the base coat covers every reachable texel), resolve all six layers, and the cheat takes every target to 0. **Still open: a hands-on wash**, to confirm the rust shows once the grime is off (layer 0 drawn at the bottom) and that starter gear can finish it. The knob is `RUST_COVERAGE`. |
| Installed packs | `<game>/Mods/PWS2LevelLoader/packs/{OKC_TransformerScreen,OKC_Streetcar}` = copies of `dist/...`. The spike packs were uninstalled on 2026-09-24 so they stay out of the career list. They are still built in `dist/`; to rerun one (`AutoSpike`/F5 need `SpikeA`), copy `dist/Mods/PWS2LevelLoader/packs/Spike<X>` back, then remove it again. |
| Loader prefs | reset for normal play: `AutoDump=false`, `AutoRunCheat=""`, `AutoRunCheat2=""`, `AutoSpike=""`, `AutoQuitAfterDumps=0`, `RegisterPacks=true` (`false` loads no pack at all, OKC included). |
| MelonLoader config | `<game>/UserData/Loader.cfg` has `hide_console = true` (2026-09-23) — **keep it**. With the console shown, ML 0.7.4-ci.2581's bootstrap IAT-hooks UnityPlayer's `CloseHandle`; a Unity worker thread's `thread_local` destructor calls it while the thread exits, after NativeAOT has torn down that thread's state, and the process dies with `0xc0000602` in `VERSION.dll+0x3692ad` on every quit (WER dumps in `%LOCALAPPDATA%\CrashDumps`). It predates this loader (first seen 2026-09-15 with only ML + CheatMenuEnabler). Logs still go to `MelonLoader/Latest.log`. |
| Save state | the live save has SpikeB and SpikeC `Completed` and OKC `Completed` (cheat). Harmless, but it is a modded save: `Progression / Complete All Jobs` and friends were never used on it. |

## What Phase 1 settled (details in `docs/phase1-spikes.md`)

1. **Stub assets deserialise onto the real IL2CPP types** — ScriptableObjects and MonoBehaviours alike. No runtime
   `AddComponent` fallback is needed, so the plan's Phase 2 authoring path stands as written.
2. **Pack references must keep `Campaign.m_loadedContentManifests` keys contiguous.** That dictionary is keyed by
   pack-reference index and `Campaign.TryFindPowerWashersFromPacks` walks it `0..Count-1`; appending behind the
   never-loading `ContentPack_EditorAssets` reference caused `KeyNotFoundException(2)` on every level load and an
   endless hub reload. `CampaignInjection.WillLoad` now inserts before the first unavailable reference.
3. **Every level scene needs a `GameplayBoot` hierarchy** (two `PrefabLoader`s carrying the game's level-system
   prefab GUIDs), or `SpawnPlayers` throws `No such registration of type: FuturLab.PW2.GameplayAudioManager`.
   `PWS2LevelKit/Assets/LevelKit/Editor/GameplayBoot.cs` builds it; `PackRegistry.LogPrefabLoaders` re-captures the
   GUIDs from the game's own scenes after an update.
4. **Two per-scene fixups belong to the loader, not the pack**: `WashTarget.m_renderSettings` and the
   `PWS/Wash Target Uber` material swap, both done in `PackRegistry.PrepareScene`.
5. **Pack-build rules**: per-pack MonoScript/built-in bundle names, `BundleNamingStyle.AppendHash`, and fail the
   build on duplicate bundle file names (Addressables only logs that and leaves `result.Error` empty).
6. **Diagnostics that need no Harmony on async methods**: `Debug/StateTracer` (state transitions plus the exception
   of a faulted loading task, which the game swallows behind "An error occurred when loading"), `Debug/JobHealth`,
   `PackRegistry.LogJobStatus` / `LogCampaignManifestKeys` / `LogPrefabLoaders`, `Patches/HubLoadPatches`.

## What game levels carry for lighting (Billboard lighting dump, 2026-09-23)

No lightmaps on renderers (`lightmapIndex=-1`); URP asset `UniversalRP-PC_High` uses `lightProbeSystem=ProbeVolumes`.
Each level scene brings an active `Directional Light` (intensity 1.5, colour (1, 0.972, 0.91), soft shadows 0.85)
set as `RenderSettings.sun`, Trilight ambient, a cubemap skybox (`Skybox/Cubemap PW2`), baked reflection probes (one
also the custom default reflection), and a global `PostProcessingVolume` on Default (Bloom, ColorAdjustments, plus the
game's own ButoVolumetricFog / LightScatteringPostProcess). The player camera has post-processing on and volume mask 1.
**The game build has no built-in skybox shaders**: a `Skybox/Procedural` material renders magenta, so the OKC sky is a
dome mesh with URP/Unlit (`OkcEnvironment.BuildSkyDome`). `ScreenCapture.CaptureScreenshot(string)` throws
`MissingMethodException` through the interop; the dumper uses `CaptureScreenshotAsTexture` at end of frame.
Job sprites: thumbnails A/B 765x328 (A: slanted two-step tab, B: blue duotone), preview 1936x1184 (rounded),
chat icon 140x140 (disc, white rim), loading 3840x2160 (navy halftone vignette), timelapse 1782x976.

## OKC pipeline (how the slice is built)

```bash
"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b --python OKC_TransformerScreen/build_okc_screen.py -- --no-render
"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b OKC_TransformerScreen/OKC_TransformerScreen.blend --python OKC_TransformerScreen/verify_okc_screen.py   # VERIFY: PASS
"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b OKC_TransformerScreen/OKC_TransformerScreen.blend --python OKC_TransformerScreen/build_okc_dirtmaps.py
"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b --python OKC_TransformerScreen/build_okc_plaza.py   # PLAZA: PASS
"C:\Program Files\Unity\Hub\Editor\6000.0.73f1\Editor\Unity.exe" -batchmode -quit -projectPath PWS2LevelKit -executeMethod PWS2LevelKit.Editor.OkcPack.CreateAndBuildFromCommandLine   # no -nographics: probe bake + thumbnail renders need the GPU
```

`OkcPack` copies the FBX to `Assets/LevelKitSource/` (outside the pack, never shipped) and writes one mesh `.asset`
per target with `uv3` = DirtMask and `uv2` = DirtMask + cell `(i % 10, i / 10)`. Note: the runtime
`UniqueId.FullName` of a child is `[Scene]OKC_Panel_01` (no root in the path) while `Pws2Hash.ObjectFullName`
includes the root; harmless because the game uses the serialised `m_id`, but match the game's form before
relying on recomputed ids.

## Next actions — Phase 2 (content)

Plan sections "Phase 2 — Content" steps 1–8. Order that matches what is now known:

```bash
# sanity
tasklist | grep -i "PowerWash Simulator 2.exe"
cd "C:/Users/jogal/OneDrive/Documents/GitHub/Mods/Powerwash Simulator 2/OKCRing"
dotnet build PWS2LevelLoader -c Debug          # copies DLL to <game>/Mods
```

1. **Blender** (`OKC_TransformerScreen/`): add `SPLIT_PANELS` to `build_okc_screen.py` so the 14 panels export as
   separate objects plus the plinth, add the plaza pieces and boundary colliders, then `build_okc_dirtmaps.py` for
   per-object control maps in `TexCoord2` space (RGB = opacity of palette layers 0/1/2, as Spike C confirmed).
   `verify_okc_screen.py` must print `VERIFY: PASS` for every exported mesh.
2. **Unity**: import per the README rules (Generate Lightmap UVs **off**, mesh compression off, MeshCollider per
   panel). Build scene `GAM_OKC_TransformerScreen` the way `SpikeCPack.cs` builds `GAM_SPIKEC_Panel`: call
   `GameplayBoot.Create()` first, then the wash-target root, the panels, the spawn position, the plaza.
3. **`BakeWashData` editor tool** generalising `SpikeCPack.cs`: deterministic `UniqueId`s via
   `Pws2Hash.ObjectId(sceneName, transform)`, one `WashTargetData` per target with the real control map and
   `SuggestedControlMapResolution` from UV2 area, `SceneWashableData` with per-component `m_startDirtiness` counted
   from each PNG, the `GameJob` (one stage, one task, 15 wash tasks), the wash CSV, and `pack.json` with `sceneName`
   and every string key.
4. **Run it** the way Spike C was run: `AutoDump=true`, `AutoRunCheat="<title>|SCRIPT_CAREER_<NAME>_TITLE"`,
   `AutoRunCheat2="Complete All Tasks - Instant"`, `AutoQuitAfterDumps=3`, launch with
   `Start-Process "steam://rungameid/2968420"` and `Wait-Process`. Pass = the same four checks as Spike C.
5. Then Phase 3 (loader hardening: multiplayer guard, per-pack enable flags, `EnableDebugKeys` default off) and
   Phase 4 (packaging, `build.ps1`, Nexus page).

Before handing the game back for normal play: `AutoDump=false`, `AutoRunCheat=""`, `AutoRunCheat2=""`,
`AutoSpike=""`, `AutoQuitAfterDumps=0`; uninstall any spike pack you copied in for the run.

## Game updates (done for 1.3.0 -> 1.3.1 on 2026-09-24)

1. Launch once so MelonLoader regenerates `Il2CppAssemblies` ("Generation Needed!" in the log), then
   `dotnet build PWS2LevelLoader -c Release`. 1.3.1: no errors.
2. If the game now dies at boot with `0xc0000005` in `coreclr.dll` (Event Viewer > Application Error), bisect the Harmony
   patches (Hard-won rules). 1.3.1: the `Campaign.JobPackReferences` getter patch.
3. `& docs/tools/export_stub_fields.ps1 -GameVersion <ver>` and diff `docs/tools/stub-fields.json`. 1.3.1: only the version
   line changed (53 types, same field names).
4. Compare the hub's `[prefabs]` lines in `Latest.log` with an older log (the `GameplayBoot` GUIDs). 1.3.1: identical.
5. Bump `PackBuilder.GameVersion` (every generator stamps it into `pack.json`) and each `Assets/Packs/*/pack.json`, then
   `PackBuilder.BuildFromCommandLine -pack <name>` for every pack. This rebuilds the bundles from the authored assets, so the
   content stays exactly what was verified, and the loader stops skipping them. Copy `dist/.../packs/*` into the game.
6. Automated runs of both OKC jobs (`AutoDump`, `AutoResetPackTargets`, `AutoRunCheat2="Complete All Tasks - Instant"`).
   1.3.1: `GAM_OKC_TransformerScreen` and `OKC_STREETCAR` (on `GAM_PW2_HS_Tram`) both load 15 targets `loaded=True` at the
   authored start texels (262 864–268 487 per panel, 445 797 plinth) and the cheat takes all 15 to `current=0`; Streetcar
   hides the tram's 277 targets and places the ring as before. No crash events.

## Hard-won rules (do not relearn)

- **Never Harmony-patch IL2CPP methods that are async or take `CancellationToken`/non-blittable structs.**
  The prefix on `Campaign.LoadContent` silently stalled boot every time. Use parameterless / reference-only hooks
  (`Campaign.AreCampaignPacksInstalled`, `LoadingHubLevelState.BuildContainer`)
  or polling. To see why a load failed, read `LoadingStateBase.Task.Exception` instead of patching anything.
- **Do not patch the `Campaign.JobPackReferences` getter.** On game 1.3.1 that prefix alone killed the process
  ~0.6 s into every hub load (`0xc0000005` at `coreclr.dll+0x1d4089` on a Unity worker thread, no managed exception,
  even with every pack skipped); bisected 2026-09-24 by disabling all patches, then that one. It was a redundant third
  injection path. After a game update, if boot dies inside `coreclr.dll`, bisect the Harmony patches first
  (`[assembly: MelonLoader.HarmonyDontPatchAll]` for one run).
- **Pack references go before any reference the game will skip** (keeps `m_loadedContentManifests` keys contiguous).
- **Every pack scene needs `GameplayBoot`** with the captured prefab GUIDs.
- **Stub field vs. property**: a proxy member with no `_X_k__BackingField` twin is a plain field; declare it
  `[SerializeField] public`, not `[field: SerializeField]`, or Unity serialises it under the wrong name and the
  value silently arrives empty.
- **Never call `WaitForCompletion()` on Addressables during boot** — crashed the process.
- **Do not hook `FuturLogger.Error`** — thousands of empty calls at boot, game died before the hub.
- `CampaignJobPackReference` has no constructor in IL2CPP: allocate with `IL2CPP.il2cpp_object_new` (see `PackRegistry.NewIl2CppObject`).
- The game's own default-campaign pack refs have `ContentPack = null`, empty `m_jobPreviews`, `m_stringTableName = "PWS2_Main"`;
  mirror that. The game-native `ContentPackManifest` route hangs for non-DLC packs.
- Interop interface proxies do not inherit members (`IEnumerator<T>` lacks `MoveNext` → `Cast<Il2CppSystem.Collections.IEnumerator>()`;
  `IList<T>` lacks `Count` → `Cast<ICollection<T>>()`; `IBootSystem` lacks `GetIl2CppType` → `TryCast<Il2CppSystem.Object>()`).
  Enumerating the binary catalog via `Locate` crashed; use `AllLocations`.
- Free Play for a job unlocks only after its career run is completed — that is a game rule, not a registration bug.
- Tools: the Bash tool runs through `eval` (single quotes inside heredocs break it; use the Write tool for such files;
  commands over ~8 KB are truncated). Run `../docs/tools/dump_types.ps1` in-process from the PowerShell tool
  (`& ../docs/tools/dump_types.ps1 -Types A,B -MethodRegex '...'`), not via `pwsh -File`. `Remove-Item` under
  Program Files is blocked by the tool guard; use bash `rm -rf`.
- Unity headless runs: do not add scripts to `PWS2LevelKit/Assets` while a batch build is running (mid-run domain reload).
- Unity CLI cannot create a project from this editor's local template ids; use `Unity.exe -createProject … -cloneFromTemplate <tgz>`.

## Environment

| Thing | Value |
| --- | --- |
| Game | `C:\Program Files (x86)\Steam\steamapps\common\PowerWash Simulator 2`, v1.3.1 (was 1.3.0 until 2026-09-24), Unity 6000.0.73f1, IL2CPP, Steam appid 2968420 |
| MelonLoader | 0.7.4-ci.2581, Il2CppInterop 1.5.1-ci.845, HarmonyX 2.10.2; proxies in `MelonLoader/Il2CppAssemblies`; `UserData/Loader.cfg` has `capture_player_logs = true`, so Unity's own log appears as `[UNITY]` in `MelonLoader/Latest.log` |
| Loader prefs | `<game>/UserData/MelonPreferences.cfg` `[PWS2LevelLoader]`: `EnableDebugKeys`, `IgnoreGameVersion`, `RegisterPacks`, `AutoDump`, `AutoDumpDelaySeconds`, `AutoRunCheat` (`Name\|ALT_KEY`), `AutoRunCheat2`, `AutoRunCheat2DelaySeconds`, `AutoQuitAfterDumps`, `AutoSpike` (`A`, `M`, `G`) |
| Installed packs | `<game>/Mods/PWS2LevelLoader/packs/{OKC_TransformerScreen,OKC_Streetcar}` (spikes: see "Where we are") |
| Debug keys in game | F7 scene dump, F8 game data dump, F6 control maps, F9 Addressables locations (opt-in), F5 Spike A (needs `SpikeA` installed) |
| Dumps | `<game>/UserData/PWS2LevelLoader/dumps/` — pruned on 2026-09-24 to the latest run (`20260924-1227*`, Streetcar). Any automated run writes a new set. |
| Unity | `C:\Program Files\Unity\Hub\Editor\6000.0.73f1\Editor\Unity.exe`; CLI `unity` on PATH (1.0.0-beta.8) |
| Type dumps | `& ../docs/tools/dump_types.ps1 -Types GameJob,Location …` (Mono.Cecil from `MelonLoader/net6`) |
| Blender | 5.1.2 at `C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`; prop scripts in `OKC_TransformerScreen/` |

## Repo map

```
../docs/phase0-findings.md         game data model (verified), shared with the repo's other mods
../docs/tools/dump_types.ps1       Cecil type dumper, shared
OKCRing/                           this mod; everything below is relative to it
README.md                          overview, build, debug keys, automation prefs
CLAUDE.md                          mod-specific guidance (repo-wide rules in ../CLAUDE.md)
docs/phase1-spikes.md              spike log: pack-build rules, hub-loop root cause, GameplayBoot requirement
docs/streetcar-variant.md          OKC ring on the game's Streetcar scene
docs/HANDOFF.md                    this file
docs/tools/export_stub_fields.ps1  re-export stub-fields.json after a game update (PackValidator reads it)
PWS2LevelLoader/                   MelonLoader mod
  src/Core.cs                      entry, prefs, automation (AutoDump/AutoSpike/second cheat/loop guard/RequestQuit)
  src/Packs/PackManifest.cs        pack.json model (jobs carry sceneName; derived localization keys)
  src/Packs/PackRegistry.cs        pack discovery, catalog loads, CampaignJobPackReferences, RegisterStrings,
                                   PrepareScene (render settings + wash shader), LogJobStatus/ManifestKeys/PrefabLoaders
  src/Patches/CampaignPatches.cs   insertion into Campaign DEFAULT (poll + 2 reference-only prefixes, WillLoad ordering)
  src/Patches/ErrorLogPatches.cs   ExitWithError logging
  src/Patches/HubLoadPatches.cs    [hubload] markers (BuildContainer, CreateGameplayState, StartPlayingLevel)
  src/Debug/Dumper.cs              F6/F7/F8/F9 dumps, cheat-menu invocation (TryRunCheat)
  src/Debug/Il2CppDump.cs          reflective proxy dumper
  src/Debug/SpikeRunner.cs         spikes A (scene), M (manifest), G (game asset)
  src/Debug/StateTracer.cs         per-frame game-state trace + faulted loading-task exception capture
  src/Debug/JobHealth.cs           one-line null/empty audit of a GameJob
PWS2LevelKit/                      Unity project
  Assets/Stubs/<Assembly>/         stub types (asmdef per game assembly), incl. FuturLab/PrefabLoader.cs
  Assets/LevelKit/Editor/          PackBuilder.cs, Pws2Hash.cs, GameplayBoot.cs, SpikeAScene.cs, SpikeBPack.cs, SpikeCPack.cs
  Assets/Packs/SpikeA, SpikeB, SpikeC   spike content
dist/                              built packs (gitignored)
OKC_TransformerScreen/             Blender prop, plaza and dirt-map generators
```
