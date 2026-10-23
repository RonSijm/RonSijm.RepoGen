namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReturnFailure
{
    public MappedReturnFailure(int id, int revision)
    {
        if (id == 2)
        {
            throw new InvalidOperationException("Returned-value mapping failed on the second row.");
        }
        Id = id;
        Revision = revision;
    }

    public int Id { get; }
    public int Revision { get; }
}
