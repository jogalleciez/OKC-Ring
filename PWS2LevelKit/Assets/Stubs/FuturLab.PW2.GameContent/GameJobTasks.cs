// STUB: mirrors the serialized layout of the PowerWash Simulator 2 type of the same name (v1.3.0).
// Field names, types and order come from docs/phase0-findings.md; behaviour is intentionally absent.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FuturLab.PW2
{
    [Serializable]
    public class GameJobStage
    {
        [SerializeField] public string m_uniqueName = "";
        [SerializeField] public bool m_hideNameWhenLocked;
        [SerializeField] public string m_localizationKey = "";
        [SerializeField] public GameJobTask[] m_tasks = new GameJobTask[0];
        [SerializeField] public bool m_delayLoadingWashMaps;
    }

    [Serializable]
    public class GameJobTask
    {
        [SerializeField] public bool m_isDefault;
        [SerializeField] public int m_washableObjectUniqueId;
        [SerializeField] public int m_parentObjectUniqueId;
        [SerializeField] public string m_previousWashableObjectUniqueName = "";
        [SerializeField] public MuckCoinValue m_bonusPayment;
        [SerializeField] public GameJobWashTaskGroupData m_washTasks = new GameJobWashTaskGroupData();
        [SerializeField] public List<GameJobWashTask> m_washTaskList = new List<GameJobWashTask>();
        [SerializeField] public int m_washTaskCount;
        [SerializeField] public string m_TaskUniqueName = "";
        [SerializeField] public string m_localizationKey = "";
    }

    [Serializable]
    public class GameJobWashTaskGroupData
    {
        [SerializeField] public List<GameJobWashTask> m_washTasks = new List<GameJobWashTask>();
    }

    [Serializable]
    public class GameJobWashTask
    {
        [SerializeField] public int m_washableComponentUniqueId;
        [SerializeField] public PrimaryCurrencyValue m_payment;
        [SerializeField] public string m_localizationKey = "";
        [SerializeField] public string m_componentLocalizationKey = "";
        [SerializeField] public string m_componentGroupLocalizationKey = "";
        [SerializeField] public string m_superTaskLocalizationKey = "";
        [SerializeField] public int m_parentWashableObjectUniqueId;
        [SerializeField] public string m_superTaskName = "";
        [SerializeField] public List<int> m_superTaskWashTargetIds = new List<int>();
    }

    [Serializable]
    public struct BonusTaskSettings
    {
        [field: SerializeField] public MuckCoinValue MuckCoinValue { get; set; }
        [field: SerializeField] public float TargetFillDuration { get; set; }
        [field: SerializeField] public float TimeToClean { get; set; }
    }

    [Serializable]
    public class GameJobBonusTask
    {
        [SerializeField] public BonusTaskSettings m_taskSettings;
        [SerializeField] public int m_uniqueId;
    }
}
