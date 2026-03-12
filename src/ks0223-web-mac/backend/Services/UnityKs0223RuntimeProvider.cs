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
    private static readonly string[] PreferredVehicleIds =
    {
        "vehicle.ks0223.arcade.blue.v1",
        "vehicle.ks0223.v1",
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

    private readonly object stateLock = new();
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private readonly SessionLogger sessionLogger;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IHubContext<TelemetryHub> hubContext;
    private readonly ILogger<UnityKs0223RuntimeProvider> logger;

    private bool desiredConnection;
    private bool unityConnected;
    private int uiConnectedClients;
    private string runtimeLabel = "Keyestudio KS0223 (Unity Simulator)";
    private string targetHost = "127.0.0.1";
    private int targetPort = 8000;
    private string selectedVehicleId = PreferredVehicleIds[0];
    private string selectedTrackId = PreferredTrackIds[0];
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
    private bool ultrasonicAutoScanEnabled = true;
    private int cameraPanDeg = 90;
    private int cameraTiltDeg = 90;
    private int autoScanDirection = 1;
    private float leftPwmNorm;
    private float rightPwmNorm;
    private float brakeNorm;
    private CancellationTokenSource? loopCts;
    private Task? loopTask;
    private string? lastSourceUrl;

    public UnityKs0223RuntimeProvider(
        SessionLogger sessionLogger,
        IHttpClientFactory httpClientFactory,
        IHubContext<TelemetryHub> hubContext,
        ILogger<UnityKs0223RuntimeProvider> logger)
    {
        this.sessionLogger = sessionLogger;
        this.httpClientFactory = httpClientFactory;
        this.hubContext = hubContext;
        this.logger = logger;
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

    public async Task<CommandResponse> SendCommandAsync(string command, string source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(command))
        {
            return new CommandResponse(false, "Command is empty");
        }

        ApplyCommand(command);
        await sessionLogger.WriteAsync("command.outgoing", new { command, source, mode = Mode }, cancellationToken);
        await BroadcastStatusAsync(cancellationToken);
        return new CommandResponse(true);
    }

    public async Task RegisterUiConnectionAsync(string connectionId)
    {
        lock (stateLock)
        {
            uiConnectedClients++;
        }

        await sessionLogger.WriteAsync("ui.connected", new { connectionId, mode = Mode, uiConnectedClients });
        await BroadcastStatusAsync();
    }

    public async Task UnregisterUiConnectionAsync(string connectionId)
    {
        lock (stateLock)
        {
            uiConnectedClients = Math.Max(0, uiConnectedClients - 1);
        }

        await sessionLogger.WriteAsync("ui.disconnected", new { connectionId, mode = Mode, uiConnectedClients });
        if (uiConnectedClients == 0)
        {
            ResetDriveState();
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
        await hubContext.Clients.All.SendAsync("status", GetStatus(), cancellationToken);
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

    public bool TryGetLatestFrame(out byte[] frame, out string contentType, out long version, out DateTimeOffset? timestamp)
    {
        lock (stateLock)
        {
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
            var initial = await ResetSimulationAsync(cancellationToken);
            UpdateFromStepResult(initial);

            lock (stateLock)
            {
                unityConnected = true;
                lastError = null;
            }

            await StartLoopAsync(cancellationToken);
            await sessionLogger.WriteAsync("unity.connected", new { host = targetHost, port = targetPort }, cancellationToken);
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
                var result = await StepSimulationAsync(cancellationToken);
                UpdateFromStepResult(result);
                await hubContext.Clients.All.SendAsync("sensorTelemetry", GetLatestSensorTelemetry(), cancellationToken);
                await hubContext.Clients.All.SendAsync("sensorStatus", GetSensorStatus(), cancellationToken);
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

                await sessionLogger.WriteAsync("unity.step_failed", new { error = ex.Message });
                await hubContext.Clients.All.SendAsync("sensorStatus", GetSensorStatus(), cancellationToken);
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

    private async Task ProbeContractAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/contract", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var availableVehicleIds = new HashSet<string>(StringComparer.Ordinal);
        var availableTrackIds = new HashSet<string>(StringComparer.Ordinal);
        var vehicleDisplayNames = new Dictionary<string, string>(StringComparer.Ordinal);

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

                availableVehicleIds.Add(deviceId);

                if (vehicle.TryGetProperty("displayName", out var displayNameElement))
                {
                    var displayName = displayNameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        vehicleDisplayNames[deviceId] = displayName!;
                    }
                }
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

                availableTrackIds.Add(trackId);
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

        var resolvedVehicleId = ResolvePreferred(availableVehicleIds, PreferredVehicleIds)
            ?? availableVehicleIds.FirstOrDefault(id => id.StartsWith("vehicle.ks0223", StringComparison.Ordinal))
            ?? availableVehicleIds.First();
        var resolvedTrackId = ResolvePreferred(availableTrackIds, PreferredTrackIds)
            ?? availableTrackIds.First();

        selectedVehicleId = resolvedVehicleId;
        selectedTrackId = resolvedTrackId;

        if (vehicleDisplayNames.TryGetValue(resolvedVehicleId, out var selectedDisplayName))
        {
            runtimeLabel = selectedDisplayName;
        }
    }

    private async Task<JsonDocument> ResetSimulationAsync(CancellationToken cancellationToken)
    {
        var payload = new
        {
            seed = 1,
            timeScale = 1.0,
            selectedTrackId,
            selectedVehicleId,
            trackParams = Array.Empty<object>(),
            vehicleParams = Array.Empty<object>(),
            flags = Array.Empty<object>(),
        };

        using var response = await SendAsync(HttpMethod.Post, "/reset", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<JsonDocument> StepSimulationAsync(CancellationToken cancellationToken)
    {
        float left;
        float right;
        float brake;
        int pan;
        int tilt;

        lock (stateLock)
        {
            left = leftPwmNorm;
            right = rightPwmNorm;
            brake = brakeNorm;
            pan = cameraPanDeg;
            tilt = cameraTiltDeg;
        }

        var payload = new
        {
            throttle = 0.0,
            steer = 0.0,
            brake,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            timeBase = "unix_ms",
            extensions = new object[]
            {
                new { key = "drive.left_pwm_norm", value = left.ToString("0.000000", CultureInfo.InvariantCulture) },
                new { key = "drive.right_pwm_norm", value = right.ToString("0.000000", CultureInfo.InvariantCulture) },
                new { key = "camera.pan_norm", value = NormalizeServo(pan).ToString("0.000000", CultureInfo.InvariantCulture) },
                new { key = "camera.tilt_norm", value = NormalizeServo(tilt).ToString("0.000000", CultureInfo.InvariantCulture) },
            },
        };

        using var response = await SendAsync(HttpMethod.Post, "/step", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(UnityKs0223RuntimeProvider));
        client.Timeout = TimeSpan.FromSeconds(5);
        var url = $"http://{targetHost}:{targetPort}{path}";

        var request = new HttpRequestMessage(method, url);
        var hostHeader = GetHostHeaderOverride(targetHost, targetPort);
        if (hostHeader is not null)
        {
            request.Headers.Host = hostHeader;
        }

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        lastSourceUrl = url;
        return await client.SendAsync(request, cancellationToken);
    }

    private void UpdateFromStepResult(JsonDocument document)
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

        AddUiFriendlyTelemetry(flat);
        var telemetryDto = new SensorTelemetryDto(now, lastSourceUrl ?? string.Empty, root.GetRawText(), flat);

        lock (stateLock)
        {
            latestTelemetry = telemetryDto;
            lastTelemetryAt = now;
            lastSuccessAt = now;
            lastLoopAt = now;
            hasTelemetry = flat.Count > 0;
            unityConnected = true;
            consecutiveFailures = 0;
            lastError = null;
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
                        latestFrame = bytes;
                        lastFrameAt = now;
                        frameVersion++;
                        framesReceived++;
                        bytesReceived += bytes.Length;
                    }
                }
                catch
                {
                    // ignore malformed frame payload
                }
            }
        }
    }

    private void AddUiFriendlyTelemetry(IDictionary<string, string> flat)
    {
        flat["config.auto_scan_enabled"] = ultrasonicAutoScanEnabled ? "true" : "false";
        flat["config.ultrasonic_servo_pin"] = ultrasonicServoPin.ToString(CultureInfo.InvariantCulture);
        flat["ultrasonic.scan_servo_angle_deg"] = ultrasonicAngleDeg.ToString(CultureInfo.InvariantCulture);
        flat["camera.pan_deg"] = cameraPanDeg.ToString(CultureInfo.InvariantCulture);
        flat["camera.tilt_deg"] = cameraTiltDeg.ToString(CultureInfo.InvariantCulture);

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

    private void ApplyCommand(string command)
    {
        var driveNorm = Math.Clamp(driveSpeedPercent / 100f, 0f, 1f);
        var cameraStep = Math.Max(1, cameraSpeedPercent / 20);

        lock (stateLock)
        {
            switch (command)
            {
                case "DirForward":
                    leftPwmNorm = driveNorm;
                    rightPwmNorm = driveNorm;
                    brakeNorm = 0f;
                    break;
                case "DirBack":
                    leftPwmNorm = -driveNorm;
                    rightPwmNorm = -driveNorm;
                    brakeNorm = 0f;
                    break;
                case "DirLeft":
                    leftPwmNorm = driveNorm;
                    rightPwmNorm = -driveNorm;
                    brakeNorm = 0f;
                    break;
                case "DirRight":
                    leftPwmNorm = -driveNorm;
                    rightPwmNorm = driveNorm;
                    brakeNorm = 0f;
                    break;
                case "DirStop":
                    ResetDriveState();
                    break;
                case "CamUp":
                    cameraTiltDeg = Math.Clamp(cameraTiltDeg - cameraStep, 0, 180);
                    break;
                case "CamDown":
                    cameraTiltDeg = Math.Clamp(cameraTiltDeg + cameraStep, 0, 180);
                    break;
                case "CamLeft":
                    cameraPanDeg = Math.Clamp(cameraPanDeg + cameraStep, 0, 180);
                    break;
                case "CamRight":
                    cameraPanDeg = Math.Clamp(cameraPanDeg - cameraStep, 0, 180);
                    break;
                case "CamStop":
                    break;
            }
        }
    }

    private void ResetDriveState()
    {
        leftPwmNorm = 0f;
        rightPwmNorm = 0f;
        brakeNorm = 0f;
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
                using var result = await StepSimulationAsync(cancellationToken);
                _ = result;
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
}
