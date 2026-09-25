// OKC Transformer Screen: the real level, built the way SpikeCPack builds its one panel, from the Blender output in
// <repo>/OKC_TransformerScreen/export (split build: OKC_Panel_01..14 + OKC_Screen_Plinth, one DirtMask atlas each)
// and the control maps from build_okc_dirtmaps.py (export/dirt/CM_<object>.png).
//
// Pack "OKC_TransformerScreen": scene GAM_OKC_TransformerScreen with a ground plane, GameplayBoot, a root
// (UniqueId + GameJobWashTargetRoot + wash CSV) holding 15 wash targets, a spawn position; one WashTargetData per
// target, two TexturePalettes of the game's dirt layers (panels, curb), SceneWashableData, one GameJob with one stage,
// one task and 15 wash tasks, JobPackManifest and pack.json.
//
// Mesh UVs after import: uv = UVMap (base texture), uv2 = DirtMask. The game samples the control map in TexCoord2
// and expects TexCoord1 = the same UVs plus an integer cell offset unique per target (docs/phase0-findings.md §5),
// so each mesh is copied into a .asset with uv3 = DirtMask and uv2 = DirtMask + (i % 10, i / 10).
//
// ImportRing / BuildRoot / BuildSceneWashableData / BuildStage are shared with OkcStreetcarPack, which authors the same
// ring for a game scene (different scene name for the ids, cells after the host scene's, "_Streetcar" asset names).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FuturLab;
using FuturLab.PW2;
using FuturLab.PW2.TexturePainter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Rendering;

namespace PWS2LevelKit.Editor
{
    public static class OkcPack
    {
        public const string Pack = "OKC_TransformerScreen";
        public const string JobUniqueName = "OKC_TRANSFORMER_SCREEN";
        public const string SceneName = "GAM_OKC_TransformerScreen";
        public const string RootName = "PWS_OKC_Root";
        public const string Table = "PWS2_Main";
        public const string SourceFolder = "Assets/LevelKitSource/OKC_TransformerScreen";
        const int CellsPerRow = 10;

        // Game assets borrowed by GUID. Dirt layers (TextureLayer) carry the game's DirtTier, which sets how hard each
        // nozzle finds them (docs/phase0-findings.md section 6); the palette order must match build_okc_dirtmaps.py.
        const string BillboardShaderCollectionGuid = "9217f14022624204f9ea2c09bc96483e";
        const string LayerPaintedFlakesRust = "94e1048e653014b4ba6be4dc12b40a37"; // Tier 6 Ingrained (the Tram's doors)
        const string LayerGrime = "acca5075491c9884aa71244dd1b7e040";             // Tier 1
        const string LayerMudSplats = "5e7adb63034f5554385adf5e2002a2af";         // Tier 1, volumetric
        const string LayerGrimeGreenSlimy = "58956cc2a4338fd4cb7913869c50e06f";   // Tier 1
        const string LayerSandDarker = "a4897b21ac3656848a2501a52ed0a3e8";        // Tier 1
        const string LayerCrackedMud = "47b3c72e2b644a746acd9cfd0dc38642";        // Tier 3 Embedded
        internal const string CustomerPortraitGuid = "844fa52259e94314187ba2cc456ad0ca";

