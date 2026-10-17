# Phase 0 findings — how PowerWash Simulator 2 v1.3.0 defines a level

Source: in-game dumps produced by `PWS2LevelLoader` (F7/F8/F6, automated with `AutoDump`) in the hub
(`GAM_PW2_HB_HomeBase`) and in the Billboard job (`GAM_PW2_HW_Billboard`), 2026-09-16, plus Mono.Cecil
dumps of the IL2CPP proxy assemblies (`docs/tools/dump_types.ps1`). Raw dumps live in
`<game>/UserData/PWS2LevelLoader/dumps/` (not committed).

Every item below is verified against the running game unless marked *assumption*.

## 1. Identity and hashing

| Thing | Rule | Example |
| --- | --- | --- |
| `GameContent.m_uniqueId` (jobs, campaigns, customers) | classic .NET Framework 32-bit `string.GetHashCode()` of `m_uniqueName` | `"BILLBOARD"` → `2015219005`; campaign `"DEFAULT"` → `-782300793` |
| `UniqueId.m_id` on scene objects | same hash of `UniqueId.FullName` = `"[<SceneName>]<Root>.<Child>.<Grandchild>"` (dotted path from the scene root) | `"[GAM_PW2_HW_Billboard]PWS_PW2_HW_Billboard.HatchHinge"` → `740449598` |
| `WashableObjectData.m_uniqueId` | equals the `UniqueId` of the `GameJobWashTargetRoot` object | `2011916029` for `PWS_PW2_HW_Billboard` |
| `WashableComponentData.m_uniqueId` | equals the `UniqueId` of that `WashTarget` object | `482848100` for `AdvertisementBackground` |

