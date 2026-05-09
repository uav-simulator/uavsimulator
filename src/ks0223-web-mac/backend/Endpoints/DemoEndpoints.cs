using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Services;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// Demo recording (start/stop video + session log) and demo replay
/// (load JSONL → fire commands back at the robot). Originally lines
/// 540-613 of Program.cs.
/// </summary>
internal static class DemoEndpoints
{
    public static void MapDemoEndpoints(this WebApplication app)
    {
        // Demo recording — orchestrates session-log + MJPEG video for human
        // expert-demonstration runs (manual driving).
        app.MapPost("/api/demo/start", async (DemoStartRequest request, SessionLogger logger, SessionVideoRecorder videoRecorder, CancellationToken cancellationToken) =>
        {
            var tag = string.IsNullOrWhiteSpace(request.Tag) ? "human-demo" : request.Tag.Trim();
            var clientId = request.ClientId ?? "web";
            var runtimeMode = request.RuntimeMode ?? "real-robot";
            var logState = await logger.StartAsync(tag, cancellationToken);
            var mjpegUrl = $"http://127.0.0.1:5287/api/camera/mjpeg?clientId={clientId}&runtimeMode={runtimeMode}";
            var videoStarted = videoRecorder.TryStart(mjpegUrl, tag);
            await logger.WriteAsync("demo.started", new { tag, clientId, runtimeMode, mjpegUrl, videoStarted }, cancellationToken);
            return Results.Ok(new
            {
                isRecording = true,
                tag,
                sessionLogPath = logState.CurrentFile,
                videoPath = videoRecorder.CurrentFile,
                videoStarted,
            });
        });

        app.MapPost("/api/demo/stop", async (SessionLogger logger, SessionVideoRecorder videoRecorder, CancellationToken cancellationToken) =>
        {
            var videoFile = videoRecorder.CurrentFile;
            videoRecorder.Stop();
            await logger.WriteAsync("demo.stopped", new { videoFile }, cancellationToken);
            var logState = await logger.StopAsync(cancellationToken);
            return Results.Ok(new
            {
                isRecording = false,
                sessionLogPath = logState.CurrentFile,
                videoPath = videoFile,
            });
        });

        // ── Demo replay ──
        // POST /api/demo/replay/start    — load JSONL, schedule command.outgoing events, fire to robot
        // POST /api/demo/replay/stop     — cancel current playback, send DirStop
        // GET  /api/demo/replay/status   — current state + progress (poll while playing)
        // GET  /api/demo/replay/sessions — list available session JSONL files
        app.MapPost("/api/demo/replay/start", (DemoReplayStartRequest request, DemoReplayService replay) =>
        {
            try
            {
                var info = replay.Start(
                    clientId: request.ClientId,
                    runtimeMode: request.RuntimeMode,
                    sessionFilePath: request.SessionFilePath,
                    agentId: request.AgentId,
                    speedMultiplier: request.SpeedMultiplier ?? 1.0);
                return Results.Ok(info);
            }
            catch (FileNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
            catch (Exception e) { return Results.BadRequest(new { error = e.Message }); }
        });

        app.MapPost("/api/demo/replay/stop", async (DemoReplayService replay, CancellationToken cancellationToken) =>
        {
            await replay.StopAsync(cancellationToken);
            return Results.Ok(new { stopped = true, state = replay.State.ToString() });
        });

        app.MapGet("/api/demo/replay/status", (DemoReplayService replay) =>
        {
            return Results.Ok(replay.GetProgress());
        });

        app.MapGet("/api/demo/replay/sessions", (DemoReplayService replay, SessionLogger logger) =>
        {
            var sessions = replay.ListSessions(logger.LogsDirectory);
            return Results.Ok(sessions);
        });
    }
}
