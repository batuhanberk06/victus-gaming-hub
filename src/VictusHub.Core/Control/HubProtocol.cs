// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VictusHub.Core.Hardware;

namespace VictusHub.Core.Control;

public enum HubAction { Status, ApplyProfile, SetFan, Release, RestoreDefault, Telemetry, PauseTelemetry }
public sealed record HubRequest(HubAction Action, DeviceProfile? Profile = null, FanChoice? Fan = null);
public static class HubProtocol
{
    public const string PipeName = "VictusHub.Control.v1";
    public static JsonSerializerOptions Json { get; } = new() { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    public static async Task<string> ReadLine(Stream stream, int limit, CancellationToken token)
    {
        byte[] buffer = new byte[1];
        using var data = new MemoryStream();
        while (data.Length <= limit)
        {
            int count = await stream.ReadAsync(buffer, token);
            if (count == 0) throw new IOException("Kontrol bağlantısı kapandı.");
            if (buffer[0] == (byte)'\n') return Encoding.UTF8.GetString(data.ToArray());
            data.WriteByte(buffer[0]);
        }
        throw new IOException("Kontrol mesajı çok uzun.");
    }
    public static async Task<ControlStatus> Send(HubRequest request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(2500, timeout.Token);
        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner?.IsWellKnown(WellKnownSidType.LocalSystemSid) != true && owner?.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) != true)
            throw new UnauthorizedAccessException("Kontrol sunucusu sistem hizmetine ait değil.");
        byte[] message = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json) + "\n");
        await pipe.WriteAsync(message, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
        var response = await ReadLine(pipe, 8192, timeout.Token);
        return JsonSerializer.Deserialize<ControlStatus>(response, Json) ?? throw new IOException("Kontrol yanıtı boş.");
    }
}
