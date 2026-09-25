# CLAUDE.md — OKCRing (custom level mod)

This file provides guidance to Claude Code (claude.ai/code) when working in `OKCRing/`. Repo-wide rules
(game environment, MelonLoader/IL2CPP rules, tool gotchas) are in the root `../CLAUDE.md`.

**All paths and commands below are relative to `OKCRing/`** (`cd OKCRing` first). Shared game docs live
one level up in `../docs/`.

## Read first

`docs/HANDOFF.md` is the live state of the project (what phase we are in, what is installed in the
game folder, what the next actions are) and it lists the hard-won rules in full. Read it before
touching anything. The other sources of truth:

- `../docs/phase0-findings.md` — how PWS2 v1.3.0 defines a level: `UniqueId` hashing, content-pack
  registration, `JobPackManifest`/`GameJob`/`SceneWashableData`/`WashTarget` layout, control-map/UV2
  rules, localization keys. Verified in-game.
- `docs/phase1-spikes.md` — spike log: pack-build rules, the hub-reload-loop root cause, the
  `GameplayBoot` requirement. Read the Spike C section before authoring a scene.
- The approved plan: `C:\Users\jogal\.claude\plans\powerwash-simulator-2-level-reflective-treehouse.md`.

## What this mod is

A MelonLoader mod that adds a custom Free Play/career level to PowerWash Simulator 2 (Steam appid
2968420, v1.3.1, Unity 6000.0.73f1, IL2CPP). It is built the way the game's own DLC is built:

```
Unity project (PWS2LevelKit)  --PackBuilder-->  Addressables content pack (catalog.bin + bundles + pack.json)
                                                        |
                              <game>/Mods/PWS2LevelLoader/packs/<Name>/
                                                        |
MelonLoader DLL (PWS2LevelLoader) loads the catalog and inserts a CampaignJobPackReference into
the DEFAULT campaign, so the game's own load path runs the level.
```

Three pieces, each with its own toolchain:

| Folder | Toolchain | Role |
| --- | --- | --- |
| `OKC_TransformerScreen/` | Blender 5.1, Python | Procedurally generates the prop mesh + dirt-mask UVs; exports FBX/GLB |
| `PWS2LevelKit/` | Unity 6000.0.73f1, URP, Addressables 2.5.0 | Authors the scene/ScriptableObjects against **stub** game types and builds the pack |
| `PWS2LevelLoader/` | .NET 8 SDK, net6.0 target | MelonLoader mod: pack discovery, catalog load, campaign injection, scene fixups, diagnostics |

## Commands

Build the loader (post-build copies the DLL into `<GameDir>/Mods`):

```bash
dotnet build PWS2LevelLoader -c Release
```

`-p:GameDir="D:\Games\PowerWash Simulator 2"` for a different install, `-p:CopyToGame=false` to skip
the copy. `PWS2LevelLoader/Directory.Build.props` derives `MLDir`/`Il2CppDir` from `GameDir`; the
csproj references the generated IL2CPP proxies in `<GameDir>/MelonLoader/Il2CppAssemblies`, so the
game with MelonLoader 0.7.x installed **is** the SDK.

Build one content pack headlessly (~20–40 s; output to `dist/Mods/PWS2LevelLoader/packs/<name>/`):

```bash
"C:\Program Files\Unity\Hub\Editor\6000.0.73f1\Editor\Unity.exe" -batchmode -quit -nographics -projectPath PWS2LevelKit -executeMethod PWS2LevelKit.Editor.PackBuilder.BuildFromCommandLine -pack SpikeC
```

In the Editor the same thing is `PWS2 > Build pack from selected folder`, and the spike authoring
tools are under `PWS2 > Spikes > …` (each also has a `…FromCommandLine` entry point).

Regenerate the prop, then verify (`VERIFY: PASS` is the gate):

```bash
"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b --python OKC_TransformerScreen/build_okc_screen.py
```

`PackBuilder.Build` first runs `PackValidator` (also `PWS2 > Validate stubs`, or
`-executeMethod PWS2LevelKit.Editor.PackValidator.ValidateFromCommandLine`), which checks every stub's serialized
field names against `docs/tools/stub-fields.json`. After a game update, re-export that file in-process with
`& docs/tools/export_stub_fields.ps1 -GameVersion <ver>` and commit it.

Dump IL2CPP type layouts with the shared `../docs/tools/dump_types.ps1` (see root `CLAUDE.md`).

## Testing = running the game

