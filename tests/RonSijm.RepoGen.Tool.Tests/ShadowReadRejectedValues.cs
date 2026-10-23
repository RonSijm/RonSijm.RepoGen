namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ShadowReadRejectedValues
{
    public int Value => throw new InvalidOperationException("Never evaluate captured configuration state: " + GetType().Name);
    public static int Reject(int value) => throw new InvalidOperationException("Never execute arbitrary helpers during generation: " + value);
}
