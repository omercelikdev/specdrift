using Specdrift.Drift;
using Specdrift.Validation;
using Xunit;

namespace Specdrift.Tests;

public sealed class DriftTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("specdrift-drift").FullName;

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private const string Profile = """
        version: 1
        manifest: .platform/manifest.yaml
        schemaVersion: 1
        wiring:
          - feature: features.outbox
            package: Platform.Messaging
            call: AddPlatformOutbox
          - feature: providers.auth
            equals: openid
            package: Platform.Auth
            call: AddPlatformAuth
        openapi:
          - committed: specs/openapi.json
            built: artifacts/openapi.json
        """;

    private Report Run(string manifestYaml)
    {
        Write(".platform/manifest.yaml", manifestYaml);
        return DriftEngine.Run(_repo, DriftEngine.LoadProfile(Profile));
    }

    [Fact]
    public void A_truthful_repo_reports_only_the_missing_built_artifact_warning()
    {
        Write("src/App/App.csproj", """<PackageReference Include="Platform.Messaging" />""");
        Write("src/App/Program.cs", "builder.AddPlatformOutbox();");
        var report = Run("schemaVersion: 1\nfeatures:\n  outbox: true");

        var finding = Assert.Single(report.Findings);
        Assert.Equal("SPEC0213", finding.RuleId);          // built openapi absent → warning only
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public void Feature_on_but_package_and_call_absent_names_both_gaps()
    {
        var report = Run("schemaVersion: 1\nfeatures:\n  outbox: true");

        Assert.Contains(report.Findings, f => f.RuleId == "SPEC0201" && f.Message.Contains("Platform.Messaging"));
        Assert.Contains(report.Findings, f => f.RuleId == "SPEC0202" && f.Message.Contains("AddPlatformOutbox"));
        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public void Referenced_but_wired_nowhere_is_its_own_precise_finding()
    {
        Write("src/App/App.csproj", """<PackageReference Include="Platform.Messaging" />""");
        var report = Run("schemaVersion: 1\nfeatures:\n  outbox: true");

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "SPEC0201");
        Assert.Contains(report.Findings, f => f.RuleId == "SPEC0202");
    }

    [Fact]
    public void Dead_weight_is_a_warning_the_reverse_direction_matters_too()
    {
        Write("src/App/App.csproj", """<PackageReference Include="Platform.Messaging" />""");
        var report = Run("schemaVersion: 1\nfeatures: {}");

        Assert.Contains(report.Findings, f => f.RuleId == "SPEC0203" && f.Severity == Severity.Warning);
    }

    [Fact]
    public void Value_gated_wiring_uses_equals()
    {
        Write("src/App/App.csproj", """<PackageReference Include="Platform.Auth" />""");
        Write("src/App/Program.cs", "builder.AddPlatformAuth();");

        Assert.DoesNotContain(Run("schemaVersion: 1\nproviders:\n  auth: openid").Findings,
            f => f.Path == "providers.auth");
        Assert.Contains(Run("schemaVersion: 1\nproviders:\n  auth: none").Findings,
            f => f.RuleId == "SPEC0203" && f.Path == "providers.auth");   // auth off, package present
    }

    [Fact]
    public void In_gated_wiring_accepts_any_listed_value_without_false_dead_weight()
    {
        const string profile = """
            version: 1
            manifest: manifest.yaml
            wiring:
              - feature: providers.auth
                in: [openid, apikey]
                package: Platform.Auth
                call: AddPlatformAuth
            """;
        Write("src/App/App.csproj", """<PackageReference Include="Platform.Auth" />""");
        Write("src/App/Program.cs", "builder.AddPlatformAuth(o => o.Strategy = Strategy.ApiKey);");

        Write("manifest.yaml", "providers:\n  auth: apikey");
        Assert.Empty(DriftEngine.Run(_repo, DriftEngine.LoadProfile(profile)).Findings);   // no dead-weight FP

        Write("manifest.yaml", "providers:\n  auth: none");
        Assert.Contains(DriftEngine.Run(_repo, DriftEngine.LoadProfile(profile)).Findings,
            f => f.RuleId == "SPEC0203");
    }

    [Fact]
    public void Bin_and_obj_are_never_scanned()
    {
        Write("src/App/obj/Generated.cs", "builder.AddPlatformOutbox();");
        Write("src/App/bin/App.csproj", """<PackageReference Include="Platform.Messaging" />""");
        var report = Run("schemaVersion: 1\nfeatures:\n  outbox: true");

        Assert.Contains(report.Findings, f => f.RuleId == "SPEC0201");
        Assert.Contains(report.Findings, f => f.RuleId == "SPEC0202");
    }

    [Fact]
    public void OpenApi_drift_compares_semantically_and_teaches_each_state()
    {
        Write("artifacts/openapi.json", """{ "openapi": "3.1.1", "paths": { "/a": {} } }""");
        var missingCommit = Run("schemaVersion: 1");
        Assert.Contains(missingCommit.Findings, f => f.RuleId == "SPEC0211");

        // Same content, different formatting → NOT drift.
        Write("specs/openapi.json", """{ "paths": { "/a": {} }, "openapi": "3.1.1" }""");
        Assert.DoesNotContain(Run("schemaVersion: 1").Findings, f => f.RuleId is "SPEC0211" or "SPEC0212");

        // Real divergence → drift.
        Write("specs/openapi.json", """{ "openapi": "3.1.1", "paths": { "/b": {} } }""");
        Assert.Contains(Run("schemaVersion: 1").Findings, f => f.RuleId == "SPEC0212");
    }

    [Fact]
    public void Schema_version_skew_hard_flags()
    {
        var report = Run("schemaVersion: 2");
        Assert.Contains(report.Findings, f => f.RuleId == "SPEC0221" && f.Message.Contains("never guess forward"));
    }

    [Fact]
    public void Missing_manifest_is_the_first_and_only_finding()
    {
        var report = DriftEngine.Run(_repo, DriftEngine.LoadProfile(Profile));
        var finding = Assert.Single(report.Findings);
        Assert.Equal("SPEC0200", finding.RuleId);
    }

    [Fact]
    public void Profile_parsing_fails_loudly_on_bad_shapes()
    {
        Assert.Contains("never guess forward", Assert.Throws<FormatException>(
            () => DriftEngine.LoadProfile("version: 9")).Message);
        Assert.Contains("neither 'package' nor 'call'", Assert.Throws<FormatException>(
            () => DriftEngine.LoadProfile("version: 1\nmanifest: m.yaml\nwiring:\n  - feature: f")).Message);
        Assert.Contains("'manifest' path", Assert.Throws<FormatException>(
            () => DriftEngine.LoadProfile("version: 1")).Message);
    }
}
