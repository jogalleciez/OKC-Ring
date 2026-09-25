# Phase 1 — toolchain and feasibility spikes (log)

Plan reference: Phase 1 of the approved plan. Each spike has a pass criterion; results are recorded here as they land.

## Toolchain (done 2026-09-16)

- Unity **6000.0.73f1** installed with the Unity CLI (`unity install 6000.0.73f1 -y --accept-eula --no-cm`).
  The CLI's `projects create --template` does not accept this editor's local template ids; the project was
  created the way the Hub does it:
  `Unity.exe -batchmode -quit -nographics -createProject <path> -cloneFromTemplate <Editor>/Data/Resources/PackageManager/ProjectTemplates/com.unity.template.3d-cross-platform-17.0.12.tgz`
- `PWS2LevelKit/` = that URP project + `com.unity.addressables 2.5.0` + `com.unity.localization 1.5.13`
  (Input System 1.19 comes with the template).
- `Assets/Stubs/<AssemblyName>/` holds one asmdef per game assembly (`FuturLab`, `FuturLab.PW2`,
  `FuturLab.PW2.Core`, `FuturLab.PW2.GameContent`, `FuturLab.PW2.TexturePainter`, `FuturLab.PW2.WashGraphics`,
  `FMODUnity`) with stub classes mirroring the serialized layout from `../docs/phase0-findings.md`. All compile.
- `Assets/LevelKit/Editor/PackBuilder.cs` builds a pack: one Addressables group per `Assets/Packs/<name>`,
  profile `PWS2Pack`, LoadPath `{UnityEngine.AddressableAssets.Addressables.RuntimePath}/../../../Mods/PWS2LevelLoader/packs/<name>`,
  local binary catalog, unique bundle ids, LZ4, hash-suffixed bundle names, per-pack MonoScript / built-in bundle
  names (`pws2pack_<name>`). Output: `dist/Mods/PWS2LevelLoader/packs/<name>/`
  with `catalog.bin`, `catalog_<name>.bin`, bundles, `pack.json`. Headless:
  `Unity.exe -batchmode -quit -nographics -projectPath PWS2LevelKit -executeMethod PWS2LevelKit.Editor.PackBuilder.BuildFromCommandLine -pack <name>`
  (Addressables 2.x note: `ShaderBundleNaming` is gone; use `BuiltInBundleNaming`.)
- Headless pack builds take ~40 s once packages are imported.

## Loader-side plumbing learned the hard way

| Attempt | Result | Lesson |
| --- | --- | --- |
| `Addressables.LoadContentCatalogAsync(...).WaitForCompletion()` inside a Harmony prefix on `Campaign.LoadContent` (boot) | catalog reported loaded, then the game process died silently seconds later, before `EmptyScene` | never block Addressables during the game's boot pipeline (remote catalog download in flight) |
| Runtime `ContentPackManifest` (IsSeparateCatalog, absolute load path, ContentPack on our pack ref) so the game loads the catalog itself | game computed `RuntimeCatalogPath = <folder>/catalog_<ID>.bin` as predicted, but its catalog load never completed: `m_catalogLoaded=false`, default campaign stuck with 0 manifests, boot never reached the hub | the game's separate-catalog path is tied to DLC mounting; not usable for mod packs |
| Async `LoadContentCatalogAsync` started at `BootScene`, pack refs with `ContentPack = null` appended in the prefix only if the catalog is ready | catalogs ready in < 1 s (campaign loads ~2.5 s after BootScene), reference appended (3 → 4), but boot still never reached the hub: every campaign (incl. BBG/HUT/XCC) had 0 loaded manifests at the 150 s timeout, so the campaign pipeline stalls on our reference's `LoadContent()` and its exception is swallowed (the game's Addressables settings have `m_LogResourceManagerExceptions = false`) | the game's own pack refs also carry **empty** `m_jobPreviews`, `m_stringTableName = "PWS2_Main"`, `ContentPack = null`; ours now mirror that. Diagnosis continues with Spike M (load the manifest directly, dump it) and a `RegisterPacks=false` preference so diagnostics never stall boot |

Also: IL2CPP stripped `CampaignJobPackReference`'s parameterless constructor; allocate it with
`IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<T>.NativeClassPtr)` and the `(IntPtr)` proxy constructor.

