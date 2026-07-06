using System.Text.Json.Nodes;
using Specdrift.Yaml;

namespace Specdrift.Validation;

/// <summary>One declared invariant: when a condition holds, require/forbid another path.</summary>
public sealed record Rule(
    string Id,
    string? Description,
    Condition? When,
    string? Require,
    string? Forbid,
    Severity Severity,
    string Message);

/// <summary>A path condition: exists · equals · contains (array membership or substring).</summary>
public sealed record Condition(string Path, string Op, string? Value);

/// <summary>
/// Evaluates data-declared cross-field invariants over the manifest tree. Rules are PROFILE
/// data — the engine ships none of its own; each golden path declares what must hold.
/// </summary>
public static class RuleEngine
{
    /// <summary>Loads rules from YAML text (see README for the shape).</summary>
    public static IReadOnlyList<Rule> Load(string rulesYaml)
    {
        var root = YamlToJson.Parse(rulesYaml)
            ?? throw new FormatException("The rules file is empty.");
        var version = root["version"]?.GetValue<long>()
            ?? throw new FormatException("The rules file declares no 'version'.");
        if (version != 1)
        {
            throw new FormatException(
                $"Rules version {version} is not understood by this engine (supported: 1) — never guess forward.");
        }

        var rules = new List<Rule>();
        foreach (var node in root["rules"]?.AsArray() ?? [])
        {
            var rule = node!.AsObject();
            var id = rule["id"]?.GetValue<string>()
                ?? throw new FormatException("Every rule needs an 'id'.");
            var message = rule["message"]?.GetValue<string>()
                ?? throw new FormatException($"Rule '{id}' has no 'message' — messages must teach the fix.");
            var require = rule["require"]?["path"]?.GetValue<string>();
            var forbid = rule["forbid"]?["path"]?.GetValue<string>();
            if (require is null == (forbid is null))
            {
                throw new FormatException($"Rule '{id}' must declare exactly one of 'require' or 'forbid'.");
            }

            Condition? when = null;
            if (rule["when"] is JsonObject whenNode)
            {
                var op = whenNode["op"]?.GetValue<string>() ?? "exists";
                if (op is not ("exists" or "equals" or "contains"))
                {
                    throw new FormatException($"Rule '{id}': unknown op '{op}' (exists|equals|contains).");
                }

                when = new Condition(
                    whenNode["path"]?.GetValue<string>()
                        ?? throw new FormatException($"Rule '{id}': 'when' needs a 'path'."),
                    op,
                    whenNode["value"]?.ToString());
            }

            var severity = rule["severity"]?.GetValue<string>() switch
            {
                "warning" => Severity.Warning,
                null or "error" => Severity.Error,
                var other => throw new FormatException($"Rule '{id}': unknown severity '{other}'."),
            };

            rules.Add(new Rule(id, rule["description"]?.GetValue<string>(), when, require, forbid, severity, message));
        }

        return rules;
    }

    /// <summary>Runs every rule; returns findings in declaration order (deterministic).</summary>
    public static IReadOnlyList<Finding> Evaluate(JsonNode manifest, IReadOnlyList<Rule> rules)
    {
        var findings = new List<Finding>();
        foreach (var rule in rules)
        {
            if (rule.When is { } when && !Holds(manifest, when))
            {
                continue;
            }

            if (rule.Require is { } require && JsonPaths.IsAbsentOrEmpty(JsonPaths.Resolve(manifest, require)))
            {
                findings.Add(new Finding(rule.Id, rule.Severity, require, rule.Message));
            }
            else if (rule.Forbid is { } forbid && !JsonPaths.IsAbsentOrEmpty(JsonPaths.Resolve(manifest, forbid)))
            {
                findings.Add(new Finding(rule.Id, rule.Severity, forbid, rule.Message));
            }
        }

        return findings;
    }

    private static bool Holds(JsonNode manifest, Condition when)
    {
        var node = JsonPaths.Resolve(manifest, when.Path);
        return when.Op switch
        {
            "exists" => !JsonPaths.IsAbsentOrEmpty(node),
            "equals" => node is JsonValue value && value.ToString() == when.Value,
            "contains" => node switch
            {
                JsonArray array => array.Any(item => item?.ToString() == when.Value),
                JsonValue value => when.Value is not null
                    && value.ToString().Contains(when.Value, StringComparison.Ordinal),
                _ => false,
            },
            _ => false,
        };
    }

}
