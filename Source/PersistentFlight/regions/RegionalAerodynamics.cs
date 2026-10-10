using System;
using System.Linq;
using UnityEngine;

namespace PersistentFlight.Regions
{
    // Stock FI assumes every atmospheric craft uses the global rotating frame.
    // Regional craft can remain in atmosphere while the focused craft enters orbit.
    // Run the native aerodynamic calculation with atmosphere-relative velocity,
    // then restore the published inertial velocity before any other callback.
    // Stock FI's execution order is configured in Unity rather than its C# type.
    // A replacement must explicitly run after vessel and part-module updates.
    [DefaultExecutionOrder(9)]
    public sealed class RegionalFlightIntegrator : FlightIntegrator
    {
        public int AtmosphereFrameCorrections { get; private set; }
        public double LastAtmosphereSpeed { get; private set; }
        protected override void UpdateAerodynamics(Part part)
        {
            var runner=RegionFlightRunner.Instance;
            if(FlightGlobals.RefFrameIsRotating || part==null || part.vessel==null ||
                runner==null || !runner.Regions.Any(r=>r.Vessel==part.vessel && r.AllowsDescent) ||
                part.vessel.atmDensity<=0 || part.Rigidbody==null)
            { base.UpdateAerodynamics(part); return; }
            Rigidbody rb=part.Rigidbody;
            Vector3 saved=rb.velocity;
            Vector3d atmosphere=part.vessel.mainBody.getRFrmVel(rb.position);
            try
            {
                rb.velocity=(Vector3)((Vector3d)saved-atmosphere);
                base.UpdateAerodynamics(part);
                AtmosphereFrameCorrections++;
                LastAtmosphereSpeed=part.dragVector.magnitude;
            }
            finally { if(rb!=null) rb.velocity=saved; }
        }
    }
    [KSPAddon(KSPAddon.Startup.MainMenu,true)]
    public sealed class RegionalIntegratorRegistration : MonoBehaviour
    {
        public void Start()
        {
            var foreign=VesselModuleManager.GetModules(false,false).FirstOrDefault(w=>w.active &&
                w.type!=typeof(FlightIntegrator) && w.type!=typeof(RegionalFlightIntegrator) &&
                typeof(FlightIntegrator).IsAssignableFrom(w.type));
            if(foreign!=null)
            {
                VesselModuleManager.SetWrapperActive(typeof(RegionalFlightIntegrator),false);
                Debug.LogError("[PF-AERO] Foreign flight integrator detected: "+foreign.type+". Regional atmospheric frame correction is unavailable.");
                return;
            }
            VesselModuleManager.SetWrapperActive(typeof(FlightIntegrator),false);
            VesselModuleManager.SetWrapperActive(typeof(RegionalFlightIntegrator),true);
            Debug.Log("[PF-AERO] Registered native flight-integrator subclass; atmosphere-relative correction applies only to owned regional atmospheric craft in inertial frames.");
        }
    }
}
