// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System.Collections.Generic;
using FuturLab.PW2.MessagingSystem;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;

namespace FuturLab.PW2
{
    public enum SaveSize { None = 0, Tiny = 1, Small = 2, Medium = 3, Large = 4, ExtraLarge = 5 }
    public enum DevelopmentStage { InDevelopment = 0, TestReady = 1, ProductionReady = 2 }

    public class GameJob : GameContent
    {
        [field: SerializeField] public int CampaignID { get; set; }
        [SerializeField] public SaveSize m_saveSize = SaveSize.Small;
        [SerializeField] public OverridableProperty<TimelapseQualityConfig> m_timelapseQualityOverride = new OverridableProperty<TimelapseQualityConfig> { Value = new TimelapseQualityConfig() };
        [SerializeField] public GameJobCustomer m_customer;
        [SerializeField] public AssetReferenceSprite m_timelapseStaticThumbnail;
        [field: SerializeField] public bool IsTestJob { get; set; }
        [field: SerializeField] public Location Location { get; set; } = new Location();
        [SerializeField] public GameJobStage[] m_stages = new GameJobStage[0];
        [SerializeField] public List<GameJobBonusTask> m_bonusTasks = new List<GameJobBonusTask>();
        [SerializeField] public PrimaryCurrencyValue m_completionPayment;
        [SerializeField] public MuckCoinValue m_completionPaymentSecondary;
        [SerializeField] public bool m_useLoadout;
        [SerializeField] public Loadout m_loadout = new Loadout { m_entries = new List<LoadoutEntry>() };
        [field: SerializeField] public bool CustomizationDisabled { get; set; }
        [SerializeField] public DevelopmentStage m_levelDevelopmentStage = DevelopmentStage.ProductionReady;
        [SerializeField] public List<MessageSequenceData> m_allMessageSequences = new List<MessageSequenceData>();
        [field: SerializeField] public MessageSequenceData BriefMessageSequence { get; set; }
        [field: SerializeField] public MessageSequenceData CompletionMessageSequence { get; set; }
        [SerializeField] public string m_stringTableName = "";
        [SerializeField] public string m_localizationId;
        [SerializeField] public LocalizedString m_localizedFreePlayInteraction;
        [SerializeField] public LocalizedString m_storyNameInteraction;
        [SerializeField] public LocalizedString m_freePlayNameInteraction;
    }
}
