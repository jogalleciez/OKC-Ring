// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System;
using System.Collections.Generic;
using FMODUnity;
using FuturLab.Addressables;
using FuturLab.PW2.TexturePainter;
using FuturLab.PW2.WashGraphics;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.Localization;

namespace FuturLab.PW2
{
    [Serializable]
    public class Location
    {
        [field: SerializeField] public SceneWashableData SceneWashableData { get; set; }
        [field: SerializeField] public string SceneName { get; set; }
        [field: SerializeField] public LocalizedString LocalizedAddress { get; set; }
        [field: SerializeField] public AssetReference SceneAssetReference { get; set; }
        [field: SerializeField] public AssetReferenceShaderVariantCollection SceneShaderCollectionAsset { get; set; }
        [field: SerializeField] public LocalizedString LocalizedLoadingInfo { get; set; }
        [field: SerializeField] public List<InputActionReference> LocalizedInputActions { get; set; } = new List<InputActionReference>();
        [field: SerializeField] public AssetReferenceSprite LoadingScreenImageAsset { get; set; }
        [field: SerializeField] public AssetReferenceSprite PreviewImageAsset { get; set; }
        [field: SerializeField] public AssetReferenceSprite ChatImageAsset { get; set; }
        [field: SerializeField] public AssetReferenceSprite SelectedThumbnailImageAsset { get; set; }
        [field: SerializeField] public AssetReferenceSprite DeselectedThumbnailImageAsset { get; set; }
        [field: SerializeField] public EventReference AmbienceSoundEvent { get; set; }
        [field: SerializeField] public float MaxMapHeight { get; set; } = 12f;
        [field: SerializeField] public bool HasWind { get; set; } = true;
        [field: SerializeField] public bool HasReverb { get; set; }
        [field: SerializeField] public bool OverrideSoapGlobalConfig { get; set; }
        [field: SerializeField] public SoapGlobalConfiguration RuntimeSoapGlobalConfig { get; set; }
        [field: SerializeField] public WashRenderSettings.RenderProfileType CustomRenderProfile { get; set; }
        [field: SerializeField] public WashRenderProfile RuntimeRenderProfile { get; set; }
        [field: SerializeField] public WashRenderWetnessProfile RuntimeWetnessRenderProfile { get; set; }
        [field: SerializeField] public WashRenderSoapProfile RuntimeSoapRenderProfile { get; set; }
    }
}
