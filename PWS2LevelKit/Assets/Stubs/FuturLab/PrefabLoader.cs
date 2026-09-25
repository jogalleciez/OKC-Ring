// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from the IL2CPP proxy dump; behaviour is intentionally absent.
//
// Every game level scene carries PrefabLoader components (hub: GameplayBoot/Boot + GameplaySpecificBoot) whose
// entries name the level-system prefabs by Addressables GUID (GameplayAudioManager, GraphicsPipelineManager, ...).
// LoadingLevelStateBase.CreateLevelContainer instantiates them (ContainerUtil.WaitForPrefabLoader) and registers
// the instances into the level's VContainer scope; without them SpawnPlayers fails with
// "No such registration of type: FuturLab.PW2.GameplayAudioManager". A pack scene lists the game's own prefab GUIDs.

using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FuturLab
{
    [Serializable]
    public class PrefabToLoad
    {
        // plain public fields in the game (no k__BackingField twin in the IL2CPP proxy), so serialized under these names
        [SerializeField] public AssetReferenceGameObject PrefabAsset;
        [SerializeField] public bool InstantiateAtRootLevel;
        [SerializeField] public PlatformConditional[] m_executeConditions = new PlatformConditional[0];
        [SerializeField] public bool m_useXRCondition;
        [SerializeField] public XRSDKConditional m_xrExecuteCondition = new XRSDKConditional();

        public PrefabToLoad() { }
        /// <summary>One entry with a single "all platforms / all releases" condition, like the game's own entries.</summary>
        public PrefabToLoad(string prefabGuid, bool atRootLevel = true)
        {
            PrefabAsset = new AssetReferenceGameObject(prefabGuid);
            InstantiateAtRootLevel = atRootLevel;
            m_executeConditions = new[] { PlatformConditional.Default };
        }
    }

    public class PrefabLoader : MonoBehaviour
    {
        [SerializeField] public PrefabLoader m_parentLoader;
        [SerializeField] public PrefabToLoad[] m_prefabsToLoad = new PrefabToLoad[0];
        [SerializeField] public float m_instantiateTimePerFrame = 0.01f;
    }
}
