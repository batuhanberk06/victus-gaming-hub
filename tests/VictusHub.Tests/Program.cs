// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using System.Text.Json;
using VictusHub.Core.Control;
using VictusHub.Core.Hardware;
using VictusHub.Core.Telemetry;

int passed = 0;
void Assert(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS " + name); }
Assert(NvidiaFrameLimiter.EcoLimit(0) == 60, "Eco caps an unlimited GPU at60FPS");
Assert(NvidiaFrameLimiter.EcoLimit(144) == 60, "Eco reduces a higher existing cap");
Assert(NvidiaFrameLimiter.EcoLimit(30) == 30, "Eco preserves a lower existing user cap");
Assert(NvidiaFrameLimiter.EcoLimit(60) == 60, "Eco preserves an existing60FPS cap");
Assert(WindowsTelemetry.CalculateCpuUsage(new(20, 100, 100), new(70, 150, 150)) == 50, "CPU kernel time includes idle; correct busy delta");
Assert(WindowsTelemetry.CalculateCpuUsage(new(20, 100, 100), new(20, 100, 100)) is null, "Zero sampling interval is unavailable");
Assert(WindowsTelemetry.CalculateCpuUsage(new(20, 100, 100), new(10, 150, 150)) is null, "Reset/wrapped counters are unavailable");
Assert(WindowsTelemetry.CalculateCpuUsage(new(20, 100, 100), new(200, 150, 150)) is null, "Impossible idle delta is unavailable");
Assert(!HpBiosReader.ValidateReply(BiosReadOperation.SystemDesign, 0, [0, 0]).Success, "Truncated capability data cannot enable control");
Assert(!HpBiosReader.ValidateReply(BiosReadOperation.MaxFan, 3, [1, 0, 0, 0]).Success, "BIOS error never becomes Max confirmation");
Assert(!HpBiosReader.ValidateReply(BiosReadOperation.GpuPowerPolicy, 0, [0, 1]).Success, "GPU policy needs all four bytes");
Assert(HpBiosReader.ValidateReply(BiosReadOperation.ThermalSensor, 0, [0, 0, 0, 0]).Success, "Zero thermal value retained as raw protocol data, not CPU temperature");
try { HpBiosReader.Describe((BiosReadOperation)999); Assert(false, "Unknown command rejection"); } catch (ArgumentOutOfRangeException) { Assert(true, "Unknown command rejection"); }
foreach (uint write in new uint[] { 0x1A, 0x22, 0x27, 0x2E, 0x29 })
    Assert(Enum.GetValues<BiosReadOperation>().All(read => HpBiosReader.Describe(read).CommandType != write), $"Read-only collector excludes setting 0x{write:X2}");
try { JsonSerializer.Deserialize<HubRequest>("{\"Action\":17}", HubProtocol.Json); Assert(false, "Numeric IPC command rejected"); } catch (JsonException) { Assert(true, "Numeric IPC command rejected"); }
try { JsonSerializer.Deserialize<HubRequest>("{\"Action\":\"RawWrite\"}", HubProtocol.Json); Assert(false, "Arbitrary IPC command rejected"); } catch (JsonException) { Assert(true, "Arbitrary IPC command rejected"); }
using (var input = new MemoryStream(Encoding.UTF8.GetBytes("hello\n"))) Assert(await HubProtocol.ReadLine(input, 5, default) == "hello", "IPC framing accepts bounded line");
try { using var input = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 514) + "\n")); await HubProtocol.ReadLine(input, 512, default); Assert(false, "Oversized IPC request rejected"); } catch (IOException) { Assert(true, "Oversized IPC request rejected"); }
try { using var input = new MemoryStream(Encoding.UTF8.GetBytes("partial")); await HubProtocol.ReadLine(input, 512, default); Assert(false, "Truncated IPC frame rejected"); } catch (IOException) { Assert(true, "Truncated IPC frame rejected"); }
Console.WriteLine($"{passed} checks passed. No firmware writes executed.");
