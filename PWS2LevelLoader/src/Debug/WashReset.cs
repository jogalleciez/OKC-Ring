using System;
using Il2CppFuturLab.PW2;
using Il2CppFuturLab.PW2.TexturePainter;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PWS2LevelLoader.Debug
{
    /// <summary>
    /// Dev helper: puts the dirt back on every wash target of a pack scene for the running session
    /// (WashManager.ResetTarget), so an automated run can look at the dirty level even when the save already
    /// holds it clean. Calls the async method and lets it run; nothing is patched. Opt-in via AutoResetPackTargets.
    /// </summary>
    internal static class WashReset
    {
        /// <param name="onlyRoots">if set, only targets whose GameJobWashTargetRoot has one of these names (a pack job on a game scene)</param>
        public static void ResetPackTargets(string sceneName, System.Collections.Generic.ICollection<string> onlyRoots = null)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded) { Core.Log.Warning($"WashReset: '{sceneName}' not loaded"); return; }

            WashManager wm = null;
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<PlayerCharacter>()))
            {
                var pc = o?.TryCast<PlayerCharacter>();
                if (pc != null && pc.gameObject.scene.IsValid() && pc.m_washManager != null) { wm = pc.m_washManager; break; }
            }
            if (wm == null) { Core.Log.Warning("WashReset: no PlayerCharacter with a WashManager"); return; }

            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                foreach (var c in root.GetComponentsInChildren(Il2CppType.Of<WashTarget>(), true))
                {
                    var wt = c?.TryCast<WashTarget>();
                    if (wt == null) continue;
                    if (onlyRoots != null && (wt.m_gameJobWashTargetRoot == null || !onlyRoots.Contains(wt.m_gameJobWashTargetRoot.gameObject.name))) continue;
                    try { wm.ResetTarget(wt, Il2CppSystem.Threading.CancellationToken.None); n++; }
                    catch (Exception ex) { Core.Log.Warning($"WashReset: {wt.name}: {ex.GetType().Name}: {ex.Message}"); }
                }
            }
            Core.Log.Msg($"WashReset: reset requested for {n} wash target(s) in '{sceneName}'");
        }
    }
}
