using Ks0223.Web.Backend.Models;
using Ks0223.Web.Backend.Services;
using static Ks0223.Web.Backend.Endpoints.EndpointHelpers;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// Model registry: upload / list / catalog / active / activate / bindings.
/// Originally lines 108-204 of Program.cs, moved here verbatim.
/// </summary>
internal static class ModelEndpoints
{
    public static void MapModelEndpoints(this WebApplication app)
    {
        app.MapPost("/api/models/upload", async (HttpRequest http, ModelRegistryService modelRegistry, CancellationToken cancellationToken) =>
        {
            if (!http.HasFormContentType)
            {
                return Results.BadRequest(new { error = "multipart/form-data is required" });
            }

            try
            {
                var form = await http.ReadFormAsync(cancellationToken);
                var file = form.Files.GetFile("file");
                if (file is null || file.Length <= 0)
                {
                    return Results.BadRequest(new { error = "Model artifact file is required in `file` field" });
                }

                await using var stream = file.OpenReadStream();
                var uploaded = await modelRegistry.UploadAsync(
                    stream,
                    file.FileName,
                    ReadFormValue(form, "name"),
                    ReadFormValue(form, "version"),
                    ReadFormValue(form, "source"),
                    ReadFormValue(form, "metadata"),
                    ReadFormValue(form, "metrics"),
                    cancellationToken);
                return Results.Ok(uploaded);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/models", (ModelRegistryService modelRegistry) =>
        {
            var models = modelRegistry.ListModels();
            return Results.Ok(models);
        });

        app.MapGet("/api/model-catalog", (ModelRegistryService modelRegistry) =>
        {
            var catalog = modelRegistry.ListCatalog();
            return Results.Ok(catalog);
        });

        app.MapGet("/api/models/active", (ModelRegistryService modelRegistry) =>
        {
            var model = modelRegistry.GetActiveModel();
            return model is null
                ? Results.NotFound(new { error = "Active model is not selected" })
                : Results.Ok(model);
        });

        app.MapPost("/api/models/activate", (ActivateModelRequest request, ModelRegistryService modelRegistry) =>
        {
            try
            {
                var active = modelRegistry.Activate(request.ModelId);
                return Results.Ok(active);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/model-bindings/current", (HttpRequest http, ModelRegistryService modelRegistry) =>
        {
            try
            {
                var clientId = ReadClientIdQuery(http);
                var runtimeMode = ReadRuntimeModeQuery(http);
                var agentId = ReadStringQuery(http, "agentId", "agent_id");
                var binding = modelRegistry.GetBinding(clientId, runtimeMode, agentId);
                return binding is null
                    ? Results.NotFound(new { error = "Model binding is not configured for this target" })
                    : Results.Ok(binding);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/model-bindings", (SetModelBindingRequest request, ModelRegistryService modelRegistry) =>
        {
            try
            {
                var binding = modelRegistry.SetBinding(request);
                return Results.Ok(binding);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}
