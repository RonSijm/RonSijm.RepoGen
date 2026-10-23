using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>A design-time whitelist of public sort aliases and typed provider expressions.</summary>
public sealed class RepositorySortFieldsBuilder<TResult>
{
    private readonly List<RepositorySortFieldDesign> fields = new();
    private string? defaultField;
    private bool defaultDescending;
    private bool preserveOrder;

    public RepositorySortFieldsBuilder<TResult> Field<TKey>(string name, Expression<Func<TResult, TKey>> keySelector)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("RGD2601: A sort field alias cannot be blank.", nameof(name));
        }

        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        if (fields.Any(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"RGD2601: Sort field alias '{name}' is registered more than once (case-insensitive).");
        }

        fields.Add(new RepositorySortFieldDesign(name, keySelector));
        return this;
    }

    /// <summary>Uses this whitelist field/direction when the request field is null or empty.</summary>
    public RepositorySortFieldsBuilder<TResult> UseDefault(string name, bool descending = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("RGD2602: A default sort field alias cannot be blank.", nameof(name));
        }

        if (preserveOrder)
        {
            throw new InvalidOperationException("RGD2602: Default-field sorting and preserving unspecified ordering are mutually exclusive.");
        }

        defaultField = name;
        defaultDescending = descending;
        return this;
    }

    /// <summary>Omits this stage for a null/empty field. Paging/ThenBy still need a guaranteed fallback order.</summary>
    public RepositorySortFieldsBuilder<TResult> PreserveOrderWhenUnspecified(bool enabled = true)
    {
        if (enabled && defaultField is not null)
        {
            throw new InvalidOperationException("RGD2602: Default-field sorting and preserving unspecified ordering are mutually exclusive.");
        }

        preserveOrder = enabled;
        return this;
    }

    internal RepositoryFieldSortDesign Build(LambdaExpression fieldName, LambdaExpression descending)
    {
        if (fields.Count == 0)
        {
            throw new InvalidOperationException("RGD2601: Field-name sorting requires at least one whitelisted field.");
        }

        var fallback = defaultField is null ? null : fields.FirstOrDefault(field => string.Equals(field.Name, defaultField, StringComparison.OrdinalIgnoreCase));
        if (defaultField is not null && fallback is null)
        {
            throw new InvalidOperationException($"RGD2602: Default sort field '{defaultField}' is not in this stage's whitelist.");
        }

        return new RepositoryFieldSortDesign(fieldName, descending,
            fields.OrderBy(field => field.Name, StringComparer.Ordinal).Select(field => new RepositorySortFieldDesign(field.Name, field.KeySelector)).ToArray(),
            preserveOrder, fallback?.Name, defaultDescending);
    }
}
