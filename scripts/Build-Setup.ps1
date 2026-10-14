[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler = Join-Path $root '.tools\installer\nsis-3.11\makensis.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'NSIS 3.11 portable compiler is required under .tools/installer/nsis-3.11.' }
$pawn = Join-Path $root '.tools\sensors\PawnIO_setup.exe'
if ((Get-FileHash -LiteralPath $pawn).Hash -ne '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032') { throw 'Unexpected PawnIO installer checksum.' }
if ((Get-AuthenticodeSignature -LiteralPath $pawn).Status -ne 'Valid') { throw 'PawnIO signature is not valid.' }
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -Package }
Push-Location (Join-Path $root 'installer')
try {
    & $compiler '/V2' 'Setup.nsi'
    if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
    $setup = Join-Path $root 'artifacts\Setup.exe'
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
    ($hash+'  Setup.exe') | Set-Content -LiteralPath (Join-Path $root 'artifacts\Setup.sha256') -Encoding ASCII
    Get-Item -LiteralPath $setup | Select-Object FullName,Length
} finally { Pop-Location }
