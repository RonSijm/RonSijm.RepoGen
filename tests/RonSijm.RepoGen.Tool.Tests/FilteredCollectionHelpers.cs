namespace RonSijm.RepoGen.Tool.Tests;

public static class FilteredCollectionHelpers
{
    public static bool Keep(string text) => throw new InvalidOperationException("Generation must not execute an opaque filter helper: " + text);
}
