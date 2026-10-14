// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using VictusHub.Core.Hardware;
using VictusHub.Core.Telemetry;
using VictusHub.Core.Control;

var json = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
if (args is ["--dc-smoke", var reportPath])
{
    if (FirmwareController.OnAc) throw new InvalidOperationException("Unplug AC before the DC-only test.");
    string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath))!;
    Directory.CreateDirectory(directory);
    using var controller = new FirmwareController(Path.Combine(directory, "dc-recovery"));
    var before = controller.Status();
    var results = new List<object>();
    bool success = true;
    try
    {
        foreach (var profile in new[] { DeviceProfile.Default, DeviceProfile.Quiet, DeviceProfile.Performance, DeviceProfile.Eco, DeviceProfile.Default })
        {
            if (FirmwareController.OnAc) throw new InvalidOperationException("Power source changed during the DC test.");
            var response = controller.Apply(profile);
            results.Add(new { Action = profile.ToString(), Response = response });
            // Firmware itself can change GPU policy when accepting a profile; DC sends no 0x22 GPU writes.
            if (!response.Success || response.OnAc) throw new InvalidOperationException("Battery profile confirmation failed.");
            await Task.Delay(1000);
        }
        foreach (var fan in new[] { FanChoice.Max, FanChoice.Auto })
        {
            var response = controller.SetFan(fan);
            results.Add(new { Action = fan.ToString(), Response = response });
            if (!response.Success || response.ConfirmedFan != fan) throw new InvalidOperationException("Battery fan confirmation failed.");
            await Task.Delay(2500);
        }
    }
    catch (Exception error) { success = false; results.Add(new { Error = error.ToString() }); }
    finally
    {
        var restored = controller.RestoreDefault();
        success &= restored.Success && restored.ConfirmedFan == FanChoice.Auto;
        controller.Release();
        File.WriteAllText(Path.GetFullPath(reportPath), JsonSerializer.Serialize(new { Success = success, At = DateTimeOffset.Now, Before = before, Results = results, Restored = restored }, json));
    }
    return success ? 0 : 1;
}
if (args is ["--sensors", var sensorOutput])
{
    var computer = new LibreHardwareMonitor.Hardware.Computer { IsCpuEnabled = true, IsGpuEnabled = true };
    computer.Open();
    try
    {
    var readings = new List<object>();
    for (int i = 0; i < 10; i++)
    {
        foreach (var hardware in computer.Hardware)
        {
            hardware.Update();
            foreach (var child in hardware.SubHardware) child.Update();
            readings.Add(new { At = DateTimeOffset.Now, hardware.Name, Type = hardware.HardwareType.ToString(), Sensors = hardware.Sensors.Select(s => new { s.Name, Id = s.Identifier.ToString(), Type = s.SensorType.ToString(), s.Value }).ToArray() });
        }
        await Task.Delay(1000);
    }
    File.WriteAllText(Path.GetFullPath(sensorOutput), JsonSerializer.Serialize(new { Readings = readings, Report = computer.GetReport() }, json));
    }
    finally { computer.Close(); }
    return 0;
}
if (args is ["--control", var command])
{
    HubRequest request = command switch
    {
        "Status" => new(HubAction.Status),
        "Telemetry" => new(HubAction.Telemetry),
        "Release" => new(HubAction.Release),
        "RestoreDefault" => new(HubAction.RestoreDefault),
        "Auto" => new(HubAction.SetFan, Fan: FanChoice.Auto),
        "Max" => new(HubAction.SetFan, Fan: FanChoice.Max),
        _ when Enum.TryParse<DeviceProfile>(command, out var profile) && Enum.IsDefined(profile) => new(HubAction.ApplyProfile, profile),
        _ => throw new ArgumentException("Unknown fixed control action.")
    };
    try
    {
        var response = await HubProtocol.Send(request);
        Console.WriteLine(JsonSerializer.Serialize(response, json));
        return response.Success ? 0 : 1;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { Error = error.Message }, json));
        return 1;
    }
}
if (args is ["--desktop-state"])
{
    using var limiter = new NvidiaFrameLimiter();
    Console.WriteLine(JsonSerializer.Serialize(new { Display = DisplayRefresh.Read(), NvidiaFrameLimit = limiter.Read() }, json));
    return 0;
}
if (args is ["--telemetry"] || args is ["--telemetry-series"])
{
    var windows = new WindowsTelemetry();
    windows.ReadCpuUsage();
    await Task.Delay(1100);
    using var nvidia = new NvidiaTelemetry();
    int count = args[0] == "--telemetry-series" ? 15 : 1;
    for (int i = 0; i < count; i++)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { At = DateTimeOffset.Now, Memory = windows.ReadMemory(), CpuUsage = windows.ReadCpuUsage(), Gpu = nvidia.Read() }, json));
        if (i + 1 < count) await Task.Delay(2000);
    }
    return 0;
}
bool collect = args is ["--collect", _];
if (args is not ["--snapshot"] && !collect)
{
    Console.WriteLine("Victus Hub Probe — salt okunur tanılama\n--telemetry   Windows ve NVIDIA sensörlerini oku\n--snapshot    HP WMI BIOS yanıtlarını oku (yönetici gerekir)\n--collect PATH  15 dakika salt okunur BIOS kaydı");
    return args.Length == 0 ? 0 : 2;
}
using var identity = WindowsIdentity.GetCurrent();
if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.Error.WriteLine("HP BIOS WMI okuması için yönetici yetkisi gerekli. Hiçbir ayar değiştirilmedi.");
    return 3;
}
StreamWriter? log = null;
try
{
    if (collect)
    {
        string path = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        log = new StreamWriter(path, append: true) { AutoFlush = true };
    }
    var machine = MachineIdentity.Detect();
    using var bios = new HpBiosReader(machine.Board);
    if (collect)
    {
        var compact = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        for (int sample = 0; sample < 180; sample++)
        {
            log!.WriteLine(JsonSerializer.Serialize(new { Machine = machine, Snapshot = bios.Capture(machine.Board) }, compact));
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        return 0;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { Machine = machine, Snapshot = bios.Capture(machine.Board) }, json));
    return 0;
}
catch (Exception e)
{
    log?.WriteLine(JsonSerializer.Serialize(new { CapturedAt = DateTimeOffset.Now, Fatal = e.Message }));
    Console.Error.WriteLine(JsonSerializer.Serialize(new { Error = e.Message }, json));
    return 1;
}
finally { log?.Dispose(); }
