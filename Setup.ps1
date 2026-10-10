param([ValidateSet('Install','Uninstall','Check')][string]$Action='Install', [string]$KspRoot, [switch]$Quiet)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2
$packageRoot=$PSScriptRoot
$allowed=@('GameData/PersistentFlight/KeepAlive.cfg','GameData/PersistentFlight/RegionalAutonomy.cfg','GameData/PersistentFlight/Plugins/PersistentFlight.dll','GameData/PersistentFlight/Plugins/PersistentFlight.Regions.Experimental.dll','Ships/Script/boot/pf_resume.ks','Ships/Script/pf_return_resumable.ks','Ships/Script/pf_ascent_resumable.ks','Ships/Script/pf_rover_resumable.ks','Ships/Script/pf_mission_pilot.ks','Ships/Script/boot/pf_mission_resume.ks','Ships/Script/pf_event_handler_example.ks','Ships/Script/pf_rover_route.ks','Ships/Script/boot/pf_rover_route_resume.ks','Ships/Script/pf_fuel_check.ks')
function Notify([string]$message, [bool]$failed=$false) {
    if($Quiet){Write-Output $message; return}
    Add-Type -AssemblyName System.Windows.Forms
    $icon=if($failed){[Windows.Forms.MessageBoxIcon]::Error}else{[Windows.Forms.MessageBoxIcon]::Information}
    [Windows.Forms.MessageBox]::Show($message,'PersistentFlight',[Windows.Forms.MessageBoxButtons]::OK,$icon) | Out-Null
}
function SafePath([string]$root,[string]$relative) {
    $root=[IO.Path]::GetFullPath($root).TrimEnd('\')
    $path=[IO.Path]::GetFullPath((Join-Path $root $relative))
    if(!$path.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'A file path escapes its destination.'}
    $cursor=$path
    while($cursor){
        if(Test-Path -LiteralPath $cursor){
            if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Linked folders are unsupported: $cursor"}
        }
        $cursor=Split-Path -Parent $cursor
    }
    return $path
}
function Hash([string]$path){return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function FindGame {
    $candidates=New-Object 'System.Collections.Generic.List[string]'
    $candidates.Add((Join-Path ${env:ProgramFiles(x86)} 'Steam/steamapps/common/Kerbal Space Program'))
    $steam=Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue
    if($steam -and $steam.PSObject.Properties['SteamPath']){
        $candidates.Add((Join-Path $steam.SteamPath 'steamapps/common/Kerbal Space Program'))
        $libraries=Join-Path $steam.SteamPath 'steamapps/libraryfolders.vdf'
        if(Test-Path -LiteralPath $libraries){
            foreach($match in [regex]::Matches((Get-Content -LiteralPath $libraries -Raw),'"path"\s+"([^"]+)"')){
                $candidates.Add((Join-Path $match.Groups[1].Value.Replace('\\','\') 'steamapps/common/Kerbal Space Program'))
            }
        }
    }
    $found=@($candidates | Select-Object -Unique | Where-Object {Test-Path -LiteralPath (Join-Path $_ 'KSP_x64.exe')})
    if($found.Count -eq 1){return $found[0]}
    if($Quiet){throw 'Specify -KspRoot with the KSP installation folder.'}
    Add-Type -AssemblyName System.Windows.Forms
    $picker=New-Object Windows.Forms.FolderBrowserDialog
    $picker.Description='Choose your Kerbal Space Program folder (the folder containing KSP_x64.exe).'
    if($found.Count -gt 0){$picker.SelectedPath=$found[0]}
    try{if($picker.ShowDialog() -ne [Windows.Forms.DialogResult]::OK){return $null}; return $picker.SelectedPath}finally{$picker.Dispose()}
}
function RestoreFiles($records,[string]$backupRoot,[string]$targetRoot){
    foreach($record in $records){
        $dest=SafePath $targetRoot $record.File
        if($record.Existed){
            $source=SafePath $backupRoot $record.File
            if(!(Test-Path -LiteralPath $source) -or (Hash $source) -ne $record.BeforeSHA256){throw "Backup verification failed: $($record.File)"}
            New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $dest -Force
            if((Hash $dest) -ne $record.BeforeSHA256){throw "Restore verification failed: $($record.File)"}
        }elseif(Test-Path -LiteralPath $dest){Remove-Item -LiteralPath $dest -Force}
    }
}
try {
    # Validate the nine expected files before touching a game installation.
    $manifest=Get-Content -LiteralPath (Join-Path $packageRoot 'payload-manifest.json') -Raw | ConvertFrom-Json
    $entries=@($manifest.Files)
    if($entries.Count -ne $allowed.Count -or @($entries.File | Select-Object -Unique).Count -ne $allowed.Count){throw 'Invalid payload manifest.'}
    foreach($entry in $entries){
        if($entry.File -cnotin $allowed){throw 'Unexpected payload file.'}
        $source=SafePath $packageRoot $entry.File
        if(!(Test-Path -LiteralPath $source) -or (Hash $source) -ne $entry.SHA256){throw "Package file is missing or damaged: $($entry.File). Extract the ZIP again."}
    }
    if(!$KspRoot){$KspRoot=FindGame}
    if(!$KspRoot){exit 0}
    $KspRoot=[IO.Path]::GetFullPath($KspRoot).TrimEnd('\')
    $null=SafePath $KspRoot 'KSP_x64.exe'
    if(!(Test-Path -LiteralPath (Join-Path $KspRoot 'KSP_x64.exe')) -or !(Test-Path -LiteralPath (Join-Path $KspRoot 'GameData') -PathType Container)){throw 'Choose the KSP game folder, not GameData or the ZIP folder.'}
    # Block all KSP processes, including another copy whose path is hidden by Windows.
    if(@(Get-Process -Name KSP,KSP_x64 -ErrorAction SilentlyContinue).Count){throw 'Close Kerbal Space Program, then run this installer again.'}
    $metadata=SafePath $KspRoot '.PersistentFlight-0.25-installer'
    $statePath=SafePath $KspRoot '.PersistentFlight-0.25-installer/state.json'
    foreach($relative in $allowed){$null=SafePath $KspRoot $relative}
    if($Action -in @('Install','Check')){
        $build=Join-Path $KspRoot 'buildID64.txt'
        if(!(Test-Path -LiteralPath $build) -or (Get-Content -LiteralPath $build -Raw) -notmatch 'build id\s*=\s*0?3190\b'){throw 'This release was tested on KSP 1.12.5 (Windows, 64-bit). Use that version for this installer.'}
        $kos=Join-Path $KspRoot 'GameData/kOS/Plugins/kOS.dll'
        if(!(Test-Path -LiteralPath $kos)){throw 'Install kOS 1.6.0.1 first, then run this installer again. See START HERE.txt for the download link.'}
        if([Reflection.AssemblyName]::GetAssemblyName($kos).Version.ToString() -ne '1.6.0.1'){throw 'This installer requires the tested kOS version 1.6.0.1. See START HERE.txt.'}
        if(!@(Get-ChildItem -LiteralPath (Join-Path $KspRoot 'GameData') -Filter 'ModuleManager*.dll' -File).Count){throw 'Install ModuleManager first, then run this installer again. See START HERE.txt.'}
    }
    if($Action -eq 'Check'){Notify ('Ready to install PersistentFlight 0.25 in '+$KspRoot+'. Game, dependencies, and package checks passed.'); exit 0}
    # Elevate only when the selected installation is not writable.
    $probe=Join-Path $KspRoot ('.pf-write-'+[Guid]::NewGuid().ToString('N'))
    try{[IO.File]::WriteAllText($probe,'write check'); Remove-Item -LiteralPath $probe -Force}
    catch [UnauthorizedAccessException] {
        if($Quiet){throw 'The game folder requires administrator access.'}
        $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
        $principal=New-Object Security.Principal.WindowsPrincipal($identity)
        if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw}
        $arguments='-NoProfile -ExecutionPolicy Bypass -STA -File "'+$PSCommandPath+'" -Action '+$Action+' -KspRoot "'+$KspRoot+'"'
        $child=Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -Verb RunAs -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
        exit $child.ExitCode
    }
    if($Action -eq 'Uninstall'){
        if(!(Test-Path -LiteralPath $statePath)){throw 'No one-click installation record exists here. For a drag-in installation, see the removal instructions in START HERE.txt.'}
        $state=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if($state.Status -ne 'Installed'){throw 'This installation is already removed or needs recovery. See the installer records.'}
        $records=@($state.Files)
        if($records.Count -ne $allowed.Count -or @($records.File | Select-Object -Unique).Count -ne $allowed.Count){throw 'Invalid installation record.'}
        if($state.Backup -notmatch '^\.PersistentFlight-0.25-installer/backups/[a-f0-9]{32}$'){throw 'Invalid backup location.'}
        $backupRoot=SafePath $KspRoot $state.Backup
        foreach($record in $records){
            if($record.File -cnotin $allowed){throw 'Invalid installation record.'}
            $dest=SafePath $KspRoot $record.File
            if(!(Test-Path -LiteralPath $dest) -or (Hash $dest) -ne $record.InstalledSHA256){throw "You have changed or removed $($record.File) since installation. Your edits were preserved. Restore the installed file before uninstalling, or remove it manually."}
            if($record.Existed){$source=SafePath $backupRoot $record.File; if(!(Test-Path -LiteralPath $source) -or (Hash $source) -ne $record.BeforeSHA256){throw 'A backup is damaged. No files were removed.'}}
        }
        # Keep a verified undo copy until the whole removal succeeds.
        $undoRoot=SafePath $KspRoot ('.PersistentFlight-0.25-installer/uninstall-undo/'+[Guid]::NewGuid().ToString('N'))
        $undoRecords=@(foreach($record in $records){
            $undo=SafePath $undoRoot $record.File
            New-Item -ItemType Directory -Path (Split-Path -Parent $undo) -Force | Out-Null
            Copy-Item -LiteralPath (SafePath $KspRoot $record.File) -Destination $undo
            if((Hash $undo) -ne $record.InstalledSHA256){throw 'Removal backup verification failed.'}
            [pscustomobject]@{File=$record.File;Existed=$true;BeforeSHA256=$record.InstalledSHA256}
        })
        try{
            RestoreFiles $records $backupRoot $KspRoot
            $state.Status='Removed'; $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
        }catch{
            $failure=$_; RestoreFiles $undoRecords $undoRoot $KspRoot
            $state.Status='Installed'; $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
            throw $failure
        }
        Notify ('Removed this installation and restored any files it replaced.'+[Environment]::NewLine+'Your saves and other mods are unchanged.'+[Environment]::NewLine+'Backups remain in '+$metadata)
        exit 0
    }
    if(Test-Path -LiteralPath $statePath){
        $old=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if($old.Status -eq 'Installed'){
            foreach($entry in $entries){$dest=SafePath $KspRoot $entry.File; if(!(Test-Path -LiteralPath $dest) -or (Hash $dest) -ne $entry.SHA256){throw 'An installation already exists with different files. Uninstall it first, preserving any edits.'}}
            Notify ('PersistentFlight 0.25 is already installed in '+$KspRoot+'.'); exit 0
        }
        if($old.Status -ne 'Removed'){throw 'A previous installation needs recovery. Keep the backups and read its state.json before trying again.'}
    }
    $backupRelative='.PersistentFlight-0.25-installer/backups/'+[Guid]::NewGuid().ToString('N')
    $backupRoot=SafePath $KspRoot $backupRelative
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $records=@(foreach($entry in $entries){
        $dest=SafePath $KspRoot $entry.File
        $existed=Test-Path -LiteralPath $dest
        $before=$null
        if($existed){
            $before=Hash $dest; $backup=SafePath $backupRoot $entry.File
            New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
            Copy-Item -LiteralPath $dest -Destination $backup
            if((Hash $backup) -ne $before){throw 'Backup verification failed. Nothing has been installed.'}
        }
        [pscustomobject]@{File=$entry.File;Existed=$existed;BeforeSHA256=$before;InstalledSHA256=$entry.SHA256}
    })
    $state=[pscustomobject]@{Version='0.25';Status='Prepared';Backup=$backupRelative;Files=$records;InstalledUTC=[DateTime]::UtcNow.ToString('o')}
    $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
    try{
        foreach($entry in $entries){
            $dest=SafePath $KspRoot $entry.File
            New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
            Copy-Item -LiteralPath (SafePath $packageRoot $entry.File) -Destination $dest -Force
            if((Hash $dest) -ne $entry.SHA256){throw "Installed file verification failed: $($entry.File)"}
        }
        $state.Status='Installed'; $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
    }catch{
        $failure=$_
        try{RestoreFiles $records $backupRoot $KspRoot; $state.Status='Removed'}catch{$state.Status='RecoveryRequired'}
        $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
        throw $failure
    }
    Notify ('PersistentFlight 0.25 is installed.'+[Environment]::NewLine+$KspRoot+[Environment]::NewLine+[Environment]::NewLine+'Start KSP. Add a kOS CPU to each craft, then follow QUICK START.txt.'+[Environment]::NewLine+'Your saves and other mods are unchanged.')
    exit 0
}catch{Notify $_.Exception.Message $true; exit 1}
