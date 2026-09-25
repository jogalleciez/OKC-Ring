// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System.Collections.Generic;
using FuturLab.PW2.WashGraphics;
using UnityEngine;

namespace FuturLab.PW2.TexturePainter
{
    public class WashTarget : MonoBehaviour
    {
        [SerializeField] public Transform m_transform;
        [SerializeField] public bool m_canBeCleanedByClient;
        [SerializeField] public WashTargetData m_washTargetData;
        [SerializeField] public WashTargetRoot m_gameJobWashTargetRoot;
        [SerializeField] public UniqueId m_uniqueId;
        [SerializeField] public WashRenderSettings m_renderSettings;
        [SerializeField] public MeshRenderer m_meshRenderer;
        [SerializeField] public List<WashTarget> m_superTaskTargets = new List<WashTarget>();
    }
}