**Root cause of every boot stall above (confirmed 2026-09-16 23:55):** the Harmony prefix on
`Campaign.LoadContent(DLCProviderManager, CancellationToken)`. It is an async method with a non-blittable struct
parameter; Il2CppInterop's Harmony support cannot pass such parameters through a patched method, so the game's own
call never completed. With that patch removed and everything else identical (`RegisterPacks=false`, catalogs loaded in
the hub), the hub loaded 14 s after boot as usual. Rule: **never Harmony-patch IL2CPP methods that take
`CancellationToken` or other non-blittable structs**; patch parameterless / reference-only methods instead
(now: `Campaign.AreCampaignPacksInstalled` plus a boot-time poll; a `Campaign.JobPackReferences` getter patch was
removed on 2026-09-24 because it crashes game 1.3.1).

| Spike | Result |
| --- | --- |
| G — game asset by GUID (`Thumbnail_PW2_Billboard_A`) with both pack catalogs loaded | **PASS** — our catalogs do not disturb the game's Addressables lookups |
| M — `AssetReferenceT<JobPackManifest>(guid).LoadAssetAsync()` on our pack | **PASS** — arrives as the real `FuturLab.PW2.JobPackManifest` with `Job_SpikeB` (`SPIKEB`, 821964646), Location scene `GAM_PW2_HW_Billboard`, scene GUID, `SWD_SpikeB` (3 components), customer, 1 stage, `unlockedByDefault=true`, title key intact. **Stub ScriptableObjects deserialise onto game types under IL2CPP.** |
| A — stub MonoBehaviours in a bundled scene | **PASS** (2026-09-17 19:35) — `SPIKE A PASS: washTargets=1 uniqueIds=2 roots=1 spawns=1 cubeId4242=True data64=True rootLink=True`; the `WashTarget` arrives as the real `FuturLab.PW2.TexturePainter.WashTarget` with `m_washTargetData = WTD_SpikeA_Cube` (res 64, area 64×64, threshold 0.1). Two pack-build bugs had to be fixed first (see below). |
| C — one washable panel end to end, all of it ours | **PASS** (2026-09-17 20:00) — our scene, mesh, control map, palette and job: dirt built from our PNG, washing completed the job, progress survived a relaunch. Needed a `GameplayBoot` in the scene (see below). |
| B — register a job in the default campaign | **PASS** (2026-09-17 19:23) — `REGISTERED`, hub stable, cheat button `SCRIPT_CAREER_SPIKEB_TITLE` (Scenes) loads `GAM_PW2_HW_Billboard` into `PlayingCareerState`, and exactly the 3 targets listed in `SWD_SpikeB` get wash maps (`loaded=True`, start dirtiness 481962 / 3448 / 54388 as authored) while the other 106 stay inactive. Root cause of the earlier hub reload loop below. |

## Pack-build rules (learned from Spike A, 2026-09-17)

| Symptom | Cause | Rule |
| --- | --- | --- |
| `The AssetBundle '…pws2levelkit_monoscripts_….bundle' can't be loaded because another AssetBundle with the same files is already loaded` → scene load fails with `Invalid path in AssetBundleProvider` | both packs named their MonoScript / built-in bundles `pws2levelkit`; Unity derives the archive-internal id from the bundle name, so the second pack's bundle is refused once the first is loaded | `MonoScriptBundleNaming` / `BuiltInBundleNaming = Custom` with a per-pack name (`PackBuilder.SharedBundleName` → `pws2pack_<name>`) |
| scene loads, but `WashTarget.m_washTargetData` is null; `WTD_SpikeA_Cube` never appears among loaded `WashTargetData` | a group with a scene **and** assets yields two bundles; with `BundleNamingStyle.NoHash` both were written as `pack_spikea_.bundle` and one overwrote the other. Addressables only logs `Unable to find registered file for bundle …` and `result.Error` stays empty | `BundleNamingStyle.AppendHash`; `PackBuilder` now fails the build when two bundles map to one file name |

## Hub reload loop after registration — root cause (2026-09-17)

