// SPDX-License-Identifier: GPL-3.0-or-later
using System.Management;

namespace VictusHub.Core.Hardware;

public sealed record MachineIdentity(string Model, string Board, string Bios, string Cpu)
{
    public static MachineIdentity Detect() => new(
        Read("Win32_ComputerSystem", "Model"), Read("Win32_BaseBoard", "Product"),
        Read("Win32_BIOS", "SMBIOSBIOSVersion"), Read("Win32_Processor", "Name"));

    private static string Read(string className, string property)
    {
        try
        {
            using var query = new ManagementObjectSearcher(@"root\cimv2", $"SELECT {property} FROM {className}");
            query.Options.Timeout = TimeSpan.FromSeconds(4);
            using var results = query.Get();
            foreach (ManagementObject row in results)
            {
                using (row) return row[property]?.ToString()?.Trim() ?? "Bilinmiyor";
            }
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return "Bilinmiyor";
    }
}
