using System.Text.Json.Nodes;
using Specdrift.Validation;
using Specdrift.Yaml;
using Xunit;

namespace Specdrift.Tests;

/// <summary>
/// Keyword-by-keyword coverage of the in-tree evaluator — the evaluator IS the product;
/// every assertion path gets a fires/stays-quiet pair.
/// </summary>
public class SchemaEvaluatorTests
{
    private static List<Finding> Eval(string instanceYaml, string schemaJson)
        => SchemaEvaluator.Evaluate(YamlToJson.Parse(instanceYaml), JsonNode.Parse(schemaJson)!);

    private static string Single(string instanceYaml, string schemaJson)
        => Assert.Single(Eval(instanceYaml, schemaJson)).Message;

    [Theory]
    [InlineData("x: 1", """{ "properties": { "x": { "type": "integer" } } }""", true)]
    [InlineData("x: 1.5", """{ "properties": { "x": { "type": "integer" } } }""", false)]
    [InlineData("x: 1.5", """{ "properties": { "x": { "type": "number" } } }""", true)]
    [InlineData("x: text", """{ "properties": { "x": { "type": "number" } } }""", false)]
    [InlineData("x: true", """{ "properties": { "x": { "type": "boolean" } } }""", true)]
    [InlineData("x: true", """{ "properties": { "x": { "type": "integer" } } }""", false)]
    [InlineData("x: [1]", """{ "properties": { "x": { "type": "array" } } }""", true)]
    [InlineData("x: {}", """{ "properties": { "x": { "type": "object" } } }""", true)]
    [InlineData("x: null", """{ "properties": { "x": { "type": "null" } } }""", true)]
    [InlineData("x: word", """{ "properties": { "x": { "type": ["string", "null"] } } }""", true)]
    [InlineData("x: 3", """{ "properties": { "x": { "type": ["string", "null"] } } }""", false)]
    public void Type_assertions_distinguish_every_kind(string instance, string schema, bool valid)
        => Assert.Equal(valid, Eval(instance, schema).Count == 0);

    [Fact]
    public void Const_and_enum_compare_by_deep_value()
    {
        Assert.Empty(Eval("kind: service", """{ "properties": { "kind": { "const": "service" } } }"""));
        Assert.Contains("must be \"module\"", Single("kind: service", """{ "properties": { "kind": { "const": "module" } } }"""));
        Assert.Empty(Eval("n: 2", """{ "properties": { "n": { "enum": [1, 2, 3] } } }"""));
        Assert.Contains("one of", Single("n: 9", """{ "properties": { "n": { "enum": [1, 2, 3] } } }"""));
    }

    [Fact]
    public void String_bounds_and_pattern_report_the_actual_numbers()
    {
        var schema = """{ "properties": { "s": { "minLength": 2, "maxLength": 4, "pattern": "^[a-z]+$" } } }""";
        Assert.Empty(Eval("s: abc", schema));
        Assert.Contains("under the minimum 2", Single("s: a", schema));
        Assert.Contains("exceeds the maximum 4", Single("s: abcde", schema));
        Assert.Contains("does not match", Single("s: ABC", schema));
    }

    [Fact]
    public void Numeric_bounds_fire_on_integers_and_floats_alike()
    {
        var schema = """{ "properties": { "n": { "minimum": 1, "maximum": 10 } } }""";
        Assert.Empty(Eval("n: 5", schema));
        Assert.Empty(Eval("n: 5.5", schema));
        Assert.Contains("under the minimum", Single("n: 0", schema));
        Assert.Contains("exceeds the maximum", Single("n: 10.5", schema));
        Assert.Empty(Eval("n: not-a-number", schema));   // bounds only speak about numbers
    }

    [Fact]
    public void Fractional_bounds_are_compared_as_numbers_not_truncated_to_integers()
    {
        // Reading the BOUND as a long rounded 0.5 down to 0 and let 0.2 through in silence.
        var schema = """{ "properties": { "ratio": { "type": "number", "minimum": 0.5, "maximum": 1.5 } } }""";
        Assert.Contains("under the minimum", Single("ratio: 0.2", schema));
        Assert.Contains("exceeds the maximum", Single("ratio: 1.9", schema));
        Assert.Empty(Eval("ratio: 0.5", schema));
        Assert.Empty(Eval("ratio: 1.5", schema));
    }

