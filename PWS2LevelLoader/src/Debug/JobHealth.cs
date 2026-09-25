using System;
using System.Text;
using Il2CppFuturLab.PW2;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;

namespace PWS2LevelLoader.Debug
{
    /// <summary>
    /// One-line health check of a loaded GameJob: which references are null or empty after deserialisation.
    /// Log it for a pack job and for a base-game job side by side to spot what the hub may trip over.
    /// </summary>
    internal static class JobHealth
    {
        public static void Log(GameJob job, string label)
        {
            try { Core.Log.Msg($"JobHealth[{label}] {Describe(job)}"); }
            catch (Exception ex) { Core.Log.Warning($"JobHealth[{label}] failed: {ex.GetType().Name}: {ex.Message}"); }
        }

        public static string Describe(GameJob job)
        {
            if (job == null) return "job=null";
            var sb = new StringBuilder();
            sb.Append(job.name).Append(' ');
            Add(sb, "id", () => job.m_uniqueId.ToString());
            Add(sb, "campaign", () => job.CampaignID.ToString());
            Add(sb, "unlockedByDefault", () => job.m_unlockedByDefault.ToString());
            Add(sb, "unlockRules", () => job.m_unlockRules == null ? "null" : job.m_unlockRules.Length.ToString());
            Add(sb, "unlockRuleTargetIds", () => job.m_unlockRuleTargetIds == null ? "null" : job.m_unlockRuleTargetIds.Count.ToString());
            Add(sb, "availability", () => job.m_availability.Platform + "/" + job.m_availability.ReleaseType);
            Add(sb, "name", () => Loc(job.m_localizedName));
            Add(sb, "description", () => Loc(job.m_localizedDescription));
            Add(sb, "freePlayInteraction", () => Loc(job.m_localizedFreePlayInteraction));
            Add(sb, "storyNameInteraction", () => Loc(job.m_storyNameInteraction));
            Add(sb, "freePlayNameInteraction", () => Loc(job.m_freePlayNameInteraction));
            Add(sb, "stringTable", () => $"'{job.m_stringTableName}'->{job.GetStringTableOrDefault()}");
            Add(sb, "localizationId", () => job.m_localizationId ?? "null");
            Add(sb, "saveSize", () => job.m_saveSize.ToString());
            Add(sb, "devStage", () => job.m_levelDevelopmentStage.ToString());
            Add(sb, "isTest", () => job.IsTestJob.ToString());
            Add(sb, "customer", () => job.m_customer == null ? "null" : $"{job.m_customer.name}(id={job.m_customer.m_uniqueId},portrait={Ref(job.m_customer.m_portraitAsset)},name={Loc(job.m_customer.m_localizedName)})");
            Add(sb, "brief", () => job.BriefMessageSequence == null ? "null" : job.BriefMessageSequence.name);
            Add(sb, "completion", () => job.CompletionMessageSequence == null ? "null" : job.CompletionMessageSequence.name);
            Add(sb, "allMessages", () => job.m_allMessageSequences == null ? "null" : job.m_allMessageSequences.Count.ToString());
            Add(sb, "timelapseQ", () => job.m_timelapseQualityOverride == null ? "null" : "set");
            Add(sb, "timelapseThumb", () => Ref(job.m_timelapseStaticThumbnail));
            Add(sb, "payment", () => job.m_completionPayment == null ? "null" : job.m_completionPayment.Value.ToString());
            Add(sb, "payment2", () => job.m_completionPaymentSecondary == null ? "null" : job.m_completionPaymentSecondary.Value.ToString());
            Add(sb, "bonusTasks", () => job.m_bonusTasks == null ? "null" : job.m_bonusTasks.Count.ToString());
            Add(sb, "loadout", () => job.m_loadout == null ? "null" : (job.m_loadout.m_entries == null ? "entries=null" : $"entries={job.m_loadout.m_entries.Count}"));
            Add(sb, "useLoadout", () => job.m_useLoadout.ToString());
            Add(sb, "stages", () => job.m_stages == null ? "null" : job.m_stages.Length.ToString());
            Add(sb, "stage0.tasks", () => job.m_stages == null || job.m_stages.Length == 0 ? "-" : (job.m_stages[0].m_tasks == null ? "null" : job.m_stages[0].m_tasks.Length.ToString()));
            Add(sb, "task0.washTasks", () => job.m_stages == null || job.m_stages.Length == 0 || job.m_stages[0].m_tasks == null || job.m_stages[0].m_tasks.Length == 0 ? "-" : (job.m_stages[0].m_tasks[0].m_washTaskList == null ? "null" : job.m_stages[0].m_tasks[0].m_washTaskList.Count.ToString()));
            var loc = job.Location;
            if (loc == null) { sb.Append("location=null"); return sb.ToString(); }
            Add(sb, "scene", () => loc.SceneName ?? "null");
            Add(sb, "sceneRef", () => Ref(loc.SceneAssetReference));
            Add(sb, "swd", () => loc.SceneWashableData == null ? "null" : $"{loc.SceneWashableData.name}(objs={(loc.SceneWashableData.m_washableObjects == null ? -1 : loc.SceneWashableData.m_washableObjects.Length)},comps={(loc.SceneWashableData.m_washableComponents == null ? -1 : loc.SceneWashableData.m_washableComponents.Length)})");
            Add(sb, "shaders", () => Ref(loc.SceneShaderCollectionAsset));
            Add(sb, "address", () => Loc(loc.LocalizedAddress));
            Add(sb, "loadingInfo", () => Loc(loc.LocalizedLoadingInfo));
            Add(sb, "loadingImg", () => Ref(loc.LoadingScreenImageAsset));
            Add(sb, "previewImg", () => Ref(loc.PreviewImageAsset));
            Add(sb, "chatImg", () => Ref(loc.ChatImageAsset));
            Add(sb, "thumbA", () => Ref(loc.SelectedThumbnailImageAsset));
            Add(sb, "thumbB", () => Ref(loc.DeselectedThumbnailImageAsset));
            return sb.ToString();
        }

        private static void Add(StringBuilder sb, string key, Func<string> f)
        {
            string v;
            try { v = f() ?? "null"; } catch (Exception ex) { v = $"<{ex.GetType().Name}>"; }
            sb.Append(key).Append('=').Append(v).Append(' ');
        }

        private static string Ref(AssetReference r)
        {
            if (r == null) return "null";
            var g = r.AssetGUID;
            return string.IsNullOrEmpty(g) ? "empty" : g;
        }

        private static string Loc(LocalizedString s)
        {
            if (s == null) return "null";
            try
            {
                if (s.IsEmpty) return "empty";
                var key = s.TableEntryReference.Key;
                return $"{s.TableReference.TableCollectionName}/{(string.IsNullOrEmpty(key) ? s.TableEntryReference.KeyId.ToString() : key)}";
            }
            catch (Exception ex) { return $"<{ex.GetType().Name}>"; }
        }
    }
}
