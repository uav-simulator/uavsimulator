using Ks0223.Web.Backend.Models;
using Microsoft.ML.OnnxRuntime;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Ks0223.Web.Backend.Services;

public sealed class ModelRegistryService
{
    private const string RegistryFileName = "registry.json";

    private readonly object gate = new();
    private readonly ILogger<ModelRegistryService> logger;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly JsonSerializerOptions jsonNodeOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
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

    public IReadOnlyList<ModelCatalogEntryDto> ListCatalog()
    {
        lock (gate)
        {
            return state.Models
                .GroupBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ModelCatalogEntryDto(
                    group.First().Name,
                    group
                        .OrderByDescending(model => model.CreatedAtUtc)
                        .Select(ToDto)
                        .ToArray()))
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

    public ModelBindingDto? GetBinding(string clientId, string runtimeMode, string? agentId)
    {
        lock (gate)
        {
            var binding = ResolveBindingRecordLocked(clientId, runtimeMode, agentId);
            if (binding is null)
            {
                return null;
            }

            var model = FindModelLocked(binding.ModelId)
                ?? throw new InvalidOperationException($"Bound model '{binding.ModelId}' not found");
            return ToBindingDto(binding, model);
        }
    }

    public ModelBindingDto SetBinding(SetModelBindingRequest request)
    {
        lock (gate)
        {
            var target = NormalizeBindingTarget(request.ClientId, request.RuntimeMode, request.AgentId);
            var model = FindRequiredLocked(request.ModelId);

            state.Bindings.RemoveAll(item =>
                string.Equals(item.ClientId, target.ClientId, StringComparison.Ordinal) &&
                string.Equals(item.RuntimeMode, target.RuntimeMode, StringComparison.Ordinal) &&
                string.Equals(item.AgentId, target.AgentId, StringComparison.Ordinal));

            var binding = new ModelBindingRecord
            {
                ClientId = target.ClientId,
                RuntimeMode = target.RuntimeMode,
                AgentId = target.AgentId,
                ModelId = model.ModelId,
                BoundAtUtc = DateTimeOffset.UtcNow,
            };
            state.Bindings.Add(binding);
            PersistLocked();
            return ToBindingDto(binding, model);
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
            return ToRuntimeSpec(FindRequiredLocked(modelId));
        }
    }

    public ModelRuntimeSpec? TryGetBoundRuntimeSpec(string clientId, string runtimeMode, string? agentId)
    {
        lock (gate)
        {
            var binding = ResolveBindingRecordLocked(clientId, runtimeMode, agentId);
            if (binding is null)
            {
                return null;
            }

            var model = FindModelLocked(binding.ModelId);
            return model is null ? null : ToRuntimeSpec(model);
        }
    }

    public ModelRuntimeSpec ResolveRuntimeSpec(string? explicitModelId, string clientId, string runtimeMode, string? agentId)
    {
        if (!string.IsNullOrWhiteSpace(explicitModelId))
        {
            return GetRuntimeSpec(explicitModelId);
        }

        var bound = TryGetBoundRuntimeSpec(clientId, runtimeMode, agentId);
        if (bound is not null)
        {
            return bound;
        }

        return GetActiveRuntimeSpec();
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
        var extension = Path.GetExtension(originalFileName);
        if (!string.Equals(extension, ".onnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .onnx artifacts are supported");
        }

        metadataJson = NormalizeJsonOrFallback(metadataJson, "{}");
        metricsJson = NormalizeJsonOrFallback(metricsJson, "{}");
        var metadata = ParseMetadata(metadataJson);

        var normalizedName = CoalesceRequired(name, metadata.Name, "Model name is required");
        var normalizedVersion = CoalesceRequired(version, metadata.Version, "Model version is required");
        var normalizedSource = CoalesceOptional(source, metadata.Source, "manual-upload");

        var canonicalCompatibility = NormalizeCompatibility(metadata.Compatibility);
        var storedMetadataJson = BuildStoredMetadataJson(
            metadataJson,
            normalizedName,
            normalizedVersion,
            normalizedSource,
            canonicalCompatibility);

        lock (gate)
        {
            if (state.Models.Any(item =>
                    string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Version, normalizedVersion, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Model '{normalizedName}' version '{normalizedVersion}' is already installed");
            }
        }

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

        try
        {
            ValidateArtifactForBackend(artifactPath);
            await File.WriteAllTextAsync(metadataPath, storedMetadataJson, cancellationToken);
            await File.WriteAllTextAsync(metricsPath, metricsJson, cancellationToken);
        }
        catch
        {
            TryDeleteFile(artifactPath);
            TryDeleteFile(metadataPath);
            TryDeleteFile(metricsPath);
            throw;
        }

        ModelRecord model;
        lock (gate)
        {
            model = new ModelRecord
            {
                ModelId = modelId,
                Name = normalizedName,
                Version = normalizedVersion,
                Source = normalizedSource,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ArtifactFileName = artifactFileName,
                MetadataFileName = metadataFileName,
                MetricsFileName = metricsFileName,
                Compatibility = canonicalCompatibility,
            };

            state.Models.RemoveAll(item => string.Equals(item.ModelId, modelId, StringComparison.Ordinal));
            state.Models.Add(model);
            if (string.IsNullOrWhiteSpace(state.ActiveModelId))
            {
                state.ActiveModelId = modelId;
            }

            PersistLocked();
        }

        logger.LogInformation("Uploaded model {ModelId} ({Name} {Version})", model.ModelId, model.Name, model.Version);
        return ToDto(model);
    }

    public ModelInfoDto Activate(string modelId)
    {
        lock (gate)
        {
            var normalizedId = NormalizeModelId(modelId);
            var model = FindRequiredLocked(normalizedId);
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
                return new RegistryState();
            }

            var json = File.ReadAllText(registryPath);
            var loaded = JsonSerializer.Deserialize<RegistryState>(json, jsonOptions);
            if (loaded is null)
            {
                return new RegistryState();
            }

            loaded.Models ??= new List<ModelRecord>();
            loaded.Bindings ??= new List<ModelBindingRecord>();
            foreach (var item in loaded.Models)
            {
                item.Compatibility ??= new CompatibilityRecord();
            }

            return loaded;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load model registry from {Path}", registryPath);
            return new RegistryState();
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

    private ModelRecord FindRequiredLocked(string modelId) =>
        FindModelLocked(modelId) ?? throw new InvalidOperationException($"Model '{NormalizeModelId(modelId)}' not found");

    private ModelRecord? FindModelLocked(string modelId)
    {
        var normalizedId = NormalizeModelId(modelId);
        return state.Models.FirstOrDefault(item => string.Equals(item.ModelId, normalizedId, StringComparison.Ordinal));
    }

    private ModelBindingRecord? ResolveBindingRecordLocked(string clientId, string runtimeMode, string? agentId)
    {
        var target = NormalizeBindingTarget(clientId, runtimeMode, agentId);
        return state.Bindings.FirstOrDefault(item =>
            string.Equals(item.ClientId, target.ClientId, StringComparison.Ordinal) &&
            string.Equals(item.RuntimeMode, target.RuntimeMode, StringComparison.Ordinal) &&
            string.Equals(item.AgentId, target.AgentId, StringComparison.Ordinal));
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
            Path.Combine(storageDir, record.MetricsFileName),
            ToDto(record.Compatibility));

    private ModelBindingDto ToBindingDto(ModelBindingRecord binding, ModelRecord record) =>
        new(
            binding.ClientId,
            binding.RuntimeMode,
            binding.AgentId,
            binding.ModelId,
            record.Name,
            record.Version,
            record.Source,
            binding.BoundAtUtc,
            ToDto(record.Compatibility));

    private ModelRuntimeSpec ToRuntimeSpec(ModelRecord record) =>
        new(
            record.ModelId,
            record.Name,
            record.Version,
            record.Source,
            Path.Combine(storageDir, record.ArtifactFileName),
            Path.Combine(storageDir, record.MetadataFileName),
            Path.Combine(storageDir, record.MetricsFileName),
            ToDto(record.Compatibility));

    private static CompatibilityHintsDto ToDto(CompatibilityRecord? compatibility)
    {
        if (compatibility is null)
        {
            return new CompatibilityHintsDto(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        }

        return
        new(
            compatibility.RuntimeModes ?? Array.Empty<string>(),
            compatibility.VehicleIds ?? Array.Empty<string>(),
            compatibility.RobotKinds ?? Array.Empty<string>());
    }

    private static string NormalizeJsonOrFallback(string? candidate, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();
        using var _ = JsonDocument.Parse(value);
        return value;
    }

    private static ParsedMetadata ParseMetadata(string metadataJson)
    {
        using var document = JsonDocument.Parse(metadataJson);
        var root = document.RootElement;
        var compatibilityRoot = TryGetProperty(root, "compatibility");

        return new ParsedMetadata
        {
            Name = ReadString(root, "name", "modelName"),
            Version = ReadString(root, "version"),
            Source = ReadString(root, "source"),
            Compatibility = new CompatibilityRecord
            {
                RuntimeModes = ReadStringArray(compatibilityRoot, "runtimeModes", "runtime_modes"),
                VehicleIds = ReadStringArray(compatibilityRoot, "vehicleIds", "vehicle_ids"),
                RobotKinds = ReadStringArray(compatibilityRoot, "robotKinds", "robot_kinds"),
            },
        };
    }

    private string BuildStoredMetadataJson(
        string metadataJson,
        string name,
        string version,
        string source,
        CompatibilityRecord compatibility)
    {
        var root = JsonNode.Parse(metadataJson)?.AsObject() ?? new JsonObject();
        root["name"] = name;
        root["version"] = version;
        root["source"] = source;

        var compatibilityNode = root["compatibility"] as JsonObject ?? new JsonObject();
        compatibilityNode["runtimeModes"] = BuildArrayNode(compatibility.RuntimeModes);
        compatibilityNode["vehicleIds"] = BuildArrayNode(compatibility.VehicleIds);
        compatibilityNode["robotKinds"] = BuildArrayNode(compatibility.RobotKinds);
        root["compatibility"] = compatibilityNode;

        return root.ToJsonString(jsonNodeOptions);
    }

    private static JsonArray BuildArrayNode(IEnumerable<string>? values)
    {
        var result = new JsonArray();
        foreach (var value in values ?? Array.Empty<string>())
        {
            result.Add(value);
        }

        return result;
    }

    private static CompatibilityRecord NormalizeCompatibility(CompatibilityRecord? compatibility)
    {
        compatibility ??= new CompatibilityRecord();
        return new CompatibilityRecord
        {
            RuntimeModes = NormalizeStringList(compatibility.RuntimeModes, RuntimeModes.Normalize),
            VehicleIds = NormalizeStringList(compatibility.VehicleIds, value => value.Trim()),
            RobotKinds = NormalizeStringList(compatibility.RobotKinds, value => value.Trim()),
        };
    }

    private static string[] NormalizeStringList(IEnumerable<string>? values, Func<string, string> normalize)
    {
        return (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(normalize)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CoalesceRequired(string? primary, string? fallback, string errorMessage)
    {
        var value = CoalesceOptional(primary, fallback, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value;
    }

    private static string CoalesceOptional(string? primary, string? fallback, string defaultValue)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return defaultValue;
    }

    private static BindingTarget NormalizeBindingTarget(string clientId, string runtimeMode, string? agentId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("clientId is required");
        }

        var normalizedMode = RuntimeModes.Normalize(runtimeMode);
        var normalizedAgentId = string.IsNullOrWhiteSpace(agentId) ? null : agentId.Trim();
        if (!string.Equals(normalizedMode, RuntimeModes.UnitySim, StringComparison.Ordinal))
        {
            normalizedAgentId = null;
        }

        return new BindingTarget(clientId.Trim(), normalizedMode, normalizedAgentId);
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

    private static void ValidateArtifactForBackend(string artifactPath)
    {
        try
        {
            using var session = new InferenceSession(artifactPath);
            if (session.InputMetadata.Count == 0)
            {
                throw new InvalidOperationException("ONNX model has no inputs");
            }

            if (session.OutputMetadata.Count == 0)
            {
                throw new InvalidOperationException("ONNX model has no outputs");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Model artifact is incompatible with backend ONNX runtime: {ex.Message}",
                ex);
        }
    }

    private static JsonElement? TryGetProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var candidate = TryGetProperty(element, propertyName);
            if (candidate is { ValueKind: JsonValueKind.String })
            {
                var value = candidate.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static string[] ReadStringArray(JsonElement? element, params string[] propertyNames)
    {
        if (element is null)
        {
            return Array.Empty<string>();
        }

        foreach (var propertyName in propertyNames)
        {
            var property = TryGetProperty(element.Value, propertyName);
            if (property is { ValueKind: JsonValueKind.Array })
            {
                return property.Value
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!.Trim())
                    .ToArray();
            }
        }

        return Array.Empty<string>();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup for failed uploads.
        }
    }

    private sealed class RegistryState
    {
        public string? ActiveModelId { get; set; }
        public List<ModelRecord> Models { get; set; } = new();
        public List<ModelBindingRecord> Bindings { get; set; } = new();
    }

    private sealed class ModelRecord
    {
        public string ModelId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public string ArtifactFileName { get; set; } = string.Empty;
        public string MetadataFileName { get; set; } = string.Empty;
        public string MetricsFileName { get; set; } = string.Empty;
        public CompatibilityRecord? Compatibility { get; set; } = new();
    }

    private sealed class ModelBindingRecord
    {
        public string ClientId { get; set; } = string.Empty;
        public string RuntimeMode { get; set; } = RuntimeModes.RealRobot;
        public string? AgentId { get; set; }
        public string ModelId { get; set; } = string.Empty;
        public DateTimeOffset BoundAtUtc { get; set; }
    }

    private sealed class CompatibilityRecord
    {
        public string[] RuntimeModes { get; set; } = Array.Empty<string>();
        public string[] VehicleIds { get; set; } = Array.Empty<string>();
        public string[] RobotKinds { get; set; } = Array.Empty<string>();
    }

    private sealed class ParsedMetadata
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Source { get; set; }
        public CompatibilityRecord? Compatibility { get; set; }
    }

    private sealed record BindingTarget(string ClientId, string RuntimeMode, string? AgentId);
}

public sealed record ModelRuntimeSpec(
    string ModelId,
    string Name,
    string Version,
    string Source,
    string ArtifactPath,
    string MetadataPath,
    string MetricsPath,
    CompatibilityHintsDto Compatibility);
