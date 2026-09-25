// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

namespace FuturLab.PW2
{
    [Flags]
    public enum UnlockableRuleType { NONE = 0, JOB = 1, EQUIPMENT = 2, WASHPROGRESS = 4, SHOP = 8, COSMETICS = 16, CAMPAIGN = 32, PLAYFAB = 64, PRESTIGE = 128, WASHTARGETPROGRESS = 256, WASHTARGETCOMPLETE = 512, INTERACTION = 1024, JOBSTAGE = 2048, POSITION = 4096, SOAPCOUNT = 8192, TROPHY = 16384, GAMEMODE = 32768, SETTINGCHANGED = 65536, WASHTARGETHIT = 131072, ENVIRONMENT = 262144 }

    [Serializable]
    public class UnlockRule
    {
        [SerializeField] public UnlockableRuleType m_unlockableRuleType;
    }

    public abstract class GameContent : ScriptableObject
    {
        [SerializeField] public bool m_unlockedByDefault;
        [SerializeField] public UnlockRule[] m_unlockRules = new UnlockRule[0];
        [SerializeField] public List<int> m_unlockRuleTargetIds = new List<int>();
        [SerializeField] public int m_uniqueId;
        [SerializeField] public string m_uniqueName;
        [SerializeField] public PlatformConditional m_availability = PlatformConditional.Default;
        [SerializeField] public LocalizedString m_localizedName;
        [SerializeField] public LocalizedString m_localizedDescription;
    }
}
