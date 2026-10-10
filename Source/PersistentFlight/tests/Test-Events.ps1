$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskExe=Join-Path $PSScriptRoot 'FleetEventTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ ('/out:'+$taskExe) (Join-Path $taskRoot 'src/FleetEvents.cs') (Join-Path $PSScriptRoot 'FleetEventTests.cs')
if($LASTEXITCODE -ne 0){throw 'Event tests failed to compile'}
& $taskExe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'event-results.txt')
if($LASTEXITCODE -ne 0){throw 'Event tests failed'}
