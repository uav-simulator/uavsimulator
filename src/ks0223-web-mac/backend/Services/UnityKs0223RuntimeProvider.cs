using System.Globalization;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using Ks0223.Web.Backend.Hubs;
using Ks0223.Web.Backend.Models;
using Microsoft.AspNetCore.SignalR;

namespace Ks0223.Web.Backend.Services;

public sealed class UnityKs0223RuntimeProvider : IKs0223RuntimeProvider
{
    private const string CameraProfile = "high";
    private const string RenderQualityProfile = "high";

    private sealed class AgentControlState
    {
        public float LeftPwmNorm { get; set; }
        public float RightPwmNorm { get; set; }
        public float BrakeNorm { get; set; }
        public int CameraPanDeg { get; set; } = 90;
        public int CameraTiltDeg { get; set; } = 90;
        public DateTimeOffset LastDriveInputAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class AgentFrameState
    {
        public byte[]? Bytes { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public long Version { get; set; }
    }

    private sealed class AgentControlOwnerState
    {
        public string? ClientId { get; set; }
        public DateTimeOffset LeaseUntil { get; set; }
    }

    private static readonly string[] PreferredVehicleIds =
    {
        "vehicle.arcade.blue.v1",
        "vehicle.prometeo.sport.v1",
    };

    private static readonly string[] PreferredTrackIds =
    {
        "track.roadsystem_realistic.v2",
        "track.roadsystem_arena.v1",
        "track.basic_arena.v1",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly TimeSpan DriveInputWatchdog = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan ControlOwnershipLease = TimeSpan.FromSeconds(2);

    private readonly object stateLock = new();
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private readonly SessionLogger sessionLogger;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IHubContext<TelemetryHub> hubContext;
    private readonly ILogger<UnityKs0223RuntimeProvider> logger;
    private readonly string? clientGroup;
    private readonly string? sessionClientId;
    private readonly string sessionRuntimeMode;

    private bool desiredConnection;
    private bool unityConnected;
    private int uiConnectedClients;
    private string runtimeLabel = "Arcade Free Racing Car (Blue)";
    private string targetHost = "127.0.0.1";
    private int targetPort = 8000;
    private string selectedVehicleId = PreferredVehicleIds[0];
    private string selectedTrackId = PreferredTrackIds[0];
    private string selectedCameraMode = "spectator";
    private string selectedControlAgentId = string.Empty;
    private List<UnityRuntimeAgentSelectionRequest> configuredAgents = new();
    private IReadOnlyList<UnityRuntimeOptionDto> availableTracks = Array.Empty<UnityRuntimeOptionDto>();
    private IReadOnlyList<UnityRuntimeOptionDto> availableVehicles = Array.Empty<UnityRuntimeOptionDto>();
    private double? latencyMs;
    private string? lastError;
    private DateTimeOffset? lastLoopAt;
    private bool hasTelemetry;
    private byte[]? latestFrame;
    private DateTimeOffset? lastFrameAt;
    private long frameVersion;
    private long framesReceived;
    private long bytesReceived;
    private SensorTelemetryDto? latestTelemetry;
    private DateTimeOffset? lastTelemetryAt;
    private DateTimeOffset? lastSuccessAt;
    private int consecutiveFailures;
    private int driveSpeedPercent = 80;
    private int cameraSpeedPercent = 70;
    private int ultrasonicServoPin = 5;
    private int ultrasonicAngleDeg = 90;
    private bool ultrasonicAutoScanEnabled = false;
    private int cameraPanDeg = 90;
    private int cameraTiltDeg = 90;
    private int autoScanDirection = 1;
    private float leftPwmNorm;
    private float rightPwmNorm;
    private float brakeNorm;
    private readonly Dictionary<string, AgentControlState> agentControlStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentFrameState> agentFrameStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentControlOwnerState> agentControlOwners = new(StringComparer.Ordinal);
    private CancellationTokenSource? loopCts;
    private Task? loopTask;
    private string? lastSourceUrl;

    public UnityKs0223RuntimeProvider(
        SessionLogger sessionLogger,
        IHttpClientFactory httpClientFactory,
        IHubContext<TelemetryHub> hubContext,
        ILogger<UnityKs0223RuntimeProvider> logger,
        string? clientGroup = null,
        string? sessionClientId = null,
        string sessionRuntimeMode = RuntimeModes.UnitySim)
    {
        this.sessionLogger = sessionLogger;
        this.httpClientFactory = httpClientFactory;
        this.hubContext = hubContext;
        this.logger = logger;
        this.clientGroup = string.IsNullOrWhiteSpace(clientGroup) ? null : clientGroup.Trim();
        this.sessionClientId = string.IsNullOrWhiteSpace(sessionClientId) ? null : sessionClientId.Trim();
        this.sessionRuntimeMode = string.IsNullOrWhiteSpace(sessionRuntimeMode)
            ? RuntimeModes.UnitySim
            : RuntimeModes.Normalize(sessionRuntimeMode);
    }

    public string Mode => RuntimeModes.UnitySim;

    public StatusDto GetStatus()
    {
        var logState = sessionLogger.GetState();
        lock (stateLock)
        {
            return new StatusDto(
                DesiredConnection: desiredConnection,
                TcpConnected: unityConnected,
                UiConnectedClients: uiConnectedClients,
                TargetHost: targetHost,
                TargetPort: targetPort,
                LatencyMs: latencyMs,
                LastError: lastError,
                LastTcpMessageAt: lastLoopAt,
                IsLogging: logState.IsLogging,
                CurrentLogFile: logState.CurrentFile,
                HasParsedTelemetry: hasTelemetry,
                RuntimeMode: Mode,
                RuntimeLabel: runtimeLabel);
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
        return ConnectCoreAsync(host, port, cancellationToken);
    }

    public async Task<UnityRuntimeCatalogDto> GetRuntimeCatalogAsync(string? host, int? port, CancellationToken cancellationToken)
    {
        var resolvedHost = !string.IsNullOrWhiteSpace(host) ? NormalizeTargetHost(host.Trim()) : targetHost;
        var resolvedPort = port is >= 1 and <= 65535 ? port.Value : targetPort;
        return await ProbeContractForEndpointAsync(resolvedHost, resolvedPort, cancellationToken);
    }

    public async Task<UnityRuntimeCatalogDto> SetRuntimeSelectionAsync(
        string? trackId,
        string? vehicleId,
        string? cameraMode,
        string? controlAgentId,
        IReadOnlyList<UnityRuntimeAgentSelectionRequest>? agents,
        bool applyImmediately,
        CancellationToken cancellationToken)
    {
        lock (stateLock)
        {
            if (!string.IsNullOrWhiteSpace(trackId))
            {
                selectedTrackId = trackId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(vehicleId))
            {
                selectedVehicleId = vehicleId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(cameraMode))
            {
                selectedCameraMode = NormalizeCameraMode(cameraMode);
            }
            _ = controlAgentId;

            if (agents is not null)
            {
                configuredAgents = agents
                    .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.VehicleId))
                    .Select((item, index) => new UnityRuntimeAgentSelectionRequest(
                        AgentId: string.IsNullOrWhiteSpace(item.AgentId) ? $"agent-{index + 1}" : item.AgentId!.Trim(),
                        VehicleId: item.VehicleId!.Trim(),
                        IsPrimary: item.IsPrimary))
                    .ToList();
            }

            SyncAgentStateDictionariesLocked(NormalizeConfiguredAgents(configuredAgents));
        }

        if (!applyImmediately)
        {
            return BuildCatalogSnapshot();
        }

        var shouldApplyReset = false;
        lock (stateLock)
        {
            shouldApplyReset = desiredConnection && unityConnected;
        }

        if (!shouldApplyReset)
        {
            return BuildCatalogSnapshot();
        }

        var catalog = await ProbeContractAsync(cancellationToken);
        using var initial = await ResetSimulationAsync(cancellationToken);
        UpdateFromStepResult(initial, selectedControlAgentId, updateSharedState: true);
        await BroadcastStatusAsync(cancellationToken);
        return catalog;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            lock (stateLock)
            {
                desiredConnection = false;
                unityConnected = false;
            }

            await SendZeroStepBestEffortAsync(cancellationToken);
            await StopLoopAsync();
            await BroadcastStatusAsync(cancellationToken);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task<CommandResponse> SendCommandAsync(
        string command,
        string source,
        string? agentId,
        string? clientId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(command))
        {
            return new CommandResponse(false, "Command is empty");
        }

        var accepted = ApplyCommand(command, agentId, clientId);
        if (!accepted)
        {
            await sessionLogger.WriteAsync(
                "command.ignored",
                new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, command, source, mode = Mode, agentId, commandClientId = clientId, reason = "control-owned-by-another-client-or-no-agent" },
                cancellationToken);
            return new CommandResponse(false, "Command ignored: control is owned by another UI tab or target agent is not selected");
        }

        await sessionLogger.WriteAsync(
            "command.outgoing",
            new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, command, source, mode = Mode, agentId, commandClientId = clientId },
            cancellationToken);
        await BroadcastStatusAsync(cancellationToken);
        return new CommandResponse(true);
    }

