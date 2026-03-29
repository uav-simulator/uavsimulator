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
builder.Services.AddSingleton<RuntimeSessionManager>();
builder.Services.AddSingleton<ModelRegistryService>();
builder.Services.AddSingleton<AutopilotService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<RuntimeSessionManager>());

var app = builder.Build();

app.UseCors("frontend");
var hasStaticFiles = Directory.Exists(app.Environment.WebRootPath);
if (hasStaticFiles)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapGet("/api/status", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
{
    var clientId = ReadClientIdQuery(http);
    var runtimeMode = ReadRuntimeModeQuery(http);
    return Results.Ok(runtimeSessionManager.GetStatus(clientId, runtimeMode));
});

app.MapGet("/api/health", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
{
    var clientId = ReadClientIdQuery(http);
    var runtimeMode = ReadRuntimeModeQuery(http);
    return Results.Ok(runtimeSessionManager.GetHealth(clientId, runtimeMode));
});

app.MapPost("/api/connection/connect", async (ConnectRequest request, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
{
    try
    {
        var status = await runtimeSessionManager.ConnectAsync(request.ClientId, request.RuntimeMode, request.Host, request.Port, cancellationToken);
        return Results.Ok(status);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/connection/disconnect", async (DisconnectRequest request, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
{
    try
    {
        var status = await runtimeSessionManager.DisconnectAsync(request.ClientId, request.RuntimeMode, cancellationToken);
        return Results.Ok(status);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/models/upload", async (HttpRequest http, ModelRegistryService modelRegistry, CancellationToken cancellationToken) =>
{
    if (!http.HasFormContentType)
    {
        return Results.BadRequest(new { error = "multipart/form-data is required" });
    }

    try
    {
        var form = await http.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length <= 0)
        {
            return Results.BadRequest(new { error = "Model artifact file is required in `file` field" });
        }

        await using var stream = file.OpenReadStream();
        var uploaded = await modelRegistry.UploadAsync(
            stream,
            file.FileName,
            ReadFormValue(form, "name"),
            ReadFormValue(form, "version"),
            ReadFormValue(form, "source"),
            ReadFormValue(form, "metadata"),
            ReadFormValue(form, "metrics"),
            cancellationToken);
        return Results.Ok(uploaded);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/models", (ModelRegistryService modelRegistry) =>
{
    var models = modelRegistry.ListModels();
    return Results.Ok(models);
});

app.MapGet("/api/models/active", (ModelRegistryService modelRegistry) =>
{
    var model = modelRegistry.GetActiveModel();
    return model is null
        ? Results.NotFound(new { error = "Active model is not selected" })
        : Results.Ok(model);
});

app.MapPost("/api/models/activate", (ActivateModelRequest request, ModelRegistryService modelRegistry) =>
{
    try
    {
        var active = modelRegistry.Activate(request.ModelId);
        return Results.Ok(active);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/autopilot/start", async (StartAutopilotRequest request, AutopilotService autopilotService, CancellationToken cancellationToken) =>
{
    try
    {
        var status = await autopilotService.StartAsync(request, cancellationToken);
        return Results.Ok(status);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/autopilot/stop", async (StopAutopilotRequest request, AutopilotService autopilotService, CancellationToken cancellationToken) =>
{
    var status = await autopilotService.StopAsync(request, cancellationToken);
    return Results.Ok(status);
});

app.MapGet("/api/autopilot/status", (HttpRequest http, AutopilotService autopilotService) =>
{
    var clientId = ReadOptionalClientIdQuery(http);
    var runtimeMode = ReadOptionalRuntimeModeQuery(http);
    return Results.Ok(autopilotService.GetStatus(clientId, runtimeMode));
});

app.MapGet("/api/connection/target", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
{
    var clientId = ReadClientIdQuery(http);
    var runtimeMode = ReadRuntimeModeQuery(http);
    return Results.Ok(runtimeSessionManager.GetConnectionTarget(clientId, runtimeMode));
});

app.MapGet("/api/unity/runtime-catalog", async (HttpRequest http, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
{
    try
    {
        var clientId = ReadClientIdQuery(http);
        var runtimeMode = ReadRuntimeModeQuery(http);
        var host = ReadStringQuery(http, "host");
        var port = ReadIntQuery(http, "port");
        var catalog = await runtimeSessionManager.GetUnityRuntimeCatalogAsync(clientId, runtimeMode, host, port, cancellationToken);
        return Results.Ok(catalog);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/unity/runtime-selection", async (UnityRuntimeSelectionRequest request, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
{
    try
    {
        var catalog = await runtimeSessionManager.SetUnityRuntimeSelectionAsync(request, cancellationToken);
        return Results.Ok(catalog);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/unity/client-selection", async (UnityClientSelectionRequest request, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
{
    try
    {
        var catalog = await runtimeSessionManager.SetUnityClientSelectionAsync(request, cancellationToken);
        return Results.Ok(catalog);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/camera/status", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
{
    var clientId = ReadClientIdQuery(http);
    var runtimeMode = ReadRuntimeModeQuery(http);
    return Results.Ok(runtimeSessionManager.GetCameraStatus(clientId, runtimeMode));
});

app.MapGet("/api/sensors/status", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
{
    var clientId = ReadClientIdQuery(http);
    var runtimeMode = ReadRuntimeModeQuery(http);
    return Results.Ok(runtimeSessionManager.GetSensorStatus(clientId, runtimeMode));
});

app.MapGet("/api/sensors/latest", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
{
    var clientId = ReadClientIdQuery(http);
    var runtimeMode = ReadRuntimeModeQuery(http);
    var latest = runtimeSessionManager.GetLatestSensorTelemetry(clientId, runtimeMode);
    return latest is null ? Results.NotFound(new { error = "Sensor telemetry is not available yet" }) : Results.Ok(latest);
});

app.MapPost("/api/sensors/config", async (HttpRequest http, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
{
    var request = await ReadBodyAsync(http, cancellationToken);
    var clientId = ReadClientId(http, request);
    var runtimeMode = ReadRuntimeMode(http, request);
    var autoScanEnabled = ReadBoolQuery(http, "autoScanEnabled", "auto_scan_enabled") ?? ReadBool(request, "autoScanEnabled", "auto_scan_enabled");
    var sampleIntervalMs = ReadIntQuery(http, "sampleIntervalMs", "sample_interval_ms") ?? ReadInt(request, "sampleIntervalMs", "sample_interval_ms");
    var scanIntervalSec = ReadDoubleQuery(http, "scanIntervalSec", "scan_interval_sec") ?? ReadDouble(request, "scanIntervalSec", "scan_interval_sec");
    var scanSettleMs = ReadIntQuery(http, "scanSettleMs", "scan_settle_ms") ?? ReadInt(request, "scanSettleMs", "scan_settle_ms");
    var driveSpeedPercent = ReadIntQuery(http, "driveSpeedPercent", "drive_speed_percent") ?? ReadInt(request, "driveSpeedPercent", "drive_speed_percent");
    var cameraSpeedPercent = ReadIntQuery(http, "cameraSpeedPercent", "camera_speed_percent") ?? ReadInt(request, "cameraSpeedPercent", "camera_speed_percent");
    var ultrasonicServoPin =
        ReadIntQuery(http, "ultrasonicServoPin", "ultrasonic_servo_pin") ?? ReadInt(request, "ultrasonicServoPin", "ultrasonic_servo_pin");

    var response = await runtimeSessionManager.UpdateConfigAsync(
        clientId,
        runtimeMode,
        autoScanEnabled,
        sampleIntervalMs,
        scanIntervalSec,
        scanSettleMs,
        driveSpeedPercent,
        cameraSpeedPercent,
        ultrasonicServoPin,
        cancellationToken);

    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapPost("/api/sensors/ultrasonic/position", async (HttpRequest http, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
{
    var request = await ReadBodyAsync(http, cancellationToken);
    var clientId = ReadClientId(http, request);
    var runtimeMode = ReadRuntimeMode(http, request);
    var angleDeg = ReadIntQuery(http, "angleDeg", "angle_deg") ?? ReadInt(request, "angleDeg", "angle_deg") ?? 90;
    var disableAutoScan =
        ReadBoolQuery(http, "disableAutoScan", "disable_auto_scan") ?? ReadBool(request, "disableAutoScan", "disable_auto_scan") ?? true;
    var servoPin = ReadIntQuery(http, "servoPin", "servo_pin", "ultrasonicServoPin", "ultrasonic_servo_pin")
        ?? ReadInt(request, "servoPin", "servo_pin", "ultrasonicServoPin", "ultrasonic_servo_pin");

    var response = await runtimeSessionManager.SetUltrasonicPositionAsync(
        clientId,
        runtimeMode,
        angleDeg,
        disableAutoScan,
        servoPin,
        cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapPost("/api/sensors/ultrasonic/auto-scan", async (HttpRequest http, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
{
    var request = await ReadBodyAsync(http, cancellationToken);
    var clientId = ReadClientId(http, request);
    var runtimeMode = ReadRuntimeMode(http, request);
    var enabled = ReadBoolQuery(http, "enabled") ?? ReadBool(request, "enabled") ?? true;
    var response = await runtimeSessionManager.SetUltrasonicAutoScanAsync(clientId, runtimeMode, enabled, cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapPost("/api/led/pattern", async (HttpRequest http, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
{
    var request = await ReadBodyAsync(http, cancellationToken);
    var clientId = ReadClientId(http, request);
    var runtimeMode = ReadRuntimeMode(http, request);
    var pattern = ReadStringQuery(http, "pattern") ?? ReadString(request, "pattern") ?? "smile";
    var response = await runtimeSessionManager.SetLedPatternAsync(clientId, runtimeMode, pattern, cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapPost("/api/led/custom", async (HttpRequest http, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
{
    var request = await ReadBodyAsync(http, cancellationToken);
    var clientId = ReadClientId(http, request);
    var runtimeMode = ReadRuntimeMode(http, request);
    var frameHex = ReadStringQuery(http, "frameHex", "frame_hex") ?? ReadString(request, "frameHex", "frame_hex") ?? string.Empty;
    var response = await runtimeSessionManager.SetLedCustomFrameAsync(clientId, runtimeMode, frameHex, cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapPost("/api/led/clear", async (HttpRequest http, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
{
    var request = await ReadBodyAsync(http, cancellationToken);
    var clientId = ReadClientId(http, request);
    var runtimeMode = ReadRuntimeMode(http, request);
    var response = await runtimeSessionManager.ClearLedAsync(clientId, runtimeMode, cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapGet("/api/camera/snapshot", async (HttpContext context, RuntimeSessionManager runtimeSessionManager) =>
{
    var requestedAgentId = context.Request.Query["agentId"].ToString();
    var clientId = ReadClientIdQuery(context.Request);
    var runtimeMode = ReadRuntimeModeQuery(context.Request);
    var hasFrame = runtimeSessionManager.TryGetLatestFrame(clientId, runtimeMode, requestedAgentId, out var frame, out var contentType, out _, out var timestamp);

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

app.MapGet("/api/camera/mjpeg", async (HttpContext context, RuntimeSessionManager runtimeSessionManager) =>
{
    const string boundary = "frame";
    var requestedAgentId = context.Request.Query["agentId"].ToString();
    var clientId = ReadClientIdQuery(context.Request);
    var runtimeMode = ReadRuntimeModeQuery(context.Request);
    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.Headers.ContentType = $"multipart/x-mixed-replace; boundary={boundary}";
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";

    var sentVersion = -1L;
    var delay = TimeSpan.FromMilliseconds(1000d / 12d);
    var token = context.RequestAborted;

    while (!token.IsCancellationRequested)
    {
        var hasFrame = runtimeSessionManager.TryGetLatestFrame(clientId, runtimeMode, requestedAgentId, out var frame, out _, out var version, out _);
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

app.MapPost("/api/command", async (CommandRequest request, RuntimeSessionManager runtimeSessionManager, AutopilotService autopilotService, CancellationToken cancellationToken) =>
{
    await autopilotService.HandleManualOverrideAsync(request.ClientId, request.RuntimeMode, cancellationToken);
    var response = await runtimeSessionManager.SendCommandAsync(
        request.ClientId,
        request.RuntimeMode,
        request.Command,
        request.AgentId,
        cancellationToken);
    return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapPost("/api/logs/start", async (StartLoggingRequest request, SessionLogger logger, CancellationToken cancellationToken) =>
{
    var state = await logger.StartAsync(request.Tag, cancellationToken);
    return Results.Ok(state);
});

app.MapPost("/api/logs/stop", async (SessionLogger logger, CancellationToken cancellationToken) =>
{
    var state = await logger.StopAsync(cancellationToken);
    return Results.Ok(state);
});

app.MapGet("/api/logs/files", (SessionLogger logger) => Results.Ok(logger.ListRecentFiles()));

app.MapPost("/api/logs/open-folder", async (SessionLogger logger) =>
{
    await logger.OpenFolderAsync();
    return Results.Ok(new { opened = true, path = logger.LogsDirectory });
});

app.MapGet("/api/protocol", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
{
    var clientId = ReadClientIdQuery(http);
    var runtimeMode = ReadRuntimeModeQuery(http);
    var target = runtimeSessionManager.GetConnectionTarget(clientId, runtimeMode);
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

static string ReadClientIdQuery(HttpRequest request)
{
    var clientId = ReadStringQuery(request, "clientId", "client_id");
    if (string.IsNullOrWhiteSpace(clientId))
    {
        throw new BadHttpRequestException("clientId query parameter is required");
    }

    return clientId.Trim();
}

static string? ReadOptionalClientIdQuery(HttpRequest request)
{
    var clientId = ReadStringQuery(request, "clientId", "client_id");
    return string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
}

static string ReadRuntimeModeQuery(HttpRequest request)
{
    var runtimeMode = ReadStringQuery(request, "runtimeMode", "runtime_mode");
    if (string.IsNullOrWhiteSpace(runtimeMode))
    {
        throw new BadHttpRequestException("runtimeMode query parameter is required");
    }

    return runtimeMode.Trim();
}

static string? ReadOptionalRuntimeModeQuery(HttpRequest request)
{
    var runtimeMode = ReadStringQuery(request, "runtimeMode", "runtime_mode");
    return string.IsNullOrWhiteSpace(runtimeMode) ? null : runtimeMode.Trim();
}

static string ReadClientId(HttpRequest request, JsonElement body)
{
    var clientId = ReadStringQuery(request, "clientId", "client_id") ?? ReadString(body, "clientId", "client_id");
    if (string.IsNullOrWhiteSpace(clientId))
    {
        throw new BadHttpRequestException("clientId is required");
    }

    return clientId.Trim();
}

static string ReadRuntimeMode(HttpRequest request, JsonElement body)
{
    var runtimeMode = ReadStringQuery(request, "runtimeMode", "runtime_mode") ?? ReadString(body, "runtimeMode", "runtime_mode");
    if (string.IsNullOrWhiteSpace(runtimeMode))
    {
        throw new BadHttpRequestException("runtimeMode is required");
    }

    return runtimeMode.Trim();
}

static string? ReadFormValue(IFormCollection form, string key)
{
    if (!form.TryGetValue(key, out var value))
    {
        return null;
    }

    var raw = value.FirstOrDefault();
    return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
}
