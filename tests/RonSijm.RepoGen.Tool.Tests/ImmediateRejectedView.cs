namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ImmediateRejectedView
{
    public ImmediateRejectedView(int id, int amount)
    {
        if (id == 2) throw new InvalidOperationException("returned value rejected");
        Id = id;
        Amount = amount;
    }
    public int Id { get; }
    public int Amount { get; }
}
