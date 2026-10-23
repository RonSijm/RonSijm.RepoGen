namespace RonSijm.RepoGen.Tool.Tests;

public static class ModelFilterGuardCalls
{
    public static int Current => throw new InvalidOperationException("Generation must never read ambient state.");
    public static bool Accept(string title) => title != "deleted";
}
