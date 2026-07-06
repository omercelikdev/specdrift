using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Specdrift.Tests;

/// <summary>
/// The M3 proof, protocol-level: a real client-shaped exchange over stdio — initialize,
/// list the tools, call spec_drift against a fixture repo, and read the findings back.
/// No SDK client here on purpose: raw JSON-RPC lines prove the wire format.
/// </summary>
public sealed class McpServerTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("specdrift-mcp").FullName;

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    [Fact]
    public async Task Stdio_server_lists_both_tools_and_answers_a_drift_call()
    {
        Directory.CreateDirectory(Path.Combine(_repo, ".specdrift"));
        File.WriteAllText(Path.Combine(_repo, ".specdrift", "drift.yaml"), """
            version: 1
            manifest: manifest.yaml
            wiring:
              - feature: features.outbox
                package: Platform.Messaging
            """);
        File.WriteAllText(Path.Combine(_repo, "manifest.yaml"), "features:\n  outbox: true");

        var serverPath = Path.Combine(AppContext.BaseDirectory, "specdrift.dll");
        using var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{serverPath}\" mcp")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        async Task<JsonElement> RoundTrip(object request)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            await process.StandardInput.FlushAsync();
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var message = JsonDocument.Parse(line).RootElement;
                if (message.TryGetProperty("id", out _))
                {
                    return message;
                }
            }
        }

        var initialize = await RoundTrip(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "specdrift-tests", version = "0" },
            },
        });
        Assert.Equal("specdrift", initialize.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());

        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        await process.StandardInput.FlushAsync();

        var tools = await RoundTrip(new { jsonrpc = "2.0", id = 2, method = "tools/list" });
        var names = tools.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("spec_validate", names);
        Assert.Contains("spec_drift", names);

        var call = await RoundTrip(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = "spec_drift", arguments = new { repoRoot = _repo } },
        });
        var payload = call.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("SPEC0201", payload);            // outbox on, package missing — the engine answered
        Assert.Contains("Platform.Messaging", payload);

        process.Kill(entireProcessTree: true);
    }
}
