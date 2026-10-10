$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskExe=Join-Path $PSScriptRoot 'FuelForecastTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ ('/out:'+$taskExe) (Join-Path $taskRoot 'src/BurnMath.cs') (Join-Path $taskRoot 'src/FuelForecast.cs') (Join-Path $PSScriptRoot 'FuelForecastTests.cs')
if($LASTEXITCODE -ne 0){throw 'Fuel forecast tests compilation failed'}
& $taskExe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'fuel-results.txt')
if($LASTEXITCODE -ne 0){throw 'Fuel forecast tests failed'}
