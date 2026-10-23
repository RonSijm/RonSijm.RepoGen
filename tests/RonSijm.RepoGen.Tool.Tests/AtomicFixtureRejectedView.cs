namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFixtureRejectedView
{
    public AtomicFixtureRejectedView(int id, int amount)
    {
        if (amount < 0) throw new InvalidOperationException("returned value rejected");
        Id = id;
        Amount = amount;
    }
    public int Id { get; }
    public int Amount { get; }
}
