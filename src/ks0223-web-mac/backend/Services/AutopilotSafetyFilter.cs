using Ks0223.Web.Backend.Options;

namespace Ks0223.Web.Backend.Services;

public readonly record struct SafetyDecision(float Throttle, float Steer, bool EStopActive);

public readonly record struct SafetyStatus(bool EStopActive, long EStopTriggerCount, float ThrottleMax);

public sealed class AutopilotSafetyFilter
{
    private readonly AutopilotSafetyOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<AutopilotSafetyFilter> logger;

    private bool eStopActive;
    private long eStopTriggerCount;
    private DateTimeOffset? eStopHoldUntil;
    private DateTimeOffset lastCallTime;
    private DateTimeOffset rampStartTime;
    private bool initialized;

    public AutopilotSafetyFilter(
        AutopilotSafetyOptions options,
        TimeProvider timeProvider,
        ILogger<AutopilotSafetyFilter> logger)
    {
        this.options = options;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public void Reset()
    {
        var now = timeProvider.GetUtcNow();
        eStopActive = false;
        eStopTriggerCount = 0;
        eStopHoldUntil = null;
        lastCallTime = now;
        rampStartTime = now;
        initialized = true;
    }

    public SafetyDecision Apply(float modelThrottle, float modelSteer, float frontDistanceM)
        => Apply(modelThrottle, modelSteer, frontDistanceM, 0f, 0f);

    public SafetyDecision Apply(
        float modelThrottle,
        float modelSteer,
        float frontDistanceM,
        float lateralLeftDistanceM,
        float lateralRightDistanceM)
    {
        if (!options.Enabled)
        {
            return new SafetyDecision(
                Math.Clamp(modelThrottle, -1f, 1f),
                Math.Clamp(modelSteer, -1f, 1f),
                false);
        }

        if (!initialized)
        {
            Reset();
        }

        var now = timeProvider.GetUtcNow();
        var clampedSteer = Math.Clamp(modelSteer, -1f, 1f);

        // --- Deadman check ---
        var msSinceLastCall = (now - lastCallTime).TotalMilliseconds;
        var deadmanFired = msSinceLastCall > options.DeadmanMs;
        if (deadmanFired)
        {
            rampStartTime = now;
        }

        lastCallTime = now;

        // --- Ultrasonic E-stop (front; only when distance is known) ---
        // 0 or negative means no usable reading — skip rather than false-trigger
        if (frontDistanceM > 0f && frontDistanceM < options.EStopDistanceM)
        {
            if (!eStopActive || eStopHoldUntil is null)
            {
                eStopTriggerCount += 1;
                eStopHoldUntil = now.AddMilliseconds(options.EStopHoldMs);
                eStopActive = true;
                logger.LogInformation(
                    "[autopilot] E-STOP front (ultrasonic={Dist:F2}m < {Threshold:F2}m); held {HoldMs}ms",
                    frontDistanceM,
                    options.EStopDistanceM,
                    options.EStopHoldMs);
            }
        }

        // --- Lateral E-stop (auto-scan left/right; only fresh readings) ---
        var lateralMinM = MinPositive(lateralLeftDistanceM, lateralRightDistanceM);
        if (lateralMinM > 0f && lateralMinM < options.LateralEStopDistanceM)
        {
            if (!eStopActive || eStopHoldUntil is null)
            {
                eStopTriggerCount += 1;
                eStopHoldUntil = now.AddMilliseconds(options.EStopHoldMs);
                eStopActive = true;
                logger.LogInformation(
                    "[autopilot] E-STOP lateral (min(L,R)={Dist:F2}m < {Threshold:F2}m); held {HoldMs}ms",
                    lateralMinM,
                    options.LateralEStopDistanceM,
                    options.EStopHoldMs);
            }
        }

        // --- E-stop hold release ---
        if (eStopActive && eStopHoldUntil.HasValue && now >= eStopHoldUntil.Value)
        {
            eStopActive = false;
            eStopHoldUntil = null;
        }

        // --- Deadman output: deadman always dominates, even if a hold expired this same call ---
        // A deadman timeout is a fresh stop event; stale hold state is moot.
        if (deadmanFired)
        {
            eStopTriggerCount += 1;
            eStopActive = true;
            eStopHoldUntil = now.AddMilliseconds(options.EStopHoldMs);
            var elapsedMs = (long)msSinceLastCall;
            logger.LogWarning(
                "[autopilot] deadman timeout ({ElapsedMs}ms > {DeadmanMs}ms); forcing stop",
                elapsedMs,
                options.DeadmanMs);
            return new SafetyDecision(0f, clampedSteer, true);
        }

        // --- During ultrasonic hold: block forward, allow reverse ---
        if (eStopActive)
        {
            if (modelThrottle < 0f)
            {
                return new SafetyDecision(Math.Max(modelThrottle, -1f), clampedSteer, false);
            }

            return new SafetyDecision(0f, clampedSteer, true);
        }

        // --- Throttle clip ---
        float clipped = modelThrottle >= 0f
            ? Math.Min(modelThrottle, options.ThrottleMax)
            : Math.Max(modelThrottle, -1f);

        // --- Ramp-up (forward only) ---
        var rampElapsedMs = (now - rampStartTime).TotalMilliseconds;
        var rampScale = options.RampUpMs > 0
            ? (float)Math.Min(1.0, rampElapsedMs / options.RampUpMs)
            : 1f;

        // ramp-up applies only to forward throttle — reverse is unscaled per spec
        var throttle = clipped >= 0f ? clipped * rampScale : clipped;

        return new SafetyDecision(throttle, clampedSteer, false);
    }

    public SafetyStatus GetStatus() =>
        new(eStopActive, eStopTriggerCount, options.ThrottleMax);

    private static float MinPositive(float a, float b)
    {
        if (a > 0f && b > 0f) return Math.Min(a, b);
        if (a > 0f) return a;
        if (b > 0f) return b;
        return 0f;
    }
}
