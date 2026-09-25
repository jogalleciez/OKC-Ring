// Spike B: does a JobPackManifest + GameJob authored with stubs register as a Free Play job?
// The job reuses the game's own Billboard scene (by Addressables GUID) with a 3-component SceneWashableData,
// so if registration works the level should load from the Jobs menu and those three targets should wash.
using System.IO;
using System.Text;
using FuturLab.PW2;
using FuturLab.PW2.TexturePainter;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;

namespace PWS2LevelKit.Editor
{
    public static class SpikeBPack
    {
        public const string Pack = "SpikeB";
        public const string JobUniqueName = "SPIKEB";
        public const string Table = "PWS2_Main";

        // Billboard reference values (docs/phase0-findings.md §3–§6)
        const string BillboardSceneGuid = "88f3121e6a1836e40afea646a375497c";
        const string BillboardThumbAGuid = "53673f1550c0aca4284677e4b9740f96";
        const string BillboardThumbBGuid = "044f6bbd34e34ae468c28348a16df456";
        const string BillboardPreviewGuid = "8bfbf0fb783cb6940a4ab3022cbce456";
        const string BillboardLoadingGuid = "dc833b6ef57d43e43966db211dd17421";
        const string BillboardShaderCollectionGuid = "9217f14022624204f9ea2c09bc96483e";
        const int BillboardRootId = 2011916029;
        static readonly (string name, int id, int dirt)[] Components =
        {
            ("AdvertisementBackground", 482848100, 481962),
            ("HatchHinge", 740449598, 3448),
            ("Hatch", -1094926826, 54388),
        };

        [MenuItem("PWS2/Spikes/B: create job pack + build")]
        public static void CreateAndBuild() { Create(); PackBuilder.Build(Pack, PackBuilder.DefaultDist); }