There is no unit-test suite. A change is verified by launching the game with the loader's automation
preferences set, letting it dump, and reading the dumps and `MelonLoader/Latest.log`. Preferences
live in `<game>/UserData/MelonPreferences.cfg` under `[PWS2LevelLoader]`
(`AutoDump`, `AutoRunCheat`, `AutoRunCheat2`, `AutoQuitAfterDumps`, `AutoSpike`, `RegisterPacks`,
`EnableDebugKeys`); the README documents each one. Launch the game as the root `CLAUDE.md` describes. Dumps land in `<game>/UserData/PWS2LevelLoader/dumps/`; interactive keys are
F5–F10 (see README).

Always restore `AutoDump=false`, `AutoRunCheat*=""`, `AutoSpike=""`, `AutoQuitAfterDumps=0` before
handing the game back for normal play.

## Architecture notes that span files

- **Stub types are the contract.** `PWS2LevelKit/Assets/Stubs/<Assembly>/` has one asmdef per game
  assembly with classes mirroring the game's serialized layout. Unity serializes against the stub;
  IL2CPP deserializes onto the real type at runtime. Field names and serialization form must match
  exactly — a proxy member with no `_X_k__BackingField` twin is a plain field, so declare it
  `[SerializeField] public`, not `[field: SerializeField]`, or the value silently arrives empty.
- **`pack.json` is the loader/kit interface.** The kit writes it next to the catalog; the loader
  parses it into `src/Packs/PackManifest.cs`. It carries the job pack manifest GUID, per-job
  `uniqueName`/`uniqueId`/`sceneName`/thumbnails, and the localization strings the loader registers.
  `uniqueId` must be the .NET Framework `string.GetHashCode()` of `uniqueName` (algorithm in
  phase0-findings §1, implemented in `Pws2Hash.cs` — never call Mono's `GetHashCode`).
- **Two fixups live in the loader, not the pack**: `WashTarget.m_renderSettings` and the
  `PWS/Wash Target Uber` material swap, both in `PackRegistry.PrepareScene`.
- **Every level scene needs a `GameplayBoot` hierarchy** (two `PrefabLoader`s with the game's level
  system prefab GUIDs), built by `Assets/LevelKit/Editor/GameplayBoot.cs`, or `SpawnPlayers` throws.
  `PackRegistry.LogPrefabLoaders` re-captures those GUIDs after a game update.
- **A pack job can run on a game scene** (`OkcStreetcarPack`, `docs/streetcar-variant.md`): its `pack.json` job lists
  `sceneAdditions` / `hideWashRoots` / `hideObjects` / `hideContainedIn`, and `Packs/SceneAugmenter` applies them in
  `OnSceneWasLoaded` only when `LoadingLevelState.Job` is that job. The game flattens a level's hierarchy after load, so
  match roots at load time or by `WashTarget.m_gameJobWashTargetRoot`; `PLT_*` roots are the level's lighting (sun
  included), never a vehicle's.
- **Pack references must keep `Campaign.m_loadedContentManifests` keys contiguous** — insert before
  the first unavailable reference (`CampaignInjection.WillLoad`), or every level load throws
  `KeyNotFoundException` and the hub reloads forever.

## Rules that cost a session each

The game-wide ones (Harmony on async IL2CPP methods, `WaitForCompletion()`, `hide_console`,
`FuturLogger.Error`, interop interface casts, tool gotchas) are in the root `CLAUDE.md`. Specific to this mod:

- To see why a level load failed, read `LoadingStateBase.Task.Exception` (`Debug/StateTracer`) instead of patching.
- **Do not patch the `Campaign.JobPackReferences` getter** — on 1.3.1 it crashes every hub load inside `coreclr.dll`.
  After a game update, a boot crash in `coreclr.dll` means: bisect the Harmony patches first. The whole update checklist
  (proxies, stub fields, `GameplayBoot` GUIDs, `PackBuilder.GameVersion`, rebuild every pack) is HANDOFF "Game updates".
- Per-pack bundle naming (`MonoScriptBundleNaming`/`BuiltInBundleNaming = Custom`,
  `pws2pack_<name>`) plus `BundleNamingStyle.AppendHash`; Addressables only *logs* duplicate bundle
  file names and leaves `result.Error` empty, so `PackBuilder` fails the build itself.
- `CampaignJobPackReference` has no IL2CPP constructor — allocate via `PackRegistry.NewIl2CppObject`.
- Do not add scripts under `PWS2LevelKit/Assets` while a batch build is running (domain reload).
- `OkcPack.CreateAndBuildFromCommandLine` must run **without** `-nographics` (reflection-probe bake and
  thumbnail renders need the GPU). The game build has no built-in skybox shaders, so a level's sky must be
  geometry with a URP shader (the OKC sky dome), never a `Skybox/*` material.
- To see a pack level dirty when the save already has it clean, set `AutoResetPackTargets=true` for the run
  (session-only `WashManager.ResetTarget`), and keep `AutoDumpDelaySeconds` near 7: the game opens the Job
  Summary a few seconds after a reset.
