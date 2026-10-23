using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ContextFilterLeaseProvider(ContextFilterContext context) : IKeyedDbContextLeaseProvider<ContextFilterContext, ContextFilterKey>
{
    public ValueTask<DbContextLease<ContextFilterContext>> AcquireAsync(ContextFilterKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.RuntimeKey = key;
        return ValueTask.FromResult(DbContextLease.Borrowed(context));
    }
}
