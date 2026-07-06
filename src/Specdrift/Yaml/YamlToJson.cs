using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace Specdrift.Yaml;

/// <summary>
/// YAML → <see cref="JsonNode"/> with YAML 1.2 core-schema scalar inference (bool/int/float/
/// null; everything else stays a string). JSON manifests pass through untouched, so both
/// formats validate identically.
/// </summary>
public static class YamlToJson
{
    /// <summary>Parses YAML or JSON text into a JsonNode tree.</summary>
    public static JsonNode? Parse(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count == 0)
        {
            return null;
        }

        return Convert(stream.Documents[0].RootNode);
    }

    private static JsonNode? Convert(YamlNode node) => node switch
    {
        YamlMappingNode mapping => ConvertMapping(mapping),
        YamlSequenceNode sequence => ConvertSequence(sequence),
        YamlScalarNode scalar => ConvertScalar(scalar),
        _ => throw new NotSupportedException($"Unsupported YAML node kind '{node.GetType().Name}'."),
    };

    private static JsonObject ConvertMapping(YamlMappingNode mapping)
    {
        var result = new JsonObject();
        foreach (var (key, value) in mapping.Children)
        {
            result[((YamlScalarNode)key).Value ?? ""] = Convert(value);
        }

        return result;
    }

    private static JsonArray ConvertSequence(YamlSequenceNode sequence)
    {
        var result = new JsonArray();
        foreach (var item in sequence.Children)
        {
            result.Add(Convert(item));
        }

        return result;
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (value is null)
        {
            return null;
        }

        // Quoted scalars are ALWAYS strings — "true" and true differ on purpose.
        if (scalar.Style is YamlDotNet.Core.ScalarStyle.SingleQuoted or YamlDotNet.Core.ScalarStyle.DoubleQuoted)
        {
            return JsonValue.Create(value);
        }

        return value switch
        {
            "null" or "~" or "" => null,
            "true" => JsonValue.Create(true),
            "false" => JsonValue.Create(false),
            _ when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                => JsonValue.Create(l),
            _ when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                => JsonValue.Create(d),
            _ => JsonValue.Create(value),
        };
    }
}
