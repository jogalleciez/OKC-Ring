// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FuturLab.PW2.TexturePainter
{
    public class WashTargetData : ScriptableObject
    {
        [field: SerializeField] public int SuggestedControlMapResolution { get; set; } = 1024;
        [field: SerializeField] public AssetReferenceTexture2D ControlMapAsset { get; set; }
        [field: SerializeField] public RectInt ControlMapAreaInUse { get; set; }
        [field: SerializeField] public Rect ControlMapTotalRect { get; set; }
        [field: SerializeField] public RectInt ControlMapTrimmedRect { get; set; }
        [field: SerializeField] public TexturePalette TexturePalette { get; set; }
        [field: SerializeField] public bool OverrideSurfaceCleanThreshold { get; set; }
        [field: SerializeField] public float SurfaceCleanThreshold { get; set; } = 0.1f;
        [field: SerializeField] public Material TemplatePaintLayerMaterial { get; set; }
        [field: SerializeField] public PaintOpacityConfig OpacityCurveConfig { get; set; }
        [field: SerializeField] public bool ConfirmedByDesigner { get; set; }
        [field: SerializeField] public bool ApprovedByDesign { get; set; }
    }
}
