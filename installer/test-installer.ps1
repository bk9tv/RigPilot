param([Parameter(Mandatory = $true)][string]$SetupPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$SetupPath = (Resolve-Path -LiteralPath $SetupPath).Path
$projectRoot = Split-Path -Parent $PSScriptRoot
$testId = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $projectRoot ('Pruefung\installer-' + $testId)
$installDirectory = Join-Path $testRoot 'App'
$dataDirectory = Join-Path $installDirectory 'Daten'
$groupName = 'RigPilot'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) ($groupName + '\RigPilot.lnk')
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7485FF70-F630-4D5C-AB81-EAE753CB7A82}_is1'
if (Test-Path -LiteralPath $uninstallKey) { throw 'RigPilot is already installed for this user. Use a separate Windows test account.' }
if (Test-Path -LiteralPath (Split-Path -Parent $shortcutPath)) { throw 'RigPilot start menu folder already exists. Use a separate Windows test account.' }
[System.IO.Directory]::CreateDirectory($dataDirectory) | Out-Null
$profilePath = Join-Path $dataDirectory 'profile.json'
$backupPath = $profilePath + '.bak'
'{"Version":1,"Programs":[],"Groups":[],"Displays":[]}' | Set-Content -LiteralPath $profilePath -Encoding UTF8
'{"sentinel":"previous-profile-must-survive"}' | Set-Content -LiteralPath $backupPath -Encoding UTF8
$profileHash = (Get-FileHash -LiteralPath $profilePath).Hash
$backupHash = (Get-FileHash -LiteralPath $backupPath).Hash
$results = [System.Collections.Generic.List[string]]::new()

function Assert-DataPreserved {
    if ((Get-FileHash -LiteralPath $profilePath).Hash -ne $profileHash) { throw 'Profile changed.' }
    if ((Get-FileHash -LiteralPath $backupPath).Hash -ne $backupHash) { throw 'Profile backup changed.' }
}
function Invoke-Setup([string]$LogName) {
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /MERGETASKS="!desktopicon" /DIR="' + $installDirectory + '" /GROUP="' + $groupName + '" /LOG="' + (Join-Path $testRoot $LogName) + '"'
    $process = Start-Process -FilePath $SetupPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    $code = $process.ExitCode
    $process.Dispose()
    return $code
}
function Invoke-TestUninstall {
    # Never uninstall an existing user installation or an unexpected path.
    $registeredPath = (Get-ItemProperty -LiteralPath $uninstallKey).InstallLocation
    if ($registeredPath.TrimEnd('\') -ne $installDirectory) { throw 'Unexpected uninstall location; cleanup stopped.' }
    $uninstaller = Join-Path $installDirectory 'unins000.exe'
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + (Join-Path $testRoot 'uninstall.log') + '"'
    $process = Start-Process -FilePath $uninstaller -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    $code = $process.ExitCode
    $process.Dispose()
    if ($code -ne 0) { throw "Uninstall failed: $code" }
}

try {
    $code = Invoke-Setup 'install.log'
    if ($code -ne 0) { throw "Install failed: $code" }
    $installedExe = Join-Path $installDirectory 'RigPilot.exe'
    $manifest = Get-Content -LiteralPath (Join-Path $installDirectory 'VERSION.json') -Raw | ConvertFrom-Json
    foreach ($file in $manifest.Files) {
        if ((Get-FileHash -LiteralPath (Join-Path $installDirectory $file.Name)).Hash.ToLowerInvariant() -ne $file.SHA256) { throw "Installed file differs: $($file.Name)" }
    }
    if (-not (Test-Path -LiteralPath $shortcutPath)) { throw 'Start menu shortcut missing.' }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    if ($shortcut.TargetPath -ne $installedExe) { throw 'Shortcut target is wrong.' }
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    Assert-DataPreserved
    $results.Add('PASS Installation, payload hashes, start menu shortcut and existing profile preservation')

    $mutex = [System.Threading.Mutex]::new($true, ('Local\RigPilot-' + [Environment]::UserName))
    try {
        $code = Invoke-Setup 'running-app-blocked.log'
        if ($code -eq 0) { throw 'Setup did not block the running application mutex.' }
        Assert-DataPreserved
        $results.Add('PASS Update blocked while application mutex exists')
    } finally { $mutex.ReleaseMutex(); $mutex.Dispose() }

    'Old documentation to be replaced by update' | Set-Content -LiteralPath (Join-Path $installDirectory 'ANLEITUNG.txt') -Encoding UTF8
    $code = Invoke-Setup 'update.log'
    if ($code -ne 0) { throw "Update failed: $code" }
    foreach ($file in $manifest.Files) {
        if ((Get-FileHash -LiteralPath (Join-Path $installDirectory $file.Name)).Hash.ToLowerInvariant() -ne $file.SHA256) { throw "Updated file differs: $($file.Name)" }
    }
    Assert-DataPreserved
    $results.Add('PASS Reinstallation updates application files and preserves profile and backup')

    Invoke-TestUninstall
    if (Test-Path -LiteralPath $installedExe) { throw 'Application executable remained after uninstall.' }
    if (Test-Path -LiteralPath $shortcutPath) { throw 'Start menu shortcut remained after uninstall.' }
    if (Test-Path -LiteralPath $uninstallKey) { throw 'Uninstall registration remained.' }
    Assert-DataPreserved
    $results.Add('PASS Uninstallation removes application and shortcut while preserving profile and backup')
} finally {
    if (Test-Path -LiteralPath $uninstallKey) { Invoke-TestUninstall }
    $results | Set-Content -LiteralPath (Join-Path $testRoot 'installer-tests.txt') -Encoding UTF8
}
$results | Write-Output
Write-Output ("Installer test evidence: " + $testRoot)
