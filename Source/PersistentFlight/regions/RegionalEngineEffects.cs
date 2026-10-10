using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace PersistentFlight.Regions
{
    // A scene handoff can restart legacy flame systems without changing the
    // FXGroup.Active flag. Stock DeactivateRunningFX then skips them forever.
    // Reconcile only idle stock engine effects; never change engine controls.
    public static class RegionalEngineEffects
    {
        const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly FieldInfo effectList=typeof(EffectList).GetField("effectList",Fields);
        static readonly FieldInfo singleEmitter=typeof(ModelParticleFX).GetField("emitter",Fields);
        static readonly FieldInfo multipleEmitters=typeof(ModelMultiParticleFX).GetField("emitters",Fields);
        public static int Corrections { get; private set; }
        public static void Reconcile(Vessel vessel)
        {
            if(vessel==null || !vessel.loaded || vessel.packed) return;
            foreach(var engine in vessel.parts.SelectMany(p=>p.Modules.OfType<ModuleEngines>()))
            {
                if(engine.GetType()!=typeof(ModuleEngines) && engine.GetType()!=typeof(ModuleEnginesFX)) continue;
                // Use the individual engine state: a stopped outer engine may
                // share a vessel with a firing centre engine at full throttle.
                if(engine.finalThrust!=0 || engine.currentThrottle>0.00001f) continue;
                foreach(var group in Groups(engine).Where(g=>g!=null && !g.Active).Distinct())
                    foreach(var particles in group.fxEmittersNewSystem)
                        if(particles!=null && (particles.isEmitting || particles.particleCount>0))
                        {
                            particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                            Corrections++;
                        }
                var modern=engine as ModuleEnginesFX;
                if(modern==null || engine.part.Effects==null || effectList==null) continue;
                var namedEffects=effectList.GetValue(engine.part.Effects) as Dictionary<string,List<EffectBehaviour>>;
                if(namedEffects==null) continue;
                foreach(var name in new[]{modern.runningEffectName,modern.powerEffectName,
                    modern.directThrottleEffectName,modern.spoolEffectName}.Where(n=>!String.IsNullOrEmpty(n)).Distinct())
                {
                    List<EffectBehaviour> effects;
                    if(!namedEffects.TryGetValue(name,out effects)) continue;
                    foreach(var effect in effects)
                    {
                        var single=effect as ModelParticleFX;
                        if(single!=null && singleEmitter!=null) ClearIdle(singleEmitter.GetValue(single) as KSPParticleEmitter);
                        var multiple=effect as ModelMultiParticleFX;
                        if(multiple!=null && multipleEmitters!=null)
                        {
                            var emitters=multipleEmitters.GetValue(multiple) as List<KSPParticleEmitter>;
                            if(emitters!=null) foreach(var emitter in emitters) ClearIdle(emitter);
                        }
                    }
                }
            }
        }
        static IEnumerable<FXGroup> Groups(ModuleEngines engine)
        {
            yield return engine.runningGroup; yield return engine.powerGroup;
            if(engine.runningGroups!=null) foreach(var group in engine.runningGroups) yield return group;
            if(engine.powerGroups!=null) foreach(var group in engine.powerGroups) yield return group;
        }
        static void ClearIdle(KSPParticleEmitter emitter)
        {
            // Modern effects emit manually. Preserve their playback state so
            // subsequent ignitions still work; exclude engage/flameout bursts.
            if(emitter!=null && (!emitter.emit || (emitter.minEmission==0 && emitter.maxEmission==0)) &&
                emitter.ps!=null && emitter.ps.particleCount>0)
            { emitter.ps.Clear(false); Corrections++; }
        }
    }
}
