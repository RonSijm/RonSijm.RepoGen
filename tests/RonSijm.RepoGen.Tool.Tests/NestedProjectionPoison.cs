namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NestedProjectionPoison
{
    private readonly string getterMessage = "Do not invoke getters during generation.";
    public NestedProjectionPoison(int id) => throw new InvalidOperationException("Do not construct data during generation.");
    public int Id => throw new InvalidOperationException(getterMessage);
    public NestedProjectionDetail Detail => throw new InvalidOperationException(getterMessage);
}