    [Fact]
    public void Required_and_additionalProperties_guard_the_envelope()
    {
        var schema = """
            { "required": ["name"], "properties": { "name": {} }, "additionalProperties": false }
            """;
        Assert.Empty(Eval("name: x", schema));
        Assert.Contains(Eval("other: x", schema), f => f.Message.Contains("'name' is missing"));
        Assert.Contains(Eval("name: x\nextra: 1", schema), f => f.Message.Contains("not a known member"));
    }

    [Fact]
    public void AdditionalProperties_as_a_schema_validates_the_extras()
    {
        var schema = """{ "properties": { "known": {} }, "additionalProperties": { "type": "integer" } }""";
        Assert.Empty(Eval("known: x\nextra: 3", schema));
        Assert.Contains("should be integer", Single("extra: text", schema));
    }

    [Fact]
    public void Array_assertions_cover_items_bounds_and_uniqueness()
    {
        var schema = """
            { "properties": { "a": { "items": { "type": "integer" }, "minItems": 1, "maxItems": 3, "uniqueItems": true } } }
            """;
        Assert.Empty(Eval("a: [1, 2]", schema));
        Assert.Contains("minimum is 1", Single("a: []", schema));
        Assert.Contains("maximum is 3", Single("a: [1, 2, 3, 4]", schema));
        Assert.Contains("duplicate", Single("a: [1, 1]", schema));
        Assert.Contains("should be integer", Single("a: [x]", schema));
    }

    [Fact]
    public void Combinators_anyOf_and_not_behave()
    {
        var anyOf = """{ "properties": { "x": { "anyOf": [{ "type": "integer" }, { "type": "string" }] } } }""";
        Assert.Empty(Eval("x: 1", anyOf));
        Assert.Empty(Eval("x: word", anyOf));
        Assert.Contains("matches none", Single("x: true", anyOf));

        var not = """{ "properties": { "x": { "not": { "const": "forbidden" } } } }""";
        Assert.Empty(Eval("x: fine", not));
        Assert.Contains("must NOT match", Single("x: forbidden", not));
    }

    [Fact]
    public void OneOf_reports_exactly_one_finding_with_the_match_count()
    {
        var schema = """{ "properties": { "x": { "oneOf": [{ "type": "integer" }, { "type": "number" }] } } }""";
        Assert.Contains("matches 2 of 2", Single("x: 3", schema));            // ambiguous
        Assert.Contains("matches 0 of 2", Single("x: word", schema));         // none
        Assert.Empty(Eval("x: 1.5", schema));                                 // exactly the number branch
    }

    [Fact]
    public void Refs_resolve_internally_with_pointer_escapes_and_reject_external()
    {
        var schema = """
            { "properties": { "t": { "$ref": "#/$defs/a~1b" } }, "$defs": { "a/b": { "const": 1 } } }
            """;
        Assert.Empty(Eval("t: 1", schema));
        Assert.Contains("must be 1", Single("t: 2", schema));

        Assert.Contains("Only internal", Assert.Throws<FormatException>(
            () => Eval("t: 1", """{ "properties": { "t": { "$ref": "https://x/schema" } } }""")).Message);
        Assert.Contains("resolves to nothing", Assert.Throws<FormatException>(
            () => Eval("t: 1", """{ "properties": { "t": { "$ref": "#/$defs/missing" } } }""")).Message);
    }

    [Fact]
    public void Boolean_schemas_and_else_branches_work()
    {
        Assert.Contains("nothing is valid", Single("x: 1", """{ "properties": { "x": false } }"""));
        Assert.Empty(Eval("x: 1", """{ "properties": { "x": true } }"""));

        var ifElse = """
            { "if": { "required": ["a"] }, "then": { "required": ["b"] }, "else": { "required": ["c"] } }
            """;
        Assert.Empty(Eval("a: 1\nb: 1", ifElse));
        Assert.Contains("'b' is missing", Single("a: 1", ifElse));
        Assert.Contains("'c' is missing", Single("z: 1", ifElse));
    }

    [Fact]
    public void UnevaluatedProperties_as_a_schema_validates_the_leftovers()
    {
        var schema = """{ "properties": { "known": {} }, "unevaluatedProperties": { "type": "integer" } }""";
        Assert.Empty(Eval("known: x\nleftover: 3", schema));
        Assert.Contains("should be integer", Single("known: x\nleftover: text", schema));
    }

    [Fact]
    public void Findings_carry_json_pointer_style_paths()
    {
        var finding = Assert.Single(Eval(
            "outer:\n  inner: [1, text]",
            """{ "properties": { "outer": { "properties": { "inner": { "items": { "type": "integer" } } } } } }"""));
        Assert.Equal("#/outer/inner/1", finding.Path);
    }
}
