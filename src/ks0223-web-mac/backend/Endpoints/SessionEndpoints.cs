using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Services;
using static Ks0223.Web.Backend.Endpoints.EndpointHelpers;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// Read-only status / health / connection-target / wire-protocol endpoints
/// plus the connect/disconnect mutations. Originally lines 68-106 + 247-252
/// + 786-814 of Program.cs, moved here verbatim with no behaviour change.
/// </summary>
internal static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/status", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
        {
            var clientId = ReadClientIdQuery(http);
            var runtimeMode = ReadRuntimeModeQuery(http);
            return Results.Ok(runtimeSessionManager.GetStatus(clientId, runtimeMode));
        });

        app.MapGet("/api/health", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
        {
            var clientId = ReadClientIdQuery(http);
            var runtimeMode = ReadRuntimeModeQuery(http);
            return Results.Ok(runtimeSessionManager.GetHealth(clientId, runtimeMode));
        });

        app.MapPost("/api/connection/connect", async (ConnectRequest request, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await runtimeSessionManager.ConnectAsync(request.ClientId, request.RuntimeMode, request.Host, request.Port, cancellationToken);
                return Results.Ok(status);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/connection/disconnect", async (DisconnectRequest request, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await runtimeSessionManager.DisconnectAsync(request.ClientId, request.RuntimeMode, cancellationToken);
                return Results.Ok(status);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/connection/target", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
        {
            var clientId = ReadClientIdQuery(http);
            var runtimeMode = ReadRuntimeModeQuery(http);
            return Results.Ok(runtimeSessionManager.GetConnectionTarget(clientId, runtimeMode));
        });

        app.MapGet("/api/protocol", (HttpRequest http, RuntimeSessionManager runtimeSessionManager) =>
        {
            var clientId = ReadClientIdQuery(http);
            var runtimeMode = ReadRuntimeModeQuery(http);
            var target = runtimeSessionManager.GetConnectionTarget(clientId, runtimeMode);
            return Results.Ok(new
            {
                runtimeMode = target.RuntimeMode,
                transport = target.RuntimeMode == RuntimeModes.RealRobot ? "tcp" : "http-json-step",
                host = target.Host,
                port = target.Port,
                encoding = "utf-8 text",
                framing = "no delimiter/no length prefix; one command per send",
                sensorTelemetry = "HTTP JSON from Pi add-on endpoint /api/telemetry (optional)",
                supportedCommands = new[]
                {
                    "DirForward",
                    "DirBack",
                    "DirLeft",
                    "DirRight",
                    "DirStop",
                    "CamUp",
                    "CamDown",
                    "CamLeft",
                    "CamRight",
                    "CamStop",
                },
            });
        });
    }
}
