[CmdletBinding()]
param([switch]$KeepDirectory)
$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"'
    if ($KeepDirectory) { $arguments += ' -KeepDirectory' }
    $process = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    exit $process.ExitCode
}
$target = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$expected = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'VictusHub')).TrimEnd('\')
if (-not [string]::Equals($target,$expected,[StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing uninstall outside the installed VictusHub directory.' }
$active = @(Get-Process -Name 'VictusHub.App' -ErrorAction SilentlyContinue)
if ($active.Count -gt 0) {
    $quit = Start-Process -FilePath (Join-Path $target 'app\VictusHub.App.exe') -ArgumentList '--quit' -PassThru -WindowStyle Hidden
    if (-not $quit.WaitForExit(10000)) { throw 'Close Victus Gaming Hub from the tray, then retry uninstall.' }
    foreach ($process in $active) { if (-not $process.WaitForExit(30000)) { throw 'Victus Gaming Hub is still running. Installation preserved.' } }
}
$userSid = (Get-Content -LiteralPath (Join-Path $target 'app\service.json') -Raw | ConvertFrom-Json).AllowedUserSid
$service = Get-Service -Name 'VictusHub.Control' -ErrorAction SilentlyContinue
if ($service) {
    $state = Join-Path $target 'app\state'
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $state 'uninstall.request') -Value 'Default+Auto required before removal' -Encoding ASCII
    if ($service.Status -eq 'Stopped') { Start-Service -Name 'VictusHub.Control'; $service.WaitForStatus('Running',[TimeSpan]::FromSeconds(30)); $service.Refresh() }
    if ($service.Status -ne 'Stopped') {
        # The protected marker asks the service to restore Default+Auto before it stops.
        Stop-Service -Name 'VictusHub.Control'
        $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(35))
    }
    if (Test-Path -LiteralPath (Join-Path $state 'uninstall.request')) { throw 'Default+Auto could not be confirmed. Installation is preserved.' }
    if (Test-Path -LiteralPath (Join-Path $target 'app\state\eco-recovery.json')) { throw 'FPS rollback is pending. Preserve the installation and inspect its control log.' }
    $service.Dispose()
    & "$env:SystemRoot\System32\sc.exe" delete 'VictusHub.Control' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Service deletion failed.' }
}
$run = 'Registry::HKEY_USERS\'+$userSid+'\Software\Microsoft\Windows\CurrentVersion\Run'
$value = (Get-ItemProperty -LiteralPath $run -Name VictusHub -ErrorAction SilentlyContinue).VictusHub
if ($value -and $value.Contains($target)) { Remove-ItemProperty -LiteralPath $run -Name VictusHub }
$profile = (Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$userSid)).ProfileImagePath
$startupPath = Join-Path $profile 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup'
$userShell = Get-Item -LiteralPath ('Registry::HKEY_USERS\'+$userSid+'\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders') -ErrorAction SilentlyContinue
if ($userShell) {
    $configured = $userShell.GetValue('Startup',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    if ($configured) { $startupPath = $configured.Replace('%USERPROFILE%',$profile).Replace('%APPDATA%',(Join-Path $profile 'AppData\Roaming')) }
}
$startupLink = Join-Path $startupPath 'Victus Gaming Hub.lnk'
if (Test-Path -LiteralPath $startupLink) { Remove-Item -LiteralPath $startupLink }
foreach ($name in @('Victus Hub.lnk','Victus Gaming Hub.lnk')) {
    $link = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) $name
    if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link }
}
$key = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VictusHub'
if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Recurse }
# The absolute recursive target has been checked against the fixed Program Files directory above.
if (-not $KeepDirectory) { Remove-Item -LiteralPath $target -Recurse -Force }
