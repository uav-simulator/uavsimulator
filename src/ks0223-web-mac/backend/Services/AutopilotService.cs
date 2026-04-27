using Ks0223.Web.Backend.Models;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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
    private const int DefaultMaxDurationSeconds = 60;
    private const int MaxDurationSecondsHardCap = 600;
    private const int RepeatedCommandThreshold = 15;
    private const int StaleTelemetryAfterMs = 1500;
    private const int EStopWindowSeconds = 10;
    private const int EStopWindowThreshold = 5;

    private readonly object gate = new();
    private readonly RuntimeSessionManager runtimeSessionManager;
    private readonly ModelRegistryService modelRegistry;
    private readonly AutopilotSafetyFilter safetyFilter;
    private readonly SessionVideoRecorder videoRecorder;
    private readonly IServer server;
    private readonly ILogger<AutopilotService> logger;

    private AutopilotState state = AutopilotState.Stopped();

    public AutopilotService(
        RuntimeSessionManager runtimeSessionManager,
        ModelRegistryService modelRegistry,
        AutopilotSafetyFilter safetyFilter,
        SessionVideoRecorder videoRecorder,
        IServer server,
        ILogger<AutopilotService> logger)
    {
        this.runtimeSessionManager = runtimeSessionManager;
        this.modelRegistry = modelRegistry;
        this.safetyFilter = safetyFilter;
        this.videoRecorder = videoRecorder;
        this.server = server;
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
                    return AutopilotState.Stopped().ToDto() with { ThrottleMax = safetyFilter.GetStatus().ThrottleMax };
                }

                if (!string.IsNullOrWhiteSpace(runtimeMode))
                {
                    var normalizedMode = RuntimeModes.Normalize(runtimeMode);
                    if (!string.Equals(state.RuntimeMode, normalizedMode, StringComparison.Ordinal))
                    {
                        return AutopilotState.Stopped().ToDto() with { ThrottleMax = safetyFilter.GetStatus().ThrottleMax };
                    }
                }
            }

            return state.ToDto() with { ThrottleMax = safetyFilter.GetStatus().ThrottleMax };
        }
    }

    public async Task<AutopilotStatusDto> StartAsync(StartAutopilotRequest request, CancellationToken cancellationToken)
    {
        var clientId = NormalizeRequired(request.ClientId, nameof(request.ClientId));
        var runtimeMode = RuntimeModes.Normalize(request.RuntimeMode);
        var agentId = string.IsNullOrWhiteSpace(request.AgentId) ? null : request.AgentId.Trim();
        var loopIntervalMs = Math.Clamp(request.LoopIntervalMs ?? 140, MinLoopIntervalMs, MaxLoopIntervalMs);
        var maxDurationSeconds = Math.Clamp(request.MaxDurationSeconds ?? DefaultMaxDurationSeconds, 5, MaxDurationSecondsHardCap);

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
            maxDurationSeconds,
            cts);

        lock (gate)
        {
            state = running;
        }

        safetyFilter.Reset();
        TryStartVideoRecording(clientId, runtimeMode, model.ModelId);
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
        var eStopTimestamps = new Queue<DateTimeOffset>();
        long previousEStopCount = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (running.MaxDurationSeconds > 0 && running.StartedAtUtc is { } startedAt &&
                    (DateTimeOffset.UtcNow - startedAt).TotalSeconds >= running.MaxDurationSeconds)
                {
                    await AutoStopAsync(running, $"max-duration {running.MaxDurationSeconds}s reached");
                    return;
                }

                var telemetry = runtimeSessionManager.GetLatestSensorTelemetry(running.ClientId!, running.RuntimeMode!);
                if (telemetry is null)
                {
                    await Task.Delay(running.LoopIntervalMs, token);
                    continue;
                }

                var telemetryAgeMs = (DateTimeOffset.UtcNow - telemetry.Timestamp).TotalMilliseconds;
                if (telemetryAgeMs > StaleTelemetryAfterMs)
                {
                    await AutoStopAsync(running, $"sensor telemetry stale {telemetryAgeMs:F0}ms");
                    return;
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
                var (leftM, rightM) = ExtractSideMeters(telemetry.Flat);
                var decision = safetyFilter.Apply(rawThrottle, rawSteer, frontM, leftM, rightM);
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
                int repeatedCount;
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
                    if (string.Equals(running.LastCommand, command, StringComparison.Ordinal))
                    {
                        running.RepeatedCommandCount += 1;
                    }
                    else
                    {
                        running.RepeatedCommandCount = 1;
                        running.LastCommand = command;
                    }
                    running.EStopActive = decision.EStopActive;
                    running.EStopTriggerCount = safetyStatus.EStopTriggerCount;
                    repeatedCount = running.RepeatedCommandCount;

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

                if (safetyStatus.EStopTriggerCount > previousEStopCount)
                {
                    var now = DateTimeOffset.UtcNow;
                    eStopTimestamps.Enqueue(now);
                    var windowStart = now.AddSeconds(-EStopWindowSeconds);
                    while (eStopTimestamps.Count > 0 && eStopTimestamps.Peek() < windowStart)
                    {
                        eStopTimestamps.Dequeue();
                    }
                    previousEStopCount = safetyStatus.EStopTriggerCount;
                    if (eStopTimestamps.Count >= EStopWindowThreshold)
                    {
                        await AutoStopAsync(running,
                            $"safety overload: {eStopTimestamps.Count} E-stops in {EStopWindowSeconds}s");
                        return;
                    }
                }

                if (repeatedCount >= RepeatedCommandThreshold && command != "DirStop")
                {
                    await AutoStopAsync(running,
                        $"stuck-command: {command} repeated {repeatedCount} times");
                    return;
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

            videoRecorder.Stop();
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

    private static readonly string[] ActionNames =
    {
        "DirStop", "DirForward", "DirBack", "DirLeft", "DirRight",
    };

    private PolicyPredictor? previewPredictor;
    private string? previewModelId;
    private readonly object previewGate = new();

    private const float ImageBlindStdDevThreshold = 0.05f;  // if image stddev below this — treat as blackout, force DirStop

    private static ImageFeaturesDto? ComputeImageFeatures(byte[]? frameBytes)
    {
        if (frameBytes is null || frameBytes.Length == 0) return null;
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(frameBytes);
            image.Mutate(c => c.Resize(64, 64));
            int w = image.Width, h = image.Height;
            int half = h / 2;
            var grey = new float[h, w];
            double sum = 0, sumSq = 0;
            int n = w * h;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = image[x, y];
                float v = 0.299f * p.R + 0.587f * p.G + 0.114f * p.B;
                grey[y, x] = v;
                sum += v;
                sumSq += v * v;
            }
            float mean = (float)(sum / n);
            float variance = (float)Math.Max(0, sumSq / n - mean * mean);
            float std = MathF.Sqrt(variance);

            float edgeTop = 0, edgeBottom = 0;
            int topCount = 0, botCount = 0;
            for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                float gx = grey[y, x + 1] - grey[y, x - 1];
                float gy = grey[y + 1, x] - grey[y - 1, x];
                float mag = MathF.Abs(gx) + MathF.Abs(gy);
                if (y < half) { edgeTop += mag; topCount++; }
                else { edgeBottom += mag; botCount++; }
            }
            if (topCount > 0) edgeTop /= topCount;
            if (botCount > 0) edgeBottom /= botCount;

            return new ImageFeaturesDto(
                BrightnessMean: mean / 255f,
                BrightnessStdDev: std / 128f,
                EdgeScoreTop: edgeTop / 100f,
                EdgeScoreBottom: edgeBottom / 100f);
        }
        catch
        {
            return null;
        }
    }

    public PreviewSampleDto SamplePreview(string clientId, string runtimeMode, string? agentId)
    {
        var normalizedClientId = NormalizeRequired(clientId, nameof(clientId));
        var normalizedMode = RuntimeModes.Normalize(runtimeMode);
        var normalizedAgent = string.IsNullOrWhiteSpace(agentId) ? null : agentId.Trim();

        var model = modelRegistry.ResolveRuntimeSpec(null, normalizedClientId, normalizedMode, normalizedAgent);

        PolicyPredictor predictor;
        lock (previewGate)
        {
            if (previewPredictor is null || previewModelId != model.ModelId)
            {
                previewPredictor?.Dispose();
                previewPredictor = new PolicyPredictor(model, logger);
                previewModelId = model.ModelId;
            }
            predictor = previewPredictor;
        }

        var telemetry = runtimeSessionManager.GetLatestSensorTelemetry(normalizedClientId, normalizedMode);
        if (telemetry is null)
        {
            return new PreviewSampleDto(false, model.ModelId, "no telemetry yet", null, null, null, null, null);
        }

        byte[]? frameBytes = null;
        if (predictor.RequiresFrame)
        {
            if (!runtimeSessionManager.TryGetLatestFrame(
                    normalizedClientId, normalizedMode, normalizedAgent,
                    out var latestFrame, out _, out _, out _))
            {
                return new PreviewSampleDto(false, model.ModelId, "no camera frame yet", null, null, null, null, null);
            }
            frameBytes = latestFrame;
        }

        float[] logits;
        try
        {
            logits = predictor.PredictRaw(telemetry.Flat, frameBytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Preview inference failed");
            return new PreviewSampleDto(false, model.ModelId, ex.Message, null, null, null, null, null);
        }

        // softmax over logits for action probabilities (only for discrete-categorical models)
        float[]? probs = null;
        string? chosenAction = null;
        int? chosenIndex = null;
        if (predictor.IsDiscreteAction && logits.Length == ActionNames.Length)
        {
            probs = new float[logits.Length];
            float maxLogit = logits[0];
            for (int i = 1; i < logits.Length; i++) if (logits[i] > maxLogit) maxLogit = logits[i];
            float sum = 0f;
            for (int i = 0; i < logits.Length; i++)
            {
                probs[i] = (float)Math.Exp(logits[i] - maxLogit);
                sum += probs[i];
            }
            int bestIdx = 0;
            for (int i = 0; i < probs.Length; i++)
            {
                probs[i] /= sum;
                if (probs[i] > probs[bestIdx]) bestIdx = i;
            }
            chosenIndex = bestIdx;
            chosenAction = ActionNames[bestIdx];
        }

        var frontM = ExtractFrontMeters(telemetry.Flat);
        var features = ComputeImageFeatures(frameBytes);

        // CV-feature guard: image too uniform = blind = force DirStop.
        string? guardReason = null;
        if (features != null && features.BrightnessStdDev < ImageBlindStdDevThreshold && probs != null)
        {
            for (int i = 0; i < probs.Length; i++) probs[i] = 0f;
            probs[0] = 1f;
            chosenIndex = 0;
            chosenAction = ActionNames[0];
            guardReason = $"image-blind (stddev={features.BrightnessStdDev:F3} < {ImageBlindStdDevThreshold:F2})";
        }

        return new PreviewSampleDto(
            true,
            model.ModelId,
            null,
            logits,
            probs,
            chosenAction,
            chosenIndex,
            frontM,
            features,
            guardReason);
    }

    private async Task AutoStopAsync(AutopilotState running, string reason)
    {
        logger.LogWarning("Autopilot auto-stop: {Reason} (client={Client} model={Model} steps={Steps})",
            reason, running.ClientId, running.ModelId, running.StepsTotal);

        // Reset DirectDrive state to (0, 0) so any stream consumer no longer sees the
        // last forward throttle, then send DirStop several times — a single command can
        // be lost or arrive interleaved with a stale in-flight command.
        try
        {
            runtimeSessionManager.SetDirectDrive(
                running.ClientId!, running.RuntimeMode!, running.AgentId, 0f, 0f);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to reset DirectDrive on auto-stop");
        }

        await SendDirStopBurstAsync(running, attempts: 4, gapMs: 80);

        videoRecorder.Stop();
        running.Cancellation?.Cancel();
        running.Predictor?.Dispose();
        lock (gate)
        {
            running.StopReason = reason;
            if (ReferenceEquals(state, running))
            {
                state = AutopilotState.StoppedFrom(running, reason);
            }
        }
    }

    private async Task SendDirStopBurstAsync(AutopilotState running, int attempts, int gapMs)
    {
        for (var i = 0; i < attempts; i++)
        {
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
                logger.LogDebug(stopEx, "DirStop burst attempt {Attempt} failed", i + 1);
            }

            if (i + 1 < attempts)
            {
                await Task.Delay(gapMs);
            }
        }
    }

    private void TryStartVideoRecording(string clientId, string runtimeMode, string modelId)
    {
        try
        {
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            var local = addresses?.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(local))
            {
                logger.LogWarning("Video recorder skipped: no local HTTP server address");
                return;
            }

            // Server addresses can use 0.0.0.0 / [::]; resolve to loopback for the sub-process.
            var uri = new Uri(local);
            var hostForLocal = uri.Host is "0.0.0.0" or "+" or "*" or "[::]" ? "127.0.0.1" : uri.Host;
            var mjpeg = $"{uri.Scheme}://{hostForLocal}:{uri.Port}/api/camera/mjpeg" +
                $"?clientId={Uri.EscapeDataString(clientId)}&runtimeMode={Uri.EscapeDataString(runtimeMode)}";
            videoRecorder.TryStart(mjpeg, modelId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to start autopilot video recorder");
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

        videoRecorder.Stop();
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

    private static (float LeftM, float RightM) ExtractSideMeters(IReadOnlyDictionary<string, string> flat)
    {
        var leftCm = ParseValue(flat, "sensor.ultrasonic.left.cm", "ultrasonic.scan.left_cm");
        var rightCm = ParseValue(flat, "sensor.ultrasonic.right.cm", "ultrasonic.scan.right_cm");
        return (leftCm > 0f ? leftCm / 100f : 0f, rightCm > 0f ? rightCm / 100f : 0f);
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
        private readonly bool isDiscreteAction = false;
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

                isDiscreteAction = TryDetectDiscreteActionMode(onnxSession, model.MetadataPath);

                this.logger.LogInformation(
                    "Loaded ONNX model {ModelId} from {Path} with autopilot mode {Mode} (discrete={IsDiscrete})",
                    model.ModelId,
                    model.ArtifactPath,
                    mode,
                    isDiscreteAction);
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

        // Mirror of python/training/discrete_action_wrapper.py ACTION_TABLE.
        // Order: 0=DirStop, 1=DirForward, 2=DirBack, 3=DirLeft, 4=DirRight.
        // Pure-steer (0, ±1) for rotation: KS0223 is true diff-drive (Unity
        // Ks0223Vehicle.cs computes linear and angular independently), so
        // in-place rotation matches real robot DirLeft/Right physics 1:1.
        private static readonly (float Throttle, float Steer)[] DiscreteActionTable =
        [
            (0.0f,  0.0f),
            (+1.0f, 0.0f),
            (-1.0f, 0.0f),
            (0.0f, +1.0f),
            (0.0f, -1.0f),
        ];

        public bool IsDiscreteAction => isDiscreteAction;

        // Hardware/safety guards at action-selection level. Applied to raw logits before
        // argmax so both real-autopilot and shadow-preview see consistent behavior.
        // 1. DirBack is masked because real KS0223 has no rear sensors.
        // 2. DirStop is forced (logit dominates) when front sonar < ForceStopBelowM,
        //    complementing the throttle/E-stop safety filter at the action layer.
        private const int DirBackIndex = 2;
        private const int DirStopIndex = 0;
        private const float ForceStopBelowM = 0.10f;

        private void ApplyPolicyGuards(float[] logits, IReadOnlyDictionary<string, string> telemetry)
        {
            if (!isDiscreteAction || logits.Length != DiscreteActionTable.Length) return;
            logits[DirBackIndex] = float.MinValue;
            var frontM = ExtractNormalizedUltrasonic(telemetry) * 5.0f;
            if (frontM > 0f && frontM < ForceStopBelowM)
            {
                float maxOther = float.MinValue;
                for (int i = 0; i < logits.Length; i++)
                    if (i != DirStopIndex && logits[i] > maxOther) maxOther = logits[i];
                logits[DirStopIndex] = maxOther + 5.0f;
            }
        }

        public float[] PredictRaw(IReadOnlyDictionary<string, string> telemetry, byte[]? frameBytes)
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
                ApplyPolicyGuards(output, telemetry);
                return output;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("ONNX inference failed", ex);
            }
        }

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

                // Apply same guards as PredictRaw so autopilot and shadow agree.
                ApplyPolicyGuards(output, telemetry);

                // Discrete-categorical policy: output is (batch, 5) action logits.
                // Argmax selects the discrete action; map through DiscreteActionTable
                // to a representative (throttle, steer) tuple that downstream
                // safety filter + ResolveCommand will route to the same command on
                // the real robot. Lets us deploy v9-style discrete policies without
                // changing the safety/ramp/E-stop pipeline.
                if (isDiscreteAction && output.Length == DiscreteActionTable.Length)
                {
                    var bestIdx = 0;
                    var bestVal = output[0];
                    for (var i = 1; i < output.Length; i++)
                    {
                        if (output[i] > bestVal)
                        {
                            bestVal = output[i];
                            bestIdx = i;
                        }
                    }
                    return DiscreteActionTable[bestIdx];
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

        private static bool TryDetectDiscreteActionMode(InferenceSession session, string metadataPath)
        {
            // 1. Prefer metadata.actionSchema.type == "discrete-categorical"
            try
            {
                if (!string.IsNullOrWhiteSpace(metadataPath) && File.Exists(metadataPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(metadataPath));
                    if (doc.RootElement.TryGetProperty("actionSchema", out var actionSchema))
                    {
                        if (actionSchema.TryGetProperty("type", out var typeProp) &&
                            typeProp.ValueKind == JsonValueKind.String &&
                            string.Equals(typeProp.GetString(), "discrete-categorical", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // metadata parse failure → fall through to ONNX shape heuristic
            }

            // 2. Fallback: detect by ONNX output shape — single output of trailing dim 5
            var firstOutput = session.OutputMetadata.FirstOrDefault();
            if (firstOutput.Value is null)
            {
                return false;
            }
            var dims = firstOutput.Value.Dimensions;
            if (dims is null || dims.Length == 0)
            {
                return false;
            }
            // expect (batch, 5) — last positive dim equals 5
            for (var i = dims.Length - 1; i >= 0; i--)
            {
                if (dims[i] > 0)
                {
                    return dims[i] == DiscreteActionTable.Length;
                }
            }
            return false;
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
            => Clamp01(ExtractFrontMeters(telemetry) / 5f);

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
        public int MaxDurationSeconds { get; private set; }
        public int RepeatedCommandCount { get; set; }
        public string? StopReason { get; set; }

        public static AutopilotState Running(
            string clientId,
            string runtimeMode,
            string? agentId,
            string modelId,
            PolicyPredictor predictor,
            int loopIntervalMs,
            int maxDurationSeconds,
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
                MaxDurationSeconds = maxDurationSeconds,
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
                MaxDurationSeconds = previous.MaxDurationSeconds,
                RepeatedCommandCount = previous.RepeatedCommandCount,
                StopReason = previous.StopReason ?? lastError,
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
                EStopTriggerCount: EStopTriggerCount,
                MaxDurationSeconds: MaxDurationSeconds,
                RepeatedCommandCount: RepeatedCommandCount,
                StopReason: StopReason);
    }
}
