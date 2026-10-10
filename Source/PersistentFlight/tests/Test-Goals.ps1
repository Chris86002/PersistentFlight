$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskExe=Join-Path $PSScriptRoot 'GoalTemplateTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ ('/out:'+$taskExe) (Join-Path $taskRoot 'regions/MissionTemplates.cs') (Join-Path $taskRoot 'regions/GoalOrbitMath.cs') (Join-Path $PSScriptRoot 'GoalTemplateTests.cs')
if($LASTEXITCODE -ne 0){throw 'Goal test build failed'}
& $taskExe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'goal-results.txt')
if($LASTEXITCODE -ne 0){throw 'Goal tests failed'}
