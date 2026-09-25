// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using UnityEngine;

namespace FuturLab.PW2
{
    public enum CharacterSpawnPositionType { Main = 1, Secondary = 2, Emergency = 4 }

    public class CharacterSpawnPosition : MonoBehaviour
    {
        [SerializeField] public CharacterSpawnPositionType m_type = CharacterSpawnPositionType.Main;
        [SerializeField] public LayerMask m_obstructionMask = 8672513;
        [SerializeField] public float m_Radius = 0.5f;
        [SerializeField] public float m_Height = 1.4f;
        [SerializeField] public int m_ObstructionFixIterationCount = 50;
        [SerializeField] public Vector3 m_InitialPostion;
        [SerializeField] public float m_MaxFixDistanceAllowed = 1f;
        [field: SerializeField] public int UnlockAtStage { get; set; }
    }
}
