// Every PWS2 level scene (hub and jobs alike) carries a "GameplayBoot" object:
//   GameplayBoot            (layer System, tag System)
//     Boot                  PrefabLoader: 46 entries, instantiate-time 0.0667 s/frame
//     GameplaySpecificBoot  PrefabLoader: parent = Boot, 1 entry, instantiate-time 0
// LoadingLevelStateBase.CreateLevelContainer awaits these loaders and registers every instantiated prefab
// (GameplayAudioManager, GraphicsPipelineManager, DroneAssetInspectorManager, cats, ...) into the level's
// VContainer scope; SpawnPlayers then injects them into the player. A pack scene without them fails with
// "No such registration of type: FuturLab.PW2.GameplayAudioManager".
//
// The GUIDs below were captured from the game's own scenes (v1.3.0, PWS2LevelLoader "[prefabs]" log lines,
// 2026-09-17). Entries whose PlatformConditional excludes PC_Steam / Master builds are omitted (the game
// instantiates exactly 28 = 27 + 1 of the 47 entries on PC Steam). Re-capture after a game update (unchanged in 1.3.1).
using System.Linq;
using FuturLab;
using UnityEditor;
using UnityEngine;

namespace PWS2LevelKit.Editor
{
    public static class GameplayBoot
    {
        public const int SystemLayer = 11;
        public const string SystemTag = "System";

        /// <summary>GameplayBoot/Boot entries that apply to PC Steam release builds, in the game's order.</summary>
        public static readonly string[] BootPrefabGuids =
        {
            "9affe9c8c4211aa41a4b7497a34216a3", // [0]  Platform -495 (incl. PC_Steam)
            "de8996ee7a52815499ddcb001258d4bb", // [1]  All (AlwaysTrueOnEditor)
            "e71c3e9f98cfe5a4799d9aa788dcf1f3", // [2]  PC_Steam
            "f68ca39d379696b4992e95c3677ae14b", // [4]
            "0311beb96678f014abbe35274af76bda", // [5]
            "44bedbf0d734efc4990650bf78853c6d", // [6]
            "23ba9f3fe003fbc4db6ba22cce6bc021", // [7]
            "d48950ef3c22f5e44a01fe5e3ca73762", // [8]
            "1f1c33a9cc7ff644f84b7bac34ef4b1b", // [9]
            "f6e261587eaa2cb4e9c39d4b5d0d07ec", // [10]
            "4daa9f328ac5dde48b8418f7cf29e253", // [11]
            "6465e121715c316438393e4f80318667", // [12]
            "ea52bcc816a72e04785b4d1f0b69d824", // [13]
            "abb9571c81663bb42ae326d1699dcc56", // [14]
            "fde76335a2985df4089d48056b39cf05", // [18] PC_Steam
            "aea3a21759899ad498da01d3607ec85d", // [20]
            "ca52ae958580a5f44a70400a9e1a7c1e", // [21]
            "1899cab5a33c37045b0157051ce28e06", // [23] Platform -17 (not Android)
            "f6364843265f1274aa3a0acb2ff39817", // [26]
            "0e71506e0b776db4ba4d188c0f140ee9", // [28]
            "3d398edcf59f40145aed7dd7ed03effc", // [30]
            "dfc1fcac27875cc4d9d5a7c4eb74eb74", // [33]
            "467fdccca0d51d54dba5376ef99fa05e", // [37]
            "7eb011728aba47447b9bb84a12b23a55", // [38]
            "b6e3441a88d4415418339d13fb898433", // [42]
            "8a8781d5e46063946a5d03d16f7f63b5", // [43] Platform -2097 (not Switch2/iOS/Android)
            "316b02ab765e7624e98eac037cb3a862", // [44]
        };

        /// <summary>GameplayBoot/GameplaySpecificBoot entries (same single entry in the hub and in Billboard).</summary>
        public static readonly string[] GameplaySpecificPrefabGuids =
        {
            "1becfa1ff362bdc4ea5557f605d7cb1e",
        };

        /// <summary>Creates the GameplayBoot hierarchy in the open scene and returns its root.</summary>
        public static GameObject Create()
        {
            EnsureTag(SystemTag);

            var root = new GameObject("GameplayBoot");
            root.layer = SystemLayer; root.tag = SystemTag;

            var boot = new GameObject("Boot");
            boot.transform.SetParent(root.transform, false);
            boot.layer = SystemLayer; boot.tag = SystemTag;
            var bootLoader = boot.AddComponent<PrefabLoader>();
            bootLoader.m_instantiateTimePerFrame = 0.0667f;
            bootLoader.m_prefabsToLoad = BootPrefabGuids.Select(g => new PrefabToLoad(g)).ToArray();

            var specific = new GameObject("GameplaySpecificBoot");
            specific.transform.SetParent(root.transform, false);
            specific.layer = SystemLayer;
            var specificLoader = specific.AddComponent<PrefabLoader>();
            specificLoader.m_parentLoader = bootLoader;
            specificLoader.m_instantiateTimePerFrame = 0f;
            specificLoader.m_prefabsToLoad = GameplaySpecificPrefabGuids.Select(g => new PrefabToLoad(g)).ToArray();
            return root;
        }

        static void EnsureTag(string tag)
        {
            if (UnityEditorInternal.InternalEditorUtility.tags.Contains(tag)) return;
            UnityEditorInternal.InternalEditorUtility.AddTag(tag);
        }
    }
}
