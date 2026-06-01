using System.Globalization;
using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Options;

namespace Ks0223.Web.Backend.Services;

public sealed record RealRobotCommandAdaptation(
    bool Accepted,
    string? Payload,
    string? Error = null,
    string? Note = null);

public sealed class RealRobotCommandAdapter
{
    private readonly RealRobotCommandOptions options;

    public RealRobotCommandAdapter(RealRobotCommandOptions options)
    {
        this.options = options;
    }

    public RealRobotCommandAdaptation Adapt(string command, string source, SensorTelemetryDto? telemetry)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new RealRobotCommandAdaptation(false, null, "Command is empty");
        }

        var normalized = command.Trim();
        if (!options.Enabled)
        {
            return new RealRobotCommandAdaptation(true, normalized, Note: "real robot command adapter disabled");
        }

        if (IsAlreadyExplicitCommand(normalized) || !IsRawDriveCommand(normalized))
        {
            return new RealRobotCommandAdaptation(true, normalized);
        }

        return normalized switch
        {
            "DirForward" => AdaptForward(source, telemetry),
            "DirLeft" => new RealRobotCommandAdaptation(true, $"DirLeft#{ClampPulse(options.TurnPulseMs)}"),
            "DirRight" => new RealRobotCommandAdaptation(true, $"DirRight#{ClampPulse(options.TurnPulseMs)}"),
            "DirBack" when options.DisableBackCommand => new RealRobotCommandAdaptation(
                true,
                "DirStop",
                Note: "DirBack is not calibrated for sim2real autopilot; sent DirStop instead"),
            "DirBack" => new RealRobotCommandAdaptation(true, "DirBack"),
            "DirStop" => AdaptStop(source, telemetry),
            _ => new RealRobotCommandAdaptation(true, normalized),
        };
    }

    private RealRobotCommandAdaptation AdaptForward(string source, SensorTelemetryDto? telemetry)
    {
        var isAutopilot = IsAutopilotSource(source);
        if (telemetry is null)
        {
            return isAutopilot && options.RequireFreshTelemetryForAutopilotForward
                ? new RealRobotCommandAdaptation(false, null, "Autopilot forward blocked: telemetry is missing")
                : BuildForwardPulse("forward pulse without telemetry");
        }

        if (isAutopilot &&
            options.RequireFreshTelemetryForAutopilotForward &&
            (DateTimeOffset.UtcNow - telemetry.Timestamp).TotalMilliseconds > options.AutopilotTelemetryStaleMs)
        {
            return new RealRobotCommandAdaptation(false, null, "Autopilot forward blocked: telemetry is stale");
        }

        if (TryReadFrontMeters(telemetry, out var frontM) && frontM < options.MinForwardClearanceM)
        {
            return new RealRobotCommandAdaptation(
                false,
                null,
                $"Forward blocked: front clearance {frontM:F2} m < {options.MinForwardClearanceM:F2} m");
        }

        return BuildForwardPulse(TryReadFrontMeters(telemetry, out frontM)
            ? $"front clearance {frontM:F2} m"
            : "front clearance unavailable");
    }

    private RealRobotCommandAdaptation AdaptStop(string source, SensorTelemetryDto? telemetry)
    {
        if (!options.RecoverAutopilotStopWithRightProbe || !IsLiveAutopilotLoopSource(source))
        {
            return new RealRobotCommandAdaptation(true, "DirStop");
        }

        if (telemetry is null)
        {
            return new RealRobotCommandAdaptation(true, "DirStop", Note: "stop recovery skipped: telemetry is missing");
        }

        if ((DateTimeOffset.UtcNow - telemetry.Timestamp).TotalMilliseconds > options.AutopilotTelemetryStaleMs)
        {
            return new RealRobotCommandAdaptation(true, "DirStop", Note: "stop recovery skipped: telemetry is stale");
        }

        if (TryReadFrontMeters(telemetry, out var frontM) && frontM < options.StopRecoveryMinFrontClearanceM)
        {
            return new RealRobotCommandAdaptation(
                true,
                "DirStop",
                Note: $"stop recovery skipped: front clearance {frontM:F2} m < {options.StopRecoveryMinFrontClearanceM:F2} m");
        }

        return new RealRobotCommandAdaptation(
            true,
            $"DirRight#{ClampPulse(options.StopRecoveryTurnPulseMs)}",
            Note: TryReadFrontMeters(telemetry, out frontM)
                ? $"DirStop recovery probe: right turn, front clearance {frontM:F2} m"
                : "DirStop recovery probe: right turn, front clearance unavailable");
    }

    private RealRobotCommandAdaptation BuildForwardPulse(string note)
    {
        var payload = string.Join(
            '|',
            $"DriveTrim#{ClampDuty(options.ForwardTrimLeft)},{ClampDuty(options.ForwardTrimRight)}",
            $"DirForward#{ClampPulse(options.ForwardPulseMs)}",
            $"DriveTrim#{ClampDuty(options.NeutralTrimLeft)},{ClampDuty(options.NeutralTrimRight)}");
        return new RealRobotCommandAdaptation(true, payload, Note: note);
    }

    private static bool IsAutopilotSource(string source) =>
        source.Contains("autopilot", StringComparison.OrdinalIgnoreCase);

    private static bool IsLiveAutopilotLoopSource(string source) =>
        string.Equals(source, "autopilot", StringComparison.OrdinalIgnoreCase);

    private static bool IsRawDriveCommand(string command) =>
        command is "DirForward" or "DirBack" or "DirLeft" or "DirRight" or "DirStop";

    private static bool IsAlreadyExplicitCommand(string command) =>
        command.StartsWith("DirForward#", StringComparison.Ordinal) ||
        command.StartsWith("DirBack#", StringComparison.Ordinal) ||
        command.StartsWith("DirLeft#", StringComparison.Ordinal) ||
        command.StartsWith("DirRight#", StringComparison.Ordinal) ||
        command.StartsWith("DriveSpeed#", StringComparison.Ordinal) ||
        command.StartsWith("DriveTrim#", StringComparison.Ordinal);

    private static int ClampPulse(int value) => Math.Clamp(value, 20, 2000);

    private static int ClampDuty(int value) => Math.Clamp(value, 0, 100);

    private static bool TryReadFrontMeters(SensorTelemetryDto telemetry, out double frontM)
    {
        if (TryReadMeters(telemetry.Flat, "sensor.ultrasonic.front.m", out frontM) ||
            TryReadMeters(telemetry.Flat, "ultrasonic.distance_m", out frontM))
        {
            return true;
        }

        if (TryReadRaw(telemetry.Flat, "ultrasonic.distance_cm", out var cm))
        {
            frontM = cm / 100.0;
            return true;
        }

        frontM = 0;
        return false;
    }

    private static bool TryReadMeters(IReadOnlyDictionary<string, string> flat, string key, out double value)
    {
        if (TryReadRaw(flat, key, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryReadRaw(IReadOnlyDictionary<string, string> flat, string key, out double value)
    {
        if (flat.TryGetValue(key, out var raw) &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) &&
            value > 0)
        {
            return true;
        }

        value = 0;
        return false;
    }
}
