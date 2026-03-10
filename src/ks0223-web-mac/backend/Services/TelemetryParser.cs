using System.Text.Json;

namespace Ks0223.Web.Backend.Services;

public sealed class TelemetryParser
{
    public IReadOnlyDictionary<string, string>? TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    map[prop.Name] = prop.Value.ToString();
                }

                return map.Count == 0 ? null : map;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        var pairs = trimmed
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(['=', ':'], 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToList();

        if (pairs.Count == 0)
        {
            return null;
        }

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            dict[pair[0]] = pair[1];
        }

        return dict.Count == 0 ? null : dict;
    }
}
