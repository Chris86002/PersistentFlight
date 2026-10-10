param([string]$KspRoot='C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program')
$ErrorActionPreference='Stop'
$taskLibrary=Join-Path $KspRoot 'GameData/kOS/Plugins/kOS.Safe.dll'
$taskExe=Join-Path $PSScriptRoot 'ScriptChecks.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ ('/out:'+$taskExe) ('/reference:'+$taskLibrary) (Join-Path $PSScriptRoot 'ScriptChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Script checker build failed'}
$taskRoot=Split-Path $PSScriptRoot -Parent
& $taskExe $taskLibrary (Join-Path $taskRoot 'mission-control/pf_event_handler_example.ks') (Join-Path $taskRoot 'mission-control/pf_mission_pilot.ks') | Tee-Object -FilePath (Join-Path $PSScriptRoot 'script-results.txt')
if($LASTEXITCODE -ne 0){throw 'Script validation failed'}
