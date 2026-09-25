// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System;
using UnityEngine;

namespace FuturLab.PW2
{
    [Serializable] public struct PrimaryCurrencyValue { [field: SerializeField] public int Value { get; set; } }
    [Serializable] public struct MuckCoinValue { [field: SerializeField] public int Value { get; set; } }
}
