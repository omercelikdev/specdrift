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
}
