using Ks0223.Web.Backend.Models;

namespace Ks0223.Web.Backend.Services;

public sealed class RuntimeControlService
{
    private readonly object stateLock = new();
    private readonly IReadOnlyDictionary<string, IKs0223RuntimeProvider> providers;
    private string currentMode = RuntimeModes.RealRobot;

    public RuntimeControlService(IEnumerable<IKs0223RuntimeProvider> providers)
    {
        this.providers = providers.ToDictionary(provider => provider.Mode, StringComparer.OrdinalIgnoreCase);
    }

    public string GetCurrentMode()
    {
        lock (stateLock)
        {
            return currentMode;
        }
    }

    public StatusDto GetStatus()
    {
        var provider = GetCurrentProvider();
        return provider.GetStatus() with { RuntimeMode = provider.Mode };
    }

    public ConnectionTargetDto GetConnectionTarget()
    {
        var provider = GetCurrentProvider();
        return provider.GetConnectionTarget() with { RuntimeMode = provider.Mode };
    }

    public async Task ConnectAsync(string? mode, string? host, int? port, CancellationToken cancellationToken)
    {
        var normalizedMode = RuntimeModes.Normalize(mode);
        var provider = ResolveProvider(normalizedMode);

        if (!string.Equals(normalizedMode, GetCurrentMode(), StringComparison.OrdinalIgnoreCase))
        {
            await GetCurrentProvider().DisconnectAsync(cancellationToken);
            lock (stateLock)
            {
                currentMode = normalizedMode;
            }
        }

        await provider.ConnectAsync(host, port, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        GetCurrentProvider().DisconnectAsync(cancellationToken);

    public Task<CommandResponse> SendCommandAsync(string command, string source, string? agentId, CancellationToken cancellationToken) =>
        GetCurrentProvider().SendCommandAsync(command, source, agentId, cancellationToken);

    public Task RegisterUiConnectionAsync(string connectionId) =>
        GetCurrentProvider().RegisterUiConnectionAsync(connectionId);

    public Task UnregisterUiConnectionAsync(string connectionId) =>
        GetCurrentProvider().UnregisterUiConnectionAsync(connectionId);

    public Task UpdateLatencyAsync(long? latency) =>
        GetCurrentProvider().UpdateLatencyAsync(latency);

    public Task BroadcastStatusAsync(CancellationToken cancellationToken = default) =>
        GetCurrentProvider().BroadcastStatusAsync(cancellationToken);

    private IKs0223RuntimeProvider GetCurrentProvider() => ResolveProvider(GetCurrentMode());

    private IKs0223RuntimeProvider ResolveProvider(string mode)
    {
        if (providers.TryGetValue(mode, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException($"Runtime provider '{mode}' is not registered.");
    }
}
