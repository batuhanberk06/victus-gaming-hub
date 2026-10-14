[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$UserSid)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bundle = Join-Path $root 'artifacts\VictusGamingHub-0.2.0'
$installed = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'VictusHub'))
$report = Join-Path $root 'local-diagnostics\lifecycle.json'
try {
    if (Get-Process -Name 'VictusHub.App' -ErrorAction SilentlyContinue) { throw 'Close Victus Hub before the lifecycle test.' }
    # Test the exact latest uninstaller at its required, fixed installed location.
    Copy-Item -LiteralPath (Join-Path $bundle 'Uninstall.ps1') -Destination (Join-Path $installed 'Uninstall.ps1') -Force
    & (Join-Path $installed 'Uninstall.ps1')
    if (Test-Path -LiteralPath $installed) { throw 'Installed directory remained after uninstall.' }
    if (Get-Service -Name 'VictusHub.Control' -ErrorAction SilentlyContinue) { throw 'Control service remained after uninstall.' }
    $removed = [DateTimeOffset]::Now.ToString('o')
    & (Join-Path $bundle 'Install.ps1') -UserSid $UserSid
    $result = Get-Content -LiteralPath (Join-Path $bundle 'install-result.json') | ConvertFrom-Json
    if (-not $result.Success) { throw $result.Error }
    $service = Get-Service -Name 'VictusHub.Control'
    $service.WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    $service.Dispose()
    @{Success=$true;RemovedAt=$removed;ReinstalledAt=[DateTimeOffset]::Now.ToString('o');Root=$installed} | ConvertTo-Json | Set-Content -LiteralPath $report -Encoding UTF8
} catch {
    @{Success=$false;Error=$_.Exception.ToString();At=[DateTimeOffset]::Now.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $report -Encoding UTF8
    exit 1
}

