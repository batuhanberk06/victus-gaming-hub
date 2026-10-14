# 0.2.0 release verification

## Changes

- AC and battery profile/fan controls for the tested board 8DD2 / F.15. DC uses the same bounded V0 profile payload with byte2=0 and the known Max/Auto command; it sends no AC-only GPU preset. Profile changes can independently update the GPU policy in BIOS.
- Per-user Startup folder shortcut, migration/removal of the legacy Run entry, and silent handling of duplicate tray starts. The tray path creates/loads the WPF window in a hidden minimized state before returning to the tray.
- Fixed same-user quit signal for graceful installer/uninstaller shutdown; no arbitrary process termination.
- Offline NSIS Setup.exe with app/service, optional startup, optional official PawnIO and native uninstall.

## Real battery evidence

`local-diagnostics/dc-smoke-confirmed.json`: Default → Quiet → Performance → Eco → Default, then Max → Auto. All actions returned success on DC; Auto/Max was read back from BIOS. Final Default + Auto restoration succeeded. Performance changed GPU readback from `00010157` to `01010157` without an app GPU-policy write; Eco/Default returned `00010157`. This establishes accepted commands and readback, not full OEM power-sharing equivalence.

The earlier `dc-smoke.json` intentionally failed an incorrect test assumption that the GPU policy must remain unchanged when the app does not write it. The BIOS itself changes policy with the profile; the implementation already correctly omitted explicit GPU-policy writes on DC. Default + Auto restoration also succeeded in that test.

## Build inputs

- NSIS 3.11 portable zip, official SourceForge distribution: SHA256 `C7D27F780DDB6CFFB4730138CD1591E841F4B7EDB155856901CDF5F214394FA1`.
- PawnIO 2.2.0 installer, https://pawnio.eu/ official download: SHA256 `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`, Authenticode Valid, publisher namazso.eu.
- .NET SDK 10.0.402; LibreHardwareMonitorLib 0.9.6.

Development tools, local diagnostics, hardware identity data and install-result.json are not included in the end-user setup payload. Setup only targets the tested board and BIOS. The application and generated Setup.exe are not Authenticode-signed; the embedded separate PawnIO installer is signed.

## Completed packaging checks

Native Setup.exe installed successfully (exit 0). The Startup folder shortcut cold-launched one hidden tray instance, connected to the service, suppressed a duplicate launch, and exited gracefully. Startup enable/disable both passed. The real WPF Eco/Default test passed on DC: 144 to 60 to 144 Hz, FPS 0 to 60 to 0. Native uninstall passed with the tray app running: graceful quit, Default+Auto restoration, service deletion and removal of Program Files, startup shortcut, Start menu shortcut and uninstall registration. PawnIO remained installed. Evidence is in local-diagnostics/setup-install-test.json, startup-shortcut-test.json, startup-toggle-test.json, ui-smoke-release/ui-smoke.json and release-clean-state.json. The user subsequently installed the final Setup.exe and confirmed that everything works, including automatic startup. This last sign-in confirmation is user-reported rather than an agent-observed reboot.


