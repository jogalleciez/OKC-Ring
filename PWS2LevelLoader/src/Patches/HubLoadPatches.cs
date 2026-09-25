using System;
using HarmonyLib;
using Il2CppFuturLab.PW2;

namespace PWS2LevelLoader.Patches
{
    /// <summary>
    /// Progress markers along the hub load pipeline (LoadingHubLevelState.Load). Only parameterless or
    /// reference-only methods are patched (see docs/HANDOFF.md hard-won rules): they show how far the load
    /// gets before ExitWithError fires, and which boot systems the hub container creates.
    /// </summary>
    [HarmonyPatch(typeof(LoadingHubLevelState), nameof(LoadingHubLevelState.BuildContainer))]
    internal static class LoadingHubLevelState_BuildContainer_Patch
    {
        private static void Prefix(LoadingHubLevelState __instance,
            Il2CppSystem.Collections.Generic.IReadOnlyCollection<UnityEngine.GameObject> prefabInstances)
        {
            int n = -1;
            try { n = prefabInstances == null ? 0 : prefabInstances.Count; } catch { }
            Core.Log.Msg($"[hubload] BuildContainer: {n} prefab instance(s)");
        }

        private static void Postfix(Il2CppSystem.Collections.Generic.List<IBootSystem> __result)
        {
            try
            {
                if (__result == null) { Core.Log.Msg("[hubload] BuildContainer -> null"); return; }
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < __result.Count; i++)
                {
                    var s = __result[i];
                    names.Append(i == 0 ? "" : ", ").Append(s == null ? "null" : (s.TryCast<Il2CppSystem.Object>()?.GetIl2CppType().Name ?? "?"));
                }
                Core.Log.Msg($"[hubload] BuildContainer -> {__result.Count} boot system(s): {names}");
            }
            catch (Exception ex) { Core.Log.Warning($"[hubload] BuildContainer postfix failed: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(LoadingHubLevelState), nameof(LoadingHubLevelState.CreateGameplayState))]
    internal static class LoadingHubLevelState_CreateGameplayState_Patch
    {
        private static void Prefix() => Core.Log.Msg("[hubload] CreateGameplayState");
    }

    [HarmonyPatch(typeof(LoadingLevelStateBase), nameof(LoadingLevelStateBase.StartPlayingLevel))]
    internal static class LoadingLevelStateBase_StartPlayingLevel_Patch
    {
        private static void Prefix(LoadingLevelStateBase __instance) => Core.Log.Msg($"[hubload] StartPlayingLevel ({__instance?.GetIl2CppType().Name})");
    }
}
