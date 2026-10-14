// SPDX-License-Identifier: GPL-3.0-or-later
using LibreHardwareMonitor.Hardware;

namespace VictusHub.Core.Telemetry;

public sealed record CpuReading(double? Usage, double? Temperature, double? PowerWatts);
public sealed record SensorReading(DateTimeOffset At, CpuReading Cpu, GpuReading? Nvidia, GpuReading? Integrated);

/// <summary>Read-only CPU/GPU monitoring. Called by the privileged service; no sensor controls are set.</summary>
public sealed class HardwareSensors : IDisposable
{
    private readonly Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true };
    public HardwareSensors()
    {
        try { _computer.Open(); }
        catch { _computer.Close(); throw; }
    }
    public SensorReading Read()
    {
        CpuReading cpu = new(null, null, null);
        GpuReading? nvidia = null, integrated = null;
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            if (hardware.HardwareType == HardwareType.Cpu)
                cpu = new(Value(hardware, SensorType.Load, "CPU Total"),
                    Positive(Value(hardware, SensorType.Temperature, "Core (Tctl/Tdie)")),
                    Positive(Value(hardware, SensorType.Power, "Package")));
            else if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
            {
                var gpu = new GpuReading(hardware.Name, Value(hardware, SensorType.Load, "GPU Core"),
                    Value(hardware, SensorType.Temperature, "GPU Core") ?? Value(hardware, SensorType.Temperature, "GPU VR SoC"),
                    Value(hardware, SensorType.Power, "GPU Package") ?? Value(hardware, SensorType.Power, "GPU Core"), null);
                if (hardware.HardwareType == HardwareType.GpuNvidia) nvidia = gpu;
                else integrated = gpu;
            }
        }
        return new(DateTimeOffset.Now, cpu, nvidia, integrated);
    }
    private static double? Positive(double? value) => value > 0 ? value : null;
    private static double? Value(IHardware hardware, SensorType type, string name)
    {
        float? value = hardware.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name)?.Value;
        return value.HasValue && float.IsFinite(value.Value) ? value.Value : null;
    }
    public void Dispose() => _computer.Close();
}
