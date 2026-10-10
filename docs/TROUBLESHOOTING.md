# Troubleshooting

| Symptom | Check |
| --- | --- |
| Installer cannot locate KSP | Select the directory containing `KSP_x64.exe`, not `GameData`. Close the game first. |
| Dependency or hash check fails | Install the tested kOS/ModuleManager versions and extract the complete release ZIP again. Do not mix files from different releases. |
| PF toolbar button is absent | Enter flight/map view; verify both mod DLLs and configuration files are in `GameData/PersistentFlight`. Check `KSP.log` for loader errors. |
| Nearby craft stops running | Check its kOS CPU opt-in, regional distance, capacity, and PF readiness/status. The live region is limited to 60 km. |
| Mission pilot cannot take control | Stop other guidance, release throttle/steering locks, activate suitable engines, and verify any supplied engine tag matches the craft. |
| Template or queue rejected | Check normal time, current body, orbit safety, future preparation time, node count/order, supported burn size, and readiness messages. |
| Preview and final orbit differ | Preview treats burns as instantaneous. Finite burn duration, engine performance, and interruptions affect the flown result. Inspect the debrief. |
| Reload does not resume a custom program | Automatic recovery supports the known mission pilot. Arbitrary kOS runtime state is not saved; custom guidance needs its own checkpoints. |
| Uninstall refuses edited files | Preserve your edits. The refusal prevents automatic rollback from overwriting player changes. Keep installer state and backups. |

For a bug report, include KSP/kOS/ModuleManager versions, the PersistentFlight release, other relevant mods, reproduction steps, expected/actual behavior, and the relevant part of `KSP.log` or the flight debrief. Remove personal paths and private save details before posting. Use [GitHub Issues](https://github.com/Chris86002/PersistentFlight/issues).
