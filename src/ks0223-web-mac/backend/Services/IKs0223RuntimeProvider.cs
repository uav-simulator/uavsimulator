using Ks0223.Web.Backend.Models;

namespace Ks0223.Web.Backend.Services;

public interface IKs0223RuntimeProvider
{
    string Mode { get; }
    StatusDto GetStatus();
    ConnectionTargetDto GetConnectionTarget();
    Task ConnectAsync(string? host, int? port, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task<CommandResponse> SendCommandAsync(string command, string source, string? agentId, string? clientId, CancellationToken cancellationToken);
    Task RegisterUiConnectionAsync(string connectionId);
    Task UnregisterUiConnectionAsync(string connectionId);
    Task UpdateLatencyAsync(long? latency);
    Task BroadcastStatusAsync(CancellationToken cancellationToken = default);
}
