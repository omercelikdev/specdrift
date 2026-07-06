namespace Specdrift.Validation;

/// <summary>Severity of a finding — errors gate (exit 1), warnings inform.</summary>
public enum Severity
{
    /// <summary>Blocks: the manifest is not fit to drive generation.</summary>
    Error,

    /// <summary>Reported, does not gate.</summary>
    Warning,
}

/// <summary>One validation finding. The message must TEACH the fix, not just name the sin.</summary>
public sealed record Finding(string RuleId, Severity Severity, string Path, string Message);

/// <summary>The deterministic result of a validate run.</summary>
public sealed record Report(IReadOnlyList<Finding> Findings)
{
    /// <summary>Exit-code contract: 0 clean · 1 error findings present.</summary>
    public int ExitCode => Findings.Any(f => f.Severity == Severity.Error) ? 1 : 0;

    /// <summary>Human rendering — one line per finding, stable order.</summary>
    public string ToText()
        => Findings.Count == 0
            ? "specdrift: clean — no findings"
            : string.Join('\n', Findings.Select(f =>
                $"{(f.Severity == Severity.Error ? "ERROR" : "WARN ")} {f.RuleId} at {f.Path}: {f.Message}"));

    /// <summary>Machine rendering — stable, minimal JSON.</summary>
    public string ToJson()
        => System.Text.Json.JsonSerializer.Serialize(
            Findings.Select(f => new
            {
                ruleId = f.RuleId,
                severity = f.Severity.ToString().ToLowerInvariant(),
                path = f.Path,
                message = f.Message,
            }),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}
