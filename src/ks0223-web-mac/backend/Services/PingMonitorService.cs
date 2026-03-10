using System.Net.NetworkInformation;

namespace Ks0223.Web.Backend.Services;

public sealed class PingMonitorService : BackgroundService
{
    private readonly RuntimeControlService runtimeControlService;

    public PingMonitorService(RuntimeControlService runtimeControlService)
    {
        this.runtimeControlService = runtimeControlService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var ping = new Ping();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var target = runtimeControlService.GetConnectionTarget();
                var reply = await ping.SendPingAsync(target.Host, 1000);
                var latency = reply.Status == IPStatus.Success ? reply.RoundtripTime : (long?)null;
                await runtimeControlService.UpdateLatencyAsync(latency);
            }
            catch
            {
                await runtimeControlService.UpdateLatencyAsync(null);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
