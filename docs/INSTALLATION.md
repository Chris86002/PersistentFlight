# Installation

The tested setup is Windows x64, KSP 1.12.5, kOS 1.6.0.1, and ModuleManager 4.2.3. Install dependencies first, close KSP, and back up the save you intend to use. Extract the complete release ZIP and run `Install.cmd`. Select the game root containing `KSP_x64.exe`; the installer also attempts Steam discovery.

The installer checks the dependency files and the SHA-256 manifest before copying its 14 allowed payload files. It records backups of overwritten files under `.PersistentFlight-0.25-installer` in the selected game directory. Keep that directory if you want to reverse an upgrade. It does not replace saves or unrelated mods.

The final layout includes:

```text
Kerbal Space Program/
  KSP_x64.exe
  GameData/
    kOS/                         # separately installed dependency
    ModuleManager.4.2.3.dll      # separately installed dependency
    PersistentFlight/
      KeepAlive.cfg
      RegionalAutonomy.cfg
      Plugins/
        PersistentFlight.dll
        PersistentFlight.Regions.Experimental.dll
  Ships/Script/
    pf_mission_pilot.ks
    boot/pf_mission_resume.ks
    ...other supplied scripts
```

Do not nest the mod inside another `GameData/GameData` folder. If installing manually, copy the release's `GameData/PersistentFlight` folder into your game's `GameData`, then merge its `Ships/Script` into the game's `Ships/Script`, preserving your own scripts. Manual copying does not create installer rollback records.

## Upgrade and removal

Close KSP before upgrading or removing the mod. Version 0.25 can overwrite 0.24 payload files and retain backups. Run the **0.25 package's `Uninstall.cmd`** against the same game root to restore the previous files where they existed and remove only newly installed payload files. If a payload file has been edited since installation, uninstall refuses to overwrite that edit. Preserve your changes and consult the installer state before proceeding.

Never remove the installer backups to resolve an error blindly. A `RecoveryRequired` state means rollback did not complete. Keep `state.json` and the backups for diagnosis.

## First use

Open the cyan PF toolbar button in flight/map view. Each automated craft needs a kOS CPU; enable **Automatic regional physics** on each CPU you want to opt in. Start the mission pilot only on an idle CPU, with suitable engines and a reviewed orbital plan. The [README](../README.md#first-orbital-mission) gives a complete first mission.

Back up both the save and `saves/<campaign>/PersistentFlight/MissionTemplates`. Try complex missions in a separate campaign before using a valuable save.
