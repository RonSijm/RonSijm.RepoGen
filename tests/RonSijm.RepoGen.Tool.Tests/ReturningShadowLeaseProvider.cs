using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

internal sealed class ReturningShadowLeaseProvider(ReturningShadowContext context) : IDbContextLeaseProvider<ReturningShadowContext>
{
    public ValueTask<DbContextLease<ReturningShadowContext>> AcquireAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(DbContextLease.Borrowed(context, "shadow-cache-test"));
}
