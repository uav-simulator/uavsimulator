using System.Text.Json;

namespace Ks0223.Web.Backend.Endpoints;

/// <summary>
/// JSON / query-string / form parsing helpers shared across the endpoint
/// extension classes in this folder. Originally inline at the bottom of
/// Program.cs as `static` local functions; extracted here verbatim (no
/// behaviour change) so each <c>Map<i>X</i>Endpoints()</c> module can reach them.
/// </summary>
internal static class EndpointHelpers
{
    public static async Task<JsonElement> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return default;
        }

        try
        {
            var body = await request.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return body.ValueKind == JsonValueKind.Undefined ? default : body;
        }
        catch
        {
            return default;
        }
    }

    public static string? ReadString(JsonElement element, params string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (!TryGetPropertyCaseInsensitive(element, key, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            {
                return value.ToString();
            }
        }

        return null;
    }

    public static int? ReadInt(JsonElement element, params string[] keys)
    {
        var raw = ReadString(element, keys);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, out var value) ? value : null;
    }

    public static double? ReadDouble(JsonElement element, params string[] keys)
    {
        var raw = ReadString(element, keys);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return double.TryParse(raw, out var value) ? value : null;
    }

    public static bool? ReadBool(JsonElement element, params string[] keys)
    {
        var raw = ReadString(element, keys);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (bool.TryParse(raw, out var boolean))
        {
            return boolean;
        }

        if (raw == "1")
        {
            return true;
        }

        if (raw == "0")
        {
            return false;
        }

        return null;
    }

    public static bool TryGetPropertyCaseInsensitive(JsonElement element, string key, out JsonElement value)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public static string? ReadStringQuery(HttpRequest request, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (request.Query.TryGetValue(key, out var value))
            {
                var first = value.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first))
                {
                    return first;
                }
            }
        }

        return null;
    }

    public static int? ReadIntQuery(HttpRequest request, params string[] keys)
    {
        var raw = ReadStringQuery(request, keys);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, out var value) ? value : null;
    }

    public static double? ReadDoubleQuery(HttpRequest request, params string[] keys)
    {
        var raw = ReadStringQuery(request, keys);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return double.TryParse(raw, out var value) ? value : null;
    }

    public static bool? ReadBoolQuery(HttpRequest request, params string[] keys)
    {
        var raw = ReadStringQuery(request, keys);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (bool.TryParse(raw, out var boolean))
        {
            return boolean;
        }

        if (raw == "1")
        {
            return true;
        }

        if (raw == "0")
        {
            return false;
        }

        return null;
    }

    public static string ReadClientIdQuery(HttpRequest request)
    {
        var clientId = ReadStringQuery(request, "clientId", "client_id");
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new BadHttpRequestException("clientId query parameter is required");
        }

        return clientId.Trim();
    }

    public static string? ReadOptionalClientIdQuery(HttpRequest request)
    {
        var clientId = ReadStringQuery(request, "clientId", "client_id");
        return string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
    }

    public static string ReadRuntimeModeQuery(HttpRequest request)
    {
        var runtimeMode = ReadStringQuery(request, "runtimeMode", "runtime_mode");
        if (string.IsNullOrWhiteSpace(runtimeMode))
        {
            throw new BadHttpRequestException("runtimeMode query parameter is required");
        }

        return runtimeMode.Trim();
    }

    public static string? ReadOptionalRuntimeModeQuery(HttpRequest request)
    {
        var runtimeMode = ReadStringQuery(request, "runtimeMode", "runtime_mode");
        return string.IsNullOrWhiteSpace(runtimeMode) ? null : runtimeMode.Trim();
    }

    public static string ReadClientId(HttpRequest request, JsonElement body)
    {
        var clientId = ReadStringQuery(request, "clientId", "client_id") ?? ReadString(body, "clientId", "client_id");
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new BadHttpRequestException("clientId is required");
        }

        return clientId.Trim();
    }

    public static string ReadRuntimeMode(HttpRequest request, JsonElement body)
    {
        var runtimeMode = ReadStringQuery(request, "runtimeMode", "runtime_mode") ?? ReadString(body, "runtimeMode", "runtime_mode");
        if (string.IsNullOrWhiteSpace(runtimeMode))
        {
            throw new BadHttpRequestException("runtimeMode is required");
        }

        return runtimeMode.Trim();
    }

    public static string? ReadFormValue(IFormCollection form, string key)
    {
        if (!form.TryGetValue(key, out var value))
        {
            return null;
        }

        var raw = value.FirstOrDefault();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }
}
