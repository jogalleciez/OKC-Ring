// OKC Transformer Screen in the game's own Streetcar level: a variant job on GAM_PW2_HS_Tram (the vanilla Streetcar
// job, TRAM, is left alone) in which the ring replaces the tram.
//
// Pack "OKC_Streetcar" ships no scene. It ships the ring as a prefab (PWS_OKC_Root + 15 wash targets, ids hashed for
// the Tram scene, TexCoord1 cells from row 38 because the tram uses rows 0..37), a GameJob whose Location is the
// game's Tram scene by GUID (the Spike B pattern) with a SceneWashableData listing only the ring, and pack.json with:
//   sceneAdditions: prefabs the loader instantiates into the scene before the game initialises wash data
//   hideWashRoots / hideContainedIn / hideObjects: the tram, whose renderers, colliders and lights the loader switches off
// Values from the Tram dump of 2026-09-23 (docs/streetcar-variant.md).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FuturLab;
using FuturLab.PW2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;

namespace PWS2LevelKit.Editor
{
    public static class OkcStreetcarPack
    {
        public const string Pack = "OKC_Streetcar";
        public const string JobUniqueName = "OKC_STREETCAR";
        public const string SceneName = "GAM_PW2_HS_Tram";
        public const string Table = "PWS2_Main";
        const string Tag = "_Streetcar";
        const int CellBase = 380;                           // tram CSV uses Y offsets 0..37

        // Where the tram stands (PWS_PW2_HS_Tram root; road surface at y ~ 0)
        static readonly Vector3 RingPosition = new Vector3(26.66f, 0f, -26.66f);
        static readonly Vector3 RingEuler = Vector3.zero;

        // Game assets from Job_PW2_Tram
        const string TramSceneGuid = "430726867bfab74409b388bdd6f12b50";
        const string TramShaderCollectionGuid = "b6e3e65d760e5ee46836cba19c2a6089";
        const string TramLoadingGuid = "03e8a69fe70511d4c905efd6995efc6e";   // 'Highstreet'
        const string TramPreviewGuid = "316c819d6de56114d84e8b121fe66b47";
        const string TramChatGuid = "324d1fcaa9ad11f4b94da0f632bf1e80";
        const string TramThumbAGuid = "4fe1d11326223a743ae40b1dce85793e";
        const string TramThumbBGuid = "ff3cda8f57385a9449c992137e799df4";
        const string TramTimelapseGuid = "495408309162a854bb59cf2d6068f208";
        const string TramAmbience = "{d6b20e49-fa0a-4945-97ca-194d254115e4}";

        // The tram: every WashTarget under this root, everything else lying inside its volume (underside, lamps,
        // steps, trolley pole; the loader grows the targets' bounds by 0.3 m sideways and 3 m up), plus named roots
        static readonly string[] HideWashRoots = { "PWS_PW2_HS_Tram" };
        // scene roots as they are when the scene has just loaded (the game flattens the hierarchy later). Not
        // PLT_PW2_HS_Tram: that is the level's whole lighting rig, sun included; the tram's own saloon and white lights
        // sit inside its volume and go with hideContainedIn.
        static readonly string[] HideObjects =
        {
            "PNW_PW2_HS_Tram",                 // non-washable tram parts: underside, headlights, inside windows
            "PNC_PW2_HS_Tram",                 // the boarding steps' navigation collision
            "PEV_PW2_HS_TramShadowCaster",
            "Animation",                       // Int_Tram_Doors_01..04 (door interactables)
        };

        [MenuItem("PWS2/OKC in the Streetcar level: create pack + build")]
        public static void CreateAndBuild() { Create(); PackBuilder.Build(Pack, PackBuilder.DefaultDist); }

