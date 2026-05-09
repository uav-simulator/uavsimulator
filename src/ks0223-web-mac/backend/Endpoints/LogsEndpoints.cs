using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Services;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// Session-log lifecycle: start / stop / list / open-folder.
/// Originally lines 520-538 of Program.cs.
/// </summary>
internal static class LogsEndpoints
{
    public static void MapLogsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/logs/start", async (StartLoggingRequest request, SessionLogger logger, CancellationToken cancellationToken) =>
        {
            var state = await logger.StartAsync(request.Tag, cancellationToken);
            return Results.Ok(state);
        });

        app.MapPost("/api/logs/stop", async (SessionLogger logger, CancellationToken cancellationToken) =>
        {
            var state = await logger.StopAsync(cancellationToken);
            return Results.Ok(state);
        });

        app.MapGet("/api/logs/files", (SessionLogger logger) => Results.Ok(logger.ListRecentFiles()));

        app.MapPost("/api/logs/open-folder", async (SessionLogger logger) =>
        {
            await logger.OpenFolderAsync();
            return Results.Ok(new { opened = true, path = logger.LogsDirectory });
        });
    }
}
