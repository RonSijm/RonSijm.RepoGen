using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity> where TEntity : class
{
    /// <summary>Maps a bounded set of request items to existing rows. Choose Save or Stage explicitly.</summary>
    public RepositoryCommandBuilder<TEntity> PatchMany<TItem, TKey, TRequest>(Expression<Func<TRequest, IEnumerable<TItem>>> items,
        Expression<Func<TEntity, TKey>> entityKey, Expression<Func<TItem, TKey>> itemKey,
        Action<RepositoryBatchItemBuilder<TEntity, TItem>> configure, int maximumItems = 1000) where TKey : notnull
        => ConfigureBatch(items, entityKey, itemKey, configure, maximumItems, RepositoryCommandKind.Patch);

    /// <summary>Stages tracked deletions for bounded, explicitly supplied keys. Choose Save or Stage explicitly.</summary>
    public RepositoryCommandBuilder<TEntity> DeleteMany<TItem, TKey, TRequest>(Expression<Func<TRequest, IEnumerable<TItem>>> items,
        Expression<Func<TEntity, TKey>> entityKey, Expression<Func<TItem, TKey>> itemKey,
        Action<RepositoryBatchItemBuilder<TEntity, TItem>> configure, int maximumItems = 1000) where TKey : notnull
        => ConfigureBatch(items, entityKey, itemKey, configure, maximumItems, RepositoryCommandKind.TrackedDelete);

    private RepositoryCommandBuilder<TEntity> ConfigureBatch<TItem, TKey, TRequest>(Expression<Func<TRequest, IEnumerable<TItem>>> items,
        Expression<Func<TEntity, TKey>> entityKey, Expression<Func<TItem, TKey>> itemKey,
        Action<RepositoryBatchItemBuilder<TEntity, TItem>> configure, int maximumItems, RepositoryCommandKind kind) where TKey : notnull
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (entityKey is null)
        {
            throw new ArgumentNullException(nameof(entityKey));
        }

        if (itemKey is null)
        {
            throw new ArgumentNullException(nameof(itemKey));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }
        if (design.Batch is not null)
        {
            throw new InvalidOperationException("RGD3001: A named command can configure only one batch mapping.");
        }

        var batch = new RepositoryBatchCommandDesign(items, entityKey, itemKey, maximumItems);
        configure(new RepositoryBatchItemBuilder<TEntity, TItem>(batch.Mapping));
        design.Kind = kind;
        design.Batch = batch;
        return this;
    }
}
