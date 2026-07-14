using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Specdrift.Validation;

/// <summary>
/// A deterministic evaluator for the pragmatic JSON-Schema 2020-12 subset golden-path
/// manifests actually use. Written in-tree after every candidate library failed OUR OWN
/// gates: one ships a maintenance-fee EULA in its binaries, one compiles code at runtime
/// (wrong for a fast CLI), one silently skips if/then. The honesty contract:
/// an UNKNOWN assertion keyword is a HARD FAIL — this engine never guesses.
/// </summary>
public static class SchemaEvaluator
{
    private static readonly HashSet<string> s_annotations =
        ["$schema", "$id", "title", "description", "default", "examples", "deprecated",
         "readOnly", "writeOnly", "$comment", "format", "$defs", "definitions"];

    private static readonly HashSet<string> s_assertions =
        ["type", "enum", "const", "required", "properties", "additionalProperties",
         "oneOf", "anyOf", "allOf", "not", "if", "then", "else", "$ref",
         "pattern", "minLength", "maxLength", "minimum", "maximum",
         "items", "uniqueItems", "minItems", "maxItems", "unevaluatedProperties"];

    /// <summary>Evaluates <paramref name="instance"/> against <paramref name="schema"/>.</summary>
    public static List<Finding> Evaluate(JsonNode? instance, JsonNode schema)
    {
        var findings = new List<Finding>();
        Visit(instance, schema, schema, "#", findings, evaluated: null);
        return findings;
    }

    // `evaluated` tracks which object members THIS schema (and its in-place applicators:
    // allOf, the matching oneOf/anyOf branches, the selected if-branch, $ref) has covered —
    // the state `unevaluatedProperties` is defined over.
    private static void Visit(JsonNode? instance, JsonNode schema, JsonNode root, string path, List<Finding> findings, HashSet<string>? evaluated)
    {
        evaluated ??= instance is JsonObject ? [] : null;
        if (schema is JsonValue boolSchema && boolSchema.TryGetValue<bool>(out var allowed))
        {
            if (!allowed)
            {
                Report(findings, path, "false-schema", "nothing is valid here — remove this member");
            }

            return;
        }

        var obj = schema.AsObject();
        foreach (var (keyword, _) in obj)
        {
            if (!s_annotations.Contains(keyword) && !s_assertions.Contains(keyword))
            {
                throw new FormatException(
                    $"Schema keyword '{keyword}' is not understood by this engine — never guess. "
                    + "Supported assertions: " + string.Join(", ", s_assertions.OrderBy(k => k, StringComparer.Ordinal)));
            }
        }

        if (obj["$ref"] is { } reference)
        {
            Visit(instance, ResolveRef(root, reference.GetValue<string>()), root, path, findings, evaluated);
            return;   // 2020-12 allows siblings, but manifests don't use them — keep the model simple.
        }

        CheckType(instance, obj, path, findings);
        CheckConstEnum(instance, obj, path, findings);
        CheckStrings(instance, obj, path, findings);
        CheckNumbers(instance, obj, path, findings);
        CheckObject(instance, obj, root, path, findings, evaluated);
        CheckArray(instance, obj, root, path, findings);
        CheckCombinators(instance, obj, root, path, findings, evaluated);
        CheckUnevaluated(instance, obj, root, path, findings, evaluated);
    }

    private static void CheckType(JsonNode? instance, JsonObject schema, string path, List<Finding> findings)
    {
        if (schema["type"] is not { } typeNode)
        {
            return;
        }

        var expected = typeNode is JsonArray types
            ? types.Select(t => t!.GetValue<string>()).ToArray()
            : [typeNode.GetValue<string>()];
        if (!expected.Any(t => MatchesType(instance, t)))
        {
            Report(findings, path, "type", $"value is {ActualType(instance)} but should be {string.Join(" or ", expected)}");
        }
    }

    private static void CheckConstEnum(JsonNode? instance, JsonObject schema, string path, List<Finding> findings)
    {
        if (schema["const"] is { } constant && !JsonNode.DeepEquals(instance, constant))
        {
            Report(findings, path, "const", $"value must be {constant.ToJsonString()}");
        }

        if (schema["enum"] is JsonArray options && !options.Any(o => JsonNode.DeepEquals(instance, o)))
        {
            Report(findings, path, "enum", $"value must be one of {string.Join(", ", options.Select(o => o?.ToJsonString() ?? "null"))}");
        }
    }

