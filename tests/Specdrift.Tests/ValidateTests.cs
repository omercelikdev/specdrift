using Specdrift.Validation;
using Specdrift.Yaml;
using Xunit;

namespace Specdrift.Tests;

public class ValidateTests
{
    private const string Schema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["version", "features"],
          "properties": {
            "version": { "type": "integer" },
            "features": {
              "type": "object",
              "properties": {
                "caching": {
                  "type": "object",
                  "properties": {
                    "levels": { "type": "array", "items": { "enum": ["l1", "l2"] } },
                    "redis": {
                      "type": "object",
                      "properties": { "connectionName": { "type": "string" } }
                    }
                  }
                }
              },
              "additionalProperties": false
            }
          }
        }
        """;

    private const string Rules = """
        version: 1
        rules:
          - id: SPEC0101
            description: l2 needs a redis connection name
            when: { path: features.caching.levels, op: contains, value: l2 }
            require: { path: features.caching.redis.connectionName }
            severity: error
            message: "levels includes 'l2' but redis.connectionName is missing - name the connection."
          - id: SPEC0102
            when: { path: features.caching.levels, op: contains, value: l1 }
            forbid: { path: features.caching.forbiddenKnob }
            severity: warning
            message: "forbiddenKnob does nothing with l1."
        """;

    [Fact]
    public void Clean_manifest_produces_no_findings_and_exit_zero()
    {
        var report = ManifestValidator.Validate("""
            version: 1
            features:
              caching:
                levels: [l1, l2]
                redis: { connectionName: redis }
            """, Schema, Rules);

        Assert.Empty(report.Findings);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal("specdrift: clean — no findings", report.ToText());
    }

    [Fact]
    public void Schema_violations_surface_with_the_instance_path()
    {
        var report = ManifestValidator.Validate("""
            version: not-an-integer
            features:
              unknownFeature: true
            """, Schema, rulesText: null);

        Assert.Equal(1, report.ExitCode);
        Assert.Contains(report.Findings, f => f.RuleId == "SPEC0001" && f.Path.Contains("version"));
        Assert.Contains(report.Findings, f => f.Path.Contains("features"));   // additionalProperties
    }

    [Fact]
    public void Cross_field_rule_fires_with_a_teaching_message_and_exact_path()
    {
        var report = ManifestValidator.Validate("""
            version: 1
            features:
              caching:
                levels: [l1, l2]
            """, Schema, Rules);

        var finding = Assert.Single(report.Findings);
        Assert.Equal("SPEC0101", finding.RuleId);
        Assert.Equal("features.caching.redis.connectionName", finding.Path);
        Assert.Contains("name the connection", finding.Message);
        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public void When_condition_gates_the_rule_and_warnings_do_not_gate_the_exit_code()
    {
        // No l2 → SPEC0101 must not fire; l1 + forbidden knob → the WARNING fires, exit stays 0.
        var report = ManifestValidator.Validate("""
            version: 1
            features:
              caching:
                levels: [l1]
                forbiddenKnob: true
            """, Schema, Rules);

        var finding = Assert.Single(report.Findings);
        Assert.Equal("SPEC0102", finding.RuleId);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public void Reports_are_deterministic_byte_for_byte()
    {
        const string manifest = """
            version: 1
            features:
              caching:
                levels: [l2]
            """;
        var first = ManifestValidator.Validate(manifest, Schema, Rules);
        var second = ManifestValidator.Validate(manifest, Schema, Rules);

        Assert.Equal(first.ToText(), second.ToText());
        Assert.Equal(first.ToJson(), second.ToJson());
    }

    [Fact]
    public void Unknown_rules_version_hard_fails_never_guesses_forward()
    {
        var ex = Assert.Throws<FormatException>(() => RuleEngine.Load("""
            version: 2
            rules: []
            """));
        Assert.Contains("never guess forward", ex.Message);
    }

    [Fact]
    public void Malformed_rules_fail_loudly_with_the_rule_id()
    {
        Assert.Contains("exactly one of", Assert.Throws<FormatException>(() => RuleEngine.Load("""
            version: 1
            rules:
              - id: SPEC9999
                message: m
            """)).Message);

        Assert.Contains("teach the fix", Assert.Throws<FormatException>(() => RuleEngine.Load("""
            version: 1
            rules:
              - id: SPEC9999
                require: { path: a.b }
            """)).Message);

        Assert.Contains("unknown op", Assert.Throws<FormatException>(() => RuleEngine.Load("""
            version: 1
            rules:
              - id: SPEC9999
                when: { path: a, op: regex, value: x }
                require: { path: b }
                message: m
            """)).Message);
    }

    [Fact]
    public void Failed_if_branches_and_passing_oneOf_alternatives_never_produce_noise()
    {
        // A kind-discriminated schema, the exact shape that buried real findings in branch noise.
        const string schema = """
            {
              "type": "object",
              "properties": {
                "kind": { "enum": ["solution", "service"] },
                "toggle": { "oneOf": [{ "type": "boolean" }, { "type": "object" }] }
              },
              "allOf": [
                {
                  "if": { "properties": { "kind": { "const": "solution" } } },
                  "then": { "required": ["modules"] }
                },
                {
                  "if": { "properties": { "kind": { "const": "service" } } },
                  "then": { "required": ["ownerTeam"] }
                }
              ]
            }
            """;

        // Valid service manifest: the solution `if` fails and toggle's object branch fails —
        // NONE of that may surface.
        var clean = ManifestValidator.Validate("""
            kind: service
            ownerTeam: risk
            toggle: true
            """, schema, rulesText: null);
        Assert.Empty(clean.Findings);

        // Invalid service manifest: exactly ONE finding, the real one.
        var dirty = ManifestValidator.Validate("kind: service\ntoggle: true", schema, rulesText: null);
        var finding = Assert.Single(dirty.Findings);
        Assert.Contains("ownerTeam", finding.Message);
    }

    [Fact]
    public void UnevaluatedProperties_sees_through_in_place_applicators()
    {
        const string schema = """
            {
              "type": "object",
              "properties": { "kind": { "enum": ["a", "b"] } },
              "allOf": [
                {
                  "if": { "properties": { "kind": { "const": "a" } }, "required": ["kind"] },
                  "then": { "properties": { "onlyForA": { "type": "string" } } }
                }
              ],
              "unevaluatedProperties": false
            }
            """;

        // kind=a evaluates onlyForA through the selected branch → clean.
        Assert.Empty(ManifestValidator.Validate("kind: a\nonlyForA: x", schema, null).Findings);

        // kind=b leaves onlyForA unevaluated → exactly one precise finding.
        var report = ManifestValidator.Validate("kind: b\nonlyForA: x", schema, null);
        var finding = Assert.Single(report.Findings);
        Assert.Contains("unevaluatedProperties", finding.Message);
        Assert.Equal("#/onlyForA", finding.Path);
    }

    [Fact]
    public void Unknown_schema_keywords_hard_fail_instead_of_guessing()
    {
        var ex = Assert.Throws<FormatException>(() => ManifestValidator.Validate(
            "a: 1", """{ "propertyNames": { "pattern": "^x" } }""", null));
        Assert.Contains("never guess", ex.Message);
    }

    [Fact]
    public void Yaml_scalars_carry_their_types_and_quoted_strings_stay_strings()
    {
        var node = YamlToJson.Parse("""
            enabled: true
            count: 42
            ratio: 0.5
            name: plain
            quoted: "true"
            nothing: null
            """)!;

        Assert.Equal("true", node["enabled"]!.ToString());
        Assert.True(node["enabled"]!.GetValue<bool>());
        Assert.Equal(42L, node["count"]!.GetValue<long>());
        Assert.Equal(0.5, node["ratio"]!.GetValue<double>());
        Assert.Equal("plain", node["name"]!.GetValue<string>());
        Assert.Equal("true", node["quoted"]!.GetValue<string>());   // quoted → string, on purpose
        Assert.Null(node["nothing"]);
    }
}
