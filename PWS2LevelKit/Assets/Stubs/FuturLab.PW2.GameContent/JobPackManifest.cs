// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System;
using System.Collections.Generic;
using FuturLab.Network;
using FuturLab.PW2.MessagingSystem;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FuturLab.PW2
{
    [Serializable]
    public struct CampaignPlayerPrefab
    {
        [field: SerializeField] public AssetReferenceGameObject Prefab { get; set; }
        [field: SerializeField] public XRSDKConditional Condition { get; set; }
    }

    [Serializable]
    public struct HUDCustomizationData
    {
        [field: SerializeField] public EnumMappings<CharacterStance, AssetReferenceSprite> LoadoutStanceSprites { get; set; }
    }

    public class JobPackManifest : ScriptableObject
    {
        [SerializeField] public CampaignPlayerPrefab[] m_playerPrefabs = new CampaignPlayerPrefab[0];
        [SerializeField] public NetworkPrefabReference[] m_networkPrefabs = new NetworkPrefabReference[0];
        [SerializeField] public List<GameJob> m_gameJobs = new List<GameJob>();
        [SerializeField] public List<MessageSequenceData> m_messageSequences = new List<MessageSequenceData>();
        [SerializeField] public List<PowerWasherData> m_powerWashers = new List<PowerWasherData>();
        [field: SerializeField] public HUDCustomizationData HUDCustomization { get; set; } = new HUDCustomizationData { LoadoutStanceSprites = new EnumMappings<CharacterStance, AssetReferenceSprite>() };
    }
}
