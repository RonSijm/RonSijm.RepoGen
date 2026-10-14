using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryResultQueryBuilder<TResult>
{
    /// <summary>Keys final rows using the operation's typed input. No runtime selector is exposed.</summary>
    public RepositoryResultQueryBuilder<TResult> Dictionary<TKey, TRequest>(
        Expression<Func<TResult, TRequest, TKey>> keySelector,
        RepositoryDuplicateKeyBehavior duplicateKeys = RepositoryDuplicateKeyBehavior.Throw) where TKey : notnull =>
        RequestDictionary(RepositoryQueryResultKind.Dictionary, keySelector, (Expression<Func<TResult, TResult>>)(row => row), duplicateKeys);

    /// <summary>Selects approved request-bound key/value columns before dictionary assembly.</summary>
    public RepositoryResultQueryBuilder<TResult> Dictionary<TKey, TValue, TRequest>(
        Expression<Func<TResult, TRequest, TKey>> keySelector, Expression<Func<TResult, TRequest, TValue>> valueSelector,
        RepositoryDuplicateKeyBehavior duplicateKeys = RepositoryDuplicateKeyBehavior.Throw) where TKey : notnull =>
        RequestDictionary(RepositoryQueryResultKind.Dictionary, keySelector, valueSelector, duplicateKeys);

    public RepositoryResultQueryBuilder<TResult> Dictionary<TKey, TValue, TRequest>(
        Expression<Func<TResult, TKey>> keySelector, Expression<Func<TResult, TRequest, TValue>> valueSelector,
        RepositoryDuplicateKeyBehavior duplicateKeys = RepositoryDuplicateKeyBehavior.Throw) where TKey : notnull =>
        RequestDictionary(RepositoryQueryResultKind.Dictionary, keySelector, valueSelector, duplicateKeys);

    public RepositoryResultQueryBuilder<TResult> Dictionary<TKey, TValue, TRequest>(
        Expression<Func<TResult, TRequest, TKey>> keySelector, Expression<Func<TResult, TValue>> valueSelector,
        RepositoryDuplicateKeyBehavior duplicateKeys = RepositoryDuplicateKeyBehavior.Throw) where TKey : notnull =>
        RequestDictionary(RepositoryQueryResultKind.Dictionary, keySelector, valueSelector, duplicateKeys);

    /// <summary>Keys duplicate-preserving final rows using the operation's typed input.</summary>
    public RepositoryResultQueryBuilder<TResult> Lookup<TKey, TRequest>(Expression<Func<TResult, TRequest, TKey>> keySelector) where TKey : notnull =>
        RequestDictionary(RepositoryQueryResultKind.Lookup, keySelector, (Expression<Func<TResult, TResult>>)(row => row));

    /// <summary>Selects approved request-bound key/value columns before duplicate-preserving lookup assembly.</summary>
    public RepositoryResultQueryBuilder<TResult> Lookup<TKey, TValue, TRequest>(
        Expression<Func<TResult, TRequest, TKey>> keySelector, Expression<Func<TResult, TRequest, TValue>> valueSelector) where TKey : notnull =>
        RequestDictionary(RepositoryQueryResultKind.Lookup, keySelector, valueSelector);

    public RepositoryResultQueryBuilder<TResult> Lookup<TKey, TValue, TRequest>(
        Expression<Func<TResult, TKey>> keySelector, Expression<Func<TResult, TRequest, TValue>> valueSelector) where TKey : notnull =>
        RequestDictionary(RepositoryQueryResultKind.Lookup, keySelector, valueSelector);

    public RepositoryResultQueryBuilder<TResult> Lookup<TKey, TValue, TRequest>(
        Expression<Func<TResult, TRequest, TKey>> keySelector, Expression<Func<TResult, TValue>> valueSelector) where TKey : notnull =>
        RequestDictionary(RepositoryQueryResultKind.Lookup, keySelector, valueSelector);

    /// <summary>Aggregates a fixed SQL-translatable mapping with the operation's typed input.</summary>
    public RepositoryResultQueryBuilder<TResult> Max<TValue, TRequest>(Expression<Func<TResult, TRequest, TValue>> selector) =>
        Scalar(RepositoryQueryResultKind.Max, selector, typeof(TValue));

    public RepositoryResultQueryBuilder<TResult> Min<TValue, TRequest>(Expression<Func<TResult, TRequest, TValue>> selector) =>
        Scalar(RepositoryQueryResultKind.Min, selector, typeof(TValue));

    public RepositoryResultQueryBuilder<TResult> Sum<TRequest>(Expression<Func<TResult, TRequest, int>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(int));
    public RepositoryResultQueryBuilder<TResult> Sum<TRequest>(Expression<Func<TResult, TRequest, long>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(long));
    public RepositoryResultQueryBuilder<TResult> Sum<TRequest>(Expression<Func<TResult, TRequest, float>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(float));
    public RepositoryResultQueryBuilder<TResult> Sum<TRequest>(Expression<Func<TResult, TRequest, double>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(double));
    public RepositoryResultQueryBuilder<TResult> Sum<TRequest>(Expression<Func<TResult, TRequest, decimal>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(decimal));
    public RepositoryResultQueryBuilder<TResult> Sum<TRequest>(Expression<Func<TResult, TRequest, int?>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(int?));
    public RepositoryResultQueryBuilder<TResult> Sum<TRequest>(Expression<Func<TResult, TRequest, long?>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(long?));
    public RepositoryResultQueryBuilder<TResult> Sum<TRequest>(Expression<Func<TResult, TRequest, float?>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(float?));
    public RepositoryResultQueryBuilder<TResult> Sum<TRequest>(Expression<Func<TResult, TRequest, double?>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(double?));
    public RepositoryResultQueryBuilder<TResult> Sum<TRequest>(Expression<Func<TResult, TRequest, decimal?>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(decimal?));

    public RepositoryResultQueryBuilder<TResult> Average<TRequest>(Expression<Func<TResult, TRequest, int>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double));
    public RepositoryResultQueryBuilder<TResult> Average<TRequest>(Expression<Func<TResult, TRequest, long>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double));
    public RepositoryResultQueryBuilder<TResult> Average<TRequest>(Expression<Func<TResult, TRequest, float>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(float));
    public RepositoryResultQueryBuilder<TResult> Average<TRequest>(Expression<Func<TResult, TRequest, double>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double));
    public RepositoryResultQueryBuilder<TResult> Average<TRequest>(Expression<Func<TResult, TRequest, decimal>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(decimal));
    public RepositoryResultQueryBuilder<TResult> Average<TRequest>(Expression<Func<TResult, TRequest, int?>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double?));
    public RepositoryResultQueryBuilder<TResult> Average<TRequest>(Expression<Func<TResult, TRequest, long?>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double?));
    public RepositoryResultQueryBuilder<TResult> Average<TRequest>(Expression<Func<TResult, TRequest, float?>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(float?));
    public RepositoryResultQueryBuilder<TResult> Average<TRequest>(Expression<Func<TResult, TRequest, double?>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double?));
    public RepositoryResultQueryBuilder<TResult> Average<TRequest>(Expression<Func<TResult, TRequest, decimal?>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(decimal?));

    private RepositoryResultQueryBuilder<TResult> RequestDictionary(RepositoryQueryResultKind kind,
        LambdaExpression keySelector, LambdaExpression valueSelector,
        RepositoryDuplicateKeyBehavior duplicateKeys = RepositoryDuplicateKeyBehavior.Throw)
    {
        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        if (valueSelector is null)
        {
            throw new ArgumentNullException(nameof(valueSelector));
        }

        if (duplicateKeys is < RepositoryDuplicateKeyBehavior.Throw or > RepositoryDuplicateKeyBehavior.KeepLast)
        {
            throw new ArgumentOutOfRangeException(nameof(duplicateKeys), "RGD2501: Unknown dictionary duplicate-key behavior.");
        }

        // Both selectors use one input, even if their lambda authors chose different local names.
        // Rebinding preserves nested parameter scopes and does not capture designer state.
        if (keySelector.Parameters.Count == 2 && valueSelector.Parameters.Count == 2)
        {
            valueSelector = RepositoryFragmentExpressions.BindRequest(valueSelector, keySelector.Parameters[1]);
        }

        Terminal(kind);
        design.Dictionary = new RepositoryDictionaryDesign(keySelector, valueSelector, duplicateKeys);
        return this;
    }
}
