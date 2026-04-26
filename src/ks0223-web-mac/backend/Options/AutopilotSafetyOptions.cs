namespace Ks0223.Web.Backend.Options;

public sealed class AutopilotSafetyOptions
{
    public bool Enabled { get; set; } = true;
    public float EStopDistanceM { get; set; } = 0.20f;
    public float LateralEStopDistanceM { get; set; } = 0.10f;
    public int EStopHoldMs { get; set; } = 500;
    public float ThrottleMax { get; set; } = 0.25f;
    public int RampUpMs { get; set; } = 200;
    public int DeadmanMs { get; set; } = 300;

    public float SuspiciousJumpFromM { get; set; } = 0.30f;
    public float SuspiciousJumpToM { get; set; } = 1.50f;
}
