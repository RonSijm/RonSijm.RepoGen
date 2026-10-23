namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryWriteTranslations;

public static class PatternNoteFunctions
{
    // This helper executes normally for production tracked setters and request guards.
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    // Only used inside provider-translated query/result expressions, never a tracked setter.
    public static int Length(string value) => throw new InvalidOperationException("Length requires the configured SQL function.");
}
