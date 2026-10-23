using System.Linq.Expressions;
using System.Reflection;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryModelBuilder
{
    private readonly List<RepositoryInMemoryModelFilterContextDesign> inMemoryModelFilterContexts = new();

    /// <summary>Binds an exact context scalar member in opted-in fixture model filters to the operation's typed context key.</summary>
    /// <remarks>Requires UseKeyedContextLeases with TContextKey. Does not invoke either expression, enable filters,
    /// construct a fixture DbContext, or change production expressions. Key values must be self-contained portable scalars.</remarks>
    public RepositoryModelBuilder HasInMemoryModelFilterContext<TContext, TContextKey, TValue>(
        Expression<Func<TContext, TValue>> contextValue, Expression<Func<TContextKey, TValue>> keyValue)
        where TContext : class
    {
        if (contextValue is null) throw new ArgumentNullException(nameof(contextValue));
        if (keyValue is null) throw new ArgumentNullException(nameof(keyValue));
        if (contextValue.Body is not MemberExpression member || member.Expression != contextValue.Parameters[0] ||
            member.Member is not PropertyInfo { GetMethod.IsPublic: true, GetMethod.IsStatic: false } and
                not FieldInfo { IsPublic: true, IsStatic: false })
            throw new InvalidOperationException("RGD4701: A fixture model-filter context binding must select one public instance property or field directly, without conversions, calls or captured state.");
        if (inMemoryModelFilterContexts.Any(binding => binding.Member == member.Member))
            throw new InvalidOperationException("RGD4701: Each exact context member may have only one fixture model-filter binding across this designer and its modules.");
        inMemoryModelFilterContexts.Add(new(member.Member, contextValue, keyValue));
        return this;
    }
}
