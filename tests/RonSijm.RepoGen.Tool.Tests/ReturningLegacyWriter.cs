using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningLegacyWriter : IRepositoryReturningWriter
{
    public Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(DbContext context, IQueryable source, ReturningWriteKind kind,
        IReadOnlyList<ReturningWriteAssignment> assignments, CancellationToken cancellationToken = default) where TEntity : class
        => throw new InvalidOperationException("The legacy writer must not be invoked by generation.");
}
