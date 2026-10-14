// SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VictusHub.Core.Control;

public static class ServiceAccess
{
    public static void EnsureRunning()
    {
        nint manager = OpenSCManager(null, null, 1);
        if (manager == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            nint service = OpenService(manager, "VictusHub.Control", 0x10);
            if (service == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Victus Hub kontrol hizmeti kurulu değil veya başlatma yetkisi eksik.");
            try
            {
                if (!StartService(service, 0, 0))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 1056) throw new Win32Exception(error);
                }
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
    public static void Stop()
    {
        nint manager = OpenSCManager(null, null, 1);
        if (manager == 0) return;
        try
        {
            nint service = OpenService(manager, "VictusHub.Control", 0x20);
            if (service == 0) return;
            try { ControlService(service, 1, out _); }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { public uint Type, State, Accepted, Win32Exit, SpecificExit, Checkpoint, WaitHint; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenService(nint manager, string name, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartService(nint service, uint count, nint arguments);
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ControlService(nint service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(nint handle);
}
