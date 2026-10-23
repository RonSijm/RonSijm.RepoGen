using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryModelBuilder
{
    /// <summary>Declares fixture semantics for one scalar-input method returning independent DTO data.</summary>
    /// <remarks>The replacement constructs flat public DTO data by default. allowNestedData explicitly permits
    /// nested independent DTOs and materialized arrays/lists, never persistence entities or arbitrary graphs.
    /// Production calls are unchanged. Automatic reads use registered semantics; commands still require
    /// UseInMemoryTranslations. Neither expression nor any DTO constructor is executed during generation.</remarks>
    public RepositoryModelBuilder HasInMemoryProjectionTranslation<T, TResult>(string name,
        Expression<Func<T, TResult>> call, Expression<Func<T, TResult>> replacement, bool allowNestedData = false)
        => AddInMemoryTranslation(name, call, replacement, projection: true, allowNestedData: allowNestedData);

    public RepositoryModelBuilder HasInMemoryProjectionTranslation<T1, T2, TResult>(string name,
        Expression<Func<T1, T2, TResult>> call, Expression<Func<T1, T2, TResult>> replacement, bool allowNestedData = false)
        => AddInMemoryTranslation(name, call, replacement, projection: true, allowNestedData: allowNestedData);

    public RepositoryModelBuilder HasInMemoryProjectionTranslation<T1, T2, T3, TResult>(string name,
        Expression<Func<T1, T2, T3, TResult>> call, Expression<Func<T1, T2, T3, TResult>> replacement, bool allowNestedData = false)
        => AddInMemoryTranslation(name, call, replacement, projection: true, allowNestedData: allowNestedData);

    public RepositoryModelBuilder HasInMemoryProjectionTranslation<T1, T2, T3, T4, TResult>(string name,
        Expression<Func<T1, T2, T3, T4, TResult>> call, Expression<Func<T1, T2, T3, T4, TResult>> replacement, bool allowNestedData = false)
        => AddInMemoryTranslation(name, call, replacement, projection: true, allowNestedData: allowNestedData);
}
