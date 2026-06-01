using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Options;
using Ks0223.Web.Backend.Services;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

public sealed class RealRobotCommandAdapterTests
{
    private static SensorTelemetryDto TelemetryWithDistanceCm(double distanceCm, int ageMs = 0)
    {
        return new SensorTelemetryDto(
            DateTimeOffset.UtcNow.AddMilliseconds(-ageMs),
            "http://robot/api/telemetry",
            "{}",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ultrasonic.distance_cm"] = distanceCm.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
    }

    [Fact]
    public void Adapt_Maps_Raw_Forward_To_Calibrated_Trimmed_Pulse()
    {
        var adapter = new RealRobotCommandAdapter(new RealRobotCommandOptions());

        var result = adapter.Adapt("DirForward", "autopilot", TelemetryWithDistanceCm(20));

        Assert.True(result.Accepted);
        Assert.Equal("DriveTrim#64,80|DirForward#40|DriveTrim#80,80", result.Payload);
    }

    [Fact]
    public void Adapt_Blocks_Forward_Only_When_Almost_Touching()
    {
        var adapter = new RealRobotCommandAdapter(new RealRobotCommandOptions());

        var result = adapter.Adapt("DirForward", "autopilot", TelemetryWithDistanceCm(2));

        Assert.False(result.Accepted);
        Assert.Null(result.Payload);
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("front clearance", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("DirLeft", "DirLeft#240")]
    [InlineData("DirRight", "DirRight#240")]
    [InlineData("DirStop", "DirStop")]
    public void Adapt_Maps_Turns_And_Stop_To_Safe_Primitives(string raw, string expected)
    {
        var adapter = new RealRobotCommandAdapter(new RealRobotCommandOptions());

        var result = adapter.Adapt(raw, "autopilot", TelemetryWithDistanceCm(120));

        Assert.True(result.Accepted);
        Assert.Equal(expected, result.Payload);
    }

    [Fact]
    public void Adapt_Converts_Live_Autopilot_Stop_To_Right_Probe_When_Clear()
    {
        var adapter = new RealRobotCommandAdapter(new RealRobotCommandOptions
        {
            RecoverAutopilotStopWithRightProbe = true,
            StopRecoveryTurnPulseMs = 80,
        });

        var result = adapter.Adapt("DirStop", "autopilot", TelemetryWithDistanceCm(80));

        Assert.True(result.Accepted);
        Assert.Equal("DirRight#80", result.Payload);
        Assert.Contains("probe", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ui")]
    [InlineData("autopilot-stop")]
    [InlineData("autopilot-failsafe")]
    public void Adapt_Does_Not_Rewrite_Manual_Or_Failsafe_Stop(string source)
    {
        var adapter = new RealRobotCommandAdapter(new RealRobotCommandOptions
        {
            RecoverAutopilotStopWithRightProbe = true,
        });

        var result = adapter.Adapt("DirStop", source, TelemetryWithDistanceCm(80));

        Assert.True(result.Accepted);
        Assert.Equal("DirStop", result.Payload);
    }

    [Fact]
    public void Adapt_Rewrites_Autopilot_Stop_Even_When_Almost_Touching()
    {
        var adapter = new RealRobotCommandAdapter(new RealRobotCommandOptions
        {
            RecoverAutopilotStopWithRightProbe = true,
            StopRecoveryTurnPulseMs = 80,
        });

        var result = adapter.Adapt("DirStop", "autopilot", TelemetryWithDistanceCm(2));

        Assert.True(result.Accepted);
        Assert.Equal("DirRight#80", result.Payload);
        Assert.Contains("probe", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Adapt_Disables_Uncalibrated_Back_Command_With_Stop()
    {
        var adapter = new RealRobotCommandAdapter(new RealRobotCommandOptions());

        var result = adapter.Adapt("DirBack", "autopilot", TelemetryWithDistanceCm(120));

        Assert.True(result.Accepted);
        Assert.Equal("DirStop", result.Payload);
        Assert.Contains("DirBack", result.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Adapt_Blocks_Autopilot_Forward_When_Sonar_Is_Missing()
    {
        var adapter = new RealRobotCommandAdapter(new RealRobotCommandOptions());

        var result = adapter.Adapt("DirForward", "autopilot", telemetry: null);

        Assert.False(result.Accepted);
        Assert.Null(result.Payload);
        Assert.Contains("telemetry", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Adapt_Blocks_Forward_When_Known_Obstacle_Is_Too_Close()
    {
        var adapter = new RealRobotCommandAdapter(new RealRobotCommandOptions());

        var result = adapter.Adapt("DirForward", "ui", TelemetryWithDistanceCm(2));

        Assert.False(result.Accepted);
        Assert.Null(result.Payload);
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("CamLeft")]
    [InlineData("DirForward#140")]
    [InlineData("DriveTrim#64,80")]
    public void Adapt_Passes_Already_Explicit_Or_Non_Drive_Commands(string command)
    {
        var adapter = new RealRobotCommandAdapter(new RealRobotCommandOptions());

        var result = adapter.Adapt(command, "ui", telemetry: null);

        Assert.True(result.Accepted);
        Assert.Equal(command, result.Payload);
    }
}
