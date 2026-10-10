# Operating guides

These guides describe the actual 0.25 implementation and its limits.

- [Mission Control](../MISSION%20CONTROL.txt)
- [Mission Templates](../MISSION%20TEMPLATES.txt)
- [Goal Templates](../GOAL%20TEMPLATES.txt)
- [Circular Orbits](../CIRCULAR%20ORBITS.txt)
- [Recovery And Readiness](../RECOVERY%20AND%20READINESS.txt)
- [Predictive Safety](../PREDICTIVE%20SAFETY.txt)
- [Flight Interruptions](../FLIGHT%20INTERRUPTIONS.txt)
- [Flight Debriefs](../FLIGHT%20DEBRIEFS.txt)
- [Event Missions](../EVENT%20MISSIONS.txt)
- [Rover Exploration](../ROVER%20EXPLORATION.txt)
- [Landing Reports](../LANDING%20REPORTS.txt)
- [Fleet Spectator](../FLEET%20SPECTATOR.txt)
- [Fleet Scaling](../FLEET%20SCALING.txt)
- [Capacity Reservations](../CAPACITY%20RESERVATIONS.txt)
- [Soi Flight Plans](../SOI%20FLIGHT%20PLANS.txt)

For goal templates, work at normal time on an idle, bound same-body orbit. Supported plans require at least 1 km clearance above the atmosphere top (or nominal airless body radius), stay below 95% of the body's sphere of influence, and reject known body encounters before completion. Preview fuel is advisory. Inspect the native map plan before queueing it.

The circular goal can use a transfer burn and arrival circularization. If the target is already an existing apsis, it reduces to a single burn. Existing sequence templates (PFMT1) and earlier goal templates (PFMT2) remain supported. The library accepts up to 100 checked template files.
