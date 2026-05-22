using Ks0223.Web.Backend.Services;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// Saliency proxy endpoints: forwards a JPEG frame to the Python
/// policy_saliency_server.py daemon and returns the rendered heatmap PNG.
/// </summary>
internal static class SaliencyEndpoints
{
    public static void MapSaliencyEndpoints(this WebApplication app)
    {
        app.MapPost("/api/saliency/frame",
            async (HttpRequest req, SaliencyProxyService svc, CancellationToken cancellationToken) =>
            {
                using var ms = new MemoryStream();
                await req.Body.CopyToAsync(ms, cancellationToken);
                var modelId = req.Query["modelId"].ToString();

                try
                {
                    var heatmap = await svc.GetHeatmapAsync(ms.ToArray(), modelId, cancellationToken);
                    return Results.File(heatmap, "image/png");
                }
                catch (Exception ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
                }
            });
    }
}
