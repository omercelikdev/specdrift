using Xunit;

namespace Specdrift.Tests;

public sealed class DriftCliTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("specdrift-driftcli").FullName;

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_repo, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Cli.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private const string Profile = """
        version: 1
        manifest: manifest.yaml
        wiring:
          - feature: features.outbox
            package: Platform.Messaging
        """;

    [Fact]
    public void Default_profile_path_is_dot_specdrift_under_the_repo()
    {
        Write(".specdrift/drift.yaml", Profile);
        Write("manifest.yaml", "features:\n  outbox: false");

        var (code, output, _) = Run("drift", "--repo", _repo);
        Assert.Equal(0, code);
        Assert.Contains("clean", output);
    }

    [Fact]
    public void Explicit_profile_findings_and_json_format_flow_through()
    {
        Write("profiles/custom.yaml", Profile);
        Write("manifest.yaml", "features:\n  outbox: true");

        var (code, output, _) = Run(
            "drift", "--repo", _repo,
            "--profile", Path.Combine(_repo, "profiles/custom.yaml"),
            "--format", "json");
        Assert.Equal(1, code);
        Assert.Contains("\"ruleId\": \"SPEC0201\"", output);
    }

    [Fact]
    public void Warnings_render_with_their_own_prefix_and_do_not_gate()
    {
        Write(".specdrift/drift.yaml", Profile);
        Write("manifest.yaml", "features:\n  outbox: false");
        Write("App.csproj", """<PackageReference Include="Platform.Messaging" />""");

        var (code, output, _) = Run("drift", "--repo", _repo);
        Assert.Equal(0, code);
        Assert.Contains("WARN  SPEC0203", output);
        Assert.Contains("dead weight", output);
    }

    [Fact]
    public void Usage_errors_are_exit_two()
    {
        Assert.Equal(2, Run("drift").Code);                                        // no --repo
        Assert.Equal(2, Run("drift", "--repo", _repo).Code);                       // no profile anywhere
        Assert.Equal(2, Run("drift", "--repo", _repo, "--format", "xml").Code);
        Assert.Equal(2, Run("drift", "surprise").Code);                            // stray positional
        Assert.Equal(2, Run("drift", "--repo", _repo, "--profile",
            Path.Combine(_repo, "nope.yaml")).Code);
    }
}
