# PowerWash Simulator 2 — custom level mod (OKC Transformer Screen)

A MelonLoader mod that adds a standalone level (a career job that unlocks in Free Play once completed) to PowerWash Simulator 2:
the perforated "OKC" transformer screen from downtown Oklahoma City on a small plaza.

The mod is built the way the game's own DLC is built: a Unity Addressables **content pack**
(`JobPackManifest` → `GameJob` → scene + `SceneWashableData`) plus a thin loader DLL
(`PWS2LevelLoader`) that registers the pack with the game's campaign at boot.

| Folder | What it is |
| --- | --- |
| `OKC_TransformerScreen/` | Procedural Blender build of the prop (FBX/GLB, dirt-mask UVs, verify script) |
| `PWS2LevelLoader/` | MelonLoader mod (C#, net6.0). Phase 0 ships the reconnaissance dumper (F6/F7/F8). |
| `PWS2LevelKit/` | Unity 6000.0.73f1 project with stub game types and the pack build tooling (Phase 1+) |
| `docs/` | Nexus description, tooling scripts (`docs/tools/dump_types.ps1`), notes |
| `dist/` | Zip layout for Nexus / GitHub Releases (build output, not committed) |

## Build the loader

Requires the .NET 8 SDK and the game with MelonLoader 0.7.x installed (the csproj references
the generated `MelonLoader/Il2CppAssemblies`). The build copies the DLL into the game's `Mods/`.

```bash
dotnet build PWS2LevelLoader -c Release
```

Different game path: `dotnet build PWS2LevelLoader -c Release -p:GameDir="D:\Games\PowerWash Simulator 2"`.
Skip the copy: `-p:CopyToGame=false`.

## Debug keys and automation (Phase 0)

Enabled by `EnableDebugKeys` in `UserData/MelonPreferences.cfg` (on by default until release).
Output goes to `UserData/PWS2LevelLoader/dumps/`.

| Key | Dump |
| --- | --- |
| F7 | Hierarchy of every loaded scene: components, `UniqueId`s, `WashTarget` data, spawn points, mesh UV channels, materials, layer table, shader property tables |
| F8 | Game data: manifest, campaigns, content packs, job packs, all jobs, the current job in full, wash data assets, cheat-menu buttons, localization tables |
| F6 | Control maps: PNG readback of each `WashTarget`'s runtime control map (first 8), wash/effect map RTs (first 2), and the wash-target CSV |
| F9 | Every Addressables location as TSV (opt-in: drives the binary catalog reader through interop) |
| F5 | Spike A: load the `SpikeA` pack scene additively and verify the stub components |
| F10 | Lighting (RenderSettings, lights, probes, URP volumes, cameras), a screenshot, and the current job's Location sprites as PNG |

Always-on diagnostics in `MelonLoader/Latest.log`: `[state]` lines trace the game's state machine and print the
exception of a faulted loading state (the game itself only shows "An error occurred when loading"); `[hubload]`
marks the hub load steps; at hub time `JobHealth[...]`, `JobStatus[...]` and the campaign's loaded-manifest keys
are logged for every pack job.

Unattended runs: set in `[PWS2LevelLoader]` of `UserData/MelonPreferences.cfg` and launch via
`steam://rungameid/2968420` (launching the exe directly drops command-line switches).

```toml
AutoDump = true              # dump every GAM_* scene 12 s after it initialises
AutoDumpDelaySeconds = 12.0
AutoRunCheat = "Billboard"   # after the hub dump, press this cheat-menu button (Scenes category = one per job)
AutoQuitAfterDumps = 2       # quit after N dumps (hub + job)
AutoSpike = ""               # "A", "M", "G" (comma list): run these spikes in the hub, dump, quit
RegisterPacks = true         # false = load nothing into the campaign (diagnostics only)
AutoResetPackTargets = false # dev: put the dirt back on a pack level's wash targets a few s after it loads (session only)
SceneAdditionOverride = ""   # dev: "x,y,z[,yaw]" places a pack's scene addition there instead of its pack.json position
```

`AutoRunCheat` accepts alternatives separated by `|` (pack jobs' buttons carry the title key, e.g.
`"Spike B Billboard|SCRIPT_CAREER_SPIKEB_TITLE"`). If the hub reloads 6 times during an automated run the loader
dumps and quits instead of looping forever.

Findings distilled from the dumps: [docs/phase0-findings.md](docs/phase0-findings.md).

## A pack job on one of the game's own levels

Pack `OKC_Streetcar` puts the ring into the game's Streetcar street (`GAM_PW2_HS_Tram`) in place of the tram, as a
separate job ("OKC Screen on Equinox Boulevard"); the vanilla Streetcar job is untouched. The job's `pack.json` lists
`sceneAdditions` (prefabs from the pack), `hideWashRoots` / `hideObjects` / `hideContainedIn` (what to switch off), and
the loader applies them only when that job is the one loading. Build it with
`-executeMethod PWS2LevelKit.Editor.OkcStreetcarPack.CreateAndBuildFromCommandLine` (`-nographics` is fine).
How and why: [docs/streetcar-variant.md](docs/streetcar-variant.md).
