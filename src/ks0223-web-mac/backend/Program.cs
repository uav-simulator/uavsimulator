using Ks0223.Web.Backend.Hubs;
using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Options;
using Ks0223.Web.Backend.Services;
using System.Text.Json;
var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:5058");
}

builder.Services.Configure<PiConnectionOptions>(builder.Configuration.GetSection("PiConnection"));
builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection("SessionLogs"));
builder.Services.Configure<CameraOptions>(builder.Configuration.GetSection("Camera"));
builder.Services.Configure<SensorBridgeOptions>(builder.Configuration.GetSection("SensorBridge"));

builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", cors =>
        cors.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.AddSingleton<SessionLogger>();
builder.Services.AddSingleton<TelemetryParser>();
builder.Services.AddSingleton<PiTcpClientService>();
builder.Services.AddSingleton<RealKs0223RuntimeProvider>();
builder.Services.AddSingleton<UnityKs0223RuntimeProvider>();
builder.Services.AddSingleton<IKs0223RuntimeProvider>(serviceProvider => serviceProvider.GetRequiredService<RealKs0223RuntimeProvider>());
builder.Services.AddSingleton<IKs0223RuntimeProvider>(serviceProvider => serviceProvider.GetRequiredService<UnityKs0223RuntimeProvider>());
builder.Services.AddSingleton<RuntimeControlService>();
builder.Services.AddSingleton<CameraStreamService>();
builder.Services.AddSingleton<SensorBridgeService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<PiTcpClientService>());
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<CameraStreamService>());
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SensorBridgeService>());
builder.Services.AddHostedService<PingMonitorService>();

var app = builder.Build();

app.UseCors("frontend");
var hasStaticFiles = Directory.Exists(app.Environment.WebRootPath);
if (hasStaticFiles)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapGet("/api/status", (RuntimeControlService runtimeControlService) => Results.Ok(runtimeControlService.GetStatus()));
app.MapGet("/api/health", (RuntimeControlService runtimeControlService, CameraStreamService cameraService, SensorBridgeService sensorBridgeService, UnityKs0223RuntimeProvider unityRuntimeProvider) =>
{
    var control = runtimeControlService.GetStatus();
    var camera = control.RuntimeMode == RuntimeModes.UnitySim ? unityRuntimeProvider.GetCameraStatus() : cameraService.GetStatus();
    var sensors = control.RuntimeMode == RuntimeModes.UnitySim ? unityRuntimeProvider.GetSensorStatus() : sensorBridgeService.GetStatus();
    var controlDegraded = control.DesiredConnection && !control.TcpConnected;
    var sensorDegraded = sensors.Enabled && sensors.ConsecutiveFailures >= 3;
    var status = controlDegraded || sensorDegraded ? "degraded" : "ok";
    var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
    return Results.Ok(new HealthDto(status, DateTimeOffset.UtcNow, version, control, camera, sensors));
});

