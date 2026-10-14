// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text.Json;

namespace VictusHub.Core.Hardware;

public enum DeviceProfile { Eco, Quiet, Default, Performance }
public enum FanChoice { Auto, Max }
public sealed record ControlStatus(bool Success, string Message, DeviceProfile? LastRequestedProfile, FanChoice? ConfirmedFan, bool OnAc, string? GpuPolicy = null, VictusHub.Core.Telemetry.SensorReading? Sensors = null);

/// <summary>Commands are restricted to the captured 8DD2/F.15 V0 OEM presets. No raw EC or arbitrary writes.</summary>
public sealed class FirmwareController : IDisposable
{
    private readonly MachineIdentity _machine;
    private readonly HpBiosReader _bios;
    private readonly string _recoveryPath;
    private DeviceProfile? _lastRequested;
    private FanChoice _requestedFan = FanChoice.Auto;

    public FirmwareController(string recoveryDirectory)
    {
        _machine = MachineIdentity.Detect();
        if (_machine.Board != "8DD2" || _machine.Bios != "F.15") throw new NotSupportedException("Kontrol yalnızca doğrulanan 8DD2 / F.15 yapılandırmasına açık.");
        _bios = new HpBiosReader(_machine.Board);
        var design = _bios.Read(BiosReadOperation.SystemDesign);
        if (!design.Success || design.Data[3] != 0) { _bios.Dispose(); throw new NotSupportedException("Beklenen V0 termal politika doğrulanamadı."); }
        Directory.CreateDirectory(recoveryDirectory);
        _recoveryPath = Path.Combine(recoveryDirectory, "eco-recovery.json");
        RestoreFrameLimit();
    }
    public static bool OnAc
    {
        get => GetSystemPowerStatus(out var status) && status.AcLineStatus == 1;
    }
    public ControlStatus Status()
    {
        var fan = _bios.Read(BiosReadOperation.MaxFan);
        var gpu = _bios.Read(BiosReadOperation.GpuPowerPolicy);
        return new(fan.Success && gpu.Success, fan.Success ? "Fan durumu BIOS'tan okundu; profil adı son gönderilen istektir." : fan.Error ?? "Fan durumu okunamadı.",
            _lastRequested, fan.Success ? (fan.Data[0] == 1 ? FanChoice.Max : FanChoice.Auto) : null, OnAc, gpu.Success ? gpu.Hex : null);
    }
    public ControlStatus Apply(DeviceProfile profile)
    {
        if (!Enum.IsDefined(profile)) return Failure("Bilinmeyen profil.");
        bool onAc = OnAc;
        try
        {
            // Prepare and persist desktop driver rollback before entering the firmware preset.
            if (profile == DeviceProfile.Eco) ApplyFrameLimit();
            else RestoreFrameLimit();
            _bios.SetFan(FanChoice.Auto);
            _bios.SetProfileMode(profile);
            // Battery GPU limits remain under firmware control. Never force AC boost policy on DC.
            if (OnAc != onAc) throw new InvalidOperationException("Power source changed; retry the profile.");
            if (onAc) _bios.SetGpuPreset(profile);
            FanChoice fan = profile == DeviceProfile.Eco ? FanChoice.Auto : _requestedFan;
            _bios.SetFan(fan);
            var check = Status();
            string expected = profile == DeviceProfile.Performance ? "01010157" : "00010157";
            if (check.OnAc != onAc || !check.Success || check.ConfirmedFan != fan || (onAc && check.GpuPolicy != expected)) throw new InvalidOperationException("Fan/GPU policy readback did not match the request.");
            _lastRequested = profile;
            return Status() with { Message = onAc ? "Profile accepted; fan and GPU policy confirmed. Profile name is the last request." : "Profile accepted on battery; fan confirmed. GPU limits remain managed by firmware." };
        }
        catch (Exception e)
        {
            // With no hardware profile getter we cannot honestly reconstruct an unknown old profile.
            // Hand back Auto and report the partial transaction rather than claim atomic success.
            try { _bios.SetFan(FanChoice.Auto); _bios.SetProfileMode(DeviceProfile.Default); if (OnAc) _bios.SetGpuPreset(DeviceProfile.Default); } catch { }
            try { RestoreFrameLimit(); } catch { }
            _lastRequested = null;
            return Failure("Profil tamamlanamadı; Default + Auto'ya dönüş denendi. " + e.Message);
        }
    }
    public ControlStatus SetFan(FanChoice fan)
    {
        if (!Enum.IsDefined(fan)) return Failure("Bilinmeyen fan seçimi.");
        if (_lastRequested == DeviceProfile.Eco && fan == FanChoice.Max) return Failure("Eco profili yalnızca Auto fan kullanır.");
        try
        {
            _bios.SetFan(fan);
            var status = Status();
            if (status.ConfirmedFan != fan) return Failure("Fan seçimi BIOS'tan doğrulanamadı.");
            _requestedFan = fan;
            return status with { Message = "Fan seçimi BIOS'tan doğrulandı." };
        }
        catch (Exception e) { return Failure(e.Message); }
    }
    public ControlStatus RestoreDefault()
    {
        _requestedFan = FanChoice.Auto;
        return Apply(DeviceProfile.Default);
    }
    public void Release()
    {
        var errors = new List<Exception>();
        try { _bios.SetFan(FanChoice.Auto); } catch (Exception e) { errors.Add(e); }
        try { RestoreFrameLimit(); } catch (Exception e) { errors.Add(e); }
        if (errors.Count > 0) throw new AggregateException("Bazı geri yüklemeler tamamlanamadı.", errors);
    }
    private ControlStatus Failure(string message) => new(false, message, _lastRequested, null, OnAc);

    private sealed record FrameRecovery(FrameLimitSetting Original, uint Applied);
    private void ApplyFrameLimit()
    {
        if (File.Exists(_recoveryPath)) return; // Existing owned Eco transaction.
        using var limiter = new NvidiaFrameLimiter();
        var original = limiter.Read();
        uint applied = NvidiaFrameLimiter.EcoLimit(original.FramesPerSecond);
        var recovery = new FrameRecovery(original, applied);
        File.WriteAllText(_recoveryPath + ".tmp", JsonSerializer.Serialize(recovery));
        File.Move(_recoveryPath + ".tmp", _recoveryPath, true);
        if (original.FramesPerSecond != applied) limiter.Set(applied);
        if (limiter.Read().FramesPerSecond != applied) throw new InvalidOperationException("Eco FPS sınırı doğrulanamadı.");
    }
    private void RestoreFrameLimit()
    {
        if (!File.Exists(_recoveryPath)) return;
        var recovery = JsonSerializer.Deserialize<FrameRecovery>(File.ReadAllText(_recoveryPath)) ?? throw new InvalidOperationException("Eco geri yükleme kaydı okunamadı.");
        using var limiter = new NvidiaFrameLimiter();
        if (limiter.Read().FramesPerSecond == recovery.Applied)
        {
            limiter.Restore(recovery.Original);
            if (limiter.Read().FramesPerSecond != recovery.Original.FramesPerSecond) throw new InvalidOperationException("Eski FPS sınırı geri yüklenemedi.");
        }
        File.Delete(_recoveryPath);
    }
    public void Dispose() => _bios.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte AcLineStatus, BatteryFlag, BatteryPercent, SystemStatus; public uint BatteryLife, BatteryFullLife; }
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
