// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FuturLab.Addressables
{
    [Serializable]
    public class AssetReferenceShaderVariantCollection : AssetReferenceT<ShaderVariantCollection>
    {
        public AssetReferenceShaderVariantCollection(string guid) : base(guid) { }
    }
}
