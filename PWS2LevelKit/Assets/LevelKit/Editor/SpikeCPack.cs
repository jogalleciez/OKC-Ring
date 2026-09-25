// Spike C: one washable panel end to end in OUR scene.
// Pack "SpikeC": scene GAM_SPIKEC_Panel with a ground plane, a root (UniqueId + GameJobWashTargetRoot), one upright
// panel mesh (three UV sets, MeshCollider, UVMetricsExtra, UniqueId, WashTarget -> WashTargetData -> generated RGB
// control map + our TexturePalette that references the game's TextureLayer assets by GUID), a spawn position,
// the wash CSV, and a GameJob / SceneWashableData / JobPackManifest like Spike B but for this scene.
// The loader (PackRegistry.PrepareScene) assigns the game's WashRenderSettings and swaps the panel material to
// "PWS/Wash Target Uber" when the scene loads; those two cannot be serialised into a pack.
using System.Collections.Generic;
using System.IO;
using System.Text;
using FuturLab;
using FuturLab.PW2;
using FuturLab.PW2.TexturePainter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.SceneManagement;

namespace PWS2LevelKit.Editor
{
    public static class SpikeCPack
    {
        public const string Pack = "SpikeC";
        public const string JobUniqueName = "SPIKEC";
        public const string SceneName = "GAM_SPIKEC_Panel";
        public const string RootName = "PWS_SPIKEC_Root";
        public const string PanelName = "Panel";
        public const string Table = "PWS2_Main";
        public const int Res = 512;

        // Game assets borrowed by GUID (docs/phase0-findings.md, Billboard dumps)
        const string BillboardThumbAGuid = "53673f1550c0aca4284677e4b9740f96";
        const string BillboardThumbBGuid = "044f6bbd34e34ae468c28348a16df456";
        const string BillboardPreviewGuid = "8bfbf0fb783cb6940a4ab3022cbce456";
        const string BillboardLoadingGuid = "dc833b6ef57d43e43966db211dd17421";
        const string BillboardShaderCollectionGuid = "9217f14022624204f9ea2c09bc96483e";
        // TextureLayer assets of the game's Billboard palettes (TPL_PW2_Billboard_GrimeGreenSlimy_MudSplats_SandDarker)
        const string LayerGrimeGreenSlimy = "58956cc2a4338fd4cb7913869c50e06f";
        const string LayerMudSplats = "5e7adb63034f5554385adf5e2002a2af";
        const string LayerSandDarker = "a4897b21ac3656848a2501a52ed0a3e8";

        [MenuItem("PWS2/Spikes/C: create panel job pack + build")]
        public static void CreateAndBuild() { Create(); PackBuilder.Build(Pack, PackBuilder.DefaultDist); }

