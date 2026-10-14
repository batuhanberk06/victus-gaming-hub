# Installed OMEN static protocol evidence

Investigation date: 2026-10-14. Target machine: Victus 15-fb3xxx, board 8DD2, BIOS F.15. This document records **static observations**, not successful device commands. No OEM assembly was loaded or executed, no hardware settings were changed, and no driver was installed during this investigation.

## Artifact identities and evidence

All assemblies below are in:

`C:\Program Files\WindowsApps\AD2F1837.OMENCommandCenter_1101.2609.11.0_x64__v10z8vjag6ke6\OmenCommandCenterApp`

| Assembly | SHA-256 | MVID | REA artifact / members Evidence IDs |
|---|---|---|---|
| PerformanceControl.dll | `8666cc0b521f223f1271eea361c148e9b4c14128b792363a3cdbef24ee31f18b` | `1e35c328-432a-474e-8901-1e61f065e79c` | `ev_d0a019aa25ee73e329cc051f6b02d8c7b9ea3f754a0d1fdd36dac237c7d8d8fb` / `ev_5ec074768b2b2cb6bfb257369fb0e525f60196e817ec8f57ce81f457729b351e` |
| HP.Omen.Core.Common.dll | `f55f3c163fbec467a86fe8c0d7eed9c3dbd59e9d06d4be137b5014ac1cde20ef` | `5cb43900-0ba6-4364-9ab7-9376be95b2cf` | `ev_f78ddcaf19f5587c34585e399a7614cd18735f40163a4114afd8c8ee5ab8b930` / unavailable members `ev_89bc8fcc86677b608669684949b936cca1bc65fa7252d97c3c64460623d6e997` |
| HP.Omen.Background.PerformanceControl.dll | `cddd8f2809c74cf811cc21aba9afc0d9964609a56e24f1b3ae6d9881aab9c576` | `b9399fa2-7a8d-49bc-895f-75c50cc5302c` | `ev_aea464b52a935fbf26faac9ca549c5451fc83c152a9d279422ca7d96f5e82382` / `ev_d18f0e62f8cee54de15da981964c9938e4744ef50f0901f2bf41f745352f5ae1` |
| HP.Omen.Core.Model.Device.dll | `0d5ea2003bd46a02f50ab6ec821579984651fcd5e0b2dfc994c02c12b34cfdf1` | `7ec97543-43fd-4725-8a5c-4080cd24579c` | `ev_4ad786fdde3b4f562581cc4d79c3585f02e02ec32e6494c42315683c8c1576fa` / unavailable members `ev_032541fe0a2b22cb5ce6f1d8ba07de8eba158f8237eae2c2e9b24f81413827c7` |
| AppHomeInfoSDK.dll | `a059d83fe57c77ba43e20e892945bcfada282ba5fd9cb9ed49b2a5a583bc474c` | `09291d74-ff76-4a35-b37a-16c8c3ba0b07` | `ev_f83dc71e60305cd6499123d3dc303d81890f7e511d8638a7bd10dfc92e13de8d` / `ev_3a3e6986a001719ff53dac4d6c0a8c468bd454431f41e03a4f920039b3ace8b1` |

REA's member parser exceeded its 64 MiB representation budget on Common.dll and Device.dll. Artifact identity inspection succeeded. REA member output for the other assemblies gives signatures, hashes, call/field anchors, and coverage limitations, but does not expose enum constants or every IL instruction. Missing details below were inspected using an independent, read-only `System.Reflection.Metadata` / `PEReader` parser under bundled PowerShell 7. Its source is in the session's temporary file `%TEMP%\victus-oem-metadata.ps1`; the parser executes, OEM code does not. Those detailed IL interpretations are supplemental static analysis, not separate canonical REA member Evidence. Tokens are meaningful only together with the exact assembly SHA-256 and MVID above.

## Enum values are different at different layers

`PerformanceControl.dll`: `Hp.Bridge.Client.SDKs.PerformanceControl.Enums.PerformanceMode`:

| Name | Value | Field token |
|---|---:|---|
| Default | 0 | `0x0400008A` |
| Performance | 1 | `0x0400008B` |
| Cool | 2 | `0x0400008C` |
| Quiet | 3 | `0x0400008D` |
| Eco | 256 (`0x100`) | `0x04000098` |

`MaxFanMode`: Off=0 (`0x04000087`), On=1 (`0x04000088`).

`Common.dll`: `HP.Omen.Core.Common.PowerControl.Enum.PerformanceModeOnUI`:
Default=0 (`0x04000D6F`), Performance=1 (`0x04000D70`), Cool=2, Quiet=3 (`0x04000D72`), Extreme=4, Balance=5, **Eco=6** (`0x04000D75`), Unleash=7.

