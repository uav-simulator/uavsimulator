namespace Ks0223.Web.Backend.Options;

public sealed class PiConnectionOptions
{
    // Default points at loopback so a fresh checkout / dist build does not
    // accidentally connect to the original developer's robot. Operators
    // override either via `appsettings.Development.json`, environment
    // variable `PiConnection__Host`, or the Web UI's connection panel.
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5051;
    public int ReconnectDelayMs { get; set; } = 1000;
    public int ReceiveBufferSize { get; set; } = 4096;
}