        public static void CreateAndBuildFromCommandLine()
        {
            try { Create(); EditorApplication.Exit(PackBuilder.Build(Pack, PackBuilder.DefaultDist) ? 0 : 1); }
            catch (System.Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void Create()
        {
            var folder = $"{PackBuilder.PacksRoot}/{Pack}";
            EnsureFolder(PackBuilder.PacksRoot); EnsureFolder(folder);
            EnsureFolder(folder + "/Scenes"); EnsureFolder(folder + "/Data"); EnsureFolder(folder + "/Textures");

            string rootFull = $"[{SceneName}]{RootName}", panelFull = $"[{SceneName}]{RootName}.{PanelName}";
            int rootId = Pws2Hash.String(rootFull), panelId = Pws2Hash.String(panelFull);

            // --- control map (RGB = opacity of palette layers 0/1/2, TexCoord2 space, full square, no cropping)
            var pngPath = $"{folder}/Textures/CM_SpikeC_Panel.png";
            int startDirtiness = WriteControlMap(Abs(pngPath));
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
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
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            var pngGuid = AssetDatabase.AssetPathToGUID(pngPath);

            // --- palette: only Addressables references; the game fills m_palette at runtime (TexturePalette.RefreshPalette)
            var palette = LoadOrCreate<TexturePalette>($"{folder}/Data/TPL_SpikeC.asset");
            palette.m_paletteAddressables = new List<AssetReferenceTextureLayer>
            {
                new AssetReferenceTextureLayer(LayerGrimeGreenSlimy),
                new AssetReferenceTextureLayer(LayerMudSplats),
                new AssetReferenceTextureLayer(LayerSandDarker),
            };
            palette.m_palette = new TextureLayer[0];
            palette.m_dirtLayersLocked = true;
            EditorUtility.SetDirty(palette);

            // --- wash target data
            var wtd = LoadOrCreate<WashTargetData>($"{folder}/Data/WTD_SpikeC_Panel.asset");
            wtd.SuggestedControlMapResolution = Res;
            wtd.ControlMapAsset = new AssetReferenceTexture2D(pngGuid);
            wtd.ControlMapAreaInUse = new RectInt(0, 0, Res, Res);
            wtd.ControlMapTrimmedRect = new RectInt(0, 0, Res, Res);
            wtd.ControlMapTotalRect = new Rect(0, 0, 0, 0);
            wtd.TexturePalette = palette;
            wtd.OverrideSurfaceCleanThreshold = true;
            wtd.SurfaceCleanThreshold = 0.1f;
            wtd.ConfirmedByDesigner = true;
            EditorUtility.SetDirty(wtd);

            // --- wash CSV (version-2 layout table; one row per target, cell (0,0))
            var csvPath = $"{folder}/Data/{RootName}_washtargets.csv";
            File.WriteAllText(Abs(csvPath), "CSV VERSION 2\nNODE NAME,TEXTURE SIZE,X OFFSET,Y OFFSET,UNIQUEID,UVSPACE\n" +
                                            $"{PanelName},{Res} x {Res},0,0,0,100%\n");
            AssetDatabase.ImportAsset(csvPath, ImportAssetOptions.ForceUpdate);
            var csvGuid = AssetDatabase.AssetPathToGUID(csvPath);

            // --- mesh + material
            var meshPath = $"{folder}/Data/Mesh_SpikeC_Panel.asset";
            AssetDatabase.DeleteAsset(meshPath);
            var mesh = BuildPanelMesh(2.0f, 1.2f);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var matPath = $"{folder}/Data/MAT_SpikeC_Panel.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.80f, 1f));
            EditorUtility.SetDirty(mat);

            // --- scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground"; ground.transform.localScale = new Vector3(3, 1, 3);

            GameplayBoot.Create(); // the game's level-system prefabs (GameplayAudioManager, ...), see GameplayBoot.cs

            var root = new GameObject(RootName);
            root.layer = 8; // Washable
            var rootUid = root.AddComponent<UniqueId>(); rootUid.m_id = rootId;
            var gjRoot = root.AddComponent<GameJobWashTargetRoot>();
            gjRoot.m_uniqueId = rootUid;
            gjRoot.m_csvFileAsset = new AssetReferenceT<TextAsset>(csvGuid);
            gjRoot.m_texturePalette = palette;

            var panel = new GameObject(PanelName);
            panel.layer = 8;
            panel.transform.SetParent(root.transform, false);
            panel.transform.position = new Vector3(0, 0.9f, 3f); // 2 m wide, 1.2 m tall, bottom edge 0.3 m up, facing the spawn
            panel.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = panel.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            panel.AddComponent<MeshCollider>().sharedMesh = mesh;
            panel.AddComponent<UVMetricsExtra>();
            var uid = panel.AddComponent<UniqueId>(); uid.m_id = panelId;
            var wt = panel.AddComponent<WashTarget>();
            wt.m_transform = panel.transform;
            wt.m_uniqueId = uid;
            wt.m_washTargetData = wtd;
            wt.m_gameJobWashTargetRoot = gjRoot;
            wt.m_meshRenderer = mr;
            wt.m_canBeCleanedByClient = false;

            var spawn = new GameObject("CharacterSpawnPosition");
            spawn.layer = 23; // SpawnPositions
            spawn.transform.position = new Vector3(0, 0, -3);
            var sp = spawn.AddComponent<CharacterSpawnPosition>(); sp.m_type = CharacterSpawnPositionType.Main;
            var cap = spawn.AddComponent<CapsuleCollider>(); cap.radius = 0.5f; cap.height = 1.4f; cap.center = new Vector3(0, 0.7f, 0); cap.isTrigger = true;

            var scenePath = $"{folder}/Scenes/{SceneName}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            var sceneGuid = AssetDatabase.AssetPathToGUID(scenePath);

            // --- scene washable data
            var swd = LoadOrCreate<SceneWashableData>($"{folder}/Data/SWD_SpikeC.asset");
            var obj = new WashableObjectData { m_name = RootName, m_uniqueId = rootId, m_parentId = 0, m_fullName = rootFull };
            obj.m_componentUniqueIds.Add(panelId);
            var comp = new WashableComponentData { m_name = PanelName, m_uniqueId = panelId, m_parentObjectUniqueId = rootId, m_startDirtiness = startDirtiness, m_groupIndex = 0 };
            comp.m_groupComponentUniqueIds.Add(panelId);
            swd.m_washableObjects = new[] { obj };
            swd.m_washableComponents = new[] { comp };
            EditorUtility.SetDirty(swd);

            // --- customer
            var customer = LoadOrCreate<GameJobCustomer>($"{folder}/Data/Customer_SpikeC.asset");
            customer.m_uniqueName = "SPIKECCUSTOMER";
            customer.m_uniqueId = Pws2Hash.String(customer.m_uniqueName);
            customer.m_unlockedByDefault = true;
            customer.m_portraitAsset = new AssetReferenceSprite("844fa52259e94314187ba2cc456ad0ca"); // Jeff Jefferson XIII portrait
            customer.m_localizedName = new LocalizedString(Table, "CUSTOMER_CAREER_SPIKECCUSTOMER_TITLE");
            customer.m_localizedDescription = new LocalizedString(Table, "CUSTOMER_CAREER_SPIKECCUSTOMER_DESCRIPTION");
            EditorUtility.SetDirty(customer);

            // --- job
            var job = LoadOrCreate<GameJob>($"{folder}/Data/Job_SpikeC.asset");
            job.m_uniqueName = JobUniqueName;
            job.m_uniqueId = Pws2Hash.String(JobUniqueName);
            job.CampaignID = Pws2Hash.String("DEFAULT");
            job.m_unlockedByDefault = true;
            job.m_saveSize = SaveSize.Small;
            job.m_levelDevelopmentStage = DevelopmentStage.ProductionReady;
            job.m_customer = customer;
            job.m_completionPayment = new PrimaryCurrencyValue { Value = 1000 };
            job.m_completionPaymentSecondary = new MuckCoinValue { Value = 100 };
            job.m_stringTableName = Table;
            job.m_localizedName = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_TITLE");
            job.m_localizedDescription = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_DESCRIPTION");
            job.m_freePlayNameInteraction = new LocalizedString(Table, $"HOMEBASE_{JobUniqueName}_FREEPLAY");
            job.m_storyNameInteraction = new LocalizedString(Table, $"HOMEBASE_{JobUniqueName}_STORY");
            job.m_timelapseStaticThumbnail = new AssetReferenceSprite(BillboardPreviewGuid);
            var loc = job.Location;
            loc.SceneWashableData = swd;
            loc.SceneName = SceneName;
            loc.SceneAssetReference = new AssetReference(sceneGuid);
            loc.SceneShaderCollectionAsset = new FuturLab.Addressables.AssetReferenceShaderVariantCollection(BillboardShaderCollectionGuid);
            loc.LocalizedAddress = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_ADDRESS");
            loc.LocalizedLoadingInfo = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_DESCRIPTION");
            loc.LoadingScreenImageAsset = new AssetReferenceSprite(BillboardLoadingGuid);
            loc.PreviewImageAsset = new AssetReferenceSprite(BillboardPreviewGuid);
            loc.ChatImageAsset = new AssetReferenceSprite(BillboardPreviewGuid);
            loc.SelectedThumbnailImageAsset = new AssetReferenceSprite(BillboardThumbAGuid);
            loc.DeselectedThumbnailImageAsset = new AssetReferenceSprite(BillboardThumbBGuid);
            loc.AmbienceSoundEvent = new FMODUnity.EventReference { Guid = FMOD.GUID.Parse("{afbe6293-29cd-4e2a-843b-e533fc242c16}") };
            loc.MaxMapHeight = 6f; loc.HasWind = false;

            var sceneUpper = SceneName.ToUpperInvariant();
            var task = new GameJobTask { m_isDefault = true, m_washableObjectUniqueId = rootId, m_TaskUniqueName = "SPIKEC_TASK", m_localizationKey = $"TASK_{JobUniqueName}_MAIN" };
            task.m_washTaskList.Add(new GameJobWashTask
            {
                m_washableComponentUniqueId = panelId, m_parentWashableObjectUniqueId = rootId,
                m_payment = new PrimaryCurrencyValue { Value = 500 },
                m_localizationKey = $"WASHTASK_{sceneUpper}_WASH_{PanelName.ToUpperInvariant()}",
                m_componentLocalizationKey = $"WASHTASK_{sceneUpper}_WASH_{PanelName.ToUpperInvariant()}",
                m_componentGroupLocalizationKey = $"WASHTASK_{sceneUpper}_WASH_{PanelName.ToUpperInvariant()}_GROUP",
            });
            task.m_washTaskCount = task.m_washTaskList.Count;
            job.m_stages = new[] { new GameJobStage { m_uniqueName = "", m_localizationKey = $"STAGE_{JobUniqueName}_", m_tasks = new[] { task } } };
            EditorUtility.SetDirty(job);

            // --- manifest
            var manifestPath = $"{folder}/Data/JobPackManifest_SpikeC.asset";
            var manifest = LoadOrCreate<JobPackManifest>(manifestPath);
            manifest.m_gameJobs.Clear(); manifest.m_gameJobs.Add(job);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();

            var manifestGuid = AssetDatabase.AssetPathToGUID(manifestPath);
            var sb = new StringBuilder();
            sb.Append("{\n  \"id\": \"SpikeC\",\n  \"displayName\": \"Spike C (one washable panel)\",\n  \"version\": \"0.0.1\",\n  \"gameVersion\": \"" + PackBuilder.GameVersion + "\",\n  \"catalog\": \"catalog.bin\",\n");
            sb.Append($"  \"jobPackManifestGuid\": \"{manifestGuid}\",\n  \"jobs\": [ {{\n    \"uniqueName\": \"{JobUniqueName}\", \"uniqueId\": {job.m_uniqueId}, \"sceneName\": \"{SceneName}\",\n");
            sb.Append("    \"title\": \"Spike C Panel\", \"address\": \"Spike Street, Testville\", \"description\": \"One washable panel built entirely from the level kit.\",\n");
            sb.Append("    \"freePlayName\": \"Spike C Panel (Free Play)\", \"storyName\": \"Spike C Panel\",\n");
            sb.Append($"    \"thumbnailSelectedGuid\": \"{BillboardThumbAGuid}\", \"thumbnailDeselectedGuid\": \"{BillboardThumbBGuid}\"\n  }} ],\n");
            sb.Append("  \"strings\": {\n    \"CUSTOMER_CAREER_SPIKECCUSTOMER_TITLE\": \"Spike Tester\", \"CUSTOMER_CAREER_SPIKECCUSTOMER_DESCRIPTION\": \"Tests panels.\",\n");
            sb.Append($"    \"TASK_{JobUniqueName}_MAIN\": \"The panel\",\n");
            sb.Append($"    \"WASHTASK_{sceneUpper}_WASH_PANEL\": \"Panel\", \"WASHTASK_{sceneUpper}_WASH_PANEL_GROUP\": \"Panel\"\n  }}\n}}\n");
            File.WriteAllText(Path.Combine(Application.dataPath, "..", folder, "pack.json"), sb.ToString());
            AssetDatabase.Refresh();
            Debug.Log($"SpikeC: job {job.m_uniqueName} id {job.m_uniqueId}, root {rootId}, panel {panelId}, startDirtiness {startDirtiness}, manifest {manifestGuid}");
        }

        /// <summary>
        /// Writes an RGBA32 PNG: R (layer 0, slimy grime) covers the whole panel except a thin clean margin, G (layer 1,
        /// mud splats) three discs, B (layer 2, sand) a band along the bottom; alpha 0 like the shipped maps.
        /// Returns the number of texels carrying any dirt (start dirtiness for SceneWashableData).
        /// </summary>
        static int WriteControlMap(string absPath)
        {
            var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false, true);
            var px = new Color32[Res * Res];
            int dirty = 0;
            var splats = new[] { new Vector2(0.25f, 0.6f), new Vector2(0.62f, 0.72f), new Vector2(0.78f, 0.35f) };
            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                {
                    float u = (x + 0.5f) / Res, v = (y + 0.5f) / Res;
                    byte r = (u > 0.03f && u < 0.97f && v > 0.03f && v < 0.97f) ? (byte)255 : (byte)0;
                    byte g = 0;
                    foreach (var s in splats) if (Vector2.Distance(new Vector2(u, v), s) < 0.12f) g = 255;
                    byte b = v < 0.2f ? (byte)255 : (byte)0;
                    px[y * Res + x] = new Color32(r, g, b, 0);
                    if (r > 0 || g > 0 || b > 0) dirty++;
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            File.WriteAllBytes(absPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return dirty;
        }

        /// <summary>Upright quad in the XY plane facing -Z, with uv0 = uv2 = 0..1 and uv1 = uv2 + cell offset (0,0).</summary>
        static Mesh BuildPanelMesh(float width, float height)
        {
            float hw = width / 2f, hh = height / 2f;
            var m = new Mesh { name = "Mesh_SpikeC_Panel" };
            m.vertices = new[] { new Vector3(-hw, -hh, 0), new Vector3(hw, -hh, 0), new Vector3(hw, hh, 0), new Vector3(-hw, hh, 0) };
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.uv = uv; m.uv2 = uv; m.uv3 = uv;
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 }; // clockwise seen from -Z
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        static string Abs(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) { a = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a, path); }
            return a;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
