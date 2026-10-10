# Changelog

## 0.25 Experimental

- Added Circular orbit at altitude to reusable orbital goal templates.
- Calculates an apsis transfer and arrival circularization, compares supported transfers for intermediate targets, and reduces existing target apsides to one burn.
- Previews all burns, transfer/final orbit, combined delta-v, and fuel advisory before creating native map nodes.
- Retains explicit map review and queueing, existing maneuver sequences and original goals, mission recovery, and flight reports.
- Rejects unsafe/unqueueable plans and known body changes before completion; corrected encounter checks to use the orbit patch transition state.

The GitHub edition adds installation/development/troubleshooting documentation, a GPL-3.0 license, issue templates, and selected unit CI. Its 14 runtime payload files are unchanged from the tested 0.25 build.

## Features retained from earlier experimental builds

Regional autonomy, cooperative maneuver queues, event missions, rover routes, predictive fuel checks, capacity reservations, SOI policies, landing reports, fleet spectator/debrief views, recovery/readiness tools, reusable maneuver templates, and the original same-body goal templates.

The repository begins with the tested 0.25 source snapshot. It does not reconstruct historical commits or claim unreleased roadmap features are implemented.
