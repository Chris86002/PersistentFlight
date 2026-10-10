param([string]$KspRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program')
$ErrorActionPreference='Stop'
$taskCompiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskManaged=Join-Path $KspRoot 'KSP_x64_Data\Managed'
$taskOutput=Join-Path $PSScriptRoot 'PersistentFlight.Regions.Experimental.dll'
$taskArgs=@('/nologo','/target:library','/optimize+',"/out:$taskOutput",('/reference:'+(Join-Path $PSScriptRoot 'PersistentFlight.dll')))
foreach($taskName in @('Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.VehiclesModule.dll','UnityEngine.UI.dll','UnityEngine.IMGUIModule.dll','UnityEngine.ParticleSystemModule.dll','UnityEngine.AnimationModule.dll')) { $taskArgs+='/reference:'+(Join-Path $taskManaged $taskName) }
$taskArgs+=(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'regions') -Filter '*.cs').FullName
foreach($taskName in @('kOS.dll','kOS.Safe.dll')) { $taskArgs+='/reference:'+(Join-Path $KspRoot ('GameData\kOS\Plugins\'+$taskName)) }
& $taskCompiler $taskArgs
if($LASTEXITCODE -ne 0) { throw 'Region build failed' }
$taskTestOutput=Join-Path $PSScriptRoot 'tests\RegionFrameTests.exe'
& $taskCompiler /nologo /target:exe /optimize+ "/out:$taskTestOutput" (Join-Path $PSScriptRoot 'src\BurnMath.cs') (Join-Path $PSScriptRoot 'regions\RegionFrame.cs') (Join-Path $PSScriptRoot 'tests\RegionFrameTests.cs')
if($LASTEXITCODE -ne 0) { throw 'Frame test compilation failed' }
& $taskTestOutput
if($LASTEXITCODE -ne 0) { throw 'Frame tests failed' }
Write-Output "Built $taskOutput"



