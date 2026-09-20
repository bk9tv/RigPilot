param([string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkDir 'csc.exe'
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $projectRoot 'RigPilot.exe' }
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/out:' + $OutputPath),('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')),('/win32icon:' + (Join-Path $projectRoot 'assets\rigpilot.ico')))
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $frameworkDir $reference) }
$compilerArgs += '/resource:' + (Join-Path $projectRoot 'src\Theme.xaml') + ',RigPilot.Theme.xaml'
$compilerArgs += '/resource:' + (Join-Path $projectRoot 'assets\rigpilot-ui.png') + ',RigPilot.Logo.png'
$compilerArgs += '/resource:' + (Join-Path $projectRoot 'assets\rigpilot.ico') + ',RigPilot.Icon.ico'
$compilerArgs += $sourceFiles
& $compilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Kompilierung fehlgeschlagen.' }
Get-Item -LiteralPath $OutputPath | Select-Object FullName, Length, LastWriteTime
