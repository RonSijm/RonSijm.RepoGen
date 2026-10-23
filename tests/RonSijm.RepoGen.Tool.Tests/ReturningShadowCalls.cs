namespace RonSijm.RepoGen.Tool.Tests;

public static class ReturningShadowCalls
{
    public static int Fail(int value) => throw new InvalidOperationException("Shadow result failure: " + value);
    public static int WholeEntity(ReturningShadowRow row) => row.Id;
}
