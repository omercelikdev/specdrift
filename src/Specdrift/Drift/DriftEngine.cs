using System.Text.Json.Nodes;
using Specdrift.Validation;
using Specdrift.Yaml;

namespace Specdrift.Drift;

/// <summary>One wiring expectation: when the manifest enables a feature, the repo must show it.</summary>
public sealed record WiringRule(string Feature, IReadOnlyList<string>? In, string? Package, string? Call);

/// <summary>One committed-vs-built artifact pair.</summary>
public sealed record ArtifactPair(string Committed, string Built);

/// <summary>The drift profile — PROFILE DATA, like the rules file: the engine ships none.</summary>
public sealed record DriftProfile(
    string ManifestPath,
    long? ExpectedSchemaVersion,
    IReadOnlyList<WiringRule> Wiring,
    IReadOnlyList<ArtifactPair> OpenApi);

/// <summary>
/// The `drift` verb: manifest ↔ repository reality. Detection is TEXTUAL by design
/// (documented boundary): csproj scanning for package references and a bounded source scan
/// for wiring calls — cheap, fast, and honest about what it is. Roslyn-grade analysis
/// belongs in in-code analyzers, not here.
/// </summary>
public static class DriftEngine
{
    /// <summary>Loads a drift profile from YAML text.</summary>
    public static DriftProfile LoadProfile(string profileYaml)
    {
        var root = YamlToJson.Parse(profileYaml)
            ?? throw new FormatException("The drift profile is empty.");
        var version = root["version"]?.GetValue<long>()
            ?? throw new FormatException("The drift profile declares no 'version'.");
        if (version != 1)
        {
            throw new FormatException(
                $"Drift profile version {version} is not understood by this engine (supported: 1) — never guess forward.");
        }

        var manifestPath = root["manifest"]?.GetValue<string>()
            ?? throw new FormatException("The drift profile declares no 'manifest' path.");

        var wiring = new List<WiringRule>();
        foreach (var node in root["wiring"]?.AsArray() ?? [])
        {
            var rule = node!.AsObject();
            var feature = rule["feature"]?.GetValue<string>()
                ?? throw new FormatException("Every wiring rule needs a 'feature' path.");
            var package = rule["package"]?.GetValue<string>();
            var call = rule["call"]?.GetValue<string>();
            if (package is null && call is null)
            {
                throw new FormatException($"Wiring rule for '{feature}' declares neither 'package' nor 'call'.");
            }

            IReadOnlyList<string>? enabledValues = rule["equals"] is { } single
                ? [single.ToString()]
                : rule["in"] is JsonArray set
                    ? set.Select(v => v!.ToString()).ToList()
                    : null;
            wiring.Add(new WiringRule(feature, enabledValues, package, call));
        }

        var openapi = new List<ArtifactPair>();
        foreach (var node in root["openapi"]?.AsArray() ?? [])
        {
            var pair = node!.AsObject();
            openapi.Add(new ArtifactPair(
                pair["committed"]?.GetValue<string>()
                    ?? throw new FormatException("Every openapi entry needs a 'committed' path."),
                pair["built"]?.GetValue<string>()
                    ?? throw new FormatException("Every openapi entry needs a 'built' path.")));
        }

        return new DriftProfile(
            manifestPath,
            root["schemaVersion"]?.GetValue<long>(),
            wiring,
            openapi);
    }

    /// <summary>Runs every drift check against the repository root.</summary>
    public static Report Run(string repoRoot, DriftProfile profile)
    {
        var findings = new List<Finding>();

        var manifestPath = Path.Combine(repoRoot, profile.ManifestPath);
        if (!File.Exists(manifestPath))
        {
            findings.Add(new Finding("SPEC0200", Severity.Error, profile.ManifestPath,
                "the manifest the profile points at does not exist"));
            return new Report(findings);
        }

        var manifest = YamlToJson.Parse(File.ReadAllText(manifestPath))
            ?? throw new FormatException("The manifest is empty.");

        CheckSchemaVersion(manifest, profile, findings);
        CheckWiring(repoRoot, manifest, profile.Wiring, findings);
        CheckOpenApi(repoRoot, profile.OpenApi, findings);
        return new Report(findings);
    }

