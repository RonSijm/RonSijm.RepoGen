using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Explicit infrastructure capability for detached field-backed/indexer scalar materialization, not only shadows.</summary>
public interface IRepositoryReturningMappedStateWriter : IRepositoryReturningStateWriter
{
    Task<IReadOnlyList<ReturningWriteRow<TEntity>>> ExecuteWithMappedStateAsync<TEntity>(DbContext context, IQueryable source,
        ReturningWriteKind kind, IReadOnlyList<ReturningWriteAssignment> assignments, CancellationToken cancellationToken = default) where TEntity : class;
}
