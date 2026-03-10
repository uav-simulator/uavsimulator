using Ks0223.Web.Backend.Services;
using Microsoft.AspNetCore.SignalR;

namespace Ks0223.Web.Backend.Hubs;

public sealed class TelemetryHub : Hub
{
    private readonly RuntimeControlService runtimeControlService;

    public TelemetryHub(RuntimeControlService runtimeControlService)
    {
        this.runtimeControlService = runtimeControlService;
    }

    public override async Task OnConnectedAsync()
    {
        await runtimeControlService.RegisterUiConnectionAsync(Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await runtimeControlService.UnregisterUiConnectionAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