Symptom: `REGISTERED` logged, then `BootScene → EmptyScene → GAM_PW2_HB_HomeBase → BootScene` every ~2 s, nothing in
`Player.log`. Diagnosis tools added to the loader (no Harmony on async methods): `Debug/StateTracer` polls
`PwsScreenManager.Instance.MainViewport.CurrentState/CurrentRootState` each frame and logs transitions, and reads
`LoadingStateBase.Task.Exception` when the loading task faults — the game catches the exception and only shows
`LoadingLevelStateBase.ExitWithError("An error occurred when loading")`. The exception:

```
KeyNotFoundException: The given key '2' was not present in the dictionary.
  at Campaign.TryFindPowerWashersFromPacks(List<PowerWasherData>)
  at ConfigurationManager.Init(...)  ← EquipmentManager.Init ← PlayerCharacter.InitializeSubSystems
  at LoadingLevelStateBase.SpawnPlayers(...) ← LoadingHubLevelState.Load
```

`Campaign.m_loadedContentManifests` is a `Dictionary<int, JobPackManifest>` keyed by the **pack reference index**, and
that method walks it as `0..Count-1`. The default campaign ships 3 references: `[0]` PW2 jobs, `[1]` Core
(player prefabs + 9 power washers), `[2]` `ContentPack_EditorAssets`, whose availability is
`Platform=-4096, ReleaseType=0, EditorModifier=AlwaysTrueOnEditor` — it never loads in builds, so vanilla keys are
`{0,1}`. Appending ours at index 3 produced `{0,1,3}` and the walk hit key 2. Fix (`CampaignInjection.WillLoad`):
insert pack references **before the first reference whose ContentPack availability excludes this platform/release**
→ keys `{0=Core, 1=PW2, 2=SpikeB}` (logged by `PackRegistry.LogCampaignManifestKeys`).

Also logged at hub time now: `JobHealth[...]` (every reference on a job, ours next to `Job_PW2_Billboard`) and
`JobStatus[...]` (`Campaign.GetFreePlayStatus/GetCareerJobStatus/GetLevelStatus` against the save). A freshly
registered job is `career=InProgress`, Free Play `Locked/Hidden`: Free Play opens only after the career job is
completed, by game rule, so "appears in Free Play" is not a registration criterion.

## Spike A — stub serialisation under IL2CPP

Pack `SpikeA`: scene `GAM_SPIKEA_Cube` with a cube carrying stub `UniqueId(4242)` + `WashTarget` → stub
`WashTargetData` (res 64), a `GameJobWashTargetRoot`, a `CharacterSpawnPosition(Main)`.
Loader: F5 or `AutoSpike = "A"` loads the catalog, loads the scene additively, and logs
`SPIKE A PASS/FAIL` with the component counts and values.

Status: **PASS** 2026-09-17 (after the two pack-build fixes above). Stub MonoBehaviours and their ScriptableObject
references deserialise onto the real IL2CPP types; no runtime `AddComponent` fallback needed.

## Spike B — job registration

Pack `SpikeB`: `JobPackManifest_SpikeB` → `Job_SpikeB` (`SPIKEB`, id 821964646) whose Location points at the
game's Billboard scene (GUID `88f3121e…`) with a 3-component `SceneWashableData`; customer, stage, one task with
three wash tasks; `pack.json` with `jobPackManifestGuid` and the job's strings.
Pass: `REGISTERED` line in the log (job present in `GameDataManifest.GetLoadedGameJobs()`), the job's cheat
button loads the Billboard scene, the three listed targets wash.

Status: **PASS** 2026-09-17 (hub reload loop solved, see above). The cheat button carries the title key
`SCRIPT_CAREER_SPIKEB_TITLE`; `AutoRunCheat` accepts `Title|KEY` alternatives. Loading the job created a career save
entry (`career=InProgress`); Free Play stays locked until completion, as for every job.

## Spike M — does our JobPackManifest deserialise?

`AutoSpike = "M"`: for each pack, `new AssetReferenceT<JobPackManifest>(guid).LoadAssetAsync()` in the hub (the exact call the game makes),
then log job count, each job's scene/SWD/customer/stages, and write a full reflective dump to `UserData/PWS2LevelLoader/dumps/*_spikeM_*.txt`.

## Spike C — one washable panel end to end

