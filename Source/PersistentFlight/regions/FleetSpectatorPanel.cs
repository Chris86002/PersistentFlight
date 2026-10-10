using System;
using System.Linq;
using UnityEngine;
using PersistentFlight;
namespace PersistentFlight.Regions {
    public sealed partial class RegionalFleetPanel {
        bool showSpectator;Vector2 spectatorScroll;int spectatorPage;
        static readonly int[] TourIntervals={5,15,30,60,120};
        void DrawSpectator(){
            var viewer=FleetSpectator.Instance;if(viewer==null){GUILayout.Label("Spectator is starting");return;}
            GUILayout.Label("Fleet spectator");
            GUILayout.Label(viewer.Status,new GUIStyle(GUI.skin.label){wordWrap=true});
            GUILayout.Label("Auto tour switches nearby coasting craft on this body. It waits during burns, approaching maneuvers, surface flight and warp.",new GUIStyle(GUI.skin.label){wordWrap=true});
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(viewer.Touring?"Pause tour":"Start auto tour"))Try(()=>{if(viewer.Touring)viewer.StopTour();else viewer.StartTour();},"Spectator tour updated.");
            if(GUILayout.Button("Interval: "+viewer.Interval.ToString("F0",C)+" s")){int i=Array.FindIndex(TourIntervals,n=>n==viewer.Interval);viewer.Interval=TourIntervals[(i+1)%TourIntervals.Length];}
            if(GUILayout.Button("Pin current craft"))Try(()=>viewer.PinCurrent(),"Current craft pinned in the status view.");
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();
            if(GUILayout.Button("Previous nearby"))Try(()=>viewer.NextLocal(-1),"Nearby focus requested.");
            if(GUILayout.Button("Next nearby"))Try(()=>viewer.NextLocal(1),"Nearby focus requested.");
            GUILayout.EndHorizontal();
            viewer.FollowFocus=GUILayout.Toggle(viewer.FollowFocus,"Status view follows the focused craft");
            var selectedCard=viewer.Cards.FirstOrDefault(c=>c.Id==viewer.Watched);
            if(selectedCard==null)GUILayout.Label("Choose a craft below to inspect its status.");
            else{
                GUILayout.BeginVertical(GUI.skin.box);GUILayout.Label(selectedCard.Name+" / "+selectedCard.Body+(selectedCard.Focused?" / Focused":" / Status view"));
                GUILayout.Label(selectedCard.Physics);GUILayout.Label(selectedCard.Phase,new GUIStyle(GUI.skin.label){wordWrap=true});GUILayout.Label(selectedCard.Mission,new GUIStyle(GUI.skin.label){wordWrap=true});
                GUILayout.Label("Altitude "+Distance(selectedCard.Altitude)+"   Orbit speed "+selectedCard.Speed.ToString("F0",C)+" m/s");
                GUILayout.Label("LF "+selectedCard.Fuel.ToString("F1",C)+" / "+selectedCard.FuelCapacity.ToString("F1",C)+"   EC "+selectedCard.Power.ToString("F1",C)+" / "+selectedCard.PowerCapacity.ToString("F1",C));
                if(!Double.IsNaN(selectedCard.Remaining))GUILayout.Label(selectedCard.Remaining.ToString("F2",C)+" m/s remaining");
                if(!String.IsNullOrEmpty(selectedCard.Alert))GUILayout.Label(selectedCard.Alert);
                if(!selectedCard.Loaded)GUILayout.Label("Saved CPU status; its script is asleep. Native orbit and stored tank amounts are shown.",new GUIStyle(GUI.skin.label){wordWrap=true});
                if(!selectedCard.Focused&&GUILayout.Button(selectedCard.Loaded&&selectedCard.Situation.Unpacked?"Focus this craft":"Visit this craft (reload fleet)"))Try(()=>viewer.VisitWatched(),"Native craft visit requested; tour paused.");
                if(PersistentFlightScenario.Instance!=null&&!FleetEventApi.IsHeld){
                    GUILayout.Label("Recent mission events");
                    foreach(var e in FleetEventApi.Timeline().Where(e=>e.VesselId==selectedCard.Id).Reverse().Take(4))GUILayout.Label("UT "+e.UT.ToString("F0",C)+" / "+e.EventName+": "+e.Detail,new GUIStyle(GUI.skin.label){wordWrap=true});
                }
                GUILayout.EndVertical();
            }
            int pages=Math.Max(1,(viewer.Cards.Length+7)/8);spectatorPage=Math.Min(spectatorPage,pages-1);
            GUILayout.BeginHorizontal();if(GUILayout.Button("Previous page"))spectatorPage=Math.Max(0,spectatorPage-1);GUILayout.Label("Fleet "+viewer.Cards.Length+" / page "+(spectatorPage+1)+" of "+pages);if(GUILayout.Button("Next page"))spectatorPage=Math.Min(pages-1,spectatorPage+1);GUILayout.EndHorizontal();
            foreach(var c in viewer.Cards.Skip(spectatorPage*8).Take(8)){
                if(GUILayout.Button((c.Id==viewer.Watched?"> ":"")+c.Name+" / "+c.Body+(c.Focused?" / Focused":"")))Try(()=>viewer.Watch(c.Id),"Status view selected; flight focus is unchanged.");
                GUILayout.Label(c.Physics+" / "+c.Mission,new GUIStyle(GUI.skin.label){wordWrap=true});
            }
        }
    }
}