    private static void CheckStrings(JsonNode? instance, JsonObject schema, string path, List<Finding> findings)
    {
        if (instance is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return;
        }

        if (schema["pattern"] is { } pattern
            && !Regex.IsMatch(text, pattern.GetValue<string>(), RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            Report(findings, path, "pattern", $"value does not match {pattern.GetValue<string>()}");
        }

        if (schema["minLength"] is { } min && text.Length < AsLong(min))
        {
            Report(findings, path, "minLength", $"length {text.Length} is under the minimum {AsLong(min)}");
        }

        if (schema["maxLength"] is { } max && text.Length > AsLong(max))
        {
            Report(findings, path, "maxLength", $"length {text.Length} exceeds the maximum {AsLong(max)}");
        }
    }

    private static void CheckNumbers(JsonNode? instance, JsonObject schema, string path, List<Finding> findings)
    {
        if (instance is not JsonValue value
            || value.TryGetValue<string>(out _)
            || value.TryGetValue<bool>(out _)
            || !TryNumber(value, out var number))
        {
            return;
        }

        // Bounds on numbers are numbers, not integers — reading them as long would round
        // `minimum: 0.5` down to 0 and pass 0.2 in silence.
        if (schema["minimum"] is { } min && number < AsDouble(min))
        {
            Report(findings, path, "minimum", $"value {value} is under the minimum {min}");
        }

        if (schema["maximum"] is { } max && number > AsDouble(max))
        {
            Report(findings, path, "maximum", $"value {value} exceeds the maximum {max}");
        }
    }

    private static void CheckObject(JsonNode? instance, JsonObject schema, JsonNode root, string path, List<Finding> findings, HashSet<string>? evaluated)
    {
        if (instance is not JsonObject obj)
        {
            return;
        }

        foreach (var required in (schema["required"] as JsonArray) ?? [])
        {
            var name = required!.GetValue<string>();
            if (!obj.ContainsKey(name))
            {
                Report(findings, path, "required", $"required member '{name}' is missing");
            }
        }

        var properties = schema["properties"] as JsonObject;
        foreach (var (name, value) in obj)
        {
            if (properties?[name] is { } propertySchema)
            {
                evaluated?.Add(name);
                Visit(value, propertySchema, root, $"{path}/{name}", findings, evaluated: null);
            }
            else if (schema["additionalProperties"] is { } additional)
            {
                evaluated?.Add(name);
                if (additional is JsonValue flag && flag.TryGetValue<bool>(out var permitted) && !permitted)
                {
                    Report(findings, $"{path}/{name}", "additionalProperties", $"'{name}' is not a known member here");
                }
                else if (additional is JsonObject additionalSchema)
                {
                    Visit(value, additionalSchema, root, $"{path}/{name}", findings, evaluated: null);
                }
            }
        }
    }

    private static void CheckArray(JsonNode? instance, JsonObject schema, JsonNode root, string path, List<Finding> findings)
    {
        if (instance is not JsonArray array)
        {
            return;
        }

        if (schema["minItems"] is { } min && array.Count < AsLong(min))
        {
            Report(findings, path, "minItems", $"{array.Count} item(s), minimum is {AsLong(min)}");
        }

        if (schema["maxItems"] is { } max && array.Count > AsLong(max))
        {
            Report(findings, path, "maxItems", $"{array.Count} item(s), maximum is {AsLong(max)}");
        }

        if (schema["uniqueItems"] is JsonValue unique && unique.TryGetValue<bool>(out var mustBeUnique) && mustBeUnique)
        {
            for (var i = 0; i < array.Count; i++)
            {
                for (var j = i + 1; j < array.Count; j++)
                {
                    if (JsonNode.DeepEquals(array[i], array[j]))
                    {
                        Report(findings, $"{path}/{j}", "uniqueItems", "duplicate item");
                    }
                }
            }
        }

        if (schema["items"] is { } items)
        {
            for (var i = 0; i < array.Count; i++)
            {
                Visit(array[i], items, root, $"{path}/{i}", findings, evaluated: null);
            }
        }
    }

    private static void CheckCombinators(JsonNode? instance, JsonObject schema, JsonNode root, string path, List<Finding> findings, HashSet<string>? evaluated)
    {
        foreach (var branch in (schema["allOf"] as JsonArray) ?? [])
        {
            Visit(instance, branch!, root, path, findings, evaluated);
        }

        if (schema["oneOf"] is JsonArray oneOf)
        {
            var matching = oneOf.Where(b => Matches(instance, b!, root)).ToList();
            if (matching.Count != 1)
            {
                // One finding at the decision point — the failed alternatives are noise.
                Report(findings, path, "oneOf", $"value matches {matching.Count} of {oneOf.Count} alternatives (exactly one required)");
            }
            else
            {
                Visit(instance, matching[0]!, root, path, findings, evaluated);   // annotations count
            }
        }

        if (schema["anyOf"] is JsonArray anyOf)
        {
            var matching = anyOf.Where(b => Matches(instance, b!, root)).ToList();
            if (matching.Count == 0)
            {
                Report(findings, path, "anyOf", $"value matches none of the {anyOf.Count} alternatives");
            }
            else
            {
                foreach (var branch in matching)
                {
                    Visit(instance, branch!, root, path, findings, evaluated);
                }
            }
        }

        if (schema["not"] is { } not && Matches(instance, not, root))
        {
            Report(findings, path, "not", "value matches a schema it must NOT match");
        }

        if (schema["if"] is { } condition)
        {
            var branch = Matches(instance, condition, root) ? schema["then"] : schema["else"];
            if (branch is not null)
            {
                Visit(instance, branch, root, path, findings, evaluated);
            }
        }
    }

    private static void CheckUnevaluated(JsonNode? instance, JsonObject schema, JsonNode root, string path, List<Finding> findings, HashSet<string>? evaluated)
    {
        if (schema["unevaluatedProperties"] is not { } unevaluated || instance is not JsonObject obj)
        {
            return;
        }

        foreach (var (name, value) in obj)
        {
            if (evaluated?.Contains(name) is true)
            {
                continue;
            }

            if (unevaluated is JsonValue flag && flag.TryGetValue<bool>(out var permitted) && !permitted)
            {
                Report(findings, $"{path}/{name}", "unevaluatedProperties", $"'{name}' is not evaluated by any part of this schema");
            }
            else if (unevaluated is JsonObject unevaluatedSchema)
            {
                Visit(value, unevaluatedSchema, root, $"{path}/{name}", findings, evaluated: null);
            }
        }
    }

    private static bool Matches(JsonNode? instance, JsonNode schema, JsonNode root)
    {
        var probe = new List<Finding>();
        Visit(instance, schema, root, "#", probe, evaluated: null);
        return probe.Count == 0;
    }

    private static JsonNode ResolveRef(JsonNode root, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            throw new FormatException($"Only internal '#/...' references are supported; got '{reference}'.");
        }

        JsonNode? current = root;
        foreach (var segment in reference[2..].Split('/'))
        {
            current = current?[segment.Replace("~1", "/").Replace("~0", "~")];
        }

        return current ?? throw new FormatException($"'{reference}' resolves to nothing in this schema.");
    }

