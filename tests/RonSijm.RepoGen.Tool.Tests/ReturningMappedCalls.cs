namespace RonSijm.RepoGen.Tool.Tests;

public static class ReturningMappedCalls
{
    public static int Fail(int value) => throw new InvalidOperationException("Mapped projection failed:" + value);
}
