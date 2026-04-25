using Ks0223.Web.Backend.Options;
using Ks0223.Web.Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Ks0223.Web.Backend.Tests;

public sealed class AutopilotSafetyFilterTests
{
    private static AutopilotSafetyFilter CreateFilter(AutopilotSafetyOptions? options = null, TimeProvider? timeProvider = null)
    {
        var opts = options ?? new AutopilotSafetyOptions();
        var tp = timeProvider ?? TimeProvider.System;
        return new AutopilotSafetyFilter(opts, tp, NullLogger<AutopilotSafetyFilter>.Instance);
    }

    // 1. E-stop triggers when distance is below threshold
    [Fact]
    public void EStop_Triggers_When_Distance_Below_Threshold()
    {
        var fake = new FakeTimeProvider();
        var filter = CreateFilter(timeProvider: fake);
        filter.Reset();

        var decision = filter.Apply(0.5f, 0f, 0.10f);

        Assert.Equal(0f, decision.Throttle);
        Assert.True(decision.EStopActive);
        Assert.Equal(1L, filter.GetStatus().EStopTriggerCount);
    }

    // 2. E-stop sticky hold: forward blocked, reverse allowed
    [Fact]
    public void EStop_Sticky_Hold_Blocks_Forward_Allows_Reverse()
    {
        var fake = new FakeTimeProvider();
        var filter = CreateFilter(timeProvider: fake);
        filter.Reset();

        // Trigger E-stop
        filter.Apply(0.5f, 0f, 0.10f);

        // 100ms into hold: forward should be blocked
        fake.Advance(TimeSpan.FromMilliseconds(100));
        var forwardDecision = filter.Apply(0.5f, 0f, 1.0f);
        Assert.Equal(0f, forwardDecision.Throttle);
        Assert.True(forwardDecision.EStopActive);

        // Still within hold: reverse should pass through
        var reverseDecision = filter.Apply(-0.3f, 0f, 1.0f);
        Assert.Equal(-0.3f, reverseDecision.Throttle, 3);
        Assert.False(reverseDecision.EStopActive);
    }

    // 3. E-stop releases after hold duration elapses
    [Fact]
    public void EStop_Releases_After_HoldMs_Elapsed()
    {
        var fake = new FakeTimeProvider();
        var filter = CreateFilter(timeProvider: fake);
        filter.Reset();

        // Trigger E-stop
        filter.Apply(0.5f, 0f, 0.10f);

        // Advance past the 500ms hold
        fake.Advance(TimeSpan.FromMilliseconds(600));

        var decision = filter.Apply(0.5f, 0f, 1.0f);
        Assert.False(decision.EStopActive);
    }

    // 4. Throttle clip: forward capped, reverse not clipped
    [Fact]
    public void Throttle_Clip_Forward_Capped_Reverse_Not()
    {
        var fake = new FakeTimeProvider();
        var filter = CreateFilter(timeProvider: fake);
        filter.Reset();

        // Advance past ramp-up so rampScale = 1.0
        fake.Advance(TimeSpan.FromMilliseconds(300));

        var d1 = filter.Apply(1.0f, 0f, 1.0f);
        Assert.Equal(0.5f, d1.Throttle, 3);

        var d2 = filter.Apply(-1.0f, 0f, 1.0f);
        Assert.Equal(-1.0f, d2.Throttle, 3);

        var d3 = filter.Apply(0.3f, 0f, 1.0f);
        Assert.Equal(0.3f, d3.Throttle, 3);

        var d4 = filter.Apply(0.7f, 0f, 1.0f);
        Assert.Equal(0.5f, d4.Throttle, 3);
    }

    // 5. Ramp-up scales throttle linearly from zero
    [Fact]
    public void RampUp_Scales_Throttle_Linearly_From_Zero()
    {
        var fake = new FakeTimeProvider();
        var filter = CreateFilter(timeProvider: fake);
        filter.Reset();

        // t=0: scale=0 → throttle=0
        var d0 = filter.Apply(1.0f, 0f, 1.0f);
        Assert.Equal(0f, d0.Throttle, 3);

        // t=100ms: rampScale=0.5; clip(1.0, 0.5)=0.5; 0.5*0.5=0.25
        fake.Advance(TimeSpan.FromMilliseconds(100));
        var d100 = filter.Apply(1.0f, 0f, 1.0f);
        Assert.Equal(0.25f, d100.Throttle, 3);

        // t=200ms+: rampScale=1.0; clip(0.5)=0.5; 0.5*1.0=0.5
        fake.Advance(TimeSpan.FromMilliseconds(100));
        var d200 = filter.Apply(0.5f, 0f, 1.0f);
        Assert.Equal(0.5f, d200.Throttle, 3);
    }

    // 6. Deadman forces stop after idle period, then ramp restarts
    [Fact]
    public void Deadman_Forces_Stop_After_Idle_Period()
    {
        var fake = new FakeTimeProvider();
        var filter = CreateFilter(timeProvider: fake);
        filter.Reset();

        // Advance past ramp so we get real throttle on first call
        fake.Advance(TimeSpan.FromMilliseconds(300));

        // Normal call to seed lastCallTime
        filter.Apply(0.5f, 0f, 1.0f);

        // Advance 400ms > 300ms deadman threshold
        fake.Advance(TimeSpan.FromMilliseconds(400));
        var deadmanDecision = filter.Apply(0.5f, 0f, 1.0f);
        Assert.Equal(0f, deadmanDecision.Throttle);
        Assert.True(deadmanDecision.EStopActive);

        // Immediately call again: ramp restarted from t=0, so scale=0
        var afterDecision = filter.Apply(0.5f, 0f, 1.0f);
        Assert.Equal(0f, afterDecision.Throttle);
    }

    // 7. Composition: all filters active
    [Fact]
    public void Composition_All_Filters_Active()
    {
        var fake = new FakeTimeProvider();
        var filter = CreateFilter(timeProvider: fake);
        filter.Reset();

        // Advance past ramp-up
        fake.Advance(TimeSpan.FromMilliseconds(300));

        // Safe distance, high throttle: only clip applies
        var safeDec = filter.Apply(1.0f, 0f, 1.0f);
        Assert.Equal(0.5f, safeDec.Throttle, 3);
        Assert.False(safeDec.EStopActive);

        // Obstacle within threshold: E-stop dominates
        var estopDec = filter.Apply(1.0f, 0f, 0.10f);
        Assert.Equal(0f, estopDec.Throttle);
        Assert.True(estopDec.EStopActive);
    }

    // 8. Disabled filter passes through with [-1,1] clamp
    [Fact]
    public void Disabled_Filter_Passes_Through_With_Clamp()
    {
        var opts = new AutopilotSafetyOptions { Enabled = false };
        var filter = CreateFilter(opts);
        filter.Reset();

        var decision = filter.Apply(2.5f, -3.0f, 0.05f);

        Assert.Equal(1.0f, decision.Throttle, 3);
        Assert.Equal(-1.0f, decision.Steer, 3);
        Assert.False(decision.EStopActive);
    }
}
