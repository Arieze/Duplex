param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'dist' }
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath $framework)) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 est nécessaire pour compiler Duplex.' }
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Xaml.dll','System.Windows.Forms.dll','System.Drawing.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path (Join-Path $framework 'WPF') $_) }
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output','/codepage:65001',('/out:' + (Join-Path $OutputDirectory 'Duplex.exe')),('/win32manifest:' + (Join-Path $root 'src\app.manifest')),('/win32icon:' + (Join-Path $root 'src\icon.ico')),('/resource:' + (Join-Path $root 'src\MainWindow.xaml') + ',Duplex.MainWindow.xaml'),('/resource:' + (Join-Path $root 'src\icon.ico') + ',Duplex.icon.ico'))
$arguments += $references
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.cs' | ForEach-Object FullName)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation échouée.' }
Write-Output (Join-Path $OutputDirectory 'Duplex.exe')
