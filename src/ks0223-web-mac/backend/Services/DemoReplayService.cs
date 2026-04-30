using System.Globalization;
using System.Text.Json;
using Ks0223.Web.Backend.Models;

namespace Ks0223.Web.Backend.Services;

/// <summary>
/// Replays a recorded session JSONL by re-issuing every command.outgoing event
/// to the connected robot with original time gaps preserved (optionally scaled).
///
/// Operator workflow (per Plan 3 / 2026-04-29-plan3-webui-demo-replay.md):
///   1. Hand-drive the robot through a corridor while WebUI Demo Recording is on
///   2. Place the robot back at start
///   3. Hit "Replay" — backend re-fires the same DirForward / DirLeft / etc.
///      sequence with original timing. Operator records video on phone instead
///      of being there with a camera every time.
///
/// Safety: replay commands go through RuntimeSessionManager.SendCommandAsync,
/// which routes through AutopilotSafetyFilter (sonar E-stop, deadman timeout,
/// etc). Replay does NOT bypass safety.
/// </summary>
public sealed class DemoReplayService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<DemoReplayService> logger;
    private readonly RuntimeSessionManager runtimeSessionManager;
    private readonly object stateLock = new();

    private CancellationTokenSource? cts;
    private Task? playbackTask;
    private List<DemoCommand> commands = new();
    private DateTimeOffset startedAtUtc;
    private string? sessionFilePath;
    private string? clientId;
    private string? runtimeMode;
    private string? agentId;
    private double speedMultiplier = 1.0;

    private DemoReplayState state = DemoReplayState.Idle;
    private int currentIndex;
    private string? lastCommand;
    private string? lastError;

    public DemoReplayService(
        ILogger<DemoReplayService> logger,
        RuntimeSessionManager runtimeSessionManager)
    {
        this.logger = logger;
        this.runtimeSessionManager = runtimeSessionManager;
    }

    public DemoReplayState State
    {
        get { lock (stateLock) { return state; } }
    }

    public DemoReplayInfo Start(
        string clientId,
        string runtimeMode,
        string sessionFilePath,
        string? agentId,
        double speedMultiplier)
    {
        lock (stateLock)
        {
            if (state == DemoReplayState.Loading || state == DemoReplayState.Playing)
            {
                throw new InvalidOperationException(
                    $"Replay already in progress (state={state}). Stop it before starting another.");
            }

            if (!File.Exists(sessionFilePath))
            {
                throw new FileNotFoundException($"Session file not found: {sessionFilePath}");
            }

            if (speedMultiplier <= 0 || speedMultiplier > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(speedMultiplier),
                    "Speed multiplier must be in (0, 10]");
            }

            state = DemoReplayState.Loading;
            currentIndex = 0;
            lastError = null;
            lastCommand = null;
        }

        // Parse session file synchronously (fast, in-memory)
        var parsed = ParseSessionLog(sessionFilePath);
        if (parsed.Count == 0)
        {
            lock (stateLock)
            {
                state = DemoReplayState.Error;
                lastError = "Session file contains zero command.outgoing events";
            }
            throw new InvalidOperationException(lastError);
        }

        var estimatedDurationMs = parsed.Count > 1
            ? (int)((parsed[^1].Timestamp - parsed[0].Timestamp).TotalMilliseconds / speedMultiplier)
            : 0;

        lock (stateLock)
        {
            this.commands = parsed;
            this.sessionFilePath = sessionFilePath;
            this.clientId = clientId;
            this.runtimeMode = runtimeMode;
            this.agentId = agentId;
            this.speedMultiplier = speedMultiplier;
            this.cts = new CancellationTokenSource();
            this.startedAtUtc = DateTimeOffset.UtcNow;
            this.state = DemoReplayState.Playing;
            this.playbackTask = Task.Run(() => RunPlaybackAsync(cts.Token));
        }

        logger.LogInformation(
            "Demo replay started: file={File} commands={Commands} speedMul={Speed}x estDur={EstMs}ms",
            Path.GetFileName(sessionFilePath), parsed.Count, speedMultiplier, estimatedDurationMs);

        return new DemoReplayInfo(parsed.Count, estimatedDurationMs, Path.GetFileName(sessionFilePath));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? localCts;
        Task? localTask;
        lock (stateLock)
        {
            localCts = cts;
            localTask = playbackTask;
            if (state == DemoReplayState.Playing || state == DemoReplayState.Loading)
            {
                state = DemoReplayState.Stopped;
            }
        }

        localCts?.Cancel();

        if (localTask is not null)
        {
            try
            {
                using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                combined.CancelAfter(TimeSpan.FromSeconds(2));
                await localTask.WaitAsync(combined.Token);
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception e) { logger.LogWarning(e, "Demo replay stop wait failed"); }
        }

        // Send DirStop as final safety command
        if (clientId is not null && runtimeMode is not null)
        {
            try
            {
                await runtimeSessionManager.SendCommandAsync(
                    clientId, runtimeMode, "DirStop", agentId, cancellationToken);
            }
            catch (Exception e) { logger.LogWarning(e, "Failed to send DirStop on replay stop"); }
        }
    }

    public DemoReplayProgress GetProgress()
    {
        lock (stateLock)
        {
            var elapsed = startedAtUtc == default
                ? 0
                : (int)(DateTimeOffset.UtcNow - startedAtUtc).TotalMilliseconds;
            string? curTs = null;
            if (currentIndex >= 0 && currentIndex < commands.Count)
            {
                curTs = commands[currentIndex].Timestamp.ToString("o", CultureInfo.InvariantCulture);
            }
            return new DemoReplayProgress(
                State: state.ToString(),
                CurrentIndex: currentIndex,
                TotalCommands: commands.Count,
                CurrentTimestampUtc: curTs,
                StartedAtUtc: startedAtUtc == default ? null : startedAtUtc,
                ElapsedMs: elapsed,
                LastCommand: lastCommand,
                LastError: lastError);
        }
    }

    private async Task RunPlaybackAsync(CancellationToken ct)
    {
        try
        {
            // Absolute-time scheduling: each command fires at
            //   playbackStart + (cmd[i].Timestamp - cmd[0].Timestamp) / speedMultiplier
            // This compensates for SendCommandAsync network round-trip time
            // (Wi-Fi to Pi ~150-200ms). With delta-time scheduling, each
            // SendCommand's processing time was added to the gap, accumulating
            // ~10s drift over a 58-command session and causing the robot to
            // overshoot stop points by seconds.
            var firstCmdTs = commands[0].Timestamp;
            var playbackStart = DateTimeOffset.UtcNow;

            for (int i = 0; i < commands.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (i > 0)
                {
                    var sourceOffsetMs = (commands[i].Timestamp - firstCmdTs).TotalMilliseconds;
                    var targetOffsetMs = sourceOffsetMs / speedMultiplier;
                    var elapsedMs = (DateTimeOffset.UtcNow - playbackStart).TotalMilliseconds;
                    var waitMs = (int)(targetOffsetMs - elapsedMs);
                    if (waitMs > 0)
                    {
                        await Task.Delay(waitMs, ct);
                    }
                    // If waitMs <= 0 we're behind schedule (network was slower
                    // than expected) — fire immediately to catch up. This
                    // means one command may follow another with no gap, but
                    // wall-clock alignment is preserved going forward.
                }

                lock (stateLock)
                {
                    currentIndex = i;
                    lastCommand = commands[i].Command;
                }

                var resp = await runtimeSessionManager.SendCommandAsync(
                    clientId!, runtimeMode!, commands[i].Command, agentId, ct);
                if (!resp.Sent)
                {
                    lock (stateLock)
                    {
                        state = DemoReplayState.Error;
                        lastError = $"SendCommand failed at index {i}: {resp.Error}";
                    }
                    logger.LogError("Demo replay aborted at command {Idx}: {Err}", i, resp.Error);
                    return;
                }
            }

            // Final DirStop for safety. If the last logged command was already
            // DirStop, this is redundant but harmless. If user stopped recording
            // mid-motion, this guarantees the robot halts. Small gap (100ms)
            // so it doesn't race the final logged command on the network.
            try
            {
                await Task.Delay(100, ct);
                if (commands.Count == 0 || !string.Equals(commands[^1].Command, "DirStop", StringComparison.Ordinal))
                {
                    await runtimeSessionManager.SendCommandAsync(
                        clientId!, runtimeMode!, "DirStop", agentId, ct);
                }
            }
            catch { /* best effort */ }

            lock (stateLock)
            {
                if (state == DemoReplayState.Playing)
                {
                    state = DemoReplayState.Done;
                    currentIndex = commands.Count;
                }
            }
            logger.LogInformation("Demo replay done after {N} commands", commands.Count);
        }
        catch (OperationCanceledException)
        {
            lock (stateLock) { state = DemoReplayState.Stopped; }
            logger.LogInformation("Demo replay cancelled at command {Idx}/{N}", currentIndex, commands.Count);
        }
        catch (Exception e)
        {
            lock (stateLock)
            {
                state = DemoReplayState.Error;
                lastError = e.Message;
            }
            logger.LogError(e, "Demo replay failed");
        }
    }

    private static List<DemoCommand> ParseSessionLog(string path)
    {
        var validCommands = new HashSet<string>
        {
            "DirStop", "DirForward", "DirBack", "DirLeft", "DirRight",
        };
        var result = new List<DemoCommand>();
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.TrimStart('﻿').Trim();
            if (string.IsNullOrEmpty(line)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("type", out var typeEl)) continue;
                if (typeEl.GetString() != "command.outgoing") continue;

                if (!doc.RootElement.TryGetProperty("timestamp", out var tsEl)) continue;
                if (!doc.RootElement.TryGetProperty("payload", out var payloadEl)) continue;
                if (!payloadEl.TryGetProperty("command", out var cmdEl)) continue;

                var cmd = cmdEl.GetString() ?? string.Empty;
                if (!validCommands.Contains(cmd)) continue;

                if (!DateTimeOffset.TryParse(tsEl.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var ts)) continue;

                result.Add(new DemoCommand(ts, cmd));
            }
        }
        // Filter out non-monotonic timestamps (corrupted / out-of-order events)
        result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return result;
    }

    public IReadOnlyList<DemoSessionFileDto> ListSessions(string sessionsDirectory)
    {
        if (!Directory.Exists(sessionsDirectory))
        {
            return Array.Empty<DemoSessionFileDto>();
        }
        var dir = new DirectoryInfo(sessionsDirectory);
        var files = dir.GetFiles("session_*.jsonl", SearchOption.TopDirectoryOnly);
        var result = new List<DemoSessionFileDto>(files.Length);
        foreach (var f in files.OrderByDescending(x => x.LastWriteTimeUtc))
        {
            var cmdCount = CountCommandsFast(f.FullName);
            result.Add(new DemoSessionFileDto(
                FileName: f.Name,
                FilePath: f.FullName,
                SizeKb: f.Length / 1024,
                LastWriteUtc: f.LastWriteTimeUtc,
                CommandCount: cmdCount));
        }
        return result;
    }

    private static int CountCommandsFast(string path)
    {
        var count = 0;
        try
        {
            foreach (var rawLine in File.ReadLines(path))
            {
                if (rawLine.Contains("\"command.outgoing\""))
                {
                    count++;
                }
            }
        }
        catch { /* best effort */ }
        return count;
    }

    private sealed record DemoCommand(DateTimeOffset Timestamp, string Command);
}
