using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Provider capability for a unique-constraint-protected insert, inside an explicit operation transaction.</summary>
public interface IRepositoryAtomicWriter
{
    Task<CommandResult<TEntity>> InsertIfAbsentAsync<TEntity>(DbContext context, TEntity entity, IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class;
}
