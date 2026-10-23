using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowOnlyWriter : IRepositoryReturningStateWriter
{
    public Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(DbContext context, IQueryable source, ReturningWriteKind kind,
        IReadOnlyList<ReturningWriteAssignment> assignments, CancellationToken cancellationToken = default) where TEntity : class
        => throw new InvalidOperationException("The shadow-only writer must not execute during generation.");
    public Task<IReadOnlyList<ReturningWriteRow<TEntity>>> ExecuteWithStateAsync<TEntity>(DbContext context, IQueryable source, ReturningWriteKind kind,
        IReadOnlyList<ReturningWriteAssignment> assignments, CancellationToken cancellationToken = default) where TEntity : class
        => throw new InvalidOperationException("The shadow-only writer must not execute during generation.");
}
