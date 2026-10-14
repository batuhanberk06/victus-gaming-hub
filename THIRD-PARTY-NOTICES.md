# Third-party notices

Victus Hub source: GPL-3.0-or-later. Copyright (c) 2026 Victus Hub contributors.

## HP BIOS protocol research

The requested Victus chevron is recreated as vector geometry for the app icon. Victus is an HP mark; this independent app is not an official HP product. Visual reference: https://commons.wikimedia.org/wiki/File:Hewlett-Packard_Victus_Logo.png .

The HP WMI command identifiers and structure definitions were researched using:

- OmenMon, Copyright Piotr Szczepański, GPL-3.0: https://github.com/OmenMon/OmenMon
- OmenCore, Copyright TheAntiPopAU, repository license MIT: https://github.com/theantipopau/omencore
- Read-only static inspection of the user's installed OMEN Gaming Hub. HP binaries are not redistributed.

Victus Hub does not redistribute OmenCore's separate power-unlock module or any unlicensed component.

## Microsoft .NET

The self-contained distribution includes the .NET and Windows Desktop runtimes and System.Management/System.ServiceProcess packages. Their original license and THIRD-PARTY-NOTICES files are retained with the runtime. .NET is licensed under MIT: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

## LibreHardwareMonitor

Sensor monitoring uses LibreHardwareMonitorLib 0.9.6, Mozilla Public License 2.0. Source and license: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6 . No modified LibreHardwareMonitor source is distributed. Its original package notices/licenses and dependencies are retained. CPU temperature/package power require the separately installed official PawnIO driver: https://pawnio.eu/ . PawnIO is not bundled or silently installed by Victus Hub.

## NVIDIA

NVML and NVAPI are loaded from the user's existing NVIDIA driver. NVIDIA driver DLLs are not redistributed. Public API declarations/constants were referenced from NVIDIA's documentation and MIT-licensed headers:

- https://docs.nvidia.com/deploy/nvml-api/latest/
- https://github.com/NVIDIA/nvapi

Copyright (c) 2019-2026 NVIDIA CORPORATION & AFFILIATES. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
