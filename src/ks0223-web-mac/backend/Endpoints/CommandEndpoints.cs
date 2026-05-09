using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Services;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// The single /api/command endpoint that operator-driven commands flow through.
/// Cancels any active autopilot first (manual override) then dispatches to
/// the active runtime session. Originally lines 508-518 of Program.cs.
/// </summary>
internal static class CommandEndpoints
{
    public static void MapCommandEndpoints(this WebApplication app)
    {
        app.MapPost("/api/command", async (CommandRequest request, RuntimeSessionManager runtimeSessionManager, AutopilotService autopilotService, CancellationToken cancellationToken) =>
        {
            await autopilotService.HandleManualOverrideAsync(request.ClientId, request.RuntimeMode, cancellationToken);
            var response = await runtimeSessionManager.SendCommandAsync(
                request.ClientId,
                request.RuntimeMode,
                request.Command,
                request.AgentId,
                cancellationToken);
            return response.Sent ? Results.Ok(response) : Results.BadRequest(response);
        });
    }
}
