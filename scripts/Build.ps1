[CmdletBinding()]
param([switch]$Package)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
Push-Location $root
try {
    & $dotnet build 'VictusHub.slnx' -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $dotnet run --project 'tests\VictusHub.Tests\VictusHub.Tests.csproj' -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($Package) {
        $destination = Join-Path $root 'artifacts\VictusGamingHub-0.2.0\app'
        foreach ($project in @('VictusHub.Service', 'VictusHub.App', 'VictusHub.Probe')) {
            & $dotnet publish ('src\'+$project+'\'+$project+'.csproj') -c Release -r win-x64 --self-contained true -o $destination -p:PublishTrimmed=false --nologo
            if ($LASTEXITCODE -ne 0) { throw ('Publish failed: '+$project) }
        }
        foreach ($name in @('Install.ps1','Uninstall.ps1','Install.cmd')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Split-Path -Parent $destination) -Force }
        foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Split-Path -Parent $destination) -Force }
        Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination (Split-Path -Parent $destination) -Recurse -Force
    }
} finally { Pop-Location }
