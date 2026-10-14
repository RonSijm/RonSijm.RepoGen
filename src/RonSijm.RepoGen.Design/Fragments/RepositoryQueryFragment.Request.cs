using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>A reusable design-time predicate. Composition uses the first request parameter's name, not each fragment's local name.</summary>
public sealed class RepositoryQueryFragment<TEntity, TRequest>
{
    internal RepositoryQueryFragment(string name, LambdaExpression predicate) => Design = new RepositoryQueryFragmentDesign(name, predicate);
    internal RepositoryQueryFragmentDesign Design { get; }
    public string Name => Design.Name;

    public RepositoryQueryFragment<TEntity, TRequest> And(RepositoryQueryFragment<TEntity, TRequest> fragment) =>
        new(Name, RepositoryFragmentExpressions.Combine(Design.Predicate, (fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate, conjunction: true));

    public RepositoryQueryFragment<TEntity, TRequest> Or(RepositoryQueryFragment<TEntity, TRequest> fragment) =>
        new(Name, RepositoryFragmentExpressions.Combine(Design.Predicate, (fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate, conjunction: false));

    public RepositoryQueryFragment<TEntity, TRequest> And(RepositoryQueryFragment<TEntity> fragment) =>
        new(Name, RepositoryFragmentExpressions.Combine(Design.Predicate, (fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate, conjunction: true));

    public RepositoryQueryFragment<TEntity, TRequest> Or(RepositoryQueryFragment<TEntity> fragment) =>
        new(Name, RepositoryFragmentExpressions.Combine(Design.Predicate, (fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate, conjunction: false));

    public RepositoryQueryFragment<TEntity, TRequest> Not() => new(Name, RepositoryFragmentExpressions.Negate(Design.Predicate));

    /// <summary>Returns a copy whose request name can be shared with other query clauses, validations and context keys.</summary>
    public RepositoryQueryFragment<TEntity, TRequest> WithRequestName(string requestName) =>
        new(Name, RepositoryFragmentExpressions.RenameRequest(Design.Predicate, requestName));
}
