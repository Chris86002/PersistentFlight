$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskExe=Join-Path $PSScriptRoot 'CircularTransferTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ ('/out:'+$taskExe) (Join-Path $taskRoot 'regions/MissionTemplates.cs') (Join-Path $taskRoot 'regions/GoalOrbitMath.cs') (Join-Path $PSScriptRoot 'CircularTransferTests.cs')
if($LASTEXITCODE -ne 0){throw 'Circular transfer test build failed'}
& $taskExe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'circular-transfer-results.txt')
if($LASTEXITCODE -ne 0){throw 'Circular transfer tests failed'}
