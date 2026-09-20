param(
    [switch]$Installer,
    [string]$IsccPath = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Build each package in a new directory. Existing packages remain available.
$projectRoot = $PSScriptRoot
if ($Installer) {
    if ([string]::IsNullOrWhiteSpace($IsccPath)) {
        $localCompiler = Join-Path $projectRoot 'Pruefung\tools\InnoSetup-7.1.0\ISCC.exe'
        if (Test-Path -LiteralPath $localCompiler) { $IsccPath = $localCompiler }
        else {
            $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($null -ne $compilerCommand) { $IsccPath = $compilerCommand.Source }
        }
    }
    if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
        throw 'Inno Setup compiler not found. Pass -IsccPath to ISCC.exe. See RELEASING.md.'
    }
    $IsccPath = (Resolve-Path -LiteralPath $IsccPath).Path
}
$buildId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$workDirectory = Join-Path $projectRoot ('Pruefung\release-' + $buildId)
$payloadDirectory = Join-Path $workDirectory 'Paket'
$testDirectory = Join-Path $workDirectory 'Tests'
$exePath = Join-Path $payloadDirectory 'RigPilot.exe'
[System.IO.Directory]::CreateDirectory($payloadDirectory) | Out-Null

& (Join-Path $projectRoot 'build.ps1') -OutputPath $exePath
$version = [System.Reflection.AssemblyName]::GetAssemblyName($exePath).Version.ToString()
$distributionDirectory = Join-Path $projectRoot ('Distribution\RigPilot-' + $version + '-' + $buildId)
$process = Start-Process -FilePath $exePath -ArgumentList ('--self-test "' + $testDirectory + '"') -Wait -PassThru -WindowStyle Hidden
$testExitCode = $process.ExitCode
$process.Dispose()
if ($testExitCode -ne 0) { throw "Self-tests failed (exit code $testExitCode). No package created. See $testDirectory" }
$testReport = @(Get-Content -LiteralPath (Join-Path $testDirectory 'self-tests.txt'))
if ($testReport.Count -eq 0 -or @($testReport | Where-Object { $_ -notmatch '^PASS ' }).Count -gt 0) {
    throw "Missing or unsuccessful self-test results. No package created. See $testDirectory"
}
$testReport | Write-Output

# Explicit allowlist: never ship profiles, logs, device IDs, or the migration backup.
foreach ($fileName in @('ANLEITUNG.txt', 'UPDATE.txt')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $fileName) -Destination (Join-Path $payloadDirectory $fileName)
}
$files = @(foreach ($fileName in @('RigPilot.exe', 'ANLEITUNG.txt', 'UPDATE.txt')) {
    $filePath = Join-Path $payloadDirectory $fileName
    [ordered]@{
        Name = $fileName
        Bytes = (Get-Item -LiteralPath $filePath).Length
        SHA256 = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$manifest = [ordered]@{
    Product = 'RigPilot'
    Version = $version
    Platform = 'Windows x64'
    BuildId = $buildId
    BuiltAtUtc = [DateTime]::UtcNow.ToString('o')
    SelfTestsPassed = $testReport.Count
    HardwareAcceptance = 'Pending on target SimRig PC'
    Files = $files
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payloadDirectory 'VERSION.json') -Encoding UTF8

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Directory]::CreateDirectory($distributionDirectory) | Out-Null
$packageName = 'RigPilot-' + $version + '-portable-win-x64.zip'
$archivePath = Join-Path $distributionDirectory $packageName
[System.IO.Compression.ZipFile]::CreateFromDirectory($payloadDirectory, $archivePath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
($archiveHash + '  ' + $packageName) | Set-Content -LiteralPath ($archivePath + '.sha256') -Encoding ASCII
Write-Output ("Package: " + $archivePath)
Write-Output ("SHA256: " + $archiveHash)

# Separate executable download, plus an optional installer for the same tested build.
Copy-Item -LiteralPath $exePath -Destination (Join-Path $distributionDirectory 'RigPilot.exe')
if ($Installer) {
    $setupName = 'RigPilot-' + $version + '-Setup-x64'
    & $IsccPath '/Qp' ('/DPayloadDir=' + $payloadDirectory) ('/DAppVersion=' + $version) ('/DOutputDir=' + $distributionDirectory) ('/DOutputName=' + $setupName) (Join-Path $projectRoot 'installer\RigPilot.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed. Do not publish this build.' }
    if (-not (Test-Path -LiteralPath (Join-Path $distributionDirectory ($setupName + '.exe')))) { throw 'Installer output missing.' }
}
$checksums = @(Get-ChildItem -LiteralPath $distributionDirectory -File | Where-Object { $_.Extension -in @('.exe', '.zip') } | Sort-Object Name | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
})
$checksums | Set-Content -LiteralPath (Join-Path $distributionDirectory 'SHA256SUMS.txt') -Encoding ASCII
Write-Output ("Release files: " + $distributionDirectory)
