using System.Text;
using System.Text.Json;
using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Services;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// Scenario picker: list YAML files under <c>configs/scenarios/</c> and load
/// one by shelling out to <c>rusim scenario reset &lt;file&gt;</c>. Both
/// endpoints are local-only by virtue of the CORS policy in Program.cs.
///
/// Originally lines 615-784 of Program.cs (incl. the static helper functions
/// for resolving the scenarios directory and rusim binary).
/// </summary>
internal static class ScenarioEndpoints
{
    public static void MapScenarioEndpoints(this WebApplication app)
    {
        app.MapGet("/api/scenarios", () =>
        {
            var dir = ResolveScenariosDir();
            if (!Directory.Exists(dir))
            {
                return Results.Ok(new
                {
                    scenariosDir = dir,
                    count = 0,
                    items = Array.Empty<object>(),
                    warning = $"scenarios directory not found at {dir}",
                });
            }

            var items = Directory.GetFiles(dir, "*.yaml")
                .Select(f =>
                {
                    var info = new FileInfo(f);
                    var name = Path.GetFileNameWithoutExtension(f);
                    return new
                    {
                        fileName = Path.GetFileName(f),
                        displayName = name,
                        filePath = f,
                        sizeBytes = info.Length,
                        lastWriteUtc = info.LastWriteTimeUtc.ToString("o"),
                    };
                })
                .OrderBy(s => s.displayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Ok(new { scenariosDir = dir, count = items.Count, items });
        });

        app.MapPost("/api/scenarios/load", async (
            LoadScenarioRequest request,
            ILoggerFactory loggerFactory,
            IHttpClientFactory httpClientFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("ScenarioLoader");
            if (string.IsNullOrWhiteSpace(request.FilePath))
            {
                return Results.BadRequest(new { error = "filePath is required" });
            }

            var path = request.FilePath.Trim();
            if (!File.Exists(path))
            {
                return Results.BadRequest(new { error = $"scenario file not found: {path}" });
            }

            var rusimBinary = ResolveRusimBinary();
            if (rusimBinary == null)
            {
                return Results.Problem(
                    statusCode: 500,
                    detail: "rusim CLI not found in PATH. Install it via `pip install -e python/` from repo root or set RUSIM_PATH env var.");
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = rusimBinary,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("scenario");
            psi.ArgumentList.Add("print-reset");
            psi.ArgumentList.Add(path);

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                return Results.Problem(statusCode: 500, detail: "Failed to start rusim subprocess");
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                logger.LogWarning("rusim scenario print-reset failed: exit={ExitCode}, stderr={Stderr}", proc.ExitCode, stderr);
                return Results.BadRequest(new
                {
                    error = "rusim scenario print-reset failed",
                    exitCode = proc.ExitCode,
                    stderr,
                    stdout,
                });
            }

            JsonDocument payloadDocument;
            try
            {
                payloadDocument = JsonDocument.Parse(stdout);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "rusim scenario print-reset returned invalid JSON: {Stdout}", stdout);
                return Results.BadRequest(new
                {
                    error = "rusim scenario print-reset returned invalid JSON",
                    stdout,
                    stderr,
                });
            }

            using (payloadDocument)
            {
                var rusimBaseUrl = Environment.GetEnvironmentVariable("RUSIM_BASE_URL");
                if (string.IsNullOrWhiteSpace(rusimBaseUrl))
                {
                    rusimBaseUrl = "http://127.0.0.1:8000";
                }

                var resetUri = BuildRuntimeResetUri(rusimBaseUrl);
                using var resetRequest = new HttpRequestMessage(HttpMethod.Post, resetUri);
                var hostHeader = GetRuntimeHostHeaderOverride(resetUri, IsRunningInContainer());
                if (hostHeader is not null)
                {
                    resetRequest.Headers.Host = hostHeader;
                }

                resetRequest.Content = new StringContent(
                    payloadDocument.RootElement.GetRawText(),
                    Encoding.UTF8,
                    "application/json");

                var httpClient = httpClientFactory.CreateClient(nameof(ScenarioEndpoints));
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                using var resetResponse = await httpClient.SendAsync(resetRequest, cancellationToken);
                var responseText = await resetResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!resetResponse.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "runtime reset failed: status={StatusCode}, body={Body}",
                        (int)resetResponse.StatusCode,
                        responseText);
                    return Results.BadRequest(new
                    {
                        error = "runtime reset failed",
                        statusCode = (int)resetResponse.StatusCode,
                        body = responseText,
                        resetUrl = resetUri.ToString(),
                    });
                }

                stdout = responseText;
            }

            try
            {
                using var doc = JsonDocument.Parse(stdout);
                return Results.Ok(doc.RootElement.Clone());
            }
            catch (JsonException)
            {
                return Results.Ok(new { rawOutput = stdout });
            }
        });

        app.MapPost("/api/scenarios/maze/generate", async (
            GenerateMazeScenarioRequest request,
            ILoggerFactory loggerFactory,
            IHttpClientFactory httpClientFactory,
            RuntimeSessionManager runtimeSessionManager,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("MazeScenarioGenerator");
            var payload = BuildMazeResetPayload(request);
            await TryPrepareUnitySessionAsync(request, payload, runtimeSessionManager, logger, cancellationToken);

            try
            {
                var resetFromConnectedSession = await runtimeSessionManager.TryResetConnectedUnityRuntimeAsync(
                    request.ClientId,
                    request.RuntimeMode,
                    payload,
                    payload.agents.FirstOrDefault()?.agentId,
                    cancellationToken);
                if (resetFromConnectedSession.HasValue)
                {
                    return Results.Ok(new
                    {
                        maze = BuildMazeSummary(payload),
                        reset = resetFromConnectedSession.Value,
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "controlled Unity maze reset failed; falling back to direct runtime reset");
            }

            var rusimBaseUrl = Environment.GetEnvironmentVariable("RUSIM_BASE_URL");
            if (string.IsNullOrWhiteSpace(rusimBaseUrl))
            {
                rusimBaseUrl = "http://127.0.0.1:8000";
            }

            var resetUri = BuildRuntimeResetUri(rusimBaseUrl);
            using var resetRequest = new HttpRequestMessage(HttpMethod.Post, resetUri);
            var hostHeader = GetRuntimeHostHeaderOverride(resetUri, IsRunningInContainer());
            if (hostHeader is not null)
            {
                resetRequest.Headers.Host = hostHeader;
            }

            resetRequest.Content = JsonContent(payload);

            var httpClient = httpClientFactory.CreateClient(nameof(ScenarioEndpoints));
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            using var resetResponse = await httpClient.SendAsync(resetRequest, cancellationToken);
            var responseText = await resetResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!resetResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "maze runtime reset failed: status={StatusCode}, body={Body}",
                    (int)resetResponse.StatusCode,
                    responseText);
                return Results.BadRequest(new
                {
                    error = "runtime reset failed",
                    statusCode = (int)resetResponse.StatusCode,
                    body = responseText,
                    resetUrl = resetUri.ToString(),
                    maze = BuildMazeSummary(payload),
                });
            }

            try
            {
                using var doc = JsonDocument.Parse(responseText);
                return Results.Ok(new
                {
                    maze = BuildMazeSummary(payload),
                    reset = doc.RootElement.Clone(),
                });
            }
            catch (JsonException)
            {
                return Results.Ok(new
                {
                    maze = BuildMazeSummary(payload),
                    rawOutput = responseText,
                });
            }
        });
    }

    internal static string ResolveScenariosDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable("SCENARIOS_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }

        var cwd = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            // Docker: configs/scenarios is bind-mounted at /app/configs/scenarios.
            "/app/configs/scenarios",
            // Local dev: backend runs from src/ks0223-web-mac/backend/, configs at repo root.
            Path.Combine(cwd, "..", "..", "..", "configs", "scenarios"),
            Path.Combine(cwd, "configs", "scenarios"),
        };
        foreach (var c in candidates)
        {
            var resolved = Path.GetFullPath(c);
            if (Directory.Exists(resolved)) return resolved;
        }
        return Path.GetFullPath(candidates[1]);
    }

    internal static string? ResolveRusimBinary()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RUSIM_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        var candidates = new[]
        {
            home != null ? Path.Combine(home, ".local", "bin", "rusim") : null,
            "/usr/local/bin/rusim",
            "/opt/homebrew/bin/rusim",
            "rusim",
        };
        foreach (var c in candidates)
        {
            if (string.IsNullOrEmpty(c)) continue;
            if (c == "rusim") return c; // last resort: assume PATH
            if (File.Exists(c)) return c;
        }
        return null;
    }

    internal static Uri BuildRuntimeResetUri(string baseUrl)
    {
        var uri = new Uri(baseUrl, UriKind.Absolute);
        var builder = new UriBuilder(uri);
        var path = builder.Path.TrimEnd('/');
        builder.Path = string.IsNullOrEmpty(path) ? "reset" : $"{path}/reset";
        return builder.Uri;
    }

    internal static string? GetRuntimeHostHeaderOverride(Uri runtimeUri, bool runningInContainer)
    {
        if (!runningInContainer)
        {
            return null;
        }

        if (!string.Equals(runtimeUri.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"127.0.0.1:{runtimeUri.Port}";
    }

    internal static RuntimeResetPayload BuildMazeResetPayload(GenerateMazeScenarioRequest request)
    {
        var seed = Math.Clamp(request.Seed ?? 42, 0, 999_999);
        var lengthCells = Math.Clamp(request.LengthCells ?? 12, 3, 80);
        var corridorWidthM = Math.Clamp(request.CorridorWidthM ?? 0.45f, 0.30f, 1.20f);
        var leftTurns = Math.Clamp(request.LeftTurns ?? 2, 0, 20);
        var rightTurns = Math.Clamp(request.RightTurns ?? 1, 0, 20);
        var wallHeightM = Math.Clamp(request.WallHeightM ?? 0.25f, 0.15f, 0.60f);
        var timeScale = Math.Clamp(request.TimeScale ?? 1.0f, 0.1f, 20.0f);
        var vehicleId = string.IsNullOrWhiteSpace(request.VehicleId)
            ? "vehicle.ks0223.v1"
            : request.VehicleId.Trim();
        var agentId = string.IsNullOrWhiteSpace(request.AgentId)
            ? "agent-1"
            : request.AgentId.Trim();
        var cameraProfile = string.IsNullOrWhiteSpace(request.CameraProfile)
            ? "high"
            : request.CameraProfile.Trim();

        return new RuntimeResetPayload(
            seed,
            timeScale,
            "track.cardboard_maze.v1",
            vehicleId,
            [
                new("route.reach_distance_m", FormatInvariant(corridorWidthM * 0.54f)),
                new("route.loop", "false"),
                new("maze.seed", seed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("maze.length_cells", lengthCells.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("maze.corridor_width_m", FormatInvariant(corridorWidthM)),
                new("maze.left_turns", leftTurns.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("maze.right_turns", rightTurns.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                // Important: existing CardboardMazeTrack keeps pathEncoded in a field.
                // Sending an empty value clears a previously-loaded curated path so
                // seed/turn generation actually takes effect on repeated WebUI resets.
                new("maze.path_encoded", string.Empty),
                new("maze.wall_height_m", FormatInvariant(wallHeightM)),
            ],
            [
                new("camera.profile", cameraProfile),
            ],
            [
                new("runtime.headless", "false"),
                new("agents.isolated", "true"),
                new("agents.see_each_other", "false"),
                new("agents.collisions_enabled", "true"),
            ],
            [
                new(agentId, vehicleId, true),
            ]);
    }

    private static object BuildMazeSummary(RuntimeResetPayload payload) => new
    {
        seed = payload.seed,
        lengthCells = ReadParam(payload.trackParams, "maze.length_cells"),
        corridorWidthM = ReadParam(payload.trackParams, "maze.corridor_width_m"),
        leftTurns = ReadParam(payload.trackParams, "maze.left_turns"),
        rightTurns = ReadParam(payload.trackParams, "maze.right_turns"),
        wallHeightM = ReadParam(payload.trackParams, "maze.wall_height_m"),
        trackId = payload.selectedTrackId,
        vehicleId = payload.selectedVehicleId,
        agentId = payload.agents.FirstOrDefault()?.agentId,
    };

    private static async Task TryPrepareUnitySessionAsync(
        GenerateMazeScenarioRequest request,
        RuntimeResetPayload payload,
        RuntimeSessionManager runtimeSessionManager,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId) ||
            !string.Equals(RuntimeModes.Normalize(request.RuntimeMode ?? string.Empty), RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await runtimeSessionManager.SetUnityRuntimeSelectionAsync(
                new UnityRuntimeSelectionRequest(
                    request.ClientId.Trim(),
                    RuntimeModes.UnitySim,
                    payload.selectedTrackId,
                    payload.selectedVehicleId,
                    "driver",
                    payload.agents.Select(agent => new UnityRuntimeAgentSelectionRequest(agent.agentId, agent.vehicleId, agent.isPrimary)).ToArray(),
                    ApplyImmediately: false,
                    CollisionsEnabled: true,
                    SeeEachOther: false),
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "Unity session is not connected; maze reset will still be sent directly");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to synchronize Unity session before maze reset");
        }
    }

    private static string? ReadParam(IEnumerable<ResetKeyValue> values, string key) =>
        values.FirstOrDefault(item => string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase))?.value;

    private static StringContent JsonContent(object payload) =>
        new(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

    private static string FormatInvariant(float value) => value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsRunningInContainer()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        if (string.Equals(env, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(env, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return File.Exists("/.dockerenv");
    }

    internal sealed record ResetKeyValue(string key, string value);

    internal sealed record ResetAgent(string agentId, string vehicleId, bool isPrimary);

    internal sealed record RuntimeResetPayload(
        int seed,
        float timeScale,
        string selectedTrackId,
        string selectedVehicleId,
        ResetKeyValue[] trackParams,
        ResetKeyValue[] vehicleParams,
        ResetKeyValue[] flags,
        ResetAgent[] agents);
}
