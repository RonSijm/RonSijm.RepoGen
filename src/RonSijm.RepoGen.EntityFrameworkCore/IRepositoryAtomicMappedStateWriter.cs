using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Explicit atomic-insert capability for materializable field/indexer scalars as well as shadows.</summary>
public interface IRepositoryAtomicMappedStateWriter : IRepositoryAtomicStateWriter
{
    Task<CommandResult<ReturningWriteRow<TEntity>>> InsertIfAbsentWithMappedStateAsync<TEntity>(DbContext context, TEntity entity,
        IReadOnlyList<string> columns, IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class;
}
