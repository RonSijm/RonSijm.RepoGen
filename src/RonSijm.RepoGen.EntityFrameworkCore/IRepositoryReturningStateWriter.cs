using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Optional infrastructure capability for detached returned scalar state, including shadow properties.</summary>
public interface IRepositoryReturningStateWriter : IRepositoryReturningWriter
{
    Task<IReadOnlyList<ReturningWriteRow<TEntity>>> ExecuteWithStateAsync<TEntity>(DbContext context, IQueryable source,
        ReturningWriteKind kind, IReadOnlyList<ReturningWriteAssignment> assignments, CancellationToken cancellationToken = default) where TEntity : class;
}
