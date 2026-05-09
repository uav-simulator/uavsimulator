using Ks0223.Web.Backend.Services;
using static Ks0223.Web.Backend.Endpoints.EndpointHelpers;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// Sensor + LED read/write endpoints (status, latest telemetry, config,
/// ultrasonic position + auto-scan, LED pattern/custom/clear).
/// Originally lines 344-446 of Program.cs.
/// </summary>
internal static class SensorsEndpoints
{
    public static void MapSensorsEndpoints(this WebApplication app)
    {
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
    }
}
