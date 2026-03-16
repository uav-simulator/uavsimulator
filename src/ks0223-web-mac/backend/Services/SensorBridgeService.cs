using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Ks0223.Web.Backend.Hubs;
using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Ks0223.Web.Backend.Services;

public sealed class SensorBridgeService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    private readonly object stateLock = new();
    private readonly SensorBridgeOptions options;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly PiTcpClientService piTcpClientService;
    private readonly SessionLogger sessionLogger;
    private readonly IHubContext<TelemetryHub> hubContext;
    private readonly ILogger<SensorBridgeService> logger;
    private readonly string? clientGroup;
    private readonly string? sessionClientId;
    private readonly string sessionRuntimeMode;

    private SensorTelemetryDto? latestTelemetry;
    private DateTimeOffset? lastSuccessAt;
    private DateTimeOffset? lastTelemetryAt;
    private string? lastError;
    private int consecutiveFailures;
    private string? endpointUrl;

    public SensorBridgeService(
        IOptions<SensorBridgeOptions> options,
        IHttpClientFactory httpClientFactory,
        PiTcpClientService piTcpClientService,
        SessionLogger sessionLogger,
        IHubContext<TelemetryHub> hubContext,
        ILogger<SensorBridgeService> logger,
        string? clientGroup = null,
        string? sessionClientId = null,
        string sessionRuntimeMode = RuntimeModes.RealRobot)
    {
        this.options = options.Value;
        this.httpClientFactory = httpClientFactory;
        this.piTcpClientService = piTcpClientService;
        this.sessionLogger = sessionLogger;
        this.hubContext = hubContext;
        this.logger = logger;
        this.clientGroup = string.IsNullOrWhiteSpace(clientGroup) ? null : clientGroup.Trim();
        this.sessionClientId = string.IsNullOrWhiteSpace(sessionClientId) ? null : sessionClientId.Trim();
        this.sessionRuntimeMode = string.IsNullOrWhiteSpace(sessionRuntimeMode)
            ? RuntimeModes.RealRobot
            : RuntimeModes.Normalize(sessionRuntimeMode);
    }

    public SensorBridgeStatusDto GetStatus()
    {
        lock (stateLock)
        {
            return new SensorBridgeStatusDto(
                Enabled: options.Enabled,
                EndpointUrl: endpointUrl,
                PollIntervalMs: Math.Max(100, options.PollIntervalMs),
                HasTelemetry: latestTelemetry is not null,
                LastTelemetryAt: lastTelemetryAt,
                LastSuccessAt: lastSuccessAt,
                LastError: lastError,
                ConsecutiveFailures: consecutiveFailures);
        }
    }

    public SensorTelemetryDto? GetLatestTelemetry()
    {
        lock (stateLock)
        {
            return latestTelemetry;
        }
    }

    public async Task<SensorBridgeResponse> SendBridgeCommandAsync(string path, object payload, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return new SensorBridgeResponse(false, 503, null, "Sensor bridge is disabled in backend configuration");
        }

        var client = httpClientFactory.CreateClient(nameof(SensorBridgeService));
        var target = piTcpClientService.GetConnectionTarget();
        var url = BuildBridgeUrl(target.Host, path);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(options.RequestTimeoutMs, 300, 10_000)));

            var payloadJson = JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions);
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var result = new SensorBridgeResponse(response.IsSuccessStatusCode, (int)response.StatusCode, body, response.IsSuccessStatusCode ? null : body);

                await sessionLogger.WriteAsync(
                    "sensor.bridge.command",
                    new
                    {
                        clientId = sessionClientId,
                        runtimeMode = sessionRuntimeMode,
                        url,
                        status = result.StatusCode,
                        sent = result.Sent,
                    payload = payloadJson,
                    body = body.Length > 1200 ? body[..1200] : body,
                },
                cancellationToken);

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SensorBridgeResponse(false, 408, null, $"Timeout calling sensor bridge endpoint {url}");
        }
        catch (Exception ex)
        {
            return new SensorBridgeResponse(false, 500, null, $"Sensor bridge command failed: {ex.Message}");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            await ResolveHubClients().SendAsync("sensorStatus", GetStatus(), stoppingToken);
            return;
        }

        var client = httpClientFactory.CreateClient(nameof(SensorBridgeService));
        var pollDelay = TimeSpan.FromMilliseconds(Math.Max(100, options.PollIntervalMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            var target = piTcpClientService.GetConnectionTarget();
            var url = BuildBridgeUrl(target.Host, options.TelemetryPath);
            UpdateEndpoint(url);

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(options.RequestTimeoutMs, 300, 10_000)));

                using var response = await client.GetAsync(url, timeoutCts.Token);
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                var telemetry = ParseTelemetry(payload, url);
                lock (stateLock)
                {
                    latestTelemetry = telemetry;
                    lastTelemetryAt = telemetry.Timestamp;
                    lastSuccessAt = telemetry.Timestamp;
                    lastError = null;
                    consecutiveFailures = 0;
                }

                await sessionLogger.WriteAsync(
                    "sensor.telemetry.incoming",
                    new
                    {
                        clientId = sessionClientId,
                        runtimeMode = sessionRuntimeMode,
                        source = telemetry.SourceUrl,
                        size = payload.Length,
                        fields = telemetry.Flat.Count,
                        timestamp = telemetry.Timestamp,
                    },
                    stoppingToken);

                await ResolveHubClients().SendAsync("sensorTelemetry", telemetry, stoppingToken);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                RecordError($"Sensor telemetry timeout to {url}");
            }
            catch (Exception ex)
            {
                RecordError($"Sensor telemetry fetch failed: {ex.Message}");
                logger.LogDebug(ex, "Sensor telemetry fetch failed from {Url}", url);
            }

            await ResolveHubClients().SendAsync("sensorStatus", GetStatus(), stoppingToken);
            await Task.Delay(pollDelay, stoppingToken);
        }
    }

    private SensorTelemetryDto ParseTelemetry(string payload, string sourceUrl)
    {
        using var document = JsonDocument.Parse(payload);
        var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Flatten(document.RootElement, string.Empty, flat);
        return new SensorTelemetryDto(DateTimeOffset.UtcNow, sourceUrl, payload, flat);
    }

    private void RecordError(string error)
    {
        lock (stateLock)
        {
            lastError = error;
            consecutiveFailures++;
        }
    }

    private void UpdateEndpoint(string url)
    {
        lock (stateLock)
        {
            endpointUrl = url;
        }
    }

    private string BuildBridgeUrl(string host, string path)
    {
        var cleanedPath = path.StartsWith('/') ? path : $"/{path}";
        return $"http://{host}:{options.Port}{cleanedPath}";
    }

    private static void Flatten(JsonElement element, string prefix, IDictionary<string, string> output)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var next = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    Flatten(prop.Value, next, output);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, $"{prefix}[{index}]", output);
                    index++;
                }
                if (index == 0 && !string.IsNullOrEmpty(prefix))
                {
                    output[prefix] = "[]";
                }
                break;
            case JsonValueKind.String:
                if (!string.IsNullOrEmpty(prefix))
                {
                    output[prefix] = element.GetString() ?? string.Empty;
                }
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (!string.IsNullOrEmpty(prefix))
                {
                    output[prefix] = element.ToString();
                }
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                if (!string.IsNullOrEmpty(prefix))
                {
                    output[prefix] = "null";
                }
                break;
            default:
                if (!string.IsNullOrEmpty(prefix))
                {
                    output[prefix] = element.GetRawText();
                }
                break;
        }
    }

    private IClientProxy ResolveHubClients() =>
        string.IsNullOrWhiteSpace(clientGroup)
            ? hubContext.Clients.All
            : hubContext.Clients.Group(clientGroup);
}
