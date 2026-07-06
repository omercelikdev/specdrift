using Specdrift.Yaml;

namespace Specdrift.Validation;

/// <summary>
/// The `validate` verb: JSON-Schema shape validation (the in-tree
/// <see cref="SchemaEvaluator"/> — see its docs for WHY no library survived our gates)
/// + declared cross-field invariants. Deterministic by construction: same inputs,
/// byte-identical report.
/// </summary>
public static class ManifestValidator
{
    /// <summary>Validates manifest text against schema text and optional rules text.</summary>
    public static Report Validate(string manifestText, string schemaText, string? rulesText)
    {
        var manifest = YamlToJson.Parse(manifestText)
            ?? throw new FormatException("The manifest is empty.");
        var schema = YamlToJson.Parse(schemaText)
            ?? throw new FormatException("The schema is empty.");

        var findings = SchemaEvaluator.Evaluate(manifest, schema);

        if (rulesText is not null)
        {
            findings.AddRange(RuleEngine.Evaluate(manifest, RuleEngine.Load(rulesText)));
        }

        return new Report(findings);
    }
}
