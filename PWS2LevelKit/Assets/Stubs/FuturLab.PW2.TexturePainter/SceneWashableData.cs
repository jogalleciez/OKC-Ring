// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FuturLab.PW2.TexturePainter
{
    public class SceneWashableData : ScriptableObject
    {
        [SerializeField] public WashableObjectData[] m_washableObjects = new WashableObjectData[0];
        [SerializeField] public WashableComponentData[] m_washableComponents = new WashableComponentData[0];
    }

    [Serializable]
    public class WashableObjectData
    {
        [SerializeField] public string m_name;
        [SerializeField] public int m_uniqueId;
        [SerializeField] public int m_parentId;
        [SerializeField] public string m_fullName;
        [SerializeField] public List<int> m_componentUniqueIds = new List<int>();
    }

    [Serializable]
    public class WashableComponentData
    {
        [SerializeField] public string m_name;
        [SerializeField] public int m_uniqueId;
        [SerializeField] public int m_parentObjectUniqueId;
        [SerializeField] public string m_superTaskName = "";
        [SerializeField] public bool m_isSuperTaskMember;
        [SerializeField] public List<int> m_superTaskIds = new List<int>();
        [SerializeField] public int m_startDirtiness;
        [SerializeField] public List<int> m_groupComponentUniqueIds = new List<int>();
        [SerializeField] public byte m_groupIndex;
        [SerializeField] public string m_cleanName;
    }
}
