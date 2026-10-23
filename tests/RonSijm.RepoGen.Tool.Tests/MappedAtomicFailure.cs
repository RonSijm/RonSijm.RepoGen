namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedAtomicFailure
{
    public MappedAtomicFailure(int revision) => throw new InvalidOperationException("atomic mapped result failed: " + revision);

    public int Revision { get; }
}