    private static bool MatchesType(JsonNode? instance, string type) => type switch
    {
        "null" => instance is null,
        "object" => instance is JsonObject,
        "array" => instance is JsonArray,
        "boolean" => instance is JsonValue b && b.TryGetValue<bool>(out _),
        "string" => instance is JsonValue s && s.TryGetValue<string>(out _),
        "integer" => instance is JsonValue i && i.TryGetValue<long>(out _)
            && !i.TryGetValue<string>(out _) && !i.TryGetValue<bool>(out _),
        "number" => instance is JsonValue n && TryNumber(n, out _)
            && !n.TryGetValue<string>(out _) && !n.TryGetValue<bool>(out _),
        _ => throw new FormatException($"Unknown type '{type}' in schema."),
    };

    private static string ActualType(JsonNode? instance) => instance switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue v when v.TryGetValue<bool>(out _) => "boolean",
        JsonValue v when v.TryGetValue<string>(out _) => "string",
        JsonValue v when v.TryGetValue<long>(out _) => "integer",
        _ => "number",
    };

    // Lengths and item counts are integer-valued by definition; numeric bounds are not.
    private static long AsLong(JsonNode node)
        => node.AsValue().TryGetValue<long>(out var l) ? l : (long)node.GetValue<double>();

    private static double AsDouble(JsonNode node)
        => node.AsValue().TryGetValue<double>(out var d) ? d : node.GetValue<long>();

    private static bool TryNumber(JsonValue value, out double number)
    {
        if (value.TryGetValue<double>(out number))
        {
            return true;
        }

        if (value.TryGetValue<long>(out var l))
        {
            number = l;
            return true;
        }

        return false;
    }

    private static void Report(List<Finding> findings, string path, string keyword, string message)
        => findings.Add(new Finding("SPEC0001", Severity.Error, path, $"schema/{keyword}: {message}"));
}
