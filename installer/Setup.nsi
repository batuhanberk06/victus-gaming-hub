Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "Sections.nsh"

!define APP "Victus Gaming Hub"
!define VERSION "0.2.0"
!define PAYLOAD "..\artifacts\VictusGamingHub-${VERSION}"
!define KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\VictusHub"
Name "${APP}"
OutFile "..\artifacts\Setup.exe"
InstallDir "$PROGRAMFILES64\VictusHub"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 32
ShowInstDetails show
ShowUninstDetails show
VIProductVersion "0.2.0.0"
VIAddVersionKey /LANG=1033 "ProductName" "${APP}"
VIAddVersionKey /LANG=1033 "FileDescription" "${APP} Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Victus Gaming Hub contributors"
!define MUI_ICON "..\src\VictusHub.App\Assets\Victus.ico"
!define MUI_UNICON "..\src\VictusHub.App\Assets\Victus.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "Install Victus Gaming Hub for HP Victus 15 (board 8DD2, BIOS F.15).$\r$\n$\r$\nThis package includes the app and its local control service. CPU temperature and power monitoring use optional PawnIO.$\r$\n$\r$\nClose Victus Gaming Hub before continuing."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "Victus Gaming Hub has been installed.$\r$\n$\r$\nOpen it from the Start menu. When enabled, Windows startup opens the app in the system tray."
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "This package requires 64-bit Windows."
    Abort
  ${EndIf}
  SetRegView 64
FunctionEnd

Section "Victus Gaming Hub (required)" SecApp
  SectionIn RO
  InitPluginsDir
  SetOutPath "$PLUGINSDIR\payload"
  File /r /x "install-result.json" /x "*.pdb" "${PAYLOAD}\*.*"
  ; The startup component is section 1. One install transaction handles all service files.
  SectionGetFlags 1 $0
  IntOp $0 $0 & ${SF_SELECTED}
  StrCpy $2 ""
  ${If} $0 <> 0
    StrCpy $2 " -EnableStartup"
  ${EndIf}
  nsExec::ExecToStack '"$WINDIR\SysNative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\payload\Install.ps1" -NativeInstaller$2'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Installation could not finish.$\r$\n$1"
    SetErrorLevel 1
    Abort
  ${EndIf}
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "${KEY}" "DisplayName" "${APP}"
  WriteRegStr HKLM "${KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${KEY}" "Publisher" "Victus Gaming Hub contributors"
  WriteRegStr HKLM "${KEY}" "DisplayIcon" "$INSTDIR\app\VictusHub.App.exe,0"
  WriteRegStr HKLM "${KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKLM "${KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKLM "${KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${KEY}" "NoRepair" 1
SectionEnd

Section "Start with Windows" SecStartup
  ; Handled in the required section, and can be changed later in Settings.
SectionEnd

Section "CPU temperature and power (PawnIO)" SecPawnIO
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO" "DisplayVersion"
  ${If} $0 == ""
    SetOutPath "$PLUGINSDIR"
    File "..\.tools\sensors\PawnIO_setup.exe"
    DetailPrint "Opening the official signed PawnIO installer. Use the standard installation."
    ExecWait '"$PLUGINSDIR\PawnIO_setup.exe"' $0
    ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO" "DisplayVersion"
    ${If} $0 == ""
      MessageBox MB_OK "PawnIO was not installed. Profile and fan control will work; CPU temperature and power will be unavailable."
    ${EndIf}
  ${EndIf}
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} "App, private local control service, and Start menu shortcut. Includes the .NET runtime. Supports board 8DD2 / F.15."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStartup} "Launch in the system tray when this user signs in to Windows."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecPawnIO} "Use the official signed PawnIO sensor driver for CPU temperature and package power. Skips installation if already present."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  SetRegView 64
  ${If} $INSTDIR != "$PROGRAMFILES64\VictusHub"
    MessageBox MB_ICONSTOP "Unexpected installation path. No files were removed."
    Abort
  ${EndIf}
  SetOutPath "$TEMP"
  nsExec::ExecToStack '"$WINDIR\SysNative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\Uninstall.ps1" -KeepDirectory'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Settings could not be restored or the app is still running. Installation preserved.$\r$\n$1"
    SetErrorLevel 1
    Abort
  ${EndIf}
  DeleteRegKey HKLM "${KEY}"
  RMDir /r "$INSTDIR"
SectionEnd
