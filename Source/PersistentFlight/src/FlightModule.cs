using System;
using UnityEngine;

namespace PersistentFlight
{
    public class ModulePersistentFlight : PartModule
    {
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Autonomous physics"), UI_Toggle(enabledText = "Armed", disabledText = "Off")]
        public bool armed = false;
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Physics radius", guiUnits = " km"), UI_FloatRange(minValue = 5, maxValue = 60, stepIncrement = 5)]
        public float rangeKm = 30;
        [KSPField(guiActive = true, guiName = "Flight simulation")]
        public string status = "Off";
        [KSPEvent(guiActive = true, guiName = "Cancel background burn")]
        public void CancelBurn()
        {
            if (PersistentFlightScenario.Instance != null && vessel != null)
                PersistentFlightScenario.Instance.Cancel(vessel.id);
        }
        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) return;
            status = armed ? (vessel.packed ? "Packed" : "Live physics") : "Off";
            if (PersistentFlightScenario.Instance != null)
            {
                BurnJob job = PersistentFlightScenario.Instance.Find(vessel.id);
                if (job != null) status = job.Status;
            }
        }
    }
}
