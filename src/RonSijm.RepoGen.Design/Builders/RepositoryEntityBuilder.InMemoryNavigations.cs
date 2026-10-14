using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Opts one mapped reference or collection into key-based detached fixture-read loading.</summary>
    /// <remarks>Requires UseInMemoryTestQueries and selected entity-shaped or explicitly mapped public stores. Keys come from
    /// the EF relationship, including alternate/composite keys. Only relationships an operation accesses are
    /// rebuilt, from one coordinated snapshot; seeded graphs are not trusted. No inverse, visibility policy,
    /// provider filter, tracking, write or relationship reconciliation is inferred. Public data mappings load
    /// plain read-row relationship slots without constructing persistence entities. Many-to-many navigation
    /// requires an explicit ordinary CLR join entity with its own selected store; implicit/shared join
    /// tables are not inferred. Optional source links do not belong to a missing owner. Optional target
    /// links require every use to begin with Where(target => target != null); unguarded projections and
    /// Include/load profiles retain an override boundary rather than simulate SQL null propagation.
    /// Principal and join keys come from both mapped foreign keys.</remarks>
    public RepositoryEntityBuilder<TEntity> HasInMemoryNavigation<TNavigation>(Expression<Func<TEntity, TNavigation>> navigation)
        => ConfigureInMemoryNavigation(navigation, enabled: true, preserveMissingTargets: false);

    /// <summary>Optionally preserves one null element per missing optional many-to-many target in fixture reads.</summary>
    /// <remarks>Only explicit null-safe query pipelines are supported. This does not infer SQL null propagation,
    /// authorize entity/identity projections, change production queries, or support explicit Include/loading.</remarks>
    public RepositoryEntityBuilder<TEntity> HasInMemoryNavigation<TNavigation>(Expression<Func<TEntity, TNavigation>> navigation,
        bool preserveMissingTargets)
        => ConfigureInMemoryNavigation(navigation, enabled: true, preserveMissingTargets);

    /// <summary>Removes one test-read relationship without changing the store, scripts or production loading.</summary>
    public RepositoryEntityBuilder<TEntity> WithoutInMemoryNavigation<TNavigation>(Expression<Func<TEntity, TNavigation>> navigation)
        => ConfigureInMemoryNavigation(navigation, enabled: false, preserveMissingTargets: false);

    private RepositoryEntityBuilder<TEntity> ConfigureInMemoryNavigation(LambdaExpression navigation, bool enabled, bool preserveMissingTargets)
    {
        if (navigation is null) throw new ArgumentNullException(nameof(navigation));
        var body = navigation.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert } conversion) body = conversion.Operand;
        if (body is not MemberExpression member || member.Expression != navigation.Parameters[0])
            throw new InvalidOperationException("RGD3304: HasInMemoryNavigation requires one direct mapped navigation member; declare nested relationships on their own entity builders.");
        design.InMemoryNavigationsList.RemoveAll(existing => existing.Name == member.Member.Name);
        if (enabled) design.InMemoryNavigationsList.Add(new(member.Member, preserveMissingTargets));
        return this;
    }
}
