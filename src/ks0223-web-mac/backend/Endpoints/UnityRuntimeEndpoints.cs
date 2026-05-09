using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Services;
using static Ks0223.Web.Backend.Endpoints.EndpointHelpers;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// Unity simulator runtime control: catalog read, runtime + client selection,
/// LAN port-scan discovery. Originally lines 254-335 of Program.cs.
/// </summary>
internal static class UnityRuntimeEndpoints
{
    public static void MapUnityRuntimeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/unity/runtime-catalog", async (HttpRequest http, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
        {
            try
            {
                var clientId = ReadClientIdQuery(http);
                var runtimeMode = ReadRuntimeModeQuery(http);
                var host = ReadStringQuery(http, "host");
                var port = ReadIntQuery(http, "port");
                var catalog = await runtimeSessionManager.GetUnityRuntimeCatalogAsync(clientId, runtimeMode, host, port, cancellationToken);
                return Results.Ok(catalog);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/unity/runtime-selection", async (UnityRuntimeSelectionRequest request, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
        {
            try
            {
                var catalog = await runtimeSessionManager.SetUnityRuntimeSelectionAsync(request, cancellationToken);
                return Results.Ok(catalog);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/unity/client-selection", async (UnityClientSelectionRequest request, RuntimeSessionManager runtimeSessionManager, CancellationToken cancellationToken) =>
        {
            try
            {
                var catalog = await runtimeSessionManager.SetUnityClientSelectionAsync(request, cancellationToken);
                return Results.Ok(catalog);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/unity/discover", async (HttpRequest http, CancellationToken cancellationToken) =>
        {
            var host = http.Query["host"].FirstOrDefault()?.Trim() ?? "127.0.0.1";
            var portFrom = int.TryParse(http.Query["portFrom"].FirstOrDefault(), out var pf) ? pf : 8000;
            var portTo = int.TryParse(http.Query["portTo"].FirstOrDefault(), out var pt) ? pt : portFrom + 7;
            portTo = Math.Min(portTo, portFrom + 15); // cap scan range

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
            var results = new List<object>();
            var tasks = new List<Task>();

            for (var port = portFrom; port <= portTo; port++)
            {
                var capturedPort = port;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var response = await httpClient.GetAsync($"http://{host}:{capturedPort}/health", cancellationToken);
                        if (response.IsSuccessStatusCode)
                        {
                            var body = await response.Content.ReadAsStringAsync(cancellationToken);
                            lock (results)
                            {
                                results.Add(new { port = capturedPort, host, baseUrl = $"http://{host}:{capturedPort}", healthy = true, health = System.Text.Json.JsonSerializer.Deserialize<object>(body) });
                            }
                        }
                    }
                    catch
                    {
                        // port not responding — skip
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
            var sorted = results.OrderBy(r => ((dynamic)r).port).ToList();
            return Results.Ok(new { host, portFrom, portTo, instances = sorted, count = sorted.Count });
        });
    }
}
