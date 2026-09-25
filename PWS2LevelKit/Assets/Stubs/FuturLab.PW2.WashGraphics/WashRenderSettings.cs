// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using UnityEngine;

namespace FuturLab.PW2.WashGraphics
{
    public class WashRenderSettings : ScriptableObject
    {
        public enum RenderProfileType { Disabled = 0, RenderProfile = 1, WetnessProfile = 2, SoapProfile = 3, WetnessAndSoapProfile = 4 }
    }
}
