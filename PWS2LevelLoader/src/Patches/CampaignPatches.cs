using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppFuturLab.PW2;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PWS2LevelLoader.Packs;
using UnityEngine;

namespace PWS2LevelLoader.Patches
{
    /// <summary>
    /// Registers content packs by appending CampaignJobPackReferences to the default campaign BEFORE the game
    /// iterates them in Campaign.LoadContent. Campaign.LoadContent itself is never patched: it is async and takes a
    /// CancellationToken (non-blittable struct), and patching it through Il2CppInterop stalls the game's boot
    /// (see docs/phase1-spikes.md). Injection is idempotent and attempted from two places, whichever runs first:
    ///   - a per-frame poll during boot (Core.OnUpdate -> TryInjectAll) as soon as the Campaign objects exist,
    ///   - a prefix on Campaign.AreCampaignPacksInstalled (reference parameter only).
    /// The Campaign.JobPackReferences getter used to be a third hook; patching it crashes game 1.3.1.
    /// </summary>
    internal static class CampaignInjection
    {
        internal const string DefaultCampaignName = "DEFAULT";
        private static readonly HashSet<IntPtr> _injected = new HashSet<IntPtr>();
        private static int _pollFrame;
        public static bool Done { get; private set; }

        /// <summary>Poll (cheap: every 10th frame, only until done) for the default campaign object.</summary>
        public static void TryInjectAll()
        {
            if (Done || !Core.RegisterPacks.Value || PackRegistry.Packs.Count == 0) return;
            if ((++_pollFrame % 10) != 0) return;
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Campaign>()))
            {
                var c = o?.TryCast<Campaign>();
                if (c != null) TryInject(c, "poll");
            }
        }

        public static void TryInject(Campaign campaign, string via)
        {
            try
            {
                if (campaign == null || Done || !Core.RegisterPacks.Value) return;
                if (campaign.UniqueName != DefaultCampaignName) return;
                if (_injected.Contains(campaign.Pointer)) return;
                if (PackRegistry.Packs.Count == 0) { Done = true; return; }

                var refs = PackRegistry.BuildPackReferences();
                if (refs.Count == 0)
                {
                    // catalogs not ready yet: keep trying from the poll / next hook
                    return;
                }
                var existing = campaign.m_jobPackReferences;
                int n = existing?.Length ?? 0;
                // Campaign.m_loadedContentManifests is keyed by reference index and the game walks it as 0..Count-1
                // (Campaign.TryFindPowerWashersFromPacks threw KeyNotFoundException(2) when our reference sat at index 3
                // behind the never-loaded ContentPack_EditorAssets). Insert before the first reference the game will
                // skip so the loaded keys stay contiguous.
                int insertAt = n;
                for (int i = 0; i < n; i++) if (!WillLoad(existing[i])) { insertAt = i; break; }
                var merged = new Il2CppReferenceArray<CampaignJobPackReference>(n + refs.Count);
                for (int i = 0; i < insertAt; i++) merged[i] = existing[i];
                for (int i = 0; i < refs.Count; i++) merged[insertAt + i] = refs[i];
                for (int i = insertAt; i < n; i++) merged[i + refs.Count] = existing[i];
                campaign.m_jobPackReferences = merged;
                _injected.Add(campaign.Pointer);
                Done = true;
                Core.Log.Msg($"Campaign DEFAULT: inserted {refs.Count} pack reference(s) at index {insertAt} via {via} ({n} -> {merged.Length})");
            }
            catch (Exception ex)
            {
                Core.Log.Error($"Pack injection via {via} failed: {ex}");
            }
        }

        /// <summary>
        /// Mirrors the game's availability gate: a reference whose ContentPack is not available on this platform /
        /// release (e.g. ContentPack_EditorAssets: Platform=-4096, ReleaseType=0, editor only) is skipped by
        /// Campaign.LoadContent and gets no entry in m_loadedContentManifests.
        /// </summary>
        private static bool WillLoad(CampaignJobPackReference r)
        {
            try
            {
                var cp = r?.ContentPack;
                if (cp == null) return true;
                var avail = cp.Availability;
                if (avail == null) return true;
                var cond = avail.Condition;
                bool platformOk = (cond.Platform & Il2CppFuturLab.Platform.PC_Steam) != 0;
                bool releaseOk = cond.ReleaseType != 0;
                if (!platformOk || !releaseOk)
                    Core.Log.Msg($"Campaign DEFAULT: reference '{cp.ID}' is unavailable here (Platform={cond.Platform}, ReleaseType={cond.ReleaseType}); pack references go before it");
                return platformOk && releaseOk;
            }
            catch (Exception ex) { Core.Log.Warning($"WillLoad check failed: {ex.Message}"); return true; }
        }
    }

    [HarmonyPatch(typeof(Campaign), nameof(Campaign.AreCampaignPacksInstalled))]
    internal static class Campaign_AreCampaignPacksInstalled_Patch
    {
        private static void Prefix(Campaign __instance) => CampaignInjection.TryInject(__instance, "AreCampaignPacksInstalled");
    }

    // No patch on the Campaign.JobPackReferences getter: on game 1.3.1 it kills the process during the hub load
    // (0xc0000005 inside coreclr.dll on a Unity worker thread, even with no packs registered). See docs/HANDOFF.md.
}
