// SPDX-License-Identifier: GPL-3.0-or-later
using System.Management;

namespace VictusHub.Core.Hardware;

/// <summary>Fixed, read-only protocol operations. UI enums are never firmware command bytes.</summary>
public enum BiosReadOperation { SystemDesign, FanCount, MaxFan, FanLevels, ThermalSensor, GpuPowerPolicy }

public sealed record BiosReply(BiosReadOperation Operation, int? ReturnCode, byte[] Data, string? Error)
{
    public bool Success => ReturnCode == 0 && Error is null;
    public string Hex => Convert.ToHexString(Data);
}

public sealed record BiosSnapshot(DateTimeOffset CapturedAt, string Board, IReadOnlyList<BiosReply> Replies);

public sealed class HpBiosReader : IDisposable
{
    private readonly ManagementScope _scope;
    private readonly ManagementObject _bios;
    private readonly ManagementClass _dataClass;

    public HpBiosReader(string board)
    {
        if (!string.Equals(board, "8DD2", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("İlk sürüm yalnızca 8DD2 anakartında tanılama yapar.");
        _scope = new ManagementScope(@"\\.\root\wmi", new ConnectionOptions { EnablePrivileges = true, Timeout = TimeSpan.FromSeconds(5) });
        _scope.Connect();
        _bios = new ManagementObject(_scope, new ManagementPath("hpqBIntM.InstanceName=\"ACPI\\\\PNP0C14\\\\0_0\""), null);
        _bios.Get();
        _dataClass = new ManagementClass(_scope, new ManagementPath("hpqBDataIn"), null);
    }

    public static (uint CommandType, int OutputSize, byte[] Payload) Describe(BiosReadOperation operation) => operation switch
    {
        BiosReadOperation.SystemDesign => (0x28, 128, []),
        BiosReadOperation.FanCount => (0x10, 4, [0, 0, 0, 0]),
        BiosReadOperation.MaxFan => (0x26, 4, [0, 0, 0, 0]),
        BiosReadOperation.FanLevels => (0x2D, 128, [0, 0, 0, 0]),
        BiosReadOperation.ThermalSensor => (0x23, 4, [1, 0, 0, 0]),
        BiosReadOperation.GpuPowerPolicy => (0x21, 4, [0, 0, 0, 0]),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    public BiosReply Read(BiosReadOperation operation)
    {
        var definition = Describe(operation);
        var result = Invoke(definition.CommandType, definition.OutputSize, definition.Payload);
        return result.Error is null ? ValidateReply(operation, result.Code!.Value, result.Data) : new(operation, result.Code, result.Data, result.Error);
    }

    internal void SetProfileMode(DeviceProfile profile)
    {
        byte mode = profile switch { DeviceProfile.Default or DeviceProfile.Eco => 0, DeviceProfile.Performance => 1, DeviceProfile.Quiet => 3, _ => throw new ArgumentOutOfRangeException(nameof(profile)) };
        Write(0x1A, [0xFF, mode, 0, 0]);
    }
    internal void SetGpuPreset(DeviceProfile profile) => Write(0x22, [(byte)(profile == DeviceProfile.Performance ? 1 : 0), 1, 1, 0x57]);
    internal void SetFan(FanChoice fan)
    {
        if (!Enum.IsDefined(fan)) throw new ArgumentOutOfRangeException(nameof(fan));
        Write(0x27, [(byte)(fan == FanChoice.Max ? 1 : 0)]);
    }
    private void Write(uint type, byte[] bytes)
    {
        var reply = Invoke(type, 0, bytes);
        if (reply.Error is not null || reply.Code != 0) throw new InvalidOperationException(reply.Error ?? $"BIOS komutu 0x{type:X2}: dönüş kodu {reply.Code}");
    }
    private (int? Code, byte[] Data, string? Error) Invoke(uint type, int outputSize, byte[] payload)
    {
        try
        {
            using var data = _dataClass.CreateInstance() ?? throw new InvalidOperationException("WMI veri nesnesi oluşturulamadı.");
            data["Sign"] = new byte[] { 0x53, 0x45, 0x43, 0x55 };
            data["Command"] = (uint)0x20008;
            data["CommandType"] = type;
            data["Size"] = (uint)payload.Length;
            data["hpqBData"] = payload;
            string method = $"hpqBIOSInt{outputSize}";
            using var input = _bios.GetMethodParameters(method);
            input["InData"] = data;
            using var output = _bios.InvokeMethod(method, input, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(5) })
                ?? throw new InvalidOperationException("WMI yanıtı boş.");
            using var result = (ManagementBaseObject)output["OutData"];
            int code = Convert.ToInt32(result["rwReturnCode"]);
            var bytes = outputSize == 0 ? [] : result["Data"] as byte[] ?? [];
            return (code, bytes, null);
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return (null, [], e.Message);
        }
    }

    public static BiosReply ValidateReply(BiosReadOperation operation, int returnCode, byte[] bytes)
    {
        if (returnCode != 0) return new(operation, returnCode, bytes, $"BIOS dönüş kodu: {returnCode}");
        int required = operation switch
        {
            BiosReadOperation.SystemDesign => 9,
            BiosReadOperation.FanLevels => 2,
            BiosReadOperation.GpuPowerPolicy => 4,
            _ => 1
        };
        return bytes.Length < required
            ? new(operation, returnCode, bytes, $"Eksik BIOS yanıtı: {bytes.Length}/{required} bayt")
            : new(operation, returnCode, bytes, null);
    }

    public BiosSnapshot Capture(string board) => new(DateTimeOffset.Now, board, Enum.GetValues<BiosReadOperation>().Select(Read).ToArray());
    public void Dispose() { _bios.Dispose(); _dataClass.Dispose(); }
}
