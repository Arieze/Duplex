$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1')
& (Join-Path $PSScriptRoot 'test.ps1')
$dist = Join-Path $root 'dist'
$exe = Join-Path $dist 'Duplex.exe'
$version = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion -replace '\.0$',''
Copy-Item -LiteralPath (Join-Path $root 'docs\UTILISATION.md') -Destination (Join-Path $dist 'LIRE-MOI.md') -Force
$archive = Join-Path $dist "Duplex-$version-portable.zip"
Compress-Archive -LiteralPath @($exe,(Join-Path $dist 'LIRE-MOI.md')) -DestinationPath $archive -Force
$lines = @($exe,$archive) | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) }
Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Value $lines -Encoding ascii
Write-Output $archive
