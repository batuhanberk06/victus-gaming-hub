// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace VictusHub.Core.Hardware;

public sealed record FrameLimitSetting(uint FramesPerSecond, bool IsPredefined, uint Location);

/// <summary>Documented NVIDIA DRS base-profile frame limiter, using the existing driver.</summary>
public sealed class NvidiaFrameLimiter : IDisposable
{
    public static uint EcoLimit(uint current) => current == 0 || current > 60 ? 60 : current;
    private const uint SettingId = 0x10835002;
    private const int SettingBytes = 12320, CurrentValueOffset = 8220;
    private nint _library;
    private QueryInterface _query = null!;

    public NvidiaFrameLimiter()
    {
        try
        {
            _library = NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvapi64.dll"));
            _query = Marshal.GetDelegateForFunctionPointer<QueryInterface>(NativeLibrary.GetExport(_library, "nvapi_QueryInterface"));
            Check(Function<Initialize>(0x0150E828)(), "NVAPI başlatma");
        }
        catch { Dispose(); throw; }
    }

    public FrameLimitSetting Read()
    {
        using var session = Open();
        nint setting = Marshal.AllocHGlobal(SettingBytes);
        try
        {
            Zero(setting);
            Marshal.WriteInt32(setting, SettingBytes | 0x10000);
            int status = Function<GetSetting>(0x73BF8338)(session.Handle, session.Profile, SettingId, setting);
            if (status == -160) return new(0, true, 0); // NVAPI_SETTING_NOT_FOUND: no explicit cap
            Check(status, "FPS sınırını okuma");
            uint type = unchecked((uint)Marshal.ReadInt32(setting, 4104));
            if (type != 0) throw new InvalidOperationException("NVIDIA FPS ayarı DWORD türünde değil.");
            return new(unchecked((uint)Marshal.ReadInt32(setting, CurrentValueOffset)), Marshal.ReadInt32(setting, 4112) != 0,
                unchecked((uint)Marshal.ReadInt32(setting, 4108)));
        }
        finally { Marshal.FreeHGlobal(setting); }
    }

    public void Set(uint fps)
    {
        if (fps > 1000) throw new ArgumentOutOfRangeException(nameof(fps));
        using var session = Open();
        nint setting = Marshal.AllocHGlobal(SettingBytes);
        try
        {
            Zero(setting);
            Marshal.WriteInt32(setting, SettingBytes | 0x10000);
            Marshal.WriteInt32(setting, 4100, unchecked((int)SettingId));
            Marshal.WriteInt32(setting, 4104, 0); // NVDRS_DWORD_TYPE
            Marshal.WriteInt32(setting, CurrentValueOffset, (int)fps);
            Check(Function<SetSetting>(0x577DD202)(session.Handle, session.Profile, setting), "FPS sınırını ayarlama");
            Check(Function<SaveSettings>(0xFCBC7E14)(session.Handle), "NVIDIA ayarını kaydetme");
        }
        finally { Marshal.FreeHGlobal(setting); }
    }

    public void Restore(FrameLimitSetting original)
    {
        // Restore an inherited/default setting by removing our override, not by pinning its old value.
        if (original.IsPredefined || original.Location != 0)
        {
            using var session = Open();
            int status = Function<DeleteSetting>(0xE4A26362)(session.Handle, session.Profile, SettingId);
            if (status != -160) Check(status, "FPS ayarını geri yükleme");
            Check(Function<SaveSettings>(0xFCBC7E14)(session.Handle), "NVIDIA geri yüklemeyi kaydetme");
        }
        else Set(original.FramesPerSecond);
    }

    private Session Open()
    {
        Check(Function<CreateSession>(0x0694D52E)(out nint handle), "NVIDIA ayar oturumu");
        var session = new Session(handle, Function<DestroySession>(0xDAD9CFF8));
        try
        {
            Check(Function<LoadSettings>(0x375DBD6B)(handle), "NVIDIA ayarlarını yükleme");
            Check(Function<GetBaseProfile>(0xDA8466A0)(handle, out nint profile), "NVIDIA temel profil");
            session.Profile = profile;
            return session;
        }
        catch { session.Dispose(); throw; }
    }
    private T Function<T>(uint id) where T : Delegate
    {
        nint address = _query(id);
        if (address == 0) throw new EntryPointNotFoundException($"NVAPI işlevi bulunamadı: {id:X8}");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    private static void Check(int status, string operation) { if (status != 0) throw new InvalidOperationException($"{operation}: NVAPI {status}"); }
    private static void Zero(nint pointer) { Marshal.Copy(new byte[SettingBytes], 0, pointer, SettingBytes); }
    public void Dispose() { if (_library != 0) NativeLibrary.Free(_library); _library = 0; }
    private sealed class Session(nint handle, DestroySession destroy) : IDisposable
    {
        public nint Handle { get; } = handle;
        public nint Profile { get; set; }
        public void Dispose() => destroy(Handle);
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint QueryInterface(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateSession(out nint session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DestroySession(nint session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LoadSettings(nint session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SaveSettings(nint session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetBaseProfile(nint session, out nint profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSetting(nint session, nint profile, uint id, nint setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSetting(nint session, nint profile, nint setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteSetting(nint session, nint profile, uint id);
}
