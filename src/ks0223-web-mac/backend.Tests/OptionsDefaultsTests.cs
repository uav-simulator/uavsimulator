using Ks0223.Web.Backend.Options;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

/// <summary>
/// Pin the C# default values of every Options class — these are the
/// fallbacks used when <c>appsettings.json</c> doesn't override them
/// (most notably during unit tests and during a pre-config bootstrap).
/// A change to any default here is a behaviour change and must be done
/// deliberately, not accidentally.
/// </summary>
public sealed class OptionsDefaultsTests
{
    [Fact]
    public void PiConnectionOptions_Defaults_To_Loopback()
    {
        var opts = new PiConnectionOptions();
        // Loopback (not a stranger's robot IP) — see commit "scrub
        // hardcoded robot IP" for the rationale.
        Assert.Equal("127.0.0.1", opts.Host);
        Assert.Equal(5051, opts.Port);
        Assert.Equal(1000, opts.ReconnectDelayMs);
        Assert.Equal(4096, opts.ReceiveBufferSize);
    }

    [Fact]
    public void AutopilotSafetyOptions_Defaults_Match_Current_Production_Profile()
    {
        var opts = new AutopilotSafetyOptions();
        Assert.True(opts.Enabled);
        Assert.Equal(0.20f, opts.EStopDistanceM, 3);
        Assert.Equal(0.10f, opts.LateralEStopDistanceM, 3);
        Assert.Equal(500, opts.EStopHoldMs);
        Assert.Equal(0.25f, opts.ThrottleMax, 3);
        Assert.Equal(200, opts.RampUpMs);
        Assert.Equal(300, opts.DeadmanMs);
        Assert.Equal(0.30f, opts.SuspiciousJumpFromM, 3);
        Assert.Equal(1.50f, opts.SuspiciousJumpToM, 3);
    }

    [Fact]
    public void AutopilotSafetyOptions_Throttle_Cap_Is_Sane()
    {
        // Hard guard: a future "demo" mode mustn't accidentally raise the
        // throttle ceiling above 0.5 — that would let the open-loop KS0223
        // drive at well over its calibrated max safe speed.
        var opts = new AutopilotSafetyOptions();
        Assert.InRange(opts.ThrottleMax, 0.0f, 0.5f);
    }
}
