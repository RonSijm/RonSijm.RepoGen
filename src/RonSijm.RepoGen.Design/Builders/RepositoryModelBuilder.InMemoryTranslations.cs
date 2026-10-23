using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryModelBuilder
{
    private readonly List<RepositoryInMemoryTranslationDesign> inMemoryTranslations = new();

    /// <summary>Registers fixture scalar-call semantics without rewriting production expressions or invoking either lambda.</summary>
    /// <remarks>Enabled automatic reads use applicable registrations; commands additionally require their own UseInMemoryTranslations opt-in.
    /// Store/copy/value adapters and provider guarantees remain unchanged.</remarks>
    public RepositoryModelBuilder HasInMemoryTranslation<T, TResult>(string name,
        Expression<Func<T, TResult>> call, Expression<Func<T, TResult>> replacement)
        => AddInMemoryTranslation(name, call, replacement);

    public RepositoryModelBuilder HasInMemoryTranslation<T1, T2, TResult>(string name,
        Expression<Func<T1, T2, TResult>> call, Expression<Func<T1, T2, TResult>> replacement)
        => AddInMemoryTranslation(name, call, replacement);

    public RepositoryModelBuilder HasInMemoryTranslation<T1, T2, T3, TResult>(string name,
        Expression<Func<T1, T2, T3, TResult>> call, Expression<Func<T1, T2, T3, TResult>> replacement)
        => AddInMemoryTranslation(name, call, replacement);

    public RepositoryModelBuilder HasInMemoryTranslation<T1, T2, T3, T4, TResult>(string name,
        Expression<Func<T1, T2, T3, T4, TResult>> call, Expression<Func<T1, T2, T3, T4, TResult>> replacement)
        => AddInMemoryTranslation(name, call, replacement);

    private RepositoryModelBuilder AddInMemoryTranslation(string name, LambdaExpression call, LambdaExpression replacement,
        bool projection = false, bool allowNestedData = false)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A translation name is required.", nameof(name));
        if (call is null) throw new ArgumentNullException(nameof(call));
        if (replacement is null) throw new ArgumentNullException(nameof(replacement));
        var trimmed = name.Trim();
        if (inMemoryTranslations.Any(translation => translation.Name == trimmed ||
                call.Body is MethodCallExpression method && translation.Call.Body is MethodCallExpression previous && method.Method == previous.Method))
            throw new InvalidOperationException("RGD4001: In-memory translation names and closed method overloads must be unique across this designer and its modules.");
        inMemoryTranslations.Add(new RepositoryInMemoryTranslationDesign(trimmed, call, replacement, projection, allowNestedData));
        return this;
    }
}
