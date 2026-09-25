// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FuturLab.PW2.TexturePainter
{
    public class TexturePalette : ScriptableObject
    {
        [SerializeField] public List<AssetReferenceTextureLayer> m_paletteAddressables = new List<AssetReferenceTextureLayer>();
        [SerializeField] public TextureLayer[] m_palette = new TextureLayer[0];
        [SerializeField] public bool m_dirtLayersLocked;
    }

    [Serializable]
    public class AssetReferenceTextureLayer : AssetReferenceT<TextureLayer>
    {
        public AssetReferenceTextureLayer(string guid) : base(guid) { }
    }
}
