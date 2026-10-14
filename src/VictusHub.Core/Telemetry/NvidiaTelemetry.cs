// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text;

namespace VictusHub.Core.Telemetry;

public sealed record GpuReading(string Name, double? Usage, double? Temperature, double? PowerWatts, string? Error);

/// <summary>Reads the system NVIDIA driver directly. Never starts nvidia-smi or changes GPU settings.</summary>
public sealed class NvidiaTelemetry : IDisposable
{
    private nint _library, _device;
    private bool _initialized;
    private string _name = "NVIDIA GPU";
    private NvmlShutdown? _shutdown;
    private NvmlUtilization? _utilization;
    private NvmlTemperature? _temperature;
    private NvmlPower? _power;
    private string? _error;

    public NvidiaTelemetry()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll");
            _library = NativeLibrary.Load(path);
            var init = Load<NvmlInit>("nvmlInit_v2");
            _shutdown = Load<NvmlShutdown>("nvmlShutdown");
            int status = init();
            if (status != 0) throw new InvalidOperationException($"NVML başlatılamadı ({status}).");
            _initialized = true;
            var handle = Load<NvmlHandle>("nvmlDeviceGetHandleByIndex_v2");
            status = handle(0, out _device);
            if (status != 0) throw new InvalidOperationException($"NVIDIA cihazı okunamadı ({status}).");
            var name = new StringBuilder(96);
            if (Load<NvmlName>("nvmlDeviceGetName")(_device, name, (uint)name.Capacity) == 0) _name = name.ToString();
            _utilization = Load<NvmlUtilization>("nvmlDeviceGetUtilizationRates");
            _temperature = Load<NvmlTemperature>("nvmlDeviceGetTemperature");
            _power = Load<NvmlPower>("nvmlDeviceGetPowerUsage");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            _error = e.Message;
            Release();
        }
    }

    public GpuReading Read()
    {
        if (!_initialized || _device == 0) return new(_name, null, null, null, _error ?? "GPU verisi alınamıyor.");
        int usageStatus = _utilization!(_device, out var utilization);
        int temperatureStatus = _temperature!(_device, 0, out var temp);
        int powerStatus = _power!(_device, out var milliwatts);
        double? usage = usageStatus == 0 ? utilization.Gpu : null;
        double? temperature = temperatureStatus == 0 ? temp : null;
        double? power = powerStatus == 0 ? milliwatts / 1000d : null;
        var errors = new List<string>();
        if (usageStatus != 0) errors.Add($"NVML kullanım: {usageStatus}");
        if (temperatureStatus != 0) errors.Add($"NVML sıcaklık: {temperatureStatus}");
        if (powerStatus != 0) errors.Add($"NVML güç: {powerStatus}");
        return new(_name, usage, temperature, power, errors.Count == 0 ? null : string.Join("; ", errors));
    }

    private T Load<T>(string symbol) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, symbol));
    private void Release()
    {
        if (_initialized) _shutdown?.Invoke();
        _initialized = false;
        _device = 0;
        if (_library != 0) NativeLibrary.Free(_library);
        _library = 0;
    }
    public void Dispose() => Release();
    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlInit();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlShutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlHandle(uint index, out nint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int NvmlName(nint device, StringBuilder name, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlUtilization(nint device, out Utilization utilization);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlTemperature(nint device, uint sensor, out uint temperature);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlPower(nint device, out uint milliwatts);
}