        public static void CreateAndBuildFromCommandLine()
        {
            try { Create(); EditorApplication.Exit(PackBuilder.Build(Pack, PackBuilder.DefaultDist) ? 0 : 1); }
            catch (System.Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void Create()
        {
            var folder = $"{PackBuilder.PacksRoot}/{Pack}";
            var ring = OkcPack.ImportRing(folder, SceneName, CellBase, Tag);
            OkcPack.EnsureFolder(folder + "/Prefabs");

            // --- the ring as a prefab (built in a throwaway scene so ids hash from the root-relative path)
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = OkcPack.BuildRoot(ring, SceneName);
            var rootId = root.GetComponent<UniqueId>().m_id;
            var swd = OkcPack.BuildSceneWashableData($"{folder}/Data/SWD_OKC{Tag}.asset", ring, root.transform, SceneName);
            var prefabPath = $"{folder}/Prefabs/{OkcPack.RootName}{Tag}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            var prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);

            // --- customer
            var customer = OkcPack.LoadOrCreate<GameJobCustomer>($"{folder}/Data/Customer_OKC{Tag}.asset");
            customer.m_uniqueName = "OKCSTREETCARCUSTOMER";
            customer.m_uniqueId = Pws2Hash.String(customer.m_uniqueName);
            customer.m_unlockedByDefault = true;
            customer.m_portraitAsset = new AssetReferenceSprite(OkcPack.CustomerPortraitGuid);
            customer.m_localizedName = new LocalizedString(Table, "CUSTOMER_CAREER_OKCSTREETCARCUSTOMER_TITLE");
            customer.m_localizedDescription = new LocalizedString(Table, "CUSTOMER_CAREER_OKCSTREETCARCUSTOMER_DESCRIPTION");
            EditorUtility.SetDirty(customer);

            // --- job on the game's Tram scene
            var job = OkcPack.LoadOrCreate<GameJob>($"{folder}/Data/Job_OKC{Tag}.asset");
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
            job.m_timelapseStaticThumbnail = new AssetReferenceSprite(TramTimelapseGuid);
            var loc = job.Location;
            loc.SceneWashableData = swd;
            loc.SceneName = SceneName;
            loc.SceneAssetReference = new AssetReference(TramSceneGuid);
            loc.SceneShaderCollectionAsset = new FuturLab.Addressables.AssetReferenceShaderVariantCollection(TramShaderCollectionGuid);
            loc.LocalizedAddress = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_ADDRESS");
            loc.LocalizedLoadingInfo = new LocalizedString(Table, $"SCRIPT_CAREER_{JobUniqueName}_DESCRIPTION");
            loc.LoadingScreenImageAsset = new AssetReferenceSprite(TramLoadingGuid);
            loc.PreviewImageAsset = new AssetReferenceSprite(TramPreviewGuid);
            loc.ChatImageAsset = new AssetReferenceSprite(TramChatGuid);
            loc.SelectedThumbnailImageAsset = new AssetReferenceSprite(TramThumbAGuid);
            loc.DeselectedThumbnailImageAsset = new AssetReferenceSprite(TramThumbBGuid);
            loc.AmbienceSoundEvent = new FMODUnity.EventReference { Guid = FMOD.GUID.Parse(TramAmbience) };
            loc.MaxMapHeight = 0f; loc.HasWind = false;

            job.m_stages = new[] { OkcPack.BuildStage(ring, rootId, JobUniqueName, "OKC_STREETCAR_TASK") };
            EditorUtility.SetDirty(job);

            var manifestPath = $"{folder}/Data/JobPackManifest_OKC{Tag}.asset";
            var manifest = OkcPack.LoadOrCreate<JobPackManifest>(manifestPath);
            manifest.m_gameJobs.Clear(); manifest.m_gameJobs.Add(job);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();

            // --- pack.json
            var manifestGuid = AssetDatabase.AssetPathToGUID(manifestPath);
            var strings = new List<(string, string)>
            {
                ("CUSTOMER_CAREER_OKCSTREETCARCUSTOMER_TITLE", "Downtown Utilities"),
                ("CUSTOMER_CAREER_OKCSTREETCARCUSTOMER_DESCRIPTION", "Keeps the lights on downtown."),
                ($"TASK_{JobUniqueName}_MAIN", "Transformer Screen"),
            };
            strings.AddRange(OkcPack.WashTaskStrings(ring));
            string Vec(Vector3 v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "[{0}, {1}, {2}]", v.x, v.y, v.z);
            string List(IEnumerable<string> xs) => "[" + string.Join(", ", xs.Select(x => $"\"{x}\"")) + "]";
            var sb = new StringBuilder();
            sb.Append($"{{\n  \"id\": \"{Pack}\",\n  \"displayName\": \"OKC Transformer Screen (Streetcar street)\",\n  \"version\": \"0.1.0\",\n  \"gameVersion\": \"{PackBuilder.GameVersion}\",\n  \"catalog\": \"catalog.bin\",\n");
            sb.Append($"  \"jobPackManifestGuid\": \"{manifestGuid}\",\n  \"jobs\": [ {{\n    \"uniqueName\": \"{JobUniqueName}\", \"uniqueId\": {job.m_uniqueId}, \"sceneName\": \"{SceneName}\",\n");
            sb.Append("    \"title\": \"OKC Screen on Equinox Boulevard\", \"address\": \"Equinox Boulevard, Detergento\",\n");
            sb.Append("    \"description\": \"The OKC transformer screen, set down where the streetcar usually waits.\",\n");
            sb.Append("    \"freePlayName\": \"OKC Screen on Equinox Boulevard (Free Play)\", \"storyName\": \"OKC Screen on Equinox Boulevard\",\n");
            sb.Append($"    \"thumbnailSelectedGuid\": \"{TramThumbAGuid}\", \"thumbnailDeselectedGuid\": \"{TramThumbBGuid}\",\n");
            sb.Append($"    \"sceneAdditions\": [ {{ \"prefabGuid\": \"{prefabGuid}\", \"name\": \"{OkcPack.RootName}\", \"position\": {Vec(RingPosition)}, \"eulerAngles\": {Vec(RingEuler)} }} ],\n");
            sb.Append($"    \"hideWashRoots\": {List(HideWashRoots)}, \"hideContainedIn\": {List(HideWashRoots)},\n");
            sb.Append($"    \"hideObjects\": {List(HideObjects)}\n  }} ],\n");
            sb.Append("  \"strings\": {\n");
            sb.Append(string.Join(",\n", strings.Select(s => $"    \"{s.Item1}\": \"{s.Item2}\"")));
            sb.Append("\n  }\n}\n");
            File.WriteAllText(OkcPack.Abs($"{folder}/pack.json"), sb.ToString());
            AssetDatabase.Refresh();

            Debug.Log($"OkcStreetcarPack: job {job.m_uniqueName} id {job.m_uniqueId}, root {rootId}, prefab {prefabGuid}, manifest {manifestGuid}\n" + OkcPack.Describe(ring));
        }
    }
}
