using System.Net.Sockets;
using System.Text;
using Ks0223.Web.Backend.Hubs;
using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Ks0223.Web.Backend.Services;

public sealed class PiTcpClientService : BackgroundService
{
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private readonly object stateLock = new();
    private readonly IHubContext<TelemetryHub> hubContext;
    private readonly SessionLogger sessionLogger;
    private readonly TelemetryParser telemetryParser;
    private readonly PiConnectionOptions options;
    private readonly ILogger<PiTcpClientService> logger;
    private readonly string? clientGroup;
    private readonly string? sessionClientId;
    private readonly string sessionRuntimeMode;

    private TcpClient? client;
    private NetworkStream? stream;
    private CancellationTokenSource? receiveCts;
    private Task? receiveLoopTask;
    private bool desiredConnection;
    private bool tcpConnected;
    private int uiConnectedClients;
    private string targetHost;
    private int targetPort;
    private double? latencyMs;
    private string? lastError;
    private DateTimeOffset? lastTcpMessageAt;
    private bool hasParsedTelemetry;

    public PiTcpClientService(
        IOptions<PiConnectionOptions> options,
        IHubContext<TelemetryHub> hubContext,
        SessionLogger sessionLogger,
        TelemetryParser telemetryParser,
        ILogger<PiTcpClientService> logger,
        string? clientGroup = null,
        string? sessionClientId = null,
        string sessionRuntimeMode = RuntimeModes.RealRobot)
    {
        this.options = options.Value;
        this.hubContext = hubContext;
        this.sessionLogger = sessionLogger;
        this.telemetryParser = telemetryParser;
        this.logger = logger;
        this.clientGroup = string.IsNullOrWhiteSpace(clientGroup) ? null : clientGroup.Trim();
        this.sessionClientId = string.IsNullOrWhiteSpace(sessionClientId) ? null : sessionClientId.Trim();
        this.sessionRuntimeMode = string.IsNullOrWhiteSpace(sessionRuntimeMode)
            ? RuntimeModes.RealRobot
            : RuntimeModes.Normalize(sessionRuntimeMode);
        targetHost = this.options.Host;
        targetPort = this.options.Port;
    }

    public StatusDto GetStatus()
    {
        var logState = sessionLogger.GetState();
        lock (stateLock)
        {
            return new StatusDto(
                DesiredConnection: desiredConnection,
                TcpConnected: tcpConnected,
                UiConnectedClients: uiConnectedClients,
                TargetHost: targetHost,
                TargetPort: targetPort,
                LatencyMs: latencyMs,
                LastError: lastError,
                LastTcpMessageAt: lastTcpMessageAt,
                IsLogging: logState.IsLogging,
                CurrentLogFile: logState.CurrentFile,
                HasParsedTelemetry: hasParsedTelemetry);
        }
    }

    public async Task ConnectAsync(string? host, int? port, CancellationToken cancellationToken)
    {
        var endpointChanged = false;
        if (host is not null || port.HasValue)
        {
            endpointChanged = TrySetTargetEndpoint(host, port, out var error);
            if (error is not null)
            {
                throw new ArgumentException(error);
            }
        }

        lock (stateLock)
        {
            desiredConnection = true;
            lastError = null;
        }

        if (endpointChanged && IsTcpConnected())
        {
            await TrySendStopBestEffortAsync("endpoint-changed", cancellationToken);
            await CloseConnectionAsync(cancellationToken);
        }

        var target = GetTargetEndpointSnapshot();
        await sessionLogger.WriteAsync(
            "connection.desired",
            new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, desired = true, target.Host, target.Port },
            cancellationToken);
        await BroadcastStatusAsync(cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            desiredConnection = false;
        }

