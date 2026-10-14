// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using VictusHub.Core.Control;
using VictusHub.Core.Hardware;
using VictusHub.Core.Telemetry;

namespace VictusHub.Service;

public sealed class ControlService : ServiceBase
{
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private FirmwareController? _controller;
    private HardwareSensors? _sensors;
    private readonly string _stateDirectory = Path.Combine(AppContext.BaseDirectory, "state");
    private SecurityIdentifier? _allowedUser;

    public ControlService() { ServiceName = "VictusHub.Control"; AutoLog = false; CanShutdown = true; }
    protected override void OnStart(string[] args)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "service.json");
        var config = JsonSerializer.Deserialize<ServiceConfig>(File.ReadAllText(configPath)) ?? throw new InvalidOperationException("Hizmet yapılandırması eksik.");
        _allowedUser = new SecurityIdentifier(config.AllowedUserSid);
        if (!_allowedUser.IsAccountSid()) throw new InvalidOperationException("Geçersiz kullanıcı SID.");
        Directory.CreateDirectory(_stateDirectory);
        _controller = new FirmwareController(_stateDirectory);
        _stop = new CancellationTokenSource();
        _worker = Task.Run(() => Serve(_stop.Token));
        Log("Hizmet başladı; profil uygulanmadı.");
    }
    private async Task Serve(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var security = new PipeSecurity();
                security.SetAccessRuleProtection(true, false);
                security.SetOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.ReadWrite | PipeAccessRights.ReadPermissions | PipeAccessRights.Synchronize, AccessControlType.Deny));
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(_allowedUser!, PipeAccessRights.ReadWrite | PipeAccessRights.ReadPermissions | PipeAccessRights.Synchronize, AccessControlType.Allow));
                using var pipe = NamedPipeServerStreamAcl.Create(HubProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 8192, security);
                await pipe.WaitForConnectionAsync(token);
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                readTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                string line = await HubProtocol.ReadLine(pipe, 512, readTimeout.Token);
                // Windows authenticates access using the protected pipe DACL above:
                // only the configured account and LocalSystem can read/write; network logons are denied.
                // Do not impersonate the UI user while accessing firmware or protected recovery files.
                ControlStatus response;
                try
                {
                    var request = JsonSerializer.Deserialize<HubRequest>(line, HubProtocol.Json) ?? throw new JsonException("Boş istek.");
                    response = request.Action switch
                    {
                        HubAction.Status => _controller!.Status(),
                        HubAction.Telemetry => ReadTelemetry(),
                        HubAction.PauseTelemetry => PauseTelemetry(),
                        HubAction.ApplyProfile when request.Profile is { } profile => _controller!.Apply(profile),
                        HubAction.SetFan when request.Fan is { } fan => _controller!.SetFan(fan),
                        HubAction.RestoreDefault => _controller!.RestoreDefault(),
                        HubAction.Release => Release(),
                        _ => throw new JsonException("Bilinmeyen veya eksik kontrol isteği.")
                    };
                    if (request.Action is not (HubAction.Status or HubAction.Telemetry or HubAction.PauseTelemetry)) Log($"{request.Action}: {response.Success} {response.Message}");
                }
                catch (Exception error) { response = new(false, error.Message, null, null, FirmwareController.OnAc); }
                byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, HubProtocol.Json) + "\n");
                await pipe.WriteAsync(data, token);
                await pipe.FlushAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) { Log(error.ToString()); await Task.Delay(500, token).ConfigureAwait(false); }
        }
    }
    private ControlStatus Release() { _controller!.Release(); return _controller.Status() with { Message = "Fan Auto'ya devredildi; uygulamanın FPS sınırı geri yüklendi." }; }
    private ControlStatus ReadTelemetry()
    {
        _sensors ??= new HardwareSensors();
        return new(true, "LibreHardwareMonitor", null, null, FirmwareController.OnAc, Sensors: _sensors.Read());
    }
    private ControlStatus PauseTelemetry()
    {
        _sensors?.Dispose();
        _sensors = null;
        return new(true, "Sensör okumaları durduruldu.", null, null, FirmwareController.OnAc);
    }
    protected override void OnStop()
    {
        RequestAdditionalTime(30000);
        _stop?.Cancel();
        try { _worker?.Wait(TimeSpan.FromSeconds(25)); } catch (AggregateException) { }
        try
        {
            string uninstall = Path.Combine(_stateDirectory, "uninstall.request");
            if (File.Exists(uninstall))
            {
                var result = _controller!.RestoreDefault();
                if (!result.Success) throw new InvalidOperationException(result.Message);
                _controller.Release();
                File.Delete(uninstall);
            }
            else _controller?.Release();
        }
        catch (Exception error) { Log("Çıkış geri yüklemesi: " + error); }
        _controller?.Dispose();
        _sensors?.Dispose();
        _stop?.Dispose();
        Log("Hizmet durdu.");
    }
    protected override void OnShutdown() => OnStop();
    private void Log(string message)
    {
        try
        {
            string path = Path.Combine(_stateDirectory, "control.log");
            if (File.Exists(path) && new FileInfo(path).Length > 512000) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:o} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }
    private sealed record ServiceConfig(string AllowedUserSid);
}
