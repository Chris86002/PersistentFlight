$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskExe=Join-Path $PSScriptRoot 'RecoveryTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ ('/out:'+$taskExe) (Join-Path $taskRoot 'src/BurnMath.cs') (Join-Path $taskRoot 'src/RecoveryStore.cs') (Join-Path $PSScriptRoot 'RecoveryTests.cs')
if($LASTEXITCODE -ne 0){throw 'Recovery compilation failed'}
& $taskExe (Join-Path $PSScriptRoot 'recovery-storage') | Tee-Object -FilePath (Join-Path $PSScriptRoot 'recovery-results.txt')
if($LASTEXITCODE -ne 0){throw 'Recovery tests failed'}
