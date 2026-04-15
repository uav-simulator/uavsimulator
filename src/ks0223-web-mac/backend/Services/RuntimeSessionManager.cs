using System.Collections.Concurrent;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using Ks0223.Web.Backend.Hubs;
using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Ks0223.Web.Backend.Services;

public sealed class RuntimeSessionManager : IHostedService
{
    private readonly ConcurrentDictionary<SessionKey, RealRuntimeSession> realSessions = new();
    private readonly ConcurrentDictionary<UnityWorldKey, UnityWorldSession> unityWorlds = new();
    private readonly ConcurrentDictionary<string, UnityClientBinding> unityClientBindings = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, int> boundClientCounts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> connectionToClient = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> clientConnections = new(StringComparer.Ordinal);

    private readonly SessionLogger sessionLogger;
    private readonly TelemetryParser telemetryParser;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IHubContext<TelemetryHub> hubContext;
    private readonly ILoggerFactory loggerFactory;
    private readonly PiConnectionOptions piOptions;
    private readonly CameraOptions cameraOptions;
    private readonly SensorBridgeOptions sensorOptions;

    public RuntimeSessionManager(
        SessionLogger sessionLogger,
        TelemetryParser telemetryParser,
        IHttpClientFactory httpClientFactory,
        IHubContext<TelemetryHub> hubContext,
        ILoggerFactory loggerFactory,
        IOptions<PiConnectionOptions> piOptions,
        IOptions<CameraOptions> cameraOptions,
        IOptions<SensorBridgeOptions> sensorOptions)
    {
        this.sessionLogger = sessionLogger;
        this.telemetryParser = telemetryParser;
        this.httpClientFactory = httpClientFactory;
        this.hubContext = hubContext;
        this.loggerFactory = loggerFactory;
        this.piOptions = Clone(piOptions.Value);
        this.cameraOptions = Clone(cameraOptions.Value);
        this.sensorOptions = Clone(sensorOptions.Value);
    }

