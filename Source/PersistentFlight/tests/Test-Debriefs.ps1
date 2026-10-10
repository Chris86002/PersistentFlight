$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskExe=Join-Path $PSScriptRoot 'FlightDebriefTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ ('/out:'+$taskExe) (Join-Path $taskRoot 'src/FlightDebriefs.cs') (Join-Path $PSScriptRoot 'FlightDebriefTests.cs')
if($LASTEXITCODE -ne 0){throw 'Debrief tests compilation failed'}
& $taskExe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'debrief-results.txt')
if($LASTEXITCODE -ne 0){throw 'Debrief tests failed'}
