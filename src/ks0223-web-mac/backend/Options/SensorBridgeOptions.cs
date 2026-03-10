namespace Ks0223.Web.Backend.Options;

public sealed class SensorBridgeOptions
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 8765;
    public string TelemetryPath { get; set; } = "/api/telemetry";
    public int PollIntervalMs { get; set; } = 500;
    public int RequestTimeoutMs { get; set; } = 1200;
}
