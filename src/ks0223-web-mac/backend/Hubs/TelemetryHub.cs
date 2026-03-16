using Ks0223.Web.Backend.Services;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace Ks0223.Web.Backend.Hubs;

public sealed class TelemetryHub : Hub
{
    private static readonly ConcurrentDictionary<string, string> ConnectionBindings = new(StringComparer.Ordinal);
    private readonly RuntimeSessionManager runtimeSessionManager;

    public TelemetryHub(RuntimeSessionManager runtimeSessionManager)
    {
        this.runtimeSessionManager = runtimeSessionManager;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public async Task BindClient(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new HubException("clientId is required");
        }

        var normalizedClientId = clientId.Trim();
        var group = RuntimeSessionManager.GetClientGroup(normalizedClientId);

        if (ConnectionBindings.TryGetValue(Context.ConnectionId, out var previousClientId) &&
            !string.Equals(previousClientId, normalizedClientId, StringComparison.Ordinal))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RuntimeSessionManager.GetClientGroup(previousClientId));
            await runtimeSessionManager.UnregisterBoundClientAsync(previousClientId, Context.ConnectionId);
        }

        ConnectionBindings[Context.ConnectionId] = normalizedClientId;
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await runtimeSessionManager.RegisterBoundClientAsync(normalizedClientId, Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectionBindings.TryRemove(Context.ConnectionId, out var clientId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RuntimeSessionManager.GetClientGroup(clientId));
            await runtimeSessionManager.UnregisterBoundClientAsync(clientId, Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
