using Ks0223.Web.Backend.Services;
using static Ks0223.Web.Backend.Endpoints.EndpointHelpers;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// Camera read-only endpoints: status, single snapshot, MJPEG stream.
/// Originally lines 337-342 + 448-506 of Program.cs.
/// </summary>
internal static class CameraEndpoints
{
    public static void MapCameraEndpoints(this WebApplication app)
    {
        app.MapGet("/api/camera/status", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
        {
            var clientId = ReadClientIdQuery(http);
            var runtimeMode = ReadRuntimeModeQuery(http);
            return Results.Ok(runtimeSessionManager.GetCameraStatus(clientId, runtimeMode));
        });

        app.MapGet("/api/camera/snapshot", async (HttpContext context, RuntimeSessionManager runtimeSessionManager) =>
        {
            var requestedAgentId = context.Request.Query["agentId"].ToString();
            var clientId = ReadClientIdQuery(context.Request);
            var runtimeMode = ReadRuntimeModeQuery(context.Request);
            var hasFrame = runtimeSessionManager.TryGetLatestFrame(clientId, runtimeMode, requestedAgentId, out var frame, out var contentType, out _, out var timestamp);

            if (!hasFrame)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { error = "Camera frame is not available yet" });
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = contentType;
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
            if (timestamp is not null)
            {
                context.Response.Headers.LastModified = timestamp.Value.ToString("R");
            }

            await context.Response.Body.WriteAsync(frame, context.RequestAborted);
        });

        app.MapGet("/api/camera/mjpeg", async (HttpContext context, RuntimeSessionManager runtimeSessionManager) =>
        {
            const string boundary = "frame";
            var requestedAgentId = context.Request.Query["agentId"].ToString();
            var clientId = ReadClientIdQuery(context.Request);
            var runtimeMode = ReadRuntimeModeQuery(context.Request);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.Headers.ContentType = $"multipart/x-mixed-replace; boundary={boundary}";
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";

            var sentVersion = -1L;
            var delay = TimeSpan.FromMilliseconds(1000d / 12d);
            var token = context.RequestAborted;

            while (!token.IsCancellationRequested)
            {
                var hasFrame = runtimeSessionManager.TryGetLatestFrame(clientId, runtimeMode, requestedAgentId, out var frame, out _, out var version, out _);
                if (hasFrame && version != sentVersion)
                {
                    sentVersion = version;
                    var header = $"--{boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n";
                    await context.Response.WriteAsync(header, token);
                    await context.Response.Body.WriteAsync(frame, token);
                    await context.Response.WriteAsync("\r\n", token);
                    await context.Response.Body.FlushAsync(token);
                }

                await Task.Delay(delay, token);
            }
        });
    }
}
