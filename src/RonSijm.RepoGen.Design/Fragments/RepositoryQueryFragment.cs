using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>A reusable design-time predicate. Composition returns a new handle and retains the left fragment's name.</summary>
public sealed class RepositoryQueryFragment<TEntity>
{
    internal RepositoryQueryFragment(string name, LambdaExpression predicate)
    {
        Design = new RepositoryQueryFragmentDesign(name, predicate);
    }

    internal RepositoryQueryFragmentDesign Design { get; }

    public string Name => Design.Name;

    public RepositoryQueryFragment<TEntity> And(RepositoryQueryFragment<TEntity> fragment) =>
        new(Name, RepositoryFragmentExpressions.Combine(Design.Predicate, (fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate, conjunction: true));

    public RepositoryQueryFragment<TEntity> Or(RepositoryQueryFragment<TEntity> fragment) =>
        new(Name, RepositoryFragmentExpressions.Combine(Design.Predicate, (fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate, conjunction: false));

    public RepositoryQueryFragment<TEntity, TRequest> And<TRequest>(RepositoryQueryFragment<TEntity, TRequest> fragment) =>
        new(Name, RepositoryFragmentExpressions.Combine(Design.Predicate, (fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate, conjunction: true));

    public RepositoryQueryFragment<TEntity, TRequest> Or<TRequest>(RepositoryQueryFragment<TEntity, TRequest> fragment) =>
        new(Name, RepositoryFragmentExpressions.Combine(Design.Predicate, (fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate, conjunction: false));

    public RepositoryQueryFragment<TEntity> Not() => new(Name, RepositoryFragmentExpressions.Negate(Design.Predicate));
}
