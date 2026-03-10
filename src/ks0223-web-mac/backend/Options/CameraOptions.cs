namespace Ks0223.Web.Backend.Options;

public sealed class CameraOptions
{
    public bool EnableUdpListener { get; set; } = true;
    public int UdpListenPort { get; set; } = 5051;
    public int MaxFrameBytes { get; set; } = 3_000_000;
    public int MjpegFps { get; set; } = 12;
    public int ProbeIntervalSec { get; set; } = 20;
    public int HttpProbeTimeoutMs { get; set; } = 1500;
    public string[] HttpProbePaths { get; set; } =
    [
        "/?action=stream",
        "/stream.mjpg",
        "/mjpg/video.mjpg",
        "/video_feed",
        "/snapshot.jpg",
        "/cam.jpg",
    ];
}