Thus an app UI value, an SDK value, and a BIOS payload byte must not be treated as interchangeable. In particular Eco=6 is not an observed BIOS command.

## Performance / fan-mode command

The setter is in **Device.dll**, not Common.dll:

`HP.Omen.Core.Model.Device.Models.PerformanceControlHelper.SetFanMode`, method token `0x06000225`.

Observed construction:

```text
data = new byte[4]
data[0] = 0xFF
data[1] = (byte)mode
data[2] = Convert.ToByte(argument2)
data[3] = 0

if (argument1) apply thermal-policy conversion below

BiosWmiCmd_SetSync(command=0x20008, commandType=0x1A, data)
```

IL anchors: payload construction `IL_0000`–`IL_001C`; optional conversion `IL_003E`–`IL_0089`; call `IL_00AB`–`IL_00B8`. Argument names are intentionally not guessed from the IL alone.

When argument1 is true:

| Thermal-policy version | SDK input | Payload byte 1 |
|---|---|---:|
| 1 | Default=0 or Eco=256 | `0x30` |
| 1 | Performance=1 | `0x31` |
| 1 | Cool=2 | `0x50` |
| 1 | Quiet=3 | `0x03` (unchanged) |
| 0 | Eco=256 | `0x00` |
| other / other modes | unchanged before byte conversion | `(byte)mode` |

When argument1 is false, no conversion occurs; Eco=256 truncates to byte zero. This is an observation of the caller's IL, not proof that the hardware uses this path.

`GetThermalPolicyVersion`, Device.dll `0x06000223`, obtains `SystemDesignData[3]`. Only boards `8607`, `8746`, `8747`, `8749`, `874A`, `8748` receive an explicit version-zero override in this method. Board 8DD2 is not in that override list.

`OmenHsaClient.get_SystemDesignData`, Common.dll `0x06000146`, first reads a cached binary value named `SystemDesignData` in `HKCU\Software\HP\OMEN Ally\Settings`. If absent, it sends a read with command `0x20008`, command type `0x28`, input four zero bytes, expected output size 128, then caches the result. A current hardware read is preferable to assuming this persisted value is fresh.

### Installed background route and unresolved device-specific branch

Background.dll `Entry.ApplySettings`, token `0x0600007C`:

1. Restores profile settings (`IL_017B` onward), assigns `CurrentMode`, thermal mode and max-fan state.
2. Calls `SetFanMode(CurrentMode, false, IsNBBiosFanControl)` at `IL_01E3`–`IL_01F1`.
3. Calls `SetMaxFan(CurrentMaxFan)` at `IL_028F`–`IL_02A0`, subject to max-fan support / cleaning state.
4. Calls `UpdateEcoModeSettings` at `IL_02AC`–`IL_02AD`.

`Entry.RestoreStorageSettings`, token `0x0600008D`, gets the mode from Device.dll `GetPowerModeNB` at `IL_0069`. `GetPowerModeNB`, token `0x0600023E`, reads profile-storage key `Mode`, with a default fallback. It does **not** read active BIOS mode. `GetCurrentPerformanceModeForAI`, token `0x06000265`, likewise reads stored `Mode`; it is not hardware confirmation. No actual active BIOS profile readback was established by this bounded investigation.

The generic background caller explicitly disables the optional thermal-policy conversion. Other installed plugins / services may use a different route. **The actual 8DD2 runtime payload and side effects still need device-specific correlation** using OMEN-selected profiles and a read-only collector before Victus Hub enables writes. A successful BIOS return code alone is insufficient to establish profile semantics or full OEM parity.

### Meaning of payload byte 2: AC/DC fan control

Background.dll `Entry.IsNBBiosFanControl`, token `0x060000AA`, returns false when the device is a desktop, platform settings are absent, **or AC power is active** (`IL_0010`–`IL_001E`). On battery only, if `CurrentThermalMode == 1`, it returns Device.dll `IsBiosAutoFanControlInDcSupport`; otherwise false. Therefore this observed background route sends byte 2 as **zero on AC**. Setting it to one unconditionally would differ from the installed caller.

Device.dll `get_IsBiosAutoFanControlInDcSupport`, token `0x06000216`, is based on `PlatformSettings.BiosAutoFanControlInDc` and, for a formal BIOS release, a version comparison with `PlatformSettings.BiosAutoFanControlInDcVersion`. It is not derived directly from a bit in SystemDesignData. Its value on 8DD2 must be established from this device's applicable platform configuration before reproducing the battery branch.