    public static string GetClientGroup(string clientId) => $"client:{clientId}";

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var pair in realSessions)
        {
            try
            {
                await pair.Value.DisposeAsync();
            }
            catch
            {
                // best effort shutdown
            }
        }

        foreach (var pair in unityWorlds)
        {
            try
            {
                await pair.Value.DisposeAsync();
            }
            catch
            {
                // best effort shutdown
            }
        }

        realSessions.Clear();
        unityWorlds.Clear();
        unityClientBindings.Clear();
    }

    public async Task RegisterBoundClientAsync(string clientId, string connectionId)
    {
        var normalizedClientId = NormalizeClientId(clientId);
        var normalizedConnectionId = connectionId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedConnectionId))
        {
            return;
        }

        if (connectionToClient.TryGetValue(normalizedConnectionId, out var existingClientId) &&
            string.Equals(existingClientId, normalizedClientId, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(existingClientId))
        {
            await UnregisterBoundClientAsync(existingClientId, normalizedConnectionId);
        }

        connectionToClient[normalizedConnectionId] = normalizedClientId;
        var clientSet = clientConnections.GetOrAdd(normalizedClientId, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        clientSet[normalizedConnectionId] = 0;
        _ = boundClientCounts.AddOrUpdate(normalizedClientId, 1, static (_, value) => value + 1);

        foreach (var realSession in realSessions.Where(pair =>
                     string.Equals(pair.Key.ClientId, normalizedClientId, StringComparison.Ordinal))
                 .Select(pair => pair.Value))
        {
            await realSession.RegisterUiConnectionAsync(normalizedConnectionId);
        }

        if (unityClientBindings.TryGetValue(normalizedClientId, out var binding) &&
            unityWorlds.TryGetValue(binding.WorldKey, out var world))
        {
            await hubContext.Groups.AddToGroupAsync(normalizedConnectionId, world.GroupName);
            await world.RegisterUiConnectionAsync(normalizedConnectionId);
        }
    }

    public async Task UnregisterBoundClientAsync(string clientId, string connectionId)
    {
        var normalizedClientId = NormalizeClientId(clientId);
        var normalizedConnectionId = connectionId?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedConnectionId))
        {
            connectionToClient.TryRemove(normalizedConnectionId, out _);
            if (clientConnections.TryGetValue(normalizedClientId, out var set))
            {
                set.TryRemove(normalizedConnectionId, out _);
                if (set.IsEmpty)
                {
                    clientConnections.TryRemove(normalizedClientId, out _);
                }
            }

            foreach (var realSession in realSessions.Where(pair =>
                         string.Equals(pair.Key.ClientId, normalizedClientId, StringComparison.Ordinal))
                     .Select(pair => pair.Value))
            {
                await realSession.UnregisterUiConnectionAsync(normalizedConnectionId);
            }

            if (unityClientBindings.TryGetValue(normalizedClientId, out var binding) &&
                unityWorlds.TryGetValue(binding.WorldKey, out var world))
            {
                await world.UnregisterUiConnectionAsync(normalizedConnectionId);
                await hubContext.Groups.RemoveFromGroupAsync(normalizedConnectionId, world.GroupName);
            }
        }

        if (!boundClientCounts.TryGetValue(normalizedClientId, out var current))
        {
            return;
        }

        var next = Math.Max(0, current - 1);
        if (next == 0)
        {
            boundClientCounts.TryRemove(normalizedClientId, out _);
            await DetachUnityClientAsync(normalizedClientId, CancellationToken.None);
            return;
        }

        boundClientCounts[normalizedClientId] = next;
    }

    public async Task<StatusDto> ConnectAsync(string clientId, string runtimeMode, string? host, int? port, CancellationToken cancellationToken)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            return await ConnectUnityAsync(key.ClientId, host, port, cancellationToken);
        }

        var target = ResolveTargetForConnect(key.Mode, host, port);
        await DisconnectOtherRealSessionsAsync(key, target.Host, target.Port, cancellationToken);

        var session = await GetOrCreateRealSessionAsync(key, cancellationToken);
        await session.ConnectAsync(host, port, cancellationToken);
        return session.GetStatus();
    }

    public async Task<StatusDto> DisconnectAsync(string clientId, string runtimeMode, CancellationToken cancellationToken)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            var target = GetConnectionTarget(key.ClientId, key.Mode);
            await DetachUnityClientAsync(key.ClientId, cancellationToken);
            var status = BuildDefaultStatus(key);
            return status with { TargetHost = target.Host, TargetPort = target.Port };
        }

        if (!realSessions.TryGetValue(key, out var session))
        {
            return BuildDefaultStatus(key);
        }

        await session.DisconnectAsync(cancellationToken);
        return session.GetStatus();
    }

    public StatusDto GetStatus(string clientId, string runtimeMode)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            if (TryGetUnityWorldForClient(key.ClientId, out var world, out _))
            {
                return BuildUnityStatus(key.ClientId, world);
            }

            return BuildDefaultStatus(key);
        }

        if (!realSessions.TryGetValue(key, out var session))
        {
            return BuildDefaultStatus(key);
        }

        return session.GetStatus();
    }

    public ConnectionTargetDto GetConnectionTarget(string clientId, string runtimeMode)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            if (unityClientBindings.TryGetValue(key.ClientId, out var binding))
            {
                return new ConnectionTargetDto(binding.WorldKey.Host, binding.WorldKey.Port, RuntimeModes.UnitySim);
            }

            return BuildDefaultTarget(key.Mode);
        }

        if (!realSessions.TryGetValue(key, out var session))
        {
            return BuildDefaultTarget(key.Mode);
        }

        return session.GetConnectionTarget();
    }

    public async Task<CommandResponse> SendCommandAsync(
        string clientId,
        string runtimeMode,
        string command,
        string? agentId,
        CancellationToken cancellationToken)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            if (!TryGetUnityWorldForClient(key.ClientId, out var world, out var binding))
            {
                return new CommandResponse(false, "Unity runtime session is not connected");
            }

            var resolvedAgentId = string.IsNullOrWhiteSpace(agentId) ? binding.SelectedControlAgentId : agentId.Trim();
            if (string.IsNullOrWhiteSpace(resolvedAgentId))
            {
                return new CommandResponse(false, "Control agent is not selected. Use /api/unity/client-selection or pass agentId in command request");
            }

            return await world.SendCommandAsync(command, "ui", resolvedAgentId, key.ClientId, cancellationToken);
        }

        if (!realSessions.TryGetValue(key, out var session))
        {
            return new CommandResponse(false, "Runtime session is not connected");
        }

        return await session.SendCommandAsync(command, "ui", agentId, key.ClientId, cancellationToken);
    }

    public void SetDirectDrive(string clientId, string runtimeMode, string? agentId, float throttle, float steer)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (!string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            return;
        }

        if (!TryGetUnityWorldForClient(key.ClientId, out var world, out var binding))
        {
            return;
        }

        var resolvedAgentId = string.IsNullOrWhiteSpace(agentId) ? binding.SelectedControlAgentId : agentId.Trim();
        world.SetDirectDrive(resolvedAgentId, throttle, steer);
    }

    public async Task<UnityRuntimeCatalogDto> GetUnityRuntimeCatalogAsync(
        string clientId,
        string runtimeMode,
        string? host,
        int? port,
        CancellationToken cancellationToken)
    {
        EnsureUnityMode(runtimeMode);
        var normalizedClientId = NormalizeClientId(clientId);

        if (TryGetUnityWorldForClient(normalizedClientId, out var world, out var binding))
        {
            var worldCatalog = await world.GetRuntimeCatalogAsync(host, port, cancellationToken);
            var normalizedBinding = NormalizeUnityClientBinding(binding, worldCatalog);
            unityClientBindings[normalizedClientId] = normalizedBinding;
            return ApplyClientSelection(worldCatalog, normalizedBinding);
        }

        var target = ResolveTargetForConnect(RuntimeModes.UnitySim, host, port);
        var probe = new UnityKs0223RuntimeProvider(
            sessionLogger,
            httpClientFactory,
            hubContext,
            loggerFactory.CreateLogger<UnityKs0223RuntimeProvider>(),
            clientGroup: null,
            sessionClientId: normalizedClientId,
            sessionRuntimeMode: RuntimeModes.UnitySim);

        var catalog = await probe.GetRuntimeCatalogAsync(target.Host, target.Port, cancellationToken);
        return catalog;
    }

    public async Task<UnityRuntimeCatalogDto> SetUnityRuntimeSelectionAsync(
        UnityRuntimeSelectionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureUnityMode(request.RuntimeMode);
        var clientId = NormalizeClientId(request.ClientId);

        if (!TryGetUnityWorldForClient(clientId, out var world, out var binding))
        {
            throw new InvalidOperationException("Unity runtime session is not connected. Connect first and then apply runtime selection");
        }

        var worldCatalog = await world.SetRuntimeSelectionAsync(
            request.TrackId,
            request.VehicleId,
            request.CameraMode,
            controlAgentId: null,
            request.Agents,
            request.ApplyImmediately,
            cancellationToken,
            collisionsEnabled: request.CollisionsEnabled,
            seeEachOther: request.SeeEachOther);

        var normalizedBinding = NormalizeUnityClientBinding(binding, worldCatalog);
        unityClientBindings[clientId] = normalizedBinding;
        return ApplyClientSelection(worldCatalog, normalizedBinding);
    }

    public async Task<UnityRuntimeCatalogDto> SetUnityClientSelectionAsync(
        UnityClientSelectionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureUnityMode(request.RuntimeMode);
        var clientId = NormalizeClientId(request.ClientId);

        if (!TryGetUnityWorldForClient(clientId, out var world, out var binding))
        {
            throw new InvalidOperationException("Unity runtime session is not connected. Connect first and then apply client selection");
        }

        var catalog = await world.GetRuntimeCatalogAsync(null, null, cancellationToken);
        var agentIds = new HashSet<string>(catalog.Agents.Select(agent => agent.AgentId), StringComparer.Ordinal);

        var nextControlAgentId = request.ControlAgentId is null ? binding.SelectedControlAgentId : request.ControlAgentId.Trim();
        if (!string.IsNullOrWhiteSpace(nextControlAgentId) && !agentIds.Contains(nextControlAgentId))
        {
            throw new ArgumentException($"Control agent '{nextControlAgentId}' does not exist in current Unity world");
        }

        var nextCameraAgentId = request.CameraAgentId is null ? binding.SelectedCameraAgentId : request.CameraAgentId.Trim();
        if (!string.IsNullOrWhiteSpace(nextCameraAgentId) && !agentIds.Contains(nextCameraAgentId))
        {
            throw new ArgumentException($"Camera agent '{nextCameraAgentId}' does not exist in current Unity world");
        }

        if (string.IsNullOrWhiteSpace(nextCameraAgentId))
        {
            nextCameraAgentId = nextControlAgentId;
        }

        var updatedBinding = new UnityClientBinding(binding.WorldKey, nextControlAgentId, nextCameraAgentId);
        updatedBinding = NormalizeUnityClientBinding(updatedBinding, catalog);
        unityClientBindings[clientId] = updatedBinding;
        return ApplyClientSelection(catalog, updatedBinding);
    }

    public CameraStatusDto GetCameraStatus(string clientId, string runtimeMode)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            if (!TryGetUnityWorldForClient(key.ClientId, out var world, out _))
            {
                return BuildDefaultCameraStatus();
            }

            return world.GetCameraStatus();
        }

        if (!realSessions.TryGetValue(key, out var session))
        {
            return BuildDefaultCameraStatus();
        }

        var status = session.GetCameraStatus();
        if (status.HasFrame)
        {
            return status;
        }

        foreach (var other in realSessions)
        {
            if (other.Key.Equals(key))
            {
                continue;
            }

            var candidate = other.Value.GetCameraStatus();
            if (candidate.HasFrame)
            {
                return candidate;
            }
        }

        return status;
    }

    public SensorBridgeStatusDto GetSensorStatus(string clientId, string runtimeMode)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            if (!TryGetUnityWorldForClient(key.ClientId, out var world, out _))
            {
                return BuildDefaultSensorStatus(runtimeMode);
            }

            return world.GetSensorStatus();
        }

        if (!realSessions.TryGetValue(key, out var session))
        {
            return BuildDefaultSensorStatus(runtimeMode);
        }

        return session.GetSensorStatus();
    }

    public SensorTelemetryDto? GetLatestSensorTelemetry(string clientId, string runtimeMode)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            if (!TryGetUnityWorldForClient(key.ClientId, out var world, out _))
            {
                return null;
            }

            return world.GetLatestSensorTelemetry();
        }

        if (!realSessions.TryGetValue(key, out var session))
        {
            return null;
        }

        return session.GetLatestSensorTelemetry();
    }

    public async Task<SensorBridgeResponse> UpdateConfigAsync(
        string clientId,
        string runtimeMode,
        bool? autoScanEnabled,
        int? sampleIntervalMs,
        double? scanIntervalSec,
        int? scanSettleMs,
        int? driveSpeedPercent,
        int? cameraSpeedPercent,
        int? ultrasonicServoPin,
        CancellationToken cancellationToken)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            if (!TryGetUnityWorldForClient(key.ClientId, out var world, out _))
            {
                return new SensorBridgeResponse(false, 400, null, "Unity runtime session is not connected");
            }

            return await world.UpdateConfigAsync(
                autoScanEnabled,
                sampleIntervalMs,
                scanIntervalSec,
                scanSettleMs,
                driveSpeedPercent,
                cameraSpeedPercent,
                ultrasonicServoPin,
                cancellationToken);
        }

        var session = await GetOrCreateRealSessionAsync(key, cancellationToken);
        return await session.UpdateConfigAsync(
            autoScanEnabled,
            sampleIntervalMs,
            scanIntervalSec,
            scanSettleMs,
            driveSpeedPercent,
            cameraSpeedPercent,
            ultrasonicServoPin,
            cancellationToken);
    }

    public async Task<SensorBridgeResponse> SetUltrasonicPositionAsync(
        string clientId,
        string runtimeMode,
        int angleDeg,
        bool disableAutoScan,
        int? servoPin,
        CancellationToken cancellationToken)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            if (!TryGetUnityWorldForClient(key.ClientId, out var world, out _))
            {
                return new SensorBridgeResponse(false, 400, null, "Unity runtime session is not connected");
            }

            return await world.SetUltrasonicPositionAsync(angleDeg, disableAutoScan, servoPin, cancellationToken);
        }

        var session = await GetOrCreateRealSessionAsync(key, cancellationToken);
        return await session.SetUltrasonicPositionAsync(angleDeg, disableAutoScan, servoPin, cancellationToken);
    }

    public async Task<SensorBridgeResponse> SetUltrasonicAutoScanAsync(
        string clientId,
        string runtimeMode,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            if (!TryGetUnityWorldForClient(key.ClientId, out var world, out _))
            {
                return new SensorBridgeResponse(false, 400, null, "Unity runtime session is not connected");
            }

            return await world.SetUltrasonicAutoScanAsync(enabled, cancellationToken);
        }

        var session = await GetOrCreateRealSessionAsync(key, cancellationToken);
        return await session.SetUltrasonicAutoScanAsync(enabled, cancellationToken);
    }

    public async Task<SensorBridgeResponse> SetLedPatternAsync(
        string clientId,
        string runtimeMode,
        string pattern,
        CancellationToken cancellationToken)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            return new SensorBridgeResponse(false, 400, null, "LED commands are not supported in unity-sim mode");
        }

        var session = await GetOrCreateRealSessionAsync(key, cancellationToken);
        return await session.SendLedCommandAsync("/api/led/pattern", new { pattern }, cancellationToken);
    }

    public async Task<SensorBridgeResponse> SetLedCustomFrameAsync(
        string clientId,
        string runtimeMode,
        string frameHex,
        CancellationToken cancellationToken)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            return new SensorBridgeResponse(false, 400, null, "LED commands are not supported in unity-sim mode");
        }

        var session = await GetOrCreateRealSessionAsync(key, cancellationToken);
        return await session.SendLedCommandAsync("/api/led/custom", new { frame_hex = frameHex }, cancellationToken);
    }

    public async Task<SensorBridgeResponse> ClearLedAsync(
        string clientId,
        string runtimeMode,
        CancellationToken cancellationToken)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            return new SensorBridgeResponse(false, 400, null, "LED commands are not supported in unity-sim mode");
        }

        var session = await GetOrCreateRealSessionAsync(key, cancellationToken);
        return await session.SendLedCommandAsync("/api/led/clear", new { }, cancellationToken);
    }

    public bool TryGetLatestFrame(
        string clientId,
        string runtimeMode,
        string? agentId,
        out byte[] frame,
        out string contentType,
        out long version,
        out DateTimeOffset? timestamp)
    {
        var key = CreateKey(clientId, runtimeMode);
        if (string.Equals(key.Mode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            if (!TryGetUnityWorldForClient(key.ClientId, out var world, out var binding))
            {
                frame = Array.Empty<byte>();
                contentType = "image/jpeg";
                version = 0;
                timestamp = null;
                return false;
            }

            var resolvedAgentId = string.IsNullOrWhiteSpace(agentId) ? binding.SelectedCameraAgentId : agentId.Trim();
            if (string.IsNullOrWhiteSpace(resolvedAgentId))
            {
                resolvedAgentId = binding.SelectedControlAgentId;
            }

            return world.TryGetLatestFrame(resolvedAgentId, out frame, out contentType, out version, out timestamp);
        }

        if (!realSessions.TryGetValue(key, out var session))
        {
            frame = Array.Empty<byte>();
            contentType = "image/jpeg";
            version = 0;
            timestamp = null;
            return false;
        }

        if (session.TryGetLatestFrame(agentId, out frame, out contentType, out version, out timestamp))
        {
            return true;
        }

        foreach (var other in realSessions)
        {
            if (other.Key.Equals(key))
            {
                continue;
            }

            if (other.Value.TryGetLatestFrame(agentId, out frame, out contentType, out version, out timestamp))
            {
                return true;
            }
        }

        return false;
    }

    public HealthDto GetHealth(string clientId, string runtimeMode)
    {
        var status = GetStatus(clientId, runtimeMode);
        var camera = GetCameraStatus(clientId, runtimeMode);
        var sensors = GetSensorStatus(clientId, runtimeMode);
        var controlDegraded = status.DesiredConnection && !status.TcpConnected;
        var sensorDegraded = sensors.Enabled && sensors.ConsecutiveFailures >= 3;
        var overallStatus = controlDegraded || sensorDegraded ? "degraded" : "ok";
        var version = typeof(RuntimeSessionManager).Assembly.GetName().Version?.ToString() ?? "unknown";
        return new HealthDto(overallStatus, DateTimeOffset.UtcNow, version, status, camera, sensors);
    }

    private async Task<StatusDto> ConnectUnityAsync(string clientId, string? host, int? port, CancellationToken cancellationToken)
    {
        var target = ResolveTargetForConnect(RuntimeModes.UnitySim, host, port);
        var worldKey = CreateUnityWorldKey(target.Host, target.Port);

        if (unityClientBindings.TryGetValue(clientId, out var existingBinding) &&
            !existingBinding.WorldKey.Equals(worldKey))
        {
            await DetachUnityClientAsync(clientId, cancellationToken);
        }

        var world = await GetOrCreateUnityWorldAsync(worldKey, target.Host, target.Port, cancellationToken);
        world.AddClient(clientId);

        var binding = unityClientBindings.AddOrUpdate(
            clientId,
            _ => new UnityClientBinding(worldKey, string.Empty, string.Empty),
            (_, current) => current with { WorldKey = worldKey });

        await AttachClientConnectionsToUnityWorldAsync(clientId, world, cancellationToken);
        await world.EnsureConnectedAsync(target.Host, target.Port, cancellationToken);

        var worldCatalog = await world.GetRuntimeCatalogAsync(target.Host, target.Port, cancellationToken);
        var normalizedBinding = NormalizeUnityClientBinding(binding, worldCatalog);
        unityClientBindings[clientId] = normalizedBinding;

        return BuildUnityStatus(clientId, world);
    }

    private async Task<RealRuntimeSession> GetOrCreateRealSessionAsync(SessionKey key, CancellationToken cancellationToken)
    {
        if (!string.Equals(key.Mode, RuntimeModes.RealRobot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Real runtime session is expected");
        }

        if (realSessions.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var created = CreateRealSession(key);
        var session = realSessions.GetOrAdd(key, created);
        if (!ReferenceEquals(session, created))
        {
            await created.DisposeAsync();
            return session;
        }

        foreach (var connectionId in GetClientConnectionsSnapshot(key.ClientId))
        {
            await session.RegisterUiConnectionAsync(connectionId);
        }

        return session;
    }

    private RealRuntimeSession CreateRealSession(SessionKey key)
    {
        var group = GetClientGroup(key.ClientId);
        return new RealRuntimeSession(
            key.ClientId,
            group,
            piOptions,
            cameraOptions,
            sensorOptions,
            sessionLogger,
            telemetryParser,
            hubContext,
            httpClientFactory,
            loggerFactory,
            enableUdpCameraListener: !realSessions.Values.Any(session => session.UsesUdpCameraListener && session.IsStarted));
    }

    private SessionKey CreateKey(string clientId, string runtimeMode)
    {
        var normalizedClientId = NormalizeClientId(clientId);
        var normalizedMode = RuntimeModes.Normalize(runtimeMode);
        if (normalizedMode != RuntimeModes.RealRobot && normalizedMode != RuntimeModes.UnitySim)
        {
            throw new NotSupportedException($"Runtime mode '{runtimeMode}' is not supported");
        }

        return new SessionKey(normalizedClientId, normalizedMode);
    }

    private static string NormalizeClientId(string clientId)
    {
        var normalized = clientId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("clientId is required");
        }

        return normalized;
    }

    private static void EnsureUnityMode(string runtimeMode)
    {
        var normalized = RuntimeModes.Normalize(runtimeMode);
        if (!string.Equals(normalized, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            throw new ArgumentException("runtimeMode must be unity-sim for this endpoint");
        }
    }

    private StatusDto BuildDefaultStatus(SessionKey key)
    {
        var target = BuildDefaultTarget(key.Mode);
        var state = sessionLogger.GetState();
        var uiClients = boundClientCounts.TryGetValue(key.ClientId, out var count) ? count : 0;
        return new StatusDto(
            DesiredConnection: false,
            TcpConnected: false,
            UiConnectedClients: uiClients,
            TargetHost: target.Host,
            TargetPort: target.Port,
            LatencyMs: null,
            LastError: null,
            LastTcpMessageAt: null,
            IsLogging: state.IsLogging,
            CurrentLogFile: state.CurrentFile,
            HasParsedTelemetry: false,
            RuntimeMode: target.RuntimeMode,
            RuntimeLabel: target.RuntimeMode == RuntimeModes.UnitySim ? "Unity simulator" : "Keyestudio KS0223 (Real Robot)");
    }

    private StatusDto BuildUnityStatus(string clientId, UnityWorldSession world)
    {
        var status = world.GetStatus();
        var uiClients = boundClientCounts.TryGetValue(clientId, out var count) ? count : 0;
        return status with { UiConnectedClients = uiClients };
    }

    private ConnectionTargetDto BuildDefaultTarget(string runtimeMode)
    {
        var mode = RuntimeModes.Normalize(runtimeMode);
        return mode == RuntimeModes.UnitySim
            ? new ConnectionTargetDto("127.0.0.1", 8000, RuntimeModes.UnitySim)
            : new ConnectionTargetDto(piOptions.Host, piOptions.Port, RuntimeModes.RealRobot);
    }

    private ConnectionTargetDto ResolveTargetForConnect(string mode, string? host, int? port)
    {
        var fallback = BuildDefaultTarget(mode);
        var resolvedHost = string.IsNullOrWhiteSpace(host) ? fallback.Host : host.Trim();
        var resolvedPort = port.GetValueOrDefault(fallback.Port);
        if (resolvedPort < 1 || resolvedPort > 65535)
        {
            resolvedPort = fallback.Port;
        }

        return new ConnectionTargetDto(resolvedHost, resolvedPort, mode);
    }

    private async Task DisconnectOtherRealSessionsAsync(
        SessionKey currentKey,
        string targetHost,
        int targetPort,
        CancellationToken cancellationToken)
    {
        foreach (var pair in realSessions)
        {
            var key = pair.Key;
            if (key.Equals(currentKey))
            {
                continue;
            }

            var sessionTarget = pair.Value.GetConnectionTarget();
            if (!string.Equals(sessionTarget.Host, targetHost, StringComparison.OrdinalIgnoreCase) ||
                sessionTarget.Port != targetPort)
            {
                continue;
            }

            try
            {
                await pair.Value.DisconnectAsync(cancellationToken);
            }
            catch
            {
                // best effort: do not block active takeover flow
            }
        }
    }

    private static CameraStatusDto BuildDefaultCameraStatus() =>
        new(
            UdpListenerEnabled: false,
            UdpListenPort: 0,
            HasFrame: false,
            LastFrameAt: null,
            Source: null,
            FramesReceived: 0,
            BytesReceived: 0,
            HttpProbeCandidates: Array.Empty<string>(),
            HttpDiscoveredStreams: Array.Empty<string>());

    private static SensorBridgeStatusDto BuildDefaultSensorStatus(string runtimeMode) =>
        new(
            Enabled: true,
            EndpointUrl: null,
            PollIntervalMs: 800,
            HasTelemetry: false,
            LastTelemetryAt: null,
            LastSuccessAt: null,
            LastError: null,
            ConsecutiveFailures: 0);

    private static PiConnectionOptions Clone(PiConnectionOptions options) => new()
    {
        Host = options.Host,
        Port = options.Port,
        ReconnectDelayMs = options.ReconnectDelayMs,
        ReceiveBufferSize = options.ReceiveBufferSize,
    };

    private static CameraOptions Clone(CameraOptions options) => new()
    {
        EnableUdpListener = options.EnableUdpListener,
        UdpListenPort = options.UdpListenPort,
        MaxFrameBytes = options.MaxFrameBytes,
        MjpegFps = options.MjpegFps,
        ProbeIntervalSec = options.ProbeIntervalSec,
        HttpProbeTimeoutMs = options.HttpProbeTimeoutMs,
        HttpProbePaths = options.HttpProbePaths?.ToArray() ?? Array.Empty<string>(),
    };

    private static SensorBridgeOptions Clone(SensorBridgeOptions options) => new()
    {
        Enabled = options.Enabled,
        Port = options.Port,
        TelemetryPath = options.TelemetryPath,
        PollIntervalMs = options.PollIntervalMs,
        RequestTimeoutMs = options.RequestTimeoutMs,
    };

    private IReadOnlyList<string> GetClientConnectionsSnapshot(string clientId)
    {
        if (!clientConnections.TryGetValue(clientId, out var set))
        {
            return Array.Empty<string>();
        }

        return set.Keys.ToArray();
    }

    private static UnityWorldKey CreateUnityWorldKey(string host, int port)
    {
        var normalizedHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim().ToLowerInvariant();
        return new UnityWorldKey(normalizedHost, port);
    }

    private async Task<UnityWorldSession> GetOrCreateUnityWorldAsync(
        UnityWorldKey key,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        if (unityWorlds.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var created = new UnityWorldSession(
            host,
            port,
            $"unity-world:{key.Host}:{key.Port}",
            sessionLogger,
            hubContext,
            httpClientFactory,
            loggerFactory);

        var world = unityWorlds.GetOrAdd(key, created);
        if (!ReferenceEquals(world, created))
        {
            await created.DisposeAsync();
            return world;
        }

        return world;
    }

    private async Task AttachClientConnectionsToUnityWorldAsync(
        string clientId,
        UnityWorldSession world,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        foreach (var connectionId in GetClientConnectionsSnapshot(clientId))
        {
            await hubContext.Groups.AddToGroupAsync(connectionId, world.GroupName);
            await world.RegisterUiConnectionAsync(connectionId);
        }
    }

    private async Task DetachUnityClientAsync(string clientId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!unityClientBindings.TryRemove(clientId, out var binding))
        {
            return;
        }

        if (!unityWorlds.TryGetValue(binding.WorldKey, out var world))
        {
            return;
        }

        foreach (var connectionId in GetClientConnectionsSnapshot(clientId))
        {
            await world.UnregisterUiConnectionAsync(connectionId);
            await hubContext.Groups.RemoveFromGroupAsync(connectionId, world.GroupName);
        }

        var remainingClients = world.RemoveClient(clientId);
        if (remainingClients > 0)
        {
            return;
        }

        if (unityWorlds.TryRemove(binding.WorldKey, out var removedWorld))
        {
            await removedWorld.DisposeAsync();
        }
    }

    private bool TryGetUnityWorldForClient(string clientId, out UnityWorldSession world, out UnityClientBinding binding)
    {
        if (unityClientBindings.TryGetValue(clientId, out var localBinding) &&
            unityWorlds.TryGetValue(localBinding.WorldKey, out var resolvedWorld) &&
            resolvedWorld is not null)
        {
            world = resolvedWorld;
            binding = localBinding;
            return true;
        }

        world = null!;
        binding = default;
        return false;
    }

    private static UnityClientBinding NormalizeUnityClientBinding(UnityClientBinding binding, UnityRuntimeCatalogDto worldCatalog)
    {
        var agentIds = new HashSet<string>(worldCatalog.Agents.Select(agent => agent.AgentId), StringComparer.Ordinal);

        var selectedControl = string.IsNullOrWhiteSpace(binding.SelectedControlAgentId)
            ? worldCatalog.SelectedControlAgentId
            : binding.SelectedControlAgentId;
        if (!string.IsNullOrWhiteSpace(selectedControl) && !agentIds.Contains(selectedControl))
        {
            selectedControl = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(selectedControl) && worldCatalog.Agents.Count > 0)
        {
            selectedControl = worldCatalog.Agents[0].AgentId;
        }

        var selectedCamera = string.IsNullOrWhiteSpace(binding.SelectedCameraAgentId)
            ? worldCatalog.SelectedCameraAgentId
            : binding.SelectedCameraAgentId;
        if (!string.IsNullOrWhiteSpace(selectedCamera) && !agentIds.Contains(selectedCamera))
        {
            selectedCamera = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(selectedCamera))
        {
            selectedCamera = selectedControl;
        }

        return new UnityClientBinding(binding.WorldKey, selectedControl ?? string.Empty, selectedCamera ?? string.Empty);
    }

    private static UnityRuntimeCatalogDto ApplyClientSelection(UnityRuntimeCatalogDto worldCatalog, UnityClientBinding binding)
    {
        var normalized = NormalizeUnityClientBinding(binding, worldCatalog);
        return worldCatalog with
        {
            SelectedControlAgentId = normalized.SelectedControlAgentId,
            SelectedCameraAgentId = normalized.SelectedCameraAgentId,
        };
    }

    private readonly record struct SessionKey(string ClientId, string Mode);
    private readonly record struct UnityWorldKey(string Host, int Port);
    private readonly record struct UnityClientBinding(UnityWorldKey WorldKey, string SelectedControlAgentId, string SelectedCameraAgentId);

    private sealed class UnityWorldSession : IAsyncDisposable
    {
        private readonly object sync = new();
        private readonly HashSet<string> attachedClients = new(StringComparer.Ordinal);
        private readonly HashSet<string> uiConnections = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim lifecycleLock = new(1, 1);
        private readonly UnityKs0223RuntimeProvider provider;

        public UnityWorldSession(
            string host,
            int port,
            string groupName,
            SessionLogger sessionLogger,
            IHubContext<TelemetryHub> hubContext,
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory)
        {
            Host = host;
            Port = port;
            GroupName = groupName;
            provider = new UnityKs0223RuntimeProvider(
                sessionLogger,
                httpClientFactory,
                hubContext,
                loggerFactory.CreateLogger<UnityKs0223RuntimeProvider>(),
                clientGroup: groupName,
                sessionClientId: $"unity-world@{host}:{port}",
                sessionRuntimeMode: RuntimeModes.UnitySim);
        }

        public string Host { get; }
        public int Port { get; }
        public string GroupName { get; }

        public void AddClient(string clientId)
        {
            lock (sync)
            {
                attachedClients.Add(clientId);
            }
        }

        public int RemoveClient(string clientId)
        {
            lock (sync)
            {
                attachedClients.Remove(clientId);
                return attachedClients.Count;
            }
        }

        public StatusDto GetStatus() => provider.GetStatus();

        public ConnectionTargetDto GetConnectionTarget() => provider.GetConnectionTarget();

        public CameraStatusDto GetCameraStatus() => provider.GetCameraStatus();

        public SensorBridgeStatusDto GetSensorStatus() => provider.GetSensorStatus();

        public SensorTelemetryDto? GetLatestSensorTelemetry() => provider.GetLatestSensorTelemetry();

        public bool TryGetLatestFrame(string? agentId, out byte[] frame, out string contentType, out long version, out DateTimeOffset? timestamp) =>
            provider.TryGetLatestFrame(agentId, out frame, out contentType, out version, out timestamp);

        public async Task EnsureConnectedAsync(string host, int port, CancellationToken cancellationToken)
        {
            await lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                var status = provider.GetStatus();
                if (status.TcpConnected && status.DesiredConnection &&
                    string.Equals(status.TargetHost, host, StringComparison.OrdinalIgnoreCase) &&
                    status.TargetPort == port)
                {
                    return;
                }

                await provider.ConnectAsync(host, port, cancellationToken);
            }
            finally
            {
                lifecycleLock.Release();
            }
        }

        public Task RegisterUiConnectionAsync(string connectionId)
        {
            lock (sync)
            {
                if (!uiConnections.Add(connectionId))
                {
                    return Task.CompletedTask;
                }
            }

            return provider.RegisterUiConnectionAsync(connectionId);
        }

        public Task UnregisterUiConnectionAsync(string connectionId)
        {
            lock (sync)
            {
                if (!uiConnections.Remove(connectionId))
                {
                    return Task.CompletedTask;
                }
            }

            return provider.UnregisterUiConnectionAsync(connectionId);
        }

        public Task<CommandResponse> SendCommandAsync(
            string command,
            string source,
            string? agentId,
            string? clientId,
            CancellationToken cancellationToken) =>
            provider.SendCommandAsync(command, source, agentId, clientId, cancellationToken);

        public void SetDirectDrive(string? agentId, float throttle, float steer) =>
            provider.SetDirectDrive(agentId, throttle, steer);

        public Task<SensorBridgeResponse> UpdateConfigAsync(
            bool? autoScanEnabled,
            int? sampleIntervalMs,
            double? scanIntervalSec,
            int? scanSettleMs,
            int? driveSpeedPercent,
            int? cameraSpeedPercent,
            int? ultrasonicServoPin,
            CancellationToken cancellationToken) =>
            provider.UpdateConfigAsync(
                autoScanEnabled,
                sampleIntervalMs,
                scanIntervalSec,
                scanSettleMs,
                driveSpeedPercent,
                cameraSpeedPercent,
                ultrasonicServoPin,
                cancellationToken);

        public Task<SensorBridgeResponse> SetUltrasonicPositionAsync(int angleDeg, bool disableAutoScan, int? servoPin, CancellationToken cancellationToken) =>
            provider.SetUltrasonicPositionAsync(angleDeg, disableAutoScan, servoPin, cancellationToken);

        public Task<SensorBridgeResponse> SetUltrasonicAutoScanAsync(bool enabled, CancellationToken cancellationToken) =>
            provider.SetUltrasonicAutoScanAsync(enabled, cancellationToken);

        public Task<UnityRuntimeCatalogDto> GetRuntimeCatalogAsync(string? host, int? port, CancellationToken cancellationToken) =>
            provider.GetRuntimeCatalogAsync(host, port, cancellationToken);

        public Task<UnityRuntimeCatalogDto> SetRuntimeSelectionAsync(
            string? trackId,
            string? vehicleId,
            string? cameraMode,
            string? controlAgentId,
            IReadOnlyList<UnityRuntimeAgentSelectionRequest>? agents,
            bool applyImmediately,
            CancellationToken cancellationToken,
            bool? collisionsEnabled = null,
            bool? seeEachOther = null) =>
            provider.SetRuntimeSelectionAsync(trackId, vehicleId, cameraMode, controlAgentId, agents, applyImmediately, cancellationToken, collisionsEnabled, seeEachOther);

        public ValueTask DisposeAsync() => new(provider.DisconnectAsync(CancellationToken.None));
    }

    private sealed class RealRuntimeSession : IAsyncDisposable
    {
        private readonly PiTcpClientService pi;
        private readonly CameraStreamService camera;
        private readonly SensorBridgeService sensor;
        private readonly SessionLogger sessionLogger;
        private readonly ILogger<RealRuntimeSession> logger;
        private readonly SemaphoreSlim lifecycleLock = new(1, 1);
        private bool started;

        public RealRuntimeSession(
            string clientId,
            string group,
            PiConnectionOptions piOptions,
            CameraOptions cameraOptions,
            SensorBridgeOptions sensorOptions,
            SessionLogger sessionLogger,
            TelemetryParser telemetryParser,
            IHubContext<TelemetryHub> hubContext,
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory,
            bool enableUdpCameraListener)
        {
            ClientId = clientId;
            Mode = RuntimeModes.RealRobot;
            UsesUdpCameraListener = enableUdpCameraListener;
            this.sessionLogger = sessionLogger;
            logger = loggerFactory.CreateLogger<RealRuntimeSession>();

            var realCameraOptions = Clone(cameraOptions);
            realCameraOptions.EnableUdpListener = enableUdpCameraListener;

            pi = new PiTcpClientService(
                Microsoft.Extensions.Options.Options.Create(Clone(piOptions)),
                hubContext,
                sessionLogger,
                telemetryParser,
                loggerFactory.CreateLogger<PiTcpClientService>(),
                group,
                clientId,
                RuntimeModes.RealRobot);
            camera = new CameraStreamService(
                Microsoft.Extensions.Options.Options.Create(realCameraOptions),
                pi,
                httpClientFactory,
                loggerFactory.CreateLogger<CameraStreamService>());
            sensor = new SensorBridgeService(
                Microsoft.Extensions.Options.Options.Create(Clone(sensorOptions)),
                httpClientFactory,
                pi,
                sessionLogger,
                hubContext,
                loggerFactory.CreateLogger<SensorBridgeService>(),
                group,
                clientId,
                RuntimeModes.RealRobot);
        }

        public string ClientId { get; }

        public string Mode { get; }
        public bool UsesUdpCameraListener { get; }
        public bool IsStarted => started;

        public StatusDto GetStatus() => pi.GetStatus() with { RuntimeMode = Mode, RuntimeLabel = "Keyestudio KS0223 (Real Robot)" };

        public ConnectionTargetDto GetConnectionTarget() => pi.GetConnectionTarget() with { RuntimeMode = Mode };

        public CameraStatusDto GetCameraStatus() => camera.GetStatus();

        public SensorBridgeStatusDto GetSensorStatus() => sensor.GetStatus();

        public SensorTelemetryDto? GetLatestSensorTelemetry() => sensor.GetLatestTelemetry();

        public bool TryGetLatestFrame(string? agentId, out byte[] frame, out string contentType, out long version, out DateTimeOffset? timestamp)
        {
            _ = agentId;
            return camera.TryGetLatestFrame(out frame, out contentType, out version, out timestamp);
        }

        public async Task ConnectAsync(string? host, int? port, CancellationToken cancellationToken)
        {
            await EnsureStartedAsync(cancellationToken);
            await pi.ConnectAsync(host, port, cancellationToken);
            await TriggerCameraBootstrapPingAsync(cancellationToken);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken) => pi.DisconnectAsync(cancellationToken);

        public async Task RegisterUiConnectionAsync(string connectionId)
        {
            await EnsureStartedAsync(CancellationToken.None);
            await pi.RegisterUiConnectionAsync(connectionId);
        }

        public async Task UnregisterUiConnectionAsync(string connectionId)
        {
            await EnsureStartedAsync(CancellationToken.None);
            await pi.UnregisterUiConnectionAsync(connectionId);
        }

        public Task<CommandResponse> SendCommandAsync(
            string command,
            string source,
            string? agentId,
            string? clientId,
            CancellationToken cancellationToken)
        {
            _ = agentId;
            _ = clientId;
            return pi.SendCommandAsync(command, source, cancellationToken);
        }

        public Task<SensorBridgeResponse> UpdateConfigAsync(
            bool? autoScanEnabled,
            int? sampleIntervalMs,
            double? scanIntervalSec,
            int? scanSettleMs,
            int? driveSpeedPercent,
            int? cameraSpeedPercent,
            int? ultrasonicServoPin,
            CancellationToken cancellationToken) => sensor.SendBridgeCommandAsync(
            "/api/config",
            new
            {
                auto_scan_enabled = autoScanEnabled,
                sample_interval_ms = sampleIntervalMs,
                scan_interval_sec = scanIntervalSec,
                scan_settle_ms = scanSettleMs,
                drive_speed_percent = driveSpeedPercent,
                camera_speed_percent = cameraSpeedPercent,
                ultrasonic_servo_pin = ultrasonicServoPin,
            },
            cancellationToken);

        public Task<SensorBridgeResponse> SetUltrasonicPositionAsync(int angleDeg, bool disableAutoScan, int? servoPin, CancellationToken cancellationToken) =>
            sensor.SendBridgeCommandAsync(
                "/api/ultrasonic/position",
                new
                {
                    angle_deg = angleDeg,
                    disable_auto_scan = disableAutoScan,
                    servo_pin = servoPin,
                },
                cancellationToken);

        public Task<SensorBridgeResponse> SetUltrasonicAutoScanAsync(bool enabled, CancellationToken cancellationToken) =>
            sensor.SendBridgeCommandAsync("/api/ultrasonic/auto-scan", new { enabled }, cancellationToken);

        public Task<SensorBridgeResponse> SendLedCommandAsync(string path, object payload, CancellationToken cancellationToken) =>
            sensor.SendBridgeCommandAsync(path, payload, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await lifecycleLock.WaitAsync();
            try
            {
                if (!started)
                {
                    return;
                }

                await sensor.StopAsync(CancellationToken.None);
                await camera.StopAsync(CancellationToken.None);
                await pi.StopAsync(CancellationToken.None);
                started = false;
            }
            finally
            {
                lifecycleLock.Release();
            }
        }

        private async Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            if (started)
            {
                return;
            }

            await lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                if (started)
                {
                    return;
                }

                await pi.StartAsync(cancellationToken);
                await camera.StartAsync(cancellationToken);
                await sensor.StartAsync(cancellationToken);
                started = true;
            }
            finally
            {
                lifecycleLock.Release();
            }
        }

        private async Task TriggerCameraBootstrapPingAsync(CancellationToken cancellationToken)
        {
            // FramesSend.py on KS0223 starts streaming only after first ICMP echo on wlan0.
            // Send best-effort ping with payload right after TCP connect so camera recovers
            // automatically after robot reboot without manual terminal steps.
            try
            {
                var target = pi.GetConnectionTarget();
                if (string.IsNullOrWhiteSpace(target.Host))
                {
                    return;
                }

                using var ping = new Ping();
                var payload = Encoding.ASCII.GetBytes("ks0223-camera-bootstrap-ping-32b");
                var options = new PingOptions(ttl: 64, dontFragment: true);
                PingReply? reply = null;
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
                    reply = await ping.SendPingAsync(target.Host, 1500, payload, options);
                    if (reply.Status == IPStatus.Success)
                    {
                        break;
                    }

                    if (attempt < 3)
                    {
                        await Task.Delay(350, cancellationToken);
                    }
                }

                await sessionLogger.WriteAsync(
                    "camera.bootstrap_ping",
                    new
                    {
                        clientId = ClientId,
                        runtimeMode = Mode,
                        targetHost = target.Host,
                        targetPort = target.Port,
                        status = reply?.Status.ToString() ?? "unknown",
                        roundtripMs = reply?.RoundtripTime,
                    },
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Camera bootstrap ping failed");
            }
        }
    }
}
