using System.Text.Json.Nodes;

namespace Specdrift.Validation;

/// <summary>Dotted-path resolution shared by the rule and drift engines.</summary>
internal static class JsonPaths
{
    public static JsonNode? Resolve(JsonNode root, string dottedPath)
    {
        JsonNode? current = root;
        foreach (var segment in dottedPath.Split('.'))
        {
            current = current is JsonObject obj ? obj[segment] : null;
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    public static bool IsAbsentOrEmpty(JsonNode? node) => node switch
    {
        null => true,
        JsonValue value => string.IsNullOrEmpty(value.ToString()),
        JsonArray array => array.Count == 0,
        _ => false,
    };

    /// <summary>
    /// Reads a node as an integer WITHOUT throwing on the wrong kind. Quoted YAML scalars stay
    /// strings on purpose, so `schemaVersion: "1"` reaches here as a string — a finding to report,
    /// never an unhandled cast.
    /// </summary>
    public static bool TryLong(JsonNode? node, out long value)
    {
        value = 0;
        return node is JsonValue candidate
            && !candidate.TryGetValue<string>(out _)
            && !candidate.TryGetValue<bool>(out _)
            && candidate.TryGetValue(out value);
    }

    /// <summary>Renders a node for a message: absent, or its JSON form so `1` and `"1"` read apart.</summary>
    public static string Describe(JsonNode? node) => node?.ToJsonString() ?? "<none>";
}