        public static void CreateAndBuildFromCommandLine()
        {
            try { Create(); EditorApplication.Exit(PackBuilder.Build(Pack, PackBuilder.DefaultDist) ? 0 : 1); }
            catch (System.Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void Create()
        {
            var folder = $"{PackBuilder.PacksRoot}/{Pack}";
            EnsureFolder(folder); EnsureFolder(folder + "/Data");

            // --- SceneWashableData subset of Billboard
            var swd = LoadOrCreate<SceneWashableData>($"{folder}/Data/SWD_SpikeB.asset");
            var obj = new WashableObjectData { m_name = "PWS_PW2_HW_Billboard", m_uniqueId = BillboardRootId, m_parentId = 0, m_fullName = "[GAM_PW2_HW_Billboard]PWS_PW2_HW_Billboard" };
            swd.m_washableComponents = new WashableComponentData[Components.Length];
            for (int i = 0; i < Components.Length; i++)
            {
                var c = Components[i];
                obj.m_componentUniqueIds.Add(c.id);
                swd.m_washableComponents[i] = new WashableComponentData { m_name = c.name, m_uniqueId = c.id, m_parentObjectUniqueId = BillboardRootId, m_startDirtiness = c.dirt, m_groupIndex = 0 };
                swd.m_washableComponents[i].m_groupComponentUniqueIds.Add(c.id);
            }
            swd.m_washableObjects = new[] { obj };
            EditorUtility.SetDirty(swd);

            // --- customer
            var customer = LoadOrCreate<GameJobCustomer>($"{folder}/Data/Customer_SpikeB.asset");
            customer.m_uniqueName = "SPIKEBCUSTOMER";
            customer.m_uniqueId = Pws2Hash.String(customer.m_uniqueName);
            customer.m_unlockedByDefault = true;
            customer.m_localizedName = new LocalizedString(Table, "CUSTOMER_CAREER_SPIKEBCUSTOMER_TITLE");
            customer.m_localizedDescription = new LocalizedString(Table, "CUSTOMER_CAREER_SPIKEBCUSTOMER_DESCRIPTION");
            EditorUtility.SetDirty(customer);

            // --- job
            var job = LoadOrCreate<GameJob>($"{folder}/Data/Job_SpikeB.asset");
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
            loc.SceneName = "GAM_PW2_HW_Billboard";
            loc.SceneAssetReference = new AssetReference(BillboardSceneGuid);
            loc.SceneShaderCollectionAsset = new FuturLab.Addressables.AssetReferenceShaderVariantCollection(BillboardShaderCollectionGuid);
            loc.LocalizedAddress = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_ADDRESS");
            loc.LocalizedLoadingInfo = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_DESCRIPTION");
            loc.LoadingScreenImageAsset = new AssetReferenceSprite(BillboardLoadingGuid);
            loc.PreviewImageAsset = new AssetReferenceSprite(BillboardPreviewGuid);
            loc.ChatImageAsset = new AssetReferenceSprite(BillboardPreviewGuid);
            loc.SelectedThumbnailImageAsset = new AssetReferenceSprite(BillboardThumbAGuid);
            loc.DeselectedThumbnailImageAsset = new AssetReferenceSprite(BillboardThumbBGuid);
            loc.AmbienceSoundEvent = new FMODUnity.EventReference { Guid = FMOD.GUID.Parse("{afbe6293-29cd-4e2a-843b-e533fc242c16}") };
            loc.MaxMapHeight = 12.1f; loc.HasWind = true;

            var task = new GameJobTask { m_isDefault = true, m_washableObjectUniqueId = BillboardRootId, m_TaskUniqueName = "SPIKEB_TASK", m_localizationKey = $"TASK_{JobUniqueName}_MAIN" };
            foreach (var c in Components)
                task.m_washTaskList.Add(new GameJobWashTask
                {
                    m_washableComponentUniqueId = c.id, m_parentWashableObjectUniqueId = BillboardRootId,
                    m_payment = new PrimaryCurrencyValue { Value = 300 },
                    m_localizationKey = $"WASHTASK_GAM_PW2_HW_BILLBOARD_WASH_{c.name.ToUpperInvariant()}",
                    m_componentLocalizationKey = $"WASHTASK_GAM_PW2_HW_BILLBOARD_WASH_{c.name.ToUpperInvariant()}",
                    m_componentGroupLocalizationKey = $"WASHTASK_GAM_PW2_HW_BILLBOARD_WASH_{c.name.ToUpperInvariant()}_GROUP",
                });
            task.m_washTaskCount = task.m_washTaskList.Count;
            job.m_stages = new[] { new GameJobStage { m_uniqueName = "", m_localizationKey = $"STAGE_{JobUniqueName}_", m_tasks = new[] { task } } };
            EditorUtility.SetDirty(job);

            // --- manifest
            var manifestPath = $"{folder}/Data/JobPackManifest_SpikeB.asset";
            var manifest = LoadOrCreate<JobPackManifest>(manifestPath);
            manifest.m_gameJobs.Clear(); manifest.m_gameJobs.Add(job);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();

            var manifestGuid = AssetDatabase.AssetPathToGUID(manifestPath);
            var sb = new StringBuilder();
            sb.Append("{\n  \"id\": \"SpikeB\",\n  \"displayName\": \"Spike B (job registration)\",\n  \"version\": \"0.0.1\",\n  \"gameVersion\": \"" + PackBuilder.GameVersion + "\",\n  \"catalog\": \"catalog.bin\",\n");
            sb.Append($"  \"jobPackManifestGuid\": \"{manifestGuid}\",\n  \"jobs\": [ {{\n    \"uniqueName\": \"{JobUniqueName}\", \"uniqueId\": {job.m_uniqueId},\n");
            sb.Append("    \"title\": \"Spike B Billboard\", \"address\": \"Spike Street, Testville\", \"description\": \"Registration test job reusing the Billboard scene.\",\n");
            sb.Append("    \"freePlayName\": \"Spike B Billboard (Free Play)\", \"storyName\": \"Spike B Billboard\",\n");
            sb.Append($"    \"thumbnailSelectedGuid\": \"{BillboardThumbAGuid}\", \"thumbnailDeselectedGuid\": \"{BillboardThumbBGuid}\"\n  }} ],\n");
            sb.Append("  \"strings\": { \"CUSTOMER_CAREER_SPIKEBCUSTOMER_TITLE\": \"Spike Tester\", \"CUSTOMER_CAREER_SPIKEBCUSTOMER_DESCRIPTION\": \"Tests things.\", \"TASK_SPIKEB_MAIN\": \"Billboard bits\" }\n}\n");
            File.WriteAllText(Path.Combine(Application.dataPath, "..", folder, "pack.json"), sb.ToString());
            AssetDatabase.Refresh();
            Debug.Log($"SpikeB: job {job.m_uniqueName} id {job.m_uniqueId}, manifest {manifestGuid}");
        }

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
