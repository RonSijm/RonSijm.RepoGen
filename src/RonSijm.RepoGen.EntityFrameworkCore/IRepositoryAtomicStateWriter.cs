using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Optional atomic-insert capability for detached scalar values, including shadow properties.</summary>
public interface IRepositoryAtomicStateWriter : IRepositoryAtomicWriter
{
    Task<CommandResult<ReturningWriteRow<TEntity>>> InsertIfAbsentWithStateAsync<TEntity>(DbContext context, TEntity entity,
        IReadOnlyList<string> columns, IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class;
}
