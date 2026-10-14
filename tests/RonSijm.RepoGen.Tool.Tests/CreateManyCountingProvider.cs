using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CreateManyCountingProvider : IKeyedDbContextLeaseProvider<CreateManyContext, string>
{
    public int Acquisitions { get; private set; }
    public ValueTask<DbContextLease<CreateManyContext>> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        Acquisitions++;
        throw new InvalidOperationException("Invalid or empty input must finish before context acquisition.");
    }
}
