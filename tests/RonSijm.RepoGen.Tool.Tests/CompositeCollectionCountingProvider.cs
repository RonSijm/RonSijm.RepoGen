using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionCountingProvider : IDbContextLeaseProvider<CompositeCollectionContext>
{
    public int Acquisitions { get; private set; }
    public ValueTask<DbContextLease<CompositeCollectionContext>> AcquireAsync(CancellationToken cancellationToken = default)
    {
        Acquisitions++;
        throw new InvalidOperationException("Invalid input must fail before context acquisition.");
    }
}
