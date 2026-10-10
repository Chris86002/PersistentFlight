using System.Collections;
using System.Linq;
using UnityEngine;

namespace PersistentFlight
{
    // Also covers programmatic game loads which bypass the main menu's scenario migration.
    [KSPAddon(KSPAddon.Startup.EveryScene,false)]
    public class ScenarioBootstrap : MonoBehaviour
    {
        public IEnumerator Start()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.SPACECENTER && HighLogic.LoadedScene != GameScenes.TRACKSTATION) yield break;
            yield return null; yield return null;
            if (HighLogic.CurrentGame == null || ScenarioRunner.Instance == null) yield break;
            ProtoScenarioModule proto = HighLogic.CurrentGame.scenarios.FirstOrDefault(s=>s.moduleName==typeof(PersistentFlightScenario).Name);
            if(proto==null)
                proto=HighLogic.CurrentGame.AddProtoScenarioModule(typeof(PersistentFlightScenario),new [] {GameScenes.FLIGHT,GameScenes.SPACECENTER,GameScenes.TRACKSTATION});
            if(PersistentFlightScenario.Instance==null && proto!=null) proto.Load(ScenarioRunner.Instance);
        }
    }
}
