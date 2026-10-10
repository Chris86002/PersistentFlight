$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskExe=Join-Path $PSScriptRoot 'SoiPlanTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ ('/out:'+$taskExe) (Join-Path $taskRoot 'src/BurnMath.cs') (Join-Path $taskRoot 'src/SoiPlanPolicy.cs') (Join-Path $PSScriptRoot 'SoiPlanTests.cs')
if($LASTEXITCODE -ne 0){throw 'SOI plan tests compilation failed'}
& $taskExe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'soi-results.txt')
if($LASTEXITCODE -ne 0){throw 'SOI plan tests failed'}
