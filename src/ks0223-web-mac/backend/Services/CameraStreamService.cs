using System.Net;
using System.Net.Sockets;
using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Options;
using Microsoft.Extensions.Options;

namespace Ks0223.Web.Backend.Services;

public sealed class CameraStreamService : BackgroundService
{
    private readonly object frameLock = new();
    private readonly CameraOptions options;
    private readonly PiTcpClientService piTcpClientService;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<CameraStreamService> logger;

    private byte[]? latestFrame;
    private DateTimeOffset? lastFrameAt;
    private string? frameSource;
    private long frameVersion;
    private long framesReceived;
    private long bytesReceived;
    private IReadOnlyList<string> httpProbeCandidates = Array.Empty<string>();
    private IReadOnlyList<string> httpDiscoveredStreams = Array.Empty<string>();

    public CameraStreamService(
        IOptions<CameraOptions> options,
        PiTcpClientService piTcpClientService,
        IHttpClientFactory httpClientFactory,
        ILogger<CameraStreamService> logger)
    {
        this.options = options.Value;
        this.piTcpClientService = piTcpClientService;
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
    }

    public CameraStatusDto GetStatus()
    {
        lock (frameLock)
        {
            var hasFreshFrame = latestFrame is not null
                && CameraFrameFreshness.IsFresh(lastFrameAt, options.MaxFrameAgeMs, DateTimeOffset.UtcNow);
            return new CameraStatusDto(
                UdpListenerEnabled: options.EnableUdpListener,
                UdpListenPort: options.UdpListenPort,
                HasFrame: hasFreshFrame,
                LastFrameAt: lastFrameAt,
                Source: frameSource,
                FramesReceived: framesReceived,
                BytesReceived: bytesReceived,
                HttpProbeCandidates: httpProbeCandidates,
                HttpDiscoveredStreams: httpDiscoveredStreams);
        }
    }

    public bool TryGetLatestFrame(out byte[] frame, out string contentType, out long version, out DateTimeOffset? timestamp)
    {
        lock (frameLock)
        {
            if (latestFrame is null
                || !CameraFrameFreshness.IsFresh(lastFrameAt, options.MaxFrameAgeMs, DateTimeOffset.UtcNow))
            {
                frame = Array.Empty<byte>();
                contentType = "image/jpeg";
                version = frameVersion;
                timestamp = lastFrameAt;
                return false;
            }

            frame = latestFrame.ToArray();
            contentType = "image/jpeg";
            version = frameVersion;
            timestamp = lastFrameAt;
            return true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>
        {
            ProbeHttpCameraEndpointsLoopAsync(stoppingToken),
        };

        if (options.EnableUdpListener)
        {
            tasks.Add(RunUdpCameraLoopAsync(stoppingToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task RunUdpCameraLoopAsync(CancellationToken stoppingToken)
    {
        UdpClient? udpClient = null;
        try
        {
            udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, options.UdpListenPort));
            logger.LogInformation("Camera UDP listener started on port {Port}", options.UdpListenPort);

            while (!stoppingToken.IsCancellationRequested)
            {
                UdpReceiveResult packet;
                try
                {
                    packet = await udpClient.ReceiveAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var payload = packet.Buffer;
                if (payload.Length == 0 || payload.Length > options.MaxFrameBytes)
                {
                    continue;
                }

                if (!LooksLikeJpeg(payload))
                {
                    continue;
                }

                UpdateFrame(payload, $"udp:{packet.RemoteEndPoint.Address}");
            }
        }
        catch (SocketException ex)
        {
            logger.LogWarning(ex, "Camera UDP listener failed to start on {Port}", options.UdpListenPort);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Camera UDP listener stopped unexpectedly");
        }
        finally
        {
            udpClient?.Dispose();
        }
    }

    private async Task ProbeHttpCameraEndpointsLoopAsync(CancellationToken stoppingToken)
    {
        var client = httpClientFactory.CreateClient(nameof(CameraStreamService));
        var probeInterval = TimeSpan.FromSeconds(Math.Clamp(options.ProbeIntervalSec, 3, 120));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var target = piTcpClientService.GetConnectionTarget();
                var candidates = BuildProbeUrls(target.Host, options.HttpProbePaths);
                var discovered = new List<string>();

                foreach (var url in candidates)
                {
                    var result = await ProbeUrlAsync(client, url, stoppingToken);
                    if (!result.Success)
                    {
                        continue;
                    }

                    discovered.Add(url);

                    if (result.JpegBytes is not null)
                    {
                        UpdateFrame(result.JpegBytes, $"http:{url}");
                    }
                }

                lock (frameLock)
                {
                    httpProbeCandidates = candidates;
                    httpDiscoveredStreams = discovered;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Camera HTTP probe failed");
            }

            await Task.Delay(probeInterval, stoppingToken);
        }
    }

    private async Task<ProbeResult> ProbeUrlAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Math.Clamp(options.HttpProbeTimeoutMs, 200, 10_000));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            return ProbeResult.NotFound;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if (contentType is null)
        {
            return ProbeResult.NotFound;
        }

        if (contentType.Contains("multipart/x-mixed-replace"))
        {
            return ProbeResult.SuccessNoSnapshot;
        }

        if (!contentType.Contains("image/jpeg") && !contentType.Contains("image/jpg"))
        {
            return ProbeResult.NotFound;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
        if (bytes.Length == 0 || bytes.Length > options.MaxFrameBytes)
        {
            return ProbeResult.SuccessNoSnapshot;
        }

        if (!LooksLikeJpeg(bytes))
        {
            return ProbeResult.SuccessNoSnapshot;
        }

        return ProbeResult.WithSnapshot(bytes);
    }

    private static List<string> BuildProbeUrls(string host, IEnumerable<string> paths)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
            normalized.Add($"http://{host}{normalizedPath}");
        }

        return normalized.ToList();
    }

    private void UpdateFrame(byte[] frame, string source)
    {
        var stamp = DateTimeOffset.UtcNow;

        lock (frameLock)
        {
            latestFrame = frame.ToArray();
            lastFrameAt = stamp;
            frameSource = source;
            framesReceived++;
            bytesReceived += frame.Length;
            frameVersion++;
        }
    }

    private static bool LooksLikeJpeg(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;
    }

    private readonly record struct ProbeResult(bool Success, byte[]? JpegBytes)
    {
        public static ProbeResult NotFound => new(false, null);

        public static ProbeResult SuccessNoSnapshot => new(true, null);

        public static ProbeResult WithSnapshot(byte[] bytes) => new(true, bytes);
    }
}