Algorithm (implement explicitly in the Unity editor tool; do not rely on Mono's `GetHashCode`):

```
h1 = h2 = 5381
for i in 0, 2, 4, ...:  h1 = ((h1 << 5) + h1) ^ s[i];  if i+1 < len: h2 = ((h2 << 5) + h2) ^ s[i+1]
id = (int)(h1 + h2 * 1566083941)      // 32-bit wraparound, then reinterpret as signed
```

## 2. Content packs and registration

- Default campaign `Campaign_Default` (`DEFAULT`, id `-782300793`) has 3 `CampaignJobPackReference`s. Its own
  pack reference has **`ContentPack = null`** and `ContentManifest` = `AssetReferenceT<JobPackManifest>` by GUID.
  The job pack manifest `CampaignManifest_ContentPack_PW2` lists the 38 base jobs; `CampaignManifest_Core`
  carries the 2 player prefabs and 9 power washers (`m_gameJobs` empty).
- DLC campaigns (`BBG`, `HUT`) each have one pack reference whose `ContentPack` is a `ContentPackManifest`
  with `IsSeparateCatalog = true`, `Type = Linked`, `m_loadPathValue = "{FuturLab.DLCProviderManager.DLCUriScheme}<ID>"`,
  resolved to `dlc:<ID>/catalog_<ID>.bin` (= `<game>/DLC/<ID>/catalog_<ID>.bin`). The base pack `PW2` uses
  `{UnityEngine.AddressableAssets.Addressables.RuntimePath}/StandaloneWindows64/PWS2` and is not a separate catalog.
- Loaded Addressables locators at boot: main catalog, `dlc:BBG/catalog_BBG.bin`, `dlc:HUT/catalog_HUT.bin`, and a
  remote catalog `https://pws2-assets.futurlab.games/addressables/1.3.0/<hash>/PCSteam/catalog_RMT.bin`
  (remote furniture). All are `ContentCatalogData.ResourceLocator` (binary catalogs, Addressables 2.5.0).
- Consequence for the mod: load our catalog ourselves with `Addressables.LoadContentCatalogAsync(path)` before
  `Campaign.LoadContent`, then insert (before `ContentPack_EditorAssets`, which never loads in builds; see `docs/phase1-spikes.md`) a `CampaignJobPackReference { ContentPack = null, ContentManifest = <our GUID>,
  DLCPackSettings.m_isDlc = false, m_jobPreviews = [...] }` to `Campaign_Default.m_jobPackReferences`. The game then
  loads our `JobPackManifest` through its normal path. Our bundles' internal ids should use a load path relative to
  the game root (e.g. `Mods/PWS2LevelLoader/packs/<pack>`) or `{UnityEngine.AddressableAssets.Addressables.RuntimePath}/../../..` (*assumption, Spike A*).
- The Jobs menu (`JobSelectionState`) lists `CampaignJobPreview` objects: `UniqueID`, `UniqueName`, `LocalizedName`,
  `LocationAddress`, `Selected/DeselectedThumbnailImageAsset`. Build them at runtime from `pack.json`.

## 3. The GameJob (Billboard reference values)

```
GameJob 'Job_PW2_Billboard'
  m_uniqueName = "BILLBOARD"            m_uniqueId = 2015219005      CampaignID = -782300793
  m_unlockedByDefault = false           m_unlockRules = 2 rules       (ours: true / none)
  m_saveSize = Small (10 MB)            m_levelDevelopmentStage = ProductionReady   IsTestJob = false
  m_completionPayment = 10000           m_completionPaymentSecondary = 5000 (MuckCoins)
  m_useLoadout = false                  m_loadout.m_entries = []      m_bonusTasks = []
  m_customer = GameJobCustomer 'Customer_PW2_JeffJeffersonXIII' (GameContent, m_portraitAsset sprite)
  m_allMessageSequences = 9, Brief/Completion message sequences (ours: none / null — verify in Spike C)
  m_timelapseQualityOverride = OverridableProperty { Override=false, Value={MaxPixelDiff=5, MinPixelDiffCount=4} }
  m_timelapseStaticThumbnail = sprite 'TimelapseEnd_Billboard'
  m_stringTableName = ""  (→ StringTableName falls back to "PWS2_Main")     m_localizationId = null
  m_localizedName          = PWS2_Main / SCRIPT_CAREER_BILLBOARD_TITLE        ("Billboard")
  m_localizedDescription   = PWS2_Main / SCRIPT_CAREER_BILLBOARD_DESCRIPTION  (missing → key shown)
  m_freePlayNameInteraction= PWS2_Main / HOMEBASE_BILLBOARD_FREEPLAY          ("Billboard (Free Play)")
  LocalizedStoryInteraction= PWS2_Main / HOMEBASE_BILLBOARD_STORY             ("Billboard")
  LocalizedNameFreePlay    = PWS2_Main / SCRIPT_CAREER_BILLBOARD_TITLE_FREEPLAY (missing → key shown)
  m_availability = PlatformConditional { Platform=All, ReleaseType=All, EditorModifier=SameAsOnBuilds }
  Location:
    SceneName = "GAM_PW2_HW_Billboard"     SceneAssetReference = guid 88f3121e…   (the .unity scene)
    SceneWashableData = SO 'GAM_PW2_HW_Billboard'
    SceneShaderCollectionAsset = AssetReferenceShaderVariantCollection guid 9217f14…
    LocalizedAddress = PWS2_Main / SCRIPT_CAREER_BILLBOARD_ADDRESS ("Mauka Aitu Highway, Lubri City ")
    LocalizedLoadingInfo = PWS2_Main / keyId 127286908130680836 (a hint string)
    LoadingScreenImageAsset 'Highway', PreviewImageAsset 'Preview_PW2_Billboard', ChatImageAsset 'ChatIcon_PWS2_Billboard',
    SelectedThumbnailImageAsset 'Thumbnail_PW2_Billboard_A', DeselectedThumbnailImageAsset 'Thumbnail_PW2_Billboard_B'
    AmbienceSoundEvent = FMOD EventReference { Guid = afbe6293-29cd-4e2a-843b-e533fc242c16 }   (highway ambience)
    MaxMapHeight = 12.1   HasWind = true   HasReverb = false   Gravity = 9.81
    OverrideSoapGlobalConfig = false, RuntimeSoapGlobalConfig = null, CustomRenderProfile = Disabled, Runtime*Profile = null
  m_stages = [ GameJobStage { m_uniqueName="", m_localizationKey="STAGE_BILLBOARD_", m_hideNameWhenLocked=false,
               m_delayLoadingWashMaps=false, m_tasks = 4 × GameJobTask } ]
  GameJobTask { m_isDefault=true (first only), m_washableObjectUniqueId=2011916029, m_parentObjectUniqueId=0,
                m_previousWashableObjectUniqueName="", m_bonusPayment=0, m_washTasks.m_washTasks=[] (unused),
                m_washTaskList = List<GameJobWashTask> (7 here; 35 across the 4 tasks), m_washTaskCount }
  GameJobWashTask { m_washableComponentUniqueId, m_payment (200…1800), m_parentWashableObjectUniqueId=2011916029,
                m_localizationKey="WASHTASK_GAM_PW2_HW_BILLBOARD_WASH_SUPPORTCOLUMN",
                m_componentLocalizationKey (same, or "…_SUPPORTCOLUMNANCHORBOLT_0" for a group member),
                m_componentGroupLocalizationKey="…_GROUP", m_superTaskName="", m_superTaskWashTargetIds=[] }
```

Localization key patterns (table `PWS2_Main`, English table `PWS2_Main_en`, ~6000 entries, loaded in the hub):

| Purpose | Key |
| --- | --- |
| job title / description / address | `SCRIPT_CAREER_<UNIQUENAME>_TITLE`, `_DESCRIPTION`, `_ADDRESS` |
| hub interactions | `HOMEBASE_<UNIQUENAME>_FREEPLAY`, `HOMEBASE_<UNIQUENAME>_STORY`, `SCRIPT_CAREER_<UNIQUENAME>_TITLE_FREEPLAY` |
| stage | `STAGE_<UNIQUENAME>_<stage m_uniqueName>` |
| wash task | `WASHTASK_<SCENENAME UPPER>_WASH_<COMPONENT UPPER>` (+ `_GROUP`, + `_<n>` per group member) |
| customer | `CUSTOMER_CAREER_<CUSTOMERNAME>_TITLE` / `_DESCRIPTION` |

Wash-task keys are derived from names at `Init()`, so the loader must add entries with these exact keys to
`PWS2_Main_en` at runtime (or postfix the getters).

## 4. SceneWashableData

- `m_washableObjects`: one `WashableObjectData` per `GameJobWashTargetRoot` (`m_name`, `m_uniqueId`,
  `m_parentId = 0`, `m_fullName = "[Scene]Root"`, `m_componentUniqueIds` = all component ids).
- `m_washableComponents`: one `WashableComponentData` per `WashTarget` (`m_name`, `m_uniqueId`,
  `m_parentObjectUniqueId`, `m_startDirtiness` (texel count, e.g. 481962 for a 1024² map), `m_groupComponentUniqueIds`
  (= itself for singletons; siblings `Name_0..n` share one wash task), `m_groupIndex`, `m_superTaskName=""`,
  `m_isSuperTaskMember=false`, `m_superTaskIds=[]`, `m_cleanName=null`).

## 5. Scene contents (Billboard)

Root objects: `PWS_PW2_HW_Billboard` (GameJobWashTargetRoot + UniqueId, layer `Washable`) with the wash targets as
children; `PNE_` (navigation/blockers), `PLT_` (lighting), `PEV_PW2_HW_Highway` (environment), `PFX_` (VFX),
`INT_` (interaction animation), spawn positions, plus runtime-spawned singletons (cats, GraphicsPipelineManager,
GameplayAudioManager, LevelProgressionSender, PlayerCharacter_Default). 12 `CharacterSpawnPosition`s
(`Main` = player, `Secondary` = co-op, `Emergency` = fallbacks), each with a `CapsuleCollider`, layer `SpawnPositions`,
`m_Radius 0.5`, `m_Height 1.4`, `m_obstructionMask 8672513`, `m_ObstructionFixIterationCount 50`, `m_MaxFixDistanceAllowed 1`.

Each wash target GameObject: `MeshFilter`, `MeshRenderer`, `UVMetricsExtra`, `WashTarget`, `UniqueId`, `MeshCollider`;
layer `Washable`. `WashTarget` fields: `m_washTargetData` (SO), `m_gameJobWashTargetRoot` (the root),
`m_uniqueId`, `m_renderSettings = WashRenderSettings 'WashRenderSettings'` (one shared asset), `m_meshRenderer`,
`m_canBeCleanedByClient = false`.

Meshes of wash targets have **three UV sets** (readable):

| Channel | Meaning | Billboard `Hatch` example |
| --- | --- | --- |
| `TexCoord0` (`mesh.uv`) | base-texture UVs, may tile outside 0..1 | (-0.29,-0.92)…(0.61,1.92) |
| `TexCoord1` (`mesh.uv2`) | dirt UV **plus an integer cell offset (X, Y)** unique per target in the scene | (0.001,46.001)…(0.345,46.556) → cell (0,46) |
| `TexCoord2` (`mesh.uv3`) | local dirt UV in 0..1; the control map is painted in this space | (0.001,0.001)…(0.345,0.556) |

The cell offsets are exactly the CSV columns below. Cells are laid out 10 wide (`UNIQUEID = Y*10 + X`).

## 6. WashTargetData and control maps

```
WashTargetData 'Billboard_Hatch_BEBCBE16'
  SuggestedControlMapResolution = 1024          ControlMapAsset = AssetReferenceTexture2D (BC7, 360x576, no mips)
  ControlMapAreaInUse = (0,0,354,571)   ControlMapTrimmedRect = (0,0,355,572)   ControlMapTotalRect = (0,0,0,0)
  TexturePalette = 'TPL_PW2_Billboard_GrimeGreenSlimy_MudSplats_SandDarker' (3 AssetReferenceTextureLayer)
  OverrideSurfaceCleanThreshold = true   SurfaceCleanThreshold = 0.27 (0.01…0.27 across targets)
  TemplatePaintLayerMaterial = null      OpacityCurveConfig = null      ConfirmedByDesigner/ApprovedByDesign = false
```

- The shipped control-map texture is the painted area **cropped** out of the `SuggestedControlMapResolution` square,
  rounded to 8-texel blocks; at runtime `WashTarget.ControlMapPNGTrimRectUV = TrimRect / SuggestedResolution`
  (Hatch: 0..0.35 × 0..0.56, matching its `TexCoord2` bounds). For our targets: no cropping — full square texture,
  `AreaInUse = TrimmedRect = (0,0,res,res)`.
- Channel meaning (readback of the runtime control map and of the freshly initialised wash map RT, which is a
  copy of it): **R, G, B = opacity of palette layer 0, 1, 2**; values mostly 0 or 255 with soft edges; alpha unused (~0).
  Effect map RT (R8) starts at 0. So a dirt mask for a 3-layer palette is an RGB PNG in `TexCoord2` space.
- `m_startDirtiness` is the texel count of dirt at load (computed by the game).
- `WashRenderSettings 'WashRenderSettings'` holds the compute shaders and the default `WashRenderProfile_Default`
  (DirtNoiseScale 1280, DirtNoiseAmount 0.95). `TexturePalette` assets are per job (`TPL_PW2_<Job>_<layers>`); the
  hub has 57 furniture palettes loaded. A palette holds only Addressables references to `TextureLayer` assets.

### Dirt layers, tiers and layering (1.3.1, from F8 dumps, 2026-09-24)

Each `TextureLayer` carries `m_dirtTier` (`DirtTier`: `Tier_1_Surface`, `_2_Encrusted`, `_3_Embedded`, `_4_Tough`,
`_5_Stubborn`, `_6_Ingrained`, `_7_Oily`), `m_layerModel` (`Thin`/`Volumetric` + `m_volumetricMaxThickness`),
`m_layerDirtType` (`PBRPack`, `Snow`, `Oil`, `Ice`, …) and particle settings. How fast a player removes a layer is the
washer's per-tier power times the nozzle's `NozzleCleaningSettings.m_cleaningPowerModifiers[tier]`. Several nozzles
have a 0 modifier for T5–T7, so the player has to pick a nozzle for tough layers. The tiers belong to the game's
layer assets. A pack references layers by GUID and never authors a tier.

| Layer | GUID | Tier | Model |
| --- | --- | --- | --- |
| Dirt_PW2_GrimeGreenSlimy | `58956cc2a4338fd4cb7913869c50e06f` | 1 | Thin |
| Dirt_PW2_Grime | `acca5075491c9884aa71244dd1b7e040` | 1 | Thin |
| Dirt_PW2_MudSplats | `5e7adb63034f5554385adf5e2002a2af` | 1 | Volumetric 0.005 |
| Dirt_PW2_SandDarker | `a4897b21ac3656848a2501a52ed0a3e8` | 1 | Thin |
| Dirt_PW2_SandDarkerYellow | `4d994ab58fa95c74daad7af7ea07d5ef` | 1 | Thin |
| Dirt_PW2_Dust | `b366e3f12be99684d8ab3f4c38c5c1cb` | 1 | Thin |
| Dirt_PW2_Mud | `e947a17b14f982644b5249754842c877` | 1 | Thin |
| Dirt_PW2_FoodDarker / Crumbs / Cobwebs | `d2e6f9ee…` / `7b2fb7eb…` / `df7626d2…` | 1 | Thin |
| Dirt_PW2_BirdPoop / BirdPoopDark | `fbe84bc3…` / `ff2d0fc6…` | 1 | Thin |
| Dirt_PW2_GrimeStreaks | `546deae2efb793e42869891b15d17347` | 2 | Thin |
| Dirt_PW2_CrackedMud | `47b3c72e2b644a746acd9cfd0dc38642` | 3 | Thin |
| Dirt_PW2_PaintedFlakes | `6300a782d3638bc47b502c7796689c3a` | 5 | Thin |
| Dirt_PW2_PaintedFlakesRust | `94e1048e653014b4ba6be4dc12b40a37` | 6 | Thin |

- **Layering convention** (from control-map readbacks of Billboard and Tram):
  - Layer 0 covers the object.
  - Layer 1 sits inside layer 0, and layer 2 inside layer 1. For Billboard `SupportColumnBase`, GB = RGB.
  - Most texels have soft, partial values.
  - Tiers are mixed. The Tram uses `TPL_PW2_Tram_PaintedFlakesRust_Grime_Dust`: a T6 base under two T1 coats.
  - A palette asset's name lists its layers in index order.
- **Start-dirtiness threshold.** `m_startDirtiness` equals the count of texels whose max channel is above a value
  between 33 and 64. Billboard Hatch is 54 388; the count is 54 414 above 32 and 54 329 above 64 (BC7 readback). Keep
  every non-zero control-map value ≥ 64, and "any channel > 0" counts the same as the game.
- **In-game results for the OKC ring (2026-09-24).** Panels use rust flakes (T6) under Grime and MudSplats. The curb
  uses GrimeGreenSlimy, SandDarker and CrackedMud (T3). All six layers resolve by GUID from our pack's scene and from
  `GAM_PW2_HS_Tram`, with the Billboard shader collection.

## 7. Wash CSV (`GameJobWashTargetRoot.m_csvFileAsset`, TextAsset)

```
CSV VERSION 2
NODE NAME,TEXTURE SIZE,X OFFSET,Y OFFSET,UNIQUEID,UVSPACE
Hatch,1024 x 1024,0,46,460,4.804289228150506%
HatchHandle,64 x 64,1,46,461,16.771984689575227%
HatchHinge,128 x 128,2,46,462,18.17473635136401%
```

One row per wash target: name, suggested control-map size, `TexCoord1` cell offset, cell id (`Y*10+X`), share of UV space.
Whether the runtime reads it is unknown (*Spike C decides*); generating it is trivial, so the pack build emits it.
`m_texturePalette` on the root was `TexturePalette_UvCheckers` (a debug palette), so the per-target palette on
`WashTargetData` is the one that matters.

## 8. Materials and shaders

Wash-target renderers use the game shader **`PWS/Wash Target Uber`** (3993 material instances in the Billboard scene);
environment uses `PWS/PBR Foliage`, `Universal Render Pipeline/Lit`, `Universal Render Pipeline/Unlit`,
`Shader Graphs/Water`. At runtime `WashTarget.CreateInstancedMaterials` clones the authored material into
`<path>mat0_<Surface> (Instance)`. The authored material must therefore already use `PWS/Wash Target Uber`.
Plan: author with URP Lit using the same property names, and let the loader switch `material.shader` to the game
shader found via `Resources.FindObjectsOfTypeAll<Shader>()` (loaded in the hub by the furniture wash targets).
`PWS/Wash Target Uber` (103 properties, 6 passes) — the ones a pack author sets:

| Property | Type | Meaning |
| --- | --- | --- |
| `_BaseColour`, `_BaseColourTint` | Texture, Color | albedo |
| `_Normal`, `_NormalIntensity` | Texture, Range | normal map |
| `_PBRPack`, `_MetallicIntensity`, `_OcclusionIntensity`, `_RoughnessIntensity` | Texture, Range×3 | packed metallic / occlusion / roughness (channel order TBD: read back a game `*_pbrPack` texture before authoring) |
| `_Tiling` | Vector (x,y tiling, z,w offset) | UV0 tiling |
| `_TRIPLANAR_MAPPING`, `_TriplanarFalloff` | Float toggle, Range | triplanar option |
| `_Emissive`, `_EmissiveColour`, `_EmissiveIntensity` | | emission |
| `_Opacity`, `_AlphaCutoff`, `_Surface`, `_Blend`, `_Cull`, `_AlphaClip`, `_Cutoff` | | surface options (defaults: opaque, cull back) |

Runtime-driven (leave alone): `_WashableEnable`, `_DirtScale`, `_WashMapRectTx`, `_WashMapTrimmedRectTx`, `_Layer*`
vectors, `_LayerEnable` (bitmask, 7 = three layers), `WashMap`, `EffectMap`, `_Dirt{0,1,2}Albedo/Normal/Metallic`
(filled from the palette's `TextureLayer`s), highlight/flash colours, `_Decal*` (optional decal layer).
**Where dirt visuals live (1.3.1, DirtStudio spikes, 2026-09-24).** The `_Dirt{0,1,2}Albedo/Normal/Metallic`
textures and the per-slot `_Layer*` vectors are set on each target's instanced material; no property block is used,
and `SetTexture` on the instance sticks. Swapping `WashTargetData.TexturePalette` for a clone before wash init makes
the game load the new `TextureLayer`s into the palette, yet the materials keep the scene's authored textures. The
visuals therefore do not follow the palette. Details: `DirtStudio/docs/spikes.md`.

Layer indices: `Washable = 8`, `SpawnPositions = 23`, `Zone = 28`, `Items = 14`, `ItemTrigger = 16`,
`NavigationCollision = 12`, `System = 11`, `PhysicsWashable = 29`, `Full Bake = 9`.

## 9. Cheat menu (dev tooling)

`PWSCheatManager(Clone)` exists in every scene. Category `Scenes` has one `CheatFunction` button per job named
after the job ("Billboard", "Gas Station", …, DLC ones show their title key). `CheatData.CheatFunction.Invoke()`
loads that job from the hub without opening the panel; `PWS2LevelLoader` uses this as `AutoRunCheat`. Other useful
buttons: `Progression / Complete All Tasks - Instant`, `Washing / Instant Wash Mode`, `Progression / Add $10000`.

## 10. Runtime plumbing facts

- Boot flow with no input: `BootScene` → `EmptyScene` → `GAM_PW2_HB_HomeBase` in ~20 s. Job load from the hub:
  `EmptyScene` → `GAM_<job>` in ~2 s.
- Launching the exe directly makes Steam relaunch it **without** the command-line arguments; use MelonPreferences
  (`UserData/MelonPreferences.cfg`) for automation settings, launch via `steam://rungameid/2968420`.
- IL2CPP interop: interface proxies do not inherit members (`IEnumerator<T>` has no `MoveNext` → cast to
  `Il2CppSystem.Collections.IEnumerator`; `IList<T>` has no `Count` → cast to `ICollection<T>`). Enumerating the binary
  catalog's keys / `Locate` through interop crashed the process natively; `AllLocations` is the safer path (F9, opt-in).
- Layers used: `Washable`, `SpawnPositions`, `Zone`, `Items`, `ItemTrigger`, `NavigationCollision`, `System`,
  `AmbienceChangerTrigger` (indices in the `=== LAYERS` table of the scene dump).

## 11. What this settles for the plan

| Plan question | Answer |
| --- | --- |
| (a) UV channel of the control map | `TexCoord2` local 0..1; `TexCoord1` = same + per-target integer cell offset |
| (b) control-map channels | RGB = palette layers 0/1/2 opacity, alpha unused |
| (c) CSV | version-2 layout table; generate it, runtime need TBD in Spike C |
| (d) content-pack values | see §2; `ContentPack = null` + own catalog load is the minimal path |
| (e) player spawn | `CharacterSpawnPositionType.Main` (+ Secondary for co-op, Emergency fallbacks) |
| (f) localization | table `PWS2_Main`, key patterns in §3 |
| (g) borrowable assets | `WashRenderSettings 'WashRenderSettings'`, any `TPL_PW2_*` `TexturePalette`, shader `PWS/Wash Target Uber`; `TemplatePaintLayerMaterial`/`OpacityCurveConfig` are null in shipped data |
| ids | job id = hash(uniqueName); object ids = hash("[Scene]Path") |
