namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeFilterThrowingCard
{
    public NativeFilterThrowingCard(int id)
    {
        if (id == 4) throw new InvalidOperationException("Deliberate fixture returned-value failure.");
        Id = id;
    }
    public int Id { get; }
}
