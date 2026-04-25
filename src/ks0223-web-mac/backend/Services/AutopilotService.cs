using Ks0223.Web.Backend.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Globalization;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Ks0223.Web.Backend.Services;

public sealed class AutopilotService
{
    private const int MinLoopIntervalMs = 80;
    private const int MaxLoopIntervalMs = 1000;

    private readonly object gate = new();
    private readonly RuntimeSessionManager runtimeSessionManager;
    private readonly ModelRegistryService modelRegistry;
    private readonly AutopilotSafetyFilter safetyFilter;
    private readonly ILogger<AutopilotService> logger;

    private AutopilotState state = AutopilotState.Stopped();

    public AutopilotService(
        RuntimeSessionManager runtimeSessionManager,
        ModelRegistryService modelRegistry,
        AutopilotSafetyFilter safetyFilter,
        ILogger<AutopilotService> logger)
    {
        this.runtimeSessionManager = runtimeSessionManager;
        this.modelRegistry = modelRegistry;
        this.safetyFilter = safetyFilter;
        this.logger = logger;
    }

    public AutopilotStatusDto GetStatus(string? clientId = null, string? runtimeMode = null)
    {
        lock (gate)
        {
            if (state.IsRunning)
            {
                if (!string.IsNullOrWhiteSpace(clientId) &&
                    !string.Equals(state.ClientId, clientId.Trim(), StringComparison.Ordinal))
                {
                    return AutopilotState.Stopped().ToDto();
                }

                if (!string.IsNullOrWhiteSpace(runtimeMode))
                {
                    var normalizedMode = RuntimeModes.Normalize(runtimeMode);
                    if (!string.Equals(state.RuntimeMode, normalizedMode, StringComparison.Ordinal))
                    {
                        return AutopilotState.Stopped().ToDto();
                    }
                }
            }

            return state.ToDto();
        }
    }

    public async Task<AutopilotStatusDto> StartAsync(StartAutopilotRequest request, CancellationToken cancellationToken)
    {
        var clientId = NormalizeRequired(request.ClientId, nameof(request.ClientId));
        var runtimeMode = RuntimeModes.Normalize(request.RuntimeMode);
        var agentId = string.IsNullOrWhiteSpace(request.AgentId) ? null : request.AgentId.Trim();
        var loopIntervalMs = Math.Clamp(request.LoopIntervalMs ?? 140, MinLoopIntervalMs, MaxLoopIntervalMs);

        var model = modelRegistry.ResolveRuntimeSpec(request.ModelId, clientId, runtimeMode, agentId);

        var predictor = new PolicyPredictor(model, logger);
        await StopIfRunningAsync("restart", cancellationToken);

        var status = runtimeSessionManager.GetStatus(clientId, runtimeMode);
        if (!status.TcpConnected)
        {
            predictor.Dispose();
            throw new InvalidOperationException("Runtime is not connected. Connect first and then start autopilot.");
        }

        _ = cancellationToken;
        var cts = new CancellationTokenSource();
        var running = AutopilotState.Running(
            clientId,
            runtimeMode,
            agentId,
            model.ModelId,
            predictor,
            loopIntervalMs,
            cts);

        lock (gate)
        {
            state = running;
        }

        safetyFilter.Reset();
        running.LoopTask = Task.Run(() => LoopAsync(running), CancellationToken.None);
        logger.LogInformation("Autopilot started for {ClientId}/{RuntimeMode} with model {ModelId}", clientId, runtimeMode, model.ModelId);
        return running.ToDto();
    }

    public async Task<AutopilotStatusDto> StopAsync(StopAutopilotRequest request, CancellationToken cancellationToken)
    {
        var expectedClientId = string.IsNullOrWhiteSpace(request.ClientId) ? null : request.ClientId.Trim();
        var expectedMode = string.IsNullOrWhiteSpace(request.RuntimeMode) ? null : RuntimeModes.Normalize(request.RuntimeMode);
        await StopIfRunningAsync("api-stop", cancellationToken, expectedClientId, expectedMode);
        return GetStatus();
    }

