$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskExe=Join-Path $PSScriptRoot 'RoverRouteTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ ('/out:'+$taskExe) (Join-Path $taskRoot 'src/FleetEvents.cs') (Join-Path $taskRoot 'src/RoverRoute.cs') (Join-Path $PSScriptRoot 'RoverRouteTests.cs')
if($LASTEXITCODE -ne 0){throw 'Rover tests compilation failed'}
& $taskExe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'rover-results.txt')
if($LASTEXITCODE -ne 0){throw 'Rover tests failed'}