        internal static readonly string BlenderExport = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "OKC_TransformerScreen", "export"));

        internal class Target
        {
            public string Name, Label, Scene;
            public bool Plinth;
            public Mesh Mesh;
            public GameObject Go;
            public int Id, Res, StartDirtiness, Cell;
            public WashTargetData Data;
            public string WashKey => $"WASHTASK_{Scene.ToUpperInvariant()}_WASH_{Name.ToUpperInvariant()}";
        }

        /// <summary>The ring's wash data, authored for one scene: targets (meshes, control maps, WashTargetData), palette, CSV and materials.</summary>
        internal class Ring
        {
            public List<Target> Targets;
            public TexturePalette Palette, CurbPalette;
            public string CsvGuid;
            public Material Metal, Concrete;
        }

        [MenuItem("PWS2/OKC Transformer Screen: create pack + build")]
        public static void CreateAndBuild() { Create(); PackBuilder.Build(Pack, PackBuilder.DefaultDist); }

        public static void CreateAndBuildFromCommandLine()
        {
            try { Create(); EditorApplication.Exit(PackBuilder.Build(Pack, PackBuilder.DefaultDist) ? 0 : 1); }
            catch (System.Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void Create()
        {
            var folder = $"{PackBuilder.PacksRoot}/{Pack}";
            var ring = ImportRing(folder, SceneName, 0, "");
            EnsureFolder(folder + "/Scenes");

            // --- scene
            // empty scene: no default Main Camera / AudioListener competing with the player's camera
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var south = OkcEnvironment.BuildPlaza(BlenderExport, SourceFolder, $"{folder}/Data");

            GameplayBoot.Create();

            var root = BuildRoot(ring, SceneName);
            var rootId = root.GetComponent<UniqueId>().m_id;
            var targets = ring.Targets;
            var ringBounds = targets.Select(t => t.Go.GetComponent<MeshRenderer>().bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });

            var spawn = new GameObject("CharacterSpawnPosition");
            spawn.layer = 23; // SpawnPositions
            spawn.transform.SetPositionAndRotation(south * 7.5f, Quaternion.LookRotation(-south)); // on the paving, street side, facing the ring
            var sp = spawn.AddComponent<CharacterSpawnPosition>(); sp.m_type = CharacterSpawnPositionType.Main;
            var cap = spawn.AddComponent<CapsuleCollider>(); cap.radius = 0.5f; cap.height = 1.4f; cap.center = new Vector3(0, 0.7f, 0); cap.isTrigger = true;

            var probe = OkcEnvironment.BuildLighting($"{folder}/Data", south);

            var scenePath = $"{folder}/Scenes/{SceneName}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            OkcEnvironment.BakeReflections(probe, $"{folder}/Data");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            var sceneGuid = AssetDatabase.AssetPathToGUID(scenePath);
            var ui = OkcThumbnails.Render(folder, south);

            var swd = BuildSceneWashableData($"{folder}/Data/SWD_OKC.asset", ring, root.transform, SceneName);

            // --- customer
            var customer = LoadOrCreate<GameJobCustomer>($"{folder}/Data/Customer_OKC.asset");
            customer.m_uniqueName = "OKCCUSTOMER";
            customer.m_uniqueId = Pws2Hash.String(customer.m_uniqueName);
            customer.m_unlockedByDefault = true;
            customer.m_portraitAsset = new AssetReferenceSprite(CustomerPortraitGuid);
            customer.m_localizedName = new LocalizedString(Table, "CUSTOMER_CAREER_OKCCUSTOMER_TITLE");
            customer.m_localizedDescription = new LocalizedString(Table, "CUSTOMER_CAREER_OKCCUSTOMER_DESCRIPTION");
            EditorUtility.SetDirty(customer);

            // --- job
            var job = LoadOrCreate<GameJob>($"{folder}/Data/Job_OKC_TransformerScreen.asset");
            job.m_uniqueName = JobUniqueName;
            job.m_uniqueId = Pws2Hash.String(JobUniqueName);
            job.CampaignID = Pws2Hash.String("DEFAULT");
            job.m_unlockedByDefault = true;
            job.m_saveSize = SaveSize.Small;
            job.m_levelDevelopmentStage = DevelopmentStage.ProductionReady;
            job.m_customer = customer;
            job.m_completionPayment = new PrimaryCurrencyValue { Value = 2500 };
            job.m_completionPaymentSecondary = new MuckCoinValue { Value = 150 };
            job.m_stringTableName = Table;
            job.m_localizedName = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_TITLE");
            job.m_localizedDescription = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_DESCRIPTION");
            job.m_freePlayNameInteraction = new LocalizedString(Table, $"HOMEBASE_{JobUniqueName}_FREEPLAY");
            job.m_storyNameInteraction = new LocalizedString(Table, $"HOMEBASE_{JobUniqueName}_STORY");
            job.m_timelapseStaticThumbnail = new AssetReferenceSprite(ui[OkcThumbnails.Timelapse]);
            var loc = job.Location;
            loc.SceneWashableData = swd;
            loc.SceneName = SceneName;
            loc.SceneAssetReference = new AssetReference(sceneGuid);
            loc.SceneShaderCollectionAsset = new FuturLab.Addressables.AssetReferenceShaderVariantCollection(BillboardShaderCollectionGuid);
            loc.LocalizedAddress = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_ADDRESS");
            loc.LocalizedLoadingInfo = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_DESCRIPTION");
            loc.LoadingScreenImageAsset = new AssetReferenceSprite(ui[OkcThumbnails.Loading]);
            loc.PreviewImageAsset = new AssetReferenceSprite(ui[OkcThumbnails.Preview]);
            loc.ChatImageAsset = new AssetReferenceSprite(ui[OkcThumbnails.Chat]);
            loc.SelectedThumbnailImageAsset = new AssetReferenceSprite(ui[OkcThumbnails.ThumbA]);
            loc.DeselectedThumbnailImageAsset = new AssetReferenceSprite(ui[OkcThumbnails.ThumbB]);
            loc.AmbienceSoundEvent = new FMODUnity.EventReference { Guid = FMOD.GUID.Parse("{afbe6293-29cd-4e2a-843b-e533fc242c16}") };
            loc.MaxMapHeight = 6f; loc.HasWind = false;

            job.m_stages = new[] { BuildStage(ring, rootId, JobUniqueName, "OKC_SCREEN_TASK") };
            EditorUtility.SetDirty(job);

            var manifestPath = $"{folder}/Data/JobPackManifest_OKC.asset";
            var manifest = LoadOrCreate<JobPackManifest>(manifestPath);
            manifest.m_gameJobs.Clear(); manifest.m_gameJobs.Add(job);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();

            // --- pack.json
            var manifestGuid = AssetDatabase.AssetPathToGUID(manifestPath);
            var strings = new List<(string, string)>
            {
                ("CUSTOMER_CAREER_OKCCUSTOMER_TITLE", "Downtown Utilities"),
                ("CUSTOMER_CAREER_OKCCUSTOMER_DESCRIPTION", "Keeps the lights on downtown."),
                ($"TASK_{JobUniqueName}_MAIN", "Transformer Screen"),
            };
            strings.AddRange(WashTaskStrings(ring));
            var sb = new StringBuilder();
            sb.Append("{\n  \"id\": \"OKC_TransformerScreen\",\n  \"displayName\": \"OKC Transformer Screen\",\n  \"version\": \"0.1.0\",\n  \"gameVersion\": \"" + PackBuilder.GameVersion + "\",\n  \"catalog\": \"catalog.bin\",\n");
            sb.Append($"  \"jobPackManifestGuid\": \"{manifestGuid}\",\n  \"jobs\": [ {{\n    \"uniqueName\": \"{JobUniqueName}\", \"uniqueId\": {job.m_uniqueId}, \"sceneName\": \"{SceneName}\",\n");
            sb.Append("    \"title\": \"OKC Transformer Screen\", \"address\": \"NW 4th & Robinson, Oklahoma City\",\n");
            sb.Append("    \"description\": \"A perforated steel screen ringed around a downtown transformer, stencilled with O, K and C.\",\n");
            sb.Append("    \"freePlayName\": \"OKC Transformer Screen (Free Play)\", \"storyName\": \"OKC Transformer Screen\",\n");
            sb.Append($"    \"thumbnailSelectedGuid\": \"{ui[OkcThumbnails.ThumbA]}\", \"thumbnailDeselectedGuid\": \"{ui[OkcThumbnails.ThumbB]}\"\n  }} ],\n");
            sb.Append("  \"strings\": {\n");
            sb.Append(string.Join(",\n", strings.Select(s => $"    \"{s.Item1}\": \"{s.Item2}\"")));
            sb.Append("\n  }\n}\n");
            File.WriteAllText(Abs($"{folder}/pack.json"), sb.ToString());
            AssetDatabase.Refresh();

            Debug.Log($"OkcPack: job {job.m_uniqueName} id {job.m_uniqueId}, root {rootId}, manifest {manifestGuid}, ring bounds {ringBounds.center} size {ringBounds.size}\n" + Describe(ring));
        }

        // ------------------------------------------------------------------ shared with OkcStreetcarPack

        /// <summary>
        /// Imports the Blender export and writes this pack's copy of the wash data: one mesh per target whose
        /// TexCoord1 cells start at <paramref name="cellBase"/> (must not collide with the host scene's cells),
        /// control maps, WashTargetData, palette, CSV, materials. <paramref name="tag"/> is appended to every asset
        /// name so two packs never ship assets with the same Addressables address ("" keeps the OKC names).
        /// </summary>
        internal static Ring ImportRing(string folder, string sceneName, int cellBase, string tag)
        {
            foreach (var f in new[] { SourceFolder, folder, folder + "/Data", folder + "/Meshes", folder + "/Textures" }) EnsureFolder(f);

            // --- source model (outside the pack folder, so the FBX itself never ships)
            var fbxPath = $"{SourceFolder}/OKC_TransformerScreen.fbx";
            File.Copy(Path.Combine(BlenderExport, "OKC_TransformerScreen.fbx"), Abs(fbxPath), true);
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            mi.globalScale = 1f; mi.useFileScale = true;
            mi.generateSecondaryUV = false;              // uv2 is our DirtMask; never let Unity replace it
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.weldVertices = false;
            mi.optimizeMeshVertices = false; mi.optimizeMeshPolygons = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false; mi.importLights = false;
            mi.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) throw new System.Exception($"OkcPack: {fbxPath} did not import");

            var names = Enumerable.Range(1, 14).Select(i => $"OKC_Panel_{i:00}").Append("OKC_Screen_Plinth").ToList();
            var targets = new List<Target>();
            for (int i = 0; i < names.Count; i++)
            {
                var src = model.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.name == names[i]);
                if (src == null) throw new System.Exception($"OkcPack: {names[i]} missing from the FBX (was the Blender build split?)");
                targets.Add(new Target
                {
                    Name = names[i], Scene = sceneName, Cell = cellBase + i, Plinth = names[i].EndsWith("Plinth"),
                    Label = names[i].EndsWith("Plinth") ? "Curb" : $"Panel {i + 1}",
                    Mesh = CopyMeshWithWashUVs(src.sharedMesh, cellBase + i, $"{folder}/Meshes/Mesh_{names[i]}{tag}.asset"),
                    Go = src.gameObject,
                });
            }

            // --- palettes: rust flakes under grime and mud on the steel; slimy grime, sand and cracked mud on the curb
            var palette = MakePalette($"{folder}/Data/TPL_OKC{tag}.asset", LayerPaintedFlakesRust, LayerGrime, LayerMudSplats);
            var curbPalette = MakePalette($"{folder}/Data/TPL_OKC_Curb{tag}.asset", LayerGrimeGreenSlimy, LayerSandDarker, LayerCrackedMud);

            // --- control maps + wash target data
            foreach (var t in targets)
            {
                var pngPath = $"{folder}/Textures/CM_{t.Name}{tag}.png";
                var srcPng = Path.Combine(BlenderExport, "dirt", $"CM_{t.Name}.png");
                if (!File.Exists(srcPng)) throw new System.Exception($"OkcPack: {srcPng} missing; run build_okc_dirtmaps.py");
                File.Copy(srcPng, Abs(pngPath), true);
                AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
                ConfigureControlMap(pngPath);
                (t.Res, t.StartDirtiness) = CountDirt(Abs(pngPath));

                var wtd = LoadOrCreate<WashTargetData>($"{folder}/Data/WTD_{t.Name}{tag}.asset");
                wtd.SuggestedControlMapResolution = t.Res;
                wtd.ControlMapAsset = new AssetReferenceTexture2D(AssetDatabase.AssetPathToGUID(pngPath));
                wtd.ControlMapAreaInUse = new RectInt(0, 0, t.Res, t.Res);
                wtd.ControlMapTrimmedRect = new RectInt(0, 0, t.Res, t.Res);
                wtd.ControlMapTotalRect = new Rect(0, 0, 0, 0);
                wtd.TexturePalette = t.Plinth ? curbPalette : palette;
                wtd.OverrideSurfaceCleanThreshold = true;
                wtd.SurfaceCleanThreshold = 0.1f;
                wtd.ConfirmedByDesigner = true;
                EditorUtility.SetDirty(wtd);
                t.Data = wtd;
            }

            // --- wash CSV (version-2 layout table; one row per target with its TexCoord1 cell)
            var csvPath = $"{folder}/Data/{RootName}{tag}_washtargets.csv";
            var csv = new StringBuilder("CSV VERSION 2\nNODE NAME,TEXTURE SIZE,X OFFSET,Y OFFSET,UNIQUEID,UVSPACE\n");
            foreach (var t in targets) csv.Append($"{t.Name},{t.Res} x {t.Res},{t.Cell % CellsPerRow},{t.Cell / CellsPerRow},{t.Cell},100%\n");
            File.WriteAllText(Abs(csvPath), csv.ToString());
            AssetDatabase.ImportAsset(csvPath, ImportAssetOptions.ForceUpdate);

            return new Ring
            {
                Targets = targets, Palette = palette, CurbPalette = curbPalette, CsvGuid = AssetDatabase.AssetPathToGUID(csvPath),
                Metal = MakeMaterial($"{folder}/Data/MAT_OKC_Metal{tag}.mat", new Color(0.40f, 0.41f, 0.43f), 0.55f, 0.45f),
                Concrete = MakeMaterial($"{folder}/Data/MAT_OKC_Concrete{tag}.mat", new Color(0.62f, 0.60f, 0.56f), 0f, 0.1f),
            };
        }

        /// <summary>
        /// Builds the wash-target root (UniqueId + GameJobWashTargetRoot + CSV) with the 15 targets as children at the
        /// FBX positions, in the active scene; ids are hashed from <paramref name="sceneName"/> and the root-relative path.
        /// </summary>
        internal static GameObject BuildRoot(Ring ring, string sceneName)
        {
            var root = new GameObject(RootName);
            root.layer = 8; // Washable
            var rootUid = root.AddComponent<UniqueId>();
            var gjRoot = root.AddComponent<GameJobWashTargetRoot>();
            gjRoot.m_uniqueId = rootUid;
            gjRoot.m_csvFileAsset = new AssetReferenceT<TextAsset>(ring.CsvGuid);
            gjRoot.m_texturePalette = ring.Palette;
            rootUid.m_id = Pws2Hash.ObjectId(sceneName, root.transform);

            foreach (var t in ring.Targets)
            {
                var go = new GameObject(t.Name);
                go.layer = 8;
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(t.Go.transform.position, t.Go.transform.rotation);
                go.transform.localScale = t.Go.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = t.Mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = t.Plinth ? ring.Concrete : ring.Metal;
                mr.shadowCastingMode = ShadowCastingMode.On;
                go.AddComponent<MeshCollider>().sharedMesh = t.Mesh;
                go.AddComponent<UVMetricsExtra>();
                var uid = go.AddComponent<UniqueId>();
                t.Id = uid.m_id = Pws2Hash.ObjectId(sceneName, go.transform);
                var wt = go.AddComponent<WashTarget>();
                wt.m_transform = go.transform;
                wt.m_uniqueId = uid;
                wt.m_washTargetData = t.Data;
                wt.m_gameJobWashTargetRoot = gjRoot;
                wt.m_meshRenderer = mr;
                wt.m_canBeCleanedByClient = false;
                t.Go = go;
            }
            return root;
        }

        /// <summary>Scene washable data: one object (the root), 15 components, each its own group.</summary>
        internal static SceneWashableData BuildSceneWashableData(string path, Ring ring, Transform root, string sceneName)
        {
            var rootId = root.GetComponent<UniqueId>().m_id;
            var swd = LoadOrCreate<SceneWashableData>(path);
            var obj = new WashableObjectData { m_name = root.name, m_uniqueId = rootId, m_parentId = 0, m_fullName = Pws2Hash.ObjectFullName(sceneName, root) };
            var comps = new List<WashableComponentData>();
            foreach (var t in ring.Targets)
            {
                obj.m_componentUniqueIds.Add(t.Id);
                var c = new WashableComponentData { m_name = t.Name, m_uniqueId = t.Id, m_parentObjectUniqueId = rootId, m_startDirtiness = t.StartDirtiness, m_groupIndex = 0, m_cleanName = t.Label };
                c.m_groupComponentUniqueIds.Add(t.Id);
                comps.Add(c);
            }
            swd.m_washableObjects = new[] { obj };
            swd.m_washableComponents = comps.ToArray();
            EditorUtility.SetDirty(swd);
            return swd;
        }

        /// <summary>One stage, one task, one wash task per target.</summary>
        internal static GameJobStage BuildStage(Ring ring, int rootId, string jobUniqueName, string taskUniqueName)
        {
            var task = new GameJobTask { m_isDefault = true, m_washableObjectUniqueId = rootId, m_TaskUniqueName = taskUniqueName, m_localizationKey = $"TASK_{jobUniqueName}_MAIN" };
            foreach (var t in ring.Targets)
                task.m_washTaskList.Add(new GameJobWashTask
                {
                    m_washableComponentUniqueId = t.Id, m_parentWashableObjectUniqueId = rootId,
                    m_payment = new PrimaryCurrencyValue { Value = t.Plinth ? 200 : 100 },
                    m_localizationKey = t.WashKey, m_componentLocalizationKey = t.WashKey, m_componentGroupLocalizationKey = t.WashKey + "_GROUP",
                });
            task.m_washTaskCount = task.m_washTaskList.Count;
            return new GameJobStage { m_uniqueName = "", m_localizationKey = $"STAGE_{jobUniqueName}_", m_tasks = new[] { task } };
        }

        internal static IEnumerable<(string, string)> WashTaskStrings(Ring ring) =>
            ring.Targets.SelectMany(t => new[] { (t.WashKey, t.Label), (t.WashKey + "_GROUP", t.Label) });

        internal static string Describe(Ring ring) =>
            string.Join("\n", ring.Targets.Select(t => $"  {t.Name}: id {t.Id} cell {t.Cell} res {t.Res} start {t.StartDirtiness} verts {t.Mesh.vertexCount} tris {t.Mesh.triangles.Length / 3}"));

        static Mesh CopyMeshWithWashUVs(Mesh src, int cell, string path)
        {
            var dirt = new List<Vector2>();
            src.GetUVs(1, dirt);
            if (dirt.Count != src.vertexCount) throw new System.Exception($"OkcPack: {src.name} has no DirtMask (uv2) channel");
            var m = Object.Instantiate(src);
            m.name = Path.GetFileNameWithoutExtension(path);
            var offset = new Vector2(cell % CellsPerRow, cell / CellsPerRow);
            m.SetUVs(2, dirt);                                        // TexCoord2: local dirt UV, control-map space
            m.SetUVs(1, dirt.Select(uv => uv + offset).ToList());     // TexCoord1: dirt UV + per-target cell
            m.RecalculateBounds();
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>A palette of game dirt layers in control-map channel order (Addressables references only; the game fills m_palette at runtime).</summary>
        static TexturePalette MakePalette(string path, params string[] layerGuids)
        {
            var palette = LoadOrCreate<TexturePalette>(path);
            palette.m_paletteAddressables = layerGuids.Select(g => new AssetReferenceTextureLayer(g)).ToList();
            palette.m_palette = new TextureLayer[0];
            palette.m_dirtLayersLocked = true;
            EditorUtility.SetDirty(palette);
            return palette;
        }

        static void ConfigureControlMap(string pngPath)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = false;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.mipmapEnabled = false;
            imp.isReadable = false;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = 4096;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }

        /// <summary>(resolution, texels with any dirt) of a square control map on disk.</summary>
        static (int, int) CountDirt(string absPng)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(File.ReadAllBytes(absPng), false);
            if (tex.width != tex.height) throw new System.Exception($"OkcPack: {absPng} is not square");
            int dirty = tex.GetPixels32().Count(p => p.r > 0 || p.g > 0 || p.b > 0);
            int res = tex.width;
            Object.DestroyImmediate(tex);
            return (res, dirty);
        }

        static Material MakeMaterial(string path, Color color, float metallic, float smoothness)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static string Abs(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        internal static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) { a = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a, path); }
            return a;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
