using System.Net.NetworkInformation;

namespace Ks0223.Web.Backend.Services;

public sealed class PingMonitorService : BackgroundService
{
    private readonly PiTcpClientService piTcpClientService;

    public PingMonitorService(PiTcpClientService piTcpClientService)
    {
        this.piTcpClientService = piTcpClientService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var ping = new Ping();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var target = piTcpClientService.GetConnectionTarget();
                var reply = await ping.SendPingAsync(target.Host, 1000);
                var latency = reply.Status == IPStatus.Success ? reply.RoundtripTime : (long?)null;
                await piTcpClientService.UpdateLatencyAsync(latency);
            }
            catch
            {
                await piTcpClientService.UpdateLatencyAsync(null);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
