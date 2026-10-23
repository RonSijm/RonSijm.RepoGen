namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ContextFilterRequest(ContextFilterKey key, int id = 0, string title = "A")
{
    public ContextFilterKey Key { get { KeyReads++; return key; } }
    public int KeyReads { get; private set; }
    public int Id { get; } = id;
    public string Title { get; } = title;
    public IReadOnlyList<ContextFilterRequest> Items { get; init; } = [];
}
