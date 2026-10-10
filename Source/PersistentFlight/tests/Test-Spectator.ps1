$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskExe=Join-Path $PSScriptRoot 'SpectatorTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ ('/out:'+$taskExe) (Join-Path $taskRoot 'src/BurnMath.cs') (Join-Path $taskRoot 'src/SpectatorPolicy.cs') (Join-Path $PSScriptRoot 'SpectatorTests.cs')
if($LASTEXITCODE -ne 0){throw 'Spectator tests compilation failed'}
& $taskExe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'spectator-results.txt')
if($LASTEXITCODE -ne 0){throw 'Spectator tests failed'}
