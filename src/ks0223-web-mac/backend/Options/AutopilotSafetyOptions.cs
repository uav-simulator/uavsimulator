namespace Ks0223.Web.Backend.Options;

public sealed class AutopilotSafetyOptions
{
    public bool Enabled { get; set; } = true;
    public float EStopDistanceM { get; set; } = 0.12f;
    public int EStopHoldMs { get; set; } = 500;
    public float ThrottleMax { get; set; } = 0.5f;
    public int RampUpMs { get; set; } = 200;
    public int DeadmanMs { get; set; } = 300;
}