Device.dll `get_IsBiosPerformanceControlSupport`, token `0x06000224`, is specifically `GetThermalPolicyVersion() == 1`. The name does not mean that no profile control is possible on policy version zero; the installed generic background setter still exists for that path.

## Max fan command

Common.dll `OmenHsaClient.SetMaxFan`, token `0x06000144`:

```text
data = [(byte)MaxFanMode]      // Off=0 / On=1
BiosWmiCmd_SetSync(0x20008, 0x27, data)
```

IL anchors: byte assignment `IL_0028`–`IL_002C`, call `IL_002D`–`IL_0036`. This proves the installed setter's construction; it does not prove supported state transitions on 8DD2, or that Off necessarily restored full automatic control in a runtime test.

## Eco host-side effects

Background.dll `Entry.UpdateEcoModeSettings`, token `0x06000096`, calls:

- `SetNvidiaMaxFrameRate` (`0x06000097`): NVIDIA presence check; captures the preceding global FPS cap; entering Eco chooses 60 FPS or preserves an already lower nonzero cap; leaving Eco restores the saved value. Calls the NVAPI wrapper, not NVML telemetry.
- `SetDisplayRefreshRate` (`0x06000098`): Eco selects 60 Hz and saves/restores normal refresh-rate state. Contains additional dynamic-refresh and graphics-switcher handling; arbitrary refresh restoration must not be approximated without preserving the user's settings.
- `SetIntelTurboBoost` (`0x0600009C`): guarded by `IsIntelPlatform`; it skips the AMD path on this Victus. Its lower-level operation uses HP's hardware helper; this is outside a new-driver-free implementation unless an existing supported API is established.
- Persists `IsInEcoMode` and notifies the overlay / integration.

Separate `SyncWindowsPowerMode`, token `0x060000C0`, is invoked only when Windows 11 power-mode sync is supported and enabled. It chooses best performance for the performance state, best efficiency for Eco, and the `NONE` mode otherwise, targeting AC or DC user-configured power mode as appropriate.

Background `PowerControlBase.get_IsInEcoMode`, token `0x06000118`, also considers automatic switching and ultimately compares `CurrentMode` to **256** at `IL_0020`–`IL_002B`.

These observations explain why sending only a fan-mode byte cannot reproduce every Eco effect. Exact CPU/GPU power-limit changes and any separate native-plugin route remain unverified.

## CPU temperature uses an RPC-backed SDK

Common.dll `OmenHsaClient.GetCpuTemperature`, token `0x06000131`, has async state-machine body `0x0600151C` (`<GetCpuTemperature>d__73.MoveNext`). At `IL_000D`, it delegates to:

`Hp.Bridge.Client.SDKs.AppHomeInfoSDK.HomeInfoHelper.GetCPUTempAsync`.

AppHomeInfoSDK.dll:

| API | Public method | Async MoveNext | Observed RPC request |
|---|---|---|---|
| GetCPUTempAsync | `0x06000005` | `0x06000011` | service `HomeInfoService`, action **17 decimal**, empty string input |
| GetHPWMICPUTempAsync | `0x06000006` | `0x06000013` | service `HomeInfoService`, action **23 decimal**, empty string input |

The request is assembled at `IL_0010`–`IL_0027` in each state machine. **RPC action 23 decimal is not BIOS command type `0x23`**, and these wrappers do not establish the underlying sensor register, provider, measurement meaning, or availability without HP services. The OmenCap / HomeInfoService handlers must be traced separately to establish those details.

A generic BIOS thermal query may expose temperature-like bytes, but this investigation did not prove that a byte in such a query is the same CPU package sensor displayed by OMEN. Victus Hub should mark CPU temperature unavailable until the actual source and device-specific scaling are verified.

## Remaining validation before enabling control

1. Obtain fresh SystemDesignData / thermal-policy version and actual device capabilities through fixed read-only queries.
2. Correlate OMEN Default, Quiet, Performance and Eco selections with persisted profile state, fresh hardware results, and the active control route. Record fan Auto / Max support per mode.
3. Establish which profile fields can be verified from hardware and which can only be recorded as the last successful requested setting.
4. Verify Max→Auto, sleep/awake, AC/DC transitions, and OEM-service interference under controlled tests before removing OMEN.
5. Decide whether Eco's FPS / refresh / optional Windows power-mode behavior is reproduced or separately configurable, with reliable restoration of prior settings.

The static command descriptions above are implementation evidence, **not permission to issue unverified writes** or a claim of complete 8DD2 compatibility.

