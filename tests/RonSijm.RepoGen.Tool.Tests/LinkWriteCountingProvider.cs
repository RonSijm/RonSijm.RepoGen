using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteCountingProvider : IDbContextLeaseProvider<LinkWriteContext>
{
    public int Acquisitions { get; private set; }
    public ValueTask<DbContextLease<LinkWriteContext>> AcquireAsync(CancellationToken cancellationToken = default)
    {
        Acquisitions++;
        throw new InvalidOperationException("Invalid inputs must fail before context acquisition.");
    }
}
