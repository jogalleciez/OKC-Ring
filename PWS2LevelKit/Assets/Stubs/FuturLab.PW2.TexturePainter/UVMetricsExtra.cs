// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using UnityEngine;

namespace FuturLab.PW2
{
    public class UVMetricsExtra : MonoBehaviour
    {
        [field: SerializeField] public float UV0DistributionMetricUnity { get; set; }
        [field: SerializeField] public float UV0DistributionMetric { get; set; }
        [field: SerializeField] public float UV1DistributionMetricUnity { get; set; }
        [field: SerializeField] public float UV1DistributionMetric { get; set; }
        [field: SerializeField] public float UV2DistributionMetric { get; set; }
        [field: SerializeField] public float UV3DistributionMetric { get; set; }
    }
}