## Follow-up: exact NVIDIA Eco FPS implementation

### Native artifact and evidence

Common.dll `NvApiWrapper.NVAPI_SetMaxFrameRate` (`0x060009AD`) delegates to P/Invoke `SetMaxFrameRate` (`0x0600099F`); getter `NVAPI_GetMaxFrameRate` (`0x060009AE`) delegates to P/Invoke `GetMaxFrameRate` (`0x060009A0`). Both import **NvidiaApi.dll**. Managed-declaration evidence: `ev_e23b6d87dd729ea9c1217bfd5c0b16d1e8c541090e2609c2d418335505154fb9`.

The installed `NvidiaApi.dll`, in the same OMEN directory as the assemblies above, has SHA-256:

`2da1eed8fd07d6459e53bec7bc018010b0438261b131b78619f45cedc7b0acb9`.

Ghidra could not admit the WindowsApps path because opening a protected path component was denied. A read-only source copy was placed at `C:\Users\User\AppData\Local\Temp\Victus-NvidiaApi-static.dll`; its SHA-256 was verified identical. Analysis used the already available Ghidra 12.1.4 provider; no engine or package was installed and the DLL was never executed. The native session was closed after inspection. Pseudocode is decompiler-derived; call IDs and machine instructions corroborate the API interpretation.

| Procedure / native address | Evidence ID | Observation |
|---|---|---|
| SetMaxFrameRate / `0x1800062D0` | `ev_760852d1092aba86466528f7caff4cc2ef930040d657e23b7a567e0e6530f931` | Complete setter construction and call sequence |
| GetMaxFrameRate / `0x180006470` | `ev_3ee111d91624620b2895faf32e27b94d8f961a406d1693ff17def3ade9040d9a` | Getter reads the same base-profile DWORD; returns zero for errors |
| API wrapper / `0x180002230` | `ev_e2dc22c230ec1676d2a84194a5c80831110122f145260ee487725e59d8493368` | QueryInterface ID `0xDA8466A0`: GetBaseProfile |
| API wrapper / `0x180002140` | `ev_5305d0147ed35820c8f2920ecd8ae0212688df19298ca77e354913fed498a8ba` | ID `0x73BF8338`: GetSetting |
| API wrapper / `0x180002060` | `ev_bfb39494842f0033478b7e7b4ea68384fba66cabe59cf3e1f7a2aa1c77daadc0` | ID `0x577DD202`: SetSetting |
| API wrapper / `0x180001D20` | `ev_8fcab603e69d5a3f062fa216c6b1ad3cfeaf3384e2cb83ff2da652f0c8848998` | ID `0x0694D52E`: CreateSession |
| API wrapper / `0x180001EC0` | `ev_f6886b13200d9785bdd01c11035daf5eaf5c227d633f8fa92aaf320a56467e63` | ID `0x375DBD6B`: LoadSettings |
| API wrapper / `0x180001F90` | `ev_be022423d8a5ebafa16f24eda8fdb8c187fbaf760dc0cb79f875911eecc69547` | ID `0xFCBC7E14`: SaveSettings |
| API wrapper / `0x180001DF0` | `ev_918eeafa08db8e01b9d3344b88439187efdc0158eb155b5b270f5286286352e3` | ID `0xDAD9CFF8`: DestroySession |

### Setting, encoding, and scope

The OEM setter initializes NVAPI, creates and loads a DRS session, obtains the **base profile**, reads setting **`0x10835002`**, and changes it only if the current value differs. On a successful change it saves the DRS settings, then destroys the session. This is a global/base-profile setting; the function does not enumerate, create, or edit per-application profiles.

The public NVIDIA header identifies this setting as `FRL_FPS_ID`. The value is a **plain DWORD FPS count**, with zero disabled. Eco's 60 FPS therefore uses raw value `0x0000003C`; no separate enable-bit mask is involved. The public header permits 0–1023; the installed OEM export applies its own stricter 0–1000 range. [NVIDIA NvApiDriverSettings.h](https://github.com/NVIDIA/nvapi/blob/main/NvApiDriverSettings.h).

