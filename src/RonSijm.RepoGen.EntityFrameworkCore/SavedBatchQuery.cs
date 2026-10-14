using System.Linq.Expressions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Infrastructure for complete-key batch reads and input-ordered results; not a service-side query API.</summary>
public static class SavedBatchQuery
{
    /// <summary>Combines generated complete-key comparisons into one bounded server-side predicate.</summary>
    public static IQueryable<TEntity> Restrict<TEntity>(IQueryable<TEntity> query, IReadOnlyList<TEntity> savedRows,
        Expression<Func<TEntity, TEntity, bool>> sameKey) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(savedRows);
        ArgumentNullException.ThrowIfNull(sameKey);
        var predicates = new Expression[savedRows.Count];
        for (var index = 0; index < savedRows.Count; index++)
        {
            predicates[index] = new SavedBatchParameterBinder(sameKey.Parameters[1], Expression.Constant(savedRows[index], typeof(TEntity))).Visit(sameKey.Body);
        }
        // Keep expression depth logarithmic for larger bounded batches; no Invoke/client evaluation.
        Expression Combine(int start, int count) => count switch
        {
            0 => Expression.Constant(false),
            1 => predicates[start],
            _ => Expression.OrElse(Combine(start, count / 2), Combine(start + count / 2, count - count / 2))
        };
        return query.Where(Expression.Lambda<Func<TEntity, bool>>(Combine(0, predicates.Length), sameKey.Parameters[0]));
    }

    /// <summary>Correlates projected saved rows by complete structural keys, rejecting missing or duplicated results.</summary>
    public static IReadOnlyList<TResult> InInputOrder<TEntity, TProjection, TResult>(IReadOnlyList<TEntity> savedRows,
        IEnumerable<TProjection> projectedRows, Func<TEntity, object?[]> savedKey,
        Func<TProjection, object?[]> projectedKey, Func<TProjection, TResult> value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(savedRows);
        ArgumentNullException.ThrowIfNull(projectedRows);
        ArgumentNullException.ThrowIfNull(savedKey);
        ArgumentNullException.ThrowIfNull(projectedKey);
        ArgumentNullException.ThrowIfNull(value);
        var byKey = new Dictionary<object?[], TProjection>(SavedBatchKeyComparer.Instance);
        foreach (var projection in projectedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byKey.Add(projectedKey(projection), projection);
        }
        var results = new List<TResult>(savedRows.Count);
        foreach (var row in savedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byKey.Remove(savedKey(row), out var projection))
                throw new InvalidOperationException("A saved batch row is missing or no longer visible after saving.");
            results.Add(value(projection));
        }
        if (byKey.Count != 0)
            throw new InvalidOperationException("The saved batch result contains rows outside the saved input keys.");
        cancellationToken.ThrowIfCancellationRequested();
        return results.AsReadOnly();
    }
}