Pack `SpikeC` (`PWS2LevelKit/Assets/LevelKit/Editor/SpikeCPack.cs`), everything authored by us: scene
`GAM_SPIKEC_Panel` with a ground plane, a `GameplayBoot` hierarchy (below), `PWS_SPIKEC_Root`
(`UniqueId` + `GameJobWashTargetRoot` + wash CSV + palette), a 2 × 1.2 m panel quad whose `uv0`/`uv1`/`uv2` are all
0..1 (`MeshCollider`, `UVMetricsExtra`, `UniqueId`, `WashTarget`), a `CharacterSpawnPosition(Main)`, plus
`WashTargetData` → generated 512² RGBA control map, `TexturePalette` → the game's three Billboard `TextureLayer`
GUIDs, and a `GameJob`/`SceneWashableData`/`JobPackManifest` in the Spike B shape.

**PASS 2026-09-17 20:00.** One run, unattended: hub → cheat `SCRIPT_CAREER_SPIKEC_TITLE` → `GAM_SPIKEC_Panel`
loads → `PlayingCareerState` → `Complete All Tasks - Instant` → `JobCompleteGameState`. Evidence:

| Check | Result |
| --- | --- |
| dirt built from our control map | `Panel uniqueId=885198933 washIndex=0 loaded=True start=242614 current=242614 normalized=1.0000`, wash map RT 512² `R8G8B8A8_UNorm`, `TrimRectUV=(0,0,1,1)` — the authored texel count (242614) survived into `SceneWashableData` and the runtime agreed |
| palette resolved | `TPL_SpikeC.m_palette` filled at runtime with `Dirt_PW2_GrimeGreenSlimy`, `Dirt_PW2_MudSplats`, `Dirt_PW2_SandDarker` (we only serialise `m_paletteAddressables`; `TexturePalette.RefreshPalette` does the rest) |
| washing registers | after the completion cheat: `start=242614 current=0 clean=1.0000 normalized=0.0000` |
| job completes and persists | `JobStatus[Job_SpikeC]: freePlayUnlocked=True freePlay=Unlocked career=Completed level(FreePlay)=Available level(Career)=Complete` — still true after a full relaunch, so the save round-trips |

Two things a pack cannot serialise are applied by the loader in `PackRegistry.PrepareScene`, called from
`Core.OnSceneWasLoaded` for any scene named in a `pack.json` job's `sceneName`: `WashTarget.m_renderSettings` gets the
game's single `WashRenderSettings` asset, and each wash material is re-created on the game shader
`PWS/Wash Target Uber` (`_BaseMap`→`_BaseColour`, `_BaseColor`→`_BaseColourTint`, `_BumpMap`→`_Normal`).

### Level-system prefabs: every level scene needs a `GameplayBoot`

First attempt failed with `VContainerException: No such registration of type: FuturLab.PW2.GameplayAudioManager`
inside `SpawnPlayers`. Every game level scene (hub and jobs alike) carries:

```
GameplayBoot                 layer System (11), tag System
  Boot                       PrefabLoader, 46 PrefabToLoad entries, m_instantiateTimePerFrame = 0.0667
  GameplaySpecificBoot       PrefabLoader, m_parentLoader = Boot, 1 entry, 0
```

`LoadingLevelStateBase.CreateLevelContainer` awaits those loaders (`ContainerUtil.WaitForPrefabLoader`) and registers
every instantiated prefab into the level's VContainer scope; `SpawnPlayers` then injects them into the player.
The GUID lists were captured from the game's own hub and Billboard scenes with the loader's `[prefabs]` dump
(`PackRegistry.LogPrefabLoaders`) and live in `PWS2LevelKit/Assets/LevelKit/Editor/GameplayBoot.cs`: 27 of the 46
`Boot` entries pass their `PlatformConditional` on PC Steam release, plus the 1 `GameplaySpecificBoot` entry.
**Re-capture after every game update.** With those present the pack scene spawned
`GraphicsPipelineManager`, `DroneAssetInspectorManager`, `GameplayAudioManager`, `LevelProgressionSender` and
`PlayerCharacter_Default` exactly as a shipped level does.

`PrefabToLoad.PrefabAsset` / `InstantiateAtRootLevel` are **plain public fields**, not auto-properties: the first stub
used `[field: SerializeField]`, which serialised them as `m_PrefabAsset` and left every entry empty. Rule: a member
that appears in the IL2CPP proxy without a matching `_X_k__BackingField` is a field, and the stub must declare it as
`[SerializeField] public`.