        await TrySendStopBestEffortAsync("manual-disconnect", cancellationToken);
        await CloseConnectionAsync(cancellationToken);
        await sessionLogger.WriteAsync(
            "connection.desired",
            new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, desired = false },
            cancellationToken);
        await BroadcastStatusAsync(cancellationToken);
    }

    public async Task<CommandResponse> SendCommandAsync(string command, string source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new CommandResponse(false, "Command is empty");
        }

        await sendLock.WaitAsync(cancellationToken);
        try
        {
            NetworkStream? activeStream;
            lock (stateLock)
            {
                activeStream = stream;
            }

            if (activeStream is null || !activeStream.CanWrite)
            {
                lock (stateLock)
                {
                    lastError = "TCP connection is not active";
                }
                return new CommandResponse(false, "TCP connection is not active");
            }

            var payload = Encoding.UTF8.GetBytes(command);
            await activeStream.WriteAsync(payload.AsMemory(), cancellationToken);
            await activeStream.FlushAsync(cancellationToken);

            await sessionLogger.WriteAsync(
                "command.outgoing",
                new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, command, source, bytes = payload.Length },
                cancellationToken);
            return new CommandResponse(true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send command {Command}", command);
            lock (stateLock)
            {
                lastError = $"Send failed: {ex.Message}";
            }

            return new CommandResponse(false, ex.Message);
        }
        finally
        {
            sendLock.Release();
        }
    }

    public async Task RegisterUiConnectionAsync(string connectionId)
    {
        lock (stateLock)
        {
            uiConnectedClients++;
            lastError = null;
        }

        await sessionLogger.WriteAsync(
            "ui.connected",
            new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, connectionId, uiConnectedClients = uiConnectedClients });
        await BroadcastStatusAsync();
    }

    public async Task UnregisterUiConnectionAsync(string connectionId)
    {
        var mustStop = false;
        lock (stateLock)
        {
            uiConnectedClients = Math.Max(0, uiConnectedClients - 1);
            mustStop = uiConnectedClients == 0;
        }

        await sessionLogger.WriteAsync(
            "ui.disconnected",
            new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, connectionId, uiConnectedClients = uiConnectedClients });

        if (mustStop)
        {
            await TrySendStopBestEffortAsync("last-ui-disconnected", CancellationToken.None);
        }

        await BroadcastStatusAsync();
    }

    public async Task UpdateLatencyAsync(long? latency)
    {
        lock (stateLock)
        {
            latencyMs = latency;
        }

        await BroadcastStatusAsync();
    }

    public ConnectionTargetDto GetConnectionTarget()
    {
        var target = GetTargetEndpointSnapshot();
        return new ConnectionTargetDto(target.Host, target.Port);
    }

    public async Task BroadcastStatusAsync(CancellationToken cancellationToken = default)
    {
        await ResolveHubClients().SendAsync("status", GetStatus(), cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var shouldConnect = GetDesiredConnection();
                if (shouldConnect)
                {
                    if (!IsTcpConnected())
                    {
                        await TryConnectAsync(stoppingToken);
                    }
                    else if (receiveLoopTask is { IsCompleted: true })
                    {
                        await HandleReceiveLoopCompletionAsync(stoppingToken);
                    }
                }
                else if (IsTcpConnected())
                {
                    await CloseConnectionAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected TCP service error");
                lock (stateLock)
                {
                    lastError = ex.Message;
                }
            }

            await BroadcastStatusAsync(stoppingToken);
            await Task.Delay(options.ReconnectDelayMs, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await TrySendStopBestEffortAsync("app-shutdown", cancellationToken);
        await CloseConnectionAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private bool GetDesiredConnection()
    {
        lock (stateLock)
        {
            return desiredConnection;
        }
    }

    private bool IsTcpConnected()
    {
        lock (stateLock)
        {
            return tcpConnected;
        }
    }

    private async Task TryConnectAsync(CancellationToken cancellationToken)
    {
        await connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsTcpConnected() || !GetDesiredConnection())
            {
                return;
            }

            var tcpClient = new TcpClient
            {
                NoDelay = true,
            };
            var target = GetTargetEndpointSnapshot();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            await tcpClient.ConnectAsync(target.Host, target.Port, timeout.Token);

            var networkStream = tcpClient.GetStream();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var loopTask = RunReceiveLoopAsync(networkStream, linkedCts.Token);

            lock (stateLock)
            {
                client = tcpClient;
                stream = networkStream;
                receiveCts = linkedCts;
                receiveLoopTask = loopTask;
                tcpConnected = true;
                lastError = null;
            }

            await sessionLogger.WriteAsync(
                "tcp.connected",
                new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, target.Host, target.Port },
                cancellationToken);
            logger.LogInformation("Connected to Pi TCP endpoint {Host}:{Port}", target.Host, target.Port);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var target = GetTargetEndpointSnapshot();
            var error = $"Connect timeout (6s) to {target.Host}:{target.Port}";
            lock (stateLock)
            {
                lastError = error;
            }

            await sessionLogger.WriteAsync(
                "tcp.connect_failed",
                new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, target.Host, target.Port, error },
                cancellationToken);
            logger.LogWarning("Connect timeout to Pi {Host}:{Port}", target.Host, target.Port);
        }
        catch (Exception ex)
        {
            var target = GetTargetEndpointSnapshot();
            lock (stateLock)
            {
                lastError = $"Connect failed: {ex.Message}";
            }

            await sessionLogger.WriteAsync(
                "tcp.connect_failed",
                new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, target.Host, target.Port, error = ex.Message },
                cancellationToken);
            logger.LogWarning(ex, "Connect failed to Pi {Host}:{Port}", target.Host, target.Port);
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private async Task RunReceiveLoopAsync(NetworkStream activeStream, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Max(256, options.ReceiveBufferSize)];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await activeStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead == 0)
                {
                    throw new IOException("TCP stream closed by remote host");
                }

                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var parsed = telemetryParser.TryParse(text);

                lock (stateLock)
                {
                    lastTcpMessageAt = DateTimeOffset.UtcNow;
                    if (parsed is not null)
                    {
                        hasParsedTelemetry = true;
                    }
                }

                var dto = new IncomingMessageDto(DateTimeOffset.UtcNow, text, parsed);
                await sessionLogger.WriteAsync(
                    "tcp.incoming",
                    new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, message = text, bytes = bytesRead, parsedTelemetry = parsed },
                    cancellationToken);
                await ResolveHubClients().SendAsync("incoming", dto, cancellationToken);
                await BroadcastStatusAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore cancellation during shutdown/reconnect
        }
        catch (Exception ex)
        {
            lock (stateLock)
            {
                lastError = $"TCP receive failed: {ex.Message}";
            }

            await sessionLogger.WriteAsync(
                "tcp.receive_failed",
                new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, error = ex.Message },
                cancellationToken);
            logger.LogWarning(ex, "TCP receive loop stopped unexpectedly");
        }
    }

    private async Task HandleReceiveLoopCompletionAsync(CancellationToken cancellationToken)
    {
        await TrySendStopBestEffortAsync("tcp-drop", cancellationToken);
        await CloseConnectionAsync(cancellationToken);
    }

    private async Task CloseConnectionAsync(CancellationToken cancellationToken)
    {
        await connectionLock.WaitAsync(cancellationToken);
        try
        {
            CancellationTokenSource? localCts;
            TcpClient? localClient;

            lock (stateLock)
            {
                localCts = receiveCts;
                localClient = client;
                receiveCts = null;
                client = null;
                stream = null;
                receiveLoopTask = null;
                tcpConnected = false;
            }

            if (localCts is not null)
            {
                await localCts.CancelAsync();
                localCts.Dispose();
            }

            localClient?.Close();
            await sessionLogger.WriteAsync(
                "tcp.disconnected",
                new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, reason = "connection-closed" },
                cancellationToken);
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private async Task TrySendStopBestEffortAsync(string reason, CancellationToken cancellationToken)
    {
        if (!IsTcpConnected())
        {
            await sessionLogger.WriteAsync(
                "failsafe.stop_attempt",
                new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, reason, sent = false, error = "skipped:not-connected" },
                cancellationToken);
            return;
        }

        var result = await SendCommandAsync("DirStop", $"failsafe:{reason}", cancellationToken);
        await sessionLogger.WriteAsync(
            "failsafe.stop_attempt",
            new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, reason, sent = result.Sent, error = result.Error },
            cancellationToken);
    }

    private (string Host, int Port) GetTargetEndpointSnapshot()
    {
        lock (stateLock)
        {
            return (targetHost, targetPort);
        }
    }

    private bool TrySetTargetEndpoint(string? host, int? port, out string? error)
    {
        lock (stateLock)
        {
            var nextHost = host is null ? targetHost : host.Trim();
            var nextPort = port ?? targetPort;

            if (string.IsNullOrWhiteSpace(nextHost))
            {
                error = "Host must not be empty.";
                return false;
            }

            if (!IsValidHost(nextHost))
            {
                error = "Host is invalid. Use IPv4/IPv6 or DNS name.";
                return false;
            }

            if (nextPort is < 1 or > 65535)
            {
                error = "Port must be in range 1..65535.";
                return false;
            }

            var changed = !string.Equals(nextHost, targetHost, StringComparison.OrdinalIgnoreCase) || nextPort != targetPort;
            if (changed)
            {
                targetHost = nextHost;
                targetPort = nextPort;
            }

            error = null;
            return changed;
        }
    }

    private static bool IsValidHost(string host)
    {
        if (Uri.CheckHostName(host) != UriHostNameType.Unknown)
        {
            return true;
        }

        return false;
    }

    private IClientProxy ResolveHubClients() =>
        string.IsNullOrWhiteSpace(clientGroup)
            ? hubContext.Clients.All
            : hubContext.Clients.Group(clientGroup);
}
