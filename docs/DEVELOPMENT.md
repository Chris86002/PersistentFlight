# Architecture and development

`Source/PersistentFlight/src` contains the core scheduling, burn math, mission/event APIs, save scenarios, capacity tracking, fuel forecasting, recovery, rover route state, and debrief logic. `Source/PersistentFlight/regions` contains the experimental regional flight implementation and PF interface, including template persistence and same-body goal orbit calculations. `mission-control` and `Ships/Script` contain the cooperative kOS pilot and supplied programs.

Nearby opted-in craft use private regional physics. Distant supported orbital burns use stored mission information and analytic propagation. Keep these guarantees separate when adding features: an analytic burn is not general live simulation of an arbitrary distant script. Persist mission state explicitly and preserve safe ownership/handoff of steering and throttle.

## Build

Use Windows PowerShell and the .NET Framework C# compiler at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. Install KSP 1.12.5 and kOS 1.6.0.1 locally; their assemblies are referenced from the game folder and are not shipped in this repository. ModuleManager is needed to run the mod.

```powershell
& ./Source/PersistentFlight/build.ps1 -KspRoot 'D:\Games\Kerbal Space Program'
& ./Source/PersistentFlight/build-regions.ps1 -KspRoot 'D:\Games\Kerbal Space Program'
```

The core build must run first. Outputs are `Source/PersistentFlight/PersistentFlight.dll` and `Source/PersistentFlight/PersistentFlight.Regions.Experimental.dll`. The regional build also runs eight frame checks.

The committed `GameData` binaries are the tested 0.25 payload. Building source does not automatically replace those files or refresh the release manifest. To distribute a changed build, stage the new DLLs in `GameData/PersistentFlight/Plugins`, update every payload hash and version consistently, test the installer and game behavior, and package the staged payload. Never leave a manifest pointing to different bytes.

## Tests without the game

```powershell
& ./Source/PersistentFlight/tests/Test-CircularTransfers.ps1
& ./Source/PersistentFlight/tests/Test-Goals.ps1
& ./Source/PersistentFlight/tests/Test-Templates.ps1
```

These compile and run C# math/persistence checks using .NET Framework without KSP assemblies. CI runs these three checks. Other `Test-*.ps1` checks cover core policies and scripts; consult their dependencies before running them.

## Native integration testing

Native harness C# sources are included for reference. Historical Start/Finish runners and packaging scripts depended on the original developer's private fixture and are deliberately excluded. A complete native fixture is not bundled. To adapt a harness, use a separate licensed KSP copy, an expendable save, and the correct game/dependency references. Do not install test harness DLLs into your normal campaign. Record exact payload hashes and restore all replaced files after the trial.

See [validation scope](VALIDATION.md) for the tests performed on the distributed 0.25 payload. Unit CI cannot establish in-game compatibility or replace native flight trials.

## Installer regression

`Source/PersistentFlight/release/Test-CircularInstaller.ps1` runs the current 78-check installer suite in synthetic installation-only directories. Supply `-PackageRoot` (the staged release folder), `-KspFixtureRoot` (a local game/dependency installation used only to copy version/dependency files), and `-PreviousPackageRoot` (an extracted 0.24 release). It does not launch the game or install into the supplied fixture root. Older release assets are not reconstructed in this repository.
