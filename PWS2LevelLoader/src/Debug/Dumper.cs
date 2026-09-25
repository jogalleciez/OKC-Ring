using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppFuturLab;
using Il2CppFuturLab.ContentPacks;
using Il2CppFuturLab.PW2;
using Il2CppFuturLab.PW2.TexturePainter;
using Il2CppFuturLab.PW2.WashGraphics;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PWS2LevelLoader.Debug
{
    /// <summary>
    /// Phase 0 reconnaissance. Three debug keys, each writing files under UserData/PWS2LevelLoader/dumps:
    ///   F7  scene hierarchy of every loaded scene, with WashTarget / UniqueId / spawn details and mesh UV channels
    ///   F8  game data: GameDataManifest, campaigns, job packs, jobs, wash data, palettes, localization tables,
    ///       and every Addressables key (TSV)
    ///   F6  control maps: PNG readback of each WashTarget's RuntimeControlMap (+ first wash-map RTs) and the wash CSV
    ///   F9  every Addressables location (TSV). Kept out of RunAll: it drives the binary catalog reader through interop.
    /// </summary>
    internal static partial class Dumper
    {
        private static string _dumpDir;

        public static void Init()
        {
            _dumpDir = Path.Combine(MelonEnvironment.UserDataDirectory, "PWS2LevelLoader", "dumps");
            Directory.CreateDirectory(_dumpDir);
        }

        public static void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7)) Run("scene", DumpScenes);
            if (Input.GetKeyDown(KeyCode.F8)) Run("gamedata", DumpGameData);
            if (Input.GetKeyDown(KeyCode.F6)) Run("controlmaps", DumpControlMaps);
            if (Input.GetKeyDown(KeyCode.F9)) Run("addressables", DumpAddressables);
            if (Input.GetKeyDown(KeyCode.F10)) { Run("lighting", DumpLighting); Run("screenshot", CaptureScreenshot); Run("sprites", StartSpriteDump); }
        }

        /// <summary>Run every dump in sequence (used by the automation mode). The sprite dump finishes over the next frames.</summary>
        public static void RunAll()
        {
            Run("scene", DumpScenes);
            Run("gamedata", DumpGameData);
            Run("controlmaps", DumpControlMaps);
            Run("lighting", DumpLighting);
            Run("screenshot", CaptureScreenshot);
            Run("sprites", StartSpriteDump);
        }

        private static void Run(string what, Action<string> action)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var scene = Sanitize(SceneManager.GetActiveScene().name);
            var pathBase = Path.Combine(_dumpDir, $"{stamp}_{scene}_{what}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                action(pathBase);
                Core.Log.Msg($"Dump '{what}' written to {pathBase}* in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Core.Log.Error($"Dump '{what}' failed: {ex}");
            }
        }

        // =============================================================== F7: scenes

        private static void DumpScenes(string pathBase)
        {
            using var w = new StreamWriter(pathBase + ".txt", false, Encoding.UTF8);
            Header(w, "SCENE HIERARCHY");
            var dump = new Il2CppDump(w, maxDepth: 3, maxElems: 32);
            var stats = new int[5]; // gos, washTargets, uniqueIds, roots, spawns

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                w.WriteLine();
                w.WriteLine($"=== SCENE {i}: name={scene.name} path={scene.path} loaded={scene.isLoaded} rootCount={scene.rootCount} active={scene == SceneManager.GetActiveScene()}");
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null) continue;
                    WalkGameObject(w, dump, root.transform, "", stats);
                }
            }
            w.WriteLine();
            w.WriteLine($"=== TOTALS: gameObjects={stats[0]} washTargets={stats[1]} uniqueIds={stats[2]} washTargetRoots={stats[3]} spawnPositions={stats[4]}");

            w.WriteLine();
            w.WriteLine("=== LAYERS (index: name)");
            for (int i = 0; i < 32; i++) { var ln = LayerMask.LayerToName(i); if (!string.IsNullOrEmpty(ln)) w.WriteLine($"  {i}: {ln}"); }

            w.WriteLine();
            w.WriteLine("=== SHADER PROPERTIES (shaders used by materials in the loaded scenes, first 12 distinct)");
            var seen = new HashSet<string>();
            foreach (var mat in FindAll<Material>())
            {
                Shader sh; try { sh = mat.shader; } catch { continue; }
                if (sh == null || !seen.Add(sh.name) || seen.Count > 12) continue;
                int n = 0; try { n = sh.GetPropertyCount(); } catch { }
                w.WriteLine($"  --- '{sh.name}' props={n} renderQueue={Safe(() => sh.renderQueue.ToString())} passes={Safe(() => sh.passCount.ToString())} example material '{mat.name}'");
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        var pname = sh.GetPropertyName(i); var ptype = sh.GetPropertyType(i);
                        string val = "";
                        if (ptype == ShaderPropertyType.Texture) { var tex = mat.GetTexture(pname); val = tex == null ? "null" : $"'{tex.name}' {tex.width}x{tex.height}"; }
                        else if (ptype == ShaderPropertyType.Float || ptype == ShaderPropertyType.Range) val = mat.GetFloat(pname).ToString("G4");
                        else if (ptype == ShaderPropertyType.Color) val = mat.GetColor(pname).ToString();
                        else if (ptype == ShaderPropertyType.Vector) val = mat.GetVector(pname).ToString();
                        else if (ptype == ShaderPropertyType.Int) val = mat.GetInt(pname).ToString();
                        w.WriteLine($"      {ptype,-8} {pname,-40} = {val}   [{Safe(() => sh.GetPropertyDescription(i))}]");
                    }
                    catch (Exception ex) { w.WriteLine($"      <prop {i} threw {ex.GetType().Name}>"); }
                }
                var kw = Safe(() => string.Join(" ", mat.shaderKeywords));
                w.WriteLine($"      keywords: {kw}");
            }
        }

        private static void WalkGameObject(StreamWriter w, Il2CppDump dump, Transform t, string indent, int[] stats)
        {
            stats[0]++;
            var go = t.gameObject;
            var comps = go.GetComponents(Il2CppType.Of<Component>());
            var names = new List<string>();
            WashTarget wt = null; UniqueId uid = null; WashTargetRoot wtr = null; CharacterSpawnPosition spawn = null;
            MeshFilter mf = null; MeshRenderer mr = null;
            foreach (var c in comps)
            {
                if (c == null) { names.Add("<missing script>"); continue; }
                string tn;
                try { tn = c.GetIl2CppType().FullName; } catch { tn = c.GetType().FullName; }
                names.Add(tn.Replace("UnityEngine.", ""));
                wt ??= c.TryCast<WashTarget>();
                uid ??= c.TryCast<UniqueId>();
                wtr ??= c.TryCast<WashTargetRoot>();
                spawn ??= c.TryCast<CharacterSpawnPosition>();
                mf ??= c.TryCast<MeshFilter>();
                mr ??= c.TryCast<MeshRenderer>();
            }
            var layer = LayerMask.LayerToName(go.layer);
            w.WriteLine($"{indent}{go.name}{(go.activeSelf ? "" : " [inactive]")} <{string.Join(", ", names)}> layer={layer} tag={go.tag} pos={t.position} scale={t.lossyScale}");
            var sub = indent + "    ";
            if (uid != null) { stats[2]++; w.WriteLine($"{sub}UniqueId: m_id={uid.m_id} ignorePrefabs={uid.m_ignorePrefabs} FullName={Safe(() => uid.FullName)}"); }
            if (spawn != null) { stats[4]++; dump.Dump(sub + "CharacterSpawnPosition", spawn); }
            if (wtr != null)
            {
                stats[3]++;
                dump.Dump(sub + "WashTargetRoot", wtr);
                var gjr = wtr.TryCast<GameJobWashTargetRoot>();
                if (gjr != null)
                {
                    w.WriteLine($"{sub}GameJobWashTargetRoot: csvObsolete={Safe(() => gjr.m_csvFileObsolete == null ? "null" : gjr.m_csvFileObsolete.name)} csvAsset={Safe(() => Il2CppDump.Summarize(gjr.m_csvFileAsset))} palette={Safe(() => gjr.m_texturePalette == null ? "null" : gjr.m_texturePalette.name)}");
                }
            }
            if (wt != null)
            {
                stats[1]++;
                dump.Dump(sub + "WashTarget", wt);
                if (mf != null) DescribeMesh(w, sub, mf, includeUvBounds: true);
                if (mr != null) DescribeRenderer(w, sub, mr);
            }
            else if (mf != null && mr != null && indent.Length < 8)
            {
                DescribeMesh(w, sub, mf, includeUvBounds: false);
                DescribeRenderer(w, sub, mr);
            }

            for (int i = 0; i < t.childCount; i++)
                WalkGameObject(w, dump, t.GetChild(i), indent + "  ", stats);
        }

        private static void DescribeMesh(StreamWriter w, string indent, MeshFilter mf, bool includeUvBounds)
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) { w.WriteLine($"{indent}Mesh: null"); return; }
            var sb = new StringBuilder();
            sb.Append($"{indent}Mesh: '{mesh.name}' verts={mesh.vertexCount} subMeshes={mesh.subMeshCount} readable={mesh.isReadable}");
            sb.Append(" uv0=").Append(mesh.HasVertexAttribute(VertexAttribute.TexCoord0));
            sb.Append(" uv1=").Append(mesh.HasVertexAttribute(VertexAttribute.TexCoord1));
            sb.Append(" uv2=").Append(mesh.HasVertexAttribute(VertexAttribute.TexCoord2));
            sb.Append(" uv3=").Append(mesh.HasVertexAttribute(VertexAttribute.TexCoord3));
            sb.Append(" color=").Append(mesh.HasVertexAttribute(VertexAttribute.Color));
            sb.Append(" bounds=").Append(mesh.bounds);
            w.WriteLine(sb.ToString());
            if (includeUvBounds && mesh.isReadable)
            {
                w.WriteLine($"{indent}  uv0 bounds: {UvBounds(mesh.uv)}");
                if (mesh.HasVertexAttribute(VertexAttribute.TexCoord1)) w.WriteLine($"{indent}  uv1 (mesh.uv2) bounds: {UvBounds(mesh.uv2)}");
                if (mesh.HasVertexAttribute(VertexAttribute.TexCoord2)) w.WriteLine($"{indent}  uv2 (mesh.uv3) bounds: {UvBounds(mesh.uv3)}");
            }
        }

        private static string UvBounds(Il2CppStructArray<Vector2> uvs)
        {
            if (uvs == null || uvs.Length == 0) return "none";
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var uv in uvs)
            {
                if (uv.x < minX) minX = uv.x;
                if (uv.y < minY) minY = uv.y;
                if (uv.x > maxX) maxX = uv.x;
                if (uv.y > maxY) maxY = uv.y;
            }
            return $"n={uvs.Length} min=({minX:F3},{minY:F3}) max=({maxX:F3},{maxY:F3})";
        }

        private static void DescribeRenderer(StreamWriter w, string indent, MeshRenderer mr)
        {
            var mats = mr.sharedMaterials;
            var parts = new List<string>();
            foreach (var m in mats)
            {
                if (m == null) { parts.Add("null"); continue; }
                string shader = Safe(() => m.shader == null ? "null" : m.shader.name);
                parts.Add($"'{m.name}' [{shader}] rq={Safe(() => m.renderQueue.ToString())}");
            }
            w.WriteLine($"{indent}Renderer: materials={mats.Length} {string.Join(" | ", parts)} lightmapIndex={mr.lightmapIndex} probes={mr.lightProbeUsage}");
        }

        // =============================================================== F8: game data

        private static void DumpGameData(string pathBase)
        {
            using (var w = new StreamWriter(pathBase + ".txt", false, Encoding.UTF8))
            {
                Header(w, "GAME DATA");
                w.WriteLine($"Addressables.RuntimePath = {Safe(() => Addressables.RuntimePath)}");
                w.WriteLine($"Addressables.Version     = {Safe(() => Addressables.Version)}");
                w.WriteLine($"UserData                 = {MelonEnvironment.UserDataDirectory}");
                w.WriteLine($"GameRoot                 = {MelonEnvironment.GameRootDirectory}");
                w.WriteLine();

                var loadedScenes = new HashSet<string>();
                for (int i = 0; i < SceneManager.sceneCount; i++) loadedScenes.Add(SceneManager.GetSceneAt(i).name);
                w.WriteLine("Loaded scenes: " + string.Join(", ", loadedScenes));

                w.WriteLine();
                w.WriteLine("=== LOADED OBJECT COUNTS");
                Count<GameDataManifest>(w); Count<Campaign>(w); Count<JobPackManifest>(w); Count<GameJob>(w); Count<GameJobCustomer>(w);
                Count<ContentPackManifest>(w); Count<DLCIDMapping>(w); Count<SceneWashableData>(w); Count<WashTargetData>(w);
                Count<TexturePalette>(w); Count<TextureLayer>(w); Count<WashRenderSettings>(w); Count<WashRenderProfile>(w);
                Count<PaintOpacityConfig>(w); Count<StringTable>(w); Count<SharedTableData>(w);

                var deep = new Il2CppDump(w, maxDepth: 7, maxElems: 48);
                foreach (var n in new[] { "GameJob", "SceneWashableData", "WashTargetData", "TexturePalette", "MessageSequenceData", "PowerWasherData", "TrophyData", "GameJobCustomer" })
                    deep.ShallowTypes.Add(n);

                w.WriteLine(); w.WriteLine("=== GAME DATA MANIFEST(S)");
                foreach (var m in FindAll<GameDataManifest>()) deep.Dump("GameDataManifest", m);

                w.WriteLine(); w.WriteLine("=== CAMPAIGNS");
                foreach (var c in FindAll<Campaign>()) deep.Dump("Campaign", c);

                w.WriteLine(); w.WriteLine("=== CONTENT PACK MANIFESTS");
                foreach (var c in FindAll<ContentPackManifest>()) deep.Dump("ContentPackManifest", c);
                foreach (var c in FindAll<DLCIDMapping>()) deep.Dump("DLCIDMapping", c);

                w.WriteLine(); w.WriteLine("=== JOB PACK MANIFESTS");
                foreach (var m in FindAll<JobPackManifest>()) deep.Dump("JobPackManifest", m);

                w.WriteLine(); w.WriteLine("=== ALL LOADED GAME JOBS (one line each)");
                var jobs = FindAll<GameJob>().ToList();
                GameJob current = null;
                foreach (var j in jobs)
                {
                    string sceneName = Safe(() => j.Location?.SceneName);
                    if (sceneName != null && loadedScenes.Contains(sceneName)) current = j;
                    w.WriteLine($"  {j.name,-40} uniqueName={Safe(() => j.UniqueName),-28} id={Safe(() => j.UniqueId.ToString()),-12} campaign={Safe(() => j.CampaignID.ToString()),-12} scene={sceneName,-32} sceneRef={Safe(() => j.Location?.SceneAssetReference?.AssetGUID)} swd={Safe(() => j.Location?.SceneWashableData?.name)} save={Safe(() => j.SaveSize.ToString())} stage={Safe(() => j.LevelDevelopmentStage.ToString())} unlockedByDefault={Safe(() => j.UnlockedByDefault.ToString())} test={Safe(() => j.IsTestJob.ToString())} stringTable={Safe(() => j.StringTableName)} locId={Safe(() => j.LocalizationId)}");
                }

                w.WriteLine(); w.WriteLine("=== CURRENT JOB (full)");
                if (current == null) w.WriteLine("  (no loaded GameJob matches a loaded scene name; you are probably in the hub)");
                else
                {
                    var full = new Il2CppDump(w, maxDepth: 10, maxElems: 400);
                    full.Dump("GameJob", current);
                    w.WriteLine();
                    var swd = Safe(() => current.Location?.SceneWashableData);
                    if (swd != null) full.Dump("SceneWashableData", swd);
                }

                w.WriteLine(); w.WriteLine("=== WASH TARGET DATA (all loaded)");
                var mid = new Il2CppDump(w, maxDepth: 3, maxElems: 32);
                foreach (var d in FindAll<WashTargetData>()) mid.Dump("WashTargetData", d);

                w.WriteLine(); w.WriteLine("=== TEXTURE PALETTES / LAYERS / RENDER SETTINGS / OPACITY CONFIGS");
                foreach (var d in FindAll<TexturePalette>()) mid.Dump("TexturePalette", d);
                foreach (var d in FindAll<TextureLayer>()) mid.Dump("TextureLayer", d);
                foreach (var d in FindAll<WashRenderSettings>()) mid.Dump("WashRenderSettings", d);
                foreach (var d in FindAll<WashRenderProfile>()) w.WriteLine($"  WashRenderProfile '{d.name}'");
                foreach (var d in FindAll<PaintOpacityConfig>()) w.WriteLine($"  PaintOpacityConfig '{d.name}'");

                w.WriteLine(); w.WriteLine("=== CHEAT MENU BUTTONS (category | name | type | enabled)");
                var cm = FindCheatManager();
                if (cm == null) w.WriteLine("  CheatManager not present (install/enable the cheat menu, e.g. CheatMenuEnabler, and open it once)");
                else
                {
                    w.WriteLine($"  CheatManager on '{Safe(() => cm.gameObject.name)}' initialised={Safe(() => CheatManager.Initialised.ToString())} accessible={Safe(() => cm.Accessible.ToString())} settings={Safe(() => cm.InstanceSettings?.name)}");
                    foreach (var (cat, d) in EnumerateCheats(cm))
                        w.WriteLine($"  {cat,-14} | {Safe(() => d.Name),-60} | {Safe(() => d.CheatButtonType.ToString()),-13} | {Safe(() => d.Enabled == null ? "n/a" : d.Enabled.Invoke().ToString())}");
                }

                w.WriteLine(); w.WriteLine("=== LOCALIZATION");
                w.WriteLine($"  SelectedLocale = {Safe(() => LocalizationSettings.SelectedLocale.Identifier.Code)}");
                w.WriteLine($"  ProjectLocale  = {Safe(() => LocalizationSettings.ProjectLocale.Identifier.Code)}");
                w.WriteLine($"  StringDatabase.DefaultTable = {Safe(() => LocalizationSettings.StringDatabase.DefaultTable.TableCollectionName)}");
                foreach (var st in FindAll<StringTable>())
                    w.WriteLine($"  StringTable '{st.name}' collection={Safe(() => st.TableCollectionName)} locale={Safe(() => st.LocaleIdentifier.Code)} entries={Safe(() => st.Count.ToString())} shared={Safe(() => st.SharedData?.name)}");
                foreach (var sd in FindAll<SharedTableData>())
                    w.WriteLine($"  SharedTableData '{sd.name}' collection={Safe(() => sd.TableCollectionName)} entries={Safe(() => sd.Entries.Count.ToString())}");
            }

        }

        // =============================================================== F9: Addressables keys (separate: touches the binary catalog reader)

        private static void DumpAddressables(string pathBase)
        {
            using var w = new StreamWriter(pathBase + ".tsv", false, Encoding.UTF8);
            w.WriteLine("locator\tprimaryKey\tinternalId\tresourceType\tproviderId\tdependencies");
            int rows = 0;
            foreach (var loc in Iter(Addressables.ResourceLocators))
            {
                if (loc == null) continue;
                string locId = Safe(() => loc.LocatorId);
                string locType = Safe(() => loc.TryCast<Il2CppSystem.Object>()?.GetIl2CppType().FullName);
                w.WriteLine($"# locator {locId} ({locType})");
                w.Flush();
                foreach (var l in Iter(loc.AllLocations))
                {
                    if (l == null) continue;
                    int deps = 0; try { deps = l.HasDependencies ? Il2Count(l.Dependencies) : 0; } catch { }
                    w.WriteLine($"{locId}\t{Safe(() => l.PrimaryKey)}\t{Safe(() => l.InternalId)}\t{Safe(() => l.ResourceType?.FullName)}\t{Safe(() => l.ProviderId)}\t{deps}");
                    rows++;
                    if ((rows & 255) == 0) w.Flush();
                }
            }
            Core.Log.Msg($"Addressables locations written: {rows}");
        }

        // =============================================================== F6: control maps

        private static void DumpControlMaps(string pathBase)
        {
            using var w = new StreamWriter(pathBase + ".txt", false, Encoding.UTF8);
            Header(w, "CONTROL MAPS");
            var targets = new List<WashTarget>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null) continue;
                    foreach (var c in root.GetComponentsInChildren(Il2CppType.Of<WashTarget>(), true))
                    {
                        var wt = c?.TryCast<WashTarget>();
                        if (wt != null) targets.Add(wt);
                    }
                }
            }
            w.WriteLine($"WashTargets in loaded scenes: {targets.Count}");
            w.WriteLine();

            const int maxPngs = 8;
            int pngs = 0;
            foreach (var wt in targets)
            {
                var name = Sanitize(wt.gameObject.name);
                w.WriteLine($"--- {wt.gameObject.name} uniqueId={Safe(() => wt.UniqueId.ToString())} washIndex={Safe(() => wt.WashIndex.ToString())} loaded={Safe(() => wt.IsLoaded.ToString())}");
                w.WriteLine($"    start={Safe(() => wt.StartDirtiness.ToString())} current={Safe(() => wt.CurrentDirtiness.ToString())} clean={Safe(() => wt.CleanProgress.ToString("F4"))} normalized={Safe(() => wt.NormalizedDirtiness.ToString("F4"))} soap={Safe(() => wt.SoapCount.ToString())}");
                w.WriteLine($"    WashMapRectTx={Safe(() => wt.WashMapRectTx.ToString())} WashMapRTDimensions={Safe(() => wt.WashMapRTDimensions.ToString())} WashMapRTFormat={Safe(() => wt.WashMapRTGraphicsFormat.ToString())}");
                w.WriteLine($"    ControlMapPNGResolution={Safe(() => wt.ControlMapPNGResolution.ToString())} PaintedRect={Safe(() => wt.ControlMapPaintedRect.ToString())} TrimRect={Safe(() => wt.ControlMapPNGTrimRect.ToString())} TrimRectUV={Safe(() => wt.ControlMapPNGTrimRectUV.ToString())}");
                var data = wt.m_washTargetData;
                if (data != null)
                    w.WriteLine($"    Data '{data.name}': suggestedRes={Safe(() => data.SuggestedControlMapResolution.ToString())} areaInUse={Safe(() => data.ControlMapAreaInUse.ToString())} totalRect={Safe(() => data.ControlMapTotalRect.ToString())} trimmed={Safe(() => data.ControlMapTrimmedRect.ToString())} controlMap={Safe(() => Il2CppDump.Summarize(data.ControlMapAsset))} palette={Safe(() => data.TexturePalette?.name)} threshold={Safe(() => data.SurfaceCleanThreshold.ToString())} override={Safe(() => data.OverrideSurfaceCleanThreshold.ToString())} templateMat={Safe(() => data.TemplatePaintLayerMaterial?.name)} opacity={Safe(() => data.OpacityCurveConfig?.name)}");

                Texture2D cm = null;
                try { cm = wt.RuntimeControlMap; } catch (Exception ex) { w.WriteLine($"    RuntimeControlMap threw {ex.GetType().Name}"); }
                if (cm == null)
                {
                    try { cm = data?.ControlMapAsset?.Asset?.TryCast<Texture2D>(); } catch { }
                }
                if (cm == null) { w.WriteLine("    control map texture: null"); }
                else
                {
                    w.WriteLine($"    control map texture: '{cm.name}' {cm.width}x{cm.height} format={cm.format} gfx={cm.graphicsFormat} mips={cm.mipmapCount} readable={cm.isReadable}");
                    if (pngs < maxPngs)
                    {
                        var file = $"{pathBase}_{pngs:00}_{name}_control.png";
                        try { File.WriteAllBytes(file, ReadbackPng(cm)); w.WriteLine($"    -> {Path.GetFileName(file)}"); }
                        catch (Exception ex) { w.WriteLine($"    control map readback failed: {ex.Message}"); }
                        if (pngs < 2)
                        {
                            TryDumpRt(w, wt.WashMapRT, $"{pathBase}_{pngs:00}_{name}_washmap.png", "wash map");
                            TryDumpRt(w, wt.EffectMapRT, $"{pathBase}_{pngs:00}_{name}_effectmap.png", "effect map");
                        }
                        pngs++;
                    }
                }
            }

            w.WriteLine();
            w.WriteLine("=== WASH TARGET ROOT CSV");
            foreach (var root in FindAll<GameJobWashTargetRoot>())
            {
                if (root.gameObject == null || !root.gameObject.scene.IsValid()) continue;
                w.WriteLine($"GameJobWashTargetRoot on '{root.gameObject.name}' uniqueId={Safe(() => root.UniqueId.ToString())} palette={Safe(() => root.m_texturePalette?.name)}");
                string csv = null;
                try { if (root.m_csvFileObsolete != null) csv = root.m_csvFileObsolete.text; } catch { }
                if (csv == null)
                {
                    try
                    {
                        var asset = root.m_csvFileAsset?.Asset?.TryCast<TextAsset>();
                        if (asset == null && root.m_csvFileAsset != null && root.m_csvFileAsset.RuntimeKeyIsValid())
                        {
                            var handle = root.m_csvFileAsset.LoadAssetAsync();
                            asset = handle.WaitForCompletion();
                        }
                        csv = asset?.text;
                    }
                    catch (Exception ex) { w.WriteLine($"  csv load failed: {ex.Message}"); }
                }
                if (csv != null)
                {
                    var file = $"{pathBase}_{Sanitize(root.gameObject.name)}_washtargets.csv";
                    File.WriteAllText(file, csv, Encoding.UTF8);
                    w.WriteLine($"  -> {Path.GetFileName(file)} ({csv.Length} chars, {csv.Split('\n').Length} lines)");
                }
                else w.WriteLine("  csv: none");
            }
        }

        private static void TryDumpRt(StreamWriter w, RenderTexture rt, string file, string label)
        {
            try
            {
                if (rt == null) { w.WriteLine($"    {label}: null"); return; }
                File.WriteAllBytes(file, ReadbackPng(rt));
                w.WriteLine($"    -> {Path.GetFileName(file)} ({rt.width}x{rt.height} {rt.graphicsFormat})");
            }
            catch (Exception ex) { w.WriteLine($"    {label} readback failed: {ex.Message}"); }
        }

        private static byte[] ReadbackPng(Texture src) => ReadbackPng(src, new Rect(0, 0, src.width, src.height), srgb: false);

        /// <summary>PNG of <paramref name="rect"/> (texels) of any texture, readable or not; srgb for colour images such as sprites.</summary>
        private static byte[] ReadbackPng(Texture src, Rect rect, bool srgb)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                tex = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false, !srgb);
                tex.ReadPixels(rect, 0, 0);
                tex.Apply(false);
                var bytes = ImageConversion.EncodeToPNG(tex);
                return bytes.ToArray();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null) Object.Destroy(tex);
            }
        }

        // =============================================================== cheat menu access

        private static CheatManager FindCheatManager()
        {
            CheatManager cm = null;
            try { if (CheatManager.HasInstance) cm = CheatManager.PeekInstance; } catch { }
            if (cm == null)
            {
                foreach (var c in FindAll<CheatManager>())
                {
                    try { if (c.gameObject != null && c.gameObject.scene.IsValid()) { cm = c; break; } } catch { }
                }
            }
            return cm;
        }

        private static IEnumerable<(CheatCategory cat, CheatData data)> EnumerateCheats(CheatManager cm)
        {
            var dict = cm.m_cheatsByCategory;
            if (dict == null) yield break;
            foreach (CheatCategory cat in Enum.GetValues(typeof(CheatCategory)))
            {
                Il2CppSystem.Collections.Generic.List<CheatData> list = null;
                try { if (dict.ContainsKey(cat)) list = dict[cat]; } catch { }
                if (list == null) continue;
                int n = list.Count;
                for (int i = 0; i < n; i++)
                {
                    var d = list[i];
                    if (d != null) yield return (cat, d);
                }
            }
        }

        /// <summary>Invoke a cheat-menu button by display name (case-insensitive). Accepts alternatives separated by '|'.</summary>
        public static bool TryRunCheat(string name)
        {
            var cm = FindCheatManager();
            if (cm == null) { Core.Log.Warning("CheatManager not found"); return false; }
            var wanted = new HashSet<string>(name.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (var (cat, d) in EnumerateCheats(cm))
            {
                string n = Safe(() => d.Name);
                if (!wanted.Contains(n)) continue;
                var fn = d.CheatFunction;
                if (fn == null) { Core.Log.Warning($"Cheat '{name}' ({cat}) has no function"); return false; }
                Core.Log.Msg($"Invoking cheat '{n}' ({cat})");
                fn.Invoke();
                return true;
            }
            return false;
        }

        // =============================================================== helpers

        /// <summary>foreach over an IL2CPP IEnumerable&lt;T&gt;: the generic enumerator proxy lacks MoveNext, so cast to the non-generic one.</summary>
        private static IEnumerable<T> Iter<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> src) where T : class
        {
            if (src == null) yield break;
            var e = src.GetEnumerator();
            var baseEnumerator = e.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (baseEnumerator.MoveNext()) yield return e.Current;
        }

        /// <summary>Count of an IL2CPP IList&lt;T&gt; (Count lives on ICollection&lt;T&gt;, which the IList proxy does not inherit).</summary>
        private static int Il2Count<T>(Il2CppSystem.Collections.Generic.IList<T> list)
            => list == null ? 0 : list.Cast<Il2CppSystem.Collections.Generic.ICollection<T>>().Count;

        private static void Header(StreamWriter w, string title)
        {
            w.WriteLine($"# PWS2LevelLoader dump: {title}");
            w.WriteLine($"# {DateTime.Now:yyyy-MM-dd HH:mm:ss} | game {Application.version} | Unity {Application.unityVersion} | active scene {SceneManager.GetActiveScene().name}");
        }

        private static IEnumerable<T> FindAll<T>() where T : Object
        {
            var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>());
            var list = new List<T>();
            foreach (var o in arr)
            {
                if (o == null) continue;
                var t = o.TryCast<T>();
                if (t != null) list.Add(t);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        private static void Count<T>(StreamWriter w) where T : Object
        {
            int n;
            try { n = FindAll<T>().Count(); }
            catch (Exception ex) { w.WriteLine($"  {typeof(T).Name}: <threw {ex.GetType().Name}>"); return; }
            w.WriteLine($"  {typeof(T).Name}: {n}");
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? "null"; } catch (Exception ex) { return $"<{ex.GetType().Name}>"; }
        }

        private static T Safe<T>(Func<T> f) where T : class
        {
            try { return f(); } catch { return null; }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s) sb.Append(Array.IndexOf(invalid, ch) >= 0 || ch == ' ' ? '_' : ch);
            return sb.ToString();
        }
    }
}
