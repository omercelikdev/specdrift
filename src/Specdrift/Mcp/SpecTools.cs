using System.ComponentModel;
using ModelContextProtocol.Server;
using Specdrift.Drift;
using Specdrift.Validation;

namespace Specdrift.Mcp;

/// <summary>
/// The MCP surface: the SAME two verbs, so coding agents ask the engine instead of
/// guessing. The tools are thin adapters — all semantics live in the engines, and the
/// JSON payload is the CLI's `--format json` output plus the exit-code contract.
/// </summary>
[McpServerToolType]
public static class SpecTools
{
    [McpServerTool(Name = "spec_validate")]
    [Description("Validate a YAML/JSON manifest against a JSON schema and optional cross-field invariant rules. Returns findings as JSON; 'clean' is an empty array. Deterministic — same inputs, same output.")]
    public static string SpecValidate(
        [Description("Path to the manifest (yaml or json)")] string manifestPath,
        [Description("Path to the JSON schema")] string schemaPath,
        [Description("Optional path to the invariant rules yaml")] string? rulesPath = null)
    {
        var report = ManifestValidator.Validate(
            File.ReadAllText(manifestPath),
            File.ReadAllText(schemaPath),
            rulesPath is null ? null : File.ReadAllText(rulesPath));
        return report.ToJson();
    }

    [McpServerTool(Name = "spec_drift")]
    [Description("Detect manifest-vs-repository drift using the repo's drift profile (.specdrift/drift.yaml by default): features enabled without their package/wiring, dead-weight references, committed-vs-built OpenAPI divergence, schema-version skew. Returns findings as JSON; 'clean' is an empty array.")]
    public static string SpecDrift(
        [Description("Repository root to inspect")] string repoRoot,
        [Description("Optional explicit drift profile path")] string? profilePath = null)
    {
        profilePath ??= Path.Combine(repoRoot, ".specdrift", "drift.yaml");
        var profile = DriftEngine.LoadProfile(File.ReadAllText(profilePath));
        return DriftEngine.Run(repoRoot, profile).ToJson();
    }
}
