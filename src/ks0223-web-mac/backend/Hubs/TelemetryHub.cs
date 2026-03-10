using Ks0223.Web.Backend.Services;
using Microsoft.AspNetCore.SignalR;

namespace Ks0223.Web.Backend.Hubs;

public sealed class TelemetryHub : Hub
{
    private readonly PiTcpClientService piTcpClientService;

    public TelemetryHub(PiTcpClientService piTcpClientService)
    {
        this.piTcpClientService = piTcpClientService;
    }

    public override async Task OnConnectedAsync()
    {
        await piTcpClientService.RegisterUiConnectionAsync(Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await piTcpClientService.UnregisterUiConnectionAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
