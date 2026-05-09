using System.Text.Json;
using Ks0223.Web.Backend.Models;

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

        app.MapPost("/api/scenarios/load", async (LoadScenarioRequest request, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
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
            psi.ArgumentList.Add("reset");
            psi.ArgumentList.Add(path);

            // In Docker, Unity runs on the host (host.docker.internal:8000); on the
            // host machine itself rusim defaults to localhost:8000. Honour the
            // RUSIM_BASE_URL override if set (Dockerfile sets it to host.docker.internal).
            var rusimBaseUrl = Environment.GetEnvironmentVariable("RUSIM_BASE_URL");
            if (!string.IsNullOrWhiteSpace(rusimBaseUrl))
            {
                psi.ArgumentList.Add("--base-url");
                psi.ArgumentList.Add(rusimBaseUrl);
            }

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
                logger.LogWarning("rusim scenario reset failed: exit={ExitCode}, stderr={Stderr}", proc.ExitCode, stderr);
                return Results.BadRequest(new
                {
                    error = "rusim scenario reset failed",
                    exitCode = proc.ExitCode,
                    stderr,
                    stdout,
                });
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
}