The native code uses `NVDRS_SETTING_V1` version **`0x00013020`**. The official structure uses pack 4, 2048 UTF-16 code units for the setting name, and two 4100-byte value unions. Calculated size: **12320 bytes**. Relevant offsets: version 0; setting ID 4100; setting type 4104; location 4108; current-predefined flag 4112; predefined-valid flag 4116; predefined union 4120; current DWORD 8220. DWORD type is enum value zero. [NVIDIA nvapi.h](https://github.com/NVIDIA/nvapi/blob/main/nvapi.h).

All API names corresponding to the observed QueryInterface IDs are confirmed by NVIDIA's published interface table. Initialization uses `0x0150E828`; deletion of a user override, useful for exact restoration, is `NvAPI_DRS_DeleteProfileSetting`, `0xE4A26362`. [NVIDIA nvapi_interface.h](https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h).

### Minimal driver-free implementation recommendation

Victus Hub can bind the installed NVIDIA **nvapi64.dll** and its `nvapi_QueryInterface` export directly with a small C# interop layer; it need not redistribute or call HP's NvidiaApi.dll. Load it from the Windows system directory using safe DLL search, resolve only the documented function IDs above, use the exact structure layout, check every status, and destroy sessions in a finally block. The official headers carry MIT licensing; retain their copyright/license attribution if definitions are copied.

For entering Eco, first read a trustworthy snapshot of the base-profile setting and whether its value is user-defined or inherited/predefined. Apply `min(existing nonzero cap, 60)` or 60 if disabled. Persist the snapshot before changing the setting so restart/crash recovery remains possible. Repeated Eco application must not replace the original snapshot with Victus Hub's own cap.

For leaving Eco, restore the saved user value or remove only Victus Hub's override when the original setting was inherited/predefined, then save and read back. If another program/user changed the cap while Eco was active, avoid overwriting that later choice blindly. Treat unavailable/failed reads as errors, not zero: the OEM native getter's zero-on-error behavior loses the distinction between disabled and unreadable and should not be reproduced. Per-app overrides can still affect individual applications; full per-app enforcement was not observed in the OEM function. [NVIDIA DRS API documentation](https://docs.nvidia.com/nvapi/group__drsapi.html).

### Preservation of an existing lower cap in OMEN

Background.dll `Entry.SetNvidiaMaxFrameRate` (`0x06000097`) captures the old cap on entry when old Eco state is false and its previous operation-result state allows a fresh snapshot. If the existing nonzero cap is less than 60, the entering value is reduced to that cap (`IL_0083`–`IL_008B`). Exit reads the saved `PerformanceControlGlobalSettings.NvidiaMaxFrameRate`. Thus OMEN does preserve a preceding cap such as 30 FPS; its native getter's error ambiguity remains a limitation.

## Follow-up: GPU power policy is a separate write

Background.dll `PowerControlBase.SetTgpPpabAsync`, token `0x0600014F`, has async body `<SetTgpPpabAsync>d__168.MoveNext`, token **`0x06000277`**. It constructs:

```text
data[0] = Convert.ToByte(tgpEnable)
data[1] = Convert.ToByte(ppabEnable)
data[2] = (byte)state
data[3] = 87                  // 0x57
BiosWmiCmd_Set(0x20008, 0x22, data)
```

Payload construction is `IL_001B`–`IL_0055`; command call is `IL_0087`–`IL_0095`. `AdaptivePowerControlV1Nuget.GpuSet` (`0x0600005E`) passes `GpuData.CTGP`, `GpuData.Ppab` and `GpuData.DState` to this method. `SetTGPAsync` has a separate one-byte form in state-machine token `0x06000275`, also command type `0x22`. Do not shorten the four-byte policy based on that different caller.

These bytes align with the parent investigation's read snapshots `00 01 01 57` for Default/Quiet and `01 01 01 57` for Performance: the first byte corresponds to the cTGP-enable request. The snapshots were collected by the parent workflow; they are not runtime observations made by this static worker. The observed SetFanMode method itself does not send `0x22`; the managed GPU algorithm issues it separately.

### Adaptive policy limitation

`AdaptivePowerControlV1.UpdateTgpPpab`, token **`0x06000036`**, calculates policies using AC/DC state, current mode, Eco state, Max-Q capability, CPU power, concurrent-power/TPP support, user settings, and platform `PpabOnInPerformanceMode`. Its Default branch on AC requests cTGP false / PPAB true / state 1; when the branch considers Eco active it can request both false. The Performance branch can change PPAB according to CPU-power/Max-Q and configuration. Quiet=3 and Eco=256 do not have direct switch cases in this particular older class and return false through the default branch; other normalization and NuGet paths exist.

Therefore fixed policies matching idle snapshots provide a bounded initial implementation, **not proof of complete OEM adaptive power behavior**. Validate Performance→Quiet, Performance→Default, Eco transitions and battery behavior separately. Record any omitted adaptive CPU/GPU logic in the app's capabilities and release notes; do not label fixed bytes as reproducing the entire OMEN algorithm.