    private static void CheckSchemaVersion(JsonNode manifest, DriftProfile profile, List<Finding> findings)
    {
        if (profile.ExpectedSchemaVersion is not { } expected)
        {
            return;
        }

        var actual = JsonPaths.Resolve(manifest, "schemaVersion")?.GetValue<long>();
        if (actual != expected)
        {
            findings.Add(new Finding("SPEC0221", Severity.Error, "schemaVersion",
                $"manifest declares schemaVersion {actual?.ToString() ?? "<none>"} but this profile understands {expected} — align them, never guess forward"));
        }
    }

    private static void CheckWiring(string repoRoot, JsonNode manifest, IReadOnlyList<WiringRule> wiring, List<Finding> findings)
    {
        if (wiring.Count == 0)
        {
            return;
        }

        var projectTexts = Scan(repoRoot, "*.csproj");
        var sourceTexts = Scan(repoRoot, "*.cs");

        foreach (var rule in wiring)
        {
            var node = JsonPaths.Resolve(manifest, rule.Feature);
            var enabled = rule.In is { } values
                ? node is not null && values.Contains(node.ToString())
                : !JsonPaths.IsAbsentOrEmpty(node) && node?.ToString() != "false";

            var packagePresent = rule.Package is { } package
                && projectTexts.Any(t => t.Contains($"\"{package}\"", StringComparison.Ordinal));
            var callPresent = rule.Call is { } call
                && sourceTexts.Any(t => t.Contains(call, StringComparison.Ordinal));

            if (enabled)
            {
                if (rule.Package is not null && !packagePresent)
                {
                    findings.Add(new Finding("SPEC0201", Severity.Error, rule.Feature,
                        $"the manifest enables this feature but no project references '{rule.Package}' — the app does not do what the manifest says"));
                }

                if (rule.Call is not null && !callPresent)
                {
                    findings.Add(new Finding("SPEC0202", Severity.Error, rule.Feature,
                        $"the manifest enables this feature but '{rule.Call}' is called nowhere — referenced, not wired"));
                }
            }
            else if (rule.Package is not null && packagePresent)
            {
                findings.Add(new Finding("SPEC0203", Severity.Warning, rule.Feature,
                    $"'{rule.Package}' is referenced but the manifest does not enable this feature — dead weight, or an undeclared capability"));
            }
        }
    }

    private static void CheckOpenApi(string repoRoot, IReadOnlyList<ArtifactPair> pairs, List<Finding> findings)
    {
        foreach (var pair in pairs)
        {
            var committedPath = Path.Combine(repoRoot, pair.Committed);
            var builtPath = Path.Combine(repoRoot, pair.Built);
            var committedExists = File.Exists(committedPath);
            var builtExists = File.Exists(builtPath);

            if (!builtExists)
            {
                findings.Add(new Finding("SPEC0213", Severity.Warning, pair.Built,
                    "the built document is missing — run the build export before checking drift"));
                continue;
            }

            if (!committedExists)
            {
                findings.Add(new Finding("SPEC0211", Severity.Error, pair.Committed,
                    "the built document exists but nothing is committed — commit the contract so reviews can see it change"));
                continue;
            }

            var committed = JsonNode.Parse(File.ReadAllText(committedPath));
            var built = JsonNode.Parse(File.ReadAllText(builtPath));
            if (!JsonNode.DeepEquals(committed, built))
            {
                findings.Add(new Finding("SPEC0212", Severity.Error, pair.Committed,
                    $"the committed document no longer matches the built one ({pair.Built}) — the contract drifted; re-export and review the diff"));
            }
        }
    }

    private static List<string> Scan(string repoRoot, string pattern)
        => Directory.EnumerateFiles(repoRoot, pattern, SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !p.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"))
            .OrderBy(p => p, StringComparer.Ordinal)   // deterministic
            .Select(File.ReadAllText)
            .ToList();
}
