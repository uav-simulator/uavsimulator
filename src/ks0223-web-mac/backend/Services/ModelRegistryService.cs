using Ks0223.Web.Backend.Models;
using System.Text.Json;

namespace Ks0223.Web.Backend.Services;

public sealed class ModelRegistryService
{
    private const string RegistryFileName = "registry.json";

    private readonly object gate = new();
    private readonly ILogger<ModelRegistryService> logger;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string storageDir;
    private readonly string registryPath;
    private RegistryState state;

    public ModelRegistryService(IHostEnvironment environment, ILogger<ModelRegistryService> logger)
    {
        this.logger = logger;
        storageDir = Path.Combine(environment.ContentRootPath, "runtime-data", "models");
        registryPath = Path.Combine(storageDir, RegistryFileName);
        Directory.CreateDirectory(storageDir);
        state = LoadState();
    }

    public IReadOnlyList<ModelInfoDto> ListModels()
    {
        lock (gate)
        {
            return state.Models
                .OrderByDescending(model => model.CreatedAtUtc)
                .Select(ToDto)
                .ToArray();
        }
    }

    public ModelInfoDto? GetActiveModel()
    {
        lock (gate)
        {
            var active = ResolveActiveModel(state);
            return active is null ? null : ToDto(active);
        }
    }

    public ModelRuntimeSpec GetActiveRuntimeSpec()
    {
        lock (gate)
        {
            var active = ResolveActiveModel(state) ?? throw new InvalidOperationException("Active model is not selected");
            return ToRuntimeSpec(active);
        }
    }

    public ModelRuntimeSpec GetRuntimeSpec(string modelId)
    {
        lock (gate)
        {
            var normalizedId = NormalizeModelId(modelId);
            var model = state.Models.FirstOrDefault(item => string.Equals(item.ModelId, normalizedId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Model '{normalizedId}' not found");
            return ToRuntimeSpec(model);
        }
    }

    public async Task<ModelInfoDto> UploadAsync(
        Stream artifactStream,
        string originalFileName,
        string? name,
        string? version,
        string? source,
        string? metadataJson,
        string? metricsJson,
        CancellationToken cancellationToken)
    {
        var modelId = BuildModelId();
        var normalizedName = string.IsNullOrWhiteSpace(name) ? modelId : name.Trim();
        var normalizedVersion = string.IsNullOrWhiteSpace(version) ? DateTimeOffset.UtcNow.ToString("yyyy.MM.dd-HHmmss") : version.Trim();
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "manual-upload" : source.Trim();

        var extension = Path.GetExtension(originalFileName);
        if (!string.Equals(extension, ".onnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .onnx artifacts are supported");
        }

        metadataJson = NormalizeJsonOrFallback(metadataJson, "{}");
        metricsJson = NormalizeJsonOrFallback(metricsJson, "{}");

        var artifactFileName = $"{modelId}.onnx";
        var metadataFileName = $"{modelId}.metadata.json";
        var metricsFileName = $"{modelId}.metrics.json";

        var artifactPath = Path.Combine(storageDir, artifactFileName);
        var metadataPath = Path.Combine(storageDir, metadataFileName);
        var metricsPath = Path.Combine(storageDir, metricsFileName);

        await using (var target = File.Create(artifactPath))
        {
            await artifactStream.CopyToAsync(target, cancellationToken);
        }

        await File.WriteAllTextAsync(metadataPath, metadataJson, cancellationToken);
        await File.WriteAllTextAsync(metricsPath, metricsJson, cancellationToken);

        ModelRecord model;
        lock (gate)
        {
            model = new ModelRecord(
                modelId,
                normalizedName,
                normalizedVersion,
                normalizedSource,
                DateTimeOffset.UtcNow,
                artifactFileName,
                metadataFileName,
                metricsFileName);

            state.Models.RemoveAll(item => string.Equals(item.ModelId, modelId, StringComparison.Ordinal));
            state.Models.Add(model);
            if (string.IsNullOrWhiteSpace(state.ActiveModelId))
            {
                state.ActiveModelId = modelId;
            }

            PersistLocked();
        }

        logger.LogInformation("Uploaded model {ModelId} ({Name})", model.ModelId, model.Name);
        return ToDto(model);
    }

    public ModelInfoDto Activate(string modelId)
    {
        lock (gate)
        {
            var normalizedId = NormalizeModelId(modelId);
            var model = state.Models.FirstOrDefault(item => string.Equals(item.ModelId, normalizedId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Model '{normalizedId}' not found");

            state.ActiveModelId = normalizedId;
            PersistLocked();
            return ToDto(model);
        }
    }

    private RegistryState LoadState()
    {
        try
        {
            if (!File.Exists(registryPath))
            {
                return new RegistryState { Models = new List<ModelRecord>() };
            }

            var json = File.ReadAllText(registryPath);
            var loaded = JsonSerializer.Deserialize<RegistryState>(json, jsonOptions);
            if (loaded is null)
            {
                return new RegistryState { Models = new List<ModelRecord>() };
            }

            loaded.Models ??= new List<ModelRecord>();
            return loaded;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load model registry from {Path}", registryPath);
            return new RegistryState { Models = new List<ModelRecord>() };
        }
    }

    private void PersistLocked()
    {
        var json = JsonSerializer.Serialize(state, jsonOptions);
        File.WriteAllText(registryPath, json);
    }

    private ModelRecord? ResolveActiveModel(RegistryState current)
    {
        if (string.IsNullOrWhiteSpace(current.ActiveModelId))
        {
            return null;
        }

        return current.Models.FirstOrDefault(model =>
            string.Equals(model.ModelId, current.ActiveModelId, StringComparison.Ordinal));
    }

    private ModelInfoDto ToDto(ModelRecord record) =>
        new(
            record.ModelId,
            record.Name,
            record.Version,
            record.Source,
            record.CreatedAtUtc,
            string.Equals(state.ActiveModelId, record.ModelId, StringComparison.Ordinal),
            Path.Combine(storageDir, record.ArtifactFileName),
            Path.Combine(storageDir, record.MetadataFileName),
            Path.Combine(storageDir, record.MetricsFileName));

    private ModelRuntimeSpec ToRuntimeSpec(ModelRecord record) =>
        new(
            record.ModelId,
            record.Name,
            record.Version,
            record.Source,
            Path.Combine(storageDir, record.ArtifactFileName),
            Path.Combine(storageDir, record.MetadataFileName),
            Path.Combine(storageDir, record.MetricsFileName));

    private static string NormalizeJsonOrFallback(string? candidate, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();
        using var _ = JsonDocument.Parse(value);
        return value;
    }

    private static string NormalizeModelId(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException("modelId is required");
        }

        return modelId.Trim();
    }

    private static string BuildModelId() =>
        $"model-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}";

    private sealed class RegistryState
    {
        public string? ActiveModelId { get; set; }
        public List<ModelRecord> Models { get; set; } = new();
    }

    private sealed record ModelRecord(
        string ModelId,
        string Name,
        string Version,
        string Source,
        DateTimeOffset CreatedAtUtc,
        string ArtifactFileName,
        string MetadataFileName,
        string MetricsFileName);
}

public sealed record ModelRuntimeSpec(
    string ModelId,
    string Name,
    string Version,
    string Source,
    string ArtifactPath,
    string MetadataPath,
    string MetricsPath);
