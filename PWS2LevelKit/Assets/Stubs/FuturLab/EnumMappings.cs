// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FuturLab
{
    /// <summary>Element layout unknown; only ever serialised as an empty list by PWS2LevelKit.</summary>
    [Serializable]
    public class Mapping<TEnum, TValue> { }

    [Serializable]
    public class EnumMappings<TEnum, TValue>
    {
        [SerializeField] public List<Mapping<TEnum, TValue>> m_mappings = new List<Mapping<TEnum, TValue>>();
    }
}
