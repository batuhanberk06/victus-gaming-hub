// SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VictusHub.Core.Telemetry;

public sealed record MemoryReading(double UsedGiB, double TotalGiB, double Percent);
public sealed record CpuTimes(ulong Idle, ulong Kernel, ulong User);

public sealed class WindowsTelemetry
{
    private CpuTimes? _previous;
    public MemoryReading ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new((status.TotalPhysical - status.AvailablePhysical) / 1073741824d,
            status.TotalPhysical / 1073741824d, 100d * (status.TotalPhysical - status.AvailablePhysical) / status.TotalPhysical);
    }

    public double? ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var current = new CpuTimes(idle, kernel, user);
        var previous = _previous;
        _previous = current;
        return previous is null ? null : CalculateCpuUsage(previous, current);
    }

    public static double? CalculateCpuUsage(CpuTimes previous, CpuTimes current)
    {
        if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User) return null;
        ulong total = current.Kernel - previous.Kernel + current.User - previous.User;
        ulong idle = current.Idle - previous.Idle;
        return total == 0 || idle > total ? null : 100d * (total - idle) / total;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
