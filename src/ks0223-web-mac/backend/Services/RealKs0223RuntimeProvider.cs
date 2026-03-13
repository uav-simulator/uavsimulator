using Ks0223.Web.Backend.Models;

namespace Ks0223.Web.Backend.Services;

public sealed class RealKs0223RuntimeProvider : IKs0223RuntimeProvider
{
    private const string RuntimeLabel = "Keyestudio KS0223 (Real Robot)";
    private readonly PiTcpClientService piTcpClientService;

    public RealKs0223RuntimeProvider(PiTcpClientService piTcpClientService)
    {
        this.piTcpClientService = piTcpClientService;
    }

    public string Mode => RuntimeModes.RealRobot;

    public StatusDto GetStatus() => piTcpClientService.GetStatus() with { RuntimeMode = Mode, RuntimeLabel = RuntimeLabel };

    public ConnectionTargetDto GetConnectionTarget() => piTcpClientService.GetConnectionTarget() with { RuntimeMode = Mode };

    public Task ConnectAsync(string? host, int? port, CancellationToken cancellationToken) =>
        piTcpClientService.ConnectAsync(host, port, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        piTcpClientService.DisconnectAsync(cancellationToken);

    public Task<CommandResponse> SendCommandAsync(string command, string source, string? agentId, CancellationToken cancellationToken) =>
        piTcpClientService.SendCommandAsync(command, source, cancellationToken);

    public Task RegisterUiConnectionAsync(string connectionId) =>
        piTcpClientService.RegisterUiConnectionAsync(connectionId);

    public Task UnregisterUiConnectionAsync(string connectionId) =>
        piTcpClientService.UnregisterUiConnectionAsync(connectionId);

    public Task UpdateLatencyAsync(long? latency) =>
        piTcpClientService.UpdateLatencyAsync(latency);

    public Task BroadcastStatusAsync(CancellationToken cancellationToken = default) =>
        piTcpClientService.BroadcastStatusAsync(cancellationToken);
}
