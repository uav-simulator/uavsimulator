using Ks0223.Web.Backend.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Globalization;

namespace Ks0223.Web.Backend.Services;

public sealed class AutopilotService
{
    private const int MinLoopIntervalMs = 80;
    private const int MaxLoopIntervalMs = 1000;

    private readonly object gate = new();
    private readonly RuntimeSessionManager runtimeSessionManager;
    private readonly ModelRegistryService modelRegistry;
    private readonly ILogger<AutopilotService> logger;

    private AutopilotState state = AutopilotState.Stopped();

    public AutopilotService(
        RuntimeSessionManager runtimeSessionManager,
        ModelRegistryService modelRegistry,
        ILogger<AutopilotService> logger)
    {
        this.runtimeSessionManager = runtimeSessionManager;
        this.modelRegistry = modelRegistry;
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

        var model = string.IsNullOrWhiteSpace(request.ModelId)
            ? modelRegistry.GetActiveRuntimeSpec()
            : modelRegistry.GetRuntimeSpec(request.ModelId);

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

                var observation = BuildObservationVector(telemetry.Flat);
                var (throttle, steer) = running.Predictor!.Predict(observation);
                var command = ResolveCommand(throttle, steer);

                var response = await runtimeSessionManager.SendCommandAsync(
                    running.ClientId!,
                    running.RuntimeMode!,
                    command,
                    running.AgentId,
                    token);

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
        private readonly string? inputName = null;
        private readonly int inputSize = 0;

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
                var firstInput = onnxSession.InputMetadata.FirstOrDefault();
                inputName = firstInput.Key;
                inputSize = ResolveInputSize(firstInput.Value?.Dimensions);
                if (string.IsNullOrWhiteSpace(inputName))
                {
                    throw new InvalidOperationException("ONNX model has no declared inputs");
                }

                this.logger.LogInformation("Loaded ONNX model {ModelId} from {Path}", model.ModelId, model.ArtifactPath);
            }
            catch (Exception ex)
            {
                onnxSession?.Dispose();
                throw new InvalidOperationException(
                    $"Failed to load ONNX model '{model.ModelId}' for autopilot runtime",
                    ex);
            }
        }

        public (float throttle, float steer) Predict(float[] observation)
        {
            try
            {
                var size = inputSize <= 0 ? observation.Length : inputSize;
                var tensor = new DenseTensor<float>(new[] { 1, size });
                var copy = Math.Min(size, observation.Length);
                for (var index = 0; index < copy; index += 1)
                {
                    tensor[0, index] = observation[index];
                }

                using var results = onnxSession!.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName!, tensor) });
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
                Mode);
    }
}