    public async Task HandleManualOverrideAsync(string clientId, string runtimeMode, CancellationToken cancellationToken)
    {
        var normalizedClientId = NormalizeRequired(clientId, nameof(clientId));
        var normalizedMode = RuntimeModes.Normalize(runtimeMode);
        await StopIfRunningAsync("manual-command", cancellationToken, normalizedClientId, normalizedMode);
    }

    private async Task LoopAsync(AutopilotState running)
    {
        var token = running.Cancellation!.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var telemetry = runtimeSessionManager.GetLatestSensorTelemetry(running.ClientId!, running.RuntimeMode!);
                if (telemetry is null)
                {
                    await Task.Delay(running.LoopIntervalMs, token);
                    continue;
                }

                byte[]? frameBytes = null;
                if (running.Predictor!.RequiresFrame)
                {
                    if (!runtimeSessionManager.TryGetLatestFrame(
                            running.ClientId!,
                            running.RuntimeMode!,
                            running.AgentId,
                            out var latestFrame,
                            out _,
                            out _,
                            out _))
                    {
                        await Task.Delay(running.LoopIntervalMs, token);
                        continue;
                    }

                    frameBytes = latestFrame;
                }

                var (rawThrottle, rawSteer) = running.Predictor!.Predict(telemetry.Flat, frameBytes);
                var frontM = ExtractFrontMeters(telemetry.Flat);
                var decision = safetyFilter.Apply(rawThrottle, rawSteer, frontM);
                var throttle = decision.Throttle;
                var steer = decision.Steer;
                var command = decision.EStopActive ? "DirStop" : ResolveCommand(throttle, steer);

                runtimeSessionManager.SetDirectDrive(
                    running.ClientId!,
                    running.RuntimeMode!,
                    running.AgentId,
                    throttle,
                    steer);

                var response = await runtimeSessionManager.SendCommandAsync(
                    running.ClientId!,
                    running.RuntimeMode!,
                    command,
                    running.AgentId,
                    token);

                var safetyStatus = safetyFilter.GetStatus();
                lock (gate)
                {
                    if (!ReferenceEquals(state, running))
                    {
                        break;
                    }

                    running.LastStepAtUtc = DateTimeOffset.UtcNow;
                    running.StepsTotal += 1;
                    running.LastThrottle = throttle;
                    running.LastSteer = steer;
                    running.LastCommand = command;
                    running.EStopActive = decision.EStopActive;
                    running.EStopTriggerCount = safetyStatus.EStopTriggerCount;

                    if (response.Sent)
                    {
                        running.CommandsSent += 1;
                        running.LastError = null;
                    }
                    else
                    {
                        running.LastError = response.Error ?? "autopilot command rejected";
                    }
                }

                await Task.Delay(running.LoopIntervalMs, token);
            }
        }
        catch (OperationCanceledException)
        {
            // no-op
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Autopilot loop failed");
            try
            {
                await runtimeSessionManager.SendCommandAsync(
                    running.ClientId!,
                    running.RuntimeMode!,
                    "DirStop",
                    running.AgentId,
                    CancellationToken.None);
            }
            catch (Exception stopEx)
            {
                logger.LogDebug(stopEx, "Failed to send DirStop after autopilot failure");
            }

            running.Predictor?.Dispose();
            lock (gate)
            {
                if (ReferenceEquals(state, running))
                {
                    state = AutopilotState.StoppedFrom(running, ex.Message);
                }
            }
        }
    }

    private async Task StopIfRunningAsync(
        string reason,
        CancellationToken cancellationToken,
        string? expectedClientId = null,
        string? expectedRuntimeMode = null)
    {
        AutopilotState snapshot;
        lock (gate)
        {
            snapshot = state;
        }

        if (!snapshot.IsRunning)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(expectedClientId) &&
            !string.Equals(snapshot.ClientId, expectedClientId, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(expectedRuntimeMode) &&
            !string.Equals(snapshot.RuntimeMode, expectedRuntimeMode, StringComparison.Ordinal))
        {
            return;
        }

        snapshot.Cancellation?.Cancel();
        if (snapshot.LoopTask is not null)
        {
            try
            {
                await snapshot.LoopTask;
            }
            catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
            {
                // no-op
            }
        }

        try
        {
            await runtimeSessionManager.SendCommandAsync(
                snapshot.ClientId!,
                snapshot.RuntimeMode!,
                "DirStop",
                snapshot.AgentId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to send DirStop during autopilot stop");
        }

        snapshot.Predictor?.Dispose();
        lock (gate)
        {
            if (ReferenceEquals(state, snapshot))
            {
                state = AutopilotState.StoppedFrom(
                    snapshot,
                    reason == "manual-command" ? "Autopilot stopped by manual override" : null);
            }
        }

        logger.LogInformation("Autopilot stopped ({Reason})", reason);
    }

    private static float[] BuildObservationVector(IReadOnlyDictionary<string, string> flat)
    {
        var left = ParseValue(flat, "sensor.line_tracker.s1_norm", "line_tracker.s1_norm", "tracking.left");
        var center = ParseValue(flat, "sensor.line_tracker.s3_norm", "line_tracker.s3_norm", "tracking.center");
        var right = ParseValue(flat, "sensor.line_tracker.s5_norm", "line_tracker.s5_norm", "tracking.right");

        var distanceCm = ParseValue(
            flat,
            "sensor.range.front_cm",
            "ultrasonic.distance_cm",
            "ultrasonic.scan.center_cm");
        var speedMps = ParseValue(flat, "sensor.speedometer.mps", "speedometer.current_mps", "speedometer.mps");

        var distanceNorm = Clamp01(distanceCm / 100f);
        var speedNorm = Clamp01(speedMps / 2f);

        return
        [
            Clamp01(left),
            Clamp01(center),
            Clamp01(right),
            distanceNorm,
            speedNorm,
            1f,
        ];
    }

    private static float ExtractFrontMeters(IReadOnlyDictionary<string, string> flat)
    {
        var frontMeters = ParseValue(flat, "sensor.ultrasonic.front.m", "ultrasonic.front_m", "ultrasonic.front.m");
        if (frontMeters <= 0f)
        {
            var cm = ParseValue(flat, "sensor.range.front_cm", "ultrasonic.distance_cm", "ultrasonic.scan.center_cm");
            if (cm > 0f)
            {
                frontMeters = cm / 100f;
            }
        }

        return frontMeters;
    }

    private static float ParseValue(IReadOnlyDictionary<string, string> flat, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!flat.TryGetValue(key, out var raw))
            {
                continue;
            }

            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return 0f;
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static string ResolveCommand(float throttle, float steer)
    {
        if (throttle < -0.25f)
        {
            return "DirBack";
        }

        if (Math.Abs(throttle) < 0.15f)
        {
            if (steer > 0.55f)
            {
                return "DirLeft";
            }

            if (steer < -0.55f)
            {
                return "DirRight";
            }

            return "DirStop";
        }

        if (steer > 0.45f)
        {
            return "DirLeft";
        }

        if (steer < -0.45f)
        {
            return "DirRight";
        }

        return "DirForward";
    }

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required");
        }

        return value.Trim();
    }

    private sealed class PolicyPredictor : IDisposable
    {
        private readonly ILogger logger;
        private readonly InferenceSession? onnxSession = null;
        private readonly PredictorMode mode = PredictorMode.FlatVector;
        private readonly string? flatInputName = null;
        private readonly int flatInputSize = 0;
        private readonly string? imageInputName = null;
        private readonly string? ultrasonicInputName = null;
        private readonly int imageHeight = 84;
        private readonly int imageWidth = 84;
        private readonly int imageChannels = 3;

        public PolicyPredictor(ModelRuntimeSpec model, ILogger logger)
        {
            this.logger = logger;
            try
            {
                if (!File.Exists(model.ArtifactPath))
                {
                    throw new InvalidOperationException($"Model artifact not found: {model.ArtifactPath}");
                }

                onnxSession = new InferenceSession(model.ArtifactPath);
                if (TryResolveVisionMode(onnxSession, model.MetadataPath, out var visionSpec))
                {
                    mode = PredictorMode.ImageAndUltrasonic;
                    imageInputName = visionSpec.ImageInputName;
                    ultrasonicInputName = visionSpec.UltrasonicInputName;
                    imageHeight = visionSpec.ImageHeight;
                    imageWidth = visionSpec.ImageWidth;
                    imageChannels = visionSpec.ImageChannels;
                }
                else
                {
                    var firstInput = onnxSession.InputMetadata.FirstOrDefault();
                    flatInputName = firstInput.Key;
                    flatInputSize = ResolveInputSize(firstInput.Value?.Dimensions);
                    if (string.IsNullOrWhiteSpace(flatInputName))
                    {
                        throw new InvalidOperationException("ONNX model has no declared inputs");
                    }
                }

                this.logger.LogInformation(
                    "Loaded ONNX model {ModelId} from {Path} with autopilot mode {Mode}",
                    model.ModelId,
                    model.ArtifactPath,
                    mode);
            }
            catch (Exception ex)
            {
                onnxSession?.Dispose();
                throw new InvalidOperationException(
                    $"Failed to load ONNX model '{model.ModelId}' for autopilot runtime",
                    ex);
            }
        }

        public bool RequiresFrame => mode == PredictorMode.ImageAndUltrasonic;

        public (float throttle, float steer) Predict(IReadOnlyDictionary<string, string> telemetry, byte[]? frameBytes)
        {
            var inputs = BuildInputs(telemetry, frameBytes);
            try
            {
                using var results = onnxSession!.Run(inputs);
                var first = results.FirstOrDefault();
                if (first is null)
                {
                    throw new InvalidOperationException("ONNX model produced no outputs");
                }

                var output = first.AsEnumerable<float>().ToArray();
                if (output.Length == 0)
                {
                    throw new InvalidOperationException("ONNX model produced an empty action tensor");
                }

                var throttle = Math.Clamp(output[0], -1f, 1f);
                var steer = output.Length > 1 ? Math.Clamp(output[1], -1f, 1f) : 0f;
                return (throttle, steer);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("ONNX inference failed during autopilot loop", ex);
            }
        }

        private List<NamedOnnxValue> BuildInputs(IReadOnlyDictionary<string, string> telemetry, byte[]? frameBytes)
        {
            if (mode == PredictorMode.ImageAndUltrasonic)
            {
                if (frameBytes is null || frameBytes.Length == 0)
                {
                    throw new InvalidOperationException("Vision model requires the latest camera frame");
                }

                var imageTensor = BuildImageTensor(frameBytes, imageHeight, imageWidth, imageChannels);
                var ultrasonicTensor = new DenseTensor<float>(new[] { 1, 1 });
                ultrasonicTensor[0, 0] = ExtractNormalizedUltrasonic(telemetry);

                return
                [
                    NamedOnnxValue.CreateFromTensor(imageInputName!, imageTensor),
                    NamedOnnxValue.CreateFromTensor(ultrasonicInputName!, ultrasonicTensor),
                ];
            }

            var observation = BuildObservationVector(telemetry);
            var size = flatInputSize <= 0 ? observation.Length : flatInputSize;
            var tensor = new DenseTensor<float>(new[] { 1, size });
            var copy = Math.Min(size, observation.Length);
            for (var index = 0; index < copy; index += 1)
            {
                tensor[0, index] = observation[index];
            }

            return [NamedOnnxValue.CreateFromTensor(flatInputName!, tensor)];
        }

        private static bool TryResolveVisionMode(
            InferenceSession session,
            string metadataPath,
            out VisionInputSpec spec)
        {
            spec = new VisionInputSpec(string.Empty, string.Empty, 0, 0, 0);
            var metadata = TryReadMetadata(metadataPath);
            var imageEntry = FindImageInput(session);
            var ultrasonicEntry = FindUltrasonicInput(session, imageEntry.Key);
            if (string.IsNullOrWhiteSpace(imageEntry.Key) || string.IsNullOrWhiteSpace(ultrasonicEntry.Key))
            {
                return false;
            }

            if (!TryResolveImageShape(imageEntry.Value?.Dimensions, metadata.ImageShape, out var imageHeight, out var imageWidth, out var imageChannels))
            {
                return false;
            }

            spec = new VisionInputSpec(imageEntry.Key, ultrasonicEntry.Key, imageHeight, imageWidth, imageChannels);
            return true;
        }

        private static KeyValuePair<string, NodeMetadata> FindImageInput(InferenceSession session)
        {
            foreach (var input in session.InputMetadata)
            {
                if (string.Equals(input.Key, "image", StringComparison.OrdinalIgnoreCase))
                {
                    return input;
                }
            }

            foreach (var input in session.InputMetadata)
            {
                var dimensions = input.Value?.Dimensions;
                if (dimensions is not null && dimensions.Length >= 4)
                {
                    return input;
                }
            }

            return default;
        }

        private static KeyValuePair<string, NodeMetadata> FindUltrasonicInput(InferenceSession session, string imageInputName)
        {
            foreach (var input in session.InputMetadata)
            {
                if (string.Equals(input.Key, imageInputName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(input.Key, "ultrasonic", StringComparison.OrdinalIgnoreCase))
                {
                    return input;
                }
            }

            foreach (var input in session.InputMetadata)
            {
                if (string.Equals(input.Key, imageInputName, StringComparison.Ordinal))
                {
                    continue;
                }

                var size = ResolveInputSize(input.Value?.Dimensions);
                if (size == 1)
                {
                    return input;
                }
            }

            return default;
        }

        private static bool TryResolveImageShape(
            int[]? onnxDimensions,
            int[]? metadataShape,
            out int imageHeight,
            out int imageWidth,
            out int imageChannels)
        {
            imageHeight = 0;
            imageWidth = 0;
            imageChannels = 0;

            var fromOnnx = onnxDimensions?
                .Where(value => value > 0)
                .TakeLast(3)
                .ToArray();
            var candidate = fromOnnx is { Length: 3 }
                ? fromOnnx
                : metadataShape is { Length: >= 3 } ? metadataShape.Take(3).ToArray() : null;
            if (candidate is null || candidate.Length < 3)
            {
                return false;
            }

            imageHeight = candidate[0];
            imageWidth = candidate[1];
            imageChannels = candidate[2];
            return imageHeight > 0 && imageWidth > 0 && imageChannels > 0;
        }

        private static ModelMetadataSpec TryReadMetadata(string metadataPath)
        {
            if (string.IsNullOrWhiteSpace(metadataPath) || !File.Exists(metadataPath))
            {
                return ModelMetadataSpec.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
                if (!document.RootElement.TryGetProperty("observationSchema", out var schema))
                {
                    return ModelMetadataSpec.Empty;
                }

                return new ModelMetadataSpec(
                    ReadShape(schema, "image"),
                    ReadShape(schema, "ultrasonic"));
            }
            catch
            {
                return ModelMetadataSpec.Empty;
            }
        }

        private static int[]? ReadShape(JsonElement schema, string propertyName)
        {
            if (!schema.TryGetProperty(propertyName, out var entry))
            {
                return null;
            }

            if (!entry.TryGetProperty("shape", out var shapeElement) || shapeElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var values = new List<int>();
            foreach (var item in shapeElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var value))
                {
                    values.Add(value);
                }
            }

            return values.Count == 0 ? null : values.ToArray();
        }

        private static DenseTensor<float> BuildImageTensor(byte[] frameBytes, int imageHeight, int imageWidth, int imageChannels)
        {
            using var image = Image.Load<Rgb24>(frameBytes);
            image.Mutate(context => context.Resize(imageWidth, imageHeight));

            var tensor = new DenseTensor<float>(new[] { 1, imageHeight, imageWidth, imageChannels });
            for (var y = 0; y < imageHeight; y += 1)
            {
                for (var x = 0; x < imageWidth; x += 1)
                {
                    var pixel = image[x, y];
                    if (imageChannels == 1)
                    {
                        tensor[0, y, x, 0] = ((0.299f * pixel.R) + (0.587f * pixel.G) + (0.114f * pixel.B));
                        continue;
                    }

                    tensor[0, y, x, 0] = pixel.R;
                    tensor[0, y, x, 1] = pixel.G;
                    tensor[0, y, x, 2] = pixel.B;
                }
            }

            return tensor;
        }

        private static float ExtractNormalizedUltrasonic(IReadOnlyDictionary<string, string> telemetry)
        {
            var frontMeters = ParseValue(
                telemetry,
                "sensor.ultrasonic.front.m",
                "ultrasonic.front_m",
                "ultrasonic.front.m");
            if (frontMeters <= 0f)
            {
                var distanceCentimeters = ParseValue(
                    telemetry,
                    "sensor.range.front_cm",
                    "ultrasonic.distance_cm",
                    "ultrasonic.scan.center_cm");
                if (distanceCentimeters > 0f)
                {
                    frontMeters = distanceCentimeters / 100f;
                }
            }

            return Clamp01(frontMeters / 5f);
        }

        private static int ResolveInputSize(int[]? dimensions)
        {
            if (dimensions is null || dimensions.Length == 0)
            {
                return 0;
            }

            var known = dimensions.Where(value => value > 0).ToArray();
            if (known.Length == 0)
            {
                return 0;
            }

            if (dimensions.Length >= 2 && dimensions[0] == 1 && dimensions[1] > 0)
            {
                return dimensions[1];
            }

            return known.Aggregate(1, (acc, value) => acc * value);
        }

        public void Dispose()
        {
            onnxSession?.Dispose();
        }

        private enum PredictorMode
        {
            FlatVector,
            ImageAndUltrasonic,
        }

        private sealed record VisionInputSpec(
            string ImageInputName,
            string UltrasonicInputName,
            int ImageHeight,
            int ImageWidth,
            int ImageChannels);

        private sealed record ModelMetadataSpec(int[]? ImageShape, int[]? UltrasonicShape)
        {
            public static ModelMetadataSpec Empty { get; } = new(null, null);
        }
    }

    private sealed class AutopilotState
    {
        public bool IsRunning { get; private set; }
        public string? ClientId { get; private set; }
        public string? RuntimeMode { get; private set; }
        public string? AgentId { get; private set; }
        public string? ModelId { get; private set; }
        public DateTimeOffset? StartedAtUtc { get; private set; }
        public DateTimeOffset? LastStepAtUtc { get; set; }
        public long StepsTotal { get; set; }
        public long CommandsSent { get; set; }
        public string? LastCommand { get; set; }
        public float LastThrottle { get; set; }
        public float LastSteer { get; set; }
        public string? LastError { get; set; }
        public string Mode => IsRunning ? "autopilot" : "manual";
        public int LoopIntervalMs { get; private set; }
        public PolicyPredictor? Predictor { get; private set; }
        public CancellationTokenSource? Cancellation { get; private set; }
        public Task? LoopTask { get; set; }
        public bool EStopActive { get; set; }
        public long EStopTriggerCount { get; set; }

        public static AutopilotState Running(
            string clientId,
            string runtimeMode,
            string? agentId,
            string modelId,
            PolicyPredictor predictor,
            int loopIntervalMs,
            CancellationTokenSource cancellation) =>
            new()
            {
                IsRunning = true,
                ClientId = clientId,
                RuntimeMode = runtimeMode,
                AgentId = agentId,
                ModelId = modelId,
                StartedAtUtc = DateTimeOffset.UtcNow,
                LoopIntervalMs = loopIntervalMs,
                Predictor = predictor,
                Cancellation = cancellation,
            };

        public static AutopilotState Stopped(string? lastError = null) =>
            new()
            {
                IsRunning = false,
                LastError = lastError,
            };

        public static AutopilotState StoppedFrom(AutopilotState previous, string? lastError = null) =>
            new()
            {
                IsRunning = false,
                ClientId = previous.ClientId,
                RuntimeMode = previous.RuntimeMode,
                AgentId = previous.AgentId,
                ModelId = previous.ModelId,
                StartedAtUtc = previous.StartedAtUtc,
                LastStepAtUtc = previous.LastStepAtUtc,
                StepsTotal = previous.StepsTotal,
                CommandsSent = previous.CommandsSent,
                LastCommand = previous.LastCommand,
                LastThrottle = previous.LastThrottle,
                LastSteer = previous.LastSteer,
                LastError = lastError,
                EStopTriggerCount = previous.EStopTriggerCount,
            };

        public AutopilotStatusDto ToDto() =>
            new(
                IsRunning,
                ClientId,
                RuntimeMode,
                AgentId,
                ModelId,
                StartedAtUtc,
                LastStepAtUtc,
                StepsTotal,
                CommandsSent,
                LastCommand,
                LastThrottle,
                LastSteer,
                LastError,
                Mode,
                EStopActive: EStopActive,
                EStopTriggerCount: EStopTriggerCount);
    }
}