app.MapPost("/api/connection/connect", async (ConnectRequest? request, RuntimeControlService runtimeControlService, CancellationToken cancellationToken) =>
{
    try
    {
        await runtimeControlService.ConnectAsync(request?.RuntimeMode, request?.Host, request?.Port, cancellationToken);
        return Results.Ok(runtimeControlService.GetStatus());
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (NotSupportedException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/connection/disconnect", async (RuntimeControlService runtimeControlService, CancellationToken cancellationToken) =>
{
    await runtimeControlService.DisconnectAsync(cancellationToken);
    return Results.Ok(runtimeControlService.GetStatus());
});

app.MapGet("/api/connection/target", (RuntimeControlService runtimeControlService) => Results.Ok(runtimeControlService.GetConnectionTarget()));
app.MapGet("/api/unity/runtime-catalog", async (string? host, int? port, UnityKs0223RuntimeProvider unityRuntimeProvider, CancellationToken cancellationToken) =>
{
    try
    {
        var catalog = await unityRuntimeProvider.GetRuntimeCatalogAsync(host, port, cancellationToken);
        return Results.Ok(catalog);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
app.MapPost("/api/unity/runtime-selection", async (UnityRuntimeSelectionRequest request, UnityKs0223RuntimeProvider unityRuntimeProvider, CancellationToken cancellationToken) =>
{
    try
    {
        var catalog = await unityRuntimeProvider.SetRuntimeSelectionAsync(
            request.TrackId,
            request.VehicleId,
            request.CameraMode,
            request.ControlAgentId,
            request.Agents,
            request.ApplyImmediately,
            cancellationToken);
        return Results.Ok(catalog);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
app.MapGet("/api/camera/status", (RuntimeControlService runtimeControlService, CameraStreamService service, UnityKs0223RuntimeProvider unityRuntimeProvider) =>
{
    var mode = runtimeControlService.GetCurrentMode();
    return Results.Ok(mode == RuntimeModes.UnitySim ? unityRuntimeProvider.GetCameraStatus() : service.GetStatus());
});
app.MapGet("/api/sensors/status", (RuntimeControlService runtimeControlService, SensorBridgeService service, UnityKs0223RuntimeProvider unityRuntimeProvider) =>
{
    var mode = runtimeControlService.GetCurrentMode();
    return Results.Ok(mode == RuntimeModes.UnitySim ? unityRuntimeProvider.GetSensorStatus() : service.GetStatus());
});
app.MapGet("/api/sensors/latest", (RuntimeControlService runtimeControlService, SensorBridgeService service, UnityKs0223RuntimeProvider unityRuntimeProvider) =>
{
    var mode = runtimeControlService.GetCurrentMode();
    var latest = mode == RuntimeModes.UnitySim ? unityRuntimeProvider.GetLatestSensorTelemetry() : service.GetLatestTelemetry();
    return latest is null ? Results.NotFound(new { error = "Sensor telemetry is not available yet" }) : Results.Ok(latest);
});
app.MapPost("/api/sensors/config", async (HttpRequest http, RuntimeControlService runtimeControlService, SensorBridgeService service, UnityKs0223RuntimeProvider unityRuntimeProvider, CancellationToken cancellationToken) =>
{
    var request = await ReadBodyAsync(http, cancellationToken);
    var autoScanEnabled = ReadBoolQuery(http, "autoScanEnabled", "auto_scan_enabled") ?? ReadBool(request, "autoScanEnabled", "auto_scan_enabled");
    var sampleIntervalMs = ReadIntQuery(http, "sampleIntervalMs", "sample_interval_ms") ?? ReadInt(request, "sampleIntervalMs", "sample_interval_ms");
    var scanIntervalSec = ReadDoubleQuery(http, "scanIntervalSec", "scan_interval_sec") ?? ReadDouble(request, "scanIntervalSec", "scan_interval_sec");
    var scanSettleMs = ReadIntQuery(http, "scanSettleMs", "scan_settle_ms") ?? ReadInt(request, "scanSettleMs", "scan_settle_ms");
    var driveSpeedPercent = ReadIntQuery(http, "driveSpeedPercent", "drive_speed_percent") ?? ReadInt(request, "driveSpeedPercent", "drive_speed_percent");
    var cameraSpeedPercent = ReadIntQuery(http, "cameraSpeedPercent", "camera_speed_percent") ?? ReadInt(request, "cameraSpeedPercent", "camera_speed_percent");
    var ultrasonicServoPin =
        ReadIntQuery(http, "ultrasonicServoPin", "ultrasonic_servo_pin") ?? ReadInt(request, "ultrasonicServoPin", "ultrasonic_servo_pin");

    var response = runtimeControlService.GetCurrentMode() == RuntimeModes.UnitySim
        ? await unityRuntimeProvider.UpdateConfigAsync(
            autoScanEnabled,
            sampleIntervalMs,
            scanIntervalSec,
            scanSettleMs,
            driveSpeedPercent,
            cameraSpeedPercent,
            ultrasonicServoPin,
            cancellationToken)
        : await service.SendBridgeCommandAsync(
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
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});
app.MapPost("/api/sensors/ultrasonic/position", async (HttpRequest http, RuntimeControlService runtimeControlService, SensorBridgeService service, UnityKs0223RuntimeProvider unityRuntimeProvider, CancellationToken cancellationToken) =>
{
    var request = await ReadBodyAsync(http, cancellationToken);
    var angleDeg = ReadIntQuery(http, "angleDeg", "angle_deg") ?? ReadInt(request, "angleDeg", "angle_deg") ?? 90;
    var disableAutoScan =
        ReadBoolQuery(http, "disableAutoScan", "disable_auto_scan") ?? ReadBool(request, "disableAutoScan", "disable_auto_scan") ?? true;
    var servoPin = ReadIntQuery(http, "servoPin", "servo_pin", "ultrasonicServoPin", "ultrasonic_servo_pin")
        ?? ReadInt(request, "servoPin", "servo_pin", "ultrasonicServoPin", "ultrasonic_servo_pin");

    var response = runtimeControlService.GetCurrentMode() == RuntimeModes.UnitySim
        ? await unityRuntimeProvider.SetUltrasonicPositionAsync(angleDeg, disableAutoScan, servoPin, cancellationToken)
        : await service.SendBridgeCommandAsync(
            "/api/ultrasonic/position",
            new
            {
                angle_deg = angleDeg,
                disable_auto_scan = disableAutoScan,
                servo_pin = servoPin,
            },
            cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});
app.MapPost("/api/sensors/ultrasonic/auto-scan", async (HttpRequest http, RuntimeControlService runtimeControlService, SensorBridgeService service, UnityKs0223RuntimeProvider unityRuntimeProvider, CancellationToken cancellationToken) =>
{
    var request = await ReadBodyAsync(http, cancellationToken);
    var enabled = ReadBoolQuery(http, "enabled") ?? ReadBool(request, "enabled") ?? true;
    var response = runtimeControlService.GetCurrentMode() == RuntimeModes.UnitySim
        ? await unityRuntimeProvider.SetUltrasonicAutoScanAsync(enabled, cancellationToken)
        : await service.SendBridgeCommandAsync(
            "/api/ultrasonic/auto-scan",
            new
            {
                enabled,
            },
            cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});
app.MapPost("/api/led/pattern", async (HttpRequest http, SensorBridgeService service, CancellationToken cancellationToken) =>
{
    var request = await ReadBodyAsync(http, cancellationToken);
    var pattern = ReadStringQuery(http, "pattern") ?? ReadString(request, "pattern") ?? "smile";
    var response = await service.SendBridgeCommandAsync(
        "/api/led/pattern",
        new
        {
            pattern,
        },
        cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});
app.MapPost("/api/led/custom", async (HttpRequest http, SensorBridgeService service, CancellationToken cancellationToken) =>
{
    var request = await ReadBodyAsync(http, cancellationToken);
    var frameHex = ReadStringQuery(http, "frameHex", "frame_hex") ?? ReadString(request, "frameHex", "frame_hex") ?? string.Empty;
    var response = await service.SendBridgeCommandAsync(
        "/api/led/custom",
        new
        {
            frame_hex = frameHex,
        },
        cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});
app.MapPost("/api/led/clear", async (SensorBridgeService service, CancellationToken cancellationToken) =>
{
    var response = await service.SendBridgeCommandAsync("/api/led/clear", new { }, cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});
app.MapGet("/api/camera/snapshot", async (HttpContext context, RuntimeControlService runtimeControlService, CameraStreamService service, UnityKs0223RuntimeProvider unityRuntimeProvider) =>
{
    var requestedAgentId = context.Request.Query["agentId"].ToString();
    var mode = runtimeControlService.GetCurrentMode();
    var hasFrame = mode == RuntimeModes.UnitySim
        ? unityRuntimeProvider.TryGetLatestFrame(requestedAgentId, out var frame, out var contentType, out _, out var timestamp)
        : service.TryGetLatestFrame(out frame, out contentType, out _, out timestamp);

    if (!hasFrame)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = "Camera frame is not available yet" });
        return;
    }

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = contentType;
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";
    if (timestamp is not null)
    {
        context.Response.Headers.LastModified = timestamp.Value.ToString("R");
    }

    await context.Response.Body.WriteAsync(frame, context.RequestAborted);
});
app.MapGet("/api/camera/mjpeg", async (HttpContext context, RuntimeControlService runtimeControlService, CameraStreamService service, UnityKs0223RuntimeProvider unityRuntimeProvider) =>
{
    const string boundary = "frame";
    var requestedAgentId = context.Request.Query["agentId"].ToString();
    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.Headers.ContentType = $"multipart/x-mixed-replace; boundary={boundary}";
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";

    var sentVersion = -1L;
    var fps = 12;
    var delay = TimeSpan.FromMilliseconds(1000d / fps);
    var token = context.RequestAborted;

    while (!token.IsCancellationRequested)
    {
        var hasFrame = runtimeControlService.GetCurrentMode() == RuntimeModes.UnitySim
            ? unityRuntimeProvider.TryGetLatestFrame(requestedAgentId, out var frame, out _, out var version, out _)
            : service.TryGetLatestFrame(out frame, out _, out version, out _);

        if (hasFrame && version != sentVersion)
        {
            sentVersion = version;
            var header = $"--{boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n";
            await context.Response.WriteAsync(header, token);
            await context.Response.Body.WriteAsync(frame, token);
            await context.Response.WriteAsync("\r\n", token);
            await context.Response.Body.FlushAsync(token);
        }

        await Task.Delay(delay, token);
    }
});

app.MapPost("/api/command", async (CommandRequest request, RuntimeControlService runtimeControlService, CancellationToken cancellationToken) =>
{
    var response = await runtimeControlService.SendCommandAsync(request.Command, "ui", request.AgentId, cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapPost("/api/logs/start", async (StartLoggingRequest request, SessionLogger logger, RuntimeControlService runtimeControlService, CancellationToken cancellationToken) =>
{
    var state = await logger.StartAsync(request.Tag, cancellationToken);
    await runtimeControlService.BroadcastStatusAsync(cancellationToken);
    return Results.Ok(state);
});

app.MapPost("/api/logs/stop", async (SessionLogger logger, RuntimeControlService runtimeControlService, CancellationToken cancellationToken) =>
{
    var state = await logger.StopAsync(cancellationToken);
    await runtimeControlService.BroadcastStatusAsync(cancellationToken);
    return Results.Ok(state);
});

app.MapGet("/api/logs/files", (SessionLogger logger) => Results.Ok(logger.ListRecentFiles()));

app.MapPost("/api/logs/open-folder", async (SessionLogger logger) =>
{
    await logger.OpenFolderAsync();
    return Results.Ok(new { opened = true, path = logger.LogsDirectory });
});

app.MapGet("/api/protocol", (RuntimeControlService runtimeControlService) =>
{
    var target = runtimeControlService.GetConnectionTarget();
    return Results.Ok(new
    {
        runtimeMode = target.RuntimeMode,
        transport = target.RuntimeMode == RuntimeModes.RealRobot ? "tcp" : "http-json-step",
        host = target.Host,
        port = target.Port,
        encoding = "utf-8 text",
        framing = "no delimiter/no length prefix; one command per send",
        sensorTelemetry = "HTTP JSON from Pi add-on endpoint /api/telemetry (optional)",
        supportedCommands = new[]
        {
            "DirForward",
            "DirBack",
            "DirLeft",
            "DirRight",
            "DirStop",
            "CamUp",
            "CamDown",
            "CamLeft",
            "CamRight",
            "CamStop",
        },
    });
});

app.MapHub<TelemetryHub>("/hub/telemetry");
if (hasStaticFiles)
{
    app.MapFallbackToFile("index.html");
}

app.Run();

static async Task<JsonElement> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
{
    if (!request.HasJsonContentType())
    {
        return default;
    }

    try
    {
        var body = await request.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return body.ValueKind == JsonValueKind.Undefined ? default : body;
    }
    catch
    {
        return default;
    }
}

static string? ReadString(JsonElement element, params string[] keys)
{
    if (element.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    foreach (var key in keys)
    {
        if (!TryGetPropertyCaseInsensitive(element, key, out var value))
        {
            continue;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
        {
            return value.ToString();
        }
    }

    return null;
}

static int? ReadInt(JsonElement element, params string[] keys)
{
    var raw = ReadString(element, keys);
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    return int.TryParse(raw, out var value) ? value : null;
}

static double? ReadDouble(JsonElement element, params string[] keys)
{
    var raw = ReadString(element, keys);
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    return double.TryParse(raw, out var value) ? value : null;
}

static bool? ReadBool(JsonElement element, params string[] keys)
{
    var raw = ReadString(element, keys);
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    if (bool.TryParse(raw, out var boolean))
    {
        return boolean;
    }

    if (raw == "1")
    {
        return true;
    }

    if (raw == "0")
    {
        return false;
    }

    return null;
}

static bool TryGetPropertyCaseInsensitive(JsonElement element, string key, out JsonElement value)
{
    foreach (var prop in element.EnumerateObject())
    {
        if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
        {
            value = prop.Value;
            return true;
        }
    }

    value = default;
    return false;
}

static string? ReadStringQuery(HttpRequest request, params string[] keys)
{
    foreach (var key in keys)
    {
        if (request.Query.TryGetValue(key, out var value))
        {
            var first = value.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }
    }

    return null;
}

static int? ReadIntQuery(HttpRequest request, params string[] keys)
{
    var raw = ReadStringQuery(request, keys);
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    return int.TryParse(raw, out var value) ? value : null;
}

static double? ReadDoubleQuery(HttpRequest request, params string[] keys)
{
    var raw = ReadStringQuery(request, keys);
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    return double.TryParse(raw, out var value) ? value : null;
}

static bool? ReadBoolQuery(HttpRequest request, params string[] keys)
{
    var raw = ReadStringQuery(request, keys);
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    if (bool.TryParse(raw, out var boolean))
    {
        return boolean;
    }

    if (raw == "1")
    {
        return true;
    }

    if (raw == "0")
    {
        return false;
    }

    return null;
}
