using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Services;
using static Ks0223.Web.Backend.Endpoints.EndpointHelpers;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// Autopilot lifecycle + preview/status read-only endpoints.
/// Originally lines 206-245 of Program.cs.
/// </summary>
internal static class AutopilotEndpoints
{
    public static void MapAutopilotEndpoints(this WebApplication app)
    {
        app.MapPost("/api/autopilot/start", async (StartAutopilotRequest request, AutopilotService autopilotService, CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await autopilotService.StartAsync(request, cancellationToken);
                return Results.Ok(status);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/autopilot/stop", async (StopAutopilotRequest request, AutopilotService autopilotService, CancellationToken cancellationToken) =>
        {
            var status = await autopilotService.StopAsync(request, cancellationToken);
            return Results.Ok(status);
        });

        app.MapGet("/api/autopilot/preview", (HttpRequest http, AutopilotService autopilotService) =>
        {
            try
            {
                var clientId = ReadClientIdQuery(http);
                var runtimeMode = ReadRuntimeModeQuery(http);
                var agentId = ReadStringQuery(http, "agentId", "agent_id");
                return Results.Ok(autopilotService.SamplePreview(clientId, runtimeMode, agentId));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/autopilot/status", (HttpRequest http, AutopilotService autopilotService) =>
        {
            var clientId = ReadOptionalClientIdQuery(http);
            var runtimeMode = ReadOptionalRuntimeModeQuery(http);
            return Results.Ok(autopilotService.GetStatus(clientId, runtimeMode));
        });
    }
}
