using Ks0223.Web.Backend.Models;

namespace Ks0223.Web.Backend.Services;

public sealed class UnityKs0223RuntimeProvider : IKs0223RuntimeProvider
{
    private readonly object stateLock = new();
    private readonly SessionLogger sessionLogger;

    private bool desiredConnection;
    private int uiConnectedClients;
    private string targetHost = "127.0.0.1";
    private int targetPort = 8000;
    private double? latencyMs;
    private string? lastError;

    public UnityKs0223RuntimeProvider(SessionLogger sessionLogger)
    {
        this.sessionLogger = sessionLogger;
    }

    public string Mode => RuntimeModes.UnitySim;

    public StatusDto GetStatus()
    {
        var logState = sessionLogger.GetState();
        lock (stateLock)
        {
            return new StatusDto(
                DesiredConnection: desiredConnection,
                TcpConnected: false,
                UiConnectedClients: uiConnectedClients,
                TargetHost: targetHost,
                TargetPort: targetPort,
                LatencyMs: latencyMs,
                LastError: lastError,
                LastTcpMessageAt: null,
                IsLogging: logState.IsLogging,
                CurrentLogFile: logState.CurrentFile,
                HasParsedTelemetry: false,
                RuntimeMode: Mode);
        }
    }

    public ConnectionTargetDto GetConnectionTarget()
    {
        lock (stateLock)
        {
            return new ConnectionTargetDto(targetHost, targetPort, Mode);
        }
    }

    public Task ConnectAsync(string? host, int? port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (stateLock)
        {
            if (!string.IsNullOrWhiteSpace(host))
            {
                targetHost = host.Trim();
            }

            if (port is >= 1 and <= 65535)
            {
                targetPort = port.Value;
            }

            desiredConnection = false;
            lastError = "Unity runtime provider is not implemented yet";
        }

        throw new NotSupportedException("Unity runtime provider is not implemented yet");
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (stateLock)
        {
            desiredConnection = false;
        }

        return Task.CompletedTask;
    }

    public Task<CommandResponse> SendCommandAsync(string command, string source, CancellationToken cancellationToken)
    {
        _ = command;
        _ = source;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandResponse(false, "Unity runtime provider is not implemented yet"));
    }

    public Task RegisterUiConnectionAsync(string connectionId)
    {
        _ = connectionId;
        lock (stateLock)
        {
            uiConnectedClients++;
        }

        return Task.CompletedTask;
    }

    public Task UnregisterUiConnectionAsync(string connectionId)
    {
        _ = connectionId;
        lock (stateLock)
        {
            uiConnectedClients = Math.Max(0, uiConnectedClients - 1);
        }

        return Task.CompletedTask;
    }

    public Task UpdateLatencyAsync(long? latency)
    {
        lock (stateLock)
        {
            latencyMs = latency;
        }

        return Task.CompletedTask;
    }

    public Task BroadcastStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
