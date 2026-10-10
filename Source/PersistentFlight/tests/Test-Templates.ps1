$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskExe=Join-Path $PSScriptRoot 'MissionTemplateTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ ('/out:'+$taskExe) (Join-Path $taskRoot 'regions/MissionTemplates.cs') (Join-Path $PSScriptRoot 'MissionTemplateTests.cs')
if($LASTEXITCODE -ne 0){throw 'Template test build failed'}
& $taskExe | Tee-Object -FilePath (Join-Path $PSScriptRoot 'template-results.txt')
if($LASTEXITCODE -ne 0){throw 'Template tests failed'}
