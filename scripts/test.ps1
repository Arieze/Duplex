$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\Duplex.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Lancez scripts\build.ps1 avant les tests.' }
$results = Join-Path $root 'test-results'
$null = New-Item -ItemType Directory -Path $results -Force
foreach ($mode in @('--self-test','--ui-test')) {
    $process = Start-Process -FilePath $exe -ArgumentList @($mode,('"' + $results + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Échec de $mode (code $($process.ExitCode)). Consultez dist\diagnostic.txt." }
}
Get-Content -LiteralPath (Join-Path $results 'resultats.txt')
Get-Content -LiteralPath (Join-Path $results 'interface.txt')
