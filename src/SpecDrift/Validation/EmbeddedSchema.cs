using System.Reflection;

namespace SpecDrift.Validation;

/// <summary>
/// The pinned goldpath manifest schema, embedded at build time (spec-engine RFC: the
/// schema ships WITH the engine; `--schema` stays as the override for forks/air-gaps).
/// </summary>
public static class EmbeddedSchema
{
    /// <summary>Reads the embedded v1 manifest schema.</summary>
    public static string V1()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("SpecDrift.Resources.goldpath-manifest.schema.v1.json")
            ?? throw new InvalidOperationException("embedded schema resource missing — the build is broken");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
