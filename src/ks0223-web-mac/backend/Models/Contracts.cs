namespace Ks0223.Web.Backend.Models;

public sealed record CommandRequest(string Command);

public sealed record CommandResponse(bool Sent, string? Error = null);

public sealed record ConnectRequest(string? Host, int? Port, string? RuntimeMode = null);

public sealed record ConnectionTargetDto(string Host, int Port, string RuntimeMode = "real-robot");

public sealed record StartLoggingRequest(string? Tag);

public sealed record LogState(bool IsLogging, string? CurrentFile);

public sealed record LogFileInfo(string Name, string AbsolutePath, long SizeBytes, DateTimeOffset LastWriteTimeUtc);

public sealed record IncomingMessageDto(
    DateTimeOffset Timestamp,
    string Message,
    IReadOnlyDictionary<string, string>? ParsedTelemetry);

public sealed record StatusDto(
    bool DesiredConnection,
    bool TcpConnected,
    int UiConnectedClients,
    string TargetHost,
    int TargetPort,
    double? LatencyMs,
    string? LastError,
    DateTimeOffset? LastTcpMessageAt,
    bool IsLogging,
    string? CurrentLogFile,
    bool HasParsedTelemetry,
    string RuntimeMode = "real-robot",
    string? RuntimeLabel = null);

public sealed record CameraStatusDto(
    bool UdpListenerEnabled,
    int UdpListenPort,
    bool HasFrame,
    DateTimeOffset? LastFrameAt,
    string? Source,
    long FramesReceived,
    long BytesReceived,
    IReadOnlyList<string> HttpProbeCandidates,
    IReadOnlyList<string> HttpDiscoveredStreams);

public sealed record SensorTelemetryDto(
    DateTimeOffset Timestamp,
    string SourceUrl,
    string RawJson,
    IReadOnlyDictionary<string, string> Flat);

public sealed record SensorBridgeStatusDto(
    bool Enabled,
    string? EndpointUrl,
    int PollIntervalMs,
    bool HasTelemetry,
    DateTimeOffset? LastTelemetryAt,
    DateTimeOffset? LastSuccessAt,
    string? LastError,
    int ConsecutiveFailures);

public sealed record SensorBridgeCommandRequest(
    IReadOnlyDictionary<string, object?> Payload);

public sealed record SensorBridgeResponse(
    bool Sent,
    int StatusCode,
    string? Body,
    string? Error = null);

public sealed record SensorConfigRequest(
    bool? AutoScanEnabled,
    int? SampleIntervalMs,
    double? ScanIntervalSec,
    int? ScanSettleMs,
    int? DriveSpeedPercent,
    int? CameraSpeedPercent);

public sealed record UltrasonicPositionRequest(int AngleDeg, bool DisableAutoScan = true);

public sealed record UltrasonicAutoScanRequest(bool Enabled);

public sealed record LedPatternRequest(string Pattern);

public sealed record LedCustomFrameRequest(string FrameHex);

public sealed record HealthDto(
    string Status,
    DateTimeOffset Timestamp,
    string Version,
    StatusDto Control,
    CameraStatusDto Camera,
    SensorBridgeStatusDto Sensors);
