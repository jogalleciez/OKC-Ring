// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

namespace FuturLab.PW2
{
    public enum GameMode { None = 0, Career = 1, FreePlay = 2, Hub = 3, BonusJob = 4 }
    public enum ContentStatus { Hidden = 0, Visible = 100, Available = 200, Owned = 300, InProgress = 400, Complete = 500 }
    public enum CharacterStance { Standing = 0, Crouching = 1, Laying = 2 }
    public enum DirtTier { None = 0, Tier_1_Surface = 1, Tier_2_Encrusted = 2, Tier_3_Embedded = 3, Tier_4_Tough = 4, Tier_5_Stubborn = 5, Tier_6_Ingrained = 6, Tier_7_Oily = 7 }
}
