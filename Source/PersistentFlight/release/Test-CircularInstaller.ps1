param([Parameter(Mandatory=$true)][string]$PackageRoot,[Parameter(Mandatory=$true)][string]$KspFixtureRoot,[Parameter(Mandatory=$true)][string]$PreviousPackageRoot)
$ErrorActionPreference='Stop'
$PackageRoot=[IO.Path]::GetFullPath($PackageRoot)
$testRoot=Join-Path $PSScriptRoot ('circular-installer-tests/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$nativeRoot=[IO.Path]::GetFullPath($KspFixtureRoot)
$manifest=Get-Content -LiteralPath (Join-Path $PackageRoot 'payload-manifest.json') -Raw | ConvertFrom-Json
$checks=New-Object 'System.Collections.Generic.List[string]'
function Assert($condition,[string]$label){if(!$condition){throw "FAIL $label"}; $checks.Add("PASS $label")}
function Fingerprint([string]$file){return (Get-FileHash -LiteralPath $file).Hash}
function Fixture([string]$name){
    $root=Join-Path $testRoot $name
    New-Item -ItemType Directory -Path (Join-Path $root 'GameData/kOS/Plugins'),(Join-Path $root 'saves'),(Join-Path $root 'Ships/Script') -Force | Out-Null
    # Installation-only fixtures, not game execution fixtures.
    [IO.File]::WriteAllText((Join-Path $root 'KSP_x64.exe'),'installation test placeholder')
    Copy-Item -LiteralPath (Join-Path $nativeRoot 'buildID64.txt') -Destination $root
    Copy-Item -LiteralPath (Join-Path $nativeRoot 'GameData/kOS/Plugins/kOS.dll') -Destination (Join-Path $root 'GameData/kOS/Plugins')
    Copy-Item -LiteralPath (Join-Path $nativeRoot 'GameData/ModuleManager.4.2.3.dll') -Destination (Join-Path $root 'GameData')
    [IO.File]::WriteAllText((Join-Path $root 'saves/preserve.sfs'),'existing save sentinel')
    [IO.File]::WriteAllText((Join-Path $root 'Ships/Script/my-program.ks'),'existing user script')
    return $root
}
function Run([string]$root,[string]$action,[int]$expected=0,[string]$package=$PackageRoot){
    $output=@(& "$env:SystemRoot/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -ExecutionPolicy Bypass -File (Join-Path $package 'Setup.ps1') -KspRoot $root -Action $action -Quiet 2>&1)
    if($LASTEXITCODE -ne $expected){throw "Unexpected installer exit $LASTEXITCODE (expected $expected): $output"}
    return $output -join "`n"
}
$clean=Fixture 'Clean game with spaces'
$saveHash=Fingerprint (Join-Path $clean 'saves/preserve.sfs')
$kosHash=Fingerprint (Join-Path $clean 'GameData/kOS/Plugins/kOS.dll')
$null=Run $clean Install
foreach($entry in $manifest.Files){Assert ((Fingerprint (Join-Path $clean $entry.File)) -eq $entry.SHA256) ('clean install '+$entry.File)}
$firstState=Get-Content -LiteralPath (Join-Path $clean '.PersistentFlight-0.25-installer/state.json') -Raw
Assert ((Run $clean Install) -like '*already installed*') 'repeat installation is a no-op'
Assert ((Get-Content -LiteralPath (Join-Path $clean '.PersistentFlight-0.25-installer/state.json') -Raw) -eq $firstState) 'repeat installation retains first backups'
$edited=Join-Path $clean 'Ships/Script/pf_rover_resumable.ks'
Add-Content -LiteralPath $edited -Value '// player edit'
$editedHash=Fingerprint $edited
Assert ((Run $clean Uninstall 1) -like '*changed or removed*') 'uninstall refuses player edits'
Assert ((Fingerprint $edited) -eq $editedHash) 'player edit preserved'
Copy-Item -LiteralPath (Join-Path $PackageRoot 'Ships/Script/pf_rover_resumable.ks') -Destination $edited -Force
$null=Run $clean Uninstall
Assert (@($manifest.Files | Where-Object {Test-Path -LiteralPath (Join-Path $clean $_.File)}).Count -eq 0) 'fresh uninstall removes all payload files'
Assert ((Fingerprint (Join-Path $clean 'saves/preserve.sfs')) -eq $saveHash) 'save preserved through install and uninstall'
Assert ((Fingerprint (Join-Path $clean 'GameData/kOS/Plugins/kOS.dll')) -eq $kosHash) 'dependency preserved'
Assert ((Get-Content -LiteralPath (Join-Path $clean 'Ships/Script/my-program.ks') -Raw) -eq 'existing user script') 'unrelated script preserved'
$null=Run $clean Install
Assert ((Get-Content -LiteralPath (Join-Path $clean '.PersistentFlight-0.25-installer/state.json') -Raw | ConvertFrom-Json).Status -eq 'Installed') 'reinstall after uninstall'
$upgrade=Fixture 'Existing mod'
$originals=@{}
foreach($entry in $manifest.Files){$dest=Join-Path $upgrade $entry.File; New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null; [IO.File]::WriteAllText($dest,'prior player file '+$entry.File); $originals[$entry.File]=Fingerprint $dest}
$null=Run $upgrade Install
$null=Run $upgrade Uninstall
foreach($entry in $manifest.Files){Assert ((Fingerprint (Join-Path $upgrade $entry.File)) -eq $originals[$entry.File]) ('upgrade uninstall restores '+$entry.File)}
$brokenBackup=Fixture 'Damaged backup'
$oldFile=Join-Path $brokenBackup 'Ships/Script/pf_rover_resumable.ks'
[IO.File]::WriteAllText($oldFile,'previous script')
$null=Run $brokenBackup Install
$state=Get-Content -LiteralPath (Join-Path $brokenBackup '.PersistentFlight-0.25-installer/state.json') -Raw | ConvertFrom-Json
Add-Content -LiteralPath (Join-Path (Join-Path $brokenBackup $state.Backup) 'Ships/Script/pf_rover_resumable.ks') -Value 'damage'
Assert ((Run $brokenBackup Uninstall 1) -like '*backup is damaged*') 'damaged backup rejected before removal'
foreach($entry in $manifest.Files){Assert ((Fingerprint (Join-Path $brokenBackup $entry.File)) -eq $entry.SHA256) ('damaged backup preserves '+$entry.File)}
$missing=Fixture 'Missing dependency'
Remove-Item -LiteralPath (Join-Path $missing 'GameData/kOS/Plugins/kOS.dll')
Assert ((Run $missing Install 1) -like '*Install kOS*') 'missing kOS rejected'
Assert (!(Test-Path -LiteralPath (Join-Path $missing '.PersistentFlight-0.25-installer'))) 'dependency refusal changes no game files'
$noMM=Fixture 'Missing ModuleManager'
Remove-Item -LiteralPath (Join-Path $noMM 'GameData/ModuleManager.4.2.3.dll')
Assert ((Run $noMM Install 1) -like '*Install ModuleManager*') 'missing ModuleManager rejected'
$wrongVersion=Fixture 'Wrong game version'
[IO.File]::WriteAllText((Join-Path $wrongVersion 'buildID64.txt'),'build id = 03173')
Assert ((Run $wrongVersion Install 1) -like '*KSP 1.12.5*') 'unsupported game version rejected'
$badPackage=Join-Path $testRoot 'Damaged package'
Copy-Item -LiteralPath $PackageRoot -Destination $badPackage -Recurse
Add-Content -LiteralPath (Join-Path $badPackage 'Ships/Script/pf_rover_resumable.ks') -Value '// damaged'
$badDest=Fixture 'Damaged package target'
Assert ((Run $badDest Install 1 $badPackage) -like '*missing or damaged*') 'package corruption rejected'
Assert (!(Test-Path -LiteralPath (Join-Path $badDest '.PersistentFlight-0.25-installer'))) 'corrupt package changes no game files'
$drag=Fixture 'Drag-in game'
Copy-Item -LiteralPath (Join-Path $PackageRoot 'GameData'),(Join-Path $PackageRoot 'Ships') -Destination $drag -Recurse -Force
foreach($entry in $manifest.Files){Assert ((Fingerprint (Join-Path $drag $entry.File)) -eq $entry.SHA256) ('drag-in layout '+$entry.File)}
Assert ((Run $drag Uninstall 1) -like '*No one-click installation record*') 'manual install removal guidance'
$upgrade=Fixture 'Existing 0.24 upgrade'
$oldRecord=Join-Path $upgrade '.PersistentFlight-0.24-installer/state.json'
New-Item -ItemType Directory -Path (Split-Path $oldRecord) -Force | Out-Null
[IO.File]::WriteAllText($oldRecord,'{"Version":"0.24","Status":"Installed","Files":[]}')
$previousRecordHash=Fingerprint $oldRecord
$upgradeDll=Join-Path $upgrade 'GameData/PersistentFlight/Plugins/PersistentFlight.Regions.Experimental.dll'
New-Item -ItemType Directory -Path (Split-Path $upgradeDll) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PreviousPackageRoot 'GameData/PersistentFlight/Plugins/PersistentFlight.Regions.Experimental.dll') -Destination $upgradeDll
$previousDllHash=Fingerprint $upgradeDll
Assert ((Run $upgrade Install 0) -like '*PersistentFlight 0.25 is installed*') 'hotfix upgrades existing 0.24 without uninstall'
Assert ((Fingerprint $oldRecord) -eq $previousRecordHash) 'older installer record preserved'
Assert ((Fingerprint $upgradeDll) -ne $previousDllHash) 'regional update installed over previous region controls'
Run $upgrade Uninstall 0 | Out-Null
Assert ((Fingerprint $upgradeDll) -eq $previousDllHash) 'update removal restores previous region controls'
Assert ((Fingerprint $oldRecord) -eq $previousRecordHash) 'older installer record retained after hotfix removal'
$checks | Set-Content -LiteralPath (Join-Path $testRoot 'RESULT.txt')
$checks | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'circular-installer-result.txt')
Write-Output ("PASS {0} installer checks; evidence: {1}" -f $checks.Count,$testRoot)







