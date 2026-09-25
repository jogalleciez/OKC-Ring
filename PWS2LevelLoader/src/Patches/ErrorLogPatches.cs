using HarmonyLib;
using Il2CppFuturLab.PW2;

namespace PWS2LevelLoader.Patches
{
    /// <summary>
    /// Level-load failures become a popup and a reload; this reference-only hook surfaces the message.
    /// Do NOT hook FuturLogger.Error: patching it produced thousands of empty calls at boot and killed the game.
    /// Unity's own log is captured via UserData/Loader.cfg capture_player_logs = true.
    /// </summary>
    [HarmonyPatch(typeof(LoadingLevelStateBase), nameof(LoadingLevelStateBase.ExitWithError))]
    internal static class LoadingLevelStateBase_ExitWithError_Patch
    {
        private static void Prefix(LoadingLevelStateBase __instance, string localizedMessage)
            => Core.Log.Error($"[game] LoadingLevelStateBase.ExitWithError ({__instance?.GetIl2CppType().Name}): {localizedMessage}");
    }
}
