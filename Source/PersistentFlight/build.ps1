param([string]$KspRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program')
$ErrorActionPreference = 'Stop'
$taskManaged = Join-Path $KspRoot 'KSP_x64_Data\Managed'
$taskKos = Join-Path $KspRoot 'GameData\kOS\Plugins'
$taskCompiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskOutput = Join-Path $PSScriptRoot 'PersistentFlight.dll'
$taskArgs = @('/nologo','/target:library','/optimize+',"/out:$taskOutput")
foreach ($taskName in @('Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.UI.dll')) {
    $taskArgs += '/reference:' + (Join-Path $taskManaged $taskName)
}
foreach ($taskName in @('kOS.dll','kOS.Safe.dll')) { $taskArgs += '/reference:' + (Join-Path $taskKos $taskName) }
$taskArgs += (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs').FullName
& $taskCompiler $taskArgs
if ($LASTEXITCODE -ne 0) { throw 'PersistentFlight compilation failed' }
Write-Output "Built $taskOutput"
