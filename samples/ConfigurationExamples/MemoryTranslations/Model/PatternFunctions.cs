namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryTranslations;

public static class PatternFunctions
{
    public static int Length(string value) => throw new InvalidOperationException("This method is translated to a provider function, never evaluated on a persistence entity.");
}
