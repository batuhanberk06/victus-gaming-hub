[CmdletBinding()]
param([string]$UserSid, [switch]$NativeInstaller, [switch]$EnableStartup)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($UserSid)) {
    $interactive = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | Where-Object { $_.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId })
    if ($interactive.Count -gt 0) { $UserSid = (Invoke-CimMethod -InputObject $interactive[0] -MethodName GetOwnerSid).Sid }
    if (-not $UserSid) { $UserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value }
}
if ($UserSid -notmatch '^S-1-5-21-\d+-\d+-\d+-\d+$') { throw 'Expected a local user account SID.' }
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'" -UserSid '+$UserSid
    if ($NativeInstaller) { $arguments += ' -NativeInstaller' }
    if ($EnableStartup) { $arguments += ' -EnableStartup' }
    $process = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    exit $process.ExitCode
}
$target = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'VictusHub'))
$source = Join-Path $PSScriptRoot 'app'
$resultPath = Join-Path $PSScriptRoot 'install-result.json'
function Invoke-Sc([string[]]$Arguments) {
    # PowerShell 5 uses ArgumentList as a single command line. All service names/paths are fixed locally.
    $quoted = $Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"'+$_.Replace('"','\"')+'"' } else { $_ } }
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $env:SystemRoot 'System32\sc.exe'
    $info.Arguments = $quoted -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($info)
    $output = $process.StandardOutput.ReadToEnd() + $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw ('sc.exe failed ('+$process.ExitCode+'): '+$output) }
}
try {
    if (-not (Test-Path -LiteralPath (Join-Path $source 'VictusHub.Service.exe'))) { throw 'Run Build.ps1 -Package before installing.' }
    $board = (Get-CimInstance Win32_BaseBoard).Product
    $bios = (Get-CimInstance Win32_BIOS).SMBIOSBIOSVersion
    if ($board -ne '8DD2' -or $bios -ne 'F.15') { throw 'This alpha supports only board 8DD2 / BIOS F.15.' }
    $existing = Get-Service -Name 'VictusHub.Control' -ErrorAction SilentlyContinue
    if ($existing -and $existing.Status -ne 'Stopped') { Stop-Service -Name 'VictusHub.Control'; $existing.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(35)) }
    New-Item -ItemType Directory -Path (Join-Path $target 'app') -Force | Out-Null
    foreach ($item in (Get-ChildItem -LiteralPath $source -Force)) { Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $target 'app') -Recurse -Force }
    @{AllowedUserSid=$UserSid} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'app\service.json') -Encoding UTF8
    foreach ($name in @('Uninstall.ps1','README.md','LICENSE','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $target -Force }
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'docs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination $target -Recurse -Force }
    $exe = Join-Path $target 'app\VictusHub.Service.exe'
    if (-not $existing) { Invoke-Sc @('create','VictusHub.Control','binPath=',('"'+$exe+'"'),'start=','demand','DisplayName=','Victus Hub Control') }
    Invoke-Sc @('description','VictusHub.Control','Local HP profile/fan control and optional PawnIO sensor monitoring.')
    $descriptor = 'D:(A;;CCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;LCRPWPLO;;;'+$UserSid+')'
    Invoke-Sc @('sdset','VictusHub.Control',$descriptor)
    $programs = [Environment]::GetFolderPath('CommonPrograms')
    $shell = New-Object -ComObject WScript.Shell
    $oldLink = Join-Path $programs 'Victus Hub.lnk'
    if (Test-Path -LiteralPath $oldLink) {
        $oldShortcut = $shell.CreateShortcut($oldLink)
        if ($oldShortcut.TargetPath -eq (Join-Path $target 'app\VictusHub.App.exe')) { Remove-Item -LiteralPath $oldLink }
    }
    $shortcut = $shell.CreateShortcut((Join-Path $programs 'Victus Gaming Hub.lnk'))
    $shortcut.TargetPath = Join-Path $target 'app\VictusHub.App.exe'
    $shortcut.WorkingDirectory = Join-Path $target 'app'
    $shortcut.IconLocation = (Join-Path $target 'app\VictusHub.App.exe') + ',0'
    $shortcut.Save()
    $uninstall = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VictusHub'
    New-Item -Path $uninstall -Force | Out-Null
    $uninstallCommand = '"'+$env:SystemRoot+'\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $target 'Uninstall.ps1')+'"'
    if (-not $NativeInstaller) {
        foreach ($entry in @{DisplayName='Victus Gaming Hub';DisplayIcon=((Join-Path $target 'app\VictusHub.App.exe')+',0');DisplayVersion='0.2.0';Publisher='Victus Gaming Hub contributors';InstallLocation=$target;UninstallString=$uninstallCommand}.GetEnumerator()) { New-ItemProperty -LiteralPath $uninstall -Name $entry.Key -Value $entry.Value -PropertyType String -Force | Out-Null }
    }
    if ($NativeInstaller -or $EnableStartup) {
        $profile = (Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$UserSid)).ProfileImagePath
        $startupPath = Join-Path $profile 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup'
        $userShellPath = 'Registry::HKEY_USERS\'+$UserSid+'\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
        $userShell = Get-Item -LiteralPath $userShellPath -ErrorAction SilentlyContinue
        if ($userShell) {
            $configured = $userShell.GetValue('Startup',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            if ($configured) { $startupPath = $configured.Replace('%USERPROFILE%',$profile).Replace('%APPDATA%',(Join-Path $profile 'AppData\Roaming')) }
        }
        $startupLink = Join-Path $startupPath 'Victus Gaming Hub.lnk'
        if ($EnableStartup) {
            New-Item -ItemType Directory -Path $startupPath -Force | Out-Null
            $startup = $shell.CreateShortcut($startupLink)
            $startup.TargetPath = Join-Path $target 'app\VictusHub.App.exe'
            $startup.Arguments = '--tray'
            $startup.WorkingDirectory = Join-Path $target 'app'
            $startup.IconLocation = $startup.TargetPath + ',0'
            $startup.Save()
        } elseif (Test-Path -LiteralPath $startupLink) { Remove-Item -LiteralPath $startupLink }
        $oldRun = 'Registry::HKEY_USERS\'+$UserSid+'\Software\Microsoft\Windows\CurrentVersion\Run'
        if (Test-Path -LiteralPath $oldRun) { Remove-ItemProperty -LiteralPath $oldRun -Name VictusHub -ErrorAction SilentlyContinue }
    }
    Start-Service -Name 'VictusHub.Control'
    @{Success=$true;InstalledAt=$target;UserSid=$UserSid;At=[DateTimeOffset]::Now.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
} catch {
    @{Success=$false;Error=$_.Exception.Message;At=[DateTimeOffset]::Now.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Output $_.Exception.Message
    exit 1
}
