using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicShadowOnlyWriter : IRepositoryAtomicStateWriter
{
    public Task<CommandResult<TEntity>> InsertIfAbsentAsync<TEntity>(DbContext context, TEntity entity, IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class
        => new SqliteAtomicWriter().InsertIfAbsentAsync(context, entity, columns, conflictColumns, cancellationToken);

    public Task<CommandResult<ReturningWriteRow<TEntity>>> InsertIfAbsentWithStateAsync<TEntity>(DbContext context, TEntity entity,
        IReadOnlyList<string> columns, IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class
        => new SqliteAtomicWriter().InsertIfAbsentWithStateAsync(context, entity, columns, conflictColumns, cancellationToken);
}
