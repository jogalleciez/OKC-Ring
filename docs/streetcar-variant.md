# The OKC ring in an existing level (Streetcar) — method and findings

Verified in-game 2026-09-23 (v1.3.0). Pack `OKC_Streetcar`, kit `PWS2LevelKit/Assets/LevelKit/Editor/OkcStreetcarPack.cs`,
loader `PWS2LevelLoader/src/Packs/SceneAugmenter.cs`.

## Answer

Feasible, and it now works: a separate job, **OKC_STREETCAR** ("OKC Screen on Equinox Boulevard"), loads the game's own
Streetcar scene with the tram removed and the ring standing where it was. The ring's 15 targets get our dirt, wash to 0,
the job completes and the result persists. The vanilla Streetcar job (`TRAM`) is unchanged: loading it gives the same
scene as without the mod (7719 objects, 277 wash targets all loaded, no OKC objects, all lights on).

Changing the vanilla job itself was rejected: it would alter the job's task list and progress in existing saves.

## How it works

| Piece | Where | What |
| --- | --- | --- |
| Job on a game scene | kit | `GameJob.Location.SceneAssetReference` = the Tram scene GUID `430726867bfab74409b388bdd6f12b50` (Spike B pattern); `SceneWashableData` lists only the ring, so the game builds wash maps only for it and leaves the 277 tram targets `loaded=False`. Location values (shader collection, ambience, loading screen, thumbnails, timelapse) are the Tram job's. |
| The ring as a prefab | kit | `PWS_OKC_Root_Streetcar.prefab`: root (`UniqueId` + `GameJobWashTargetRoot` + CSV) + 15 targets, ids hashed as `[GAM_PW2_HS_Tram]PWS_OKC_Root.<child>`. TexCoord1 cells start at 380 (row 38): the tram uses rows 0..37. Asset names carry `_Streetcar` so no Addressables address repeats the OKC pack's. |
| Which job is loading | loader | `PwsScreenManager.Instance.MainViewport.CurrentState` → `LoadingLevelState.Job`, read in `OnSceneWasLoaded`. |
| Timing | loader | `OnSceneWasLoaded` runs before the level pipeline reaches `WashManager.InitializeScene`, so a prefab instantiated there (and moved into the Tram scene with `SceneManager.MoveGameObjectToScene`) is initialised like a scene-authored target. The prefab is loaded ahead of time (`AssetReferenceGameObject.LoadAssetAsync` once the pack catalog is ready, at boot) and never waited on. |
| Removing the tram | loader | Renderers, colliders and lights are switched off, never `SetActive(false)`, so scene scripts that reference the tram keep valid objects. `hideWashRoots` walks every `WashTarget` whose `m_gameJobWashTargetRoot` is `PWS_PW2_HS_Tram`; `hideObjects` names scene roots; `hideContainedIn` switches off whatever lies entirely inside the tram's volume (its targets' bounds + 0.3 m sideways, +3 m up), skipping particle/VFX objects and triggers. A directional light is never switched off. A recheck 10 s later found nothing re-enabled by the game. |
| Fixups | loader | `PrepareScene` (render settings + wash shader) runs for a game scene only when the augmented pack job is loading; `AutoResetPackTargets` resets only the added roots. |

## Tram scene facts (dump of 2026-09-23)

- The tram stands at (26.66, 0, -26.66), long axis along Z, ~2.9 m wide, in the middle of a ~21 m wide street
  (pavements at x ≈ 15.4 and ≈ 37). The ring (Ø 9.2 m) at the tram's origin clears both pavements; the 12 spawn
  positions are 8–15 m away.
- At scene-load time the tram is grouped: `PWS_PW2_HS_Tram` (targets), `PNW_PW2_HS_Tram` (non-washable parts),
  `PNC_PW2_HS_Tram` (step collision), `PEV_PW2_HS_TramShadowCaster`, `Animation` (door interactables). **The game
  flattens the hierarchy later** (a runtime dump shows 229 of the 277 targets as scene roots and `UniqueId.FullName`
  without the root), so match roots at load time or by `m_gameJobWashTargetRoot`, never by children after load.
- **`PLT_PW2_HS_Tram` is the level's whole lighting rig, sun included** — hiding it turned the trees black. The tram's
  own lights (`AmbientInsideLight_01/02`, `WhiteLight_01..03`) sit inside its volume and go with `hideContainedIn`.
- The player did not always start at the Main spawn (one run started at (24.16, 0, -31.91), just outside the ring).
  Not investigated; harmless here, but a larger addition could trap the player inside it.

## Doing this for another level

1. Dump the level (`AutoRunCheat="<button>"`, `AutoDump=true`) and read: the target root name and its load-time sibling
   groups, the placement spot, spawn positions, the max `Y OFFSET` in the wash CSV, and the job's `Location` values.
2. Copy `OkcStreetcarPack.cs`: scene name, cell base, GUIDs, placement, hide lists.
3. Build, run the variant with `AutoRunCheat2="Complete All Tasks - Instant"`, then run the vanilla job and compare its
   scene totals with a vanilla dump.

Open ends: the job uses the Tram's thumbnails, preview and timelapse sprites (they show the tram); the dirt reads
faintly on the dark metal in this light.
