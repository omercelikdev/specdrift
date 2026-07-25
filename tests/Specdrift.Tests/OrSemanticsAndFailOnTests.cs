using Specdrift.Drift;
using Specdrift.Validation;
using Xunit;

namespace Specdrift.Tests;

/// <summary>
/// 0.4.2 — the OR-semantics fix (a package shared by several wiring rows is justified
/// when ANY of them is enabled), the --fail-on gate, and the embedded schema default.
/// </summary>
public sealed class OrSemanticsAndFailOnTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("specdrift-042").FullName;

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // Three rows share Platform.Jobs — the goldpath issue #11 shape (bulk justifies it).
    private const string SharedPackageProfile = """
        version: 1
        manifest: .platform/manifest.yaml
        schemaVersion: 1
        wiring:
          - feature: features.bulk
            package: Platform.Jobs
            call: AddPlatformJobs
          - feature: features.notification
            package: Platform.Jobs
            call: AddPlatformJobs
          - feature: features.campaign
            package: Platform.Jobs
            call: AddPlatformJobs
        """;

    private Report Run(string manifestYaml)
    {
        Write(".platform/manifest.yaml", manifestYaml);
        return DriftEngine.Run(_repo, DriftEngine.LoadProfile(SharedPackageProfile));
    }

    [Fact]
    public void One_enabled_row_justifies_the_shared_package_for_all_rows()
    {
        Write("src/App/App.csproj", """<PackageReference Include="Platform.Jobs" />""");
        Write("src/App/Program.cs", "builder.AddPlatformJobs();");
        var report = Run("schemaVersion: 1\nfeatures:\n  bulk: true");

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "SPEC0203");
    }

    [Fact]
    public void An_unjustified_shared_package_is_flagged_ONCE_naming_every_candidate()
    {
        Write("src/App/App.csproj", """<PackageReference Include="Platform.Jobs" />""");
        var report = Run("schemaVersion: 1\nfeatures: {}");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "SPEC0203");
        Assert.Contains("features.bulk", finding.Path, StringComparison.Ordinal);
        Assert.Contains("features.notification", finding.Path, StringComparison.Ordinal);
        Assert.Contains("features.campaign", finding.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Fail_on_warn_gates_the_drift_exit_code()
    {
        Write("src/App/App.csproj", """<PackageReference Include="Platform.Jobs" />""");
        Write(".specdrift/drift.yaml", SharedPackageProfile);
        Write(".platform/manifest.yaml", "schemaVersion: 1\nfeatures: {}");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Assert.Equal(0, Cli.Run(["drift", "--repo", _repo], stdout, stderr));                       // warning informs
        Assert.Equal(1, Cli.Run(["drift", "--repo", _repo, "--fail-on", "warn"], stdout, stderr));  // warning gates
        Assert.Equal(2, Cli.Run(["drift", "--repo", _repo, "--fail-on", "nonsense"], stdout, stderr));
    }

    [Fact]
    public void Validate_falls_back_to_the_embedded_goldpath_schema()
    {
        Write("manifest.yaml", """
            schemaVersion: 1
            kind: solution
            name: Probe
            description: Embedded-schema probe app
            owner: team-probe
            architecture:
              deploymentModel: modular-monolith
              codeOrg: vertical-slice
            providers:
              db: postgresql
              cache: inmemory
              broker: none
              auth: none
            features: {}
            """);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = Cli.Run(["validate", Path.Combine(_repo, "manifest.yaml")], stdout, stderr);

        Assert.Equal(0, exit);   // no --schema given: the embedded v1 schema validated it
    }
}
