// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System;
using UnityEngine;

namespace FuturLab
{
    [Flags] public enum Platform { PC_Steam = 1, PS4 = 2, Switch = 4, Android = 16, iOS = 32, PS5 = 64, XboxSeries = 128, PC_GRDK = 256, PC_Other = 512, PC_EOS = 1024, Switch2 = 2048, PC_All = 1793, Console_All = 2246, Non_XR = 4039, All = -1 }
    [Flags] public enum ReleaseType { Debug = 1, Staging = 2, Master = 4, All = -1 }
    public enum EditorModifier { SameAsOnBuilds = 0, AlwaysTrueOnEditor = 1, AlwaysFalseOnEditor = 2 }

    [Serializable]
    public struct PlatformConditional
    {
        public Platform Platform;
        public ReleaseType ReleaseType;
        public EditorModifier EditorModifier;
        public static PlatformConditional Default => new PlatformConditional { Platform = Platform.All, ReleaseType = ReleaseType.All, EditorModifier = EditorModifier.SameAsOnBuilds };
    }

    [Serializable]
    public class Conditional
    {
        public EditorModifier EditorModifier;
    }

    [Serializable]
    public class XRSDKConditional : Conditional
    {
        public enum XRPlatform { None = 0, OpenXR = 1, visionOS = 2 }
        [SerializeField] public XRPlatform m_enabledXRSDKs;
    }
}
