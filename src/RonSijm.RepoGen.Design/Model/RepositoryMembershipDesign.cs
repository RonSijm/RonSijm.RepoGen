using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>Inspectable row key and request-bound values. The generated implementation snapshots values before acquiring a context.</summary>
public sealed class RepositoryMembershipDesign
{
    private RepositoryMembershipDesign(LambdaExpression keySelector, LambdaExpression values, bool negated, RepositoryMembershipOptions options)
    {
        KeySelector = keySelector;
        Values = values;
        Negated = negated;
        Options = options;
    }

    public LambdaExpression KeySelector { get; internal set; }
    public LambdaExpression Values { get; internal set; }
    public bool Negated { get; }
    public RepositoryMembershipOptions Options { get; }

    internal static RepositoryMembershipDesign Create<TRow, TValue, TRequest>(Expression<Func<TRow, TValue>> keySelector,
        Expression<Func<TRequest, IEnumerable<TValue>?>> values, bool negated, Action<RepositoryMembershipOptionsBuilder>? configure)
    {
        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        var options = new RepositoryMembershipOptionsBuilder();
        configure?.Invoke(options);
        return new RepositoryMembershipDesign(keySelector, values, negated, options.Build());
    }
}
