using Specdrift.Validation;

return Specdrift.Cli.Run(args, Console.Out, Console.Error);

namespace Specdrift
{
    /// <summary>
    /// The CLI shell — argument parsing kept by hand ON PURPOSE: one verb today, zero
    /// dependencies to audit, and the exit-code contract stays visible in one file.
    /// Exit codes: 0 clean · 1 error findings · 2 usage or I/O failure.
    /// </summary>
    public static class Cli
    {
        private const string Usage = """
            specdrift — deterministic spec lint for manifest-driven golden paths

            usage:
              specdrift validate <manifest.(yaml|yml|json)> --schema <schema.json> [--rules <rules.yaml>] [--format text|json]
              specdrift drift --repo <dir> [--profile <drift.yaml>] [--format text|json]
            """;

        /// <summary>Runs the CLI; returns the process exit code.</summary>
        public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                stdout.WriteLine(Usage);
                return args.Length == 0 ? 2 : 0;
            }

            if (args[0] == "drift")
            {
                return RunDrift(args, stdout, stderr);
            }

            if (args[0] != "validate")
            {
                stderr.WriteLine($"specdrift: unknown command '{args[0]}'");
                stderr.WriteLine(Usage);
                return 2;
            }

            string? manifestPath = null, schemaPath = null, rulesPath = null;
            var format = "text";
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--schema":
                        schemaPath = Next(args, ref i);
                        break;
                    case "--rules":
                        rulesPath = Next(args, ref i);
                        break;
                    case "--format":
                        format = Next(args, ref i);
                        break;
                    default:
                        if (manifestPath is not null)
                        {
                            stderr.WriteLine($"specdrift: unexpected argument '{args[i]}'");
                            return 2;
                        }

                        manifestPath = args[i];
                        break;
                }
            }

            if (manifestPath is null || schemaPath is null || format is not ("text" or "json"))
            {
                stderr.WriteLine(Usage);
                return 2;
            }

            try
            {
                var report = ManifestValidator.Validate(
                    File.ReadAllText(manifestPath),
                    File.ReadAllText(schemaPath),
                    rulesPath is null ? null : File.ReadAllText(rulesPath));
                stdout.WriteLine(format == "json" ? report.ToJson() : report.ToText());
                return report.ExitCode;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
            {
                stderr.WriteLine($"specdrift: {ex.Message}");
                return 2;
            }
        }

        private static int RunDrift(string[] args, TextWriter stdout, TextWriter stderr)
        {
            string? repo = null, profilePath = null;
            var format = "text";
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--repo":
                        repo = Next(args, ref i);
                        break;
                    case "--profile":
                        profilePath = Next(args, ref i);
                        break;
                    case "--format":
                        format = Next(args, ref i);
                        break;
                    default:
                        stderr.WriteLine($"specdrift: unexpected argument '{args[i]}'");
                        return 2;
                }
            }

            if (repo is null || format is not ("text" or "json"))
            {
                stderr.WriteLine(Usage);
                return 2;
            }

            profilePath ??= Path.Combine(repo, ".specdrift", "drift.yaml");
            try
            {
                var profile = Specdrift.Drift.DriftEngine.LoadProfile(File.ReadAllText(profilePath));
                var report = Specdrift.Drift.DriftEngine.Run(repo, profile);
                stdout.WriteLine(format == "json" ? report.ToJson() : report.ToText());
                return report.ExitCode;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
            {
                stderr.WriteLine($"specdrift: {ex.Message}");
                return 2;
            }
        }

        private static string? Next(string[] args, ref int i)
            => ++i < args.Length ? args[i] : null;
    }
}
