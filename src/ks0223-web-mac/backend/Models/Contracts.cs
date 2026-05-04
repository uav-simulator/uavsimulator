namespace Ks0223.Web.Backend.Models;

public sealed record CommandRequest(
    string ClientId,
    string RuntimeMode,
    string Command,
    string? AgentId = null);

public sealed record CommandResponse(bool Sent, string? Error = null);

public sealed record ConnectRequest(
    string ClientId,
    string RuntimeMode,
    string? Host,
    int? Port);

public sealed record DisconnectRequest(
    string ClientId,
    string RuntimeMode);

public sealed record UnityRuntimeOptionDto(string Id, string DisplayName);

public sealed record UnityRuntimeAgentDto(
    string AgentId,
    string VehicleId,
    string DisplayName,
    bool IsPrimary);

public sealed record UnityRuntimeCatalogDto(
    string SelectedTrackId,
    string SelectedVehicleId,
    string SelectedCameraMode,
    string SelectedControlAgentId,
    string SelectedCameraAgentId,
    IReadOnlyList<UnityRuntimeOptionDto> Tracks,
    IReadOnlyList<UnityRuntimeOptionDto> Vehicles,
    IReadOnlyList<UnityRuntimeAgentDto> Agents);

public sealed record UnityRuntimeAgentSelectionRequest(
    string? AgentId,
    string? VehicleId,
    bool IsPrimary = false);

public sealed record UnityRuntimeSelectionRequest(
    string ClientId,
    string RuntimeMode,
    string? TrackId,
    string? VehicleId,
    string? CameraMode,
    IReadOnlyList<UnityRuntimeAgentSelectionRequest>? Agents,
    bool ApplyImmediately = true,
    bool? CollisionsEnabled = null,
    bool? SeeEachOther = null);

public sealed record UnityClientSelectionRequest(
    string ClientId,
    string RuntimeMode,
    string? ControlAgentId,
    string? CameraAgentId);

public sealed record ConnectionTargetDto(string Host, int Port, string RuntimeMode = "real-robot");

public sealed record StartLoggingRequest(string? Tag);

public sealed record DemoStartRequest(string? Tag, string? ClientId, string? RuntimeMode);

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

public sealed record ModelInfoDto(
    string ModelId,
    string Name,
    string Version,
    string Source,
    DateTimeOffset CreatedAtUtc,
    bool IsActive,
    string ArtifactPath,
    string MetadataPath,
    string MetricsPath,
    CompatibilityHintsDto Compatibility);

public sealed record CompatibilityHintsDto(
    IReadOnlyList<string> RuntimeModes,
    IReadOnlyList<string> VehicleIds,
    IReadOnlyList<string> RobotKinds);

public sealed record ModelCatalogEntryDto(
    string Name,
    IReadOnlyList<ModelInfoDto> Versions);

public sealed record SetModelBindingRequest(
    string ClientId,
    string RuntimeMode,
    string? AgentId,
    string ModelId);

public sealed record ModelBindingDto(
    string ClientId,
    string RuntimeMode,
    string? AgentId,
    string ModelId,
    string Name,
    string Version,
    string Source,
    DateTimeOffset BoundAtUtc,
    CompatibilityHintsDto Compatibility);

public sealed record ActivateModelRequest(string ModelId);

public sealed record ImageFeaturesDto(
    float BrightnessMean,
    float BrightnessStdDev,
    float EdgeScoreTop,
    float EdgeScoreBottom);

public sealed record PreviewSampleDto(
    bool Ok,
    string ModelId,
    string? Reason,
    float[]? Logits,
    float[]? Probabilities,
    string? ChosenAction,
    int? ChosenIndex,
    float? FrontUltrasonicM,
    ImageFeaturesDto? ImageFeatures = null,
    string? GuardReason = null);

public sealed record StartAutopilotRequest(
    string ClientId,
    string RuntimeMode,
    string? AgentId = null,
    string? ModelId = null,
    int? LoopIntervalMs = null,
    int? MaxDurationSeconds = null);

public sealed record StopAutopilotRequest(
    string? ClientId = null,
    string? RuntimeMode = null);

public sealed record AutopilotStatusDto(
    bool IsRunning,
    string? ClientId,
    string? RuntimeMode,
    string? AgentId,
    string? ModelId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastStepAtUtc,
    long StepsTotal,
    long CommandsSent,
    string? LastCommand,
    float LastThrottle,
    float LastSteer,
    string? LastError,
    string Mode,
    bool SafetyEnabled = true,
    bool EStopActive = false,
    long EStopTriggerCount = 0,
    float ThrottleMax = 0.5f,
    int MaxDurationSeconds = 0,
    int RepeatedCommandCount = 0,
    string? StopReason = null);

// ── Demo replay ──

public enum DemoReplayState
{
    Idle,
    Loading,
    Playing,
    Done,
    Error,
    Stopped,
}

public sealed record DemoReplayStartRequest(
    string ClientId,
    string RuntimeMode,
    string SessionFilePath,
    string? AgentId = null,
    double? SpeedMultiplier = 1.0);

public sealed record DemoReplayInfo(
    int TotalCommands,
    int EstimatedDurationMs,
    string SessionFile);

public sealed record DemoReplayProgress(
    string State,
    int CurrentIndex,
    int TotalCommands,
    string? CurrentTimestampUtc,
    DateTimeOffset? StartedAtUtc,
    int ElapsedMs,
    string? LastCommand,
    string? LastError);

public sealed record DemoSessionFileDto(
    string FileName,
    string FilePath,
    long SizeKb,
    DateTimeOffset LastWriteUtc,
    int CommandCount);

public sealed record LoadScenarioRequest(string FilePath);

