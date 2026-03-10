namespace Ks0223.Web.Backend.Options;

public sealed class PiConnectionOptions
{
    public string Host { get; set; } = "192.168.1.121";
    public int Port { get; set; } = 5051;
    public int ReconnectDelayMs { get; set; } = 1000;
    public int ReceiveBufferSize { get; set; } = 4096;
}
