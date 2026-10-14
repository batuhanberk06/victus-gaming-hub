# Battery control evidence — 8DD2 / F.15

Investigation: 2026-10-14. This pass was read-only: no BIOS commands, service changes, app installation, or hardware writes. It distinguishes previously retained static OEM evidence from new filesystem observations. This document does not establish successful DC operation.

## Retained OEM evidence

See [PROTOCOL-EVIDENCE.md](PROTOCOL-EVIDENCE.md) for exact assembly hashes, MVIDs, REA Evidence IDs and method tokens. The original OMEN 1101.2609.11.0 assemblies were inspected without executing OEM code.

- `PerformanceControlHelper.SetFanMode` (`Device.dll`, `0x06000225`) constructs `FF mode byte2 00`, command `0x20008`, type `0x1A`.
- `Entry.IsNBBiosFanControl` (`Background.dll`, `0x060000AA`) returns false on AC. On battery, when `CurrentThermalMode == 1`, it returns `IsBiosAutoFanControlInDcSupport`; otherwise false.
- `get_IsBiosAutoFanControlInDcSupport` (`Device.dll`, `0x06000216`) depends on the applicable `PlatformSettings.BiosAutoFanControlInDc` and, for formal BIOS releases, the configured `BiosAutoFanControlInDcVersion` threshold.
- The applicable configuration value for board 8DD2/F.15 was **not established**. Setting byte2 to one universally is not supported by this evidence. Setting it to zero on DC is a possible OEM branch, but the investigation has not shown which branch this device uses for each fan/thermal state.
- `SetMaxFan` has a one-byte `0x27` payload, 0/1. The retained method-level evidence does not establish a DC restriction or successful DC Max operation.
- Previous GPU policy observations (`00010157` baseline and `01010157` Performance) were on AC. They do not establish the appropriate DC GPU policy.

## New bounded artifact search

The OMEN WindowsApps directory and its per-user package directory are no longer present. The original managed assemblies cannot be inspected again from their earlier paths. No duplicate REA session was opened for a missing target.

Remaining `HKCU\\Software\\HP\\OMEN Ally` keys contain cached `SystemDesignData`, installation-monitor and GameBooster keys; they do not provide the required platform DC flag. `ProgramData\\HP\\bridge` contains an analytics JSON, not the applicable platform settings. The HP OmenCap driver-store component remains, but does not contain the previously inspected managed Device/Common/Background assemblies or a platform JSON/XML configuration at the searched paths.

The retained temporary metadata parser remains, but does not embed OEM assembly copies. `%TEMP%\\VictusHubResearch-20261014` contains earlier open-source snapshots only. A targeted UTF-8/UTF-16 search in the existing 71 MB OMEN background crash dump found neither `BiosAutoFanControlInDc` nor `8DD2`; no general dump content was exported. These negative searches are bounded and are not proof that no recoverable configuration exists anywhere.

Web searches for the exact flag, including flag plus board, returned no result.

## Open-source corroboration and limits

The retained [OmenMon BiosCtl.cs](https://github.com/OmenMon/OmenMon/blob/d89340e6d4dc9802b609390729b0bfaec05563e2/Hardware/BiosCtl.cs) always constructs `FF mode 00 00` in `SetFanMode`. SHA-256 of the retained snapshot: `E897012AC440AA1B132A4D9FBD4DBE9FAEFDA9469591342FA08BF735538B4A78`. This shows an implementation choice, not validation on this board on battery.

The retained [OmenCore model database](https://github.com/theantipopau/omencore/blob/2f8772cd9c43051b3c9a0c8d27785b902df5785e/src/OmenCore.Core/Hardware/ModelCapabilityDatabase.cs#L1944) has an exact 8DD2 entry, but explicitly `UserVerified=false`, inherited capability flags and no DC mapping. Snapshot SHA-256: `3BE10D92D806A356982567F90FB75757E444A91D14B97CF44B34CACBB38BF027`.

## Implementation recommendation

1. Do not remove all DC guards and reuse AC GPU boost presets without validation. Do not claim complete OEM battery equivalence.
2. Preserve BIOS board/version and return-code checks. A DC-specific implementation can avoid writing GPU power policy entirely, leaving firmware battery power management in charge; success verification must then not compare against an AC GPU preset.
3. To enable firmware profile/fan controls on DC, obtain a real DC test of the chosen conservative `0x1A` payload and `0x27` transitions on this machine, recording actual power source, BIOS errors, fan-state readback and sensor behavior. BIOS success alone is not proof of profile performance semantics; no reliable active profile getter is established.
4. If that evidence cannot be obtained, Eco's existing reversible Windows refresh/FPS behavior can still be available on battery without pretending that an unverified BIOS profile was applied. Other firmware profile controls should remain unavailable until their DC path is established.
5. Handle AC/DC transitions independently of UI visibility; a lost adapter must never trigger an AC-only boost write. Rollback and application exit must restore owned host settings even on battery.

Status: OEM DC branch **partially answered**; exact device configuration **unresolved**; device DC execution **not tested by this investigation**.

## Subsequent device validation by the primary agent

The real DC test was completed after this read-only investigation. See docs/RELEASE-0.2.0.md and local-diagnostics/dc-smoke-confirmed.json. The bounded legacy byte2=0 route and 0x27 Max/Auto returned success; fan readbacks matched. BIOS independently changed GPU policy after Performance although the DC application path sends no 0x22 write. This does not resolve the optional OEM DC auto-fan flag or establish full OEM adaptive parity.

