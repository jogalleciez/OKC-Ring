// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System;
using UnityEngine;

namespace FMOD
{
    [Serializable]
    public struct GUID
    {
        public int Data1, Data2, Data3, Data4;

        /// <summary>Same byte packing FMOD Unity uses when converting a System.Guid.</summary>
        public static GUID FromGuid(System.Guid guid)
        {
            var b = guid.ToByteArray();
            return new GUID { Data1 = BitConverter.ToInt32(b, 0), Data2 = BitConverter.ToInt32(b, 4), Data3 = BitConverter.ToInt32(b, 8), Data4 = BitConverter.ToInt32(b, 12) };
        }
        public static GUID Parse(string s) => FromGuid(System.Guid.Parse(s.Trim('{', '}')));
    }
}

namespace FMODUnity
{
    [Serializable]
    public struct EventReference
    {
        [SerializeField] public FMOD.GUID Guid;
    }
}
