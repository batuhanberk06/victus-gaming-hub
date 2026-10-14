// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace VictusHub.Core.Hardware;

public sealed record DisplayReading(string Device, uint Width, uint Height, uint Frequency, uint BitsPerPixel);

/// <summary>Runs in the interactive user's session. Only refresh rate at the existing resolution is changed.</summary>
public static class DisplayRefresh
{
    public static DisplayReading Read()
    {
        var mode = CreateMode();
        string device = PrimaryDevice();
        if (!EnumDisplaySettings(device, -1, ref mode)) throw new InvalidOperationException("Ekran modu okunamadı.");
        return new(device, mode.Width, mode.Height, mode.Frequency, mode.BitsPerPixel);
    }
    public static void Set(uint frequency)
    {
        if (frequency is < 30 or > 500) throw new ArgumentOutOfRangeException(nameof(frequency));
        var current = Read();
        for (int index = 0; ; index++)
        {
            var mode = CreateMode();
            if (!EnumDisplaySettings(current.Device, index, ref mode)) break;
            if (mode.Width != current.Width || mode.Height != current.Height || mode.BitsPerPixel != current.BitsPerPixel || mode.Frequency != frequency) continue;
            mode.Fields = 0x00400000; // DM_DISPLAYFREQUENCY only
            int test = ChangeDisplaySettingsEx(current.Device, ref mode, 0, 2, 0); // CDS_TEST
            if (test != 0) throw new InvalidOperationException($"Ekran frekansı desteklenmiyor ({test}).");
            int status = ChangeDisplaySettingsEx(current.Device, ref mode, 0, 0, 0);
            if (status != 0) throw new InvalidOperationException($"Ekran frekansı değiştirilemedi ({status}).");
            if (Read().Frequency != frequency) throw new InvalidOperationException("Ekran frekansı doğrulanamadı.");
            return;
        }
        throw new NotSupportedException($"Mevcut çözünürlükte {frequency} Hz modu yok.");
    }
    private static DevMode CreateMode() => new() { Size = (ushort)Marshal.SizeOf<DevMode>() };
    private static string PrimaryDevice()
    {
        for (uint index = 0; index < 32; index++)
        {
            var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 0)) break;
            if ((device.Flags & 4) != 0 && device.Name is not null) return device.Name;
        }
        throw new InvalidOperationException("Birincil ekran bulunamadı.");
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Key;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TrueType, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? FormName;
        public ushort LogPixels;
        public uint BitsPerPixel, Width, Height, Flags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplaySettings(string? device, int mode, ref DevMode devMode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string? device, ref DevMode mode, nint hwnd, uint flags, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice display, uint flags);
}