    public async Task RegisterUiConnectionAsync(string connectionId)
    {
        lock (stateLock)
        {
            uiConnectedClients++;
        }

        await sessionLogger.WriteAsync(
            "ui.connected",
            new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, connectionId, mode = Mode, uiConnectedClients });
        await BroadcastStatusAsync();
    }

    public async Task UnregisterUiConnectionAsync(string connectionId)
    {
        lock (stateLock)
        {
            uiConnectedClients = Math.Max(0, uiConnectedClients - 1);
        }

        await sessionLogger.WriteAsync(
            "ui.disconnected",
            new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, connectionId, mode = Mode, uiConnectedClients });
        if (uiConnectedClients == 0)
        {
            ResetDriveState();
            lock (stateLock)
            {
                agentControlOwners.Clear();
            }
        }

        await BroadcastStatusAsync();
    }

    public Task UpdateLatencyAsync(long? latency)
    {
        lock (stateLock)
        {
            latencyMs = latency;
        }

        return Task.CompletedTask;
    }

    public async Task BroadcastStatusAsync(CancellationToken cancellationToken = default)
    {
        await ResolveHubClients().SendAsync("status", GetStatus(), cancellationToken);
    }

    public CameraStatusDto GetCameraStatus()
    {
        lock (stateLock)
        {
            return new CameraStatusDto(
                UdpListenerEnabled: false,
                UdpListenPort: 0,
                HasFrame: latestFrame is not null,
                LastFrameAt: lastFrameAt,
                Source: lastSourceUrl,
                FramesReceived: framesReceived,
                BytesReceived: bytesReceived,
                HttpProbeCandidates: Array.Empty<string>(),
                HttpDiscoveredStreams: Array.Empty<string>());
        }
    }

    public bool TryGetLatestFrame(out byte[] frame, out string contentType, out long version, out DateTimeOffset? timestamp) =>
        TryGetLatestFrame(null, out frame, out contentType, out version, out timestamp);

    public bool TryGetLatestFrame(string? agentId, out byte[] frame, out string contentType, out long version, out DateTimeOffset? timestamp)
    {
        lock (stateLock)
        {
            if (!string.IsNullOrWhiteSpace(agentId) &&
                agentFrameStates.TryGetValue(agentId.Trim(), out var agentFrame) &&
                agentFrame.Bytes is { Length: > 0 })
            {
                frame = agentFrame.Bytes.ToArray();
                contentType = "image/jpeg";
                version = agentFrame.Version;
                timestamp = agentFrame.Timestamp;
                return true;
            }

            if (latestFrame is null)
            {
                frame = Array.Empty<byte>();
                contentType = "image/jpeg";
                version = frameVersion;
                timestamp = lastFrameAt;
                return false;
            }

            frame = latestFrame.ToArray();
            contentType = "image/jpeg";
            version = frameVersion;
            timestamp = lastFrameAt;
            return true;
        }
    }

    public SensorBridgeStatusDto GetSensorStatus()
    {
        lock (stateLock)
        {
            return new SensorBridgeStatusDto(
                Enabled: true,
                EndpointUrl: lastSourceUrl,
                PollIntervalMs: 80,
                HasTelemetry: latestTelemetry is not null,
                LastTelemetryAt: lastTelemetryAt,
                LastSuccessAt: lastSuccessAt,
                LastError: lastError,
                ConsecutiveFailures: consecutiveFailures);
        }
    }

    public SensorTelemetryDto? GetLatestSensorTelemetry()
    {
        lock (stateLock)
        {
            return latestTelemetry;
        }
    }

    public Task<SensorBridgeResponse> UpdateConfigAsync(
        bool? autoScanEnabled,
        int? sampleIntervalMs,
        double? scanIntervalSec,
        int? scanSettleMs,
        int? driveSpeedPercent,
        int? cameraSpeedPercent,
        int? ultrasonicServoPin,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = sampleIntervalMs;
        _ = scanIntervalSec;
        _ = scanSettleMs;

        lock (stateLock)
        {
            if (autoScanEnabled.HasValue)
            {
                ultrasonicAutoScanEnabled = autoScanEnabled.Value;
            }

            if (driveSpeedPercent.HasValue)
            {
                this.driveSpeedPercent = Math.Clamp(driveSpeedPercent.Value, 0, 100);
            }

            if (cameraSpeedPercent.HasValue)
            {
                this.cameraSpeedPercent = Math.Clamp(cameraSpeedPercent.Value, 0, 100);
            }

            if (ultrasonicServoPin.HasValue)
            {
                this.ultrasonicServoPin = ultrasonicServoPin.Value;
            }
        }

        return Task.FromResult(new SensorBridgeResponse(true, 200, "{\"mode\":\"unity-sim\"}"));
    }

    public Task<SensorBridgeResponse> SetUltrasonicPositionAsync(int angleDeg, bool disableAutoScan, int? servoPin, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (stateLock)
        {
            ultrasonicAngleDeg = Math.Clamp(angleDeg, 0, 180);
            if (disableAutoScan)
            {
                ultrasonicAutoScanEnabled = false;
            }

            if (servoPin.HasValue)
            {
                ultrasonicServoPin = servoPin.Value;
            }
        }

        return Task.FromResult(new SensorBridgeResponse(true, 200, "{\"mode\":\"unity-sim\"}"));
    }

    public Task<SensorBridgeResponse> SetUltrasonicAutoScanAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (stateLock)
        {
            ultrasonicAutoScanEnabled = enabled;
        }

        return Task.FromResult(new SensorBridgeResponse(true, 200, "{\"mode\":\"unity-sim\"}"));
    }

    private async Task ConnectCoreAsync(string? host, int? port, CancellationToken cancellationToken)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(host))
            {
                targetHost = NormalizeTargetHost(host.Trim());
            }

            if (port is >= 1 and <= 65535)
            {
                targetPort = port.Value;
            }

            lock (stateLock)
            {
                desiredConnection = true;
                unityConnected = false;
                lastError = null;
            }

            await ProbeHealthAsync(cancellationToken);
            await ProbeContractAsync(cancellationToken);
            lock (stateLock)
            {
                ResetInteractiveDefaultsLocked();
            }
            var initial = await ResetSimulationAsync(cancellationToken);
            UpdateFromStepResult(initial, selectedControlAgentId, updateSharedState: true);

            lock (stateLock)
            {
                unityConnected = true;
                lastError = null;
            }

            await StartLoopAsync(cancellationToken);
            await sessionLogger.WriteAsync(
                "unity.connected",
                new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, host = targetHost, port = targetPort },
                cancellationToken);
            await BroadcastStatusAsync(cancellationToken);
        }
        catch
        {
            lock (stateLock)
            {
                desiredConnection = false;
                unityConnected = false;
            }

            throw;
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    private async Task StartLoopAsync(CancellationToken cancellationToken)
    {
        await StopLoopAsync();

        var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        loopCts = localCts;
        loopTask = Task.Run(() => StepLoopAsync(localCts.Token), localCts.Token);
    }

    private async Task StopLoopAsync()
    {
        var cts = loopCts;
        var task = loopTask;
        loopCts = null;
        loopTask = null;

        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
        }

        if (task is not null)
        {
            try { await task; } catch { }
        }

        cts?.Dispose();
    }

    private async Task StepLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                AdvanceAutoScan();
                var agentsSnapshot = GetLoopAgentsSnapshot();
                if (agentsSnapshot.Count == 0)
                {
                    await Task.Delay(80, cancellationToken);
                    continue;
                }

                foreach (var agent in agentsSnapshot)
                {
                    var agentId = ResolveCommandTargetAgentId(agent.AgentId);
                    if (string.IsNullOrWhiteSpace(agentId))
                    {
                        continue;
                    }

                    var commandState = GetAgentControlStateSnapshot(agentId);
                    var result = await StepSimulationAsync(agentId, commandState, cancellationToken);
                    UpdateFromStepResult(result, agentId, updateSharedState: true);
                }

                await ResolveHubClients().SendAsync("sensorTelemetry", GetLatestSensorTelemetry(), cancellationToken);
                await ResolveHubClients().SendAsync("sensorStatus", GetSensorStatus(), cancellationToken);
                await BroadcastStatusAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unity step loop failed");
                lock (stateLock)
                {
                    lastError = $"Unity step failed: {ex.Message}";
                    desiredConnection = false;
                    unityConnected = false;
                    consecutiveFailures++;
                }

                await sessionLogger.WriteAsync(
                    "unity.step_failed",
                    new { clientId = sessionClientId, runtimeMode = sessionRuntimeMode, error = ex.Message });
                await ResolveHubClients().SendAsync("sensorStatus", GetSensorStatus(), cancellationToken);
                await BroadcastStatusAsync(cancellationToken);
                break;
            }

            await Task.Delay(80, cancellationToken);
        }
    }

    private async Task ProbeHealthAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/health", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<UnityRuntimeCatalogDto> ProbeContractAsync(CancellationToken cancellationToken)
    {
        return await ProbeContractForEndpointAsync(targetHost, targetPort, cancellationToken);
    }

    private async Task<UnityRuntimeCatalogDto> ProbeContractForEndpointAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/contract", null, host, port, cancellationToken, updateLastSource: false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var vehicleOptions = new List<UnityRuntimeOptionDto>();
        var trackOptions = new List<UnityRuntimeOptionDto>();
        var availableVehicleIds = new HashSet<string>(StringComparer.Ordinal);
        var availableTrackIds = new HashSet<string>(StringComparer.Ordinal);

        if (document.RootElement.TryGetProperty("availableVehicles", out var vehicles) &&
            vehicles.ValueKind == JsonValueKind.Array)
        {
            foreach (var vehicle in vehicles.EnumerateArray())
            {
                if (!vehicle.TryGetProperty("deviceId", out var deviceIdElement))
                {
                    continue;
                }

                var deviceId = deviceIdElement.GetString();
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }

                if (!availableVehicleIds.Add(deviceId))
                {
                    continue;
                }

                var displayName = vehicle.TryGetProperty("displayName", out var displayNameElement)
                    ? displayNameElement.GetString()
                    : null;
                vehicleOptions.Add(new UnityRuntimeOptionDto(deviceId, string.IsNullOrWhiteSpace(displayName) ? deviceId : displayName!));
            }
        }

        if (document.RootElement.TryGetProperty("availableTracks", out var tracks) &&
            tracks.ValueKind == JsonValueKind.Array)
        {
            foreach (var track in tracks.EnumerateArray())
            {
                if (!track.TryGetProperty("trackId", out var trackIdElement))
                {
                    continue;
                }

                var trackId = trackIdElement.GetString();
                if (string.IsNullOrWhiteSpace(trackId))
                {
                    continue;
                }

                if (!availableTrackIds.Add(trackId))
                {
                    continue;
                }

                var displayName = track.TryGetProperty("displayName", out var displayNameElement)
                    ? displayNameElement.GetString()
                    : null;
                trackOptions.Add(new UnityRuntimeOptionDto(trackId, string.IsNullOrWhiteSpace(displayName) ? trackId : displayName!));
            }
        }

        if (availableVehicleIds.Count == 0)
        {
            throw new InvalidOperationException("Unity simulator contract does not expose any vehicles");
        }

        if (availableTrackIds.Count == 0)
        {
            throw new InvalidOperationException("Unity simulator contract does not expose any tracks");
        }

        var resolvedVehicleId = ResolveSelected(availableVehicleIds, selectedVehicleId, PreferredVehicleIds)
            ?? availableVehicleIds.FirstOrDefault(id => id.StartsWith("vehicle.arcade", StringComparison.Ordinal))
            ?? availableVehicleIds.FirstOrDefault(id => id.StartsWith("vehicle.prometeo", StringComparison.Ordinal))
            ?? availableVehicleIds.First();
        var resolvedTrackId = ResolveSelected(availableTrackIds, selectedTrackId, PreferredTrackIds)
            ?? availableTrackIds.First();

        lock (stateLock)
        {
            selectedVehicleId = resolvedVehicleId;
            selectedTrackId = resolvedTrackId;
            availableVehicles = vehicleOptions;
            availableTracks = trackOptions;
            configuredAgents = NormalizeConfiguredAgents(configuredAgents);
            SyncAgentStateDictionariesLocked(configuredAgents);
            selectedControlAgentId = ResolveSelectedControlAgentId(selectedControlAgentId, configuredAgents);
            var selectedDisplayName = vehicleOptions.FirstOrDefault(option => string.Equals(option.Id, resolvedVehicleId, StringComparison.Ordinal))
                ?.DisplayName;
            if (!string.IsNullOrWhiteSpace(selectedDisplayName))
            {
                runtimeLabel = selectedDisplayName!;
            }
        }

        return BuildCatalogSnapshot();
    }

    private async Task<JsonDocument> ResetSimulationAsync(CancellationToken cancellationToken)
    {
        List<UnityRuntimeAgentSelectionRequest> agentsSnapshot;
        string cameraMode;
        lock (stateLock)
        {
            agentsSnapshot = NormalizeConfiguredAgents(configuredAgents);
            cameraMode = selectedCameraMode;
        }

        var payload = new
        {
            seed = 1,
            timeScale = 1.0,
            selectedTrackId,
            selectedVehicleId,
            trackParams = Array.Empty<object>(),
            vehicleParams = new object[]
            {
                new { key = "camera.mode", value = cameraMode },
                new { key = "camera.profile", value = CameraProfile },
            },
            flags = agentsSnapshot.Count > 1
                ? new object[]
                {
                    new { key = "render.quality_profile", value = RenderQualityProfile },
                    new { key = "agents.see_each_other", value = "true" },
                    new { key = "agents.collisions_enabled", value = "false" },
                    new { key = "agents.allow_empty", value = "false" },
                }
                : new object[]
                {
                    new { key = "render.quality_profile", value = RenderQualityProfile },
                    new { key = "agents.allow_empty", value = agentsSnapshot.Count == 0 ? "true" : "false" },
                },
            agents = agentsSnapshot.Select((agent, index) => new
            {
                agentId = string.IsNullOrWhiteSpace(agent.AgentId) ? $"agent-{index + 1}" : agent.AgentId,
                vehicleId = agent.VehicleId,
                isPrimary = agent.IsPrimary || index == 0,
                trackParams = Array.Empty<object>(),
                vehicleParams = new object[]
                {
                    new { key = "camera.mode", value = cameraMode },
                    new { key = "camera.profile", value = CameraProfile },
                },
                flags = Array.Empty<object>(),
            }).ToArray(),
        };

        using var response = await SendAsync(HttpMethod.Post, "/reset", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<JsonDocument> StepSimulationAsync(
        string agentId,
        AgentControlState commandState,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            throttle = 0.0,
            steer = 0.0,
            brake = commandState.BrakeNorm,
            targetAgentId = agentId,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            timeBase = "unix_ms",
            extensions = new object[]
            {
                new { key = "drive.left_pwm_norm", value = commandState.LeftPwmNorm.ToString("0.000000", CultureInfo.InvariantCulture) },
                new { key = "drive.right_pwm_norm", value = commandState.RightPwmNorm.ToString("0.000000", CultureInfo.InvariantCulture) },
                new { key = "camera.pan_norm", value = NormalizeServo(commandState.CameraPanDeg).ToString("0.000000", CultureInfo.InvariantCulture) },
                new { key = "camera.tilt_norm", value = NormalizeServo(commandState.CameraTiltDeg).ToString("0.000000", CultureInfo.InvariantCulture) },
            },
        };

        using var response = await SendAsync(HttpMethod.Post, "/step", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        return await SendAsync(method, path, payload, targetHost, targetPort, cancellationToken, updateLastSource: true);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? payload,
        string host,
        int port,
        CancellationToken cancellationToken,
        bool updateLastSource)
    {
        var client = httpClientFactory.CreateClient(nameof(UnityKs0223RuntimeProvider));
        client.Timeout = TimeSpan.FromSeconds(5);
        var url = $"http://{host}:{port}{path}";

        var request = new HttpRequestMessage(method, url);
        var hostHeader = GetHostHeaderOverride(host, port);
        if (hostHeader is not null)
        {
            request.Headers.Host = hostHeader;
        }

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        if (updateLastSource)
        {
            lastSourceUrl = url;
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private void UpdateFromStepResult(JsonDocument document, string agentId, bool updateSharedState)
    {
        var now = DateTimeOffset.UtcNow;
        var root = document.RootElement;
        var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("state", out var state) &&
            state.TryGetProperty("telemetry", out var telemetry) &&
            telemetry.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in telemetry.EnumerateArray())
            {
                if (!item.TryGetProperty("key", out var keyElement) || !item.TryGetProperty("value", out var valueElement))
                {
                    continue;
                }

                var key = keyElement.GetString();
                var value = valueElement.GetString();
                if (string.IsNullOrWhiteSpace(key) || value is null)
                {
                    continue;
                }

                flat[key] = value;
            }
        }

        AddUiFriendlyTelemetry(flat, agentId);
        var telemetryDto = new SensorTelemetryDto(now, lastSourceUrl ?? string.Empty, root.GetRawText(), flat);

        lock (stateLock)
        {
            lastSuccessAt = now;
            lastLoopAt = now;
            unityConnected = true;
            consecutiveFailures = 0;
            lastError = null;

            if (updateSharedState)
            {
                latestTelemetry = telemetryDto;
                lastTelemetryAt = now;
                hasTelemetry = flat.Count > 0;
            }
        }

        if (root.TryGetProperty("frame", out var frame) &&
            frame.TryGetProperty("dataBase64", out var dataBase64Element))
        {
            var dataBase64 = dataBase64Element.GetString();
            if (!string.IsNullOrWhiteSpace(dataBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(dataBase64);
                    lock (stateLock)
                    {
                        var frameState = GetOrCreateAgentFrameStateLocked(agentId);
                        frameState.Bytes = bytes;
                        frameState.Timestamp = now;
                        frameState.Version++;
                        framesReceived++;
                        bytesReceived += bytes.Length;

                        if (updateSharedState)
                        {
                            latestFrame = bytes;
                            lastFrameAt = now;
                            frameVersion++;
                        }
                    }
                }
                catch
                {
                    // ignore malformed frame payload
                }
            }
        }
    }

    private void AddUiFriendlyTelemetry(IDictionary<string, string> flat, string agentId)
    {
        var commandState = GetAgentControlStateSnapshot(agentId);

        flat["drive.left_pwm_norm"] = commandState.LeftPwmNorm.ToString("0.###", CultureInfo.InvariantCulture);
        flat["drive.right_pwm_norm"] = commandState.RightPwmNorm.ToString("0.###", CultureInfo.InvariantCulture);
        flat["control.brake_norm"] = commandState.BrakeNorm.ToString("0.###", CultureInfo.InvariantCulture);
        flat["config.auto_scan_enabled"] = ultrasonicAutoScanEnabled ? "true" : "false";
        flat["config.ultrasonic_servo_pin"] = ultrasonicServoPin.ToString(CultureInfo.InvariantCulture);
        flat["ultrasonic.scan_servo_angle_deg"] = ultrasonicAngleDeg.ToString(CultureInfo.InvariantCulture);
        flat["camera.pan_deg"] = commandState.CameraPanDeg.ToString(CultureInfo.InvariantCulture);
        flat["camera.tilt_deg"] = commandState.CameraTiltDeg.ToString(CultureInfo.InvariantCulture);
        flat["camera.mode"] = selectedCameraMode;
        flat["control.agent_id"] = agentId;

        if (flat.TryGetValue("sensor.ultrasonic.front.m", out var ultrasonicMeters) &&
            float.TryParse(ultrasonicMeters, NumberStyles.Float, CultureInfo.InvariantCulture, out var distanceM))
        {
            var distanceCm = Math.Round(distanceM * 100f).ToString(CultureInfo.InvariantCulture);
            flat["ultrasonic.distance_cm"] = distanceCm;
            flat["ultrasonic.scan.left_cm"] = distanceCm;
            flat["ultrasonic.scan.center_cm"] = distanceCm;
            flat["ultrasonic.scan.right_cm"] = distanceCm;
        }

        flat["tracking.left"] = NormalizeTracking(flat, "sensor.line_tracker.s1_norm");
        flat["tracking.center"] = NormalizeTracking(flat, "sensor.line_tracker.s3_norm");
        flat["tracking.right"] = NormalizeTracking(flat, "sensor.line_tracker.s5_norm");
    }

    private static string NormalizeTracking(IDictionary<string, string> flat, string key)
    {
        if (!flat.TryGetValue(key, out var raw) ||
            !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return "n/a";
        }

        return value >= 0.5f ? "1" : "0";
    }

    private bool ApplyCommand(string command, string? targetAgentId, string? clientId)
    {
        var driveNorm = Math.Clamp(driveSpeedPercent / 100f, 0f, 1f);
        var cameraStep = Math.Max(1, cameraSpeedPercent / 20);

        lock (stateLock)
        {
            var resolvedAgentId = ResolveCommandTargetAgentId(targetAgentId);
            if (string.IsNullOrWhiteSpace(resolvedAgentId))
            {
                return false;
            }

            _ = clientId;

            var commandState = GetOrCreateAgentControlStateLocked(resolvedAgentId);
            var now = DateTimeOffset.UtcNow;
            switch (command)
            {
                case "DirForward":
                    commandState.LeftPwmNorm = driveNorm;
                    commandState.RightPwmNorm = driveNorm;
                    commandState.BrakeNorm = 0f;
                    commandState.LastDriveInputAt = now;
                    break;
                case "DirBack":
                    commandState.LeftPwmNorm = -driveNorm;
                    commandState.RightPwmNorm = -driveNorm;
                    commandState.BrakeNorm = 0f;
                    commandState.LastDriveInputAt = now;
                    break;
                case "DirLeft":
                    commandState.LeftPwmNorm = driveNorm;
                    commandState.RightPwmNorm = -driveNorm;
                    commandState.BrakeNorm = 0f;
                    commandState.LastDriveInputAt = now;
                    break;
                case "DirRight":
                    commandState.LeftPwmNorm = -driveNorm;
                    commandState.RightPwmNorm = driveNorm;
                    commandState.BrakeNorm = 0f;
                    commandState.LastDriveInputAt = now;
                    break;
                case "DirStop":
                    ResetDriveStateLocked(resolvedAgentId);
                    commandState.LastDriveInputAt = now;
                    break;
                case "CamUp":
                    commandState.CameraTiltDeg = Math.Clamp(commandState.CameraTiltDeg - cameraStep, 0, 180);
                    break;
                case "CamDown":
                    commandState.CameraTiltDeg = Math.Clamp(commandState.CameraTiltDeg + cameraStep, 0, 180);
                    break;
                case "CamLeft":
                    commandState.CameraPanDeg = Math.Clamp(commandState.CameraPanDeg + cameraStep, 0, 180);
                    break;
                case "CamRight":
                    commandState.CameraPanDeg = Math.Clamp(commandState.CameraPanDeg - cameraStep, 0, 180);
                    break;
                case "CamStop":
                    break;
            }

            if (string.Equals(resolvedAgentId, selectedControlAgentId, StringComparison.Ordinal))
            {
                leftPwmNorm = commandState.LeftPwmNorm;
                rightPwmNorm = commandState.RightPwmNorm;
                brakeNorm = commandState.BrakeNorm;
                cameraPanDeg = commandState.CameraPanDeg;
                cameraTiltDeg = commandState.CameraTiltDeg;
            }
        }

        return true;
    }

    private void ResetDriveState()
    {
        lock (stateLock)
        {
            foreach (var agentId in agentControlStates.Keys.ToArray())
            {
                ResetDriveStateLocked(agentId);
            }

            leftPwmNorm = 0f;
            rightPwmNorm = 0f;
            brakeNorm = 0f;
        }
    }

    private List<UnityRuntimeAgentSelectionRequest> GetLoopAgentsSnapshot()
    {
        lock (stateLock)
        {
            return NormalizeConfiguredAgents(configuredAgents);
        }
    }

    private AgentControlState GetAgentControlStateSnapshot(string agentId)
    {
        lock (stateLock)
        {
            var state = GetOrCreateAgentControlStateLocked(agentId);
            var now = DateTimeOffset.UtcNow;
            if ((state.LeftPwmNorm != 0f || state.RightPwmNorm != 0f) &&
                now - state.LastDriveInputAt > DriveInputWatchdog)
            {
                state.LeftPwmNorm = 0f;
                state.RightPwmNorm = 0f;
                state.BrakeNorm = 0f;
            }

            return new AgentControlState
            {
                LeftPwmNorm = state.LeftPwmNorm,
                RightPwmNorm = state.RightPwmNorm,
                BrakeNorm = state.BrakeNorm,
                CameraPanDeg = state.CameraPanDeg,
                CameraTiltDeg = state.CameraTiltDeg,
                LastDriveInputAt = state.LastDriveInputAt,
            };
        }
    }

    private string? ResolveCommandTargetAgentId(string? targetAgentId)
    {
        if (!string.IsNullOrWhiteSpace(targetAgentId))
        {
            return targetAgentId.Trim();
        }

        return string.IsNullOrWhiteSpace(selectedControlAgentId) ? null : selectedControlAgentId;
    }

    private AgentControlState GetOrCreateAgentControlStateLocked(string agentId)
    {
        var normalizedAgentId = string.IsNullOrWhiteSpace(agentId) ? "agent-1" : agentId.Trim();
        if (!agentControlStates.TryGetValue(normalizedAgentId, out var state))
        {
            state = new AgentControlState
            {
                CameraPanDeg = cameraPanDeg,
                CameraTiltDeg = cameraTiltDeg,
            };
            agentControlStates[normalizedAgentId] = state;
        }

        return state;
    }

    private AgentFrameState GetOrCreateAgentFrameStateLocked(string agentId)
    {
        var normalizedAgentId = string.IsNullOrWhiteSpace(agentId) ? "agent-1" : agentId.Trim();
        if (!agentFrameStates.TryGetValue(normalizedAgentId, out var state))
        {
            state = new AgentFrameState();
            agentFrameStates[normalizedAgentId] = state;
        }

        return state;
    }

    private void ResetDriveStateLocked(string agentId)
    {
        var state = GetOrCreateAgentControlStateLocked(agentId);
        state.LeftPwmNorm = 0f;
        state.RightPwmNorm = 0f;
        state.BrakeNorm = 0f;
        state.LastDriveInputAt = DateTimeOffset.UtcNow;
    }

    private void SyncAgentStateDictionariesLocked(IReadOnlyList<UnityRuntimeAgentSelectionRequest> agents)
    {
        var normalizedAgentIds = new HashSet<string>(
            (agents ?? Array.Empty<UnityRuntimeAgentSelectionRequest>())
                .Select(agent => string.IsNullOrWhiteSpace(agent.AgentId) ? string.Empty : agent.AgentId!.Trim())
                .Where(agentId => !string.IsNullOrWhiteSpace(agentId)),
            StringComparer.Ordinal);

        foreach (var agentId in normalizedAgentIds)
        {
            _ = GetOrCreateAgentControlStateLocked(agentId);
            _ = GetOrCreateAgentFrameStateLocked(agentId);
        }

        foreach (var staleAgentId in agentControlStates.Keys.Where(id => !normalizedAgentIds.Contains(id)).ToArray())
        {
            agentControlStates.Remove(staleAgentId);
        }

        foreach (var staleAgentId in agentFrameStates.Keys.Where(id => !normalizedAgentIds.Contains(id)).ToArray())
        {
            agentFrameStates.Remove(staleAgentId);
        }

        foreach (var staleAgentId in agentControlOwners.Keys.Where(id => !normalizedAgentIds.Contains(id)).ToArray())
        {
            agentControlOwners.Remove(staleAgentId);
        }
    }

    private void ResetInteractiveDefaultsLocked()
    {
        selectedVehicleId = PreferredVehicleIds[0];
        selectedTrackId = PreferredTrackIds[0];
        selectedControlAgentId = string.Empty;
        selectedCameraMode = "spectator";
        ultrasonicAutoScanEnabled = false;
        configuredAgents = NormalizeConfiguredAgents(Array.Empty<UnityRuntimeAgentSelectionRequest>());
        SyncAgentStateDictionariesLocked(configuredAgents);
        ResetDriveState();
    }

    private bool TryAcquireControlOwnershipLocked(string agentId, string command, string? clientId)
    {
        var normalizedClientId = NormalizeClientId(clientId);
        if (string.IsNullOrWhiteSpace(normalizedClientId))
        {
            return true;
        }

        var state = GetOrCreateAgentControlOwnerLocked(agentId);
        state.ClientId = normalizedClientId;
        state.LeaseUntil = DateTimeOffset.UtcNow + ControlOwnershipLease;
        _ = command;
        return true;
    }

    private AgentControlOwnerState GetOrCreateAgentControlOwnerLocked(string agentId)
    {
        var normalizedAgentId = string.IsNullOrWhiteSpace(agentId) ? "agent-1" : agentId.Trim();
        if (!agentControlOwners.TryGetValue(normalizedAgentId, out var state))
        {
            state = new AgentControlOwnerState();
            agentControlOwners[normalizedAgentId] = state;
        }

        return state;
    }

    private static bool IsStopLikeCommand(string command) =>
        string.Equals(command, "DirStop", StringComparison.Ordinal) ||
        string.Equals(command, "CamStop", StringComparison.Ordinal);

    private static string? NormalizeClientId(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        return clientId.Trim();
    }

    private static string NormalizeCameraMode(string? mode)
    {
        var normalized = mode?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "bumper" => "bumper",
            "chase" => "chase",
            "spectator" => "spectator",
            _ => "driver",
        };
    }

    private static List<UnityRuntimeAgentSelectionRequest> NormalizeConfiguredAgents(
        IReadOnlyList<UnityRuntimeAgentSelectionRequest>? configured)
    {
        var result = new List<UnityRuntimeAgentSelectionRequest>();

        if (configured is not null)
        {
            var index = 1;
            var usedAgentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in configured)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.VehicleId))
                {
                    continue;
                }

                var agentId = string.IsNullOrWhiteSpace(item.AgentId) ? $"agent-{index}" : item.AgentId!.Trim();
                var vehicleId = item.VehicleId!.Trim();
                if (string.IsNullOrWhiteSpace(agentId) || usedAgentIds.Contains(agentId))
                {
                    index++;
                    continue;
                }

                usedAgentIds.Add(agentId);
                result.Add(new UnityRuntimeAgentSelectionRequest(agentId, vehicleId, item.IsPrimary));
                index++;
            }
        }

        if (result.Count > 0 && !result.Any(agent => agent.IsPrimary))
        {
            var first = result[0];
            result[0] = first with { IsPrimary = true };
        }

        return result;
    }

    private static string ResolveSelectedControlAgentId(string? current, IReadOnlyList<UnityRuntimeAgentSelectionRequest> agents)
    {
        if (!string.IsNullOrWhiteSpace(current) &&
            agents.Any(agent => string.Equals(agent.AgentId, current, StringComparison.Ordinal)))
        {
            return current!;
        }

        return agents.FirstOrDefault(agent => agent.IsPrimary)?.AgentId
            ?? agents.FirstOrDefault()?.AgentId
            ?? string.Empty;
    }

    private void AdvanceAutoScan()
    {
        lock (stateLock)
        {
            if (!ultrasonicAutoScanEnabled)
            {
                return;
            }

            ultrasonicAngleDeg += autoScanDirection * 4;
            if (ultrasonicAngleDeg >= 150)
            {
                ultrasonicAngleDeg = 150;
                autoScanDirection = -1;
            }
            else if (ultrasonicAngleDeg <= 30)
            {
                ultrasonicAngleDeg = 30;
                autoScanDirection = 1;
            }
        }
    }

    private async Task SendZeroStepBestEffortAsync(CancellationToken cancellationToken)
    {
        try
        {
            ResetDriveState();
            if (!string.IsNullOrWhiteSpace(targetHost))
            {
                foreach (var agent in GetLoopAgentsSnapshot())
                {
                    var agentId = ResolveCommandTargetAgentId(agent.AgentId);
                    if (string.IsNullOrWhiteSpace(agentId))
                    {
                        continue;
                    }

                    using var result = await StepSimulationAsync(agentId, GetAgentControlStateSnapshot(agentId), cancellationToken);
                    _ = result;
                }
            }
        }
        catch
        {
            // best effort
        }
    }

    private static string? ResolvePreferred(HashSet<string> availableIds, IEnumerable<string> preferredIds)
    {
        foreach (var candidate in preferredIds)
        {
            if (availableIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveSelected(HashSet<string> availableIds, string? selectedId, IEnumerable<string> preferredIds)
    {
        if (!string.IsNullOrWhiteSpace(selectedId) && availableIds.Contains(selectedId))
        {
            return selectedId;
        }

        return ResolvePreferred(availableIds, preferredIds);
    }

    private UnityRuntimeCatalogDto BuildCatalogSnapshot()
    {
        lock (stateLock)
        {
            var normalizedAgents = NormalizeConfiguredAgents(configuredAgents);
            return new UnityRuntimeCatalogDto(
                SelectedTrackId: selectedTrackId,
                SelectedVehicleId: selectedVehicleId,
                SelectedCameraMode: selectedCameraMode,
                SelectedControlAgentId: ResolveSelectedControlAgentId(selectedControlAgentId, normalizedAgents),
                SelectedCameraAgentId: ResolveSelectedControlAgentId(selectedControlAgentId, normalizedAgents),
                Tracks: availableTracks,
                Vehicles: availableVehicles,
                Agents: normalizedAgents.Select(agent =>
                {
                    var displayName = availableVehicles.FirstOrDefault(option => string.Equals(option.Id, agent.VehicleId, StringComparison.Ordinal))?.DisplayName
                        ?? agent.VehicleId
                        ?? string.Empty;
                    return new UnityRuntimeAgentDto(
                        AgentId: agent.AgentId ?? string.Empty,
                        VehicleId: agent.VehicleId ?? string.Empty,
                        DisplayName: displayName,
                        IsPrimary: agent.IsPrimary);
                }).ToArray());
        }
    }

    private static float NormalizeServo(int angleDeg) => Math.Clamp((angleDeg - 90f) / 90f, -1f, 1f);

    private static string NormalizeTargetHost(string host)
    {
        if (!IsRunningInContainer())
        {
            return host;
        }

        return host switch
        {
            "127.0.0.1" or "localhost" or "::1" => "host.docker.internal",
            _ => host,
        };
    }

    private static bool IsRunningInContainer()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        if (string.Equals(env, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(env, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return File.Exists("/.dockerenv");
    }

    private static string? GetHostHeaderOverride(string host, int port)
    {
        if (!IsRunningInContainer())
        {
            return null;
        }

        if (!string.Equals(host, "host.docker.internal", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"127.0.0.1:{port}";
    }

    private IClientProxy ResolveHubClients() =>
        string.IsNullOrWhiteSpace(clientGroup)
            ? hubContext.Clients.All
            : hubContext.Clients.Group(clientGroup);
}
